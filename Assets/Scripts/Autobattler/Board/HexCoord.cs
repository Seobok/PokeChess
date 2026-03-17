using System;
using Fusion;

namespace PokeChess.Autobattler
{
    /// <summary>
    /// Network-serializable odd-r offset hex coordinate.
    /// </summary>
    [Serializable]
    public struct HexCoord : INetworkStruct, IEquatable<HexCoord>
    {
        public int Q;
        public int R;
        public const int NeighborCount = 6;

        public HexCoord(int q, int r) { Q = q; R = r; }

        // odd-r offset (?됱씠 ??섎㈃ ?ㅻⅨ履쎌쑝濡?諛?移?諛由? 湲곗? 6諛⑺뼢
        private static readonly HexCoord[] NeighborEvenR =
        {
            new(+1, 0),  // E
            new(0, -1),  // NE
            new(-1, -1), // NW
            new(-1, 0),  // W
            new(-1, +1), // SW
            new(0, +1)   // SE
        };

        private static readonly HexCoord[] NeighborOddR =
        {
            new(+1, 0),  // E
            new(+1, -1), // NE
            new(0, -1),  // NW
            new(-1, 0),  // W
            new(0, +1),  // SW
            new(+1, +1)  // SE
        };

        public HexCoord Neighbor(int directionIndex)
        {
            var dirs = (R & 1) == 0 ? NeighborEvenR : NeighborOddR;
            var d = dirs[directionIndex];
            return new HexCoord(Q + d.Q, R + d.R);
        }

        // odd-r offset -> cube 蹂????嫄곕━
        public static int Distance(HexCoord a, HexCoord b)
        {
            var ac = ToCube(a);
            var bc = ToCube(b);

            int dx = Math.Abs(ac.x - bc.x);
            int dy = Math.Abs(ac.y - bc.y);
            int dz = Math.Abs(ac.z - bc.z);

            return (dx + dy + dz) / 2;
        }

        private static (int x, int y, int z) ToCube(HexCoord h)
        {
            // odd-r: x = col - (row - (row&1))/2, z=row
            int x = h.Q - ((h.R - (h.R & 1)) / 2);
            int z = h.R;
            int y = -x - z;
            return (x, y, z);
        }

        public bool Equals(HexCoord other) => Q == other.Q && R == other.R;
        public override bool Equals(object obj) => obj is HexCoord other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Q, R);
        public static bool operator ==(HexCoord left, HexCoord right) => left.Equals(right);
        public static bool operator !=(HexCoord left, HexCoord right) => !left.Equals(right);
        public override string ToString() => $"({Q}, {R})";
    }
}

