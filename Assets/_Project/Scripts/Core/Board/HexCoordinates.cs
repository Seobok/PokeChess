using System;
using System.Collections.Generic;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Board
{
    /// <summary>Odd-R: odd offset rows are shifted right by half a hex.</summary>
    public static class HexCoordinates
    {
        // Fixed axial direction order. Screen orientation is handled by Client.
        private static readonly AxialPosition[] directions = {
            new AxialPosition(1,0), new AxialPosition(1,-1), new AxialPosition(0,-1),
            new AxialPosition(-1,0), new AxialPosition(-1,1), new AxialPosition(0,1)
        };

        public static AxialPosition ToAxial(BoardPosition position) =>
            new AxialPosition(checked(position.Column - (position.Row - (position.Row & 1)) / 2), position.Row);

        public static bool TryToOffset(AxialPosition position, out BoardPosition offset)
        {
            long column = (long)position.Q + ((long)position.R - (position.R & 1)) / 2;
            if (position.R < 0 || column < 0 || column > int.MaxValue)
            { offset = default; return false; }
            offset = new BoardPosition((int)column, position.R);
            return true;
        }

        public static BoardPosition ToOffset(AxialPosition position)
        {
            if (!TryToOffset(position, out var offset))
                throw new ArgumentOutOfRangeException(nameof(position), "Axial coordinate cannot be represented by nonnegative BoardPosition.");
            return offset;
        }

        public static int Distance(AxialPosition a, AxialPosition b)
        {
            long dq = (long)a.Q - b.Q;
            long dr = (long)a.R - b.R;
            return checked((int)Math.Max(Math.Abs(dq + dr), Math.Max(Math.Abs(dq), Math.Abs(dr))));
        }
        public static int Distance(BoardPosition a, BoardPosition b) => Distance(ToAxial(a), ToAxial(b));
        public static bool IsInRange(AxialPosition a, AxialPosition b, int radius)
        {
            if (radius < 0) throw new ArgumentOutOfRangeException(nameof(radius));
            return Distance(a,b) <= radius;
        }
        public static bool IsInRange(BoardPosition a, BoardPosition b, int radius) => IsInRange(ToAxial(a), ToAxial(b), radius);

        public static AxialPosition Neighbor(AxialPosition position, int direction)
        {
            if (direction < 0 || direction >= directions.Length) throw new ArgumentOutOfRangeException(nameof(direction));
            var delta = directions[direction];
            return new AxialPosition(checked(position.Q + delta.Q), checked(position.R + delta.R));
        }
        public static IEnumerable<AxialPosition> Neighbors(AxialPosition position)
        {
            for (int direction = 0; direction < directions.Length; direction++)
                yield return Neighbor(position, direction);
        }

        /// <summary>Unbounded radius, inclusive center by default. Deterministic Q then R order.</summary>
        public static IEnumerable<AxialPosition> WithinRadius(AxialPosition center, int radius, bool includeCenter = true)
        {
            if (radius < 0) throw new ArgumentOutOfRangeException(nameof(radius));
            return EnumerateRadius(center, radius, includeCenter);
        }
        private static IEnumerable<AxialPosition> EnumerateRadius(AxialPosition center, int radius, bool includeCenter)
        {
            // Long loop bounds avoid overflow at integer coordinate limits.
            for (long dq = -(long)radius; dq <= radius; dq++)
            {
                long minimum = Math.Max(-(long)radius, -dq - radius);
                long maximum = Math.Min((long)radius, -dq + radius);
                for (long dr = minimum; dr <= maximum; dr++)
                {
                    if (!includeCenter && dq == 0 && dr == 0) continue;
                    yield return new AxialPosition(checked((int)(center.Q + dq)), checked((int)(center.R + dr)));
                }
            }
        }

        /// <summary>180-degree opponent transform on the fixed 7x8 combat board.</summary>
        public static BoardPosition MirrorCombat(BoardPosition position)
        {
            if (!HexBoardBounds.Combat.Contains(position)) throw new ArgumentOutOfRangeException(nameof(position));
            return new BoardPosition(6 - position.Column, 7 - position.Row);
        }
    }
}
