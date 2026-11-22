using System;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Core.Environments;
using Unity.Services.Authentication;

namespace PokeChess.Client.Services
{
    public enum AuthenticationState { NotStarted, Initializing, SigningIn, Ready, Failed, Expired, SignedOut }
    public interface IAuthenticationBackend
    {
        bool IsAuthorized { get; }
        string PlayerId { get; }
        event Action Expired;
        event Action SignedOut;
        Task InitializeAsync(string environment,string profile);
        Task SignInAsync();
    }
    public interface IAccountBoundAuthenticationBackend
    {
        string BoundAccountId { get; }
        long ExpiresAt { get; }
        void SignOut();
    }
    public sealed class OnlineSignInException : Exception
    {
        public string Code { get; }
        public OnlineSignInException(string code) : base(code) { Code=code; }
    }
    public sealed class UnityAuthenticationBackend : IAuthenticationBackend,IAccountBoundAuthenticationBackend
    {
        const string Profile="account-api-v1";
        bool subscribed;
        string environment;
        public string BoundAccountId { get; private set; }
        public long ExpiresAt { get; private set; }
        public bool IsAuthorized=>UnityServices.State==ServicesInitializationState.Initialized && AuthenticationService.Instance.IsAuthorized;
        public string PlayerId=>IsAuthorized?AuthenticationService.Instance.PlayerId:null;
        public event Action Expired;
        public event Action SignedOut;
        public async Task InitializeAsync(string environment,string profile)
        {
            this.environment=environment;
            if(UnityServices.State!=ServicesInitializationState.Initialized)
                await UnityServices.InitializeAsync(new InitializationOptions().SetEnvironmentName(environment).SetProfile(Profile));
            var auth=AuthenticationService.Instance;
            if(auth.Profile!=Profile) {
                if(auth.IsSignedIn) auth.SignOut(); // Preserve the old anonymous profile; never import it implicitly.
                auth.SwitchProfile(Profile);
            }
            if(!subscribed) { auth.Expired+=()=>Expired?.Invoke(); auth.SignedOut+=()=>SignedOut?.Invoke(); subscribed=true; }
        }
        public async Task SignInAsync()
        {
            var own=AccountSessionService.Instance; var account=own.AccountId;
            var reply=await own.RequestOnlineAsync("/auth/ugs/sign-in");
            if(reply.Status!=200) throw new OnlineSignInException(reply.ErrorCode??(reply.Status==401?"session_expired":"ugs_unavailable"));
            if(!own.IsSignedIn || own.AccountId!=account || reply.AccountId!=account || string.IsNullOrEmpty(reply.PlayerId) || string.IsNullOrEmpty(reply.AccessToken) ||
                !string.Equals(reply.ProjectId,Application.cloudProjectId,StringComparison.OrdinalIgnoreCase) || reply.Environment!=environment || reply.ExpiresAt<=DateTimeOffset.UtcNow.ToUnixTimeSeconds()+30)
                throw new OnlineSignInException("ugs_invalid_response");
            var auth=AuthenticationService.Instance;
            if(auth.IsSignedIn) auth.SignOut(true);
            // Access-token only: no Unity refresh credential is written into the SDK's PlayerPrefs cache.
            auth.ProcessAuthenticationTokens(reply.AccessToken,null);
            if(auth.PlayerId!=reply.PlayerId || !auth.IsAuthorized) { auth.SignOut(true); throw new OnlineSignInException("ugs_invalid_response"); }
            BoundAccountId=account; ExpiresAt=reply.ExpiresAt;
        }
        public void SignOut()
        {
            BoundAccountId=null; ExpiresAt=0;
            if(UnityServices.State==ServicesInitializationState.Initialized && AuthenticationService.Instance.Profile==Profile)
                AuthenticationService.Instance.SignOut(true);
        }
    }
    // Temporary Boot testing profile: independent of the account API and its SDK profile.
    public sealed class AnonymousTestAuthenticationBackend : IAuthenticationBackend
    {
        const string Profile="boot-playtest-v1";
        bool subscribed;
        public bool IsAuthorized=>UnityServices.State==ServicesInitializationState.Initialized && AuthenticationService.Instance.Profile==Profile && AuthenticationService.Instance.IsAuthorized;
        public string PlayerId=>IsAuthorized?AuthenticationService.Instance.PlayerId:null;
        public event Action Expired;
        public event Action SignedOut;
        public async Task InitializeAsync(string environment,string profile) {
            if(UnityServices.State!=ServicesInitializationState.Initialized)
                await UnityServices.InitializeAsync(new InitializationOptions().SetEnvironmentName(environment).SetProfile(Profile));
            var auth=AuthenticationService.Instance;
            if(auth.Profile!=Profile) { if(auth.IsSignedIn) auth.SignOut(); auth.SwitchProfile(Profile); }
            if(!subscribed) { auth.Expired+=()=>Expired?.Invoke(); auth.SignedOut+=()=>SignedOut?.Invoke(); subscribed=true; }
        }
        public async Task SignInAsync() { if(!IsAuthorized) await AuthenticationService.Instance.SignInAnonymouslyAsync(); }
    }
    public sealed class OnlineAuthenticationService
    {
        public static OnlineAuthenticationService Instance { get; private set; }=new OnlineAuthenticationService(new UnityAuthenticationBackend(),()=>AccountSessionService.Instance.IsSignedIn,()=>AccountSessionService.Instance.AccountId);
        public static void ConfigureBootMode(bool useAccountLogin) {
            Instance=useAccountLogin
                ?new OnlineAuthenticationService(new UnityAuthenticationBackend(),()=>AccountSessionService.Instance.IsSignedIn,()=>AccountSessionService.Instance.AccountId)
                :new OnlineAuthenticationService(new AnonymousTestAuthenticationBackend());
        }
        readonly IAuthenticationBackend backend;
        readonly Func<bool> accountAllowed;
        readonly Func<string> accountKey;
        Task pending;
        string pendingAccount;
        int pendingGeneration;
        int generation;
        double nextRefresh;
        public AuthenticationState State { get; private set; }
        public string PlayerId { get; private set; }
        public string Error { get; private set; }
        public bool IsReady=>accountAllowed() && State==AuthenticationState.Ready && backend.IsAuthorized && PlayerId!=null && PlayerId==backend.PlayerId &&
            (accountKey==null || (backend is IAccountBoundAuthenticationBackend bound && bound.BoundAccountId==accountKey()));
        public event Action Changed;
        public OnlineAuthenticationService(IAuthenticationBackend backend,Func<bool> accountAllowed=null,Func<string> accountKey=null)
        {
            this.backend=backend??throw new ArgumentNullException(nameof(backend)); this.accountAllowed=accountAllowed??(()=>true); this.accountKey=accountKey;
            backend.Expired+=()=>Invalidate(AuthenticationState.Expired); backend.SignedOut+=()=>Invalidate(AuthenticationState.SignedOut);
        }
        public Task EnsureSignedInAsync(string environment=null,string profile="default",bool forceRenew=false)
        {
            if(!accountAllowed()) { Invalidate(AuthenticationState.SignedOut); return Task.CompletedTask; }
            if(pending!=null && !pending.IsCompleted) return pendingAccount==accountKey?.Invoke() && pendingGeneration==generation?pending:AfterPending(environment,profile);
            if(IsReady && !forceRenew) return Task.CompletedTask;
            if(environment==null) { var settings=Resources.Load<AccountApiSettings>("AccountApiSettings"); environment=settings==null?"production":settings.UgsEnvironment; }
            pendingAccount=accountKey?.Invoke(); pendingGeneration=generation; pending=ConnectAsync(environment,profile,pendingAccount,pendingGeneration); return pending;
        }
        async Task AfterPending(string environment,string profile) { await pending; if(accountAllowed() && !IsReady) await EnsureSignedInAsync(environment,profile); }
        async Task ConnectAsync(string environment,string profile,string account,int version)
        {
            Error=null; PlayerId=null;
            try {
                SetState(AuthenticationState.Initializing); await backend.InitializeAsync(environment,profile);
                if(version!=generation || !accountAllowed() || account!=accountKey?.Invoke()) return;
                SetState(AuthenticationState.SigningIn); await backend.SignInAsync();
                if(version!=generation || !accountAllowed() || account!=accountKey?.Invoke()) return;
                if(!backend.IsAuthorized || string.IsNullOrEmpty(backend.PlayerId) || (accountKey!=null && (!(backend is IAccountBoundAuthenticationBackend bound) || bound.BoundAccountId!=account))) throw new OnlineSignInException("ugs_invalid_response");
                PlayerId=backend.PlayerId; SetState(AuthenticationState.Ready);
            } catch(Exception error) {
                if(version!=generation) return;
                Error=error is OnlineSignInException known?known.Code:error is RequestFailedException request?"Service error "+request.ErrorCode:error.GetType().Name;
                SetState(AuthenticationState.Failed);
            }
        }
        public void InvalidateForAccountChange() { generation++; Invalidate(AuthenticationState.SignedOut); }
        public void SignOut() { generation++; if(backend is IAccountBoundAuthenticationBackend bound) bound.SignOut(); Invalidate(AuthenticationState.SignedOut); }
        public void Tick()
        {
            if(backend is AnonymousTestAuthenticationBackend) {
                if(!IsReady && (pending==null || pending.IsCompleted) && Time.realtimeSinceStartupAsDouble>=nextRefresh) {
                    nextRefresh=Time.realtimeSinceStartupAsDouble+30; _=EnsureSignedInAsync();
                }
                return;
            }
            if(!accountAllowed() || (pending!=null&&!pending.IsCompleted) || !(backend is IAccountBoundAuthenticationBackend bound) || bound.ExpiresAt==0) return;
            if(bound.ExpiresAt<=DateTimeOffset.UtcNow.ToUnixTimeSeconds()+120 && Time.realtimeSinceStartupAsDouble>=nextRefresh) {
                nextRefresh=Time.realtimeSinceStartupAsDouble+30; _=EnsureSignedInAsync(forceRenew:true);
            }
        }
        void Invalidate(AuthenticationState state) { PlayerId=null; Error=null; SetState(state); }
        void SetState(AuthenticationState state) { State=state; Changed?.Invoke(); }
    }
}
