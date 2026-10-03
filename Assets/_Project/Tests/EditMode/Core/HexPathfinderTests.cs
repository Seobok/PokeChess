using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PokeChess.Core.Board;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Tests
{
    public sealed class HexPathfinderTests
    {
        internal static BoardPosition P(int c,int r)=>new BoardPosition(c,r);
        internal static string[] Snapshot(IReadOnlyCombatBoard b)=>b.Cells.Select(p=>b.TryGetOccupant(p,out var id)?id:null).ToArray();
        internal static Dictionary<BoardPosition,int> Bfs(IReadOnlyCombatBoard board, BoardPosition start)
        {
            var costs=new Dictionary<BoardPosition,int>{{start,0}};
            var queue=new Queue<BoardPosition>();queue.Enqueue(start);
            while(queue.Count>0)
            {
                var current=queue.Dequeue();
                foreach(var next in HexBoardBounds.Combat.Neighbors(current))
                    if(!board.IsOccupied(next)&&!costs.ContainsKey(next))
                    {costs.Add(next,costs[current]+1);queue.Enqueue(next);}
            }
            return costs;
        }
        internal static void ValidPath(IReadOnlyCombatBoard b,BoardPosition start,PathSearchResult result)
        {
            Assert.That(result.Path[0],Is.EqualTo(start));
            Assert.That(result.MoveCount,Is.EqualTo(result.Path.Count-1));
            Assert.That(result.Path.Distinct().Count(),Is.EqualTo(result.Path.Count));
            for(int i=1;i<result.Path.Count;i++)
            {
                Assert.That(b.Contains(result.Path[i]),Is.True);
                Assert.That(b.IsOccupied(result.Path[i]),Is.False);
                Assert.That(HexCoordinates.Distance(result.Path[i-1],result.Path[i]),Is.EqualTo(1));
            }
        }

        [Test]
        public void EmptyBoardAllPairsMatchHexDistance()
        {
            var b=new CombatBoard();
            foreach(var start in b.Cells)foreach(var target in b.Cells)
            {
                var r=HexPathfinder.FindPath(b.ReadOnly,start,target);
                Assert.That(r.Succeeded,Is.True);
                Assert.That(r.Destination.Value,Is.EqualTo(target));
                Assert.That(r.MoveCount,Is.EqualTo(HexCoordinates.Distance(start,target)));
                ValidPath(b,start,r);
            }
        }
        [Test]
        public void RandomOccupiedBoardsMatchIndependentBfs()
        {
            var rng=new Random(150);var start=P(0,0);
            for(int trial=0;trial<20;trial++)
            {
                var b=new CombatBoard();b.TryPlace("mover",start);
                foreach(var cell in b.Cells.Where(p=>!p.Equals(start)))
                    if(rng.NextDouble()<.28)b.TryPlace("block-"+cell.Column+"-"+cell.Row,cell);
                var before=Snapshot(b);var costs=Bfs(b,start);
                foreach(var destination in b.Cells)
                {
                    var result=HexPathfinder.FindPath(b.ReadOnly,start,destination);
                    if(destination.Equals(start))Assert.That(result.Status,Is.EqualTo(PathSearchStatus.MovementNotRequired));
                    else if(b.IsOccupied(destination))Assert.That(result.Error,Is.EqualTo(PathSearchError.DestinationOccupied));
                    else if(costs.TryGetValue(destination,out int expected))
                    {
                        Assert.That(result.Status,Is.EqualTo(PathSearchStatus.PathFound));
                        Assert.That(result.MoveCount,Is.EqualTo(expected));
                        ValidPath(b,start,result);
                    }
                    else Assert.That(result.Status,Is.EqualTo(PathSearchStatus.NoPath));
                }
                Assert.That(Snapshot(b),Is.EqualTo(before));
            }
        }
        [Test]
        public void BlockerOnDirectRouteRequiresDetour()
        {
            var b=new CombatBoard();b.TryPlace("self",P(0,0));b.TryPlace("ally",P(1,0));
            b.TryPlace("enemy",P(2,0));
            var r=HexPathfinder.FindPath(b,P(0,0),P(3,0));
            Assert.That(r.Status,Is.EqualTo(PathSearchStatus.PathFound));
            Assert.That(r.MoveCount,Is.GreaterThan(3));ValidPath(b,P(0,0),r);
        }
        [Test]
        public void SolidWallReturnsNoPathAndEmptyResult()
        {
            var b=new CombatBoard();
            for(int c=0;c<7;c++)b.TryPlace("wall-"+c,P(c,4));
            var r=HexPathfinder.FindPath(b,P(0,0),P(6,7));
            Assert.That(r.Status,Is.EqualTo(PathSearchStatus.NoPath));
            Assert.That(r.Path,Is.Empty);Assert.That(r.MoveCount,Is.EqualTo(-1));Assert.That(r.Destination,Is.Null);
        }
        [Test]
        public void SameOccupiedStartIsStationaryAndInvalidInputsAreDistinct()
        {
            var b=new CombatBoard();b.TryPlace("self",P(0,0));b.TryPlace("other",P(1,0));
            var r=HexPathfinder.FindPath(b,P(0,0),P(0,0));
            Assert.That(r.Status,Is.EqualTo(PathSearchStatus.MovementNotRequired));Assert.That(r.MoveCount,Is.Zero);
            Assert.That(HexPathfinder.FindPath(null,P(0,0),P(1,0)).Error,Is.EqualTo(PathSearchError.NullBoard));
            Assert.That(HexPathfinder.FindPath(b,P(7,0),P(1,0)).Error,Is.EqualTo(PathSearchError.OutOfBounds));
            Assert.That(HexPathfinder.FindPath(b,P(0,0),P(0,8)).Error,Is.EqualTo(PathSearchError.OutOfBounds));
            Assert.That(HexPathfinder.FindPath(b,P(0,0),P(1,0)).Error,Is.EqualTo(PathSearchError.DestinationOccupied));
        }
        [Test]
        public void RepeatedQueriesHaveSamePathAndResultsAreDetached()
        {
            var b=new CombatBoard();b.TryPlace("self",P(0,0));var r=HexPathfinder.FindPath(b,P(0,0),P(6,7));
            for(int i=0;i<20;i++)Assert.That(HexPathfinder.FindPath(b,P(0,0),P(6,7)).Path,Is.EqualTo(r.Path));
            var copy=r.Path.ToArray();b.TryPlace("new-blocker",r.Path[1]);
            Assert.That(r.Path,Is.EqualTo(copy));
            Assert.Throws<NotSupportedException>(()=>((IList<BoardPosition>)r.Path).Add(P(0,0)));
        }
    }
}
