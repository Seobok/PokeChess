using System;
using System.Linq;
using UnityEngine;
using PokeChess.Core.Battle;

namespace PokeChess.Content
{
    [Serializable]
    public sealed class SkillEffectData
    {
        public SkillEffectType Type;
        public EffectTargetSelector TargetSelector;
        public float BaseValue;
        public bool SpellPowerScalable;
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
        public SkillEffectDefinition ToCore()=>new SkillEffectDefinition(Type,TargetSelector,BaseValue,SpellPowerScalable,
            DamageType,Radius,ProjectileSpeed,Status==null?null:Status.Id,StatusTargetTeam,RankValues,ValueSource,
            Type==SkillEffectType.ApplyStatus?RankStatuses.Select(s=>s.Id):null,HasImpactRadius?(int?)ImpactRadius:null);
    }
    [CreateAssetMenu(menuName="PokeChess/Content/Skill")]
    public sealed class SkillData : ScriptableObject
    {
        public string Id, DisplayName;
        public float CastTime=.4f, PostCastTime=.4f;
        public SkillTargetRule TargetRule;
        public TargetLostPolicy TargetLostPolicy;
        public int Range=3;
        public bool Interruptible=true, HasEnergyLock=true;
        public float EnergyLock=1;
        public SkillEffectData[] Effects=Array.Empty<SkillEffectData>();
        public SkillDefinition ToCore()=>new SkillDefinition(Id,CastTime,PostCastTime,TargetRule,TargetLostPolicy,
            Effects.Select(e=>e.ToCore()),Range,Interruptible,HasEnergyLock?(float?)EnergyLock:null);
    }
}
