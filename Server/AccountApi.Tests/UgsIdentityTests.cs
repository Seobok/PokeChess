using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

static class UgsIdentityTests
{
    static readonly string Project=Guid.NewGuid().ToString("D"), Environment=Guid.NewGuid().ToString("D");
    static UgsOptions Options()=>new() { ProjectId=Project,EnvironmentId=Environment,EnvironmentName="production",KeyId="test-only-key",SecretKey="test-only-secret" };
    static UgsCredentials Identity(string account,string player,long now)=>new() { AccountId=account,PlayerId=player,ProjectId=Project,EnvironmentId=Environment,Environment="production",AccessToken="not-a-live-token",ExpiresAt=now+3600 };
    sealed class Issuer : IUgsIdentityIssuer
    {
        public UgsOptions Options { get; }=UgsIdentityTests.Options();
        public string Player="ugs-a";
        public TaskCompletionSource<UgsCredentials>? Gate;
        public Task<UgsCredentials> IssueAsync(string accountId,CancellationToken ct)=>Gate?.Task??Task.FromResult(Identity(accountId,Player,DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
    }
    sealed class Handler(Func<HttpRequestMessage,int,Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        int count;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>respond(request,++count);
    }
    static string Jwt(string player,string project,string env,string name,long expires)
    {
        string Encode(string text)=>Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+','-').Replace('/','_');
        return Encode("{\"alg\":\"RS256\"}")+"."+Encode(JsonSerializer.Serialize(new { sub=player,project_id=project,aud=new[]{"envId:"+env,"envName:"+name},exp=expires,iat=expires-3600 }))+".testsignature";
    }
    public static async Task Run(string root)
    {
        void Check(bool value,string message) { if(!value)throw new Exception(message); }
        async Task Error(Func<Task> action,string code) {
            try { await action(); throw new Exception("Expected online error: "+code); }
            catch(OnlineIdentityException error) { Check(error.Code==code,"Wrong sanitized error"); }
        }
        var clock=new TestClock(); var now=clock.GetUtcNow().ToUnixTimeSeconds(); var path=Path.Combine(root,"ugs.db"); var store=new AccountStore(path,clock);
        const string password="ugs-test-password-12345";
        store.Register("ugs_alice",password); store.Register("ugs_bob",password);
        var a=store.Login("ugs_alice",password); var b=store.Login("ugs_bob",password);
        var issuer=new Issuer(); var online=new OnlineIdentityService(store,issuer);
        await Error(()=>online.SignInAsync("invalid"),"session_expired");
        var first=await online.SignInAsync(a.Token!);
        var device2=store.Login("ugs_alice",password);
        Check((await new OnlineIdentityService(new AccountStore(path,clock),issuer).SignInAsync(device2.Token!)).PlayerId==first.PlayerId,"Devices must share canonical UGS identity");
        issuer.Player="unexpected-other-player"; await Error(()=>online.SignInAsync(a.Token!),"ugs_identity_conflict");
        issuer.Player=first.PlayerId; await Error(()=>online.SignInAsync(b.Token!),"ugs_identity_conflict");
        issuer.Player="ugs-b"; await online.SignInAsync(b.Token!);
        string Nonce()=>Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var nonce=Nonce(); var ticket=store.IssueAdmission(b.Token!,Project,Environment,"room-one",nonce)!;
        Check(ticket.Length==64,"Admission ticket entropy/format");
        Check(!store.VerifyAdmission(a.Token!,Project,Environment,ticket,"room-two",nonce,"ugs-b"),"Room-bound ticket");
        Check(!store.VerifyAdmission(a.Token!,Project,Environment,ticket,"room-one",Nonce(),"ugs-b"),"Nonce-bound ticket");
        Check(!store.VerifyAdmission(a.Token!,Project,Environment,ticket,"room-one",nonce,"ugs-a"),"Player-bound ticket");
        Check(!store.VerifyAdmission(a.Token!,Project,Guid.NewGuid().ToString("D"),ticket,"room-one",nonce,"ugs-b"),"Environment-bound ticket");
        Check(store.VerifyAdmission(a.Token!,Project,Environment,ticket,"room-one",nonce,"ugs-b"),"Verified peer admission");
        Check(!store.VerifyAdmission(a.Token!,Project,Environment,ticket,"room-one",nonce,"ugs-b"),"Ticket cannot be replayed");
        nonce=Nonce(); ticket=store.IssueAdmission(b.Token!,Project,Environment,"room-one",nonce)!;
        var doubleUse=await Task.WhenAll(Enumerable.Range(0,2).Select(_=>Task.Run(()=>new AccountStore(path,clock).VerifyAdmission(a.Token!,Project,Environment,ticket,"room-one",nonce,"ugs-b"))));
        Check(doubleUse.Count(x=>x)==1,"Concurrent replay only succeeds once");
        nonce=Nonce(); ticket=store.IssueAdmission(b.Token!,Project,Environment,"room-one",nonce)!; clock.Advance(TimeSpan.FromSeconds(60));
        Check(!store.VerifyAdmission(a.Token!,Project,Environment,ticket,"room-one",nonce,"ugs-b"),"Ticket expiry boundary");
        nonce=Nonce(); ticket=store.IssueAdmission(b.Token!,Project,Environment,"room-one",nonce)!;
        Check(store.IsActiveUgsPlayer(a.Token!,Project,Environment,"ugs-b"),"Linked host/player recognized");
        Check(!store.IsActiveUgsPlayer(a.Token!,Project,Environment,"anonymous-not-linked"),"Anonymous host rejected");
        store.RevokeAll(b.Token!);
        Check(!store.VerifyAdmission(a.Token!,Project,Environment,ticket,"room-one",nonce,"ugs-b"),"Session revocation invalidates tickets");
        Check(!store.IsActiveUgsPlayer(a.Token!,Project,Environment,"ugs-b"),"Account without active session rejected");
        var race=store.Login("ugs_alice",password); issuer.Player="ugs-a"; issuer.Gate=new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending=online.SignInAsync(race.Token!); store.Revoke(race.Token!); issuer.Gate.SetResult(Identity(race.AccountId!,"ugs-a",now));
        await Error(()=>pending,"session_expired"); issuer.Gate=null;
        var budget=store.Login("ugs_alice",password); clock.Advance(TimeSpan.FromSeconds(60));
        for(int i=0;i<10;i++) await online.SignInAsync(budget.Token!);
        await Error(()=>online.SignInAsync(budget.Token!),"ugs_rate_limited");
        clock.Advance(TimeSpan.FromSeconds(60)); await online.SignInAsync(budget.Token!);
        var h=new Handler(async (request,index)=>{
            if(index==1) {
                Check(request.RequestUri!.Host=="services.api.unity.com" && request.RequestUri.AbsolutePath=="/auth/v2/token-exchange","Fixed exchange endpoint");
                Check(request.RequestUri.Query.Contains(Project) && request.RequestUri.Query.Contains(Environment),"Project/environment scoped exchange");
                Check(request.Headers.Authorization?.Scheme=="Basic","Backend-only key authentication");
                var body=await request.Content!.ReadAsStringAsync(); Check(body.Contains("player_auth.server.custom_id_auth"),"Least privilege scope");
                return new(HttpStatusCode.OK) { Content=JsonContent.Create(new { accessToken="test-admin-token" }) };
            }
            Check(request.RequestUri!.Host=="player-auth.services.api.unity.com" && request.RequestUri.AbsolutePath.EndsWith("/authentication/server/custom-id"),"Fixed custom ID endpoint");
            using var json=JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Check(json.RootElement.GetProperty("externalId").GetString()=="pc_"+a.AccountId,"Server derives external ID");
            Check(!json.RootElement.TryGetProperty("accessToken",out _),"No automatic anonymous-account migration");
            Check(request.Headers.GetValues("UnityEnvironment").Single()=="production","Server environment");
            return new(HttpStatusCode.OK) { Content=JsonContent.Create(new { userId="ugs-a",idToken=Jwt("ugs-a",Project,Environment,"production",now+3600),expiresIn=3600,sessionToken="must-never-leave-issuer" }) };
        });
        var realIssuer=new UnityCustomIdIssuer(new HttpClient(h),Options(),new FixedTime(now));
        var result=await realIssuer.IssueAsync(a.AccountId!,default);
        Check(result.PlayerId=="ugs-a" && result.ExpiresAt==now+3600,"Provider contract");
        Check(!JsonSerializer.Serialize(result).Contains("must-never-leave-issuer"),"Unity refresh credential not returned/stored");
        foreach(var kind in new[]{"wrong-project","wrong-environment","wrong-subject","expired"}) {
            var handler=new Handler((request,index)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content=index==1?
                JsonContent.Create(new { accessToken="admin" }):JsonContent.Create(new { userId="ugs-a",expiresIn=3600,idToken=Jwt(kind=="wrong-subject"?"other":"ugs-a",kind=="wrong-project"?Guid.NewGuid().ToString("D"):Project,kind=="wrong-environment"?Guid.NewGuid().ToString("D"):Environment,"production",kind=="expired"?now-1:now+3600) }) }));
            await Error(()=>new UnityCustomIdIssuer(new HttpClient(handler),Options(),new FixedTime(now)).IssueAsync(a.AccountId!,default),"ugs_invalid_response");
        }
        await Error(()=>new UnityCustomIdIssuer(new HttpClient(new Handler((_,_)=>throw new Exception("Must not call Unity"))),new UgsOptions()).IssueAsync(a.AccountId!,default),"online_not_configured");
        Console.WriteLine("PASS: canonical cross-device UGS binding, conflicts, replay/expiry/nonce/room/player/environment-bound admissions, revocation race, account sign-in budget, Unity API contract and token consistency checks.");
    }
    sealed class FixedTime(long now) : TimeProvider { public override DateTimeOffset GetUtcNow()=>DateTimeOffset.FromUnixTimeSeconds(now); }
}
