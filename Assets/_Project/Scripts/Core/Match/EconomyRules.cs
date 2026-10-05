using System;

namespace PokeChess.Core.Match
{
    public sealed class EconomyRules
    {
        public int BaseIncome { get; }
        public int GoldPerInterest { get; }
        public int MaxInterest { get; }
        public int FirstStreakThreshold { get; }
        public int SecondStreakThreshold { get; }
        public int ThirdStreakThreshold { get; }
        public int FirstStreakBonus { get; }
        public int SecondStreakBonus { get; }
        public int ThirdStreakBonus { get; }

        public EconomyRules(int baseIncome = 5, int goldPerInterest = 10, int maxInterest = 5,
            int firstStreakThreshold = 2, int secondStreakThreshold = 4, int thirdStreakThreshold = 6,
            int firstStreakBonus = 1, int secondStreakBonus = 2, int thirdStreakBonus = 3)
        {
            if (baseIncome < 0) throw new ArgumentOutOfRangeException(nameof(baseIncome));
            if (goldPerInterest <= 0) throw new ArgumentOutOfRangeException(nameof(goldPerInterest));
            if (maxInterest < 0) throw new ArgumentOutOfRangeException(nameof(maxInterest));
            if (firstStreakThreshold <= 0) throw new ArgumentOutOfRangeException(nameof(firstStreakThreshold));
            if (secondStreakThreshold <= firstStreakThreshold) throw new ArgumentOutOfRangeException(nameof(secondStreakThreshold));
            if (thirdStreakThreshold <= secondStreakThreshold) throw new ArgumentOutOfRangeException(nameof(thirdStreakThreshold));
            if (firstStreakBonus < 0) throw new ArgumentOutOfRangeException(nameof(firstStreakBonus));
            if (secondStreakBonus < firstStreakBonus) throw new ArgumentOutOfRangeException(nameof(secondStreakBonus));
            if (thirdStreakBonus < secondStreakBonus) throw new ArgumentOutOfRangeException(nameof(thirdStreakBonus));
            BaseIncome = baseIncome;
            GoldPerInterest = goldPerInterest;
            MaxInterest = maxInterest;
            FirstStreakThreshold = firstStreakThreshold;
            SecondStreakThreshold = secondStreakThreshold;
            ThirdStreakThreshold = thirdStreakThreshold;
            FirstStreakBonus = firstStreakBonus;
            SecondStreakBonus = secondStreakBonus;
            ThirdStreakBonus = thirdStreakBonus;
        }
        internal int StreakBonus(int count) => count >= ThirdStreakThreshold ? ThirdStreakBonus
            : count >= SecondStreakThreshold ? SecondStreakBonus
            : count >= FirstStreakThreshold ? FirstStreakBonus : 0;
    }
}
