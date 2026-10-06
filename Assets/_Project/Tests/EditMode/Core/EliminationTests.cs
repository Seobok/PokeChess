using System;
using System.Linq;
using NUnit.Framework;
using PokeChess.Core.Match;
using PokeChess.Core.Battle;
using PokeChess.Core.Pokemon;
namespace PokeChess.Core.Tests
{
    public sealed class EliminationTests
    {
        private MatchState m;private PokemonCatalog catalog;private LocalRoundCoordinator loop;
        private void Setup(int count=4)
        {
            catalog=new PokemonCatalog(new[]{new PokemonDefinition("t","T",1,new PokemonStats(100,10,0,1,0,0,1,2,0,0,.25f),"r","s")});
            m=MatchStateFactory.CreateWithPool("elim",Enumerable.Range(1,count).Select(i=>"p"+i),catalog,new[]{"t"},matchSeed:123);
            m.TransitionTo(MatchPhase.Starting);m.TransitionTo(MatchPhase.Preparation);
            foreach(var p in m.Players)new ShopSystem().RefreshForRound(m,p.PlayerId);loop=new LocalRoundCoordinator(m,catalog);
        }
        private void Add(string id,UnitRank rank=UnitRank.One)=>SharedPoolSystem.RegisterUnit(m,catalog.CreateUnit("u-"+id,"t",id,rank,0,UnitPlacement.OnBoard(new BoardPosition(0,0))),"t");
        private void Complete(){for(int i=0;i<1400 && m.Phase==MatchPhase.Combat;i++)loop.Step();Assert.That(loop.IsSettled,Is.True);}
        [Test] public void DamageBatchRanksRawHpThenLowerDamageAndReleasesOnce()
        {
            Setup(4);Add("p1",UnitRank.Two);Add("p2");Add("p3");m.TransitionTo(MatchPhase.Combat);m.TransitionTo(MatchPhase.Result);
            m.GetPlayer("p1").SetHP(2);m.GetPlayer("p2").SetHP(1);m.GetPlayer("p3").SetHP(2);var d=new PlayerDamageSystem();
            d.Apply(m.GetPlayer("p1"),1,RoundOutcome.Loss,2); // -2, damage 4
            d.Apply(m.GetPlayer("p2"),1,RoundOutcome.Loss,1); // -2, damage 3
            d.Apply(m.GetPlayer("p3"),1,RoundOutcome.Loss,3); // -3, damage 5
            var sys=new EliminationSystem();sys.SettleDamage(m);sys.SettleDamage(m);
            Assert.That(m.GetPlayer("p3").FinalPlacement,Is.EqualTo(4));Assert.That(m.GetPlayer("p1").FinalPlacement,Is.EqualTo(3));Assert.That(m.GetPlayer("p2").FinalPlacement,Is.EqualTo(2));
            Assert.That(m.GetPlayer("p4").FinalPlacement,Is.Null);Assert.That(m.Players.Take(3).All(p=>p.Units.Count==0 && p.IsEliminated),Is.True);m.Pool.AssertConservation(m);
        }
        [Test] public void ExactTiesHaveStableUniquePlacements()
        {
            int[] first=null;for(int run=0;run<2;run++)
            {Setup();m.TransitionTo(MatchPhase.Combat);m.TransitionTo(MatchPhase.Result);foreach(var p in m.Players){p.SetHP(1);new PlayerDamageSystem().Apply(p,1,RoundOutcome.Draw,1);}new EliminationSystem().SettleDamage(m);var ranks=m.Players.Select(p=>p.FinalPlacement.Value).ToArray();Assert.That(ranks.Distinct().Count(),Is.EqualTo(4));if(first==null)first=ranks;else Assert.That(ranks,Is.EqualTo(first));}
        }
        [Test] public void PreparationEvenRosterUsesSurrenderBoardAndPreservesOtherPairs()
        {
            Setup(4);foreach(var p in m.Players)Add(p.PlayerId);var prior=m.PairingPlan;var removed=prior.Pairs[0].PlayerOneId;var other=prior.Pairs[0].PlayerTwoId;var kept=prior.Pairs[1];
            var result=loop.Surrender(removed,1,MatchPhase.Preparation);Assert.That(result.Accepted,Is.True);Assert.That(result.Elimination.Placement,Is.EqualTo(4));
            Assert.That(m.PairingPlan.Pairs.Single(),Is.SameAs(kept));Assert.That(m.PairingPlan.ShadowPair.PlayerOneId,Is.EqualTo(other));Assert.That(m.PairingPlan.FrozenShadow.Units.Count,Is.EqualTo(1));
            Assert.That(m.GetPlayer(removed).Units,Is.Empty);loop.StartCombat(1);Complete();Assert.That(loop.Battles.Single(b=>b.IsShadow).ShadowSnapshot.SourcePlayerId,Is.EqualTo(removed));m.Pool.AssertConservation(m);
        }
        [Test] public void PreparationOddRosterConnectsOrphanWithShadowTarget()
        {
            Setup(5);var prior=m.PairingPlan;var removed=prior.Pairs[0].PlayerOneId;var orphan=prior.Pairs[0].PlayerTwoId;
            loop.Surrender(removed,1,MatchPhase.Preparation);Assert.That(m.PairingPlan.ShadowPair,Is.Null);Assert.That(m.PairingPlan.Pairs.Any(p=>p.Contains(orphan)&&p.Contains(prior.UnpairedPlayerId)),Is.True);
        }
        [TestCase(3)][TestCase(4)][TestCase(5)][TestCase(6)][TestCase(7)][TestCase(8)]
        public void MultiplePreparationSurrendersKeepEverySurvivorExactlyOnce(int count)
        {
            Setup(count);foreach(var p in m.Players)Add(p.PlayerId);
            for(int i=1;i<count;i++)
            {Assert.That(loop.Surrender("p"+i,1,MatchPhase.Preparation).Accepted,Is.True);var plan=m.PairingPlan;var ids=plan.Pairs.SelectMany(p=>p.ParticipantIds).Concat(plan.ShadowPair?.ParticipantIds??Array.Empty<string>()).ToArray();if(plan.SurvivorIds.Count>1)Assert.That(ids.OrderBy(id=>id),Is.EqualTo(plan.SurvivorIds));Assert.That(plan.Pairs.Count(p=>p.IsShadow),Is.Zero);m.Pool.AssertConservation(m);}
            Assert.That(m.PairingPlan.NoBattleRequired,Is.True);Assert.That(loop.StartCombat(1),Is.False);
        }
        [Test] public void CombatSurrenderForcesDeathWithoutDamageAndDoesNotKillShadowClone()
        {
            Setup(3);foreach(var p in m.Players)Add(p.PlayerId);var source=m.PairingPlan.ShadowPair.PlayerTwoId;loop.StartCombat(1);var normal=loop.GetBattleFor(source);var shadow=loop.Battles.Single(b=>b.IsShadow);var clone=shadow.Battle.Units.Single(u=>u.OwnerPlayerId==source);
            int gold=m.GetPlayer(source).Gold;Assert.That(loop.Surrender(source,1,MatchPhase.Combat).Accepted,Is.True);
            Assert.That(normal.Reason,Is.EqualTo(BattleEndReason.Surrender));Assert.That(normal.Battle.LifecycleEventsThisTick.Any(e=>e.Kind==BattleLifecycleEventKind.UnitDied && e.DeathReason==DeathReason.Surrender),Is.True);
            Assert.That(normal.Battle.DamageResultsThisTick,Is.Empty);Assert.That(clone.IsAlive,Is.True);Complete();
            Assert.That(m.GetPlayer(source).Gold,Is.EqualTo(gold));Assert.That(loop.LastResult.Damage.ContainsKey(source),Is.False);Assert.That(loop.LastResult.Income.ContainsKey(source),Is.False);
            var opponent=normal.Pairing.OpponentOf(source);Assert.That(loop.LastResult.PlayerResults[opponent].Outcome,Is.EqualTo(RoundOutcome.Win));m.Pool.AssertConservation(m);
        }
        [Test] public void ResultSurrenderPreservesFinalBattleAndSettlement()
        {
            Setup();loop.StartCombat(1);var result=loop.LastResult;var outcome=result.PlayerResults["p1"];var damage=result.Damage["p1"];int gold=m.GetPlayer("p1").Gold;
            Assert.That(loop.Surrender("p1",1,MatchPhase.Result).Accepted,Is.True);loop.SettleResult();Assert.That(loop.LastResult,Is.SameAs(result));Assert.That(result.PlayerResults["p1"],Is.SameAs(outcome));Assert.That(result.Damage["p1"],Is.SameAs(damage));Assert.That(m.GetPlayer("p1").Gold,Is.EqualTo(gold));
        }
        [Test] public void SurrenderReplayIsIdempotentAndStaleCommandRejected()
        {
            Setup();Assert.That(loop.Surrender("p1",2,MatchPhase.Preparation).Accepted,Is.False);var a=loop.Surrender("p1",1,MatchPhase.Preparation);var b=loop.Surrender("p1",1,MatchPhase.Preparation);
            Assert.That(b.AlreadySurrendered,Is.True);Assert.That(a.Elimination,Is.SameAs(b.Elimination));Assert.That(loop.Surrender("missing",1,MatchPhase.Preparation).Accepted,Is.False);m.Pool.AssertConservation(m);
        }
        [Test] public void EliminationPreflightFailureLeavesAllPayoutsAndHpUntouchedUntilRetry()
        {
            Setup();var p=m.GetPlayer("p1");p.SetHP(1);var pair=m.PairingPlan.Pairs.Single(x=>x.Contains("p1"));Add(pair.OpponentOf("p1"));
            long revision=p.Shop.Revision;typeof(ShopState).GetProperty("Revision").SetValue(p.Shop,long.MaxValue);
            Assert.Throws<OverflowException>(()=>loop.StartCombat(1));Assert.That(p.HP,Is.EqualTo(1));Assert.That(m.Players.All(x=>x.LastEconomyRound==0),Is.True);
            typeof(ShopState).GetProperty("Revision").SetValue(p.Shop,revision);loop.SettleResult();Assert.That(p.IsEliminated,Is.True);Assert.That(p.FinalPlacement,Is.EqualTo(4));
            loop.SettleResult();Assert.That(p.FinalPlacement,Is.EqualTo(4));m.Pool.AssertConservation(m);
        }
        [Test] public void LaterSurrenderReceivesHigherPlacementAndCannotReturnToMatch()
        {
            Setup();loop.Surrender("p1",1,MatchPhase.Preparation);loop.Surrender("p2",1,MatchPhase.Preparation);
            Assert.That(m.GetPlayer("p1").FinalPlacement,Is.EqualTo(4));Assert.That(m.GetPlayer("p2").FinalPlacement,Is.EqualTo(3));
            Assert.Throws<InvalidOperationException>(()=>m.GetPlayer("p1").SetHP(60));Assert.That(m.GetPlayer("p3").FinalPlacement,Is.Null);
        }
        [Test] public void ReleaseOverflowRejectsSurrenderWithoutMutation()
        {
            Setup();Add("p1");var p=m.GetPlayer("p1");typeof(ShopState).GetProperty("Revision").SetValue(p.Shop,long.MaxValue);
            Assert.Throws<OverflowException>(()=>loop.Surrender("p1",1,MatchPhase.Preparation));Assert.That(p.HP,Is.EqualTo(60));Assert.That(p.IsEliminated,Is.False);Assert.That(p.Units.Count,Is.EqualTo(1));m.Pool.AssertConservation(m);
        }
    }
}

