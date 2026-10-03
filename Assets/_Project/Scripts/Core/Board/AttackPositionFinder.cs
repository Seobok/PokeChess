using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Board
{
    public static class AttackPositionFinder
    {
        /// <summary>Finds a reachable empty attack cell for a supplied target, without selecting targets or changing occupancy.</summary>
        public static PathSearchResult FindPosition(IReadOnlyCombatBoard board, string attackerId, string targetId, int attackRange)
        {
            if (board == null) return PathSearchResult.Invalid(PathSearchError.NullBoard);
            if (attackRange < 1) return PathSearchResult.Invalid(PathSearchError.InvalidRange);
            if (!board.TryGetPosition(attackerId, out var start) || !board.TryGetPosition(targetId, out var target))
                return PathSearchResult.Invalid(PathSearchError.UnitNotFound);
            if (attackerId == targetId) return PathSearchResult.Invalid(PathSearchError.SameUnit);
            if (HexCoordinates.IsInRange(start, target, attackRange)) return PathSearchResult.Stationary(start);
            PathSearchResult best = null;
            foreach (var candidate in board.Cells)
            {
                if (board.IsOccupied(candidate) || !HexCoordinates.IsInRange(candidate, target, attackRange)) continue;
                var result = HexPathfinder.FindPath(board, start, candidate);
                if (!result.Succeeded) continue;
                if (best == null || result.MoveCount < best.MoveCount ||
                    (result.MoveCount == best.MoveCount && Earlier(candidate, best.Destination.Value)))
                    best = result;
            }
            return best ?? PathSearchResult.Unreachable();
        }
        private static bool Earlier(BoardPosition a, BoardPosition b) =>
            a.Row < b.Row || (a.Row == b.Row && a.Column < b.Column);
    }
}
