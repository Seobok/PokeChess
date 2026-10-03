using System;
using System.Collections.Generic;
using System.Linq;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Board
{
    public interface IReadOnlyCombatBoard
    {
        int CellCount { get; }
        int OccupiedCount { get; }
        IEnumerable<BoardPosition> Cells { get; }
        bool Contains(BoardPosition position);
        bool IsOccupied(BoardPosition position);
        bool TryGetOccupant(BoardPosition position, out string unitId);
        bool TryGetPosition(string unitId, out BoardPosition position);
        IReadOnlyList<BoardPosition> EmptyNeighbors(BoardPosition position);
    }

    /// <summary>Fixed 7x8 occupancy. Mutations run on the single simulation thread.</summary>
    public sealed class CombatBoard : IReadOnlyCombatBoard
    {
        private readonly string[] occupants = new string[56];
        private readonly Dictionary<string, BoardPosition> positions =
            new Dictionary<string, BoardPosition>(StringComparer.Ordinal);
        private readonly IReadOnlyCombatBoard readOnly;
        public CombatBoard() { readOnly = new ReadOnlyView(this); }
        public IReadOnlyCombatBoard ReadOnly => readOnly;
        public int CellCount => occupants.Length;
        public int OccupiedCount => positions.Count;
        public IEnumerable<BoardPosition> Cells => HexBoardBounds.Combat.Cells();
        public bool Contains(BoardPosition position) => HexBoardBounds.Combat.Contains(position);
        private static int Index(BoardPosition position) => position.Row * 7 + position.Column;
        private void RequireInside(BoardPosition position)
        {
            if (!Contains(position)) throw new ArgumentOutOfRangeException(nameof(position));
        }
        public bool IsOccupied(BoardPosition position)
        {
            RequireInside(position);
            return occupants[Index(position)] != null;
        }
        public bool TryGetOccupant(BoardPosition position, out string unitId)
        {
            RequireInside(position); // Out-of-bounds is not an empty cell.
            unitId = occupants[Index(position)];
            return unitId != null;
        }
        public bool TryGetPosition(string unitId, out BoardPosition position)
        {
            if (string.IsNullOrWhiteSpace(unitId)) { position = default; return false; }
            return positions.TryGetValue(unitId, out position);
        }
        public IReadOnlyList<BoardPosition> EmptyNeighbors(BoardPosition position) =>
            Array.AsReadOnly(HexBoardBounds.Combat.Neighbors(position).Where(p => !IsOccupied(p)).ToArray());

        public BoardOperationResult TryPlace(string unitId, BoardPosition destination)
        {
            if (string.IsNullOrWhiteSpace(unitId)) return BoardOperationResult.InvalidUnitId;
            if (!Contains(destination)) return BoardOperationResult.OutOfBounds;
            if (positions.ContainsKey(unitId)) return BoardOperationResult.UnitAlreadyPlaced;
            if (occupants[Index(destination)] != null) return BoardOperationResult.CellOccupied;
            positions.Add(unitId, destination);
            occupants[Index(destination)] = unitId;
            return BoardOperationResult.Success;
        }
        public BoardOperationResult TryMove(string unitId, BoardPosition destination)
        {
            if (string.IsNullOrWhiteSpace(unitId)) return BoardOperationResult.InvalidUnitId;
            if (!Contains(destination)) return BoardOperationResult.OutOfBounds;
            if (!positions.TryGetValue(unitId, out var source)) return BoardOperationResult.UnitNotFound;
            if (source.Equals(destination)) return BoardOperationResult.Success;
            if (occupants[Index(destination)] != null) return BoardOperationResult.CellOccupied;
            positions[unitId] = destination;
            occupants[Index(source)] = null;
            occupants[Index(destination)] = unitId;
            return BoardOperationResult.Success;
        }
        public BoardOperationResult TryRemove(string unitId)
        {
            if (string.IsNullOrWhiteSpace(unitId)) return BoardOperationResult.InvalidUnitId;
            if (!positions.TryGetValue(unitId, out var position)) return BoardOperationResult.UnitNotFound;
            positions.Remove(unitId);
            occupants[Index(position)] = null;
            return BoardOperationResult.Success;
        }

        // A facade prevents casting BattleState.Board to the mutable owner.
        private sealed class ReadOnlyView : IReadOnlyCombatBoard
        {
            private readonly CombatBoard board;
            public ReadOnlyView(CombatBoard board) { this.board = board; }
            public int CellCount => board.CellCount;
            public int OccupiedCount => board.OccupiedCount;
            public IEnumerable<BoardPosition> Cells => board.Cells;
            public bool Contains(BoardPosition p) => board.Contains(p);
            public bool IsOccupied(BoardPosition p) => board.IsOccupied(p);
            public bool TryGetOccupant(BoardPosition p, out string id) => board.TryGetOccupant(p, out id);
            public bool TryGetPosition(string id, out BoardPosition p) => board.TryGetPosition(id, out p);
            public IReadOnlyList<BoardPosition> EmptyNeighbors(BoardPosition p) => board.EmptyNeighbors(p);
        }
    }
}
