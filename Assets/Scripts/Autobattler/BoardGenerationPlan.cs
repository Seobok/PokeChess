using System.Collections.Generic;

namespace PokeChess.Autobattler
{
    /// <summary>
    /// 네트워크 상태와 무관하게 보드 생성 계획(보드 수/좌표 순회)을 계산합니다.
    /// </summary>
    public static class BoardGenerationPlan
    {
        public const int MaxBoardCount = 8;

        public static int CalculateBoardCount(int originCount, int playerCount)
        {
            if (originCount <= 0 || playerCount <= 0)
            {
                return 0;
            }

            return System.Math.Min(playerCount, System.Math.Min(MaxBoardCount, originCount));
        }

        public static IEnumerable<HexCoord> EnumerateBoardCoords()
        {
            for (int r = 0; r < BoardManager.BoardHeight; r++)
            {
                for (int q = 0; q < BoardManager.BoardWidth; q++)
                {
                    yield return new HexCoord(q, r);
                }
            }
        }
    }
}
