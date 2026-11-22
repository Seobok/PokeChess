using System;
using System.IO;
using System.Threading.Tasks;
using PokeChess.Client.Services;

namespace PokeChess.Editor
{
    public static class AccountPersistenceVerification
    {
        public static string Result { get; private set; }="not_started";
        public static void Start(string url) { Result="running"; Run(url); }
        sealed class OfflineApi : IAccountApi
        {
            public bool UsesCookies=>false;
            public string SessionScope { get; }
            public OfflineApi(string scope) { SessionScope=scope; }
            public Task<AccountReply> SendAsync(string method,string path,AccountPayload payload,string token) => Task.FromResult(new AccountReply { Status=0 });
        }
        static void Check(bool condition,string message) { if(!condition)throw new Exception(message); }
        static async void Run(string url)
        {
            var api=new UnityAccountApi(url,true); var store=new WindowsSessionTokenStore();
            var folder=Path.Combine(Path.GetTempPath(),"pokechess-persist-"+Guid.NewGuid().ToString("N"));
            var scope=api.SessionScope;
            try {
                var username="persist_"+Guid.NewGuid().ToString("N").Substring(0,16);
                const string password="persistence-test-password-123";
                var first=new AccountSessionService(api,store,new FileRestoreGuard(folder));
                Check(await first.RegisterAsync(username,password),"Register failed"); first.ConfirmRecoverySaved();
                Check(await first.LoginAsync(username,password),"Login failed");
                Check(first.StorageWarning==null && first.HasSavedSession,"Secure save failed");
                var second=new AccountSessionService(new UnityAccountApi(url,true),new WindowsSessionTokenStore(),new FileRestoreGuard(folder));
                Check(!second.IsSignedIn,"Trusted local token before server verification");
                Check(await second.RestoreAsync() && second.AccountId==first.AccountId,"Server-verified restart restore failed");
                var remote=new AccountSessionService(new UnityAccountApi(url,true));
                Check(await remote.LoginAsync(username,password),"Remote session failed");
                Check(await remote.LogoutAsync(true),"Remote all-session revocation failed");
                var revoked=new AccountSessionService(api,new WindowsSessionTokenStore(),new FileRestoreGuard(folder));
                Check(!await revoked.RestoreAsync() && !revoked.IsSignedIn,"Revoked token restored");
                Check(await store.ReadAsync(scope)==null,"Revoked token not removed from vault");
                var relogin=new AccountSessionService(api,store,new FileRestoreGuard(folder));
                Check(await relogin.LoginAsync(username,password),"Relogin failed");
                var offline=new AccountSessionService(new OfflineApi(scope),store,new FileRestoreGuard(folder));
                Check(!await offline.RestoreAsync() && !offline.IsSignedIn && offline.HasSavedSession,"Offline restore granted access or lost saved credential");
                Check(!await offline.LogoutAsync(false),"Offline logout unexpectedly succeeded remotely");
                var afterLogout=new AccountSessionService(api,new WindowsSessionTokenStore(),new FileRestoreGuard(folder));
                Check(!await afterLogout.RestoreAsync(),"Offline logout restored on restart");
                Result="passed: real API + Windows credential vault, recreated service restore, remote revocation, offline restore and offline logout persistence";
            } catch(Exception error) { Result="failed: "+error.GetType().Name+": "+error.Message; }
            finally { if(scope!=null) { try { await store.DeleteAsync(scope); } catch { Result="failed: test vault cleanup"; } } }
        }
    }
}
