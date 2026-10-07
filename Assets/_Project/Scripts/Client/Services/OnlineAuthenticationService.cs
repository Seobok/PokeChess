using System;
using System.Threading.Tasks;
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
        Task InitializeAsync(string environment, string profile);
        Task SignInAsync();
    }

    public sealed class UnityAuthenticationBackend : IAuthenticationBackend
    {
        private bool subscribed;
        public bool IsAuthorized => UnityServices.State == ServicesInitializationState.Initialized && AuthenticationService.Instance.IsAuthorized;
        public string PlayerId => IsAuthorized ? AuthenticationService.Instance.PlayerId : null;
        public event Action Expired;
        public event Action SignedOut;
        public async Task InitializeAsync(string environment, string profile)
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
                await UnityServices.InitializeAsync(new InitializationOptions().SetEnvironmentName(environment).SetProfile(profile));
            if (!subscribed)
            {
                AuthenticationService.Instance.Expired += () => Expired?.Invoke();
                AuthenticationService.Instance.SignedOut += () => SignedOut?.Invoke();
                subscribed = true;
            }
        }
        public Task SignInAsync() => AuthenticationService.Instance.SignInAnonymouslyAsync();
    }

    public sealed class OnlineAuthenticationService
    {
        public static OnlineAuthenticationService Instance { get; } = new OnlineAuthenticationService(new UnityAuthenticationBackend());
        private readonly IAuthenticationBackend backend;
        private Task pending;
        public AuthenticationState State { get; private set; }
        public string PlayerId { get; private set; }
        public string Error { get; private set; }
        public bool IsReady => State == AuthenticationState.Ready && backend.IsAuthorized && !string.IsNullOrEmpty(PlayerId);
        public event Action Changed;
        public OnlineAuthenticationService(IAuthenticationBackend backend)
        {
            this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
            backend.Expired += () => Invalidate(AuthenticationState.Expired);
            backend.SignedOut += () => Invalidate(AuthenticationState.SignedOut);
        }
        public Task EnsureSignedInAsync(string environment = "production", string profile = "default")
        {
            if (pending != null && !pending.IsCompleted) return pending;
            if (IsReady) return Task.CompletedTask;
            pending = ConnectAsync(environment, profile);
            return pending;
        }
        private async Task ConnectAsync(string environment, string profile)
        {
            Error = null; PlayerId = null;
            try
            {
                SetState(AuthenticationState.Initializing);
                await backend.InitializeAsync(environment, profile);
                SetState(AuthenticationState.SigningIn);
                if (!backend.IsAuthorized) await backend.SignInAsync();
                if (!backend.IsAuthorized || string.IsNullOrEmpty(backend.PlayerId)) throw new InvalidOperationException("Authentication returned no authorized player.");
                PlayerId = backend.PlayerId;
                SetState(AuthenticationState.Ready);
            }
            catch (Exception error)
            {
                // Do not include request payloads or tokens in UI/logs.
                Error = error is RequestFailedException request ? "Service error " + request.ErrorCode : error.GetType().Name;
                SetState(AuthenticationState.Failed);
            }
        }
        private void Invalidate(AuthenticationState state) { PlayerId = null; Error = null; SetState(state); }
        private void SetState(AuthenticationState state) { State = state; Changed?.Invoke(); }
    }
}
