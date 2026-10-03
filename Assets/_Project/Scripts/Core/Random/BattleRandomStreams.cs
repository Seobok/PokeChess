using System;

namespace PokeChess.Core.Random
{
    /// <summary>Version 1: SplitMix64, fixed purpose salts. Host-internal; never put seeds/state in client DTOs.</summary>
    public sealed class DeterministicRandom
    {
        public ulong State { get; private set; }
        public long DrawCount { get; private set; }
        public DeterministicRandom(ulong seed) { State = seed; }
        public ulong NextUInt64()
        {
            State = unchecked(State + 0x9E3779B97F4A7C15UL);
            DrawCount++;
            return Mix(State);
        }
        internal static ulong Mix(ulong value)
        {
            value = unchecked((value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL);
            value = unchecked((value ^ (value >> 27)) * 0x94D049BB133111EBUL);
            return value ^ (value >> 31);
        }
        public int NextInt(int exclusiveMaximum)
        {
            if (exclusiveMaximum < 1) throw new ArgumentOutOfRangeException(nameof(exclusiveMaximum));
            if (exclusiveMaximum == 1) return 0;
            ulong bound = (ulong)exclusiveMaximum;
            ulong threshold = unchecked(0UL - bound) % bound;
            ulong value;
            do { value = NextUInt64(); } while (value < threshold);
            return (int)(value % bound);
        }
    }

    public sealed class BattleRandomStreams
    {
        public DeterministicRandom Target { get; }
        public DeterministicRandom Critical { get; }
        public DeterministicRandom Skill { get; }
        public DeterministicRandom Item { get; }
        public BattleRandomStreams(ulong battleSeed)
        {
            Target = new DeterministicRandom(DeterministicRandom.Mix(battleSeed ^ 0x5441524745540001UL));
            Critical = new DeterministicRandom(DeterministicRandom.Mix(battleSeed ^ 0x4352495449430001UL));
            Skill = new DeterministicRandom(DeterministicRandom.Mix(battleSeed ^ 0x534B494C4C000001UL));
            Item = new DeterministicRandom(DeterministicRandom.Mix(battleSeed ^ 0x4954454D00000001UL));
        }
    }
}
