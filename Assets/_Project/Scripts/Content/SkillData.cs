using System;
using System.Linq;
using UnityEngine;
using PokeChess.Core.Battle;
using PokeChess.Core.Board;
using System.Collections.Generic;

namespace PokeChess.Content
{
    [Serializable]
    public sealed class SkillEffectData
    {
        public SkillEffectType Type;
        public EffectTargetSelector TargetSelector;
        public float BaseValue;
        public bool SpellPowerScalable;
        public SkillPowerScaling PowerScaling;
        public DamageType DamageType=DamageType.Magic;
        public int Radius=1;
        public float ProjectileSpeed=6;
        public bool HasImpactRadius;
        public int ImpactRadius;
        public StatusData Status;
        public StatusTargetTeam StatusTargetTeam;
        public SkillValueSource ValueSource;
        public float[] RankValues=new float[3];
        public StatusData[] RankStatuses=Array.Empty<StatusData>();
        public SkillSpatialData Spatial;
        public SkillMovementData Movement;
        public bool Returns;
        public bool HasNumeric;
        public SkillNumericData Numeric=new SkillNumericData();
        public SkillEffectData[] PathImpactEffects=Array.Empty<SkillEffectData>();
        public StatusData[] ChoiceStatuses=Array.Empty<StatusData>();
        public float ChainDelay=.15f;
        public bool ChainSequential;
        public int ProjectileGroupSize;
        public SkillEffectDefinition ToCore()=>new SkillEffectDefinition(Type,TargetSelector,BaseValue,SpellPowerScalable,
            DamageType,Radius,ProjectileSpeed,Status==null?null:Status.Id,StatusTargetTeam,RankValues,ValueSource,
            Type==SkillEffectType.ApplyStatus?RankStatuses.Select(s=>s.Id):null,HasImpactRadius?(int?)ImpactRadius:null,
            TargetSelector==EffectTargetSelector.Spatial?Spatial?.ToCore():null,
            Type==SkillEffectType.Move?Movement?.ToCore():null,Returns,
            (PathImpactEffects??Array.Empty<SkillEffectData>()).Select(e=>e.ToCore()),HasNumeric?Numeric.ToCore():null,ChoiceStatuses.Select(s=>s.Id),ChainDelay,ChainSequential,ProjectileGroupSize,PowerScaling);
    }
    [Serializable]
    public sealed class SkillNumericData
    {
        public SkillValueCondition Condition;
        public float Threshold=.25f,Lifesteal,ArmorPenetration,CritBonus,Chance=1,ExecuteThreshold,BossBonus;
        public float[] ConditionalValues=new float[3];
        public bool HasDamageCap,CanCrit,HasShieldDuration;
        public float DamageCap,ShieldDuration=4,MagicPenetration;
        public bool Bounty;
        public SkillNumericDefinition ToCore()=>new SkillNumericDefinition(Condition,Threshold,ConditionalValues,
            HasDamageCap?(float?)DamageCap:null,Lifesteal,ArmorPenetration,CanCrit,CritBonus,Chance,
            HasShieldDuration?(float?)ShieldDuration:null,ExecuteThreshold,BossBonus,Bounty,MagicPenetration);
    }
    [Serializable]
    public sealed class SkillActivationData
    {
        public int MinimumAdjacentEnemies=2;
        public bool RequireSafeDestination=true;
        public SkillActivationDefinition ToCore()=>new SkillActivationDefinition(MinimumAdjacentEnemies,RequireSafeDestination);
    }
    [Serializable]
    public sealed class SkillSpatialData
    {
        public SpatialTargetMode Mode;
        public SkillAreaShape Shape;
        public SkillAreaCenter Center;
        public int Extent=1,MaxTargets,SearchRange=56,ChainRange=1;
        public bool IncludeSelf;
        public bool IncludePrimary=true;
        public SkillSpatialDefinition ToCore()=>new SkillSpatialDefinition(Mode,Shape,Center,Extent,MaxTargets,SearchRange,ChainRange,IncludeSelf,IncludePrimary);
    }
    [Serializable]
    public sealed class SkillMovementData
    {
        public SkillMovementKind Kind;
        public int Distance=1,ClusterRadius=1;
        public float Speed=6;
        public bool HasTravelTime;public float TravelTime;
        public SkillEffectData[] ImpactEffects=Array.Empty<SkillEffectData>();
        public SkillMovementDefinition ToCore()=>new SkillMovementDefinition(Kind,Distance,Speed,ClusterRadius,ImpactEffects.Select(e=>e.ToCore()),HasTravelTime?(float?)TravelTime:null);
    }
    [Serializable]
    public sealed class SkillStepData
    {
        [Tooltip("Seconds relative to CastTime; negative values allow preparation effects.")]
        public float Delay;
        public float Interval,FirstDelay;
        public int Count=1;
        public SkillStepLifetime Lifetime;
        public SkillStepTargeting Targeting;
        public bool SurvivesCaster=true;
        public SkillEffectData[] Effects=Array.Empty<SkillEffectData>();
        public SkillStepDefinition ToCore()=>new SkillStepDefinition(Effects.Select(e=>e.ToCore()),Delay,Interval,Count,
            Lifetime,Targeting,SurvivesCaster,FirstDelay);
    }
    [CreateAssetMenu(menuName="PokeChess/Content/Skill")]
    public sealed class SkillData : ScriptableObject
    {
        public string Id, DisplayName;
        public string[] VariantIds=Array.Empty<string>();
        public SkillVariantMode VariantMode;
        [TextArea] public string Description,Notes;
        public float CastTime=.4f, PostCastTime=.4f;
        public SkillTargetRule TargetRule;
        public TargetLostPolicy TargetLostPolicy;
        public int Range=3;
        public bool Interruptible=true, HasEnergyLock=true;
        public float EnergyLock=1;
        public bool HasActivation;
        public SkillActivationData Activation=new SkillActivationData();
        public SkillEffectData[] Effects=Array.Empty<SkillEffectData>();
        public SkillStepData[] Steps=Array.Empty<SkillStepData>();
        public SkillEffectData[] AllEffects => Effects.Concat((Steps??Array.Empty<SkillStepData>()).SelectMany(s=>
            s==null?throw new InvalidOperationException("Null skill step."):s.Effects)).SelectMany(e=>Flatten(e,0)).ToArray();
        private static IEnumerable<SkillEffectData> Flatten(SkillEffectData effect,int depth)
        {
            if(depth>8)throw new InvalidOperationException("Nested impact effects exceed authoring depth.");
            yield return effect;if(effect==null)yield break;
            if(effect.Type==SkillEffectType.Move&&effect.Movement!=null)
                foreach(var child in effect.Movement.ImpactEffects)foreach(var e in Flatten(child,depth+1))yield return e;
            foreach(var child in effect.PathImpactEffects??Array.Empty<SkillEffectData>())foreach(var e in Flatten(child,depth+1))yield return e;
        }
        public SkillDefinition ToCore()=>new SkillDefinition(Id,CastTime,PostCastTime,TargetRule,TargetLostPolicy,
            Effects.Select(e=>e.ToCore()),Range,Interruptible,HasEnergyLock?(float?)EnergyLock:null,
            (Steps??Array.Empty<SkillStepData>()).Select(s=>s.ToCore()),HasActivation?Activation.ToCore():null,VariantIds,VariantMode);
    }
}
