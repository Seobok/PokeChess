using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

public sealed record RecoveryResult(AuthStatus Status, string? RecoveryCode = null, int RetryAfterSeconds = 0);

public sealed partial class AccountStore
{
    // High-entropy offline recovery secret: no expiration, single use, replaceable.
    static string NewRecoveryCode()
    {
        var hex = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        return string.Join("-", Enumerable.Range(0, 8).Select(i => hex.Substring(i * 8, 8)));
    }
    static string? NormalizeRecoveryCode(string code)
    {
        if (code.Length != 64 && code.Length != 71) return null;
        if (code.Length == 71 && Enumerable.Range(1, 7).Any(i => code[i * 9 - 1] != '-')) return null;
        var normalized = code.Replace("-", "").ToUpperInvariant();
        return normalized.Length == 64 && normalized.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F') ? normalized : null;
    }
    static byte[] RecoveryHash(string normalized) => SHA256.HashData(Encoding.UTF8.GetBytes("PokeChess.Recovery.v1:" + normalized));
    static void SaveRecoveryCode(SqliteConnection db, SqliteTransaction tx, string id, string code) =>
        Execute(db, tx, """
            INSERT INTO recovery_codes VALUES($id,$hash)
            ON CONFLICT(account_id) DO UPDATE SET hash=excluded.hash
            """, ("$id", id), ("$hash", RecoveryHash(NormalizeRecoveryCode(code)!)));
    int RecoveryRetryAfter(SqliteConnection db, SqliteTransaction tx, string username)
    {
        Execute(db, tx, "DELETE FROM recovery_failures WHERE expires <= $now", ("$now", Now));
        using var cmd = Command(db, tx, "SELECT failures,expires FROM recovery_failures WHERE username=$name", ("$name", username));
        using var reader = cmd.ExecuteReader();
        return reader.Read() && reader.GetInt32(0) >= MaxFailures ? (int)Math.Max(1, reader.GetInt64(1) - Now) : 0;
    }
    static void ResetRecoveryFailures(SqliteConnection db, SqliteTransaction tx, string username) =>
        Execute(db, tx, "DELETE FROM recovery_failures WHERE username=$name", ("$name", username));
    public RecoveryResult Recover(string username, string recoveryCode, string newPassword)
    {
        using var db = Open(); using var tx = db.BeginTransaction(deferred: false);
        var retry = RecoveryRetryAfter(db, tx, username);
        if (retry > 0) { tx.Commit(); return new(AuthStatus.Throttled, RetryAfterSeconds: retry); }
        string? id = null; byte[] expected = new byte[32];
        using (var cmd = Command(db, tx, """
            SELECT a.id,r.hash FROM accounts a JOIN recovery_codes r ON r.account_id=a.id WHERE a.username=$name
            """, ("$name", username)))
        using (var reader = cmd.ExecuteReader())
            if (reader.Read()) { id = reader.GetString(0); expected = (byte[])reader[1]; }
        var normalized = NormalizeRecoveryCode(recoveryCode);
        var valid = CryptographicOperations.FixedTimeEquals(RecoveryHash(normalized ?? ""), expected);
        if (!valid || normalized is null || id is null)
        {
            Execute(db, tx, """
                INSERT INTO recovery_failures VALUES($name,1,$expires)
                ON CONFLICT(username) DO UPDATE SET failures=failures+1
                """, ("$name", username), ("$expires", Now + FailureWindowSeconds));
            tx.Commit(); return new(AuthStatus.Unauthorized);
        }
        var salt = RandomNumberGenerator.GetBytes(16); var hash = Hash(newPassword, salt);
        var replacement = NewRecoveryCode();
        Execute(db, tx, "UPDATE accounts SET salt=$salt,hash=$hash WHERE id=$id", ("$salt", salt), ("$hash", hash), ("$id", id));
        SaveRecoveryCode(db, tx, id, replacement);
        Execute(db, tx, "DELETE FROM sessions WHERE account_id=$id", ("$id", id));
        ResetFailures(db, tx, username);
        ResetRecoveryFailures(db, tx, username);
        tx.Commit(); return new(AuthStatus.Success, replacement);
    }
    public RecoveryResult RotateRecoveryCode(string token, string currentPassword)
    {
        using var db = Open(); using var tx = db.BeginTransaction(deferred: false);
        var id = Resolve(db, tx, token);
        if (id is null) return new(AuthStatus.Unauthorized);
        using var cmd = Command(db, tx, "SELECT username FROM accounts WHERE id=$id", ("$id", id));
        var username = (string)cmd.ExecuteScalar()!;
        var retry = RetryAfter(db, tx, username);
        if (retry > 0) { tx.Commit(); return new(AuthStatus.Throttled, RetryAfterSeconds: retry); }
        if (Verify(db, tx, username, currentPassword) is null)
        { Fail(db, tx, username); tx.Commit(); return new(AuthStatus.Unauthorized); }
        var code = NewRecoveryCode();
        SaveRecoveryCode(db, tx, id, code);
        ResetFailures(db, tx, username);
        ResetRecoveryFailures(db, tx, username);
        tx.Commit(); return new(AuthStatus.Success, code);
    }
}
