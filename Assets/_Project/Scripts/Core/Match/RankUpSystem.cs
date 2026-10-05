using System;
using System.Collections.Generic;
using System.Linq;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Match
{
    public sealed class UnitRankedUp
    {
        public string ResultUnitId { get; }
        public UnitRank PreviousRank { get; }
        public UnitRank Rank { get; }
        public UnitPlacement Placement { get; }
        public IReadOnlyList<string> ConsumedUnitIds { get; }
        public IReadOnlyList<string> ReturnedItemIds { get; }
        internal UnitRankedUp(RankNode survivor,RankNode[] consumed)
        {
            ResultUnitId=survivor.Unit.InstanceId;PreviousRank=survivor.Rank;Rank=(UnitRank)((int)survivor.Rank+1);Placement=survivor.Placement;
            ConsumedUnitIds=Array.AsReadOnly(consumed.Select(n=>n.Unit.InstanceId).ToArray());
            ReturnedItemIds=Array.AsReadOnly(consumed.SelectMany(n=>n.Unit.ItemInstanceIds).ToArray());
        }
    }
    public sealed class PurchaseResult
    {
        // The final survivor of the purchased copy, which may be an existing unit.
        public UnitInstance Unit { get; }
        public IReadOnlyList<UnitRankedUp> RankUps { get; }
        internal PurchaseResult(UnitInstance unit,IReadOnlyList<UnitRankedUp> rankUps) { Unit=unit;RankUps=rankUps; }
    }
    internal sealed class RankNode
    {
        internal readonly UnitInstance Unit;
        internal UnitRank Rank;
        internal UnitPlacement Placement;
        internal readonly List<UnitInstance> Originals;
        internal RankNode(UnitInstance unit) { Unit=unit;Rank=unit.Rank;Placement=unit.Placement;Originals=new List<UnitInstance>{unit}; }
    }
    internal sealed class RankUpPlan
    {
        internal readonly UnitInstance[] Before;
        internal readonly UnitInstance Added;
        internal readonly RankNode[] Final;
        internal readonly IReadOnlyList<UnitRankedUp> Events;
        internal bool Changed => Added!=null || Events.Count>0;
        internal UnitInstance PurchaseSurvivor => Final.Single(n=>n.Originals.Contains(Added)).Unit;
        private RankUpPlan(UnitInstance[] before,UnitInstance added,RankNode[] final,List<UnitRankedUp> events)
        { Before=before;Added=added;Final=final;Events=events.AsReadOnly(); }
        internal static RankUpPlan Build(PlayerState player,PokemonCatalog catalog,UnitInstance added=null)
        {
            var before=player.Units.ToArray();var nodes=before.Select(u=>new RankNode(u)).ToList();
            if(added!=null) nodes.Add(new RankNode(added));
            var events=new List<UnitRankedUp>();
            foreach(var rank in new[]{UnitRank.One,UnitRank.Two})
            {
                var groups=nodes.Where(n=>n.Rank==rank && n.Placement.Kind!=PlacementKind.Unplaced)
                    .GroupBy(n=>new {n.Unit.DefinitionId,n.Unit.EvolutionStage})
                    .OrderBy(g=>g.Key.DefinitionId,StringComparer.Ordinal).ThenBy(g=>g.Key.EvolutionStage).ToArray();
                foreach(var group in groups)
                {
                    var candidates=group.OrderBy(n=>n.Placement.Kind==PlacementKind.Board ? 0 : 1)
                        .ThenBy(n=>n.Placement.Position?.Row ?? n.Placement.BenchSlot.Value)
                        .ThenBy(n=>n.Placement.Position?.Column ?? 0).ThenBy(n=>n.Unit.InstanceId,StringComparer.Ordinal).ToList();
                    while(candidates.Count>=3)
                    {
                        var survivor=candidates[0];var consumed=candidates.Skip(1).Take(2).ToArray();
                        events.Add(new UnitRankedUp(survivor,consumed));survivor.Rank=(UnitRank)((int)rank+1);
                        foreach(var node in consumed) { survivor.Originals.AddRange(node.Originals);nodes.Remove(node); }
                        candidates.RemoveRange(0,3);
                    }
                }
            }
            if(added!=null)
            {
                var newNode=nodes.FirstOrDefault(n=>ReferenceEquals(n.Unit,added));
                if(newNode!=null && newNode.Placement.BenchSlot==player.Rules.BenchCapacity)
                {
                    var used=new HashSet<int>(nodes.Where(n=>n!=newNode && n.Placement.Kind==PlacementKind.Bench).Select(n=>n.Placement.BenchSlot.Value));
                    int? free=Enumerable.Range(0,player.Rules.BenchCapacity).Where(i=>!used.Contains(i)).Select(i=>(int?)i).FirstOrDefault();
                    if(!free.HasValue) throw new InvalidOperationException("Bench is full after rank-up planning.");
                    newNode.Placement=UnitPlacement.OnBench(free.Value);
                }
            }
            foreach(var node in nodes.Where(n=>n.Rank!=n.Unit.Rank)) RankRules.Default.Apply(catalog.Get(node.Unit.DefinitionId).BaseStats,node.Rank);
            return new RankUpPlan(before,added,nodes.ToArray(),events);
        }
    }
    // Trusted host service; callers serialize commands and invoke after acquisitions/imports.
    public sealed class RankUpSystem
    {
        private readonly PokemonCatalog catalog;
        public RankUpSystem(PokemonCatalog catalog) { this.catalog=catalog ?? throw new ArgumentNullException(nameof(catalog)); }
        public IReadOnlyList<UnitRankedUp> Resolve(MatchState match,string playerId,long expectedPlacementRevision)
        {
            if(match==null) throw new ArgumentNullException(nameof(match));
            var player=match.GetPlayer(playerId);
            if(match.Phase!=MatchPhase.Preparation || player.IsEliminated || player.HP==0) throw new InvalidOperationException("Rank up requires a live player in Preparation.");
            if(player.PlacementRevision!=expectedPlacementRevision) throw new InvalidOperationException("Stale placement revision.");
            var plan=RankUpPlan.Build(player,catalog);if(!plan.Changed) return plan.Events;
            Commit(match,player,plan);return plan.Events;
        }
        internal static void Validate(MatchState match,PlayerState player,RankUpPlan plan,PoolHolding purchase=null)
        {
            player.ValidateRankUp(plan);match.Pool?.ValidateRankUp(plan,purchase);
            if(match.Pool==null && plan.Before.Any(u=>u.PoolOriginDefinitionId!=null)) throw new InvalidOperationException("Missing owning pool.");
        }
        internal static void Commit(MatchState match,PlayerState player,RankUpPlan plan,PoolHolding purchase=null)
        {
            long revision=player.ValidateRankUp(plan);
            var poolPlan=match.Pool?.ValidateRankUp(plan,purchase);
            if(match.Pool==null && plan.Before.Any(u=>u.PoolOriginDefinitionId!=null)) throw new InvalidOperationException("Missing owning pool.");
            player.ApplyRankUp(plan,revision);
            if(poolPlan!=null) match.Pool.ApplyRankUp(plan,poolPlan,purchase);
        }
    }
}
