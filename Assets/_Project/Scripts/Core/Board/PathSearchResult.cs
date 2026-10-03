using System;
using System.Collections.Generic;
using System.Linq;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Board
{
    public enum PathSearchStatus { PathFound, MovementNotRequired, NoPath, InvalidInput }
    public enum PathSearchError { None, NullBoard, OutOfBounds, DestinationOccupied, UnitNotFound, InvalidRange, SameUnit }

    public sealed class PathSearchResult
    {
        public PathSearchStatus Status { get; }
        public PathSearchError Error { get; }
        public IReadOnlyList<BoardPosition> Path { get; }
        public bool Succeeded => Status == PathSearchStatus.PathFound || Status == PathSearchStatus.MovementNotRequired;
        public int MoveCount => Succeeded ? Path.Count - 1 : -1;
        public BoardPosition? Destination => Succeeded ? (BoardPosition?)Path[Path.Count - 1] : null;

        private PathSearchResult(PathSearchStatus status, PathSearchError error, IEnumerable<BoardPosition> path)
        { Status = status; Error = error; Path = Array.AsReadOnly(path.ToArray()); }
        internal static PathSearchResult Found(IEnumerable<BoardPosition> path) =>
            new PathSearchResult(PathSearchStatus.PathFound, PathSearchError.None, path);
        internal static PathSearchResult Stationary(BoardPosition position) =>
            new PathSearchResult(PathSearchStatus.MovementNotRequired, PathSearchError.None, new[] { position });
        internal static PathSearchResult Unreachable() =>
            new PathSearchResult(PathSearchStatus.NoPath, PathSearchError.None, Array.Empty<BoardPosition>());
        internal static PathSearchResult Invalid(PathSearchError error) =>
            new PathSearchResult(PathSearchStatus.InvalidInput, error, Array.Empty<BoardPosition>());
    }
}
