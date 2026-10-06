using System;
using System.Linq;
using NUnit.Framework;
using PokeChess.Core.Match;
using PokeChess.Core.Battle;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Tests
{
    public sealed class PlayerDamageTests
    {
        private PokemonCatalog catalog;private MatchState match;private LocalRoundCoordinator loop;
        private void Setup(int count=2,int hp=60,bool longBattle=false)
        {
            catalog=new PokemonCatalog(new[]{new PokemonDefinition("test","Test",1,new PokemonStats(longBattle ? 100000 : 100,longBattle ? 1 : 20,0,1,0,0,1,2,0,0,.25f),"role","skill")});
            match=MatchStateFactory.CreateWithPool("damage",Enumerable.Range(1,count).Select(i=>"p"+i),catalog,new[]{"test"},rules:new MatchRules(startingHP:hp),matchSeed:123);
            match.TransitionTo(MatchPhase.Starting);match.TransitionTo(MatchPhase.Preparation);
            foreach(var p in match.Players)new ShopSystem().RefreshForRound(match,p.PlayerId);
            loop=new LocalRoundCoordinator(match,catalog);
        }
        private UnitInstance Add(string owner,int slot=0,UnitRank rank=UnitRank.One)
            => SharedPoolSystem.RegisterUnit(match,catalog.CreateUnit("u-"+owner+"-"+slot,"test",owner,rank,0,UnitPlacement.OnBench(slot)),"test");
        private void Complete()
        {
            for(int i=0;i<1400 && match.Phase==MatchPhase.Combat;i++)loop.Step();
            Assert.That(match.Phase,Is.EqualTo(MatchPhase.Result));Assert.That(loop.IsSettled,Is.True);
        }
        [TestCase(1,2)][TestCase(4,2)][TestCase(5,4)][TestCase(8,4)][TestCase(9,6)]
        [TestCase(12,6)][TestCase(13,8)][TestCase(16,8)][TestCase(17,10)][TestCase(int.MaxValue,10)]
        public void LossUsesRoundBandAndSurvivors(int round,int basic)
        {
            var rules=new PlayerDamageRules();Assert.That(rules.BaseDamage(round),Is.EqualTo(basic));
            Assert.That(rules.Calculate(round,RoundOutcome.Loss,3),Is.EqualTo(basic+3));
        }
        [TestCase(1)][TestCase(10)][TestCase(17)]
        public void DrawHasOnlySurvivorDamageAndWinHasNone(int round)
        {
            var rules=new PlayerDamageRules();Assert.That(rules.Calculate(round,RoundOutcome.Draw,3),Is.EqualTo(3));
            Assert.That(rules.Calculate(round,RoundOutcome.Draw,0),Is.Zero);Assert.That(rules.Calculate(round,RoundOutcome.Win,3),Is.Zero);
        }
        [Test] public void InvalidInputsAndOverflowAreRejected()
        {
            var rules=new PlayerDamageRules();Assert.Throws<ArgumentOutOfRangeException>(()=>rules.Calculate(0,RoundOutcome.Draw,0));
            Assert.Throws<ArgumentOutOfRangeException>(()=>rules.Calculate(1,(RoundOutcome)99,0));
            Assert.Throws<ArgumentOutOfRangeException>(()=>rules.Calculate(1,RoundOutcome.Loss,-1));
            Assert.Throws<ArgumentOutOfRangeException>(()=>new PlayerDamageRules(-1));
            Assert.Throws<OverflowException>(()=>rules.Calculate(17,RoundOutcome.Loss,int.MaxValue));
        }
        [Test] public void PreviewIsPureAndOverkillPreservesRawFinalHp()
        {
            Setup(hp:5);var p=match.GetPlayer("p1");var service=new PlayerDamageSystem();var plan=service.Preview(p,1,RoundOutcome.Loss,8);
            Assert.That(p.HP,Is.EqualTo(5));Assert.That(p.LastDamageRound,Is.Zero);
            Assert.That(plan.TotalDamage,Is.EqualTo(10));Assert.That(plan.RawHPAfter,Is.EqualTo(-5));Assert.That(plan.HPAfter,Is.Zero);Assert.That(plan.AppliedDamage,Is.EqualTo(5));
            var applied=service.Apply(p,1,RoundOutcome.Loss,8);Assert.That(p.HP,Is.Zero);Assert.That(p.LastDamageRound,Is.EqualTo(1));Assert.That(p.LastDamage,Is.SameAs(applied));
            Assert.That(p.IsEliminated,Is.False);
        }
        [Test] public void ReplayIsIdempotentAndConflictingOrStaleRequestsAreRejected()
        {
            Setup();var p=match.GetPlayer("p1");var service=new PlayerDamageSystem();
            Assert.Throws<InvalidOperationException>(()=>service.Apply(p,2,RoundOutcome.Loss,1));
            var first=service.Apply(p,1,RoundOutcome.Loss,1);
            Assert.That(service.Apply(p,1,RoundOutcome.Loss,1),Is.SameAs(first));Assert.That(p.HP,Is.EqualTo(57));
            Assert.Throws<InvalidOperationException>(()=>service.Apply(p,1,RoundOutcome.Loss,2));
            service.Apply(p,2,RoundOutcome.Win,1);Assert.Throws<InvalidOperationException>(()=>service.Apply(p,1,RoundOutcome.Loss,1));
        }
        [Test] public void ZeroDamageRoundsStillRecordProcessing()
        {
            Setup();var p=match.GetPlayer("p1");var service=new PlayerDamageSystem();
            service.Apply(p,1,RoundOutcome.Win,2);service.Apply(p,2,RoundOutcome.Draw,0);
            Assert.That(p.HP,Is.EqualTo(60));Assert.That(p.LastDamageRound,Is.EqualTo(2));
        }
        [TestCase(false,false,0,0)][TestCase(true,false,0,3)][TestCase(false,true,3,0)]
        public void EmptyBoardResultsUseDeploymentSnapshotCounts(bool one,bool two,int firstDamage,int secondDamage)
        {
            Setup();if(one)Add("p1");if(two)Add("p2");loop.StartCombat(1);
            Assert.That(loop.LastResult.Damage["p1"].TotalDamage,Is.EqualTo(firstDamage));Assert.That(loop.LastResult.Damage["p2"].TotalDamage,Is.EqualTo(secondDamage));
            Assert.That(match.GetPlayer("p1").HP,Is.EqualTo(60-firstDamage));Assert.That(match.GetPlayer("p2").HP,Is.EqualTo(60-secondDamage));
        }
        [Test] public void TimeLimitDrawUsesOppositeTeamCountsIndependently()
        {
            Setup(longBattle:true);match.GetPlayer("p1").SetProgress(5,0,2);Add("p1");Add("p1",1);Add("p2");
            loop.StartCombat(1);Complete();Assert.That(loop.LastResult.Result,Is.EqualTo(BattleResult.Draw));
            Assert.That(loop.LastResult.Damage["p1"].BaseDamage,Is.Zero);Assert.That(loop.LastResult.Damage["p1"].TotalDamage,Is.EqualTo(1));
            Assert.That(loop.LastResult.Damage["p2"].BaseDamage,Is.Zero);Assert.That(loop.LastResult.Damage["p2"].TotalDamage,Is.EqualTo(2));
            Assert.That(match.GetPlayer("p1").HP,Is.EqualTo(59));Assert.That(match.GetPlayer("p2").HP,Is.EqualTo(58));
        }
        [Test] public void SimultaneousAnnihilationDrawHasZeroDamage()
        {
            Setup();Add("p1");Add("p2");loop.StartCombat(1);
            foreach(var unit in loop.Battle.Units)unit.SetVitals(0,0);Complete();
            Assert.That(loop.LastResult.Result,Is.EqualTo(BattleResult.Draw));Assert.That(loop.LastResult.Damage.Values.All(d=>d.TotalDamage==0),Is.True);
        }
        [Test] public void RankThreeIsOneSurvivorAndBenchDoesNotCount()
        {
            Setup();Add("p2",rank:UnitRank.Three);Add("p2",1);loop.StartCombat(1);
            var damage=loop.LastResult.Damage["p1"];Assert.That(damage.OpponentSurvivors,Is.EqualTo(1));Assert.That(damage.TotalDamage,Is.EqualTo(3));
        }
        [Test] public void LivingUnitsRemovedFromBoardDoNotCountAsSurvivors()
        {
            Setup();match.GetPlayer("p1").SetProgress(5,0,2);Add("p1");Add("p1",1);Add("p2");loop.StartCombat(1);
            var removed=loop.Battle.Units.First(u=>u.TeamId==1);loop.Battle.TryRemoveUnit(removed.UnitInstanceId);
            Assert.That(removed.IsAlive,Is.True);Assert.That(removed.IsOnBoard,Is.False);
            loop.Battle.Units.Single(u=>u.TeamId==2).SetVitals(0,0);Complete();
            Assert.That(loop.LastResult.Damage["p2"].OpponentSurvivors,Is.EqualTo(1));
            Assert.That(loop.LastResult.Damage["p2"].TotalDamage,Is.EqualTo(3));
        }
        [Test] public void CountsFreezeWhenEachBattleEndsAndHpWaitsForAllPairs()
        {
            Setup(4,longBattle:true);foreach(var p in match.Players)Add(p.PlayerId);loop.StartCombat(1);
            var first=loop.Battles[0];first.Battle.Units.Single(u=>u.TeamId==2).SetVitals(0,0);loop.Step();
            Assert.That(first.IsComplete,Is.True);Assert.That(first.OpponentSurvivorsOf(first.Pairing.PlayerTwoId),Is.EqualTo(1));
            first.Battle.Units.Single(u=>u.TeamId==1).SetVitals(0,0);
            Assert.That(match.Players.All(p=>p.HP==60 && p.LastDamageRound==0),Is.True);
            Complete();Assert.That(loop.LastResult.Damage[first.Pairing.PlayerTwoId].TotalDamage,Is.EqualTo(3));
        }
        [Test] public void ShadowLossDamagesOnlyTargetAndSourceUsesNormalVictory()
        {
            Setup(3);var pair=match.PairingPlan.ShadowPair;Add(pair.PlayerTwoId);loop.StartCombat(1);
            var target=loop.LastResult.Damage[pair.PlayerOneId];var source=loop.LastResult.Damage[pair.PlayerTwoId];
            Assert.That(target.TotalDamage,Is.EqualTo(3));Assert.That(source.TotalDamage,Is.Zero);
            Assert.That(match.GetPlayer(pair.PlayerOneId).HP,Is.EqualTo(57));Assert.That(match.GetPlayer(pair.PlayerTwoId).HP,Is.EqualTo(60));
            Assert.That(loop.LastResult.Damage.Count,Is.EqualTo(3));
        }
        [Test] public void ShadowDrawUsesCloneSurvivorsAndDoesNotDuplicateSourceDamage()
        {
            Setup(3,longBattle:true);foreach(var p in match.Players)Add(p.PlayerId);loop.StartCombat(1);Complete();
            var pair=match.PairingPlan.ShadowPair;Assert.That(loop.LastResult.PlayerResults[pair.PlayerOneId].Outcome,Is.EqualTo(RoundOutcome.Draw));
            Assert.That(loop.LastResult.Damage[pair.PlayerOneId].TotalDamage,Is.EqualTo(1));
            Assert.That(match.Players.All(p=>p.HP==59 && p.LastDamageRound==1),Is.True);Assert.That(loop.LastResult.Damage.Count,Is.EqualTo(3));
        }
        [Test] public void EconomyFailurePreventsAllHpChangesAndRetryAppliesOnce()
        {
            Setup();Add("p2");match.GetPlayer("p2").SetProgress(int.MaxValue,0,1);
            Assert.Throws<OverflowException>(()=>loop.StartCombat(1));Assert.That(match.Players.All(p=>p.HP==60 && p.LastDamageRound==0),Is.True);
            match.GetPlayer("p2").SetProgress(5,0,1);loop.SettleResult();Assert.That(match.GetPlayer("p1").HP,Is.EqualTo(57));
            loop.SettleResult();loop.Step();Assert.That(match.GetPlayer("p1").HP,Is.EqualTo(57));Assert.That(loop.LastResult.Damage.Count,Is.EqualTo(2));
        }
        [Test] public void InvalidDamageRoundPreventsAnyPayoutOrHpMutation()
        {
            Setup(4);var last=match.GetPlayer("p4");typeof(PlayerState).GetProperty("LastDamageRound").SetValue(last,2);
            Assert.Throws<InvalidOperationException>(()=>loop.StartCombat(1));
            Assert.That(match.Players.All(p=>p.LastEconomyRound==0 && p.HP==60),Is.True);
            typeof(PlayerState).GetProperty("LastDamageRound").SetValue(last,0);loop.SettleResult();Assert.That(loop.IsSettled,Is.True);
        }
        [TestCase(0)][TestCase(1)]
        public void ZeroOrOneSurvivorPausesPreparationWithoutTimerFault(int survivors)
        {
            Setup(hp:1);if(survivors==1)Add("p2");else foreach(var p in match.Players)Add(p.PlayerId);
            var flow=new RoundFlowController(loop);flow.RequestAdvance(1,MatchPhase.Preparation);
            if(survivors==0)
            {
                // Keep both alive through the time limit, producing one survivor damage each.
                for(int i=0;i<1350 && match.Phase==MatchPhase.Combat;i++)
                {
                    foreach(var unit in loop.Battle.Units)if(unit.IsAlive)unit.SetShield(100000);
                    loop.Step();
                }
            }
            Complete();Assert.That(loop.NextRound(1),Is.True);flow.RefreshState();
            Assert.That(flow.AwaitingMatchEnd,Is.True);Assert.That(flow.IsTimerRunning,Is.False);
            flow.AdvanceTime(100);Assert.That(flow.Fault,Is.Null);Assert.That(flow.RequestAdvance(2,MatchPhase.Preparation),Is.False);
            Assert.That(match.Players.Count(p=>p.HP>0),Is.EqualTo(survivors));
        }
        [Test] public void ZeroHpPlayersAreEliminatedAndExcludedFromNextPairing()
        {
            Setup(4,hp:2);foreach(var pair in match.PairingPlan.Pairs)Add(pair.PlayerOneId);
            var losers=match.PairingPlan.Pairs.Select(p=>p.PlayerTwoId).ToArray();loop.StartCombat(1);
            Assert.That(losers.All(id=>match.GetPlayer(id).HP==0),Is.True);Assert.That(loop.NextRound(1),Is.True);
            Assert.That(match.PairingPlan.SurvivorIds.Count,Is.EqualTo(2));Assert.That(match.PairingPlan.SurvivorIds.Intersect(losers),Is.Empty);
            Assert.That(losers.All(id=>match.GetPlayer(id).IsEliminated && match.GetPlayer(id).FinalPlacement.HasValue),Is.True);match.Pool.AssertConservation(match);
        }
    }
}


