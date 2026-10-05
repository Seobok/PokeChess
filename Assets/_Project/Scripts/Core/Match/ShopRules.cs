using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace PokeChess.Core.Match
{
    public sealed class ShopRules
    {
        public const int SlotCount = 5;
        private readonly ReadOnlyCollection<IReadOnlyList<int>> probabilities;
        public IReadOnlyList<IReadOnlyList<int>> Probabilities => probabilities;
        public int MaxLevel => probabilities.Count;
        public int RerollGoldCost { get; }
        public ShopRules(IEnumerable<IEnumerable<int>> probabilities = null, int rerollGoldCost = 2)
        {
            var input = probabilities ?? new[] {
                new[] {100,0,0,0,0}, new[] {100,0,0,0,0}, new[] {75,25,0,0,0},
                new[] {55,30,15,0,0}, new[] {45,33,20,2,0}, new[] {30,40,25,5,0},
                new[] {19,35,35,10,1}, new[] {18,25,36,18,3}, new[] {10,20,25,35,10},
                new[] {5,10,20,40,25} };
            var rows = new List<IReadOnlyList<int>>();
            foreach (var row in input)
            {
                if (row == null) throw new ArgumentException("Null probability row.", nameof(probabilities));
                var copy = row.ToArray();
                if (copy.Length != 5 || copy.Any(v => v < 0 || v > 100) || copy.Sum() != 100)
                    throw new ArgumentException("Each level requires five percentages totaling 100.", nameof(probabilities));
                rows.Add(Array.AsReadOnly(copy));
            }
            if (rows.Count == 0) throw new ArgumentException("At least one level is required.", nameof(probabilities));
            if (rerollGoldCost < 0) throw new ArgumentOutOfRangeException(nameof(rerollGoldCost));
            this.probabilities = rows.AsReadOnly();
            RerollGoldCost = rerollGoldCost;
        }
        public IReadOnlyList<int> ForLevel(int level)
        {
            if (level < 1 || level > MaxLevel) throw new ArgumentOutOfRangeException(nameof(level));
            return probabilities[level - 1];
        }
        public int SelectCost(int level, int roll)
        {
            if (roll < 0 || roll >= 100) throw new ArgumentOutOfRangeException(nameof(roll));
            var row = ForLevel(level);
            int cumulative = 0;
            for (int i=0; i<5; i++)
            {
                cumulative += row[i];
                if (roll < cumulative) return i + 1;
            }
            throw new InvalidOperationException("Invalid probability total.");
        }
    }
}
