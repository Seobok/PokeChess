using System;
using System.Linq;
using System.Collections.Generic;
using NUnit.Framework;
using PokeChess.Core.Match;
using PokeChess.Core.Battle;
using PokeChess.Core.Board;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Tests
{
    public sealed class ShadowBattleTests
    {
        private PokemonCatalog catalog;
        private MatchState match;
        private LocalRoundCoordinator loop;
        private void Setup(int count,ulong seed=123,bool reverse=false,bool longBattle=false)
        {
            catalog=new PokemonCatalog(new[]{new PokemonDefinition("test","Test",1,new PokemonStats(longBattle ? 100000 : 100,longBattle ? 1 : 20,0,1,0,0,1,2,0,0,.25f),"role","skill")});
            var ids=Enumerable.Range(1,count).Select(i=>"p"+i);
            match=MatchStateFactory.CreateWithPool("shadow",reverse ? ids.Reverse() : ids,catalog,new[]{"test"},matchSeed:seed);
            match.TransitionTo(MatchPhase.Starting);match.TransitionTo(MatchPhase.Preparation);
            foreach(var p in match.Players)new ShopSystem().RefreshForRound(match,p.PlayerId);
            loop=new LocalRoundCoordinator(match,catalog);
        }
        private UnitInstance Add(string owner,UnitRank rank=UnitRank.One,int slot=0)
        {
            var unit=catalog.CreateUnit("unit-"+owner+"-"+slot,"test",owner,rank,0,UnitPlacement.OnBench(slot));
            return SharedPoolSystem.RegisterUnit(match,unit,"test");
        }
        private void Complete()
        {
            for(int i=0;i<1400 && match.Phase==MatchPhase.Combat;i++)loop.Step();
            Assert.That(match.Phase,Is.EqualTo(MatchPhase.Result));Assert.That(loop.IsSettled,Is.True);
        }
        private static string Signature(RoundPairingPlan plan) => string.Join("|",plan.Pairs.Select(p=>p.PlayerOneId+":"+p.PlayerTwoId))+"/"+plan.ShadowPair?.PlayerOneId+":"+plan.ShadowPair?.PlayerTwoId;
        private static void Advance(MatchState state)
        { state.TransitionTo(MatchPhase.Combat);state.TransitionTo(MatchPhase.Result);state.TransitionTo(MatchPhase.Preparation); }
        [TestCase(3)][TestCase(5)][TestCase(7)]
        public void TenAutomaticRoundsFightAndSettleEveryRealPlayerOnce(int count)
        {
            Setup(count);foreach(var p in match.Players)Add(p.PlayerId);
            var flow=new RoundFlowController(loop,new RoundFlowRules(.1,.1));
            for(int round=1;round<=10;round++)
            {
                flow.AdvanceTime(.1);Assert.That(match.Phase,Is.EqualTo(MatchPhase.Combat));Assert.That(flow.Fault,Is.Null);
                Assert.That(loop.Battles.Count,Is.EqualTo((count+1)/2));Assert.That(loop.Battles.Count(b=>b.IsShadow),Is.EqualTo(1));
                Assert.That(loop.Battles.SelectMany(b=>b.Pairing.ParticipantIds).Distinct().Count(),Is.EqualTo(count));
                Assert.That(loop.Battles.Select(b=>b.Battle.BattleSeed).Distinct().Count(),Is.EqualTo(loop.Battles.Count));
                for(int frame=0;frame<1400 && match.Phase==MatchPhase.Combat;frame++)flow.AdvanceTime(1d/30);
                Assert.That(flow.Fault,Is.Null);Assert.That(loop.IsSettled,Is.True);
                Assert.That(loop.LastResult.PlayerResults.Count,Is.EqualTo(count));Assert.That(loop.LastResult.PlayerResults.Values.Count(r=>r.IsShadow),Is.EqualTo(1));
                Assert.That(loop.LastResult.Income.Count,Is.EqualTo(count));Assert.That(loop.LastResult.XP.Count,Is.EqualTo(count));
                Assert.That(match.Players.All(p=>p.LastEconomyRound==round && p.LastAutomaticXPRound==round && p.Units.Count==1),Is.True);
                var gold=match.Players.Select(p=>p.Gold).ToArray();loop.SettleResult();Assert.That(match.Players.Select(p=>p.Gold),Is.EqualTo(gold));
                match.Pool.AssertConservation(match);flow.AdvanceTime(.2);
                Assert.That(match.Phase,Is.EqualTo(MatchPhase.Preparation));Assert.That(match.RoundNumber,Is.EqualTo(round+1));
            }
        }
        [TestCase(3)][TestCase(5)][TestCase(7)]
        public void SourceIsDifferentAlivePlayerAndHasOnlyItsNormalBattle(int count)
        {
            Setup(count);var plan=match.PairingPlan;var shadow=plan.ShadowPair;
            Assert.That(shadow.IsShadow,Is.True);Assert.That(shadow.PlayerOneId,Is.EqualTo(plan.UnpairedPlayerId));
            Assert.That(shadow.PlayerTwoId,Is.Not.EqualTo(shadow.PlayerOneId));Assert.That(plan.SurvivorIds,Does.Contain(shadow.PlayerTwoId));
            Assert.That(shadow.ParticipantIds,Is.EqualTo(new[]{shadow.PlayerOneId}));Assert.That(shadow.Contains(shadow.PlayerTwoId),Is.False);
            Assert.Throws<ArgumentException>(()=>shadow.OpponentOf(shadow.PlayerTwoId));
            loop.StartCombat(1);
            Assert.That(loop.GetBattleFor(shadow.PlayerTwoId).IsShadow,Is.False);Assert.That(loop.GetBattleFor(shadow.PlayerOneId).IsShadow,Is.True);
            Assert.That(loop.LastResult.PlayerResults[shadow.PlayerTwoId].IsShadow,Is.False);
        }
        [Test] public void ShadowAndNormalSnapshotsHaveIndependentVitalsItemsAndRng()
        {
            Setup(3);foreach(var p in match.Players)Add(p.PlayerId);
            var source=match.GetPlayer(match.PairingPlan.ShadowPair.PlayerTwoId);var owned=source.Units.Single();owned.AddItem("before");
            var stock=match.Pool.GetStock("test").Available;loop.StartCombat(1);
            var normal=loop.GetBattleFor(source.PlayerId).Battle;var shadow=loop.Battles.Single(b=>b.IsShadow);
            var a=normal.Units.Single(u=>u.OwnerPlayerId==source.PlayerId);var b=shadow.Battle.Units.Single(u=>u.TeamId==2);
            Assert.That(a,Is.Not.SameAs(b));Assert.That(a.Stats.MaxHP,Is.EqualTo(b.Stats.MaxHP));
            Assert.That(b.Position,Is.EqualTo(HexCoordinates.MirrorCombat(owned.Placement.Position.Value)));
            a.SetVitals(1,10);a.SetShield(25);a.SetUntargetable(true);a.SetTauntSource("taunt");
            Assert.That(b.CurrentHP,Is.EqualTo(b.Stats.MaxHP));Assert.That(b.CurrentEnergy,Is.Zero);Assert.That(b.CurrentShield,Is.Zero);Assert.That(b.IsUntargetable,Is.False);Assert.That(b.TauntSourceId,Is.Null);
            var random=shadow.Battle.RngStreams.Target.State;normal.RngStreams.Target.NextUInt64();
            Assert.That(shadow.Battle.RngStreams.Target.State,Is.EqualTo(random));
            owned.AddItem("after");Assert.That(b.ItemInstanceIds,Is.EqualTo(new[]{"before"}));Assert.That(shadow.ShadowSnapshot.Units.Single().ItemInstanceIds,Is.EqualTo(new[]{"before"}));
            Assert.That(match.Pool.GetStock("test").Available,Is.EqualTo(stock));Assert.That(source.Units.Single(),Is.SameAs(owned));
        }
        [Test] public void ShadowUsesAutomaticDeploymentRankItemsAndExcludesRemainingBench()
        {
            Setup(3);var source=match.PairingPlan.ShadowPair.PlayerTwoId;var target=match.PairingPlan.ShadowPair.PlayerOneId;
            var unit=Add(source,UnitRank.Two);unit.AddItem("held");Add(source,UnitRank.One,1);Add(target);
            Assert.That(match.GetPlayer(source).DeployedUnitCount,Is.Zero);loop.StartCombat(1);
            var shadow=loop.GetBattleFor(target);var copy=shadow.Battle.Units.Single(u=>u.TeamId==2);
            Assert.That(shadow.ShadowSnapshot.Units.Count,Is.EqualTo(1));Assert.That(copy.Rank,Is.EqualTo(UnitRank.Two));Assert.That(copy.ItemInstanceIds,Is.EqualTo(new[]{"held"}));
            Assert.That(copy.Stats.MaxHP,Is.EqualTo(RankRules.Default.Apply(catalog.Get("test").BaseStats,UnitRank.Two).MaxHP));
            Assert.That(match.GetPlayer(source).Bench[1].InstanceId,Is.EqualTo("unit-"+source+"-1"));
        }
        [Test] public void SourceNormalLossIsNotReplacedByShadowVictory()
        {
            Setup(3);var plan=match.PairingPlan;string source=plan.ShadowPair.PlayerTwoId,target=plan.ShadowPair.PlayerOneId;
            string normalOpponent=plan.Pairs.Single().OpponentOf(source);Add(source);Add(normalOpponent);loop.StartCombat(1);
            Assert.That(loop.GetBattleFor(target).Result,Is.EqualTo(BattleResult.TeamTwoWin));
            loop.GetBattleFor(source).Battle.Units.Single(u=>u.OwnerPlayerId==source).SetVitals(0,0);Complete();
            Assert.That(loop.LastResult.PlayerResults[source].Outcome,Is.EqualTo(RoundOutcome.Loss));Assert.That(loop.LastResult.PlayerResults[source].IsShadow,Is.False);
            Assert.That(match.GetPlayer(source).LoseStreak,Is.EqualTo(1));Assert.That(match.GetPlayer(source).WinStreak,Is.Zero);
            Assert.That(loop.LastResult.PlayerResults[target].Outcome,Is.EqualTo(RoundOutcome.Loss));Assert.That(loop.LastResult.Income.Count,Is.EqualTo(3));
        }
        [Test] public void SourceNormalVictoryIsNotReplacedByShadowLoss()
        {
            Setup(3);var pair=match.PairingPlan.ShadowPair;Add(pair.PlayerTwoId);Add(pair.PlayerOneId);loop.StartCombat(1);
            var normal=loop.GetBattleFor(pair.PlayerTwoId);Assert.That(normal.OutcomeOf(pair.PlayerTwoId),Is.EqualTo(RoundOutcome.Win));
            loop.GetBattleFor(pair.PlayerOneId).Battle.Units.Single(u=>u.TeamId==2).SetVitals(0,0);Complete();
            Assert.That(loop.LastResult.PlayerResults[pair.PlayerTwoId].Outcome,Is.EqualTo(RoundOutcome.Win));
            Assert.That(loop.LastResult.PlayerResults[pair.PlayerOneId].Outcome,Is.EqualTo(RoundOutcome.Win));
            Assert.That(match.GetPlayer(pair.PlayerTwoId).WinStreak,Is.EqualTo(1));Assert.That(match.GetPlayer(pair.PlayerTwoId).LoseStreak,Is.Zero);
        }
        [TestCase(false,false,RoundOutcome.Draw)][TestCase(true,false,RoundOutcome.Win)][TestCase(false,true,RoundOutcome.Loss)]
        public void EmptyShadowBoardsResolveWithoutFakeParticipants(bool targetUnit,bool sourceUnit,RoundOutcome outcome)
        {
            Setup(3);var pair=match.PairingPlan.ShadowPair;if(targetUnit)Add(pair.PlayerOneId);if(sourceUnit)Add(pair.PlayerTwoId);
            loop.StartCombat(1);Assert.That(loop.IsSettled,Is.True);Assert.That(loop.GetBattleFor(pair.PlayerOneId).Battle,Is.Null);
            Assert.That(loop.LastResult.PlayerResults[pair.PlayerOneId].Outcome,Is.EqualTo(outcome));Assert.That(loop.LastResult.PlayerResults.Count,Is.EqualTo(3));
        }
        [Test] public void NormalPairsWaitForLongShadowBeforeAnyPayout()
        {
            Setup(3,longBattle:true);var pair=match.PairingPlan.ShadowPair;Add(pair.PlayerOneId);Add(pair.PlayerTwoId);loop.StartCombat(1);
            Assert.That(loop.Battles.First(b=>!b.IsShadow).IsComplete,Is.True);for(int i=0;i<100;i++)loop.Step();
            Assert.That(match.Phase,Is.EqualTo(MatchPhase.Combat));Assert.That(loop.LastResult,Is.Null);Assert.That(match.Players.All(p=>p.LastEconomyRound==0),Is.True);
            Complete();Assert.That(loop.GetBattleFor(pair.PlayerOneId).Reason,Is.EqualTo(BattleEndReason.TimeLimit));
        }
        [Test] public void ShadowWaitsForLongNormalPairBeforeAnyPayout()
        {
            Setup(3,longBattle:true);foreach(var id in match.PairingPlan.Pairs.Single().ParticipantIds)Add(id);
            loop.StartCombat(1);Assert.That(loop.Battles.Single(b=>b.IsShadow).IsComplete,Is.True);for(int i=0;i<100;i++)loop.Step();
            Assert.That(match.Phase,Is.EqualTo(MatchPhase.Combat));Assert.That(loop.LastResult,Is.Null);Complete();
        }
        [TestCase(3)][TestCase(5)][TestCase(7)]
        public void PlanAndSourceAreDeterministicAcrossInputOrderAndRetries(int count)
        {
            Setup(count);var a=match;Setup(count,reverse:true);var b=match;var system=new PairingSystem();
            for(int round=1;round<=12;round++)
            {
                var first=system.PrepareRound(a,round);var second=system.PrepareRound(b,round);
                Assert.That(Signature(first),Is.EqualTo(Signature(second)));Assert.That(system.PrepareRound(a,round),Is.SameAs(first));
                Advance(a);Advance(b);
            }
        }
        [Test] public void SourceSelectionMinimizesTargetsActualTwoRoundHistory()
        {
            for(ulong seed=0;seed<20;seed++)
            {
                Setup(5,seed);
                for(int round=1;round<=12;round++)
                {
                    var plan=new PairingSystem().PrepareRound(match,round);var shadow=plan.ShadowPair;
                    Func<string,int> score=opponent=>
                    {
                        int value=0;
                        for(int ago=1;ago<=2;ago++)if(match.PairingHistory.TryGetValue(round-ago,out var prior))
                        {
                            var encounter=prior.Pairs.FirstOrDefault(p=>p.Contains(shadow.PlayerOneId)) ?? (prior.ShadowPair?.Contains(shadow.PlayerOneId)==true ? prior.ShadowPair : null);
                            if(encounter!=null && encounter.OpponentOf(shadow.PlayerOneId)==opponent)value+=ago==1 ? 100 : 1;
                        }
                        return value;
                    };
                    Assert.That(score(shadow.PlayerTwoId),Is.EqualTo(plan.SurvivorIds.Where(id=>id!=shadow.PlayerOneId).Min(score)));
                    Advance(match);
                }
            }
        }
        [Test] public void FrozenSnapshotSurvivesSourceRemovalAndCanCreateIndependentBattles()
        {
            Setup(3);var unit=Add("p1",UnitRank.Two);unit.AddItem("old");unit.SetPlacement(UnitPlacement.OnBoard(new BoardPosition(2,1)));
            var snapshot=ShadowBoardSnapshot.Capture(match.GetPlayer("p1"),1,catalog);
            unit.AddItem("new");match.GetPlayer("p1").RemoveUnit(unit.InstanceId);match.GetPlayer("p1").SetHP(0);
            var challenger=Add("p2");var setup=new[]{new BattleUnitSetup(challenger,1,new BoardPosition(0,0))};
            var a=snapshot.CreateBattle("frozen-a",1,30,catalog,setup);var b=snapshot.CreateBattle("frozen-b",2,30,catalog,setup);
            Assert.That(a.Units.Single(u=>u.TeamId==2).ItemInstanceIds,Is.EqualTo(new[]{"old"}));
            Assert.That(a.Units.Single(u=>u.TeamId==2).Position,Is.EqualTo(HexCoordinates.MirrorCombat(new BoardPosition(2,1))));
            a.Units.Single(u=>u.TeamId==2).SetVitals(0,0);Assert.That(b.Units.Single(u=>u.TeamId==2).IsAlive,Is.True);
        }
        [Test] public void FailedShadowCaptureLeavesAllPlacementsUnchangedAndCanRetry()
        {
            Setup(5);var pair=match.PairingPlan.ShadowPair;
            var foreign=new PokemonCatalog(new[]{new PokemonDefinition("foreign","Foreign",1,new PokemonStats(100,20,0,1,0,0,1,2,0,0,.25f),"role","skill")});
            var source=match.GetPlayer(pair.PlayerTwoId);source.AddUnit(foreign.CreateUnit("foreign-unit","foreign",source.PlayerId,UnitRank.One,0,UnitPlacement.OnBench(0)));
            string normalOpponent=match.PairingPlan.Pairs.Single(p=>p.Contains(source.PlayerId)).OpponentOf(source.PlayerId);
            foreach(var p in match.Players.Where(p=>p.PlayerId!=source.PlayerId && p.PlayerId!=normalOpponent))Add(p.PlayerId);
            var revisions=match.Players.Select(p=>p.PlacementRevision).ToArray();var plan=match.PairingPlan;
            Assert.Throws<KeyNotFoundException>(()=>loop.StartCombat(1));Assert.That(match.Phase,Is.EqualTo(MatchPhase.Preparation));
            Assert.That(loop.Battles,Is.Empty);Assert.That(match.Players.Select(p=>p.PlacementRevision),Is.EqualTo(revisions));Assert.That(match.Players.All(p=>p.DeployedUnitCount==0),Is.True);
            source.RemoveUnit("foreign-unit");Add(source.PlayerId);Assert.That(loop.StartCombat(1),Is.True);Assert.That(match.PairingPlan,Is.SameAs(plan));Complete();
        }
        [Test] public void FailedShadowBattleCreationPreservesAllPlannedDeployments()
        {
            Setup(3);var target=match.GetPlayer(match.PairingPlan.UnpairedPlayerId);
            var foreign=new PokemonCatalog(new[]{new PokemonDefinition("foreign","Foreign",1,new PokemonStats(100,20,0,1,0,0,1,2,0,0,.25f),"role","skill")});
            target.AddUnit(foreign.CreateUnit("foreign-target","foreign",target.PlayerId,UnitRank.One,0,UnitPlacement.OnBench(0)));
            foreach(var p in match.Players.Where(p=>p.PlayerId!=target.PlayerId))Add(p.PlayerId);
            var revisions=match.Players.Select(p=>p.PlacementRevision).ToArray();
            Assert.Throws<KeyNotFoundException>(()=>loop.StartCombat(1));
            Assert.That(match.Phase,Is.EqualTo(MatchPhase.Preparation));Assert.That(loop.Battles,Is.Empty);
            Assert.That(match.Players.All(p=>p.DeployedUnitCount==0),Is.True);Assert.That(match.Players.Select(p=>p.PlacementRevision),Is.EqualTo(revisions));
            target.RemoveUnit("foreign-target");Add(target.PlayerId);Assert.That(loop.StartCombat(1),Is.True);Complete();
        }
        [Test] public void SettlementFailureIncludesShadowRecipientAndRetriesExactlyOnce()
        {
            Setup(3);var target=match.GetPlayer(match.PairingPlan.UnpairedPlayerId);target.SetProgress(int.MaxValue,0,1);
            Assert.Throws<OverflowException>(()=>loop.StartCombat(1));Assert.That(match.Phase,Is.EqualTo(MatchPhase.Result));Assert.That(match.Players.All(p=>p.LastEconomyRound==0),Is.True);
            target.SetProgress(5,0,1);loop.SettleResult();Assert.That(loop.IsSettled,Is.True);Assert.That(loop.LastResult.XP.Count,Is.EqualTo(3));
            var gold=match.Players.Select(p=>p.Gold).ToArray();loop.SettleResult();Assert.That(match.Players.Select(p=>p.Gold),Is.EqualTo(gold));
        }
        [Test] public void StaleAndDuplicateStartDoNotRecloneOrPayTwice()
        {
            Setup(3);foreach(var p in match.Players)Add(p.PlayerId);var plan=match.PairingPlan;
            Assert.That(loop.StartCombat(0),Is.False);Assert.That(loop.StartCombat(1),Is.True);var battles=loop.Battles;
            Assert.That(loop.StartCombat(1),Is.False);Assert.That(loop.Battles,Is.SameAs(battles));Assert.That(match.PairingPlan,Is.SameAs(plan));Complete();
            Assert.That(loop.NextRound(1),Is.True);var next=match.PairingPlan;Assert.That(loop.NextRound(1),Is.False);Assert.That(match.PairingPlan,Is.SameAs(next));
        }
    }
}



