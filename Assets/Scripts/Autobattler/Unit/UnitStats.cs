using UnityEngine;

namespace PokeChess.Autobattler
{
    /// <summary>
    /// 오토배틀러 유닛의 정적 스탯 데이터입니다.
    /// </summary>
    [CreateAssetMenu(menuName = "PokeChess/Autobattler/Unit Stats", fileName = "UnitStats")]
    public class UnitStats : ScriptableObject
    {
        [Header("전투 스탯")]
        [Min(1)] public int attackPower = 10;
        [Min(1)] public int maxHp = 100;
        [Min(0)] public int maxMana = 100;
        [Min(0)] public int defense = 0;

        [Header("행동 스탯")]
        [Min(1)] public int attackRange = 1;
        [Min(0.1f)] public float attackSpeed = 1f;
        [Min(1)] public int moveSpeedPerTick = 1;
    }
}
