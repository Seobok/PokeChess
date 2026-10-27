using System;
using System.Collections.Generic;
using System.Linq;

namespace PokeChess.Core.Battle
{
    public enum SkillStepLifetime { Cast, Independent }
    public enum SkillStepTargeting { KeepTarget, Reselect }

    // Delay is relative to CastTime; a negative delay permits a preparation effect.
    public sealed class SkillStepDefinition
    {
        public float Delay { get; }
        public float Interval { get; }
        public int Count { get; }
        public SkillStepLifetime Lifetime { get; }
        public SkillStepTargeting Targeting { get; }
        public bool SurvivesCaster { get; }
        public float FirstDelay { get; }
        public IReadOnlyList<SkillEffectDefinition> Effects { get; }
        public SkillStepDefinition(IEnumerable<SkillEffectDefinition> effects,float delay=0,float interval=0,int count=1,
            SkillStepLifetime lifetime=SkillStepLifetime.Cast,SkillStepTargeting targeting=SkillStepTargeting.KeepTarget,
            bool survivesCaster=true,float firstDelay=0)
        {
            DamageCalculator.Validate(delay,false);DamageCalculator.Validate(interval,true);
            DamageCalculator.Validate(firstDelay,true);
            if(lifetime==SkillStepLifetime.Cast&&firstDelay!=0)throw new ArgumentException("FirstDelay belongs to independent effects.");
            if(lifetime==SkillStepLifetime.Independent&&targeting!=SkillStepTargeting.KeepTarget)
                throw new ArgumentException("Independent effects retain their generated anchor; reselect belongs to cast steps.");
            if(count<1||(count>1&&interval<=0)||!Enum.IsDefined(typeof(SkillStepLifetime),lifetime)||
                !Enum.IsDefined(typeof(SkillStepTargeting),targeting))throw new ArgumentOutOfRangeException();
            var copy=(effects??throw new ArgumentNullException(nameof(effects))).ToArray();
            if(copy.Length==0||copy.Any(e=>e==null))throw new ArgumentException("Step effects cannot be empty/null.");
            if(lifetime==SkillStepLifetime.Independent&&copy.Any(e=>e.Type==SkillEffectType.ProjectileDamage||e.Type==SkillEffectType.Move||e.Type==SkillEffectType.PathProjectileDamage))
                throw new ArgumentException("Independent steps apply effects directly; projectiles own their flight lifetime.");
            Delay=delay;Interval=interval;Count=count;Lifetime=lifetime;Targeting=targeting;
            SurvivesCaster=survivesCaster;FirstDelay=firstDelay;Effects=Array.AsReadOnly(copy);
        }
        internal int StartOffset(SkillDefinition skill,int rate) =>
            BattleSimulation.ToTicks((double)skill.CastTime+Delay,rate,false);
        internal int RepeatOffset(int iteration,int rate) => BattleSimulation.ToTicks((double)Interval*iteration,rate,false);
    }
}
