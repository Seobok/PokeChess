using System;
using System.Collections.Generic;
using System.Linq;

namespace PokeChess.Core.Battle
{
    public enum EnergyChangeReason { BasicAttackHit, DamageReceived, SkillCost, LockStarted, Item, SkillRefund }
    public enum EnergyBlockReason { None, Unavailable, Casting, Locked, Insufficient }
    public readonly struct EnergyEvent
    {
        public string UnitId { get; }
        public long Tick { get; }
        public EnergyChangeReason Reason { get; }
        public EnergyBlockReason BlockReason { get; }
        public float Before { get; }
        public float After { get; }
        public float Delta => After-Before;
        public float Requested { get; }
        public long LockUntilTick { get; }
        public EnergyEvent(UnitCombatState unit,long tick,EnergyChangeReason reason,EnergyBlockReason block,
            float before,float requested)
        { UnitId=unit.UnitInstanceId; Tick=tick; Reason=reason; BlockReason=block; Before=before;
          After=unit.CurrentEnergy; Requested=requested; LockUntilTick=unit.EnergyLockUntilTick; }
    }
    public sealed class EnergySystem
    {
        private readonly BattleState battle;
        private readonly List<EnergyEvent> events=new List<EnergyEvent>();
        public IReadOnlyList<EnergyEvent> EventsThisTick => Array.AsReadOnly(events.ToArray());
        internal EnergySystem(BattleState battle) { this.battle=battle; }
        internal void BeginTick() => events.Clear();
        private void Validate(UnitCombatState unit)
        { if(unit==null||!battle.Units.Contains(unit))throw new ArgumentException("Unit must belong to battle."); }
        public bool IsSkillReady(UnitCombatState unit)
        { Validate(unit); return unit.IsAlive&&unit.IsOnBoard&&unit.ActionState!=CombatActionState.Casting&&
            unit.Stats.MaxEnergy>0&&unit.CurrentEnergy>=unit.Stats.MaxEnergy; }
        public bool Gain(UnitCombatState unit,float amount,EnergyChangeReason reason)
        {
            Validate(unit);DamageCalculator.Validate(amount,true);
            if(reason!=EnergyChangeReason.BasicAttackHit&&reason!=EnergyChangeReason.DamageReceived&&reason!=EnergyChangeReason.Item)
                throw new ArgumentOutOfRangeException(nameof(reason));
            if(amount==0)return false;
            amount=(float)Math.Min(float.MaxValue,(double)amount*unit.EffectiveStats.EnergyGain);
            if(reason==EnergyChangeReason.DamageReceived)amount=Math.Min(50,amount);
            var block=!unit.IsAlive||!unit.IsOnBoard?EnergyBlockReason.Unavailable:
                unit.ActionState==CombatActionState.Casting?EnergyBlockReason.Casting:
                battle.CurrentTick<unit.EnergyLockUntilTick?EnergyBlockReason.Locked:EnergyBlockReason.None;
            float before=unit.CurrentEnergy;
            if(block==EnergyBlockReason.None)
                unit.SetEnergy((float)Math.Min(float.MaxValue,(double)before+amount));
            events.Add(new EnergyEvent(unit,battle.CurrentTick,reason,block,before,amount));
            return block==EnergyBlockReason.None;
        }
        public bool Refund(UnitCombatState unit,float amount)
        {
            Validate(unit);DamageCalculator.Validate(amount,true);float before=unit.CurrentEnergy;
            if(!unit.IsAlive||!unit.IsOnBoard)return false;
            unit.SetEnergy((float)Math.Min(float.MaxValue,(double)before+amount));
            events.Add(new EnergyEvent(unit,battle.CurrentTick,EnergyChangeReason.SkillRefund,EnergyBlockReason.None,before,amount));return true;
        }
        internal bool TryConsumeSkill(UnitCombatState unit)
        {
            Validate(unit);float before=unit.CurrentEnergy;
            if(!IsSkillReady(unit))return false;
            unit.SetEnergy(before-unit.Stats.MaxEnergy);
            events.Add(new EnergyEvent(unit,battle.CurrentTick,EnergyChangeReason.SkillCost,EnergyBlockReason.None,before,-unit.Stats.MaxEnergy));
            return true;
        }
        internal void EndCast(UnitCombatState unit,int? lockTicks=null)
        {
            int duration=lockTicks??BattleSimulation.ToTicks(unit.Stats.EnergyLockSeconds,battle.TickRate,false);
            unit.EnergyLockUntilTick=Math.Max(unit.EnergyLockUntilTick,checked(battle.CurrentTick+duration));
            events.Add(new EnergyEvent(unit,battle.CurrentTick,EnergyChangeReason.LockStarted,
                EnergyBlockReason.None,unit.CurrentEnergy,0));
        }
        internal void OnDamage(DamageResult result)
        {
            if(result.Missed||result.Request.Origin==DamageOrigin.Reflection||result.Request.Origin==DamageOrigin.FreezeBreak||result.Request.Origin==DamageOrigin.Execution)return;
            if(result.PostMitigationDamage<=0)return;
            var source=battle.Units.Single(u=>u.UnitInstanceId==result.Request.SourceId);
            var target=battle.Units.Single(u=>u.UnitInstanceId==result.Request.TargetId);
            if(result.Request.IsBasicAttack)Gain(source,10,EnergyChangeReason.BasicAttackHit);
            float received=(float)((double)result.PreMitigationDamage*.01+result.PostMitigationDamage*.07);
            Gain(target,received,EnergyChangeReason.DamageReceived);
        }
    }
}
