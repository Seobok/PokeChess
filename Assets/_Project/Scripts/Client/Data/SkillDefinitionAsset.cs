using System;
using System.Linq;
using PokeChess.Core.Battle;
using UnityEngine;
namespace PokeChess.Client.Data
{
    [Serializable]
    public sealed class SkillEffectData
    {
        public SkillEffectType type;
        public EffectTargetSelector targetSelector;
        public float baseValue=40;
        public bool spellPowerScalable;
        public DamageType damageType=DamageType.Magic;
        public int radius=1;
        public float projectileSpeed=6;
        public SkillEffectDefinition ToDefinition() => new SkillEffectDefinition(type,targetSelector,baseValue,
            spellPowerScalable,damageType,radius,projectileSpeed);
    }
    [CreateAssetMenu(menuName="PokeChess/Data/Skill Definition")]
    public sealed class SkillDefinitionAsset : ScriptableObject
    {
        [SerializeField] private string skillId="sample-skill";
        [SerializeField] private float castTime=.25f;
        [SerializeField] private float postCastTime=.25f;
        [SerializeField] private bool overrideEnergyLock;
        [SerializeField] private float energyLock=1;
        [SerializeField] private bool interruptible=true;
        [SerializeField] private SkillTargetRule targetRule;
        [SerializeField] private TargetLostPolicy targetLostPolicy;
        [SerializeField] private int range=3;
        [SerializeField, HideInInspector] private SkillEffectKind effectKind;
        [SerializeField, HideInInspector] private float effectValue=40;
        [SerializeField, HideInInspector] private DamageType damageType=DamageType.Magic;
        [SerializeField, HideInInspector] private float projectileSpeed=6;
        [SerializeField] private SkillEffectData[] effects=Array.Empty<SkillEffectData>();
        public SkillDefinition ToDefinition()
        {
            if(effects!=null&&effects.Length>0) {
                if(effects.Any(e=>e==null))throw new ArgumentException("Null skill effect data.");
                return new SkillDefinition(skillId,castTime,postCastTime,targetRule,targetLostPolicy,
                    effects.Select(e=>e.ToDefinition()),range,interruptible,overrideEnergyLock?(float?)energyLock:null);
            }
            // Serialized 1.13 asset migration fallback. Newly authored assets should fill Effects.
            return new SkillDefinition(skillId,castTime,postCastTime,targetRule,targetLostPolicy,
                effectKind,effectValue,range,interruptible,overrideEnergyLock?(float?)energyLock:null,damageType,projectileSpeed);
        }
    }
}
