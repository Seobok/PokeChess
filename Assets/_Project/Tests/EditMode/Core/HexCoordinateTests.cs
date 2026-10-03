using System;
using System.Linq;
using NUnit.Framework;
using PokeChess.Core.Board;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Tests
{
    public sealed class HexCoordinateTests
    {
        [Test]
        public void EveryCombatCellRoundTrips()
        {
            var cells = HexBoardBounds.Combat.Cells().ToArray();
            Assert.That(cells.Length, Is.EqualTo(56));
            foreach (var cell in cells)
                Assert.That(HexCoordinates.ToOffset(HexCoordinates.ToAxial(cell)), Is.EqualTo(cell));
        }

        [Test]
        public void OddRExamplesMatchTheChosenLayout()
        {
            Assert.That(HexCoordinates.ToAxial(new BoardPosition(3,0)), Is.EqualTo(new AxialPosition(3,0)));
            Assert.That(HexCoordinates.ToAxial(new BoardPosition(3,1)), Is.EqualTo(new AxialPosition(3,1)));
            Assert.That(HexCoordinates.ToAxial(new BoardPosition(3,2)), Is.EqualTo(new AxialPosition(2,2)));
            Assert.That(HexCoordinates.ToAxial(new BoardPosition(0,7)), Is.EqualTo(new AxialPosition(-3,7)));
        }

        [Test]
        public void OddAndEvenRowsHaveExpectedNeighbors()
        {
            var even = HexBoardBounds.Combat.Neighbors(new BoardPosition(3,2)).ToArray();
            var odd = HexBoardBounds.Combat.Neighbors(new BoardPosition(3,3)).ToArray();
            Assert.That(even, Is.EquivalentTo(new[] {
                new BoardPosition(4,2),new BoardPosition(3,1),new BoardPosition(2,1),
                new BoardPosition(2,2),new BoardPosition(2,3),new BoardPosition(3,3) }));
            Assert.That(odd, Is.EquivalentTo(new[] {
                new BoardPosition(4,3),new BoardPosition(4,2),new BoardPosition(3,2),
                new BoardPosition(2,3),new BoardPosition(3,4),new BoardPosition(4,4) }));
        }

        [Test]
        public void DistancesAreSymmetricAndMatchNeighborRelationsForAllPairs()
        {
            var cells = HexBoardBounds.Combat.Cells().ToArray();
            foreach (var a in cells)
            {
                Assert.That(HexCoordinates.Distance(a,a), Is.Zero);
                foreach (var b in cells)
                {
                    int distance = HexCoordinates.Distance(a,b);
                    Assert.That(distance, Is.EqualTo(HexCoordinates.Distance(b,a)));
                    Assert.That(distance == 1, Is.EqualTo(HexBoardBounds.Combat.Neighbors(a).Contains(b)));
                }
            }
            Assert.That(HexCoordinates.Distance(new AxialPosition(0,0),new AxialPosition(3,-2)), Is.EqualTo(3));
        }

        [Test]
        public void DistancesEqualShortestPathsOnTheCombatBoard()
        {
            foreach (var start in HexBoardBounds.Combat.Cells())
            {
                var lengths = new System.Collections.Generic.Dictionary<BoardPosition,int> { [start] = 0 };
                var queue = new System.Collections.Generic.Queue<BoardPosition>();
                queue.Enqueue(start);
                while(queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    foreach(var neighbor in HexBoardBounds.Combat.Neighbors(current))
                        if(!lengths.ContainsKey(neighbor)) { lengths.Add(neighbor,lengths[current]+1); queue.Enqueue(neighbor); }
                }
                foreach(var entry in lengths)
                    Assert.That(HexCoordinates.Distance(start,entry.Key), Is.EqualTo(entry.Value));
            }
        }

        [Test]
        public void EdgeNeighborsAreClippedAndReciprocal()
        {
            Assert.That(HexBoardBounds.Combat.Neighbors(new BoardPosition(0,0)).Count(), Is.EqualTo(2));
            Assert.That(HexBoardBounds.Combat.Neighbors(new BoardPosition(6,7)).Count(), Is.EqualTo(2));
            foreach (var cell in HexBoardBounds.Combat.Cells())
                foreach(var neighbor in HexBoardBounds.Combat.Neighbors(cell))
                {
                    Assert.That(HexBoardBounds.Combat.Contains(neighbor), Is.True);
                    Assert.That(HexBoardBounds.Combat.Neighbors(neighbor), Does.Contain(cell));
                }
        }

        [TestCase(0,1)]
        [TestCase(1,7)]
        [TestCase(2,19)]
        [TestCase(3,37)]
        public void UnboundedRadiusHasExpectedSizeAndDistances(int radius,int count)
        {
            var center = new AxialPosition(-2,-3);
            var cells = HexCoordinates.WithinRadius(center,radius).ToArray();
            Assert.That(cells.Length, Is.EqualTo(count));
            Assert.That(cells.Distinct().Count(), Is.EqualTo(count));
            Assert.That(cells, Does.Contain(center));
            Assert.That(HexCoordinates.WithinRadius(center,radius,false).Count(), Is.EqualTo(count-1));
            foreach(var cell in cells) Assert.That(HexCoordinates.Distance(center,cell), Is.LessThanOrEqualTo(radius));
        }

        [Test]
        public void BoardRangesMatchDistanceAndHaveStableOrder()
        {
            foreach(var center in HexBoardBounds.Combat.Cells())
                for(int radius=0;radius<=3;radius++)
                {
                    var range=HexBoardBounds.Combat.WithinRadius(center,radius).ToArray();
                    var expected=HexBoardBounds.Combat.Cells().Where(c=>HexCoordinates.Distance(center,c)<=radius).ToArray();
                    Assert.That(range, Is.EqualTo(expected));
                    Assert.That(HexBoardBounds.Combat.WithinRadius(center,radius,false), Has.No.Member(center));
                }
            var origin=new AxialPosition(0,0);
            Assert.That(HexCoordinates.Neighbors(origin), Is.EqualTo(new[] {
                new AxialPosition(1,0),new AxialPosition(1,-1),new AxialPosition(0,-1),
                new AxialPosition(-1,0),new AxialPosition(-1,1),new AxialPosition(0,1) }));
            Assert.That(HexCoordinates.WithinRadius(origin,2), Is.EqualTo(HexCoordinates.WithinRadius(origin,2)));
        }

        [Test]
        public void MirroringPreservesDistanceForEveryPairAndMovesPreparationToOpponentHalf()
        {
            var cells=HexBoardBounds.Combat.Cells().ToArray();
            foreach(var a in cells)
            {
                Assert.That(HexCoordinates.MirrorCombat(HexCoordinates.MirrorCombat(a)), Is.EqualTo(a));
                foreach(var b in cells)
                    Assert.That(HexCoordinates.Distance(a,b), Is.EqualTo(HexCoordinates.Distance(
                        HexCoordinates.MirrorCombat(a),HexCoordinates.MirrorCombat(b))));
            }
            foreach(var cell in HexBoardBounds.Preparation.Cells())
            {
                var mirrored=HexCoordinates.MirrorCombat(cell);
                Assert.That(mirrored.Row, Is.InRange(4,7));
                Assert.That(HexBoardBounds.Combat.Contains(mirrored), Is.True);
            }
        }

        [Test]
        public void InvalidCoordinatesAndArgumentsAreHandledExplicitly()
        {
            Assert.That(HexCoordinates.TryToOffset(new AxialPosition(0,-1),out _), Is.False);
            Assert.That(HexCoordinates.TryToOffset(new AxialPosition(-1,0),out _), Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(()=>HexCoordinates.ToOffset(new AxialPosition(-1,0)));
            Assert.That(HexBoardBounds.Combat.Contains(new AxialPosition(-4,7)), Is.False);
            Assert.That(HexBoardBounds.Combat.Contains(new BoardPosition(7,0)), Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(()=>HexCoordinates.WithinRadius(default,-1));
            Assert.Throws<ArgumentOutOfRangeException>(()=>HexCoordinates.IsInRange(default(AxialPosition),default,-1));
            Assert.Throws<ArgumentOutOfRangeException>(()=>HexCoordinates.Neighbor(default,6));
            Assert.Throws<ArgumentOutOfRangeException>(()=>HexCoordinates.MirrorCombat(new BoardPosition(0,8)));
            Assert.Throws<ArgumentOutOfRangeException>(()=>HexBoardBounds.Combat.Neighbors(new BoardPosition(7,0)));
            Assert.Throws<ArgumentOutOfRangeException>(()=>new HexBoardBounds(0,8));
            Assert.Throws<OverflowException>(()=>HexCoordinates.Distance(new AxialPosition(int.MinValue,0),new AxialPosition(int.MaxValue,0)));
        }
    }
}
