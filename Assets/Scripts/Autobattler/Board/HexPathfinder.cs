using System.Collections.Generic;

namespace PokeChess.Autobattler
{
    /// <summary>
    /// 단일 유닛 이동을 위한 Hex A* 경로 탐색 유틸리티입니다. (멀티 보드 지원)
    /// </summary>
    public static class HexPathfinder
    {
        /// <summary>
        /// boardIndex 보드 안에서 start -> goal 경로를 찾습니다.
        /// goal은 비어있어야 하며(=점유 시 실패), start==goal이면 path는 [start]를 반환합니다.
        /// </summary>
        public static bool TryFindPath(BoardManager board, byte boardIndex, HexCoord start, HexCoord goal, out List<HexCoord> path)
        {
            path = new List<HexCoord>();

            if (board == null || !board.IsInside(start) || !board.IsInside(goal))
                return false;

            // 목적지가 점유되어 있으면 이동 경로 불가 (기존 정책 유지)
            if (start != goal && board.IsOccupied(boardIndex, goal))
                return false;

            if (start == goal)
            {
                path.Add(start);
                return true;
            }

            var openSet = new List<HexCoord> { start };
            var cameFrom = new Dictionary<HexCoord, HexCoord>();
            var gScore = new Dictionary<HexCoord, int> { [start] = 0 };
            var fScore = new Dictionary<HexCoord, int> { [start] = HexCoord.Distance(start, goal) };
            var closed = new HashSet<HexCoord>();

            while (openSet.Count > 0)
            {
                HexCoord current = GetLowestFScore(openSet, fScore);

                if (current == goal)
                {
                    BuildPath(cameFrom, current, path);
                    return true;
                }

                openSet.Remove(current);
                closed.Add(current);

                foreach (HexCoord neighbor in board.GetNeighbors(current))
                {
                    if (closed.Contains(neighbor))
                        continue;

                    // ✅ 멀티 보드 점유 체크
                    if (board.IsOccupied(boardIndex, neighbor))
                        continue;

                    int tentativeG = gScore[current] + 1;

                    if (gScore.TryGetValue(neighbor, out int oldG) && tentativeG >= oldG)
                        continue;

                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeG;
                    fScore[neighbor] = tentativeG + HexCoord.Distance(neighbor, goal);

                    if (!openSet.Contains(neighbor))
                        openSet.Add(neighbor);
                }
            }

            return false;
        }

        /// <summary>
        /// (옵션) 기존 호출부를 최대한 안 바꾸고 싶을 때: boardIndex=0으로 동작
        /// </summary>
        public static bool TryFindPath(BoardManager board, HexCoord start, HexCoord goal, out List<HexCoord> path)
        {
            return TryFindPath(board, 0, start, goal, out path);
        }

        private static HexCoord GetLowestFScore(List<HexCoord> candidates, Dictionary<HexCoord, int> fScore)
        {
            HexCoord best = candidates[0];
            int bestScore = GetScoreOrMax(fScore, best);

            for (int i = 1; i < candidates.Count; i++)
            {
                HexCoord candidate = candidates[i];
                int score = GetScoreOrMax(fScore, candidate);
                if (score < bestScore)
                {
                    best = candidate;
                    bestScore = score;
                }
            }

            return best;
        }

        private static int GetScoreOrMax(Dictionary<HexCoord, int> dict, HexCoord coord)
            => dict.TryGetValue(coord, out int value) ? value : int.MaxValue;

        private static void BuildPath(Dictionary<HexCoord, HexCoord> cameFrom, HexCoord current, List<HexCoord> path)
        {
            path.Clear();
            path.Add(current);

            while (cameFrom.TryGetValue(current, out HexCoord previous))
            {
                current = previous;
                path.Add(current);
            }

            path.Reverse();
        }
    }
}