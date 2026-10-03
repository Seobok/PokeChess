using System;
using System.Collections.Generic;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Board
{
    public sealed class HexBoardBounds
    {
        public static HexBoardBounds Combat { get; } = new HexBoardBounds(7, 8);
        public static HexBoardBounds Preparation { get; } = new HexBoardBounds(7, 4);
        public int Columns { get; }
        public int Rows { get; }
        public HexBoardBounds(int columns, int rows)
        {
            if (columns < 1 || rows < 1) throw new ArgumentOutOfRangeException();
            Columns = columns;
            Rows = rows;
        }
        public bool Contains(BoardPosition position) => position.Column < Columns && position.Row < Rows;
        public bool Contains(AxialPosition position) => HexCoordinates.TryToOffset(position, out var offset) && Contains(offset);

        /// <summary>Row-major cell order. No occupancy or unit state is stored here.</summary>
        public IEnumerable<BoardPosition> Cells()
        {
            for (int row = 0; row < Rows; row++)
                for (int column = 0; column < Columns; column++)
                    yield return new BoardPosition(column,row);
        }
        public IEnumerable<BoardPosition> Neighbors(BoardPosition position)
        {
            RequireInside(position);
            return EnumerateNeighbors(position);
        }
        private IEnumerable<BoardPosition> EnumerateNeighbors(BoardPosition position)
        {
            foreach (var neighbor in HexCoordinates.Neighbors(HexCoordinates.ToAxial(position)))
                if (HexCoordinates.TryToOffset(neighbor, out var offset) && Contains(offset))
                    yield return offset;
        }

        /// <summary>Clipped range in row-major order; center must be on this board.</summary>
        public IEnumerable<BoardPosition> WithinRadius(BoardPosition center, int radius, bool includeCenter = true)
        {
            RequireInside(center);
            if (radius < 0) throw new ArgumentOutOfRangeException(nameof(radius));
            return EnumerateRadius(center, radius, includeCenter);
        }
        private IEnumerable<BoardPosition> EnumerateRadius(BoardPosition center, int radius, bool includeCenter)
        {
            foreach (var cell in Cells())
                if ((includeCenter || !cell.Equals(center)) && HexCoordinates.IsInRange(center, cell, radius))
                    yield return cell;
        }
        private void RequireInside(BoardPosition position)
        {
            if (!Contains(position)) throw new ArgumentOutOfRangeException(nameof(position));
        }
    }
}
