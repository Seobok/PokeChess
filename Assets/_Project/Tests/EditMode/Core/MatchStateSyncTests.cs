using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Tests
{
    public sealed class MatchStateSyncTests
    {
        private HostCommandProcessor processor;
        private MatchState match;
        private LocalRoundCoordinator rounds;
        [SetUp] public void Setup()
        {
            var catalog=PrototypeRoster.CreateCatalog();match=MatchStateFactory.CreateWithPool("sync",new[]{"p1","p2"},catalog,PrototypeRoster.CreateDefinitions().Select(d=>d.Id),rules:new MatchRules(startingGold:50),matchSeed:123);
            match.TransitionTo(MatchPhase.Starting);match.TransitionTo(MatchPhase.Preparation);foreach(var p in match.Players)new ShopSystem().RefreshForRound(match,p.PlayerId);
            rounds=new LocalRoundCoordinator(match,catalog);processor=new HostCommandProcessor(match,catalog,rounds);
        }
        private CommandAck Command(string player,MatchCommandKind kind,string unit=null)
        {
            var s=processor.Snapshot(player);return processor.Process(player,new MatchCommand{commandId=Guid.NewGuid().ToString("N"),matchId=match.MatchId,playerId=player,sequence=s.nextSequence,kind=kind,unitId=unit,destination=PlacementKind.Board,
                round=s.round,phase=s.phase,playerRevision=s.playerRevision,shopRevision=s.shopRevision,placementRevision=s.placementRevision});
        }
        [Test] public void PrivateBenchStaysPrivateAndOnlyPlacedUnitsBecomePublic()
        {
            Assert.That(Command("p2",MatchCommandKind.Buy).accepted,Is.True);var unit=match.GetPlayer("p2").Units.Single();
            Assert.That(processor.PublicSnapshot().players.Single(p=>p.id=="p2").board,Is.Empty);
            Assert.That(processor.FullSnapshot("p1").ownerState.units,Is.Empty);
            Assert.That(Command("p2",MatchCommandKind.Move,unit.InstanceId).accepted,Is.True);
            var board=processor.PublicSnapshot().players.Single(p=>p.id=="p2").board;
            Assert.That(board.Single().id,Is.EqualTo(unit.InstanceId));Assert.That(board.Single().rank,Is.EqualTo(1));Assert.That(board.Single().column,Is.Zero);
            var json=JsonUtility.ToJson(processor.PublicSnapshot());
            foreach(var forbidden in new[]{"gold","shop","inventory","bench","seed","Pool","PairingPlan","nextSequence","playerRevision"})Assert.That(json,Does.Not.Contain("\""+forbidden+"\""));
            Assert.That(JsonUtility.ToJson(processor.Snapshot("p1")),Does.Not.Contain("\"players\""));
        }
        [Test] public void FullBaselineRejectsWrongOwnerMatchProtocolAndMixedVersions()
        {
            var store=new MatchSnapshotStore();var value=processor.FullSnapshot("p1");Assert.That(store.Apply(value,"sync","p2"),Is.False);Assert.That(store.Apply(value,"other","p1"),Is.False);
            value.protocol=1;Assert.That(store.Apply(value,"sync","p1"),Is.False);value.protocol=2;value.publicState.revision++;
            Assert.That(store.Apply(value,"sync","p1"),Is.False);Assert.That(store.Owner,Is.Null);Assert.That(store.Public,Is.Null);
            Assert.That(store.Apply(processor.FullSnapshot("p1"),"sync","p1"),Is.True);
        }
        [Test] public void OldSnapshotCannotUndoPurchaseAndSyncDoesNotMutateEconomy()
        {
            var store=new MatchSnapshotStore();var old=processor.FullSnapshot("p1");Command("p1",MatchCommandKind.Buy);var latest=processor.FullSnapshot("p1");Assert.That(store.Apply(latest,"sync","p1"),Is.True);
            Assert.That(store.Apply(old,"sync","p1"),Is.False);Assert.That(store.Owner.gold,Is.EqualTo(49));
            var version=processor.StateVersion;var ack=Command("p1",MatchCommandKind.Sync);Assert.That(ack.accepted,Is.True);Assert.That(processor.StateVersion,Is.EqualTo(version));
            Assert.That(store.Apply(processor.FullSnapshot("p1"),"sync","p1"),Is.True);Assert.That(store.Owner.gold,Is.EqualTo(49));
            Assert.That(store.Apply(latest,"sync","p1"),Is.False);store.Clear();Assert.That(store.Owner,Is.Null);Assert.That(store.Public,Is.Null);
        }
        [Test] public void HostDrivenPhaseChangeUsesStateVersionWithoutConsumingCommandSequence()
        {
            var before=processor.FullSnapshot("p1");match.TransitionTo(MatchPhase.Combat);processor.NotifyHostStateChanged();var after=processor.FullSnapshot("p1");
            Assert.That(after.publicState.revision,Is.GreaterThan(before.publicState.revision));Assert.That(after.ownerState.nextSequence,Is.EqualTo(before.ownerState.nextSequence));
            Assert.That(after.publicState.phase,Is.EqualTo(MatchPhase.Combat));Assert.That(after.publicState.revision,Is.EqualTo(after.ownerState.revision));
            Assert.That(after.ownerState.playerRevision,Is.GreaterThan(before.ownerState.playerRevision));
            Assert.That(after.publicState.hasPhaseDeadline,Is.False);
        }
        [Test] public void SurrenderPublishesStandingsAndWinnerWithoutPrivateRewardLeak()
        {
            Assert.That(Command("p2",MatchCommandKind.Surrender).accepted,Is.True);var value=processor.PublicSnapshot();Assert.That(value.phase,Is.EqualTo(MatchPhase.Finished));
            Assert.That(value.winnerPlayerId,Is.EqualTo("p1"));Assert.That(value.players.Single(p=>p.id=="p2").placement,Is.EqualTo(2));Assert.That(value.players.Single(p=>p.id=="p2").eliminated,Is.True);
            Assert.That(JsonUtility.ToJson(value),Does.Not.Contain("\"reward\""));
        }
        [Test] public void EquippedItemsArePublicAndUnattachedInventoryIsOwnerOnly()
        {
            Command("p2",MatchCommandKind.Buy);var unit=match.GetPlayer("p2").Units.Single();unit.AddItem("equipped-item");match.GetPlayer("p2").AddInventoryItem("private-item");
            Command("p2",MatchCommandKind.Move,unit.InstanceId);var json=JsonUtility.ToJson(processor.PublicSnapshot());Assert.That(json,Does.Contain("equipped-item"));Assert.That(json,Does.Not.Contain("private-item"));
            Assert.That(processor.Snapshot("p2").inventory,Does.Contain("private-item"));Assert.That(processor.Snapshot("p1").inventory,Is.Empty);
        }
        [Test] public void SettledRoundPublishesOutcomesButKeepsRewardBreakdownPrivate()
        {
            Assert.That(rounds.StartCombat(match.RoundNumber),Is.True);for(int i=0;i<5&&match.Phase==MatchPhase.Combat;i++)rounds.Step();processor.NotifyHostStateChanged();
            Assert.That(rounds.LastResult,Is.Not.Null);var owner=processor.Snapshot("p1");Assert.That(owner.reward.totalIncome,Is.EqualTo(rounds.LastResult.Income["p1"].TotalIncome));
            Assert.That(owner.reward.goldBefore,Is.EqualTo(50));Assert.That(processor.PublicSnapshot().results.Length,Is.EqualTo(2));
            Assert.That(JsonUtility.ToJson(processor.PublicSnapshot()),Does.Not.Contain("\"interest\""));
        }
    }
}
