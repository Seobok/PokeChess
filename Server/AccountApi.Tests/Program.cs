using Microsoft.Data.Sqlite;

var root = Path.Combine(Path.GetTempPath(), "pokechess-auth-security-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
const string password = "test-password-before-change";
const string changedPassword = "test-password-after-change";
var clock = new TestClock();
var path = Path.Combine(root, "accounts.db");
var store = new AccountStore(path, clock);
void Check(bool condition, string description) { if (!condition) throw new Exception(description); }
void Success(AuthResult result) => Check(result.Status == AuthStatus.Success, "Expected success");
Check(store.Register("alice", password) is not null, "Registration");
Check(store.Register("bob", password) is not null, "Second registration");
var existing = store.Login("alice", password); Success(existing);
for (var i = 0; i < 5; i++) Check(store.Login("alice", "wrong-password-long-enough").Status == AuthStatus.Unauthorized, "Failure counting");
Check(store.Login("alice", password).Status == AuthStatus.Throttled, "Valid password cannot bypass cooldown");
Check(store.Resolve(existing.Token!) == existing.AccountId, "Cooldown must not revoke existing sessions");
Success(store.Login("bob", password));
store = new AccountStore(path, clock);
Check(store.Login("alice", password).RetryAfterSeconds == 900, "Cooldown persists across store restart");
clock.Advance(TimeSpan.FromSeconds(899));
Check(store.Login("alice", password).RetryAfterSeconds == 1, "Retry-after boundary");
clock.Advance(TimeSpan.FromSeconds(1));
Success(store.Login("alice", password));
for (var i = 0; i < 5; i++) Check(store.Login("missing", password).Status == AuthStatus.Unauthorized, "Unknown account failure");
Check(store.Login("missing", password).Status == AuthStatus.Throttled, "Unknown account same throttle");

var bobSession = store.Login("bob", password); Success(bobSession);
for (var i = 0; i < 5; i++) Check(store.Login("bob", "wrong-password-long-enough").Status == AuthStatus.Unauthorized, "Shared failure counter");
Check(store.ChangePassword(bobSession.Token!, password, changedPassword).Status == AuthStatus.Throttled, "Password change shares account cooldown");
Check(store.RevokeAll(bobSession.Token!), "Logout-all still possible during cooldown");
clock.Advance(TimeSpan.FromSeconds(900));
var tokens = new List<string>();
for (var i = 0; i < 6; i++) { var login = store.Login("alice", password); Success(login); tokens.Add(login.Token!); }
Check(store.Resolve(tokens[0]) is null, "Oldest session eviction");
Check(tokens.Skip(1).All(token => store.Resolve(token) is not null), "Keep newest five");
var bob = store.Login("bob", password); Success(bob);
Check(!store.RevokeAll("invalid"), "Invalid logout-all");
Check(store.RevokeAll(tokens[^1]), "Logout all");
Check(tokens.All(token => store.Resolve(token) is null), "All account sessions revoked");
Check(store.Resolve(bob.Token!) == bob.AccountId, "Other account unaffected");
var first = store.Login("alice", password); var second = store.Login("alice", password);
Check(store.ChangePassword("invalid", password, changedPassword).Status == AuthStatus.Unauthorized, "Change requires session");
Check(store.ChangePassword(first.Token!, "wrong-password-long-enough", changedPassword).Status == AuthStatus.Unauthorized, "Current password required");
Check(store.Resolve(first.Token!) is not null, "Failed change preserves session");
Success(store.ChangePassword(first.Token!, password, changedPassword));
Check(store.Resolve(first.Token!) is null && store.Resolve(second.Token!) is null, "Password change revokes every session");
Check(store.Login("alice", password).Status == AuthStatus.Unauthorized, "Old password rejected");
Success(store.Login("alice", changedPassword));
// Concurrent session creation is serialized by SQLite, including across store instances.
var concurrentTokens = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(() => new AccountStore(path, clock).Login("alice", changedPassword).Token!)));
Check(concurrentTokens.Count(token => store.Resolve(token) is not null) == 5, "Concurrent logins obey session cap");
var raceSession = store.Login("alice", changedPassword);
var race = await Task.WhenAll(Task.Run(() => store.Login("alice", changedPassword)),
    Task.Run(() => new AccountStore(path, clock).ChangePassword(raceSession.Token!, changedPassword, password)));
Success(race[1]);
Check(race[0].Token is null || store.Resolve(race[0].Token!) is null, "Old-password login cannot survive password change race");
var expiring = store.Login("alice", password); Success(expiring);
clock.Advance(TimeSpan.FromDays(7));
Check(store.Resolve(expiring.Token!) is null, "Exact expiry boundary");

// Existing three-column session schema is upgraded without destroying accounts/sessions.
var legacyPath = Path.Combine(root, "legacy.db");
using (var db = new SqliteConnection("Data Source=" + legacyPath)) {
    db.Open(); using var command = db.CreateCommand();
    command.CommandText = "CREATE TABLE accounts(id TEXT PRIMARY KEY,username TEXT UNIQUE NOT NULL,salt BLOB NOT NULL,hash BLOB NOT NULL); CREATE TABLE sessions(hash TEXT PRIMARY KEY,account_id TEXT NOT NULL REFERENCES accounts(id),expires INTEGER NOT NULL);";
    command.ExecuteNonQuery();
}
Check(new AccountStore(legacyPath, clock).Register("legacy", password) is not null, "Additive schema upgrade");
var gate = new PasswordWorkGate();
var lease1 = gate.TryEnter(); var lease2 = gate.TryEnter();
Check(lease1 is not null && lease2 is not null && gate.TryEnter() is null, "Two password operations, no waiting queue");
lease1!.Dispose(); lease1.Dispose();
using (var replacement = gate.TryEnter()) Check(replacement is not null && gate.TryEnter() is null, "Slot released exactly once");
lease2!.Dispose();
try { using var failureLease = gate.TryEnter(); throw new InvalidOperationException(); } catch (InvalidOperationException) { }
using var recovered1 = gate.TryEnter(); using var recovered2 = gate.TryEnter();
Check(recovered1 is not null && recovered2 is not null, "Slots restored after exception");
await OperationsTests.Run(root);
await RecoveryTests.Run(root);
await UgsIdentityTests.Run(root);
Console.WriteLine("PASS: persistent account throttle, cooldown boundaries, unknown accounts, isolation, session cap/concurrency, logout-all, password reauthentication/revocation/race, expiry, schema upgrade, password work gate.");
sealed class TestClock : TimeProvider
{
    DateTimeOffset now = new(2026, 11, 22, 0, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan duration) => now += duration;
}
