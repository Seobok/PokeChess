using System;
using System.Collections.Generic;
using System.Linq;
namespace PokeChess.Core.Match
{
    public enum MatchEndReason { LastPlayerAlive, AllPlayersEliminated }
    public sealed class FinalPlayerResult
    {
        public string PlayerId { get; }
        public int Placement { get; }
        public int HP { get; }
        public bool IsWinner { get; }
        public bool WasAlive { get; }
        public int? EliminationRound { get; }
        public EliminationReason? EliminationReason { get; }
        public int? RawHPAfter { get; }
        public int Damage { get; }
        internal FinalPlayerResult(PlayerState player,int place,string winner)
        {
            PlayerId=player.PlayerId;Placement=place;HP=player.HP;IsWinner=PlayerId==winner;
            WasAlive=player.HP>0 && !player.IsEliminated;EliminationRound=player.Elimination?.Round;
            EliminationReason=player.Elimination?.Reason;RawHPAfter=player.Elimination?.RawHPAfter;
            Damage=player.Elimination?.Damage??0;
        }
    }
    public sealed class MatchFinalResult
    {
        public string MatchId { get; }
        public int EndRound { get; }
        public MatchEndReason Reason { get; }
        public string WinnerPlayerId { get; }
        public IReadOnlyList<FinalPlayerResult> Standings { get; }
        internal MatchFinalResult(MatchState match,PlayerState survivor)
        {
            MatchId=match.MatchId;EndRound=match.RoundNumber;
            Reason=survivor!=null ? MatchEndReason.LastPlayerAlive : MatchEndReason.AllPlayersEliminated;
            var rows=match.Players.Select(p=>new {Player=p,Place=p==survivor ? 1 : p.FinalPlacement??throw new InvalidOperationException("Missing final placement.")}).OrderBy(p=>p.Place).ToArray();
            if(!rows.Select(p=>p.Place).SequenceEqual(Enumerable.Range(1,rows.Length)))throw new InvalidOperationException("Invalid final standings.");
            WinnerPlayerId=rows[0].Player.PlayerId;
            Standings=Array.AsReadOnly(rows.Select(p=>new FinalPlayerResult(p.Player,p.Place,WinnerPlayerId)).ToArray());
        }
    }
    public sealed partial class LocalRoundCoordinator
    {
        // Only a fully settled Result or a Preparation surrender can conclude a match.
        public bool TryFinishMatch(int expectedRound)
        {
            if(expectedRound!=Match.RoundNumber)return false;
            if(Match.Phase==MatchPhase.Finished)return Match.FinalResult!=null;
            if(Match.Phase!=MatchPhase.Preparation && Match.Phase!=MatchPhase.Result)return false;
            if(Match.Phase==MatchPhase.Result && !IsSettled)return false;
            var alive=Survivors.ToArray();
            if(alive.Length>1 || Match.Players.Any(p=>p.HP==0 && (!p.IsEliminated || p.Elimination==null)))return false;
            var result=new MatchFinalResult(Match,alive.SingleOrDefault());
            Match.CommitFinalResult(result);refreshing=false;return true;
        }
    }
}
