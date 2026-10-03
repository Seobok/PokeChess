using System;
using System.Linq;
using PokeChess.Core.Board;

namespace PokeChess.Core.Battle
{
    /// <summary>Temporary nearest/ordinal-ID targeting. Replace with WBS 1.7; no damage or skill effects by default.</summary>
    public class CombatBehaviorPolicy
    {
        public virtual string SelectTarget(BattleState battle, UnitCombatState unit) =>
            battle.Units.Where(t => t.TeamId != unit.TeamId && IsTargetable(t))
                .OrderBy(t => HexCoordinates.Distance(unit.Position, t.Position))
                .ThenBy(t => t.UnitInstanceId, StringComparer.Ordinal)
                .Select(t => t.UnitInstanceId).FirstOrDefault();
        public virtual bool IsTargetable(UnitCombatState unit) => unit.IsTargetable;
        public virtual bool CanMove(BattleState battle, UnitCombatState unit) => true;
        public virtual bool CanAttack(BattleState battle, UnitCombatState unit) => true;
        public virtual bool TryGetSkillTiming(BattleState battle, UnitCombatState unit, UnitCombatState target,
            out CombatActionTiming timing) { timing = default; return false; }
        public virtual bool CanContinueSkill(BattleState battle, UnitCombatState unit, UnitCombatState target) =>
            target != null && IsTargetable(target);
        public virtual CombatActionTiming GetAttackTiming(BattleState battle, UnitCombatState unit)
        {
            int hit = BattleSimulation.ToTicks(unit.Stats.AttackHitTime, battle.TickRate, false);
            int duration = Math.Max(BattleSimulation.ToTicks(1.0 / unit.Stats.AttackSpeed, battle.TickRate), hit);
            return new CombatActionTiming(duration, hit);
        }
        public virtual void OnSkillStarted(BattleState battle, UnitCombatState unit, UnitCombatState target) { }
        public virtual void OnAttackTiming(BattleState battle, UnitCombatState unit, UnitCombatState target) { }
        public virtual void OnSkillTiming(BattleState battle, UnitCombatState unit, UnitCombatState target) { }
    }
}
