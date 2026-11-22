using Microsoft.Data.Sqlite;
public sealed partial class AccountStore
{
    public int CleanupExpired() {
        using var db=Open(); using var tx=db.BeginTransaction(deferred:false); int count=0;
        foreach(var table in new[]{"sessions","auth_failures","recovery_failures","ugs_admissions","ugs_signin_limits"}) {
            using var cmd=Command(db,tx,$"DELETE FROM {table} WHERE expires <= $now",("$now",Now));
            count+=cmd.ExecuteNonQuery();
        }
        tx.Commit(); return count;
    }
    // SQLite backup includes committed WAL pages; copying a live DB file does not.
    public void BackupTo(string destination) {
        var full=Path.GetFullPath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        using(var reserve=new FileStream(full,FileMode.CreateNew,FileAccess.Write,FileShare.None)) { }
        using var source=Open();
        using var target=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=full }.ToString());
        target.Open(); source.BackupDatabase(target);
        using var check=target.CreateCommand(); check.CommandText="PRAGMA integrity_check";
        if((string?)check.ExecuteScalar()!="ok") throw new IOException("Backup integrity check failed.");
    }
}
public sealed class AccountMaintenance(AccountStore store,ILogger<AccountMaintenance> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using var timer=new PeriodicTimer(TimeSpan.FromMinutes(15));
        do {
            try { store.CleanupExpired(); }
            catch { logger.LogError("Account maintenance failed; retrying next interval."); }
        } while(await timer.WaitForNextTickAsync(stoppingToken));
    }
}
