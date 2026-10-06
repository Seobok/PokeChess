using System;
using System.Linq;
using NUnit.Framework;
using PokeChess.Core.Match;
using PokeChess.Core.Battle;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Tests
{
    public sealed class MultiPlayerRoundTests
    {
        private PokemonCatalog catalog;
        private MatchState match;
        private LocalRoundCoordinator loop;
        private void Setup(int count,bool longBattle=false,int startingHP=60)
        {
            catalog=new PokemonCatalog(new[]{new PokemonDefinition("test","Test",1,new PokemonStats(longBattle ? 100000 : 100,longBattle ? 1 : 20,0,1,0,0,1,2,0,0,.25f),"role","skill")});
            match=MatchStateFactory.CreateWithPool("multi",Enumerable.Range(1,count).Select(i=>"p"+i),catalog,new[]{"test"},rules:new MatchRules(startingHP:startingHP),matchSeed:123);
            match.TransitionTo(MatchPhase.Starting);match.TransitionTo(MatchPhase.Preparation);
            foreach(var p in match.Players)new ShopSystem().RefreshForRound(match,p.PlayerId);
            loop=new LocalRoundCoordinator(match,catalog);
        }
        private void Add(string id)
        { SharedPoolSystem.RegisterUnit(match,catalog.CreateUnit("unit-"+id,"test",id,UnitRank.One,0,UnitPlacement.OnBench(0)),"test"); }
        private void Complete()
        {
            for(int tick=0;tick<1400 && match.Phase==MatchPhase.Combat;tick++)loop.Step();
            Assert.That(match.Phase,Is.EqualTo(MatchPhase.Result));Assert.That(loop.IsSettled,Is.True);
        }
        [TestCase(4)][TestCase(6)][TestCase(8)]
        public void TenAutomaticRoundsRunEveryPairAndSettleEveryPlayerOnce(int count)
        {
            Setup(count,startingHP:1000);foreach(var p in match.Players)Add(p.PlayerId);
            var flow=new RoundFlowController(loop,new RoundFlowRules(.1,.1));
            for(int round=1;round<=10;round++)
            {
                Assert.That(match.RoundNumber,Is.EqualTo(round));
                flow.AdvanceTime(.1);Assert.That(match.Phase,Is.EqualTo(MatchPhase.Combat));
                Assert.That(loop.Battles.Count,Is.EqualTo(count/2));
                Assert.That(loop.Battles.Select(b=>b.Battle.BattleSeed).Distinct().Count(),Is.EqualTo(count/2));
                Assert.That(loop.Battles.All(b=>b.Battle.Units.Count==2),Is.True);
                for(int frame=0;frame<1500 && match.Phase==MatchPhase.Combat;frame++)flow.AdvanceTime(1d/30);
                Assert.That(flow.Fault,Is.Null);Assert.That(loop.IsSettled,Is.True);
                Assert.That(loop.LastResult.PlayerResults.Count,Is.EqualTo(count));Assert.That(loop.LastResult.Income.Count,Is.EqualTo(count));Assert.That(loop.LastResult.XP.Count,Is.EqualTo(count));
                var gold=match.Players.Select(p=>p.Gold).ToArray();loop.SettleResult();
                Assert.That(match.Players.Select(p=>p.Gold),Is.EqualTo(gold));
                Assert.That(match.Players.All(p=>p.LastEconomyRound==round && p.LastAutomaticXPRound==round && p.DeployedUnitCount==1),Is.True);
                match.Pool.AssertConservation(match);
                flow.AdvanceTime(.2);
                Assert.That(match.Phase,Is.EqualTo(MatchPhase.Preparation));Assert.That(match.RoundNumber,Is.EqualTo(round+1));
            }
        }
        [Test] public void CompletedPairWaitsForLongPairWithoutEarlySettlement()
        {
            Setup(4,true);var plan=match.PairingPlan;Add(plan.Pairs[1].PlayerOneId);Add(plan.Pairs[1].PlayerTwoId);
            Assert.That(loop.StartCombat(1),Is.True);
            Assert.That(loop.Battles[0].IsComplete,Is.True);Assert.That(loop.Battles[1].IsComplete,Is.False);
            for(int i=0;i<100;i++)loop.Step();
            Assert.That(match.Phase,Is.EqualTo(MatchPhase.Combat));Assert.That(loop.LastResult,Is.Null);
            Assert.That(match.Players.All(p=>p.LastEconomyRound==0 && p.LastAutomaticXPRound==0 && p.Gold==5),Is.True);
            Complete();Assert.That(loop.Battles[0].EndTick,Is.Zero);Assert.That(loop.Battles[1].EndTick,Is.EqualTo(1350));
        }
        [Test] public void MixedEmptyBoardOutcomesAreMappedToCorrectPlayers()
        {
            Setup(4);var plan=match.PairingPlan;Add(plan.Pairs[0].PlayerTwoId);Add(plan.Pairs[1].PlayerOneId);
            loop.StartCombat(1);Assert.That(loop.IsSettled,Is.True);
            Assert.That(loop.LastResult.PlayerResults[plan.Pairs[0].PlayerOneId].Outcome,Is.EqualTo(RoundOutcome.Loss));
            Assert.That(loop.LastResult.PlayerResults[plan.Pairs[0].PlayerTwoId].Outcome,Is.EqualTo(RoundOutcome.Win));
            Assert.That(loop.LastResult.PlayerResults[plan.Pairs[1].PlayerOneId].Outcome,Is.EqualTo(RoundOutcome.Win));
            Assert.That(loop.LastResult.PlayerResults[plan.Pairs[1].PlayerTwoId].Outcome,Is.EqualTo(RoundOutcome.Loss));
            foreach(var p in match.Players)Assert.That(loop.GetBattleFor(p.PlayerId).Pairing.OpponentOf(p.PlayerId),Is.EqualTo(loop.LastResult.PlayerResults[p.PlayerId].OpponentId));
        }
        [Test] public void FailedLastPairSnapshotDoesNotDeployAnyPlayer()
        {
            Setup(8);foreach(var p in match.Players)Add(p.PlayerId);
            // Replace one late-pair player's unit with a valid definition missing from the coordinator catalog.
            var id=match.PairingPlan.Pairs.Last().PlayerTwoId;
            var foreign=new PokemonCatalog(new[]{new PokemonDefinition("other","Other",1,new PokemonStats(100,20,0,1,0,0,1,2,0,0,.25f),"role","skill")});
            var owner=match.GetPlayer(id);owner.SetProgress(5,0,2);owner.AddUnit(foreign.CreateUnit("foreign","other",id,UnitRank.One,0,UnitPlacement.OnBench(1)));
            var revisions=match.Players.Select(x=>x.PlacementRevision).ToArray();
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(()=>loop.StartCombat(1));
            Assert.That(match.Phase,Is.EqualTo(MatchPhase.Preparation));Assert.That(loop.Battles,Is.Empty);
            Assert.That(match.Players.Select(x=>x.PlacementRevision),Is.EqualTo(revisions));
            Assert.That(match.Players.All(x=>x.DeployedUnitCount==0),Is.True);
        }
        [Test] public void LatePlayerSettlementFailurePreflightsAllRecipientsAndRetriesOnce()
        {
            Setup(8);var last=match.GetPlayer(match.PairingPlan.Pairs.Last().PlayerTwoId);last.SetProgress(int.MaxValue,0,1);
            Assert.Throws<OverflowException>(()=>loop.StartCombat(1));
            Assert.That(match.Phase,Is.EqualTo(MatchPhase.Result));Assert.That(match.Players.All(p=>p.LastEconomyRound==0 && p.LastAutomaticXPRound==0),Is.True);
            last.SetProgress(5,0,1);loop.SettleResult();Assert.That(loop.IsSettled,Is.True);
            var gold=match.Players.Select(p=>p.Gold).ToArray();loop.SettleResult();Assert.That(match.Players.Select(p=>p.Gold),Is.EqualTo(gold));
        }
        [Test] public void EliminatedPlayersAreNotRefreshedOrPaidInFollowingRound()
        {
            Setup(8);loop.StartCombat(1);match.GetPlayer("p7").SetHP(0);match.GetPlayer("p8").SetHP(0);
            loop.SettleResult();
            var dead=match.GetPlayer("p8");int gold=dead.Gold;long shop=dead.Shop.Revision;
            Assert.That(loop.NextRound(1),Is.True);Assert.That(loop.PreparationReady,Is.True);
            Assert.That(match.PairingPlan.SurvivorIds.Count,Is.EqualTo(6));Assert.That(dead.Shop.Revision,Is.EqualTo(shop));Assert.That(dead.Shop.LastRefreshRound,Is.EqualTo(1));
            Assert.That(loop.StartCombat(2),Is.True);Assert.That(loop.IsSettled,Is.True);
            Assert.That(loop.LastResult.PlayerResults.ContainsKey("p8"),Is.False);Assert.That(dead.Gold,Is.EqualTo(gold));Assert.That(dead.LastAutomaticXPRound,Is.EqualTo(1));
        }
        [TestCase(3)][TestCase(5)][TestCase(7)]
        public void OddSurvivorsRunShadowAndSettleAllRealPlayers(int count)
        {
            Setup(count);foreach(var p in match.Players)Add(p.PlayerId);
            Assert.That(match.PairingPlan.RequiresShadow,Is.True);Assert.That(loop.StartCombat(1),Is.True);
            Assert.That(loop.Battles.Count,Is.EqualTo((count+1)/2));Assert.That(loop.Battles.Count(b=>b.IsShadow),Is.EqualTo(1));
            Complete();Assert.That(loop.LastResult.PlayerResults.Count,Is.EqualTo(count));
            Assert.That(loop.LastResult.Income.Count,Is.EqualTo(count));Assert.That(loop.LastResult.XP.Count,Is.EqualTo(count));
        }
        [Test] public void DuplicateAndStaleCommandsPreservePairingAndSnapshots()
        {
            Setup(8);foreach(var p in match.Players)Add(p.PlayerId);
            var plan=match.PairingPlan;Assert.That(loop.StartCombat(0),Is.False);Assert.That(loop.StartCombat(1),Is.True);
            var battles=loop.Battles;Assert.That(loop.StartCombat(1),Is.False);Assert.That(loop.Battles,Is.SameAs(battles));Assert.That(match.PairingPlan,Is.SameAs(plan));
            Complete();Assert.That(loop.NextRound(0),Is.False);Assert.That(loop.NextRound(1),Is.True);var next=match.PairingPlan;
            Assert.That(loop.NextRound(1),Is.False);Assert.That(match.PairingPlan,Is.SameAs(next));
        }
        [Test] public void ShopRandomDrawsDoNotAffectPairing()
        {
            Setup(8);var plan=match.PairingPlan;match.GetPlayer("p1").SetProgress(100,0,1);
            for(int i=0;i<10;i++)new ShopSystem().Reroll(match,"p1");
            Assert.That(new PairingSystem().Calculate(match,1).Pairs.Select(p=>p.PlayerOneId+":"+p.PlayerTwoId),Is.EqualTo(plan.Pairs.Select(p=>p.PlayerOneId+":"+p.PlayerTwoId)));
        }
    }
}





