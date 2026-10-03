using System;

namespace PokeChess.Core.Board
{
    /// <summary>Signed pointy-top axial coordinates. Cube S is -Q-R.</summary>
    public readonly struct AxialPosition : IEquatable<AxialPosition>
    {
        public int Q { get; }
        public int R { get; }
        public AxialPosition(int q, int r) { Q = q; R = r; }
        public bool Equals(AxialPosition other) => Q == other.Q && R == other.R;
        public override bool Equals(object obj) => obj is AxialPosition other && Equals(other);
        public override int GetHashCode() => unchecked(Q * 397 ^ R);
        public static bool operator ==(AxialPosition a, AxialPosition b) => a.Equals(b);
        public static bool operator !=(AxialPosition a, AxialPosition b) => !a.Equals(b);
        public override string ToString() => $"({Q}, {R})";
    }
}
