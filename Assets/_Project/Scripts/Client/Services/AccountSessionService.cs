using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PokeChess.Client.Services
{
    [Serializable] public sealed class AccountPayload { public string username,password,currentPassword,newPassword,recoveryCode,roomId,nonce,admissionTicket,playerId; }
    public sealed class AccountReply { public long Status; public string AccountId,Token,RecoveryCode; public int RetryAfterSeconds; public string PlayerId,AccessToken,ProjectId,Environment,EnvironmentId,AdmissionTicket,ErrorCode; public long ExpiresAt; public bool Authorized; }
    public interface IAccountApi
    {
        bool UsesCookies { get; }
        string SessionScope { get; }
        Task<AccountReply> SendAsync(string method,string path,AccountPayload payload,string token);
    }
    public static class AccountValidation
    {
        public static bool Username(string value) => value!=null && Regex.IsMatch(value,"\\A[a-zA-Z0-9_]{3,32}\\z");
        public static bool Password(string value) => value!=null && value.Length>=15 && value.Length<=128;
        public static bool Token(string value) => value!=null && Regex.IsMatch(value,"\\A[a-fA-F0-9]{64}\\z");
        public static bool RecoveryCode(string value) => value!=null && Regex.IsMatch(value,"\\A(?:[a-fA-F0-9]{64}|[a-fA-F0-9]{8}(?:-[a-fA-F0-9]{8}){7})\\z");
    }
    public sealed class AccountSessionService
    {
        static AccountSessionService instance;
        public static AccountSessionService Instance => instance ?? (instance=Create());
        static AccountSessionService Create() => new AccountSessionService(new UnityAccountApi(),SessionTokenStores.Create(),SessionTokenStores.CreateGuard());
        readonly IAccountApi api;
        readonly ISessionTokenStore storage;
        readonly ISessionRestoreGuard guard;
        string token,boundScope;
        public string AccountId { get; private set; }
        public bool IsSignedIn => AccountId!=null && boundScope==api.SessionScope;
        public bool IsBusy { get; private set; }
        public bool IsRestoring { get; private set; }
        public bool RestoreCompleted { get; private set; }
        public bool HasSavedSession { get; private set; }
        public string Error { get; private set; }
        public string StorageWarning { get; private set; }
        public int RetryAfterSeconds { get; private set; }
        // Recovery codes are memory-only, independent of persisted session credentials.
        public string PendingRecoveryCode { get; private set; }
        public event Action Changed;
        public AccountSessionService(IAccountApi api,ISessionTokenStore storage=null,ISessionRestoreGuard guard=null)
        {
            this.api=api??throw new ArgumentNullException(nameof(api));
            this.storage=storage??new VolatileSessionTokenStore(); this.guard=guard??new MemoryRestoreGuard();
        }
        public void ConfirmRecoverySaved() { PendingRecoveryCode=null; Changed?.Invoke(); }
        void ClearIdentity() { token=null; AccountId=null; boundScope=null; }
        bool SameScope(string scope)
        {
            if(scope==api.SessionScope && scope!=null) return true;
            ClearIdentity(); HasSavedSession=false; Error="server_changed"; return false;
        }
        bool Accepted(AccountReply reply,string unauthorized)
        {
            if(reply.Status>=200 && reply.Status<300) return true;
            RetryAfterSeconds=reply.RetryAfterSeconds;
            Error=reply.Status==401?unauthorized:reply.Status==409?"username_unavailable":reply.Status==429?"too_many_attempts":
                reply.Status==503?"server_busy":reply.Status==400?"invalid_format":"connection_failed";
            return false;
        }
        async Task<bool> Run(Func<Task<bool>> action)
        {
            if(IsBusy || PendingRecoveryCode!=null) return false;
            IsBusy=true; Error=null; RetryAfterSeconds=0; Changed?.Invoke();
            try {
                if(boundScope!=null && !SameScope(boundScope)) return false;
                return await action();
            } catch { Error="connection_failed"; return false; }
            finally { IsBusy=false; IsRestoring=false; Changed?.Invoke(); }
        }
        async Task Forget(string scope)
        {
            HasSavedSession=false; if(scope==null) return;
            bool blocked=false,deleted=false;
            try { guard.Block(scope); blocked=true; } catch { }
            if(!api.UsesCookies) { try { await storage.DeleteAsync(scope); deleted=true; } catch { } }
            // A durable block prevents restoration even if the OS vault cannot delete an item.
            if(!blocked && (!deleted || api.UsesCookies)) { HasSavedSession=true; StorageWarning="saved_login_clear_failed"; }
        }
        async Task Save(string scope,string value)
        {
            HasSavedSession=false; StorageWarning=null;
            try {
                guard.Block(scope);
                if(!api.UsesCookies) {
                    await storage.WriteAsync(scope,value);
                    if(await storage.ReadAsync(scope)!=value) throw new InvalidOperationException("Credential write verification failed.");
                }
                guard.Allow(scope); HasSavedSession=true;
            } catch {
                await Forget(scope);
                if(StorageWarning==null) StorageWarning="session_not_saved";
            }
        }
        public Task<bool> RestoreAsync() => Run(async () => {
            IsRestoring=true; Changed?.Invoke();
            try {
                if(IsSignedIn) return await ValidateCore();
                var scope=api.SessionScope;
                if(scope==null) return false;
                bool blocked;
                try { blocked=guard.IsBlocked(scope); } catch { HasSavedSession=true; StorageWarning="saved_login_unavailable"; return false; }
                if(blocked) { ClearIdentity(); HasSavedSession=false; return false; }
                if(!api.UsesCookies) {
                    string saved;
                    try { saved=await storage.ReadAsync(scope); }
                    catch { HasSavedSession=true; StorageWarning="saved_login_unavailable"; return false; }
                    if(saved==null) { HasSavedSession=false; return false; }
                    if(!AccountValidation.Token(saved)) { await Forget(scope); StorageWarning="saved_login_invalid"; return false; }
                    if(!SameScope(scope)) return false;
                    token=saved;
                }
                boundScope=scope; HasSavedSession=true;
                return await ValidateCore();
            } finally { RestoreCompleted=true; }
        });
        public Task<bool> ForgetSavedSessionAsync() => Run(async () => {
            var scope=boundScope??api.SessionScope; ClearIdentity(); StorageWarning=null; await Forget(scope);
            return StorageWarning==null;
        });
        public Task<bool> RegisterAsync(string username,string password) => Run(async () => {
            var reply=await api.SendAsync("POST","/auth/register",new AccountPayload { username=username,password=password },null);
            if(!Accepted(reply,"invalid_credentials")) return false;
            if(!AccountValidation.RecoveryCode(reply.RecoveryCode)) { Error="invalid_response"; return false; }
            PendingRecoveryCode=reply.RecoveryCode; return true;
        });
        public Task<bool> LoginAsync(string username,string password) => Run(async () => {
            var scope=api.SessionScope;
            var reply=await api.SendAsync("POST","/auth/login"+(api.UsesCookies?"":"?client=app"),new AccountPayload { username=username,password=password },null);
            if(!Accepted(reply,"invalid_credentials")) return false;
            if(!SameScope(scope)) return false;
            if(string.IsNullOrEmpty(reply.AccountId) || (!api.UsesCookies && !AccountValidation.Token(reply.Token)))
            { ClearIdentity(); Error="invalid_response"; return false; }
            token=api.UsesCookies?null:reply.Token; boundScope=scope; AccountId=reply.AccountId;
            await Save(scope,token); return SameScope(scope);
        });
        async Task<bool> ValidateCore()
        {
            if(!api.UsesCookies && token==null) return false;
            var scope=boundScope??api.SessionScope; if(!SameScope(scope)) return false;
            bool wasSignedIn=IsSignedIn, wasSavedNative=HasSavedSession && !api.UsesCookies;
            var reply=await api.SendAsync("GET","/auth/me",null,token);
            if(!SameScope(scope)) return false;
            if(reply.Status==401) { ClearIdentity(); await Forget(scope); if(wasSignedIn || wasSavedNative) Error="session_expired"; return false; }
            if(!Accepted(reply,"session_expired")) return false;
            if(string.IsNullOrEmpty(reply.AccountId) || (AccountId!=null && AccountId!=reply.AccountId))
            { ClearIdentity(); await Forget(scope); Error="session_expired"; return false; }
            boundScope=scope; AccountId=reply.AccountId; return true;
        }
        public Task<bool> ValidateAsync() => Run(ValidateCore);
        public Task<bool> RecoverAsync(string username,string recoveryCode,string newPassword) => Run(async () => {
            var scope=api.SessionScope;
            var reply=await api.SendAsync("POST","/auth/recover",new AccountPayload { username=username,recoveryCode=recoveryCode,newPassword=newPassword },null);
            if(!Accepted(reply,"invalid_recovery")) return false;
            ClearIdentity(); await Forget(scope);
            if(!AccountValidation.RecoveryCode(reply.RecoveryCode)) { Error="invalid_response"; return false; }
            PendingRecoveryCode=reply.RecoveryCode; return true;
        });
        public Task<bool> ChangePasswordAsync(string currentPassword,string newPassword) => Run(async () => {
            var scope=boundScope??api.SessionScope;
            var reply=await api.SendAsync("POST","/auth/change-password",new AccountPayload { currentPassword=currentPassword,newPassword=newPassword },token);
            if(!Accepted(reply,"invalid_credentials")) { if(reply.Status==401) await CheckSessionAfterFailure(); return false; }
            ClearIdentity(); await Forget(scope); return true;
        });
        public Task<bool> RotateRecoveryCodeAsync(string currentPassword) => Run(async () => {
            var reply=await api.SendAsync("POST","/auth/recovery-code/rotate",new AccountPayload { currentPassword=currentPassword },token);
            if(!Accepted(reply,"invalid_credentials")) { if(reply.Status==401) await CheckSessionAfterFailure(); return false; }
            if(!AccountValidation.RecoveryCode(reply.RecoveryCode)) { Error="invalid_response"; return false; }
            PendingRecoveryCode=reply.RecoveryCode; return true;
        });
        async Task CheckSessionAfterFailure()
        {
            var scope=boundScope??api.SessionScope;
            if(!SameScope(scope)) return;
            var reply=await api.SendAsync("GET","/auth/me",null,token);
            if(reply.Status==401) { ClearIdentity(); await Forget(scope); Error="session_expired"; }
        }
        // Auxiliary online requests may overlap a periodic /me check, but must never reuse a changed login.
        public async Task<AccountReply> RequestOnlineAsync(string path,AccountPayload payload=null)
        {
            if(path!="/auth/ugs/sign-in" && path!="/auth/ugs/admission" && path!="/auth/ugs/admission/verify" && path!="/auth/ugs/player") throw new ArgumentException("Invalid online route.");
            if(!IsSignedIn || PendingRecoveryCode!=null) return new AccountReply { Status=401 };
            var scope=boundScope; var current=token; var id=AccountId;
            AccountReply reply;
            try { reply=await api.SendAsync("POST",path,payload??new AccountPayload(),current); }
            catch { return new AccountReply { Status=503,ErrorCode="ugs_unavailable" }; }
            if(scope!=boundScope || scope!=api.SessionScope || current!=token || id!=AccountId) return new AccountReply { Status=401,ErrorCode="online_account_changed" };
            if(reply.Status==401) { ClearIdentity(); await Forget(scope); Error="session_expired"; Changed?.Invoke(); }
            return reply;
        }
        public Task<bool> LogoutAsync(bool allDevices) => Run(async () => {
            var scope=boundScope??api.SessionScope; var currentToken=token;
            ClearIdentity(); StorageWarning=null; await Forget(scope);
            if(!SameScope(scope)) return false;
            var reply=await api.SendAsync("POST",allDevices?"/auth/logout-all":"/auth/logout",new AccountPayload(),currentToken);
            return Accepted(reply,"session_expired");
        });
    }
}
