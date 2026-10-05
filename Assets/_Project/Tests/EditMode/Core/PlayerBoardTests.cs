using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;
using PokeChess.Core.Board;
using PokeChess.Core.Battle;

namespace PokeChess.Core.Tests
{
    public sealed class PlayerBoardTests
    {
        private static PokemonCatalog Catalog() => new PokemonCatalog(new[] {
            new PokemonDefinition("test","Test",1,new PokemonStats(100,10,0,1,0,0,1,2,0,0,.25f),"role","skill") });
        private static MatchState Create(MatchRules rules=null) => MatchStateFactory.Create("m",new[]{"p1","p2"},rules);
        private static UnitInstance Add(PlayerState p,string id,UnitPlacement placement) =>
            p.AddUnit(Catalog().CreateUnit(id,"test",p.PlayerId,UnitRank.One,0,placement));
        private static string Position(UnitInstance unit) => unit.Placement.Kind+":"+
            (unit.Placement.Position.HasValue ? unit.Placement.Position.Value.Column+","+unit.Placement.Position.Value.Row : "-")+
            ":"+unit.Placement.BenchSlot;
        private static string Snapshot(PlayerState p) => p.HP+"|"+p.Gold+"|"+p.XP+"|"+p.Level+"|"+p.DeployedUnitCount+"|"+p.RemainingDeploymentCapacity+
            "|"+string.Join(",",p.Units.Select(u=>u.InstanceId+":"+Position(u)+":"+u.PoolOriginDefinitionId+":"+string.Join("/",u.ItemInstanceIds)))+
            "|"+string.Join(",",p.BoardCells.Select(c=>c.Unit?.InstanceId ?? "-"))+"|"+string.Join(",",p.Bench.Select(u=>u?.InstanceId ?? "-"));
        private static void AssertViews(PlayerState p)
        {
            var cells=p.BoardCells;
            Assert.That(cells.Count,Is.EqualTo(p.Rules.BoardWidth*p.Rules.BoardHeight));
            for(int i=0;i<cells.Count;i++)
            {
                Assert.That(cells[i].Position,Is.EqualTo(new BoardPosition(i%p.Rules.BoardWidth,i/p.Rules.BoardWidth)));
                Assert.That(cells[i].Unit,Is.SameAs(p.GetBoardUnit(cells[i].Position)));
                Assert.That(cells[i].IsEmpty,Is.EqualTo(cells[i].Unit==null));
            }
            Assert.That(cells.Where(c=>!c.IsEmpty).Select(c=>c.Unit),Is.EqualTo(p.Board));
            Assert.That(p.DeployedUnitCount,Is.EqualTo(p.Board.Count));
            for(int slot=0;slot<p.Bench.Count;slot++) Assert.That(p.GetBenchUnit(slot),Is.SameAs(p.Bench[slot]));
            var placed=cells.Where(c=>!c.IsEmpty).Select(c=>c.Unit).Concat(p.Bench.Where(u=>u!=null)).ToArray();
            Assert.That(placed.Select(u=>u.InstanceId).Distinct().Count(),Is.EqualTo(placed.Length));
            Assert.That(p.Units.Count(u=>u.Placement.Kind!=PlacementKind.Unplaced),Is.EqualTo(placed.Length));
        }
        [Test] public void DefaultBoardHas28EmptyCellsAndBenchHas9EmptySlots()
        {
            var p=Create().GetPlayer("p1");Assert.That(p.BoardCells.Count,Is.EqualTo(28));Assert.That(p.Bench.Count,Is.EqualTo(9));
            Assert.That(p.BoardCells.All(c=>c.IsEmpty),Is.True);Assert.That(p.Bench.All(u=>u==null),Is.True);
            Assert.That(p.Board,Is.Empty);Assert.That(p.DeployedUnitCount,Is.Zero);Assert.That(p.RemainingDeploymentCapacity,Is.EqualTo(1));
            Assert.That(p.FindFirstEmptyBenchSlot(),Is.Zero);AssertViews(p);
        }
        [Test] public void EveryBoardCellSupportsPlacementAndSamePositionReassignmentAtLevelOne()
        {
            var p=Create().GetPlayer("p1");var unit=Add(p,"a",UnitPlacement.Unplaced);
            for(int row=0;row<4;row++) for(int column=0;column<7;column++)
            {
                var position=new BoardPosition(column,row);p.SetPlacement("a",UnitPlacement.OnBoard(position));
                Assert.That(p.GetBoardUnit(position),Is.SameAs(unit));Assert.That(p.DeployedUnitCount,Is.EqualTo(1));
                Assert.That(p.RemainingDeploymentCapacity,Is.Zero);Assert.That(p.BoardCells.Count(c=>!c.IsEmpty),Is.EqualTo(1));
                Assert.DoesNotThrow(()=>unit.SetPlacement(unit.Placement));AssertViews(p);
                Assert.That(HexCoordinates.ToOffset(HexCoordinates.ToAxial(position)),Is.EqualTo(position));
            }
        }
        [Test] public void EveryBenchSlotSupportsPlacementAndSameSlotReassignment()
        {
            var p=Create().GetPlayer("p1");var unit=Add(p,"a",UnitPlacement.Unplaced);
            for(int slot=0;slot<9;slot++)
            {
                p.SetPlacement("a",UnitPlacement.OnBench(slot));Assert.That(p.GetBenchUnit(slot),Is.SameAs(unit));
                Assert.That(p.Bench.Count(u=>u!=null),Is.EqualTo(1));Assert.That(p.FindFirstEmptyBenchSlot(),Is.EqualTo(slot==0 ? 1 : 0));
                Assert.DoesNotThrow(()=>unit.SetPlacement(unit.Placement));AssertViews(p);
            }
        }
        [TestCase(7,0)] [TestCase(0,4)] [TestCase(7,4)] [TestCase(int.MaxValue,int.MaxValue)]
        public void OutsideBoardQueryAndPlacementAreRejectedAtomically(int column,int row)
        {
            var p=Create().GetPlayer("p1");var unit=Add(p,"a",UnitPlacement.OnBoard(new BoardPosition(6,3)));var before=Snapshot(p);
            var position=new BoardPosition(column,row);
            Assert.Throws<ArgumentOutOfRangeException>(()=>p.GetBoardUnit(position));
            Assert.Throws<ArgumentOutOfRangeException>(()=>unit.SetPlacement(UnitPlacement.OnBoard(position)));
            Assert.Throws<ArgumentOutOfRangeException>(()=>Add(p,"b",UnitPlacement.OnBoard(position)));Assert.That(Snapshot(p),Is.EqualTo(before));
        }
        [TestCase(-1)] [TestCase(9)] [TestCase(int.MaxValue)]
        public void OutsideBenchQueryAndPlacementAreRejectedAtomically(int slot)
        {
            var p=Create().GetPlayer("p1");var unit=Add(p,"a",UnitPlacement.OnBench(8));var before=Snapshot(p);
            Assert.Throws<ArgumentOutOfRangeException>(()=>p.GetBenchUnit(slot));
            Assert.Throws<ArgumentOutOfRangeException>(()=>unit.SetPlacement(UnitPlacement.OnBench(slot)));Assert.That(Snapshot(p),Is.EqualTo(before));
        }
        [TestCase(-1,0)] [TestCase(0,-1)] public void NegativeCoordinatesCannotBeCreated(int column,int row)
        { Assert.Throws<ArgumentOutOfRangeException>(()=>new BoardPosition(column,row)); }
        [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)]
        [TestCase(6)] [TestCase(7)] [TestCase(8)] [TestCase(9)] [TestCase(10)]
        public void LevelLimitsDeploymentButAllowsMovingExistingUnitAtCapacity(int level)
        {
            var p=Create().GetPlayer("p1");p.SetProgress(5,0,level);
            for(int i=0;i<level;i++) Add(p,"board"+i,UnitPlacement.OnBoard(new BoardPosition(i%7,i/7)));
            var extra=Add(p,"extra",UnitPlacement.OnBench(0));var before=Snapshot(p);
            Assert.That(p.DeployedUnitCount,Is.EqualTo(level));Assert.That(p.RemainingDeploymentCapacity,Is.Zero);
            Assert.Throws<InvalidOperationException>(()=>extra.SetPlacement(UnitPlacement.OnBoard(new BoardPosition(6,3))));Assert.That(Snapshot(p),Is.EqualTo(before));
            p.SetPlacement("board0",UnitPlacement.OnBoard(new BoardPosition(6,3)));Assert.That(p.GetBoardUnit(new BoardPosition(0,0)),Is.Null);
            Assert.That(p.GetBoardUnit(new BoardPosition(6,3)).InstanceId,Is.EqualTo("board0"));Assert.That(p.RemainingDeploymentCapacity,Is.Zero);AssertViews(p);
        }
        [Test] public void XpLevelUpAndBenchReturnUpdateRemainingCapacity()
        {
            var p=Create().GetPlayer("p1");Add(p,"a",UnitPlacement.OnBoard(new BoardPosition(0,0)));var b=Add(p,"b",UnitPlacement.OnBench(0));
            new LevelSystem().GrantXP(p,2);Assert.That(p.Level,Is.EqualTo(2));Assert.That(p.RemainingDeploymentCapacity,Is.EqualTo(1));
            b.SetPlacement(UnitPlacement.OnBoard(new BoardPosition(6,3)));Assert.That(p.RemainingDeploymentCapacity,Is.Zero);
            p.SetPlacement("a",UnitPlacement.OnBench(0));Assert.That(p.RemainingDeploymentCapacity,Is.EqualTo(1));
            Assert.That(p.BoardCells.Count,Is.EqualTo(28));Assert.That(p.Gold,Is.EqualTo(5));AssertViews(p);
        }
        [Test] public void OccupiedBoardAndBenchRejectMovesWithoutPartialChanges()
        {
            var p=Create().GetPlayer("p1");p.SetProgress(5,0,2);
            var a=Add(p,"a",UnitPlacement.OnBoard(new BoardPosition(0,0)));var b=Add(p,"b",UnitPlacement.OnBoard(new BoardPosition(6,3)));
            Add(p,"c",UnitPlacement.OnBench(0));Add(p,"d",UnitPlacement.OnBench(8));var before=Snapshot(p);
            Assert.Throws<InvalidOperationException>(()=>a.SetPlacement(b.Placement));
            Assert.Throws<InvalidOperationException>(()=>p.SetPlacement("c",UnitPlacement.OnBench(8)));
            Assert.Throws<InvalidOperationException>(()=>p.SetPlacement("b",UnitPlacement.OnBench(0)));
            Assert.Throws<InvalidOperationException>(()=>Add(p,"e",UnitPlacement.OnBoard(new BoardPosition(6,3))));Assert.That(Snapshot(p),Is.EqualTo(before));AssertViews(p);
        }
        [Test] public void AllNineBenchSlotsCanFillAndLowestFreedSlotIsFound()
        {
            var p=Create().GetPlayer("p1");for(int i=8;i>=0;i--) Add(p,"b"+i,UnitPlacement.OnBench(i));
            Assert.That(p.FindFirstEmptyBenchSlot(),Is.Null);p.SetPlacement("b6",UnitPlacement.Unplaced);p.SetPlacement("b2",UnitPlacement.Unplaced);
            Assert.That(p.FindFirstEmptyBenchSlot(),Is.EqualTo(2));p.SetPlacement("b2",UnitPlacement.OnBoard(new BoardPosition(6,3)));
            Assert.That(p.FindFirstEmptyBenchSlot(),Is.EqualTo(2));p.SetPlacement("b6",UnitPlacement.OnBench(2));
            Assert.That(p.FindFirstEmptyBenchSlot(),Is.EqualTo(6));AssertViews(p);
        }
        [Test] public void BoardBenchUnplacedMovesAndRemovalLeaveNoGhostOccupancy()
        {
            var p=Create().GetPlayer("p1");var unit=Add(p,"a",UnitPlacement.OnBoard(new BoardPosition(6,3)));
            unit.SetPlacement(UnitPlacement.OnBench(8));Assert.That(p.GetBoardUnit(new BoardPosition(6,3)),Is.Null);AssertViews(p);
            unit.SetPlacement(UnitPlacement.Unplaced);Assert.That(p.GetBenchUnit(8),Is.Null);Assert.That(p.DeployedUnitCount,Is.Zero);AssertViews(p);
            unit.SetPlacement(UnitPlacement.OnBoard(new BoardPosition(0,0)));AssertViews(p);p.RemoveUnit("a");
            Assert.That(p.GetBoardUnit(new BoardPosition(0,0)),Is.Null);Assert.That(p.FindFirstEmptyBenchSlot(),Is.Zero);AssertViews(p);
        }
        [Test] public void PlayersMayUseSameCoordinateAndSlotWithoutSharingOwnership()
        {
            var m=Create();var p1=m.GetPlayer("p1");var p2=m.GetPlayer("p2");var position=new BoardPosition(6,3);
            var a=Add(p1,"a",UnitPlacement.OnBoard(position));var b=Add(p2,"b",UnitPlacement.OnBoard(position));
            var c=Add(p1,"c",UnitPlacement.OnBench(8));var d=Add(p2,"d",UnitPlacement.OnBench(8));
            var before1=Snapshot(p1);var before2=Snapshot(p2);
            Assert.Throws<KeyNotFoundException>(()=>p1.SetPlacement("b",UnitPlacement.OnBench(0)));
            Assert.Throws<ArgumentException>(()=>p1.AddUnit(b));Assert.That(Snapshot(p1),Is.EqualTo(before1));Assert.That(Snapshot(p2),Is.EqualTo(before2));
            Assert.That(p1.GetBoardUnit(position),Is.SameAs(a));Assert.That(p2.GetBoardUnit(position),Is.SameAs(b));
            Assert.That(p1.GetBenchUnit(8),Is.SameAs(c));Assert.That(p2.GetBenchUnit(8),Is.SameAs(d));AssertViews(p1);AssertViews(p2);
        }
        [Test] public void ReadOnlyMembershipSnapshotsRequireFreshQueriesAfterMoves()
        {
            var p=Create().GetPlayer("p1");var unit=Add(p,"a",UnitPlacement.OnBoard(new BoardPosition(0,0)));
            var cells=p.BoardCells;var bench=p.Bench;
            Assert.Throws<NotSupportedException>(()=>((IList<PlayerBoardCell>)cells).Clear());
            Assert.Throws<NotSupportedException>(()=>((IList<PlayerBoardCell>)cells)[0]=null);
            Assert.Throws<NotSupportedException>(()=>((IList<UnitInstance>)bench)[0]=unit);
            unit.SetPlacement(UnitPlacement.OnBench(0));Assert.That(cells[0].Unit,Is.SameAs(unit));Assert.That(bench[0],Is.Null);
            Assert.That(p.BoardCells[0].IsEmpty,Is.True);Assert.That(p.GetBenchUnit(0),Is.SameAs(unit));AssertViews(p);
        }
        [Test] public void ViewsHonorExplicitDimensionsAndDoNotAssumeDefaults()
        {
            var p=Create(new MatchRules(startingLevel:4,maxLevel:4,boardWidth:2,boardHeight:3,benchCapacity:2)).GetPlayer("p1");
            Assert.That(p.BoardCells.Count,Is.EqualTo(6));Assert.That(p.Bench.Count,Is.EqualTo(2));
            Add(p,"a",UnitPlacement.OnBoard(new BoardPosition(1,2)));Add(p,"b",UnitPlacement.OnBench(1));
            Assert.That(p.FindFirstEmptyBenchSlot(),Is.Zero);Assert.That(p.RemainingDeploymentCapacity,Is.EqualTo(3));AssertViews(p);
        }
        [Test] public void LevelReductionBelowDeploymentIsAtomic()
        {
            var p=Create().GetPlayer("p1");p.SetProgress(20,1,3);
            for(int i=0;i<3;i++) Add(p,"a"+i,UnitPlacement.OnBoard(new BoardPosition(i,0)));
            var before=Snapshot(p);Assert.Throws<InvalidOperationException>(()=>p.SetProgress(99,4,2));Assert.That(Snapshot(p),Is.EqualTo(before));AssertViews(p);
        }
        [Test] public void SharedPoolBuyMoveSellAndEliminationKeepViewsConsistent()
        {
            var catalog=Catalog();var m=MatchStateFactory.CreateWithPool("m",new[]{"p1","p2"},catalog,new[]{"test"});
            m.TransitionTo(MatchPhase.Starting);m.TransitionTo(MatchPhase.Preparation);var p=m.GetPlayer("p1");new ShopSystem().RefreshForRound(m,"p1");
            var trades=new ShopTransactionSystem(catalog);var a=trades.Buy(m,"p1",0,p.Shop.Revision);a.AddItem("kept-item");
            Assert.That(a.Placement.BenchSlot,Is.Zero);AssertViews(p);
            int gold=p.Gold;var revision=p.Shop.Revision;var rng=p.Shop.RandomState;var origin=a.PoolOriginDefinitionId;
            p.SetPlacement(a.InstanceId,UnitPlacement.OnBoard(new BoardPosition(6,3)));AssertViews(p);
            p.SetPlacement(a.InstanceId,UnitPlacement.OnBench(8));AssertViews(p);
            Assert.That(p.Gold,Is.EqualTo(gold));Assert.That(p.Shop.Revision,Is.EqualTo(revision));Assert.That(p.Shop.RandomState,Is.EqualTo(rng));
            Assert.That(a.PoolOriginDefinitionId,Is.EqualTo(origin));Assert.That(a.ItemInstanceIds,Is.EqualTo(new[]{"kept-item"}));
            Assert.That(m.Pool.GetStock("test").Available,Is.EqualTo(21));m.Pool.AssertConservation(m);
            var b=trades.Buy(m,"p1",1,p.Shop.Revision);Assert.That(b.Placement.BenchSlot,Is.Zero);
            b.SetPlacement(UnitPlacement.OnBoard(new BoardPosition(3,2)));b.AddItem("returned-item");AssertViews(p);
            trades.Sell(m,"p1",b.InstanceId);Assert.That(p.GetBoardUnit(new BoardPosition(3,2)),Is.Null);
            Assert.That(p.ItemInventory,Is.EqualTo(new[]{"returned-item"}));Assert.That(m.Pool.GetStock("test").Available,Is.EqualTo(21));AssertViews(p);
            p.SetHP(0);SharedPoolSystem.ReleasePlayerHoldings(m,"p1");Assert.That(p.BoardCells.All(c=>c.IsEmpty),Is.True);
            Assert.That(p.Bench.All(u=>u==null),Is.True);Assert.That(m.Pool.GetStock("test").Available,Is.EqualTo(22));AssertViews(p);m.Pool.AssertConservation(m);
        }
        [Test] public void BattleIncludesOnlyDeployedUnitsAndCombatCannotChangePreparationState()
        {
            var catalog=Catalog();var m=MatchStateFactory.CreateWithPool("m",new[]{"p1","p2"},catalog,new[]{"test"});
            var p1=m.GetPlayer("p1");var p2=m.GetPlayer("p2");
            SharedPoolSystem.RegisterUnit(m,catalog.CreateUnit("a","test","p1",UnitRank.One,0,UnitPlacement.OnBoard(new BoardPosition(6,3)),new[]{"item"}),"test");
            SharedPoolSystem.RegisterUnit(m,catalog.CreateUnit("bench-a","test","p1",UnitRank.One,0,UnitPlacement.OnBench(8)),"test");
            SharedPoolSystem.RegisterUnit(m,catalog.CreateUnit("b","test","p2",UnitRank.One,0,UnitPlacement.OnBoard(new BoardPosition(6,0))),"test");
            SharedPoolSystem.RegisterUnit(m,catalog.CreateUnit("bench-b","test","p2",UnitRank.One,0,UnitPlacement.OnBench(0)),"test");
            var before1=Snapshot(p1);var before2=Snapshot(p2);int stock=m.Pool.GetStock("test").Available;
            // Opponent mapping is explicit test setup; the round-loop adapter belongs to WBS 2.11.
            var setup=p1.Board.Select(u=>new BattleUnitSetup(u,1,u.Placement.Position.Value))
                .Concat(p2.Board.Select(u=>new BattleUnitSetup(u,2,new BoardPosition(6,4))));
            var battle=BattleStateFactory.Create("battle",1,123,30,catalog,setup);
            Assert.That(battle.Units.Select(u=>u.UnitInstanceId),Is.EquivalentTo(new[]{"a","b"}));
            Assert.That(battle.TryMoveUnit("a",new BoardPosition(5,3)),Is.EqualTo(BoardOperationResult.Success));
            var simulation=new BattleSimulation(battle);
            for(int calls=0;calls<1351 && battle.Result==BattleResult.InProgress;calls++) simulation.Step();
            Assert.That(battle.Result,Is.Not.EqualTo(BattleResult.InProgress));Assert.That(battle.Units.Any(u=>!u.IsAlive),Is.True);
            Assert.That(Snapshot(p1),Is.EqualTo(before1));Assert.That(Snapshot(p2),Is.EqualTo(before2));
            Assert.That(m.Pool.GetStock("test").Available,Is.EqualTo(stock));m.Pool.AssertConservation(m);AssertViews(p1);AssertViews(p2);
        }
    }
}
