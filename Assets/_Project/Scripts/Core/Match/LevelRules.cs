using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace PokeChess.Core.Match
{
    public sealed class LevelRules
    {
        private readonly ReadOnlyCollection<int> requirements;
        // Index 0 is XP for 1 -> 2. The table covers every level from 1.
        public IReadOnlyList<int> Requirements => requirements;
        public int MaxLevel => requirements.Count + 1;
        public int PurchaseGoldCost { get; }
        public int PurchaseXP { get; }
        public int AutomaticXP { get; }
        public LevelRules(IEnumerable<int> requirements = null, int purchaseGoldCost = 4,
            int purchaseXP = 4, int automaticXP = 2)
        {
            var copy = (requirements ?? new[] {2,2,6,10,20,36,60,68,68}).ToArray();
            if (copy.Any(x => x <= 0)) throw new ArgumentException("XP requirements must be positive.", nameof(requirements));
            if (purchaseGoldCost <= 0) throw new ArgumentOutOfRangeException(nameof(purchaseGoldCost));
            if (purchaseXP <= 0) throw new ArgumentOutOfRangeException(nameof(purchaseXP));
            if (automaticXP < 0) throw new ArgumentOutOfRangeException(nameof(automaticXP));
            this.requirements = Array.AsReadOnly(copy);
            PurchaseGoldCost = purchaseGoldCost;
            PurchaseXP = purchaseXP;
            AutomaticXP = automaticXP;
        }
        public int XPToNextLevel(int level)
        {
            if (level < 1 || level > MaxLevel) throw new ArgumentOutOfRangeException(nameof(level));
            return level == MaxLevel ? 0 : requirements[level - 1];
        }
    }
}
