using System;
using System.Linq;
using NUnit.Framework;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;
using UnityEngine;
namespace PokeChess.Core.Tests
{
    public sealed class HostCommandProcessorTests
    {
        private HostCommandProcessor processor;
        private MatchState match;
        [SetUp] public void Setup()
        {
            var catalog=PrototypeRoster.CreateCatalog();match=MatchStateFactory.CreateWithPool("commands",new[]{"p1","p2"},catalog,PrototypeRoster.CreateDefinitions().Select(d=>d.Id),rules:new MatchRules(startingGold:50),matchSeed:7);
            match.TransitionTo(MatchPhase.Starting);match.TransitionTo(MatchPhase.Preparation);foreach(var p in match.Players)new ShopSystem().RefreshForRound(match,p.PlayerId);
            processor=new HostCommandProcessor(match,catalog,new LocalRoundCoordinator(match,catalog));
        }
        private MatchCommand Next(MatchCommandKind kind,string player="p1")
        {var s=processor.Snapshot(player);return new MatchCommand{commandId=Guid.NewGuid().ToString("N"),matchId=match.MatchId,playerId=player,sequence=s.nextSequence,kind=kind,round=s.round,phase=s.phase,playerRevision=s.playerRevision,shopRevision=s.shopRevision,placementRevision=s.placementRevision};}
        private string Owned(string player="p1"){var s=processor.Snapshot(player);s.nextSequence=0;return JsonUtility.ToJson(s);}
        [Test] public void DuplicatePurchaseAndChangedPayloadCannotChargeAgain()
        {
            var command=Next(MatchCommandKind.Buy);command.slot=0;var first=processor.Process("p1",command);Assert.That(first.accepted,Is.True);string state=Owned();
            var repeated=processor.Process("p1",command.Copy());Assert.That(repeated.accepted,Is.True);Assert.That(Owned(),Is.EqualTo(state));
            var changed=command.Copy();changed.slot=1;Assert.That(processor.Process("p1",changed).code,Is.EqualTo("DuplicateConflict"));Assert.That(Owned(),Is.EqualTo(state));
        }
        [Test] public void WrongOwnerStaleStateAndOldPhasePreserveOwnedState()
        {
            foreach(var change in new Action<MatchCommand>[] {c=>c.playerId="p2",c=>c.playerRevision=-1,c=>c.shopRevision=-1,c=>c.round++,c=>c.phase=MatchPhase.Combat,c=>c.protocol=999,c=>c.matchId="other"}){
                string before=Owned();var c=Next(MatchCommandKind.Buy);change(c);Assert.That(processor.Process("p1",c).accepted,Is.False);Assert.That(Owned(),Is.EqualTo(before));
            }
        }
        [Test] public void BuyingMovingSellingAndXPUseHostRules()
        {
            var buy=Next(MatchCommandKind.Buy);buy.slot=0;Assert.That(processor.Process("p1",buy).accepted,Is.True);var unit=match.GetPlayer("p1").Units.Single();
            var move=Next(MatchCommandKind.Move);move.unitId=unit.InstanceId;move.destination=PlacementKind.Board;Assert.That(processor.Process("p1",move).accepted,Is.True);Assert.That(unit.Placement.Kind,Is.EqualTo(PlacementKind.Board));
            var sell=Next(MatchCommandKind.Sell);sell.unitId=unit.InstanceId;Assert.That(processor.Process("p1",sell).accepted,Is.True);Assert.That(match.GetPlayer("p1").Gold,Is.EqualTo(50));
            Assert.That(processor.Process("p1",Next(MatchCommandKind.BuyXP)).accepted,Is.True);Assert.That(match.GetPlayer("p1").Gold,Is.EqualTo(46));match.Pool.AssertConservation(match);
        }
        [Test] public void OtherPlayersUnitAndAnonymousSenderAreDeniedWithoutPrivateLeak()
        {
            var buy=Next(MatchCommandKind.Buy,"p2");processor.Process("p2",buy);var unit=match.GetPlayer("p2").Units.Single();string before=Owned();
            var move=Next(MatchCommandKind.Move);move.unitId=unit.InstanceId;move.destination=PlacementKind.Board;Assert.That(processor.Process("p1",move).code,Is.EqualTo("UnitNotOwned"));Assert.That(Owned(),Is.EqualTo(before));
            var snapshot=processor.Snapshot("p1");Assert.That(snapshot.units,Is.Empty);Assert.That(snapshot.players.Select(p=>p.id),Does.Contain("p2"));Assert.That(processor.Process(null,Next(MatchCommandKind.Sync)).state,Is.Null);
        }
        [Test] public void CombatKeepsBuyAndBenchMovesButRejectsBoardEconomyActions()
        {
            match.TransitionTo(MatchPhase.Combat);var buy=Next(MatchCommandKind.Buy);Assert.That(processor.Process("p1",buy).accepted,Is.True);var unit=match.GetPlayer("p1").Units.Single();
            var move=Next(MatchCommandKind.Move);move.unitId=unit.InstanceId;move.destination=PlacementKind.Bench;move.bench=2;Assert.That(processor.Process("p1",move).accepted,Is.True);
            string before=Owned();move=Next(MatchCommandKind.Move);move.unitId=unit.InstanceId;move.destination=PlacementKind.Board;Assert.That(processor.Process("p1",move).accepted,Is.False);Assert.That(Owned(),Is.EqualTo(before));
            foreach(var kind in new[]{MatchCommandKind.Reroll,MatchCommandKind.BuyXP,MatchCommandKind.Sell}){var c=Next(kind);c.unitId=unit.InstanceId;before=Owned();Assert.That(processor.Process("p1",c).accepted,Is.False);Assert.That(Owned(),Is.EqualTo(before));}
        }
        [Test] public void SequenceGapAndEvictedDuplicateNeverMutate()
        {
            var buy=Next(MatchCommandKind.Buy);Assert.That(processor.Process("p1",buy).accepted,Is.True);
            for(int i=0;i<129;i++)Assert.That(processor.Process("p1",Next(MatchCommandKind.Sync)).accepted,Is.True);
            var before=Owned();Assert.That(processor.Process("p1",buy).code,Is.EqualTo("ExpiredCommand"));Assert.That(Owned(),Is.EqualTo(before));
            var gap=Next(MatchCommandKind.Buy);gap.sequence+=4;Assert.That(processor.Process("p1",gap).code,Is.EqualTo("SequenceGap"));Assert.That(Owned(),Is.EqualTo(before));
        }
        [Test] public void RerollLockAndSurrenderFollowExistingRules()
        {
            Assert.That(processor.Process("p1",Next(MatchCommandKind.Reroll)).accepted,Is.True);Assert.That(match.GetPlayer("p1").Gold,Is.EqualTo(48));
            var locked=Next(MatchCommandKind.Lock);locked.locked=true;Assert.That(processor.Process("p1",locked).accepted,Is.True);Assert.That(match.GetPlayer("p1").Shop.IsLocked,Is.True);
            Assert.That(processor.Process("p1",Next(MatchCommandKind.Surrender)).accepted,Is.True);Assert.That(match.GetPlayer("p1").IsEliminated,Is.True);Assert.That(match.Phase,Is.EqualTo(MatchPhase.Finished));match.Pool.AssertConservation(match);
        }
    }
}
