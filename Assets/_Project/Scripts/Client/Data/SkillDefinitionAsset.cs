using System;
using System.Linq;
using PokeChess.Core.Battle;
using UnityEngine;
namespace PokeChess.Client.Data
{
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
        [SerializeField] private SkillEffectKind effectKind;
        [SerializeField] private float effectValue=40;
        [SerializeField] private DamageType damageType=DamageType.Magic;
        [SerializeField] private float projectileSpeed=6;
        public SkillDefinition ToDefinition() => new SkillDefinition(skillId,castTime,postCastTime,targetRule,
            targetLostPolicy,effectKind,effectValue,range,interruptible,overrideEnergyLock?(float?)energyLock:null,damageType,projectileSpeed);
    }
}
