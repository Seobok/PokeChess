using UnityEngine;
using UnityEngine.Serialization;

namespace PokeChess.Autobattler
{
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

    public enum SkillTargetTeam : byte
    {
        Enemy = 0,
        Ally = 1,
        AllyOrSelf = 2,
        Self = 3
    }

    public enum BuffStatType : byte
    {
        AttackPower = 0,
        Armor = 1,
        MagicResist = 2,
        AttackSpeed = 3,
        MoveSpeed = 4,
        AttackRange = 5
    }

    [System.Serializable]
    public struct StatBuffEffect
    {
        public BuffStatType statType;
        public float additiveAmount;
        public float multiplier;
    }
}
