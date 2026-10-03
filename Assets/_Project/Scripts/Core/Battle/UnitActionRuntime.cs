using System;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Battle
{
    public readonly struct CombatActionTiming
    {
        public int DurationTicks { get; }
        public int EffectOffsetTicks { get; }
        public CombatActionTiming(int durationTicks, int effectOffsetTicks)
        {
            if (durationTicks < 1 || effectOffsetTicks < 0 || effectOffsetTicks > durationTicks)
                throw new ArgumentOutOfRangeException();
            DurationTicks = durationTicks;
            EffectOffsetTicks = effectOffsetTicks;
        }
    }

    public sealed class UnitActionRuntime
    {
        public string UnitId { get; }
        public long StartTick { get; internal set; }
        public long EndTick { get; internal set; }
        public long EffectTick { get; internal set; }
        public bool EffectApplied { get; internal set; }
        public BoardPosition? MoveDestination { get; internal set; }
        internal UnitActionRuntime(string unitId) { UnitId = unitId; }
    }

    public enum CombatActionSignalKind { Started, TimingReached, Completed, Cancelled }
    // These are local behavior diagnostics, not network CombatEvents or damage confirmations.
    public readonly struct CombatActionSignal
    {
        public long Tick { get; }
        public string UnitId { get; }
        public CombatActionState Action { get; }
        public CombatActionSignalKind Kind { get; }
        public CombatActionSignal(long tick, string unitId, CombatActionState action, CombatActionSignalKind kind)
        { Tick = tick; UnitId = unitId; Action = action; Kind = kind; }
    }
}
