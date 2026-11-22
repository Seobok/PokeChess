using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using PokeChess.Client.Services;
using UnityEngine;

namespace PokeChess.Core.Tests
{
    public sealed class AccountPersistenceTests
    {
        const string Token="0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
        const string Code="01234567-89ABCDEF-01234567-89ABCDEF-01234567-89ABCDEF-01234567-89ABCDEF";
        const string Scope="https://test.example/api/";
        sealed class Api : IAccountApi
        {
            public bool UsesCookies { get; set; }
            public string SessionScope { get; set; }=Scope;
            public readonly Queue<AccountReply> Replies=new Queue<AccountReply>();
            public readonly List<string> Tokens=new List<string>();
            public TaskCompletionSource<AccountReply> Pending;
            public Task<AccountReply> SendAsync(string method,string path,AccountPayload payload,string token)
            { Tokens.Add(token); return Pending!=null?Pending.Task:Task.FromResult(Replies.Dequeue()); }
            public Api Reply(long status,string id=null,string token=null,string code=null)
            { Replies.Enqueue(new AccountReply { Status=status,AccountId=id,Token=token,RecoveryCode=code }); return this; }
        }
        sealed class Store : ISessionTokenStore
        {
            public readonly Dictionary<string,string> Values=new Dictionary<string,string>();
            public bool FailRead,FailWrite,FailDelete,DiscardWrite;
            public int Reads,Writes,Deletes;
            public TaskCompletionSource<string> ReadGate;
            public Task<string> ReadAsync(string scope)
            { Reads++; if(FailRead)throw new InvalidOperationException(); return ReadGate!=null?ReadGate.Task:Task.FromResult(Values.TryGetValue(scope,out var value)?value:null); }
            public Task WriteAsync(string scope,string token)
            { Writes++; if(FailWrite)throw new InvalidOperationException(); if(!DiscardWrite)Values[scope]=token; return Task.CompletedTask; }
            public Task DeleteAsync(string scope)
            { Deletes++; if(FailDelete)throw new InvalidOperationException(); Values.Remove(scope); return Task.CompletedTask; }
        }
        sealed class BrokenGuard : ISessionRestoreGuard
        {
            public bool IsBlocked(string scope)=>throw new IOException();
            public void Block(string scope)=>throw new IOException();
            public void Allow(string scope)=>throw new IOException();
        }
        [Test] public async Task NewServiceRestoresOnlyAfterServerVerification()
        {
            var store=new Store(); var guard=new MemoryRestoreGuard();
            var first=new AccountSessionService(new Api().Reply(200,"account",Token),store,guard);
            Assert.That(await first.LoginAsync("alice","password"),Is.True);
            var api=new Api().Reply(200,"account"); var restarted=new AccountSessionService(api,store,guard);
            Assert.That(restarted.IsSignedIn,Is.False);
            Assert.That(await restarted.RestoreAsync(),Is.True); Assert.That(restarted.IsSignedIn,Is.True);
            Assert.That(api.Tokens[0],Is.EqualTo(Token)); Assert.That(restarted.RestoreCompleted,Is.True);
        }
        [Test] public async Task MissingOrBlockedCredentialDoesNotContactServer()
        {
            var store=new Store(); var guard=new MemoryRestoreGuard(); var api=new Api();
            Assert.That(await new AccountSessionService(api,store,guard).RestoreAsync(),Is.False);
            store.Values[Scope]=Token; guard.Block(Scope);
            Assert.That(await new AccountSessionService(api,store,guard).RestoreAsync(),Is.False); Assert.That(api.Tokens.Count,Is.Zero);
        }
        [Test] public async Task InvalidStoredTokenIsRemovedBeforeAnyRequest()
        {
            var store=new Store(); store.Values[Scope]="corrupt"; var api=new Api(); var guard=new MemoryRestoreGuard();
            var service=new AccountSessionService(api,store,guard);
            Assert.That(await service.RestoreAsync(),Is.False); Assert.That(api.Tokens.Count,Is.Zero);
            Assert.That(store.Values.ContainsKey(Scope),Is.False); Assert.That(guard.IsBlocked(Scope),Is.True);
        }
        [Test] public async Task ExpiredOrRevokedSavedSessionCannotRestore()
        {
            var store=new Store(); store.Values[Scope]=Token; var guard=new MemoryRestoreGuard();
            var service=new AccountSessionService(new Api().Reply(401),store,guard);
            Assert.That(await service.RestoreAsync(),Is.False); Assert.That(service.Error,Is.EqualTo("session_expired"));
            Assert.That(service.IsSignedIn,Is.False); Assert.That(store.Values.ContainsKey(Scope),Is.False); Assert.That(guard.IsBlocked(Scope),Is.True);
        }
        [Test] public async Task OfflineRestoreRetainsCredentialWithoutAuthorizingAndCanRetry()
        {
            var store=new Store(); store.Values[Scope]=Token;
            var service=new AccountSessionService(new Api().Reply(0).Reply(200,"account"),store);
            Assert.That(await service.RestoreAsync(),Is.False); Assert.That(service.IsSignedIn,Is.False);
            Assert.That(service.HasSavedSession,Is.True); Assert.That(store.Values[Scope],Is.EqualTo(Token));
            Assert.That(await service.RestoreAsync(),Is.True);
        }
        [Test] public async Task LockedVaultCanBeRetriedWithoutPlaintextFallback()
        {
            var store=new Store { FailRead=true }; store.Values[Scope]=Token;
            var api=new Api().Reply(200,"account"); var service=new AccountSessionService(api,store);
            Assert.That(await service.RestoreAsync(),Is.False); Assert.That(service.HasSavedSession,Is.True); Assert.That(api.Tokens.Count,Is.Zero);
            store.FailRead=false; Assert.That(await service.RestoreAsync(),Is.True);
        }
        [Test] public async Task FailedWriteRetainsOnlyCurrentInMemoryLoginAndBlocksOldCredential()
        {
            var store=new Store { FailWrite=true,FailDelete=true }; store.Values[Scope]=Token;
            var guard=new MemoryRestoreGuard(); var service=new AccountSessionService(new Api().Reply(200,"account",Token),store,guard);
            Assert.That(await service.LoginAsync("alice","password"),Is.True); Assert.That(service.IsSignedIn,Is.True);
            Assert.That(service.StorageWarning,Is.EqualTo("session_not_saved"));
            Assert.That(await new AccountSessionService(new Api(),store,guard).RestoreAsync(),Is.False);
        }
        [Test] public async Task WriteIsReadBackBeforeAllowingRestore()
        {
            var store=new Store { DiscardWrite=true }; var guard=new MemoryRestoreGuard();
            var service=new AccountSessionService(new Api().Reply(200,"account",Token),store,guard);
            Assert.That(await service.LoginAsync("alice","password"),Is.True); Assert.That(service.HasSavedSession,Is.False);
            Assert.That(guard.IsBlocked(Scope),Is.True);
        }
        [Test] public async Task OfflineLogoutWithVaultDeletionFailureCannotRestore()
        {
            var store=new Store(); var guard=new MemoryRestoreGuard(); var service=new AccountSessionService(new Api().Reply(200,"account",Token).Reply(0),store,guard);
            await service.LoginAsync("alice","password"); store.FailDelete=true;
            Assert.That(await service.LogoutAsync(false),Is.False); Assert.That(service.IsSignedIn,Is.False);
            Assert.That(store.Values.ContainsKey(Scope),Is.True); Assert.That(guard.IsBlocked(Scope),Is.True);
            Assert.That(await new AccountSessionService(new Api(),store,guard).RestoreAsync(),Is.False);
        }
        [Test] public async Task BothDurableClearMechanismsFailWithVisibleWarningAndRetryOption()
        {
            var store=new Store { FailDelete=true }; store.Values[Scope]=Token;
            var service=new AccountSessionService(new Api(),store,new BrokenGuard());
            Assert.That(await service.ForgetSavedSessionAsync(),Is.False);
            Assert.That(service.StorageWarning,Is.EqualTo("saved_login_clear_failed")); Assert.That(service.HasSavedSession,Is.True);
        }
        [Test] public async Task PasswordChangeAndRecoveryRemovePersistedSession()
        {
            var store=new Store(); var guard=new MemoryRestoreGuard(); var api=new Api().Reply(200,"account",Token).Reply(204);
            var service=new AccountSessionService(api,store,guard); await service.LoginAsync("alice","password");
            Assert.That(await service.ChangePasswordAsync("old","new"),Is.True); Assert.That(store.Values.ContainsKey(Scope),Is.False);
            api.Reply(200,"account",Token).Reply(200,code:Code); await service.LoginAsync("alice","password");
            Assert.That(await service.RecoverAsync("alice",Code,"new"),Is.True); Assert.That(store.Values.ContainsKey(Scope),Is.False);
            Assert.That(service.PendingRecoveryCode,Is.EqualTo(Code)); Assert.That(guard.IsBlocked(Scope),Is.True);
        }
        [Test] public async Task ApiChangeNeverSendsOldTokenToNewServer()
        {
            var store=new Store(); var api=new Api().Reply(200,"account",Token); var service=new AccountSessionService(api,store);
            await service.LoginAsync("alice","password"); api.SessionScope="https://other.example/";
            Assert.That(await service.ValidateAsync(),Is.False); Assert.That(api.Tokens.Count,Is.EqualTo(1));
            Assert.That(service.IsSignedIn,Is.False); Assert.That(store.Values[Scope],Is.EqualTo(Token));
        }
        [Test] public async Task ApiChangeWhileUnlockingNeverSendsTheLoadedToken()
        {
            var store=new Store { ReadGate=new TaskCompletionSource<string>() }; var api=new Api(); var service=new AccountSessionService(api,store);
            var restoring=service.RestoreAsync(); api.SessionScope="https://other.example/"; store.ReadGate.SetResult(Token);
            Assert.That(await restoring,Is.False); Assert.That(api.Tokens.Count,Is.Zero);
        }
        [Test] public async Task BrowserOfflineLogoutBlocksCookieRestoreWithoutStoringTokens()
        {
            var store=new Store(); var guard=new MemoryRestoreGuard();
            var api=new Api { UsesCookies=true }; api.Reply(200,"account").Reply(0);
            var service=new AccountSessionService(api,store,guard); await service.LoginAsync("alice","password"); await service.LogoutAsync(false);
            var restartedApi=new Api { UsesCookies=true };
            Assert.That(await new AccountSessionService(restartedApi,store,guard).RestoreAsync(),Is.False);
            Assert.That(store.Reads+store.Writes+store.Deletes,Is.Zero); Assert.That(restartedApi.Tokens.Count,Is.Zero);
        }
        [Test] public async Task BrowserFreshLoginAllowsFutureCookieRestore()
        {
            var store=new Store(); var guard=new MemoryRestoreGuard(); guard.Block(Scope);
            var api=new Api { UsesCookies=true }; api.Reply(200,"account");
            await new AccountSessionService(api,store,guard).LoginAsync("alice","password");
            var restartingApi=new Api { UsesCookies=true }; restartingApi.Reply(200,"account");
            Assert.That(await new AccountSessionService(restartingApi,store,guard).RestoreAsync(),Is.True);
            Assert.That(store.Reads+store.Writes+store.Deletes,Is.Zero); Assert.That(restartingApi.Tokens[0],Is.Null);
        }
        [Test] public async Task DuplicateRestoreMakesOnlyOneServerRequest()
        {
            var store=new Store(); store.Values[Scope]=Token;
            var api=new Api { Pending=new TaskCompletionSource<AccountReply>() }; var service=new AccountSessionService(api,store);
            var first=service.RestoreAsync(); Assert.That(service.IsRestoring,Is.True);
            Assert.That(await service.RestoreAsync(),Is.False); Assert.That(api.Tokens.Count,Is.EqualTo(1));
            api.Pending.SetResult(new AccountReply { Status=200,AccountId="account" }); Assert.That(await first,Is.True);
        }
        [Test] public void RestoreMarkerContainsNoCredentialsAndSurvivesGuardRecreation()
        {
            var path=Path.Combine(Path.GetTempPath(),"pokechess-restore-"+Guid.NewGuid().ToString("N")); var guard=new FileRestoreGuard(path);
            guard.Block(Scope); Assert.That(new FileRestoreGuard(path).IsBlocked(Scope),Is.True);
            var files=Directory.GetFiles(path); Assert.That(files.Length,Is.EqualTo(1)); Assert.That(File.ReadAllBytes(files[0]),Is.EqualTo(new byte[]{1}));
            guard.Allow(Scope); Assert.That(new FileRestoreGuard(path).IsBlocked(Scope),Is.False);
        }
        [Test] public async Task WindowsCredentialVaultRoundTripAcrossStoreInstances()
        {
            if(Application.platform!=RuntimePlatform.WindowsEditor && Application.platform!=RuntimePlatform.WindowsPlayer) Assert.Ignore("Windows-only vault test");
            var scope="pokechess-vault-test-"+Guid.NewGuid().ToString("N"); var store=new WindowsSessionTokenStore();
            try {
                Assert.That(await store.ReadAsync(scope),Is.Null); await store.WriteAsync(scope,Token);
                Assert.That(await new WindowsSessionTokenStore().ReadAsync(scope),Is.EqualTo(Token));
                await store.DeleteAsync(scope); Assert.That(await new WindowsSessionTokenStore().ReadAsync(scope),Is.Null);
            } finally { await store.DeleteAsync(scope); }
        }
    }
}
