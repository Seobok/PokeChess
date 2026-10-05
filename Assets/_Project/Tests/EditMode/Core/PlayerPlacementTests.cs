using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Tests
{
    public sealed class PlayerPlacementTests
    {
        private PokemonCatalog catalog;
        private MatchState match;
        private PlayerState player;
        private readonly PlayerPlacementSystem commands=new PlayerPlacementSystem();
        [SetUp] public void Setup()
        {
            catalog=new PokemonCatalog(new[]{new PokemonDefinition("test","Test",1,new PokemonStats(100,10,0,1,0,0,1,2,0,0,.25f),"role","skill")});
            match=MatchStateFactory.CreateWithPool("m",new[]{"p1","p2"},catalog,new[]{"test"});player=match.GetPlayer("p1");player.SetProgress(5,0,2);
            Register("A",UnitPlacement.OnBoard(new BoardPosition(0,0)));Register("B",UnitPlacement.OnBoard(new BoardPosition(1,0)));
            Register("C",UnitPlacement.OnBench(0));Register("D",UnitPlacement.OnBench(8));player.GetUnit("A").AddItem("item");player.AddInventoryItem("inventory");
            match.TransitionTo(MatchPhase.Starting);match.TransitionTo(MatchPhase.Preparation);
        }
        private UnitInstance Register(string id,UnitPlacement placement) => SharedPoolSystem.RegisterUnit(match,
            catalog.CreateUnit(id,"test","p1",UnitRank.One,0,placement),"test");
        private static string Place(UnitPlacement p) => p.Kind+":"+(p.Position.HasValue ? p.Position.Value.Column+","+p.Position.Value.Row : "-")+":"+p.BenchSlot;
        private string Metadata() => player.Gold+"|"+player.XP+"|"+player.Level+"|"+player.HP+"|"+player.Shop.Revision+"|"+player.Shop.RandomState+"|"+player.Shop.RandomDrawCount+
            "|"+string.Join(",",player.Units.Select(u=>u.InstanceId+":"+u.OwnerPlayerId+":"+u.DefinitionId+":"+u.Rank+":"+u.EvolutionStage+":"+u.PoolOriginDefinitionId+":"+string.Join("/",u.ItemInstanceIds)))+
            "|"+string.Join(",",player.ItemInventory)+"|"+string.Join(",",match.Pool.Stocks.Select(s=>s.Available));
        private string Snapshot() => Metadata()+"|"+player.PlacementRevision+"|"+string.Join(",",player.Units.Select(u=>u.InstanceId+":"+Place(u.Placement)));
        private PlacementCommandResult Move(string id,UnitPlacement destination,long? revision=null) => commands.Move(match,"p1",id,destination,revision ?? player.PlacementRevision);
        private void Reject(string id,UnitPlacement destination,PlacementRejectReason reason,long? revision=null)
        {
            var before=Snapshot();var result=Move(id,destination,revision);
            Assert.That(result.Outcome,Is.EqualTo(PlacementOutcome.Rejected));Assert.That(result.Reason,Is.EqualTo(reason));Assert.That(Snapshot(),Is.EqualTo(before));match.Pool.AssertConservation(match);
        }
        private void AssertViews()
        {
            foreach(var unit in player.Units)
            {
                if(unit.Placement.Kind==PlacementKind.Board) Assert.That(player.GetBoardUnit(unit.Placement.Position.Value),Is.SameAs(unit));
                if(unit.Placement.Kind==PlacementKind.Bench) Assert.That(player.GetBenchUnit(unit.Placement.BenchSlot.Value),Is.SameAs(unit));
            }
            Assert.That(player.BoardCells.Count(c=>!c.IsEmpty),Is.EqualTo(player.DeployedUnitCount));match.Pool.AssertConservation(match);
        }
        [TestCase("A",PlacementKind.Board,6,3)] [TestCase("A",PlacementKind.Bench,3,0)]
        [TestCase("C",PlacementKind.Bench,4,0)] [TestCase("C",PlacementKind.Board,6,3)]
        public void EmptyMovesPreserveResourcesReferencesAndAdvanceRevisionOnce(string id,PlacementKind kind,int x,int y)
        {
            if(id=="C" && kind==PlacementKind.Board) player.SetProgress(5,0,3);
            var source=player.GetUnit(id);var destination=kind==PlacementKind.Board ? UnitPlacement.OnBoard(new BoardPosition(x,y)) : UnitPlacement.OnBench(x);
            var before=Metadata();var revision=player.PlacementRevision;var result=Move(id,destination);
            Assert.That(result.Outcome,Is.EqualTo(PlacementOutcome.Moved));Assert.That(Place(source.Placement),Is.EqualTo(Place(destination)));
            Assert.That(player.GetUnit(id),Is.SameAs(source));Assert.That(player.PlacementRevision,Is.EqualTo(revision+1));Assert.That(Metadata(),Is.EqualTo(before));AssertViews();
        }
        [TestCase("A","B")] [TestCase("B","A")] [TestCase("A","C")]
        [TestCase("C","A")] [TestCase("C","D")] [TestCase("D","C")]
        public void AllSwapDirectionsAreAtomicAtBoardCapacity(string a,string b)
        {
            var source=player.GetUnit(a);var target=player.GetUnit(b);var from=source.Placement;var to=target.Placement;
            var before=Metadata();var revision=player.PlacementRevision;var result=Move(a,to);
            Assert.That(result.Outcome,Is.EqualTo(PlacementOutcome.Swapped));Assert.That(result.TargetUnitId,Is.EqualTo(b));
            Assert.That(Place(source.Placement),Is.EqualTo(Place(to)));Assert.That(Place(target.Placement),Is.EqualTo(Place(from)));
            Assert.That(player.GetUnit(a),Is.SameAs(source));Assert.That(player.GetUnit(b),Is.SameAs(target));Assert.That(player.PlacementRevision,Is.EqualTo(revision+1));
            Assert.That(player.DeployedUnitCount,Is.EqualTo(2));Assert.That(Metadata(),Is.EqualTo(before));AssertViews();
        }
        [Test] public void BenchToEmptyBoardRejectsAtCapacity()
        { Reject("C",UnitPlacement.OnBoard(new BoardPosition(6,3)),PlacementRejectReason.BoardLimit); }
        [Test] public void SameLocationIsAcceptedWithoutRevisionOrMutation()
        {
            var before=Snapshot();var result=Move("A",player.GetUnit("A").Placement);
            Assert.That(result.Outcome,Is.EqualTo(PlacementOutcome.Unchanged));Assert.That(result.Accepted,Is.True);Assert.That(Snapshot(),Is.EqualTo(before));
        }
        [Test] public void PreviewIsPureAndDoesNotConsumeRevision()
        {
            var before=Snapshot();var result=commands.Preview(match,"p1","C",player.GetUnit("A").Placement,player.PlacementRevision);
            Assert.That(result.Outcome,Is.EqualTo(PlacementOutcome.Swapped));Assert.That(result.TargetUnitId,Is.EqualTo("A"));Assert.That(Snapshot(),Is.EqualTo(before));
        }
        [TestCase(7,0)] [TestCase(0,4)] public void OutOfBoundsBoardRejects(int column,int row)
        { Reject("A",UnitPlacement.OnBoard(new BoardPosition(column,row)),PlacementRejectReason.InvalidDestination); }
        [Test] public void OutOfBoundsBenchAndUnplacedDestinationReject()
        {
            Reject("A",UnitPlacement.OnBench(9),PlacementRejectReason.InvalidDestination);
            Reject("A",UnitPlacement.Unplaced,PlacementRejectReason.InvalidDestination);
        }
        [Test] public void UnplacedSourceAndMissingUnitReject()
        {
            player.SetPlacement("C",UnitPlacement.Unplaced);Reject("C",UnitPlacement.OnBench(3),PlacementRejectReason.InvalidSource);
            Reject("missing",UnitPlacement.OnBench(3),PlacementRejectReason.UnitNotOwned);
            Reject(" ",UnitPlacement.OnBench(3),PlacementRejectReason.UnitNotOwned);
        }
        [Test] public void ForeignUnitAndUnknownPlayerRejectWithoutTouchingEitherPlayer()
        {
            var p2=match.GetPlayer("p2");var other=p2.AddUnit(catalog.CreateUnit("foreign","test","p2",UnitRank.One,0,UnitPlacement.OnBench(0)));
            Reject("foreign",UnitPlacement.OnBench(3),PlacementRejectReason.UnitNotOwned);Assert.That(other.Placement.BenchSlot,Is.Zero);
            var before=Snapshot();Assert.That(commands.Move(match,"missing","A",UnitPlacement.OnBench(3),0).Reason,Is.EqualTo(PlacementRejectReason.UnknownPlayer));Assert.That(Snapshot(),Is.EqualTo(before));
        }
        [TestCase(MatchPhase.Waiting)] [TestCase(MatchPhase.Starting)] [TestCase(MatchPhase.Combat)]
        [TestCase(MatchPhase.Result)] [TestCase(MatchPhase.Finished)]
        public void BoardMovesRejectOutsidePreparation(MatchPhase phase)
        {
            if(phase==MatchPhase.Waiting || phase==MatchPhase.Starting)
            {
                match=MatchStateFactory.CreateWithPool("other",new[]{"p1","p2"},catalog,new[]{"test"});player=match.GetPlayer("p1");Register("A",UnitPlacement.OnBench(0));
                if(phase==MatchPhase.Starting) match.TransitionTo(MatchPhase.Starting);
            }
            else
            {
                if(phase==MatchPhase.Combat || phase==MatchPhase.Result) match.TransitionTo(MatchPhase.Combat);
                if(phase==MatchPhase.Result) match.TransitionTo(MatchPhase.Result);
                if(phase==MatchPhase.Finished) match.TransitionTo(MatchPhase.Finished);
            }
            Reject("A",UnitPlacement.OnBench(3),PlacementRejectReason.InvalidPhase);
        }
        [TestCase(false)] [TestCase(true)]
        public void CombatBenchMovesAndSwapsPreserveBoardAndResources(bool swap)
        {
            match.TransitionTo(MatchPhase.Combat);
            var board=string.Join(",",player.Board.Select(u=>u.InstanceId+":"+Place(u.Placement)));
            var destination=swap ? player.GetUnit("D").Placement : UnitPlacement.OnBench(3);
            var revision=player.PlacementRevision;var before=Metadata();var source=player.GetUnit("C");var target=player.GetUnit("D");var oldSource=source.Placement;
            var snapshot=Snapshot();var preview=commands.Preview(match,"p1","C",destination,revision);
            Assert.That(preview.Accepted,Is.True);Assert.That(Snapshot(),Is.EqualTo(snapshot));
            var result=Move("C",destination);
            Assert.That(result.Outcome,Is.EqualTo(swap ? PlacementOutcome.Swapped : PlacementOutcome.Moved));
            Assert.That(Place(source.Placement),Is.EqualTo(Place(destination)));
            if(swap) Assert.That(Place(target.Placement),Is.EqualTo(Place(oldSource)));
            Assert.That(player.GetUnit("C"),Is.SameAs(source));Assert.That(player.GetUnit("D"),Is.SameAs(target));
            Assert.That(player.PlacementRevision,Is.EqualTo(revision+1));Assert.That(Metadata(),Is.EqualTo(before));
            Assert.That(string.Join(",",player.Board.Select(u=>u.InstanceId+":"+Place(u.Placement))),Is.EqualTo(board));AssertViews();
            Reject("D",UnitPlacement.OnBench(4),PlacementRejectReason.StaleRevision,revision);
        }
        [TestCase("A",PlacementKind.Board,6,3)] [TestCase("A",PlacementKind.Board,1,0)]
        [TestCase("A",PlacementKind.Bench,3,0)] [TestCase("A",PlacementKind.Bench,0,0)]
        [TestCase("C",PlacementKind.Board,6,3)] [TestCase("C",PlacementKind.Board,0,0)]
        public void CombatRejectsEveryMoveOrSwapTouchingBoard(string id,PlacementKind kind,int x,int y)
        {
            match.TransitionTo(MatchPhase.Combat);
            Reject(id,kind==PlacementKind.Board ? UnitPlacement.OnBoard(new BoardPosition(x,y)) : UnitPlacement.OnBench(x),PlacementRejectReason.InvalidPhase);
        }
        [Test] public void CombatBenchNoOpAndInvalidSlotRespectExistingRules()
        {
            match.TransitionTo(MatchPhase.Combat);var before=Snapshot();
            Assert.That(Move("C",player.GetUnit("C").Placement).Outcome,Is.EqualTo(PlacementOutcome.Unchanged));Assert.That(Snapshot(),Is.EqualTo(before));
            Reject("C",UnitPlacement.OnBench(9),PlacementRejectReason.InvalidDestination);
            player.SetHP(0);Reject("C",UnitPlacement.OnBench(3),PlacementRejectReason.Eliminated);
        }
        [Test] public void ZeroHpAndEliminatedPlayersReject()
        {
            player.SetHP(0);Reject("A",UnitPlacement.OnBench(3),PlacementRejectReason.Eliminated);
            SharedPoolSystem.ReleasePlayerHoldings(match,"p1");Reject("A",UnitPlacement.OnBench(3),PlacementRejectReason.Eliminated);
        }
        [Test] public void DuplicateRequestAfterMoveIsStaleAndDoesNotSwapBack()
        {
            var revision=player.PlacementRevision;var target=player.GetUnit("B").Placement;Move("A",target,revision);
            Reject("A",target,PlacementRejectReason.StaleRevision,revision);
        }
        [TestCase("buy")] [TestCase("sell")] [TestCase("placement")] [TestCase("add")] [TestCase("remove")]
        public void OtherPlacementChangesInvalidateCapturedDrag(string operation)
        {
            player.AddUnit(catalog.CreateUnit("gift","test","p1",UnitRank.One,0,UnitPlacement.Unplaced));var revision=player.PlacementRevision;
            if(operation=="buy") { new ShopSystem().RefreshForRound(match,"p1");new ShopTransactionSystem(catalog).Buy(match,"p1",0,player.Shop.Revision); }
            if(operation=="sell") new ShopTransactionSystem(catalog).Sell(match,"p1","A");
            if(operation=="placement") player.GetUnit("A").SetPlacement(UnitPlacement.OnBoard(new BoardPosition(6,3)));
            if(operation=="add") player.AddUnit(catalog.CreateUnit("new","test","p1",UnitRank.One,0,UnitPlacement.Unplaced));
            if(operation=="remove") player.RemoveUnit("gift");
            Reject("D",UnitPlacement.OnBench(3),PlacementRejectReason.StaleRevision,revision);
        }
        [Test] public void SameStoragePlacementDoesNotAdvanceRevision()
        {
            var unit=player.GetUnit("A");var before=Snapshot();unit.SetPlacement(unit.Placement);player.SetPlacement("A",unit.Placement);Assert.That(Snapshot(),Is.EqualTo(before));
        }
        [Test] public void FailedStorageRegistrationAndMoveDoNotAdvanceRevision()
        {
            var before=Snapshot();Assert.Throws<InvalidOperationException>(()=>player.SetPlacement("C",UnitPlacement.OnBoard(new BoardPosition(6,3))));
            Assert.Throws<InvalidOperationException>(()=>Register("extra",UnitPlacement.OnBench(0)));Assert.That(Snapshot(),Is.EqualTo(before));
        }
        [Test] public void RevisionOverflowRejectsAtomicSwapAndStorageChanges()
        {
            typeof(PlayerState).GetField("<PlacementRevision>k__BackingField",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(player,long.MaxValue);
            Reject("A",player.GetUnit("B").Placement,PlacementRejectReason.RevisionOverflow);
            var before=Snapshot();Assert.Throws<OverflowException>(()=>player.SetPlacement("A",UnitPlacement.OnBench(3)));
            Assert.Throws<OverflowException>(()=>player.RemoveUnit("A"));Assert.Throws<OverflowException>(()=>Register("extra",UnitPlacement.OnBench(3)));
            Assert.That(Snapshot(),Is.EqualTo(before));Assert.That(Move("A",player.GetUnit("A").Placement).Outcome,Is.EqualTo(PlacementOutcome.Unchanged));
        }
        [Test] public void EliminationPreflightsAllPlacementRevisionIncrements()
        {
            typeof(PlayerState).GetField("<PlacementRevision>k__BackingField",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(player,long.MaxValue-1);
            player.SetHP(0);var before=Snapshot();Assert.Throws<OverflowException>(()=>SharedPoolSystem.ReleasePlayerHoldings(match,"p1"));
            Assert.That(Snapshot(),Is.EqualTo(before));Assert.That(player.IsEliminated,Is.False);AssertViews();
        }
    }
}
