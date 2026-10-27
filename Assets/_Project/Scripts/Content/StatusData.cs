using System;
using System.Linq;
using UnityEngine;
using PokeChess.Core.Battle;

namespace PokeChess.Content
{
    [Serializable]
    public sealed class StatModifierData
    {
        public CombatStat Stat;
        public StatModifierType Type;
        public float Value;
        public StatModifierDefinition ToCore()=>new StatModifierDefinition(Stat,Type,Value);
    }
    [CreateAssetMenu(menuName="PokeChess/Content/Status")]
    public sealed class StatusData : ScriptableObject
    {
        public string Id, StackGroup;
        public float Duration=4, SlowFraction;
        public StatusTargetTeam TargetTeam;
        public CrowdControlKind CrowdControl;
        public StatusBehavior Behavior;
        public float RecoveryFraction;
        public bool FixedSpellDamage;
        public float EnergyRefundFraction,EffectValue,RestReduction=.3f,ReflectionMultiplier;
        public StackPolicy StackPolicy=StackPolicy.Refresh;
        public SourceStackRule SourceRule;
        public int MaxStack=1;
        public StatModifierData[] Modifiers=Array.Empty<StatModifierData>();
        public StatusEffectDefinition ToCore()=>new StatusEffectDefinition(Id,Duration,TargetTeam,CrowdControl,
            StackPolicy,SourceRule,MaxStack,Modifiers.Select(m=>m.ToCore()),string.IsNullOrEmpty(StackGroup)?null:StackGroup,SlowFraction,Behavior,RecoveryFraction,EnergyRefundFraction,EffectValue,RestReduction,ReflectionMultiplier,FixedSpellDamage);
    }
}
