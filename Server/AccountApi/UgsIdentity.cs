using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

public sealed class UgsOptions
{
    public string ProjectId { get; set; }="";
    public string EnvironmentId { get; set; }="";
    public string EnvironmentName { get; set; }="production";
    public string KeyId { get; set; }="";
    public string SecretKey { get; set; }="";
    public bool IsConfigured => Guid.TryParse(ProjectId,out _) && Guid.TryParse(EnvironmentId,out _) &&
        Regex.IsMatch(EnvironmentName,"\\A[a-zA-Z0-9_-]{1,64}\\z") && !string.IsNullOrWhiteSpace(KeyId) && !string.IsNullOrWhiteSpace(SecretKey);
}
// No token-bearing record gets a generated ToString implementation.
public sealed class UgsCredentials
{
    public string AccountId { get; init; }="";
    public string PlayerId { get; init; }="";
    public string ProjectId { get; init; }="";
    public string EnvironmentId { get; init; }="";
    public string Environment { get; init; }="";
    public string AccessToken { get; init; }="";
    public long ExpiresAt { get; init; }
}
public sealed class OnlineIdentityException(string code) : Exception(code) { public string Code { get; }=code; }
public interface IUgsIdentityIssuer
{
    UgsOptions Options { get; }
    Task<UgsCredentials> IssueAsync(string accountId,CancellationToken cancellationToken);
}
public sealed class UnityCustomIdIssuer(HttpClient http,UgsOptions options,TimeProvider? time=null) : IUgsIdentityIssuer
{
    public UgsOptions Options => options;
    readonly TimeProvider clock=time??TimeProvider.System;
    readonly SemaphoreSlim slots=new(8,8);
    sealed class Exchange { public string? AccessToken { get; set; } }
    sealed class Player
    {
        public string? UserId { get; set; }
        public string? IdToken { get; set; }
        public int ExpiresIn { get; set; }
    }
    static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    static async Task<T> Read<T>(HttpResponseMessage response,CancellationToken ct)
    {
        if(!response.IsSuccessStatusCode) throw new OnlineIdentityException("ugs_unavailable");
        await using var stream=await response.Content.ReadAsStreamAsync(ct);
        using var bounded=new MemoryStream(); var buffer=new byte[4096]; int size;
        while((size=await stream.ReadAsync(buffer,ct))>0) {
            if(bounded.Length+size>65536) throw new OnlineIdentityException("ugs_invalid_response");
            await bounded.WriteAsync(buffer.AsMemory(0,size),ct);
        }
        try { return JsonSerializer.Deserialize<T>(bounded.ToArray(),Json)??throw new OnlineIdentityException("ugs_invalid_response"); }
        catch(JsonException) { throw new OnlineIdentityException("ugs_invalid_response"); }
    }
    public async Task<UgsCredentials> IssueAsync(string accountId,CancellationToken cancellationToken)
    {
        if(!options.IsConfigured) throw new OnlineIdentityException("online_not_configured");
        if(!await slots.WaitAsync(0,cancellationToken)) throw new OnlineIdentityException("ugs_busy");
        try {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(15));
            string project=Guid.Parse(options.ProjectId).ToString("D"), environment=Guid.Parse(options.EnvironmentId).ToString("D");
            // Fixed HTTPS Unity endpoints only. Never accept an endpoint, playerId or externalId from a client.
            using var exchange=new HttpRequestMessage(HttpMethod.Post,"https://services.api.unity.com/auth/v2/token-exchange?projectId="+project+"&environmentId="+environment);
            exchange.Headers.Authorization=new AuthenticationHeaderValue("Basic",Convert.ToBase64String(Encoding.UTF8.GetBytes(options.KeyId+":"+options.SecretKey)));
            exchange.Content=JsonContent.Create(new { scopes=new[]{"player_auth.server.custom_id_auth"} });
            using var exchangeResponse=await http.SendAsync(exchange,HttpCompletionOption.ResponseHeadersRead,timeout.Token);
            var admin=await Read<Exchange>(exchangeResponse,timeout.Token);
            if(string.IsNullOrEmpty(admin.AccessToken) || admin.AccessToken.Length>16384) throw new OnlineIdentityException("ugs_invalid_response");
            using var signIn=new HttpRequestMessage(HttpMethod.Post,"https://player-auth.services.api.unity.com/v1/projects/"+project+"/authentication/server/custom-id");
            signIn.Headers.Authorization=new AuthenticationHeaderValue("Bearer",admin.AccessToken);
            signIn.Headers.Add("UnityEnvironment",options.EnvironmentName);
            signIn.Content=JsonContent.Create(new { externalId="pc_"+accountId, signInOnly=false });
            using var response=await http.SendAsync(signIn,HttpCompletionOption.ResponseHeadersRead,timeout.Token);
            var player=await Read<Player>(response,timeout.Token);
            if(player.UserId==null || !Regex.IsMatch(player.UserId,"\\A[a-zA-Z0-9_-]{1,128}\\z") || player.IdToken==null || player.IdToken.Length>16384)
                throw new OnlineIdentityException("ugs_invalid_response");
            long expires=CheckTrustedUnityToken(player.IdToken,player.UserId,project,environment,options.EnvironmentName);
            return new UgsCredentials { AccountId=accountId,PlayerId=player.UserId,ProjectId=project,EnvironmentId=environment,
                Environment=options.EnvironmentName,AccessToken=player.IdToken,ExpiresAt=expires };
        } catch(OnlineIdentityException) { throw; }
        catch(Exception error) when(error is HttpRequestException or OperationCanceledException or JsonException or FormatException) { throw new OnlineIdentityException("ugs_unavailable"); }
        finally { slots.Release(); }
    }
    long CheckTrustedUnityToken(string token,string player,string project,string environment,string environmentName)
    {
        // Consistency checks on a token received directly from Unity over HTTPS, NOT verification of client JWTs.
        try {
            var parts=token.Split('.'); if(parts.Length!=3) throw new FormatException();
            var encoded=parts[1].Replace('-','+').Replace('_','/'); encoded=encoded.PadRight((encoded.Length+3)/4*4,'=');
            using var json=JsonDocument.Parse(Convert.FromBase64String(encoded)); var root=json.RootElement;
            var audiences=root.GetProperty("aud").EnumerateArray().Select(x=>x.GetString()).ToArray();
            long expires=root.GetProperty("exp").GetInt64(), now=clock.GetUtcNow().ToUnixTimeSeconds();
            if(root.GetProperty("sub").GetString()!=player || root.GetProperty("project_id").GetString()!=project ||
                !audiences.Contains("envId:"+environment) || !audiences.Contains("envName:"+environmentName) || expires<=now+120 || expires>now+86400)
                throw new FormatException();
            return expires;
        } catch(Exception error) when(error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException) { throw new OnlineIdentityException("ugs_invalid_response"); }
    }
}
public sealed class OnlineIdentityService(AccountStore store,IUgsIdentityIssuer issuer)
{
    public async Task<UgsCredentials> SignInAsync(string session,CancellationToken cancellationToken=default)
    {
        var account=store.Resolve(session)??throw new OnlineIdentityException("session_expired");
        if(!issuer.Options.IsConfigured) throw new OnlineIdentityException("online_not_configured");
        var permit=store.ReserveUgsSignIn(session);
        if(permit!=AuthStatus.Success) throw new OnlineIdentityException(permit==AuthStatus.Unauthorized?"session_expired":"ugs_rate_limited");
        var credentials=await issuer.IssueAsync(account,cancellationToken);
        if(credentials.AccountId!=account) throw new OnlineIdentityException("ugs_invalid_response");
        // Revalidate the session after the external call: logout/password reset must win over an in-flight sign-in.
        var status=store.BindUgs(session,credentials);
        if(status!=UgsBindingStatus.Success) throw new OnlineIdentityException(status==UgsBindingStatus.Unauthorized?"session_expired":"ugs_identity_conflict");
        return credentials;
    }
    public UgsOptions Options=>issuer.Options;
}
