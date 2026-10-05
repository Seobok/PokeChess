using System;
namespace PokeChess.Core.Battle
{
    public enum BattleLifecycleEventKind { UnitDied, OvertimeStarted, BattleEnded }
    public enum DeathReason { Damage, StateChange }
    public readonly struct BattleLifecycleEvent
    {
        public BattleLifecycleEventKind Kind { get; }
        public long Tick { get; }
        public string UnitId { get; }
        public DeathReason DeathReason { get; }
        public BattleResult Result { get; }
        public BattleEndReason EndReason { get; }
        public BattleLifecycleEvent(BattleLifecycleEventKind kind,long tick,string unitId=null,
            DeathReason deathReason=DeathReason.StateChange,BattleResult result=BattleResult.InProgress,
            BattleEndReason endReason=BattleEndReason.None)
        {Kind=kind;Tick=tick;UnitId=unitId;DeathReason=deathReason;Result=result;EndReason=endReason;}
    }
}
