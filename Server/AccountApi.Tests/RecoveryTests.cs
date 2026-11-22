using Microsoft.Data.Sqlite;

static class RecoveryTests
{
    public static async Task Run(string root)
    {
        const string password = "recovery-original-password";
        const string changed = "recovery-updated-password";
        var path = Path.Combine(root, "recovery.db"); var clock = new TestClock();
        var store = new AccountStore(path, clock);
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        string Register(string name) => store.Register(name, password) ?? throw new Exception("Registration failed");
        void Success(RecoveryResult result) => Check(result.Status == AuthStatus.Success && result.RecoveryCode is { Length: 71 }, "Recovery success with replacement");
        var code = Register("recover_alice"); var otherCode = Register("recover_bob");
        Check(code.Length == 71 && code != otherCode, "Distinct recovery codes");
        Check(store.Register("recover_alice", password) is null, "Duplicate registration must not disclose/reset recovery code");
        var alice1 = store.Login("recover_alice", password); var alice2 = store.Login("recover_alice", password);
        var bob = store.Login("recover_bob", password);
        using (var db = new SqliteConnection("Data Source=" + path)) {
            db.Open(); using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT r.hash FROM recovery_codes r JOIN accounts a ON a.id=r.account_id WHERE a.username='recover_alice'";
            Check(cmd.ExecuteScalar() is byte[] { Length: 32 }, "DB stores 32-byte recovery digest only");
        }
        Check(store.Recover("recover_alice", otherCode, changed).Status == AuthStatus.Unauthorized, "Recovery code bound to account");
        Check(store.Recover("recover_alice", "invalid-code", changed).Status == AuthStatus.Unauthorized, "Invalid recovery code");
        Check(store.Resolve(alice1.Token!) is not null, "Failed recovery must preserve sessions");
        Check(store.Recover("missing", code, changed).Status == AuthStatus.Unauthorized, "Unknown account same error");
        for (var i = 0; i < 5; i++) Check(store.Login("recover_alice", "wrong-current-password").Status == AuthStatus.Unauthorized, "Login failure setup");
        Check(store.Login("recover_alice", password).Status == AuthStatus.Throttled, "Login blocked before recovery");
        store = new AccountStore(path, clock);
        var recovered = store.Recover("recover_alice", code.Replace("-", "").ToLowerInvariant(), changed);
        Success(recovered);
        Check(recovered.RecoveryCode != code, "Recovery rotates code");
        Check(store.Resolve(alice1.Token!) is null && store.Resolve(alice2.Token!) is null, "Recovery revokes all sessions");
        Check(store.Resolve(bob.Token!) == bob.AccountId, "Recovery isolates accounts");
        Check(store.Login("recover_alice", password).Status == AuthStatus.Unauthorized, "Old password rejected");
        var newSession = store.Login("recover_alice", changed);
        Check(newSession.Status == AuthStatus.Success, "New password works, login throttle reset");
        Check(store.Recover("recover_alice", code, password).Status == AuthStatus.Unauthorized, "Used code rejected");
        Check(store.RotateRecoveryCode("invalid-token", changed).Status == AuthStatus.Unauthorized, "Rotation requires session");
        Check(store.RotateRecoveryCode(newSession.Token!, password).Status == AuthStatus.Unauthorized, "Rotation requires current password");
        var rotated = store.RotateRecoveryCode(newSession.Token!, changed); Success(rotated);
        Check(store.Resolve(newSession.Token!) is not null, "Rotation preserves existing session");
        Check(store.Recover("recover_alice", recovered.RecoveryCode!, password).Status == AuthStatus.Unauthorized, "Rotated-away code rejected");
        Success(store.Recover("recover_alice", rotated.RecoveryCode!, password));

        // Recovery abuse never consumes login attempts or revokes sessions.
        var throttleCode = Register("recovery_throttle");
        var active = store.Login("recovery_throttle", password);
        for (var i = 0; i < 5; i++) Check(store.Recover("recovery_throttle", "invalid-code", changed).Status == AuthStatus.Unauthorized, "Recovery failures");
        store = new AccountStore(path, clock);
        Check(store.Recover("recovery_throttle", throttleCode, changed).RetryAfterSeconds == 900, "Persistent separate recovery cooldown");
        Check(store.Login("recovery_throttle", password).Status == AuthStatus.Success && store.Resolve(active.Token!) is not null, "Recovery attack does not lock login");
        clock.Advance(TimeSpan.FromSeconds(899));
        Check(store.Recover("recovery_throttle", throttleCode, changed).RetryAfterSeconds == 1, "Recovery cooldown boundary");
        clock.Advance(TimeSpan.FromSeconds(1));
        Success(store.Recover("recovery_throttle", throttleCode, changed));
        for (var i = 0; i < 5; i++) Check(store.Recover("still_missing", "invalid-code", password).Status == AuthStatus.Unauthorized, "Unknown-account recovery failures");
        Check(store.Recover("still_missing", throttleCode, password).Status == AuthStatus.Throttled, "Unknown-account recovery same cooldown");
        var ordinaryCode = Register("ordinary_change"); var ordinarySession = store.Login("ordinary_change", password);
        Check(store.ChangePassword(ordinarySession.Token!, password, changed).Status == AuthStatus.Success, "Ordinary password change");
        Success(store.Recover("ordinary_change", ordinaryCode, password));

        // Only one request can consume a code, even through independent store instances.
        var raceCode = Register("recovery_race");
        var race = await Task.WhenAll(Task.Run(() => new AccountStore(path, clock).Recover("recovery_race", raceCode, changed)),
            Task.Run(() => new AccountStore(path, clock).Recover("recovery_race", raceCode, "different-race-password")));
        Check(race.Count(result => result.Status == AuthStatus.Success) == 1 && race.Count(result => result.Status == AuthStatus.Unauthorized) == 1, "Exactly one concurrent recovery succeeds");
        var winnerPassword = race[0].Status == AuthStatus.Success ? changed : "different-race-password";
        Check(store.Login("recovery_race", winnerPassword).Status == AuthStatus.Success, "Winning recovery owns password");
        var loginRaceCode = Register("recovery_login_race");
        var oldLogin = Task.Run(() => new AccountStore(path, clock).Login("recovery_login_race", password));
        var reset = Task.Run(() => new AccountStore(path, clock).Recover("recovery_login_race", loginRaceCode, changed));
        await Task.WhenAll(oldLogin, reset); Success(reset.Result);
        Check(oldLogin.Result.Token is null || store.Resolve(oldLogin.Result.Token) is null, "Old-password login cannot survive recovery race");

        // Simulate an existing account DB from before recovery tables were introduced.
        var legacyPath = Path.Combine(root, "pre-recovery.db");
        var legacy = new AccountStore(legacyPath, clock); var discarded = legacy.Register("legacy", password);
        var legacySession = legacy.Login("legacy", password);
        using (var db = new SqliteConnection("Data Source=" + legacyPath)) {
            db.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "DROP TABLE recovery_codes; DROP TABLE recovery_failures"; cmd.ExecuteNonQuery();
        }
        legacy = new AccountStore(legacyPath, clock);
        Check(legacy.Resolve(legacySession.Token!) == legacySession.AccountId, "Additive migration preserves old session/account");
        Check(legacy.Recover("legacy", discarded!, changed).Status == AuthStatus.Unauthorized, "No code assigned to legacy account silently");
        var issued = legacy.RotateRecoveryCode(legacySession.Token!, password); Success(issued);
        Success(legacy.Recover("legacy", issued.RecoveryCode!, changed));
        Console.WriteLine("PASS: recovery issuance/digest, account binding, separate persisted cooldown, password reset/session revocation, single-use/rotation, concurrent redemption/login race, legacy-account migration.");
    }
}
