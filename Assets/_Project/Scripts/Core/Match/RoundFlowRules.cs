using System;

namespace PokeChess.Core.Match
{
    public sealed class RoundFlowRules
    {
        public double PreparationSeconds { get; }
        public double ResultSeconds { get; }
        public int MaxCombatTicksPerUpdate { get; }

        public RoundFlowRules(double preparationSeconds = 30, double resultSeconds = 0,
            int maxCombatTicksPerUpdate = 60)
        {
            ValidateDuration(preparationSeconds, nameof(preparationSeconds));
            if (double.IsNaN(resultSeconds) || double.IsInfinity(resultSeconds) || resultSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(resultSeconds));
            if (maxCombatTicksPerUpdate < 1)
                throw new ArgumentOutOfRangeException(nameof(maxCombatTicksPerUpdate));
            PreparationSeconds = preparationSeconds;
            ResultSeconds = resultSeconds;
            MaxCombatTicksPerUpdate = maxCombatTicksPerUpdate;
        }

        private static void ValidateDuration(double seconds, string name)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0)
                throw new ArgumentOutOfRangeException(name);
        }
    }
}
