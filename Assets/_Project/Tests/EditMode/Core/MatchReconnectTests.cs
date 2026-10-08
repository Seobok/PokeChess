using System;
using System.Linq;
using NUnit.Framework;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;
namespace PokeChess.Core.Tests
{
    public sealed class MatchReconnectTests
    {
        private MatchState match;private LocalRoundCoordinator rounds;private HostCommandProcessor host;
        [SetUp] public void Setup(){var catalog=PrototypeRoster.CreateCatalog();match=MatchStateFactory.CreateWithPool("reconnect",new[]{"p1","p2","p3","p4"},catalog,PrototypeRoster.CreateDefinitions().Select(d=>d.Id),matchSeed:55);match.TransitionTo(MatchPhase.Starting);match.TransitionTo(MatchPhase.Preparation);foreach(var p in match.Players)new ShopSystem().RefreshForRound(match,p.PlayerId);rounds=new LocalRoundCoordinator(match,catalog,skillCatalog:PrototypeRoster.CreateSkills(),statusCatalog:PrototypeRoster.CreateStatuses());host=new HostCommandProcessor(match,catalog,rounds);}
        private void Add(string id){SharedPoolSystem.RegisterUnit(match,PrototypeRoster.CreateCatalog().CreateUnit("u-"+id,"mankey",id,UnitRank.One,0,UnitPlacement.OnBench(0)),"mankey");}
        [Test] public void RepeatedDisconnectDoesNotExtendDeadlineOrChangeHoldings()
        {Add("p2");Assert.That(host.Reconnect.Disconnect("p2",10),Is.True);Assert.That(host.Reconnect.Disconnect("p2",60),Is.False);Assert.That(host.Reconnect.Deadline("p2"),Is.EqualTo(130));Assert.That(match.GetPlayer("p2").Units.Count,Is.EqualTo(1));Assert.That(match.Players.Count,Is.EqualTo(4));}
        [Test] public void SamePlayerReturnsBeforeDeadlineAndUnknownPlayerIsRejected()
        {Add("p2");var before=match.GetPlayer("p2");host.Reconnect.Disconnect("p2",10);Assert.That(host.Reconnect.Reconnect("p2",129.999),Is.Null);Assert.That(match.GetPlayer("p2"),Is.SameAs(before));Assert.That(host.Reconnect.IsDisconnected("p2"),Is.False);Assert.That(host.Reconnect.Reconnect("stranger",20),Is.EqualTo("NotMatchParticipant"));}
        [Test] public void ExactDeadlineRejectsReconnectAndTimeoutReturnsPoolOnlyOnce()
        {Add("p2");host.Reconnect.Disconnect("p2",10);Assert.That(host.Reconnect.Reconnect("p2",130),Is.EqualTo("ReconnectExpired"));Assert.That(host.Reconnect.Expire(130),Is.EqualTo(new[]{"p2"}));Assert.That(host.Reconnect.Expire(131),Is.Empty);Assert.That(match.GetPlayer("p2").Elimination.Reason,Is.EqualTo(EliminationReason.DisconnectTimeout));Assert.That(match.GetPlayer("p2").Units,Is.Empty);match.Pool.AssertConservation(match);}
        [Test] public void TimeoutDuringCombatKillsFrozenUnitsAndSettlementCompletes()
        {foreach(var participant in match.Players)Add(participant.PlayerId);rounds.StartCombat(1);host.Reconnect.Disconnect("p2",0);host.Reconnect.Expire(120);Assert.That(rounds.GetBattleFor("p2").Battle.Units.Where(u=>u.OwnerPlayerId=="p2").All(u=>u.CurrentHP==0),Is.True);for(int i=0;i<1400&&match.Phase==MatchPhase.Combat;i++)rounds.Step();Assert.That(match.Phase,Is.Not.EqualTo(MatchPhase.Combat));Assert.That(rounds.IsSettled,Is.True);match.Pool.AssertConservation(match);}
        [Test] public void TimeoutAfterResultDoesNotInvalidateSettledRewards()
        {foreach(var participant in match.Players)Add(participant.PlayerId);rounds.StartCombat(1);for(int i=0;i<1400&&match.Phase==MatchPhase.Combat;i++)rounds.Step();var p=match.GetPlayer("p2");int gold=p.Gold;host.Reconnect.Disconnect("p2",0);host.Reconnect.Expire(120);Assert.That(rounds.IsSettled,Is.True);Assert.That(p.Gold,Is.EqualTo(gold));Assert.That(rounds.NextRound(1),Is.True);match.Pool.AssertConservation(match);}
        [Test] public void LostAckCommandUsesExistingLedgerAfterRebinding()
        {var state=host.Snapshot("p2");var command=new MatchCommand{protocol=1,matchId=match.MatchId,commandId="buy-before-loss",playerId="p2",sequence=state.nextSequence,kind=MatchCommandKind.Buy,slot=0,round=state.round,phase=state.phase,playerRevision=state.playerRevision,shopRevision=state.shopRevision,placementRevision=state.placementRevision};var ack=host.Process("p2",command);Assert.That(ack.accepted,Is.True);int gold=match.GetPlayer("p2").Gold;host.Reconnect.Disconnect("p2",0);host.NotifyHostStateChanged();host.Reconnect.Reconnect("p2",30);host.NotifyHostStateChanged();Assert.That(host.Process("p2",command),Is.SameAs(ack));Assert.That(match.GetPlayer("p2").Gold,Is.EqualTo(gold));Assert.That(match.GetPlayer("p2").Units.Count,Is.EqualTo(1));}
        [Test] public void SurrenderedPlayerCannotResumeAndPublicGraceDoesNotExposePrivateState()
        {host.Reconnect.Disconnect("p3",5);host.NotifyHostStateChanged();var summary=host.PublicSnapshot().players.First(p=>p.id=="p3");Assert.That(summary.disconnected,Is.True);Assert.That(summary.reconnectDeadline,Is.EqualTo(125));rounds.Surrender("p2",1,MatchPhase.Preparation);Assert.That(host.Reconnect.Reconnect("p2",10),Is.EqualTo("PlayerAlreadyEliminated"));}
    }
}
