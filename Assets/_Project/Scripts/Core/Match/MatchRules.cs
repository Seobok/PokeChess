using System;

namespace PokeChess.Core.Match
{
    public sealed class MatchRules
    {
        public int StartingHP { get; }
        public int StartingGold { get; }
        public int StartingXP { get; }
        public int StartingLevel { get; }
        public int MaxLevel { get; }
        public int BoardWidth { get; }
        public int BoardHeight { get; }
        public int BenchCapacity { get; }

        public MatchRules(int startingHP = 60, int startingGold = 5, int startingXP = 0,
            int startingLevel = 3, int maxLevel = 8, int boardWidth = 7,
            int boardHeight = 4, int benchCapacity = 9)
        {
            if (startingHP <= 0) throw new ArgumentOutOfRangeException(nameof(startingHP));
            if (startingGold < 0) throw new ArgumentOutOfRangeException(nameof(startingGold));
            if (startingXP < 0) throw new ArgumentOutOfRangeException(nameof(startingXP));
            if (maxLevel < 1) throw new ArgumentOutOfRangeException(nameof(maxLevel));
            if (startingLevel < 1 || startingLevel > maxLevel) throw new ArgumentOutOfRangeException(nameof(startingLevel));
            if (boardWidth < 1) throw new ArgumentOutOfRangeException(nameof(boardWidth));
            if (boardHeight < 1) throw new ArgumentOutOfRangeException(nameof(boardHeight));
            if (benchCapacity < 1) throw new ArgumentOutOfRangeException(nameof(benchCapacity));
            StartingHP = startingHP;
            StartingGold = startingGold;
            StartingXP = startingXP;
            StartingLevel = startingLevel;
            MaxLevel = maxLevel;
            BoardWidth = boardWidth;
            BoardHeight = boardHeight;
            BenchCapacity = benchCapacity;
        }
    }
}
