using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Match
{
    // Cell membership is a snapshot. Unit is the existing owned instance, not a copy.
    public sealed class PlayerBoardCell
    {
        public BoardPosition Position { get; }
        public UnitInstance Unit { get; }
        public bool IsEmpty => Unit == null;
        internal PlayerBoardCell(BoardPosition position, UnitInstance unit)
        { Position = position; Unit = unit; }
    }
}
