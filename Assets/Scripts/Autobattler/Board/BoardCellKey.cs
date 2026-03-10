using PokeChess.Autobattler;

/// <summary>
/// Compact key used to index a cell within a specific board.
/// </summary>
public readonly struct BoardCellKey : System.IEquatable<BoardCellKey>
{
    public readonly byte Board;
    public readonly byte Q;
    public readonly byte R;

    public BoardCellKey(byte board, HexCoord coord)
    {
        Board = board;
        Q = (byte)coord.Q;
        R = (byte)coord.R;
    }

    public bool Equals(BoardCellKey other) => Board == other.Board && Q == other.Q && R == other.R;
    public override bool Equals(object obj) => obj is BoardCellKey other && Equals(other);
    public override int GetHashCode() => (Board << 16) ^ (Q << 8) ^ R;
}
