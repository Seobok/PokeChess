using System.Collections.Generic;
using System.Linq;

namespace PokeChess.Core.Battle
{
    public static class CombatStatusIcons
    {
        public static IEnumerable<string> ForStatus(StatusEffectDefinition status)
        {
            if (status.Id == "synergy-disruptor-expose")
            {
                yield return "vulnerability";
                yield break;
            }

            string icon = status.Behavior switch
            {
                StatusBehavior.Poison => "poison",
                StatusBehavior.Burn => "burn",
                StatusBehavior.Bleed => "bleed",
                StatusBehavior.Blind => "blind",
                StatusBehavior.Sleep => "sleep",
                StatusBehavior.Freeze => "freeze",
                StatusBehavior.Paralysis => "paralysis",
                StatusBehavior.Confusion => "confusion",
                StatusBehavior.GuaranteedCritical => "guaranteed_crit",
                StatusBehavior.AttackSplash => "attack_splash",
                StatusBehavior.SwordDance => "sword_dance",
                StatusBehavior.Untargetable => "untargetable",
                StatusBehavior.RestSleep => "rest_sleep",
                StatusBehavior.Focus => "focus",
                StatusBehavior.AttackRecovery => "skill_recovery",
                StatusBehavior.ActionRecovery => "skill_recovery",
                _ => status.CrowdControl switch
                {
                    CrowdControlKind.Stun => "stun",
                    CrowdControlKind.Root => "root",
                    CrowdControlKind.Taunt => "taunt",
                    CrowdControlKind.Slow => "move_speed_down",
                    _ => null
                }
            };
            if (icon != null)
            {
                yield return icon;
                yield break;
            }

            bool mapped = false;
            foreach (var modifier in status.Modifiers)
            {
                float delta = modifier.Value - (modifier.Type == StatModifierType.Multiplicative ? 1 : 0);
                if (delta == 0) continue;
                string key = modifier.Stat switch
                {
                    CombatStat.Attack when delta < 0 => "attack_down",
                    CombatStat.SpellPower => delta > 0 ? "spell_power_up" : "spell_power_down",
                    CombatStat.Armor => delta > 0 ? "armor_up" : "armor_down",
                    CombatStat.MagicResistance => delta > 0 ? "magic_resistance_up" : "magic_resistance_down",
                    CombatStat.AttackSpeed => delta > 0 ? "attack_speed_up" : "attack_speed_down",
                    CombatStat.MoveSpeed => delta > 0 ? "move_speed_up" : "move_speed_down",
                    CombatStat.EnergyGain when delta < 0 => "mana_gain_down",
                    CombatStat.AttackRange when delta > 0 => "attack_range_up",
                    CombatStat.Evasion when delta > 0 => "evasion_up",
                    CombatStat.CritChance when delta > 0 => "crit_chance_up",
                    CombatStat.DamageReduction when delta > 0 => "damage_reduction",
                    CombatStat.DamageReduction when delta < 0 => "vulnerability",
                    _ => null
                };
                if (key == null) continue;
                mapped = true;
                yield return key;
            }

            if (!mapped)
                yield return status.TargetTeam == StatusTargetTeam.Ally ? "buff" : "debuff";
        }

        public static bool UsesBuffFrame(string icon) => icon switch
        {
            "poison" or
            "burn" or
            "bleed" or
            "blind" or
            "sleep" or
            "freeze" or
            "paralysis" or
            "confusion" or
            "stun" or
            "root" or
            "taunt" or
            "move_speed_down" or
            "attack_speed_down" or
            "attack_down" or
            "spell_power_down" or
            "armor_down" or
            "magic_resistance_down" or
            "mana_gain_down" or
            "vulnerability" or
            "contamination" or
            "tracker_mark" or
            "skill_recovery" or "debuff" => false,
            _ => true
        };

        public static IEnumerable<string> ForUnit(BattleState battle, UnitCombatState unit)
            => battle.StatusEffects.Active
                .Where(status => status.TargetId == unit.UnitInstanceId &&
                    status.StartTick <= battle.CurrentTick && status.ExpireTick > battle.CurrentTick)
                .SelectMany(status => ForStatus(status.Definition))
                .Concat(ForOtherEffects(battle, unit));

        public static IEnumerable<string> ForOtherEffects(BattleState battle, UnitCombatState unit)
        {
            var items = battle.Items;
            var synergies = battle.Synergies;
            if (unit.CurrentShield > 0) yield return "shield";
            if (synergies.IsContaminated(unit)) yield return "contamination";
            if (synergies.HasVenomManaErosion(unit)) yield return "mana_gain_down";
            if (battle.Units.Any(source => source.IsAlive && source.IsOnBoard &&
                synergies.TrackerTarget(source) == unit.UnitInstanceId)) yield return "tracker_mark";
            if (unit.IsUntargetable) yield return "untargetable";
            if (synergies.HasStatusDamageReduction(unit) || items.HasStatusDamageReduction(unit))
                yield return "damage_reduction";
            if (synergies.DuelistAttackEnd(unit) > battle.CurrentTick || synergies.AssassinAttackReady(unit) ||
                items.HasChoiceBandBuff(unit)) yield return "basic_attack_damage_up";
            if (items.ExposureMultiplier(unit) > 1) yield return "vulnerability";
            if (items.HasStatusBlockCharge(unit)) yield return "debuff_block";
            if (items.HasGuaranteedBasicCritical(unit)) yield return "guaranteed_crit";
            if (items.IsRecoveringFromSash(unit)) yield return "focus_sash_recovery";
            if (items.AttackSpeedMultiplier(unit) > 1) yield return "attack_speed_up";
            if (items.DynamicCritChance(unit) > 0) yield return "crit_chance_up";
        }
    }
}
