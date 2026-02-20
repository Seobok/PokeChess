using System;
using Fusion;

namespace PokeChess.Autobattler
{
    /// <summary>
    /// 육각형 보드의 Axial 좌표(q, r)를 표현하는 네트워크 구조체입니다.
    /// </summary>
    [Serializable]
    public struct HexCoord : INetworkStruct, IEquatable<HexCoord>
    {
        // Axial 좌표의 q 축
        public int Q;

        // Axial 좌표의 r 축
        public int R;

        public HexCoord(int q, int r)
        {
            Q = q;
            R = r;
        }

        /// <summary>
        /// Axial 좌표 기준 6방향 이웃 벡터(시계 방향)입니다.
        /// </summary>
        public static readonly HexCoord[] NeighborDirections =
        {
            new HexCoord(+1, 0),
            new HexCoord(+1, -1),
            new HexCoord(0, -1),
            new HexCoord(-1, 0),
            new HexCoord(-1, +1),
            new HexCoord(0, +1)
        };

        /// <summary>
        /// 두 Hex 좌표 사이의 거리(헥스 맨해튼 거리)를 계산합니다.
        /// </summary>
        public static int Distance(HexCoord a, HexCoord b)
        {
            int dx = a.Q - b.Q;
            int dz = a.R - b.R;
            int dy = -dx - dz;
            return (Math.Abs(dx) + Math.Abs(dy) + Math.Abs(dz)) / 2;
        }

        /// <summary>
        /// 주어진 방향 인덱스(0~5)의 인접 칸 좌표를 반환합니다.
        /// </summary>
        public HexCoord Neighbor(int directionIndex)
        {
            HexCoord dir = NeighborDirections[directionIndex];
            return new HexCoord(Q + dir.Q, R + dir.R);
        }

        public bool Equals(HexCoord other)
        {
            return Q == other.Q && R == other.R;
        }

        public override bool Equals(object obj)
        {
            return obj is HexCoord other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Q, R);
        }

        public static bool operator ==(HexCoord left, HexCoord right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(HexCoord left, HexCoord right)
        {
            return !left.Equals(right);
        }

        public override string ToString()
        {
            return $"({Q}, {R})";
        }
    }
}
