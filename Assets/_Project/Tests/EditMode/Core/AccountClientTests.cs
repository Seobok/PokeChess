using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using PokeChess.Client.Services;

namespace PokeChess.Core.Tests
{
    public sealed class AccountClientTests
    {
        const string Token = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
        const string Code = "01234567-89ABCDEF-01234567-89ABCDEF-01234567-89ABCDEF-01234567-89ABCDEF";
        sealed class Api : IAccountApi
        {
            public bool UsesCookies { get; set; }
            public string SessionScope { get; set; } = "test-api";
            public readonly Queue<AccountReply> Replies = new Queue<AccountReply>();
            public readonly List<(string path, string token)> Requests = new List<(string, string)>();
            public TaskCompletionSource<AccountReply> Pending;
            public Task<AccountReply> SendAsync(string method, string path, AccountPayload payload, string token)
            { Requests.Add((path,token)); return Pending != null ? Pending.Task : Task.FromResult(Replies.Dequeue()); }
            public void LoginReply() => Replies.Enqueue(new AccountReply { Status = 200, AccountId = "account-a", Token = Token });
        }
        [Test] public void TransportRequiresHttpsAndSameOriginForWeb()
        {
            Assert.That(UnityAccountApi.TryBaseUrl("http://localhost:5187", "", false, out _), Is.False);
            Assert.That(UnityAccountApi.TryBaseUrl("http://127.0.0.1:5187", "", false, out _, true), Is.True);
            Assert.That(UnityAccountApi.TryBaseUrl("http://example.com", "", false, out _, true), Is.False);
            Assert.That(UnityAccountApi.TryBaseUrl("https://user:secret@example.com", "", false, out _), Is.False);
            Assert.That(UnityAccountApi.TryBaseUrl("https://example.com?secret=value", "", false, out _), Is.False);
            Assert.That(UnityAccountApi.TryBaseUrl("https://other.example", "https://game.example/play", true, out _), Is.False);
            Assert.That(UnityAccountApi.TryBaseUrl("", "https://game.example/play", true, out var origin), Is.True);
            Assert.That(origin.AbsoluteUri, Is.EqualTo("https://game.example/"));
            Assert.That(UnityAccountApi.TryBaseUrl("https://game.example/account-api", "https://game.example/play", true, out var root), Is.True);
            Assert.That(new Uri(root,"auth/me").AbsoluteUri, Is.EqualTo("https://game.example/account-api/auth/me"));
        }
        [Test] public async Task NativeUsesAppLoginAndBearerForSubsequentRequests()
        {
            var api = new Api(); api.LoginReply(); api.Replies.Enqueue(new AccountReply { Status=200, AccountId="account-a" });
            var service = new AccountSessionService(api);
            Assert.That(await service.LoginAsync("alice","password"), Is.True);
            Assert.That(await service.ValidateAsync(), Is.True);
            Assert.That(api.Requests[0].path, Is.EqualTo("/auth/login?client=app"));
            Assert.That(api.Requests[1].token, Is.EqualTo(Token));
        }
        [Test] public async Task BrowserUsesCookieLoginWithoutBearer()
        {
            var api = new Api { UsesCookies = true }; api.Replies.Enqueue(new AccountReply { Status=200, AccountId="account-a" });
            api.Replies.Enqueue(new AccountReply { Status=200, AccountId="account-a" });
            var service = new AccountSessionService(api);
            Assert.That(await service.LoginAsync("alice","password"), Is.True);
            Assert.That(await service.ValidateAsync(), Is.True);
            Assert.That(api.Requests[0].path, Is.EqualTo("/auth/login")); Assert.That(api.Requests[1].token, Is.Null);
        }
        [Test] public async Task RecoveryCodeMustBeAcknowledgedBeforeAnotherOperation()
        {
            var api = new Api(); api.Replies.Enqueue(new AccountReply { Status=201, RecoveryCode=Code });
            var service = new AccountSessionService(api);
            Assert.That(await service.RegisterAsync("alice","password"), Is.True);
            Assert.That(service.IsSignedIn, Is.False); Assert.That(service.PendingRecoveryCode, Is.EqualTo(Code));
            Assert.That(await service.LoginAsync("alice","password"), Is.False); Assert.That(api.Requests.Count, Is.EqualTo(1));
            service.ConfirmRecoverySaved(); Assert.That(service.PendingRecoveryCode, Is.Null);
        }
        [Test] public async Task SessionRevocationAndSuccessfulRecoveryClearLogin()
        {
            var api = new Api(); api.LoginReply(); api.Replies.Enqueue(new AccountReply { Status=401 });
            var service = new AccountSessionService(api); await service.LoginAsync("alice","password");
            Assert.That(await service.ValidateAsync(), Is.False); Assert.That(service.IsSignedIn, Is.False);
            Assert.That(service.Error, Is.EqualTo("session_expired"));
            api.LoginReply(); await service.LoginAsync("alice","password");
            api.Replies.Enqueue(new AccountReply { Status=200, RecoveryCode=Code });
            Assert.That(await service.RecoverAsync("alice",Code,"new password"), Is.True);
            Assert.That(service.IsSignedIn, Is.False); Assert.That(service.PendingRecoveryCode, Is.EqualTo(Code));
        }
        [Test] public async Task FailedCurrentPasswordPreservesValidSessionAndExpiredSessionIsDetected()
        {
            var api = new Api(); api.LoginReply(); var service = new AccountSessionService(api); await service.LoginAsync("alice","password");
            api.Replies.Enqueue(new AccountReply { Status=401 }); api.Replies.Enqueue(new AccountReply { Status=200, AccountId="account-a" });
            Assert.That(await service.ChangePasswordAsync("wrong","new"), Is.False); Assert.That(service.IsSignedIn, Is.True);
            api.Replies.Enqueue(new AccountReply { Status=401 }); api.Replies.Enqueue(new AccountReply { Status=401 });
            Assert.That(await service.RotateRecoveryCodeAsync("wrong"), Is.False); Assert.That(service.IsSignedIn, Is.False);
        }
        [Test] public async Task LogoutClearsLocalSessionEvenWhenServerIsUnavailable()
        {
            var api = new Api(); api.LoginReply(); var service = new AccountSessionService(api); await service.LoginAsync("alice","password");
            api.Replies.Enqueue(new AccountReply { Status=0 });
            Assert.That(await service.LogoutAsync(true), Is.False); Assert.That(service.IsSignedIn, Is.False);
            Assert.That(api.Requests[1].path, Is.EqualTo("/auth/logout-all"));
        }
        [Test] public async Task DuplicateSubmissionAndRetryAfterAreHandled()
        {
            var api = new Api { Pending = new TaskCompletionSource<AccountReply>() }; var service = new AccountSessionService(api);
            var first = service.LoginAsync("alice","password");
            Assert.That(service.IsBusy, Is.True); Assert.That(await service.LoginAsync("alice","password"), Is.False);
            Assert.That(api.Requests.Count, Is.EqualTo(1));
            api.Pending.SetResult(new AccountReply { Status=429, RetryAfterSeconds=42 });
            Assert.That(await first, Is.False); Assert.That(service.IsBusy, Is.False);
            Assert.That(service.RetryAfterSeconds, Is.EqualTo(42)); Assert.That(service.Error, Is.EqualTo("too_many_attempts"));
        }
        [Test] public async Task FreshBrowserWithoutCookieDoesNotReportExpiredSession()
        {
            var api = new Api { UsesCookies = true }; api.Replies.Enqueue(new AccountReply { Status=401 });
            var service = new AccountSessionService(api);
            Assert.That(await service.ValidateAsync(), Is.False); Assert.That(service.Error, Is.Null);
        }
        [Test] public async Task UnexpectedSessionIdentityClearsTheSession()
        {
            var api = new Api(); api.LoginReply(); var service = new AccountSessionService(api); await service.LoginAsync("alice","password");
            api.Replies.Enqueue(new AccountReply { Status=200, AccountId="account-b" });
            Assert.That(await service.ValidateAsync(), Is.False); Assert.That(service.IsSignedIn, Is.False);
        }
        [Test] public async Task PasswordChangeSuccessRequiresLoginAgain()
        {
            var api = new Api(); api.LoginReply(); var service = new AccountSessionService(api); await service.LoginAsync("alice","password");
            api.Replies.Enqueue(new AccountReply { Status=204 });
            Assert.That(await service.ChangePasswordAsync("old","new"), Is.True); Assert.That(service.IsSignedIn, Is.False);
        }        [Test] public void PasswordAndRecoveryValidationDoesNotTrimOrTruncate()
        {
            Assert.That(AccountValidation.Password(new string('x',14)), Is.False);
            Assert.That(AccountValidation.Password(new string('x',15)), Is.True);
            Assert.That(AccountValidation.Password(new string('x',129)), Is.False);
            Assert.That(AccountValidation.RecoveryCode(Code), Is.True);
            Assert.That(AccountValidation.RecoveryCode(Code + " "), Is.False);
        }
    }
}
