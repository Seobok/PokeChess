using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using PokeChess.Client.Services;

namespace PokeChess.Core.Tests
{
    public sealed class AccountOnlineTests
    {
        sealed class Backend : IAuthenticationBackend,IAccountBoundAuthenticationBackend
        {
            public bool IsAuthorized { get; set; }
            public string PlayerId { get; set; }
            public string BoundAccountId { get; set; }
            public long ExpiresAt { get; set; }
            public event Action Expired;
            public event Action SignedOut;
            public Func<string> Account;
            public TaskCompletionSource<bool> Gate;
            public string Error,OverrideIdentity;
            public int Logins,Signouts;
            public Task InitializeAsync(string environment,string profile)=>Task.CompletedTask;
            public async Task SignInAsync()
            {
                Logins++; var account=Account(); var gate=Gate; Gate=null;
                if(gate!=null) await gate.Task;
                if(Error!=null)throw new OnlineSignInException(Error);
                IsAuthorized=true; BoundAccountId=OverrideIdentity??account; PlayerId="ugs-"+BoundAccountId;
            }
            public void SignOut() { Signouts++; IsAuthorized=false; BoundAccountId=null; ExpiresAt=0; SignedOut?.Invoke(); }
            public void Expire() { IsAuthorized=false; Expired?.Invoke(); }
        }
        sealed class Api : IAccountApi
        {
            public bool UsesCookies=>false;
            public string SessionScope=>"online-test";
            public TaskCompletionSource<AccountReply> Online;
            public readonly Queue<AccountReply> Replies=new Queue<AccountReply>();
            public Task<AccountReply> SendAsync(string method,string path,AccountPayload payload,string token)
                =>path.StartsWith("/auth/ugs/",StringComparison.Ordinal)?Online.Task:Task.FromResult(Replies.Dequeue());
        }
        [Test] public async Task AnonymousTestBackendCanBeReadyWithoutAnAccountBinding()
        {
            var backend=new Backend { Account=()=>"anonymous-test" };
            var service=new OnlineAuthenticationService(backend);
            await service.EnsureSignedInAsync();
            Assert.That(service.IsReady,Is.True);
            Assert.That(backend.Logins,Is.EqualTo(1));
        }
        [Test] public void AccountAndAnonymousTestRoomsAreSeparated()
        {
            var before=PokeChess.Network.Session.OnlineConnection.RequireAccountAdmission;
            try {
                PokeChess.Network.Session.OnlineConnection.RequireAccountAdmission=true;
                var accountRoom=PokeChess.Network.Session.RoomBrowser.RoomKind;
                PokeChess.Network.Session.OnlineConnection.RequireAccountAdmission=false;
                Assert.That(PokeChess.Network.Session.RoomBrowser.RoomKind,Is.Not.EqualTo(accountRoom));
            } finally { PokeChess.Network.Session.OnlineConnection.RequireAccountAdmission=before; }
        }
        [Test] public async Task ReadyRequiresTheCurrentlySignedInAccountBinding()
        {
            string account="a"; var backend=new Backend { Account=()=>account }; var service=new OnlineAuthenticationService(backend,()=>account!=null,()=>account);
            await service.EnsureSignedInAsync(); Assert.That(service.IsReady,Is.True);
            account="b"; Assert.That(service.IsReady,Is.False);
            await service.EnsureSignedInAsync(); Assert.That(service.IsReady,Is.True); Assert.That(service.PlayerId,Is.EqualTo("ugs-b"));
        }
        [Test] public async Task ExistingAnonymousAuthorizationCannotSkipServerSignIn()
        {
            var backend=new Backend { Account=()=>"a",IsAuthorized=true,PlayerId="old-anonymous" };
            var service=new OnlineAuthenticationService(backend,()=>true,()=>"a"); await service.EnsureSignedInAsync();
            Assert.That(backend.Logins,Is.EqualTo(1)); Assert.That(service.PlayerId,Is.EqualTo("ugs-a"));
        }
        [Test] public async Task MissingServerSetupFailsClosedWithSafeError()
        {
            var backend=new Backend { Account=()=>"a",Error="online_not_configured" }; var service=new OnlineAuthenticationService(backend,()=>true,()=>"a");
            await service.EnsureSignedInAsync(); Assert.That(service.IsReady,Is.False); Assert.That(service.Error,Is.EqualTo("online_not_configured"));
        }
        [Test] public async Task WrongBoundIdentityCannotBecomeReady()
        {
            var backend=new Backend { Account=()=>"a",OverrideIdentity="foreign" }; var service=new OnlineAuthenticationService(backend,()=>true,()=>"a");
            await service.EnsureSignedInAsync(); Assert.That(service.IsReady,Is.False); Assert.That(service.State,Is.EqualTo(AuthenticationState.Failed));
        }
        [Test] public async Task AccountSwitchDuringPendingSignInCannotPublishOldIdentity()
        {
            string account="a"; var gate=new TaskCompletionSource<bool>(); var backend=new Backend { Account=()=>account,Gate=gate };
            var service=new OnlineAuthenticationService(backend,()=>account!=null,()=>account);
            var first=service.EnsureSignedInAsync(); account="b"; service.InvalidateForAccountChange();
            var second=service.EnsureSignedInAsync(); gate.SetResult(true); await Task.WhenAll(first,second);
            Assert.That(service.IsReady,Is.True); Assert.That(service.PlayerId,Is.EqualTo("ugs-b")); Assert.That(backend.Logins,Is.EqualTo(2));
        }
        [Test] public async Task SameAccountReloginMustStartANewGenerationAfterPendingSignIn()
        {
            var gate=new TaskCompletionSource<bool>();
            var backend=new Backend { Account=()=>"a",Gate=gate };
            var service=new OnlineAuthenticationService(backend,()=>true,()=>"a");
            var previous=service.EnsureSignedInAsync();
            service.InvalidateForAccountChange(); service.SignOut();
            var relogin=service.EnsureSignedInAsync();
            Assert.That(ReferenceEquals(previous,relogin),Is.False);
            gate.SetResult(true); await Task.WhenAll(previous,relogin);
            Assert.That(backend.Logins,Is.EqualTo(2));
            Assert.That(service.IsReady,Is.True); Assert.That(service.PlayerId,Is.EqualTo("ugs-a"));
        }        [Test] public async Task DuplicateRequestsShareThePendingOperation()
        {
            var gate=new TaskCompletionSource<bool>(); var backend=new Backend { Account=()=>"a",Gate=gate }; var service=new OnlineAuthenticationService(backend,()=>true,()=>"a");
            var first=service.EnsureSignedInAsync(); var second=service.EnsureSignedInAsync(); Assert.That(ReferenceEquals(first,second),Is.True);
            gate.SetResult(true); await first; Assert.That(backend.Logins,Is.EqualTo(1));
        }
        [Test] public async Task AccessTokenIsRenewedThroughTheAccountBackend()
        {
            var backend=new Backend { Account=()=>"a",ExpiresAt=DateTimeOffset.UtcNow.ToUnixTimeSeconds()+60 }; var service=new OnlineAuthenticationService(backend,()=>true,()=>"a");
            await service.EnsureSignedInAsync(); service.Tick(); Assert.That(backend.Logins,Is.EqualTo(2));
            service.SignOut(); Assert.That(service.IsReady,Is.False); Assert.That(backend.Signouts,Is.EqualTo(1));
        }
        [Test] public async Task RevokedOwnSessionClearsLoginBeforeReturningUgsCredentials()
        {
            var api=new Api { Online=new TaskCompletionSource<AccountReply>() };
            api.Replies.Enqueue(new AccountReply { Status=200,AccountId="a",Token=new string('A',64) });
            var own=new AccountSessionService(api); await own.LoginAsync("alice","password");
            var request=own.RequestOnlineAsync("/auth/ugs/sign-in"); api.Online.SetResult(new AccountReply { Status=401 });
            Assert.That((await request).Status,Is.EqualTo(401)); Assert.That(own.IsSignedIn,Is.False);
        }
        [Test] public async Task StaleAuxiliaryFailureCannotSignOutANewLogin()
        {
            var api=new Api { Online=new TaskCompletionSource<AccountReply>() };
            api.Replies.Enqueue(new AccountReply { Status=200,AccountId="a",Token=new string('A',64) });
            api.Replies.Enqueue(new AccountReply { Status=204 });
            api.Replies.Enqueue(new AccountReply { Status=200,AccountId="a",Token=new string('B',64) });
            var own=new AccountSessionService(api); await own.LoginAsync("alice","password");
            var pending=own.RequestOnlineAsync("/auth/ugs/sign-in"); await own.LogoutAsync(false); await own.LoginAsync("alice","password");
            api.Online.SetResult(new AccountReply { Status=401 }); Assert.That((await pending).ErrorCode,Is.EqualTo("online_account_changed"));
            Assert.That(own.IsSignedIn,Is.True);
        }
    }
}
