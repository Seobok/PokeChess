using System;
using System.Linq;
using NUnit.Framework;
using PokeChess.Core.Board;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Tests
{
    public sealed class AttackPositionFinderTests
    {
        private static BoardPosition P(int c,int r)=>new BoardPosition(c,r);
        private static CombatBoard Board(BoardPosition start,BoardPosition target)
        {var b=new CombatBoard();b.TryPlace("self",start);b.TryPlace("target",target);return b;}

        [Test]
        public void AlreadyInRangeReturnsCurrentCell()
        {
            var b=Board(P(2,2),P(3,2));
            var r=AttackPositionFinder.FindPosition(b,"self","target",1);
            Assert.That(r.Status,Is.EqualTo(PathSearchStatus.MovementNotRequired));
            Assert.That(r.Path,Is.EqualTo(new[]{P(2,2)}));
        }
        [Test]
        public void RandomSetupsChooseShortestReachableAttackCellWithStableTieBreak()
        {
            var rng=new Random(151);var start=P(0,0);var target=P(6,7);
            for(int trial=0;trial<40;trial++)
            {
                var b=Board(start,target);int range=1+trial%3;
                foreach(var cell in b.Cells.Where(p=>!p.Equals(start)&&!p.Equals(target)))
                    if(rng.NextDouble()<.22)b.TryPlace("block-"+cell.Column+"-"+cell.Row,cell);
                var before=HexPathfinderTests.Snapshot(b);
                var costs=HexPathfinderTests.Bfs(b,start);
                var candidates=b.Cells.Where(p=>!b.IsOccupied(p)&&HexCoordinates.Distance(p,target)<=range&&costs.ContainsKey(p))
                    .OrderBy(p=>costs[p]).ThenBy(p=>p.Row).ThenBy(p=>p.Column).ToArray();
                var result=AttackPositionFinder.FindPosition(b.ReadOnly,"self","target",range);
                if(candidates.Length==0)Assert.That(result.Status,Is.EqualTo(PathSearchStatus.NoPath));
                else
                {
                    Assert.That(result.Status,Is.EqualTo(PathSearchStatus.PathFound));
                    Assert.That(result.Destination.Value,Is.EqualTo(candidates[0]));
                    Assert.That(result.MoveCount,Is.EqualTo(costs[candidates[0]]));
                    HexPathfinderTests.ValidPath(b,start,result);
                    Assert.That(result.Path.Contains(target),Is.False);
                    Assert.That(HexCoordinates.Distance(result.Destination.Value,target),Is.LessThanOrEqualTo(range));
                    Assert.That(AttackPositionFinder.FindPosition(b,"self","target",range).Path,Is.EqualTo(result.Path));
                }
                Assert.That(HexPathfinderTests.Snapshot(b),Is.EqualTo(before));
            }
        }
        [Test]
        public void BlockedAttackCellsAndTrappedAttackerReturnNoPath()
        {
            var b=Board(P(0,0),P(4,4));int i=0;
            foreach(var cell in HexBoardBounds.Combat.Neighbors(P(4,4)))b.TryPlace("guard-"+i++,cell);
            Assert.That(AttackPositionFinder.FindPosition(b,"self","target",1).Status,Is.EqualTo(PathSearchStatus.NoPath));
            b=Board(P(0,0),P(6,7));i=0;
            foreach(var cell in HexBoardBounds.Combat.Neighbors(P(0,0)))b.TryPlace("trap-"+i++,cell);
            Assert.That(AttackPositionFinder.FindPosition(b,"self","target",2).Status,Is.EqualTo(PathSearchStatus.NoPath));
        }
        [Test]
        public void OccupiedNearestCandidateDoesNotPreventFindingAnother()
        {
            var b=Board(P(0,0),P(4,0));b.TryPlace("guard",P(3,0));
            var result=AttackPositionFinder.FindPosition(b,"self","target",1);
            Assert.That(result.Status,Is.EqualTo(PathSearchStatus.PathFound));
            Assert.That(result.Path.Contains(P(3,0)),Is.False);
            Assert.That(result.Path.Contains(P(4,0)),Is.False);
            Assert.That(HexCoordinates.Distance(result.Destination.Value,P(4,0)),Is.EqualTo(1));
        }
        [Test]
        public void InvalidInputAndRemovedTargetsAreRejected()
        {
            var b=Board(P(0,0),P(6,7));
            Assert.That(AttackPositionFinder.FindPosition(null,"self","target",1).Error,Is.EqualTo(PathSearchError.NullBoard));
            Assert.That(AttackPositionFinder.FindPosition(b,"self","target",0).Error,Is.EqualTo(PathSearchError.InvalidRange));
            Assert.That(AttackPositionFinder.FindPosition(b,null,"target",1).Error,Is.EqualTo(PathSearchError.UnitNotFound));
            Assert.That(AttackPositionFinder.FindPosition(b,"self","self",1).Error,Is.EqualTo(PathSearchError.SameUnit));
            b.TryRemove("target");
            Assert.That(AttackPositionFinder.FindPosition(b,"self","target",1).Error,Is.EqualTo(PathSearchError.UnitNotFound));
        }
    }
}
