using NUnit.Framework;
using PokeChess.Autobattler;

namespace PokeChess.Tests.Autobattler
{
    public class BoardGenerationPlanTests
    {
        [Test]
        public void CalculateBoardCount_ClampsByOriginPlayerAndMax()
        {
            int boardCount = BoardGenerationPlan.CalculateBoardCount(originCount: 10, playerCount: 9);

            Assert.AreEqual(BoardGenerationPlan.MaxBoardCount, boardCount);
        }

        [Test]
        public void CalculateBoardCount_ReturnsZeroWhenInputIsInvalid()
        {
            Assert.AreEqual(0, BoardGenerationPlan.CalculateBoardCount(originCount: 0, playerCount: 1));
            Assert.AreEqual(0, BoardGenerationPlan.CalculateBoardCount(originCount: 4, playerCount: 0));
        }

        [Test]
        public void EnumerateBoardCoords_ReturnsWholeBoard()
        {
            int count = 0;
            foreach (HexCoord _ in BoardGenerationPlan.EnumerateBoardCoords())
            {
                count++;
            }

            Assert.AreEqual(BoardManager.BoardWidth * BoardManager.BoardHeight, count);
        }
    }
}
