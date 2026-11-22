using Microsoft.Data.Sqlite;
public static class OperationsTests {
    public static async Task Run(string root) {
        static void Check(bool value,string message) { if(!value) throw new Exception(message); }
        var clock=new TestClock(); var path=Path.Combine(root,"operations.db");
        var store=new AccountStore(path,clock); const string password="operations-password-123";
        store.Register("operations",password); var login=store.Login("operations",password);
        var project=Guid.NewGuid().ToString("D"); var environment=Guid.NewGuid().ToString("D");
        Check(store.BindUgs(login.Token!,new UgsCredentials { AccountId=login.AccountId!,PlayerId="operations-player",ProjectId=project,EnvironmentId=environment })==UgsBindingStatus.Success,"Bind before backup");
        store.ReserveUgsSignIn(login.Token!);
        store.Login("operations","wrong-password-long-enough");
        var nonce=Convert.ToBase64String(new byte[32]);
        var ticket=store.IssueAdmission(login.Token!,project,environment,"operations-room",nonce);
        Check(ticket!=null,"Admission exists before cleanup");
        var backup=Path.Combine(root,"verified-backup.db"); store.BackupTo(backup);
        var restored=new AccountStore(backup,clock);
        Check(restored.Resolve(login.Token!)==login.AccountId,"Backup restores active session after restart");
        Check(restored.IsActiveUgsPlayer(login.Token!,project,environment,"operations-player"),"Backup preserves canonical UGS binding");
        Check(restored.Login("operations",password).Status==AuthStatus.Success,"Backup restores password");
        Check(restored.RotateRecoveryCode(login.Token!,password).Status==AuthStatus.Success,"Backup restores recovery state");
        bool refused=false; try {store.BackupTo(backup);} catch(IOException){refused=true;}
        Check(refused,"Backup cannot overwrite existing file");
        clock.Advance(TimeSpan.FromSeconds(60)); store.CleanupExpired();
        Check(store.Resolve(login.Token!)!=null,"Cleanup preserves active sessions");
        Check(!store.VerifyAdmission(login.Token!,project,environment,ticket!,"operations-room",nonce,"operations-player"),"Exact ticket expiry cleaned");
        clock.Advance(TimeSpan.FromDays(7));
        await Task.WhenAll(Enumerable.Range(0,4).Select(_=>Task.Run(()=>new AccountStore(path,clock).CleanupExpired())));
        Check(store.Resolve(login.Token!)==null,"Expired session rejected after restart/cleanup");
        using(var db=new SqliteConnection("Data Source="+path)) { db.Open(); using var cmd=db.CreateCommand(); cmd.CommandText="SELECT COUNT(*) FROM sessions"; Check((long)cmd.ExecuteScalar()! == 0,"Expired sessions removed"); }
        Check(store.Login("operations",password).Status==AuthStatus.Success,"Cleanup preserves accounts");
        Console.WriteLine("PASS: backup/integrity/restore/restart, overwrite protection, concurrent expiry cleanup.");
    }
}
