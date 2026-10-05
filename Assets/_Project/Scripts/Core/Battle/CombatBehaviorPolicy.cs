using System;
using System.Linq;
using PokeChess.Core.Board;

namespace PokeChess.Core.Battle
{
    /// <summary>Nearest/center/TargetRng targeting for all units; no damage or skill effects by default.</summary>
    public class CombatBehaviorPolicy
    {
        public virtual string SelectTarget(BattleState battle, UnitCombatState unit) =>
            CombatTargetSelector.Select(battle, unit, IsTargetable, GetTauntSource(unit));
        public virtual string GetTauntSource(UnitCombatState unit) => unit.TauntSourceId;
        public virtual bool IsTargetable(UnitCombatState unit) => unit.IsTargetable;
        public virtual bool CanMove(BattleState battle, UnitCombatState unit) => true;
        public virtual bool CanAttack(BattleState battle, UnitCombatState unit) => true;
        public virtual bool CanUseSkill(BattleState battle,UnitCombatState unit) => true;
        public virtual bool TryGetSkillDefinition(BattleState battle,UnitCombatState unit,out SkillDefinition skill)
        { skill=null; return false; }
        public virtual void ExecuteSkillEffect(BattleState battle,UnitCombatState unit,UnitCombatState target,SkillDefinition skill) { }
        public virtual bool TryGetSkillTiming(BattleState battle, UnitCombatState unit, UnitCombatState target,
            out CombatActionTiming timing) { timing = default; return false; }
        public virtual bool CanContinueSkill(BattleState battle, UnitCombatState unit, UnitCombatState target) =>
            target != null && IsTargetable(target);
        public virtual CombatActionTiming GetAttackTiming(BattleState battle, UnitCombatState unit)
        {
            int hit = BattleSimulation.ToTicks(unit.Stats.AttackHitTime, battle.TickRate, false);
            int duration = Math.Max(BattleSimulation.ToTicks(1.0 / Math.Max(0.1, Math.Min(5.0, unit.Stats.AttackSpeed)), battle.TickRate), hit);
            return new CombatActionTiming(duration, hit);
        }
        public virtual void OnProjectilesTiming(BattleState battle) { }
        public virtual void OnSkillStarted(BattleState battle, UnitCombatState unit, UnitCombatState target) { }
        public virtual void OnAttackTiming(BattleState battle, UnitCombatState unit, UnitCombatState target) { }
        public virtual void OnSkillTiming(BattleState battle, UnitCombatState unit, UnitCombatState target) { }
    }
}
