using UnityEngine;
using UnityEngine.Serialization;

namespace PokeChess.Autobattler
{
    /// <summary>
    /// Authoring asset that defines combat, movement, mana, and skill data for a unit.
    /// </summary>
    [CreateAssetMenu(menuName = "PokeChess/Autobattler/Unit Stats", fileName = "UnitStats")]
    public class UnitStats : ScriptableObject
    {
        [Header("Combat")]
        [Min(0.01f)] public float attackPower = 10f;
        [Min(1f)] public float maxHp = 100f;
        [Min(0)] public int maxMana = 100;
        [FormerlySerializedAs("defense")] public float armor = 0f;
        public float magicResist = 0f;

        [Header("Action")]
        [Min(1)] public int attackRange = 1;
        [Min(0.1f)] public float attackSpeed = 1f;
        [Min(0f)] public float moveSpeed = 1f;

        [Header("Mana")]
        [Min(0)] public int manaGainOnAttack = 20;
        [Min(0)] public int manaGainOnHit = 10;

        [Header("Skill")]
        public UnitSkillDefinition skill;
    }

    public enum DamageType : byte
    {
        Physical = 0,
        Special = 1
    }

    public abstract class UnitSkillDefinition : ScriptableObject
    {
        [Header("Targeting")]
        [Min(0)] public int castRange = 1;
        public DamageType damageType = DamageType.Special;

        public abstract bool TryCast(UnitController caster, UnitController preferredTarget);

        protected UnitController ResolveTarget(UnitController caster, UnitController preferredTarget)
        {
            if (caster == null)
                return null;

            if (caster.IsEnemyInRange(preferredTarget, castRange))
                return preferredTarget;

            return caster.FindClosestEnemyInRange(castRange);
        }
    }

    [CreateAssetMenu(menuName = "PokeChess/Autobattler/Skills/Direct Damage Skill", fileName = "DirectDamageSkill")]
    public class DirectDamageSkillDefinition : UnitSkillDefinition
    {
        [Header("Effect")]
        [Min(0f)] public float damage = 50f;

        public override bool TryCast(UnitController caster, UnitController preferredTarget)
        {
            UnitController target = ResolveTarget(caster, preferredTarget);
            if (target == null)
                return false;

            return caster.TryApplyDamage(target, damage, damageType);
        }
    }
}
