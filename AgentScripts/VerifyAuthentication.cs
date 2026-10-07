using System;
using System.IO;
using System.Threading.Tasks;
using PokeChess.Client.Services;
public static class VerifyAuthentication
{
    private sealed class Backend : IAuthenticationBackend
    {
        public bool IsAuthorized { get; set; }
        public string PlayerId { get; set; } = "same-player";
        public event Action Expired;
        public event Action SignedOut;
        public int Initializations,Logins;
        public bool Fail;
        public TaskCompletionSource<bool> Gate;
        public Task InitializeAsync(string environment,string profile) { Initializations++; return Task.CompletedTask; }
        public async Task SignInAsync() { Logins++; if(Gate!=null)await Gate.Task; if(Fail)throw new InvalidOperationException(); IsAuthorized=true; }
        public void Expire() { IsAuthorized=false; Expired?.Invoke(); }
        public void SignOut() { IsAuthorized=false; SignedOut?.Invoke(); }
    }
    public static string Main() { Run(); return "Authentication checks started; see TestResults/WBS5.1/service-checks.json"; }
    private static void Check(bool condition,string label) { if(!condition)throw new Exception(label); }
    private static async void Run()
    {
        Directory.CreateDirectory("TestResults/WBS5.1");
        try {
            var b=new Backend{Gate=new TaskCompletionSource<bool>()};var s=new OnlineAuthenticationService(b);
            var first=s.EnsureSignedInAsync();var second=s.EnsureSignedInAsync();Check(ReferenceEquals(first,second)&&b.Logins==1,"concurrent login");
            b.Gate.SetResult(true);await first;Check(s.IsReady&&s.PlayerId=="same-player","first login");
            await s.EnsureSignedInAsync();Check(b.Logins==1,"already authorized");
            b.Expire();Check(!s.IsReady&&s.PlayerId==null&&s.State==AuthenticationState.Expired,"expiry invalidation");
            b.Gate=null;await s.EnsureSignedInAsync();Check(s.IsReady&&s.PlayerId=="same-player","cached recovery");
            b.SignOut();Check(!s.IsReady&&s.State==AuthenticationState.SignedOut,"signout");
            b.Fail=true;await s.EnsureSignedInAsync();Check(s.State==AuthenticationState.Failed&&!s.IsReady,"failure");
            b.Fail=false;await s.EnsureSignedInAsync();Check(s.IsReady,"retry");
            b.SignOut();b.PlayerId="";await s.EnsureSignedInAsync();Check(s.State==AuthenticationState.Failed,"empty identity rejection");
            File.WriteAllText("TestResults/WBS5.1/service-checks.json","{\"passed\":9,\"failed\":0}");
        } catch(Exception e) { File.WriteAllText("TestResults/WBS5.1/service-checks.json",e.ToString()); }
    }
}
