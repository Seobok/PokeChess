using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using Microsoft.Data.Sqlite;

public enum AuthStatus { Success, Unauthorized, Throttled }
public sealed record AuthResult(AuthStatus Status, string? AccountId = null, string? Token = null, int RetryAfterSeconds = 0);

public sealed partial class AccountStore
{
    public const int MaxSessions = 5;
    public const int MaxFailures = 5;
    public const int FailureWindowSeconds = 900;
    readonly string connectionString;
    readonly TimeProvider clock;
    public AccountStore(string path, TimeProvider? clock = null)
    {
        this.clock = clock ?? TimeProvider.System;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = path, ForeignKeys = true }.ToString();
        using var db = Open();
        using var command = db.CreateCommand();
        // Additive schema upgrade: existing accounts and sessions remain valid.
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS accounts(id TEXT PRIMARY KEY, username TEXT UNIQUE NOT NULL, salt BLOB NOT NULL, hash BLOB NOT NULL);
            CREATE TABLE IF NOT EXISTS sessions(hash TEXT PRIMARY KEY, account_id TEXT NOT NULL REFERENCES accounts(id), expires INTEGER NOT NULL);
            CREATE INDEX IF NOT EXISTS session_expiry ON sessions(expires);
            CREATE INDEX IF NOT EXISTS session_account ON sessions(account_id);
            CREATE TABLE IF NOT EXISTS auth_failures(username TEXT PRIMARY KEY, failures INTEGER NOT NULL, expires INTEGER NOT NULL);
            CREATE INDEX IF NOT EXISTS failure_expiry ON auth_failures(expires);
            CREATE TABLE IF NOT EXISTS recovery_codes(account_id TEXT PRIMARY KEY REFERENCES accounts(id), hash BLOB NOT NULL);
            CREATE TABLE IF NOT EXISTS recovery_failures(username TEXT PRIMARY KEY, failures INTEGER NOT NULL, expires INTEGER NOT NULL);
            CREATE INDEX IF NOT EXISTS recovery_failure_expiry ON recovery_failures(expires);
            CREATE TABLE IF NOT EXISTS ugs_bindings(account_id TEXT NOT NULL REFERENCES accounts(id),project_id TEXT NOT NULL,environment_id TEXT NOT NULL,player_id TEXT NOT NULL,
                PRIMARY KEY(account_id,project_id,environment_id),UNIQUE(project_id,environment_id,player_id));
            CREATE TABLE IF NOT EXISTS ugs_admissions(hash TEXT PRIMARY KEY,session_hash TEXT NOT NULL REFERENCES sessions(hash) ON DELETE CASCADE,
                project_id TEXT NOT NULL,environment_id TEXT NOT NULL,player_id TEXT NOT NULL,room_id TEXT NOT NULL,nonce TEXT NOT NULL,expires INTEGER NOT NULL,consumed INTEGER NOT NULL);
            CREATE INDEX IF NOT EXISTS admission_expiry ON ugs_admissions(expires);
            CREATE INDEX IF NOT EXISTS admission_session ON ugs_admissions(session_hash);
            CREATE TABLE IF NOT EXISTS ugs_signin_limits(account_id TEXT PRIMARY KEY REFERENCES accounts(id),attempts INTEGER NOT NULL,expires INTEGER NOT NULL);
            """;
        command.ExecuteNonQuery();
    }
    long Now => clock.GetUtcNow().ToUnixTimeSeconds();
    SqliteConnection Open() { var db = new SqliteConnection(connectionString); db.Open(); return db; }
    static SqliteCommand Command(SqliteConnection db, SqliteTransaction tx, string sql, params (string, object)[] values)
    {
        var cmd = db.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = sql;
        foreach (var (name, value) in values) cmd.Parameters.AddWithValue(name, value);
        return cmd;
    }
    static void Execute(SqliteConnection db, SqliteTransaction tx, string sql, params (string, object)[] values)
    { using var cmd = Command(db, tx, sql, values); cmd.ExecuteNonQuery(); }
    static byte[] Hash(string password, byte[] salt)
    {
        using var argon = new Argon2id(Encoding.UTF8.GetBytes(password)) { Salt = salt, MemorySize = 19456, Iterations = 2, DegreeOfParallelism = 1 };
        return argon.GetBytes(32);
    }
    public string? Register(string username, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16); var hash = Hash(password, salt);
        var id = Guid.NewGuid().ToString("N"); var recoveryCode = NewRecoveryCode();
        using var db = Open(); using var tx = db.BeginTransaction(deferred: false);
        try {
            Execute(db, tx, "INSERT INTO accounts VALUES($id,$name,$salt,$hash)",
                ("$id", id), ("$name", username), ("$salt", salt), ("$hash", hash));
            SaveRecoveryCode(db, tx, id, recoveryCode);
            tx.Commit(); return recoveryCode;
        } catch (SqliteException e) when (e.SqliteExtendedErrorCode == 2067) { return null; }
    }
    // Called inside the same write transaction as verification and session creation/change.
    // This prevents concurrent attempts from bypassing the counter or reviving an old password session.
    int RetryAfter(SqliteConnection db, SqliteTransaction tx, string username)
    {
        Execute(db, tx, "DELETE FROM auth_failures WHERE expires <= $now", ("$now", Now));
        using var cmd = Command(db, tx, "SELECT failures, expires FROM auth_failures WHERE username=$name", ("$name", username));
        using var reader = cmd.ExecuteReader();
        return reader.Read() && reader.GetInt32(0) >= MaxFailures ? (int)Math.Max(1, reader.GetInt64(1) - Now) : 0;
    }
    void Fail(SqliteConnection db, SqliteTransaction tx, string username) => Execute(db, tx, """
        INSERT INTO auth_failures VALUES($name,1,$expires)
        ON CONFLICT(username) DO UPDATE SET failures=failures+1
        """, ("$name", username), ("$expires", Now + FailureWindowSeconds));
    static void ResetFailures(SqliteConnection db, SqliteTransaction tx, string username) =>
        Execute(db, tx, "DELETE FROM auth_failures WHERE username=$name", ("$name", username));
    static string? Verify(SqliteConnection db, SqliteTransaction tx, string username, string password)
    {
        string? id = null; byte[] salt = new byte[16], expected = new byte[32];
        using (var cmd = Command(db, tx, "SELECT id,salt,hash FROM accounts WHERE username=$name", ("$name", username)))
        using (var reader = cmd.ExecuteReader())
            if (reader.Read()) { id = reader.GetString(0); salt = (byte[])reader[1]; expected = (byte[])reader[2]; }
        var valid = CryptographicOperations.FixedTimeEquals(Hash(password, salt), expected);
        return valid ? id : null;
    }
    static string TokenHash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public AuthResult Login(string username, string password)
    {
        using var db = Open(); using var tx = db.BeginTransaction(deferred: false);
        var retry = RetryAfter(db, tx, username);
        if (retry > 0) { tx.Commit(); return new(AuthStatus.Throttled, RetryAfterSeconds: retry); }
        var id = Verify(db, tx, username, password);
        if (id is null) { Fail(db, tx, username); tx.Commit(); return new(AuthStatus.Unauthorized); }
        ResetFailures(db, tx, username);
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        Execute(db, tx, "DELETE FROM sessions WHERE expires <= $now", ("$now", Now));
        Execute(db, tx, "INSERT INTO sessions VALUES($hash,$id,$expires)",
            ("$hash", TokenHash(token)), ("$id", id), ("$expires", Now + 604800));
        // rowid gives insertion order even for legacy sessions with identical expiry times.
        Execute(db, tx, """
            DELETE FROM sessions WHERE hash IN
            (SELECT hash FROM sessions WHERE account_id=$id ORDER BY rowid DESC LIMIT -1 OFFSET $max)
            """, ("$id", id), ("$max", MaxSessions));
        tx.Commit(); return new(AuthStatus.Success, id, token);
    }
    string? Resolve(SqliteConnection db, SqliteTransaction tx, string token)
    {
        if (token.Length != 64) return null;
        using var cmd = Command(db, tx, "SELECT account_id FROM sessions WHERE hash=$hash AND expires>$now",
            ("$hash", TokenHash(token)), ("$now", Now));
        return cmd.ExecuteScalar() as string;
    }
    public string? Resolve(string token)
    {
        using var db = Open(); using var tx = db.BeginTransaction();
        var id = Resolve(db, tx, token); tx.Commit(); return id;
    }
    public void Revoke(string token)
    {
        using var db = Open(); using var tx = db.BeginTransaction();
        Execute(db, tx, "DELETE FROM sessions WHERE hash=$hash", ("$hash", TokenHash(token))); tx.Commit();
    }
    public bool RevokeAll(string token)
    {
        using var db = Open(); using var tx = db.BeginTransaction(deferred: false);
        var id = Resolve(db, tx, token);
        if (id is null) return false;
        Execute(db, tx, "DELETE FROM sessions WHERE account_id=$id", ("$id", id)); tx.Commit(); return true;
    }
    public AuthResult ChangePassword(string token, string currentPassword, string newPassword)
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
        var salt = RandomNumberGenerator.GetBytes(16); var hash = Hash(newPassword, salt);
        Execute(db, tx, "UPDATE accounts SET salt=$salt,hash=$hash WHERE id=$id", ("$salt", salt), ("$hash", hash), ("$id", id));
        Execute(db, tx, "DELETE FROM sessions WHERE account_id=$id", ("$id", id));
        ResetFailures(db, tx, username);
        tx.Commit(); return new(AuthStatus.Success, id);
    }
}
