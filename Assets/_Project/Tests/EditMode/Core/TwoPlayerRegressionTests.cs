using System;
using System.Linq;
using NUnit.Framework;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Tests
{
    public sealed class TwoPlayerRegressionTests
    {
        private MatchState match;
        private LocalRoundCoordinator rounds;
        private HostCommandProcessor host;
        private OnlineCombatRuntime runtime;
        [SetUp] public void Setup()
        {
            var catalog=PrototypeRoster.CreateCatalog();
            match=MatchStateFactory.CreateWithPool("two-player",new[]{"p1","p2"},catalog,PrototypeRoster.CreateDefinitions().Select(d=>d.Id),matchSeed:57);
            match.TransitionTo(MatchPhase.Starting);match.TransitionTo(MatchPhase.Preparation);
            foreach(var p in match.Players)new ShopSystem().RefreshForRound(match,p.PlayerId);
            SharedPoolSystem.RegisterUnit(match,catalog.CreateUnit("winner","mankey","p1",UnitRank.Three,0,UnitPlacement.OnBench(0)),"mankey");
            SharedPoolSystem.RegisterUnit(match,catalog.CreateUnit("loser","squirtle","p2",UnitRank.One,0,UnitPlacement.OnBench(0)),"squirtle");
            rounds=new LocalRoundCoordinator(match,catalog,skillCatalog:PrototypeRoster.CreateSkills(),statusCatalog:PrototypeRoster.CreateStatuses());
            host=new HostCommandProcessor(match,catalog,rounds);runtime=new OnlineCombatRuntime(rounds,host);
        }
        private void Enter(MatchPhase phase)
        {
            if(phase==MatchPhase.Preparation)return;
            Assert.That(runtime.StartCombat(1),Is.True);
            if(phase==MatchPhase.Result){for(int i=0;i<1400&&match.Phase==MatchPhase.Combat;i++)runtime.Step();}
            Assert.That(match.Phase,Is.EqualTo(phase));
        }
        private void Finish(MatchPhase phase,bool timeout)
        {
            Enter(phase);int gold=match.GetPlayer("p2").Gold;
            if(timeout){Assert.That(host.Reconnect.Disconnect("p2",0),Is.True);Assert.That(host.Reconnect.Expire(120),Is.EqualTo(new[]{"p2"}));}
            else Assert.That(rounds.Surrender("p2",1,phase).Accepted,Is.True);
            runtime.CaptureCommandEffects();
            Assert.That(match.Phase,Is.EqualTo(MatchPhase.Finished));
            Assert.That(match.FinalResult.WinnerPlayerId,Is.EqualTo("p1"));
            Assert.That(match.FinalResult.Standings.Select(s=>s.Placement),Is.EqualTo(new[]{1,2}));
            Assert.That(match.GetPlayer("p2").Elimination.Reason,Is.EqualTo(timeout?EliminationReason.DisconnectTimeout:EliminationReason.Surrender));
            Assert.That(match.GetPlayer("p2").Gold,Is.EqualTo(gold));
            Assert.That(match.GetPlayer("p2").Units,Is.Empty);
            var result=match.FinalResult;host.Reconnect.Expire(121);rounds.SettleResult();
            Assert.That(match.FinalResult,Is.SameAs(result));Assert.That(rounds.NextRound(1),Is.False);
            Assert.That(host.Reconnect.Reconnect("p2",121),Is.Not.Null);match.Pool.AssertConservation(match);
        }
        [Test] public void PreparationSurrenderFinishesExactlyOnce()=>Finish(MatchPhase.Preparation,false);
        [Test] public void CombatSurrenderFinishesExactlyOnce()=>Finish(MatchPhase.Combat,false);
        [Test] public void ResultSurrenderPreservesSettlement()=>Finish(MatchPhase.Result,false);
        [Test] public void PreparationTimeoutFinishesExactlyOnce()=>Finish(MatchPhase.Preparation,true);
        [Test] public void CombatTimeoutFinishesExactlyOnce()=>Finish(MatchPhase.Combat,true);
        [Test] public void ResultTimeoutPreservesSettlement()=>Finish(MatchPhase.Result,true);
        [Test] public void FinishedMatchRejectsFreshEconomicCommand()
        {
            Finish(MatchPhase.Preparation,false);var state=host.Snapshot("p1");int gold=match.GetPlayer("p1").Gold;
            var ack=host.Process("p1",new MatchCommand{matchId=match.MatchId,commandId="late-buy",playerId="p1",sequence=state.nextSequence,kind=MatchCommandKind.Buy,slot=0,round=state.round,phase=state.phase,playerRevision=state.playerRevision,shopRevision=state.shopRevision});
            Assert.That(ack.accepted,Is.False);Assert.That(ack.code,Is.EqualTo("MatchFinished"));Assert.That(match.GetPlayer("p1").Gold,Is.EqualTo(gold));
        }
    }
}
