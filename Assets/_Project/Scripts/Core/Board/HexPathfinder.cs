using System.Collections.Generic;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Board
{
    public static class HexPathfinder
    {
        /// <summary>Path includes start and destination. Start may contain the mover; every other cell must be empty.</summary>
        public static PathSearchResult FindPath(IReadOnlyCombatBoard board, BoardPosition start, BoardPosition destination)
        {
            if (board == null) return PathSearchResult.Invalid(PathSearchError.NullBoard);
            if (!board.Contains(start) || !board.Contains(destination)) return PathSearchResult.Invalid(PathSearchError.OutOfBounds);
            if (start.Equals(destination)) return PathSearchResult.Stationary(start);
            if (board.IsOccupied(destination)) return PathSearchResult.Invalid(PathSearchError.DestinationOccupied);

            var open = new List<BoardPosition> { start };
            var closed = new HashSet<BoardPosition>();
            var costs = new Dictionary<BoardPosition, int> { [start] = 0 };
            var parents = new Dictionary<BoardPosition, BoardPosition>();
            while (open.Count > 0)
            {
                int best = 0;
                for (int i = 1; i < open.Count; i++)
                    if (Compare(open[i], open[best], destination, costs) < 0) best = i;
                var current = open[best];
                open.RemoveAt(best);
                if (current.Equals(destination))
                {
                    var path = new List<BoardPosition> { current };
                    while (!current.Equals(start)) { current = parents[current]; path.Add(current); }
                    path.Reverse();
                    return PathSearchResult.Found(path);
                }
                closed.Add(current);
                foreach (var neighbor in board.EmptyNeighbors(current))
                {
                    if (closed.Contains(neighbor)) continue;
                    int nextCost = costs[current] + 1;
                    if (costs.TryGetValue(neighbor, out int previous) && nextCost >= previous) continue;
                    costs[neighbor] = nextCost;
                    parents[neighbor] = current;
                    if (!open.Contains(neighbor)) open.Add(neighbor);
                }
            }
            return PathSearchResult.Unreachable();
        }

        private static int Compare(BoardPosition a, BoardPosition b, BoardPosition destination, Dictionary<BoardPosition,int> costs)
        {
            int ah = HexCoordinates.Distance(a, destination), bh = HexCoordinates.Distance(b, destination);
            int order = (costs[a] + ah).CompareTo(costs[b] + bh);
            if (order != 0) return order;
            order = ah.CompareTo(bh);
            if (order != 0) return order;
            order = a.Row.CompareTo(b.Row);
            return order != 0 ? order : a.Column.CompareTo(b.Column);
        }
    }
}
