using System;
using System.Linq;
using UnityEngine;
using PokeChess.Core.Battle;
using PokeChess.Core.Board;
using System.Collections.Generic;
using PokeChess.Core.Pokemon;

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
        public float ApIncrement, ApCap;
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
            (PathImpactEffects??Array.Empty<SkillEffectData>()).Select(e=>e.ToCore()),HasNumeric?Numeric.ToCore():null,ChoiceStatuses.Select(s=>s.Id),ChainDelay,ChainSequential,ProjectileGroupSize,PowerScaling,ApIncrement,ApCap);
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
        [Tooltip("Author all three ranks in this asset; runtime rank IDs remain stable.")]
        public bool Ranked;
        public SkillRankOverride[] RankOverrides=Array.Empty<SkillRankOverride>();
        public string[] VariantIds=Array.Empty<string>();
        public SkillVariantMode VariantMode;
        [TextArea] public string Description,Notes;
        [TextArea, Tooltip("Optional live description. Tokens: {value:0}, {ratio:0}, {duration:1}, {range}, {castTime}, {postCastTime}.")]
        public string DescriptionTemplate;
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
        public SkillEffectData[] AllEffects => Effects
            .Concat(RankOverrides.Where(r=>r.OverrideEffects).SelectMany(r=>r.Effects))
            .Concat(RankOverrides.Where(r=>r.OverrideEffects).SelectMany(r=>r.Steps).SelectMany(s=>s.Effects))
            .Concat((Steps??Array.Empty<SkillStepData>()).SelectMany(s=>
            s==null?throw new InvalidOperationException("Null skill step."):s.Effects)).SelectMany(e=>Flatten(e,0)).ToArray();
        private static IEnumerable<SkillEffectData> Flatten(SkillEffectData effect,int depth)
        {
            if(depth>8)throw new InvalidOperationException("Nested impact effects exceed authoring depth.");
            yield return effect;if(effect==null)yield break;
            if(effect.Type==SkillEffectType.Move&&effect.Movement!=null)
                foreach(var child in effect.Movement.ImpactEffects)foreach(var e in Flatten(child,depth+1))yield return e;
            foreach(var child in effect.PathImpactEffects??Array.Empty<SkillEffectData>())foreach(var e in Flatten(child,depth+1))yield return e;
        }
        public string RankId(int rank) => Id+"-r"+rank;
        public IEnumerable<SkillDefinition> ToDefinitions()
        {
            yield return ToCore();
            if(Ranked)for(int rank=1;rank<=3;rank++)yield return ToCore((UnitRank)rank);
        }
        public SkillDefinition ToCore()
        {
            // The router is generated, never separately authored. Preserve the existing event IDs.
            if(Ranked)return new SkillDefinition(Id,.4f,.4f,SkillTargetRule.Enemy,TargetLostPolicy.Cancel,
                Array.Empty<SkillEffectDefinition>(),energyLock:1,variantIds:Enumerable.Range(1,3).Select(RankId),variantMode:SkillVariantMode.Rank);
            return Definition(Id,null);
        }
        public SkillDefinition ToCore(UnitRank rank)
        {
            if((int)rank<1||(int)rank>3)throw new ArgumentOutOfRangeException(nameof(rank));
            return Ranked?Definition(RankId((int)rank),RankOverrides.SingleOrDefault(r=>r.Rank==rank)):ToCore();
        }
        private SkillDefinition Definition(string id,SkillRankOverride r)
        {
            return new SkillDefinition(id,r!=null&&r.OverrideTiming?r.CastTime:CastTime,
                r!=null&&r.OverrideTiming?r.PostCastTime:PostCastTime,
                r!=null&&r.OverrideTargeting?r.TargetRule:TargetRule,
                r!=null&&r.OverrideTargeting?r.TargetLostPolicy:TargetLostPolicy,
                (r!=null&&r.OverrideEffects?r.Effects:Effects).Select(e=>e.ToCore()),
                r!=null&&r.OverrideTargeting?r.Range:Range,
                r!=null&&r.OverrideTiming?r.Interruptible:Interruptible,
                (r!=null&&r.OverrideTiming?r.HasEnergyLock:HasEnergyLock)?(float?)(r!=null&&r.OverrideTiming?r.EnergyLock:EnergyLock):null,
                (r!=null&&r.OverrideEffects?r.Steps:Steps).Select(s=>s.ToCore()),
                (r!=null&&r.OverrideActivation?r.HasActivation:HasActivation)?(r!=null&&r.OverrideActivation?r.Activation:Activation).ToCore():null,
                r!=null&&r.OverrideVariants?r.VariantIds:VariantIds,
                r!=null&&r.OverrideVariants?r.VariantMode:VariantMode);
        }
    }
}
