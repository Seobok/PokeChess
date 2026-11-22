using System.Security.Cryptography;
using System.Text.RegularExpressions;

public enum UgsBindingStatus { Success,Unauthorized,Conflict }
public sealed partial class AccountStore
{
    public UgsBindingStatus BindUgs(string session,UgsCredentials identity)
    {
        using var db=Open(); using var tx=db.BeginTransaction(deferred:false);
        var id=Resolve(db,tx,session); if(id==null || id!=identity.AccountId) return UgsBindingStatus.Unauthorized;
        using(var existing=Command(db,tx,"SELECT player_id FROM ugs_bindings WHERE account_id=$id AND project_id=$project AND environment_id=$env",
            ("$id",id),("$project",identity.ProjectId),("$env",identity.EnvironmentId))) {
            var player=existing.ExecuteScalar() as string;
            if(player!=null) return player==identity.PlayerId?UgsBindingStatus.Success:UgsBindingStatus.Conflict;
        }
        try {
            Execute(db,tx,"INSERT INTO ugs_bindings VALUES($id,$project,$env,$player)",("$id",id),("$project",identity.ProjectId),("$env",identity.EnvironmentId),("$player",identity.PlayerId));
            tx.Commit(); return UgsBindingStatus.Success;
        } catch(Microsoft.Data.Sqlite.SqliteException error) when(error.SqliteErrorCode==19) { return UgsBindingStatus.Conflict; }
    }
    public AuthStatus ReserveUgsSignIn(string session)
    {
        using var db=Open(); using var tx=db.BeginTransaction(deferred:false);
        var id=Resolve(db,tx,session); if(id==null)return AuthStatus.Unauthorized;
        Execute(db,tx,"DELETE FROM ugs_signin_limits WHERE expires<=$now",("$now",Now));
        using(var query=Command(db,tx,"SELECT attempts FROM ugs_signin_limits WHERE account_id=$id",("$id",id))) {
            if(Convert.ToInt32(query.ExecuteScalar()??0)>=10)return AuthStatus.Throttled;
        }
        Execute(db,tx,"INSERT INTO ugs_signin_limits VALUES($id,1,$expires) ON CONFLICT(account_id) DO UPDATE SET attempts=attempts+1",("$id",id),("$expires",Now+60));
        tx.Commit(); return AuthStatus.Success;
    }
    string? BoundPlayer(Microsoft.Data.Sqlite.SqliteConnection db,Microsoft.Data.Sqlite.SqliteTransaction tx,string id,string project,string env)
    {
        using var cmd=Command(db,tx,"SELECT player_id FROM ugs_bindings WHERE account_id=$id AND project_id=$project AND environment_id=$env",("$id",id),("$project",project),("$env",env));
        return cmd.ExecuteScalar() as string;
    }
    public static bool ValidRoom(string? room)=>room!=null && Regex.IsMatch(room,"\\A[a-zA-Z0-9_-]{1,128}\\z");
    public static bool ValidNonce(string? nonce)
    { try { return nonce!=null && nonce.Length==44 && Convert.FromBase64String(nonce).Length==32; } catch(FormatException) { return false; } }
    public string? IssueAdmission(string session,string project,string environment,string room,string nonce)
    {
        if(!ValidRoom(room)||!ValidNonce(nonce)) return null;
        using var db=Open(); using var tx=db.BeginTransaction(deferred:false);
        var id=Resolve(db,tx,session); if(id==null) return null;
        var player=BoundPlayer(db,tx,id,project,environment); if(player==null) return null;
        Execute(db,tx,"DELETE FROM ugs_admissions WHERE expires<=$now",("$now",Now));
        var ticket=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        Execute(db,tx,"INSERT INTO ugs_admissions VALUES($hash,$session,$project,$env,$player,$room,$nonce,$expires,0)",
            ("$hash",TokenHash(ticket)),("$session",TokenHash(session)),("$project",project),("$env",environment),("$player",player),("$room",room),("$nonce",nonce),("$expires",Now+60));
        Execute(db,tx,"DELETE FROM ugs_admissions WHERE hash IN (SELECT hash FROM ugs_admissions WHERE session_hash=$session ORDER BY rowid DESC LIMIT -1 OFFSET 32)",("$session",TokenHash(session)));
        tx.Commit(); return ticket;
    }
    public bool VerifyAdmission(string verifierSession,string project,string environment,string ticket,string room,string nonce,string player)
    {
        if(ticket.Length!=64 || !ValidRoom(room)||!ValidNonce(nonce)) return false;
        using var db=Open(); using var tx=db.BeginTransaction(deferred:false);
        var verifier=Resolve(db,tx,verifierSession);
        if(verifier==null || BoundPlayer(db,tx,verifier,project,environment)==null) return false;
        using var cmd=Command(db,tx,"""
            UPDATE ugs_admissions SET consumed=1 WHERE hash=$hash AND project_id=$project AND environment_id=$env
              AND room_id=$room AND nonce=$nonce AND player_id=$player AND expires>$now AND consumed=0
              AND EXISTS(SELECT 1 FROM sessions s WHERE s.hash=ugs_admissions.session_hash AND s.expires>$now)
            """,("$hash",TokenHash(ticket)),("$project",project),("$env",environment),("$room",room),("$nonce",nonce),("$player",player),("$now",Now));
        bool valid=cmd.ExecuteNonQuery()==1; tx.Commit(); return valid;
    }
    public bool IsActiveUgsPlayer(string verifierSession,string project,string environment,string player)
    {
        using var db=Open(); using var tx=db.BeginTransaction();
        if(Resolve(db,tx,verifierSession)==null) return false;
        using var cmd=Command(db,tx,"""
            SELECT 1 FROM ugs_bindings b WHERE b.project_id=$project AND b.environment_id=$env AND b.player_id=$player
            AND EXISTS(SELECT 1 FROM sessions s WHERE s.account_id=b.account_id AND s.expires>$now)
            """,("$project",project),("$env",environment),("$player",player),("$now",Now));
        return cmd.ExecuteScalar()!=null;
    }
}
