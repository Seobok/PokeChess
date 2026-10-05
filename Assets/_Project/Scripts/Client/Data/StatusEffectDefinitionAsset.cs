using System;
using System.Linq;
using PokeChess.Core.Battle;
using UnityEngine;
namespace PokeChess.Client.Data
{
    [Serializable]
    public sealed class StatModifierData
    {
        public CombatStat stat;
        public StatModifierType type;
        public float value;
        public StatModifierDefinition ToDefinition() => new StatModifierDefinition(stat,type,value);
    }
    [CreateAssetMenu(menuName="PokeChess/Data/Status Effect Definition")]
    public sealed class StatusEffectDefinitionAsset : ScriptableObject
    {
        [SerializeField] private string statusId="sample-stun";
        [SerializeField] private float duration=1;
        [SerializeField] private StatusTargetTeam targetTeam=StatusTargetTeam.Enemy;
        [SerializeField] private CrowdControlKind crowdControl=CrowdControlKind.Stun;
        [SerializeField, Range(0,1)] private float slowFraction;
        [SerializeField] private StackPolicy stackPolicy=StackPolicy.Refresh;
        [SerializeField] private SourceStackRule sourceRule=SourceStackRule.PerSource;
        [SerializeField, Min(1)] private int maxStack=1;
        [SerializeField] private string stackGroup;
        [SerializeField] private StatModifierData[] modifiers=Array.Empty<StatModifierData>();
        public StatusEffectDefinition ToDefinition()
        {
            if(modifiers==null||modifiers.Any(m=>m==null))throw new ArgumentException("Invalid stat modifier entries.");
            return new StatusEffectDefinition(statusId,duration,targetTeam,crowdControl,stackPolicy,sourceRule,maxStack,
                modifiers.Select(m=>m.ToDefinition()),string.IsNullOrWhiteSpace(stackGroup)?null:stackGroup,slowFraction);
        }
    }
}
