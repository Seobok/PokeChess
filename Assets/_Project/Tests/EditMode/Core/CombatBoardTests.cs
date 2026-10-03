using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PokeChess.Core.Board;
using PokeChess.Core.Battle;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Tests
{
    public sealed class CombatBoardTests
    {
        private static BoardPosition P(int c,int r) => new BoardPosition(c,r);
        private static void Consistent(IReadOnlyCombatBoard board)
        {
            var ids=new HashSet<string>();
            foreach(var cell in board.Cells)
            {
                bool occupied=board.TryGetOccupant(cell,out var id);
                Assert.That(board.IsOccupied(cell),Is.EqualTo(occupied));
                if(!occupied) continue;
                Assert.That(ids.Add(id),Is.True);
                Assert.That(board.TryGetPosition(id,out var p),Is.True);
                Assert.That(p,Is.EqualTo(cell));
            }
            Assert.That(ids.Count,Is.EqualTo(board.OccupiedCount));
        }
        private static string[] Snapshot(IReadOnlyCombatBoard board) =>
            board.Cells.Select(p=>board.TryGetOccupant(p,out var id)?id:null).ToArray();

        [Test]
        public void NewBoardHas56UniqueEmptyCells()
        {
            var b=new CombatBoard();
            Assert.That(b.CellCount,Is.EqualTo(56));
            Assert.That(b.Cells.Distinct().Count(),Is.EqualTo(56));
            Assert.That(b.OccupiedCount,Is.Zero);
            Assert.That(b.Cells.All(p=>!b.IsOccupied(p)),Is.True);
            Consistent(b);
        }
        [Test]
        public void FullBoardCanBeRemovedAndReused()
        {
            var b=new CombatBoard(); int i=0;
            foreach(var cell in b.Cells) Assert.That(b.TryPlace("u"+i++,cell),Is.EqualTo(BoardOperationResult.Success));
            Assert.That(b.OccupiedCount,Is.EqualTo(56));
            Consistent(b);
            foreach(var cell in b.Cells)
            {
                b.TryGetOccupant(cell,out var id);
                Assert.That(b.TryRemove(id),Is.EqualTo(BoardOperationResult.Success));
                Assert.That(b.TryPlace("new-"+id,cell),Is.EqualTo(BoardOperationResult.Success));
                Assert.That(b.TryGetPosition(id,out _),Is.False);
            }
            Consistent(b);
        }
        [Test]
        public void FailedChangesPreserveOccupancy()
        {
            var b=new CombatBoard(); b.TryPlace("a",P(0,0)); b.TryPlace("b",P(1,0));
            var before=Snapshot(b);
            Assert.That(b.TryPlace("a",P(2,0)),Is.EqualTo(BoardOperationResult.UnitAlreadyPlaced));
            Assert.That(b.TryPlace("c",P(1,0)),Is.EqualTo(BoardOperationResult.CellOccupied));
            Assert.That(b.TryPlace("c",P(7,0)),Is.EqualTo(BoardOperationResult.OutOfBounds));
            Assert.That(b.TryMove("a",P(1,0)),Is.EqualTo(BoardOperationResult.CellOccupied));
            Assert.That(b.TryMove("a",P(0,8)),Is.EqualTo(BoardOperationResult.OutOfBounds));
            Assert.That(b.TryMove("missing",P(2,0)),Is.EqualTo(BoardOperationResult.UnitNotFound));
            Assert.That(b.TryRemove("missing"),Is.EqualTo(BoardOperationResult.UnitNotFound));
            Assert.That(b.TryPlace(" ",P(2,0)),Is.EqualTo(BoardOperationResult.InvalidUnitId));
            Assert.That(b.TryMove(null,P(2,0)),Is.EqualTo(BoardOperationResult.InvalidUnitId));
            Assert.That(b.TryRemove(""),Is.EqualTo(BoardOperationResult.InvalidUnitId));
            Assert.That(Snapshot(b),Is.EqualTo(before)); Consistent(b);
        }
        [Test]
        public void MoveAndSameCellRequestsAreConsistent()
        {
            var b=new CombatBoard(); b.TryPlace("a",P(2,3)); var before=Snapshot(b);
            Assert.That(b.TryMove("a",P(2,3)),Is.EqualTo(BoardOperationResult.Success));
            Assert.That(Snapshot(b),Is.EqualTo(before));
            Assert.That(b.TryMove("a",P(3,3)),Is.EqualTo(BoardOperationResult.Success));
            Assert.That(b.IsOccupied(P(2,3)),Is.False);
            Assert.That(b.TryGetOccupant(P(3,3),out var id),Is.True);
            Assert.That(id,Is.EqualTo("a")); Consistent(b);
        }
        [Test]
        public void OutsideQueriesAreNotEmptyCells()
        {
            var b=new CombatBoard();
            Assert.That(b.TryGetOccupant(P(0,0),out var id),Is.False); Assert.That(id,Is.Null);
            Assert.Throws<ArgumentOutOfRangeException>(()=>b.IsOccupied(P(7,0)));
            Assert.Throws<ArgumentOutOfRangeException>(()=>b.TryGetOccupant(P(0,8),out _));
            Assert.That(b.TryGetPosition(null,out _),Is.False);
        }
        [Test]
        public void EmptyNeighborsAreClippedAndOrdered()
        {
            var b=new CombatBoard(); var center=P(3,3);
            var neighbors=HexBoardBounds.Combat.Neighbors(center).ToArray();
            b.TryPlace("blocker",neighbors[1]);
            Assert.That(b.EmptyNeighbors(center),Is.EqualTo(neighbors.Where(p=>!p.Equals(neighbors[1]))));
            Assert.That(b.EmptyNeighbors(P(0,0)).Count,Is.EqualTo(2));
        }
        [Test]
        public void OneThousandOperationsMatchReferenceMap()
        {
            var b=new CombatBoard(); var map=new Dictionary<string,BoardPosition>(); var rng=new Random(140);
            for(int i=0;i<1000;i++)
            {
                string id="u"+rng.Next(20); var dest=P(rng.Next(8),rng.Next(9)); int op=rng.Next(3);
                var expected=BoardOperationResult.Success;
                if(op!=2 && (dest.Column>=7||dest.Row>=8)) expected=BoardOperationResult.OutOfBounds;
                else if(op==0 && map.ContainsKey(id)) expected=BoardOperationResult.UnitAlreadyPlaced;
                else if(op!=0 && !map.ContainsKey(id)) expected=BoardOperationResult.UnitNotFound;
                else if(op!=2 && map.Any(pair=>pair.Key!=id&&pair.Value.Equals(dest))) expected=BoardOperationResult.CellOccupied;
                var actual=op==0?b.TryPlace(id,dest):op==1?b.TryMove(id,dest):b.TryRemove(id);
                Assert.That(actual,Is.EqualTo(expected));
                if(actual==BoardOperationResult.Success) { if(op==2)map.Remove(id);else map[id]=dest; }
                Assert.That(b.OccupiedCount,Is.EqualTo(map.Count));
                foreach(var pair in map) { Assert.That(b.TryGetPosition(pair.Key,out var p),Is.True);Assert.That(p,Is.EqualTo(pair.Value)); }
                Consistent(b);
            }
        }
        private static PokemonCatalog Catalog()=>new PokemonCatalog(new[]{
            new PokemonDefinition("test","Test",1,new PokemonStats(100,10,0,1,0,0,1,2,100,0,.25f),"role","skill")});
        private static UnitInstance Unit(PokemonCatalog c,string id,string owner)=>
            c.CreateUnit(id,"test",owner,UnitRank.One,0,UnitPlacement.OnBoard(P(0,0)));
        private static BattleState Battle(PokemonCatalog c,BoardPosition a,BoardPosition b)=>
            BattleStateFactory.Create("b",1,0,30,c,new[]{
                new BattleUnitSetup(Unit(c,"a","p1"),1,a),new BattleUnitSetup(Unit(c,"b","p2"),2,b)});
        [Test]
        public void InitialSetupIsValidatedBeforeStatsResolution()
        {
            var c=Catalog();int calls=0;
            var same=new[]{new BattleUnitSetup(Unit(c,"a","p1"),1,P(0,0)),new BattleUnitSetup(Unit(c,"b","p2"),2,P(0,0))};
            Assert.Throws<ArgumentException>(()=>BattleStateFactory.Create("b",1,0,30,c,same,(u,d)=>{calls++;return d.BaseStats;}));
            Assert.That(calls,Is.Zero);
            Assert.Throws<ArgumentException>(()=>Battle(c,P(0,0),P(7,0)));
            Assert.That(Battle(c,P(0,0),P(6,7)).Board.OccupiedCount,Is.EqualTo(2));
        }
        [Test]
        public void BattleMovesBothPositionAndOccupancyButNotOwnedPlacement()
        {
            var c=Catalog();var source=Unit(c,"a","p1");
            var b=BattleStateFactory.Create("b",1,0,30,c,new[]{
                new BattleUnitSetup(source,1,P(0,0)),new BattleUnitSetup(Unit(c,"b","p2"),2,P(6,7))});
            Assert.That(b.TryMoveUnit("a",P(1,0)),Is.EqualTo(BoardOperationResult.Success));
            Assert.That(b.Units[0].Position,Is.EqualTo(P(1,0)));
            Assert.That(source.Placement.Position.Value,Is.EqualTo(P(0,0)));
            Assert.That(b.TryMoveUnit("a",P(6,7)),Is.EqualTo(BoardOperationResult.CellOccupied));
            Assert.That(b.Units[0].Position,Is.EqualTo(P(1,0)));
            Assert.That(b.Board is CombatBoard,Is.False);
            Assert.That(typeof(UnitCombatState).GetMethod("SetPosition"),Is.Null); Consistent(b.Board);
        }
        [Test]
        public void RemovedUnitsKeepHistoryButCannotMoveOrBeTargeted()
        {
            var b=Battle(Catalog(),P(0,0),P(6,7));
            Assert.That(b.TryRemoveUnit("a"),Is.EqualTo(BoardOperationResult.Success));
            Assert.That(b.Units.Count,Is.EqualTo(2));
            Assert.That(b.Units[0].Position,Is.EqualTo(P(0,0)));
            Assert.That(b.Units[0].IsOnBoard,Is.False);
            Assert.That(b.Units[0].IsTargetable,Is.False);
            Assert.That(b.TryMoveUnit("a",P(1,0)),Is.EqualTo(BoardOperationResult.UnitNotFound));
            Assert.That(b.TryRemoveUnit("a"),Is.EqualTo(BoardOperationResult.UnitNotFound));
            Assert.That(b.Board.IsOccupied(P(0,0)),Is.False);
            Assert.That(b.TryMoveUnit("b",P(0,0)),Is.EqualTo(BoardOperationResult.Success));Consistent(b.Board);
        }
    }
}
