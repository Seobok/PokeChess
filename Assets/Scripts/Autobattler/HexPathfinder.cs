using System.Collections.Generic;

namespace PokeChess.Autobattler
{
    /// <summary>
    /// 단일 유닛 이동을 위한 Hex A* 경로 탐색 유틸리티입니다.
    /// </summary>
    public static class HexPathfinder
    {
        public static bool TryFindPath(BoardManager board, HexCoord start, HexCoord goal, out List<HexCoord> path)
        {
            path = new List<HexCoord>();

            if (board == null || board.IsInside(start) == false || board.IsInside(goal) == false)
            {
                return false;
            }

            if (start != goal && board.IsOccupied(goal))
            {
                return false;
            }

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
                    {
                        continue;
                    }

                    if (board.IsOccupied(neighbor))
                    {
                        continue;
                    }

                    int tentativeG = gScore[current] + 1;
                    if (gScore.TryGetValue(neighbor, out int oldG) && tentativeG >= oldG)
                    {
                        continue;
                    }

                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeG;
                    fScore[neighbor] = tentativeG + HexCoord.Distance(neighbor, goal);

                    if (openSet.Contains(neighbor) == false)
                    {
                        openSet.Add(neighbor);
                    }
                }
            }

            return false;
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
        {
            return dict.TryGetValue(coord, out int value) ? value : int.MaxValue;
        }

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
