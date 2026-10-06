using System;
using System.Linq;
using PokeChess.Core.Random;
namespace PokeChess.Core.Match
{
    public enum EliminationReason { Damage, Surrender }
    public sealed class PlayerElimination
    {
        public string PlayerId { get; }
        public int Round { get; }
        public int Placement { get; }
        public EliminationReason Reason { get; }
        public int RawHPAfter { get; }
        public int Damage { get; }
        public ulong TieBreak { get; }
        internal PlayerElimination(PlayerState p,int round,int placement,EliminationReason reason,int raw,int damage,ulong tie)
        {PlayerId=p.PlayerId;Round=round;Placement=placement;Reason=reason;RawHPAfter=raw;Damage=damage;TieBreak=tie;}
    }
    public sealed class EliminationSystem
    {
        internal static void ValidateRelease(MatchState match,PlayerState p)
        {
            match.Pool.ValidateRelease(p.Units);checked {var revision=p.Shop.Revision+1;}
            p.ValidatePlacementChanges(p.Units.Count);
        }
        private static ulong Tie(MatchState match,PlayerState p)
        {
            ulong hash=14695981039346656037UL;foreach(char c in p.PlayerId)hash=unchecked((hash^c)*1099511628211UL);
            return new DeterministicRandom(match.MatchSeed^hash^0x454C494D54494501UL).NextUInt64();
        }
        public void SettleDamage(MatchState match)
        {
            if(match==null)throw new ArgumentNullException(nameof(match));
            if(match.Phase!=MatchPhase.Result)throw new InvalidOperationException("Damage elimination requires Result.");
            var candidates=match.Players.Where(p=>p.HP==0 && !p.IsEliminated).ToArray();
            foreach(var p in candidates)ValidateRelease(match,p);
            foreach(var p in candidates.OrderBy(p=>p.LastDamage?.RawHPAfter??0).ThenByDescending(p=>p.LastDamage?.TotalDamage??0).ThenBy(p=>Tie(match,p)).ThenBy(p=>p.PlayerId,StringComparer.Ordinal))
                Commit(match,p,EliminationReason.Damage,p.LastDamage?.RawHPAfter??0,p.LastDamage?.TotalDamage??0);
        }
        internal void Commit(MatchState match,PlayerState p,EliminationReason reason,int raw,int damage)
        {
            if(p.IsEliminated)return;
            int place=match.Players.Count(x=>!x.IsEliminated);
            var record=new PlayerElimination(p,match.RoundNumber,place,reason,raw,damage,Tie(match,p));
            p.SetHP(0);SharedPoolSystem.ReleasePlayerHoldings(match,p.PlayerId);p.RecordElimination(record);
        }
    }
}
