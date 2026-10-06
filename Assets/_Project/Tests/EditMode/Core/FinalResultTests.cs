using System;
using System.Linq;
using NUnit.Framework;
using PokeChess.Core.Match;
using PokeChess.Core.Battle;
using PokeChess.Core.Pokemon;
namespace PokeChess.Core.Tests
{
    public sealed class FinalResultTests
    {
        private MatchState m;private PokemonCatalog catalog;private LocalRoundCoordinator loop;
        private void Setup(int count=2,int hp=60)
        {
            catalog=new PokemonCatalog(new[]{new PokemonDefinition("t","T",1,new PokemonStats(100,10,0,1,0,0,1,2,0,0,.25f),"r","s")});
            m=MatchStateFactory.CreateWithPool("final",Enumerable.Range(1,count).Select(i=>"p"+i),catalog,new[]{"t"},rules:new MatchRules(startingHP:hp),matchSeed:123);
            m.TransitionTo(MatchPhase.Starting);m.TransitionTo(MatchPhase.Preparation);
            foreach(var p in m.Players)new ShopSystem().RefreshForRound(m,p.PlayerId);loop=new LocalRoundCoordinator(m,catalog);
        }
        private void Add(string id)=>SharedPoolSystem.RegisterUnit(m,catalog.CreateUnit("u-"+id,"t",id,UnitRank.One,0,UnitPlacement.OnBench(0)),"t");
        private void Complete(){for(int i=0;i<1400 && m.Phase==MatchPhase.Combat;i++)loop.Step();}
        [Test] public void LastAliveDamageWinnerKeepsUnitsAndFinalRoundNeverAdvances()
        {
            Setup(hp:1);Add("p2");Assert.That(loop.StartCombat(1),Is.True);
            Assert.That(m.Phase,Is.EqualTo(MatchPhase.Finished));var result=m.FinalResult;
            Assert.That(result.Reason,Is.EqualTo(MatchEndReason.LastPlayerAlive));Assert.That(result.WinnerPlayerId,Is.EqualTo("p2"));Assert.That(result.EndRound,Is.EqualTo(1));
            var winner=m.GetPlayer("p2");Assert.That(winner.FinalPlacement,Is.EqualTo(1));Assert.That(winner.IsEliminated,Is.False);Assert.That(winner.Elimination,Is.Null);Assert.That(winner.Units.Count,Is.EqualTo(1));
            Assert.That(result.Standings.Select(r=>r.Placement),Is.EqualTo(new[]{1,2}));Assert.That(loop.IsSettled,Is.True);
            Assert.That(loop.NextRound(1),Is.False);Assert.That(m.RoundNumber,Is.EqualTo(1));m.Pool.AssertConservation(m);
        }
        [TestCase(2)][TestCase(3)][TestCase(4)][TestCase(5)][TestCase(6)][TestCase(7)][TestCase(8)]
        public void PreparationSurrendersConcludeWithCompleteUniqueStandings(int count)
        {
            Setup(count);for(int i=1;i<count;i++)Assert.That(loop.Surrender("p"+i,1,MatchPhase.Preparation).Accepted,Is.True);
            Assert.That(m.Phase,Is.EqualTo(MatchPhase.Finished));Assert.That(m.FinalResult.WinnerPlayerId,Is.EqualTo("p"+count));
            Assert.That(m.FinalResult.Standings.Select(r=>r.Placement),Is.EqualTo(Enumerable.Range(1,count)));Assert.That(loop.LastResult,Is.Null);Assert.That(m.RoundNumber,Is.EqualTo(1));
        }
        [Test] public void CombatLastSurvivorWaitsForIndependentShadowAndPayout()
        {
            Setup(3);foreach(var p in m.Players)Add(p.PlayerId);loop.StartCombat(1);
            var pair=loop.Battles.Single(b=>!b.IsShadow).Pairing;var shadow=loop.Battles.Single(b=>b.IsShadow);
            loop.Surrender(pair.PlayerOneId,1,MatchPhase.Combat);loop.Surrender(pair.PlayerTwoId,1,MatchPhase.Combat);
            Assert.That(m.Phase,Is.EqualTo(MatchPhase.Combat));Assert.That(m.FinalResult,Is.Null);Assert.That(loop.TryFinishMatch(1),Is.False);Assert.That(shadow.IsComplete,Is.False);
            Complete();Assert.That(m.Phase,Is.EqualTo(MatchPhase.Finished));Assert.That(m.FinalResult.WinnerPlayerId,Is.EqualTo(shadow.Pairing.PlayerOneId));
            Assert.That(m.GetPlayer(shadow.Pairing.PlayerOneId).LastEconomyRound,Is.EqualTo(1));Assert.That(m.GetPlayer(shadow.Pairing.PlayerOneId).LastAutomaticXPRound,Is.EqualTo(1));
        }
        [Test] public void ResultLastSurrenderPreservesPreviousBattleAndRewards()
        {
            Setup();loop.StartCombat(1);var round=loop.LastResult;int gold=m.GetPlayer("p1").Gold;
            Assert.That(loop.Surrender("p1",1,MatchPhase.Result).Accepted,Is.True);Assert.That(m.Phase,Is.EqualTo(MatchPhase.Finished));
            Assert.That(loop.LastResult,Is.SameAs(round));Assert.That(round.PlayerResults["p1"].Outcome,Is.EqualTo(RoundOutcome.Draw));Assert.That(m.GetPlayer("p1").Gold,Is.EqualTo(gold));
            Assert.That(m.FinalResult.Standings[1].EliminationReason,Is.EqualTo(EliminationReason.Surrender));
        }
        [Test] public void EveryoneDiesUsesEliminationPlacementOneWithoutResurrection()
        {
            Setup(hp:1);foreach(var p in m.Players)Add(p.PlayerId);loop.StartCombat(1);
            for(int i=0;i<1400 && m.Phase==MatchPhase.Combat;i++) {foreach(var u in loop.Battle.Units)if(u.IsAlive)u.SetShield(100000);loop.Step();}
            Assert.That(m.Phase,Is.EqualTo(MatchPhase.Finished));var result=m.FinalResult;
            Assert.That(result.Reason,Is.EqualTo(MatchEndReason.AllPlayersEliminated));Assert.That(result.WinnerPlayerId,Is.EqualTo(m.Players.Single(p=>p.FinalPlacement==1).PlayerId));
            Assert.That(m.Players.All(p=>p.IsEliminated && p.HP==0),Is.True);Assert.That(result.Standings.All(r=>!r.WasAlive),Is.True);Assert.That(result.Standings.Count(r=>r.IsWinner),Is.EqualTo(1));m.Pool.AssertConservation(m);
        }
        [Test] public void FailedFinalSettlementCanRetryExactlyOnce()
        {
            Setup(hp:1);Add("p2");var p=m.GetPlayer("p1");long revision=p.Shop.Revision;typeof(ShopState).GetProperty("Revision").SetValue(p.Shop,long.MaxValue);
            Assert.Throws<OverflowException>(()=>loop.StartCombat(1));Assert.That(m.Phase,Is.EqualTo(MatchPhase.Result));Assert.That(m.FinalResult,Is.Null);Assert.That(loop.TryFinishMatch(1),Is.False);
            typeof(ShopState).GetProperty("Revision").SetValue(p.Shop,revision);loop.SettleResult();var result=m.FinalResult;int gold=m.GetPlayer("p2").Gold;loop.SettleResult();loop.Step();
            Assert.That(loop.TryFinishMatch(1),Is.True);Assert.That(m.FinalResult,Is.SameAs(result));Assert.That(m.GetPlayer("p2").Gold,Is.EqualTo(gold));Assert.That(m.RoundNumber,Is.EqualTo(1));
        }
        [Test] public void FinishedTimersAndCommandsCannotChangeAuthoritativeState()
        {
            Setup();Add("p2");loop.Surrender("p1",1,MatchPhase.Preparation);var p=m.GetPlayer("p2");var final=m.FinalResult;
            var flow=new RoundFlowController(loop);int gold=p.Gold;long revision=p.Shop.Revision;var unit=p.Units.Single();
            flow.AdvanceTime(1000);Assert.That(flow.IsTimerRunning,Is.False);Assert.That(flow.Fault,Is.Null);Assert.That(flow.RequestAdvance(1,MatchPhase.Finished),Is.False);
            Assert.That(loop.StartCombat(1),Is.False);Assert.That(loop.NextRound(1),Is.False);Assert.That(loop.Surrender("p2",1,MatchPhase.Finished).Accepted,Is.False);Assert.That(loop.Surrender("p1",1,MatchPhase.Preparation).Accepted,Is.False);
            Assert.Throws<InvalidOperationException>(()=>new ShopSystem().RefreshForRound(m,"p2"));Assert.Throws<InvalidOperationException>(()=>new ShopSystem().Reroll(m,"p2"));Assert.Throws<InvalidOperationException>(()=>new LevelSystem().BuyXP(m,"p2"));
            Assert.Throws<InvalidOperationException>(()=>new ShopTransactionSystem(catalog).Buy(m,"p2",0,revision));
            Assert.That(new PlayerPlacementSystem().Preview(m,"p2",unit.InstanceId,UnitPlacement.OnBoard(new BoardPosition(0,0)),p.PlacementRevision).Accepted,Is.False);
            Assert.That(p.Gold,Is.EqualTo(gold));Assert.That(p.Shop.Revision,Is.EqualTo(revision));Assert.That(m.FinalResult,Is.SameAs(final));m.Pool.AssertConservation(m);
        }
        [Test] public void FinalSnapshotDoesNotExposeMutablePlayerState()
        {
            Setup();loop.Surrender("p1",1,MatchPhase.Preparation);var row=m.FinalResult.Standings[0];int hp=row.HP;m.GetPlayer("p2").SetHP(0);
            Assert.That(row.HP,Is.EqualTo(hp));Assert.That(row.WasAlive,Is.True);Assert.That(m.FinalResult.WinnerPlayerId,Is.EqualTo("p2"));
        }
        [Test] public void MultipleSurvivorsAndStaleRoundCannotFinish()
        {
            Setup(4);Assert.That(loop.TryFinishMatch(1),Is.False);Assert.That(loop.TryFinishMatch(0),Is.False);loop.StartCombat(1);Assert.That(m.Phase,Is.EqualTo(MatchPhase.Result));Assert.That(loop.TryFinishMatch(1),Is.False);Assert.That(m.FinalResult,Is.Null);
        }
    }
}
