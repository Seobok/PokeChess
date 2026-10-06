using System;
using System.Collections.Generic;
using System.Linq;
using PokeChess.Core.Battle;
namespace PokeChess.Core.Match
{
    public sealed class SurrenderCommandResult
    {
        public bool Accepted { get; }
        public bool AlreadySurrendered { get; }
        public string Message { get; }
        public PlayerElimination Elimination { get; }
        internal SurrenderCommandResult(bool accepted,string message,PlayerElimination elimination=null,bool repeated=false)
        {Accepted=accepted;Message=message;Elimination=elimination;AlreadySurrendered=repeated;}
    }
    public sealed partial class LocalRoundCoordinator
    {
        public SurrenderCommandResult Surrender(string playerId,int expectedRound,MatchPhase expectedPhase)
        {
            var player=Match.Players.FirstOrDefault(p=>p.PlayerId==playerId);
            if(player==null)return new SurrenderCommandResult(false,"Unknown player.");
            if(player.Elimination?.Reason==EliminationReason.Surrender)return new SurrenderCommandResult(true,"Already surrendered.",player.Elimination,true);
            if(Match.RoundNumber!=expectedRound || Match.Phase!=expectedPhase || (expectedPhase!=MatchPhase.Preparation && expectedPhase!=MatchPhase.Combat && expectedPhase!=MatchPhase.Result))
                return new SurrenderCommandResult(false,"Stale or unavailable surrender.");
            if(player.IsEliminated || player.HP==0)return new SurrenderCommandResult(false,"Player is eliminated.");
            // Result rewards/damage are immutable; a failed settlement must be retried first.
            if(expectedPhase==MatchPhase.Result) { SettleResult();if(player.IsEliminated)return new SurrenderCommandResult(false,"Player is eliminated."); }
            EliminationSystem.ValidateRelease(Match,player);
            var prior=Match.PairingPlan;
            var snapshot=expectedPhase==MatchPhase.Preparation ? ShadowBoardSnapshot.Capture(player,expectedRound,catalog) : null;
            elimination.Commit(Match,player,EliminationReason.Surrender,0,0);
            if(expectedPhase==MatchPhase.Preparation)RepairSurrenderPairings(prior,snapshot);
            else if(expectedPhase==MatchPhase.Combat)
            {
                var battle=GetBattleFor(playerId);battle?.Surrender(playerId);
                if(battles.All(b=>b.IsComplete))Finish();
            }
            Match.Pool.AssertConservation(Match);
            return new SurrenderCommandResult(true,"Surrendered.",player.Elimination);
        }
        private void RepairSurrenderPairings(RoundPairingPlan prior,ShadowBoardSnapshot snapshot)
        {
            var ids=Survivors.Select(p=>p.PlayerId).OrderBy(id=>id,StringComparer.Ordinal).ToArray();
            if(ids.Length<2) {Match.CommitPairings(new RoundPairingPlan(Match.RoundNumber,ids,Array.Empty<RoundPairing>(),ids.FirstOrDefault(),0,0));return;}
            var live=new HashSet<string>(ids,StringComparer.Ordinal);
            var pairs=(prior?.Pairs??Array.Empty<RoundPairing>()).Where(p=>live.Contains(p.PlayerOneId)&&live.Contains(p.PlayerTwoId)).ToList();
            var assigned=new HashSet<string>(pairs.SelectMany(p=>p.ParticipantIds),StringComparer.Ordinal);
            var remaining=ids.Where(id=>!assigned.Contains(id)).ToList();
            while(remaining.Count>1) {pairs.Add(new RoundPairing(remaining[0],remaining[1]));remaining.RemoveRange(0,2);}
            string target=remaining.FirstOrDefault();RoundPairing shadow=null;ShadowBoardSnapshot frozen=null;
            if(target!=null)
            {
                // Preserve a valid existing shadow when its target/source still survive.
                if(prior?.ShadowPair!=null && prior.ShadowPair.PlayerOneId==target && live.Contains(prior.ShadowPair.PlayerTwoId))shadow=prior.ShadowPair;
                else {frozen=snapshot;shadow=new RoundPairing(target,snapshot.SourcePlayerId,true);}
            }
            Match.CommitPairings(new RoundPairingPlan(Match.RoundNumber,ids,pairs.ToArray(),target,0,0,shadow,frozen));
        }
    }
}
