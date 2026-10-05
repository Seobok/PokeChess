using System;
using System.Collections.Generic;
using System.Linq;
using PokeChess.Core.Board;

namespace PokeChess.Core.Battle
{
    public enum SkillEffectType { Damage, Heal, Shield, ProjectileDamage, ApplyStatus }
    public enum EffectTargetSelector { CastTarget, Self, EnemiesAroundCastTarget, AlliesAroundCaster }
    public enum SkillEffectOutcome { Applied, Spawned, Skipped }
    public enum SkillEffectSkipReason { None, NoValidTarget, CasterUnavailable, StackPolicyIgnored }
    public sealed class SkillEffectDefinition
    {
        public SkillEffectType Type { get; }
        public EffectTargetSelector TargetSelector { get; }
        public float BaseValue { get; }
        public bool SpellPowerScalable { get; }
        public DamageType DamageType { get; }
        public int Radius { get; }
        public float ProjectileSpeed { get; }
        public string StatusId { get; }
        public StatusTargetTeam StatusTargetTeam { get; }
        public bool RequiresEnemy => IsDamage || (Type==SkillEffectType.ApplyStatus&&StatusTargetTeam==StatusTargetTeam.Enemy);
        public bool NeedsCastTarget => TargetSelector==EffectTargetSelector.CastTarget ||
            TargetSelector==EffectTargetSelector.EnemiesAroundCastTarget;
        public bool IsDamage => Type==SkillEffectType.Damage||Type==SkillEffectType.ProjectileDamage;
        public SkillEffectDefinition(SkillEffectType type,EffectTargetSelector targetSelector,float baseValue,
            bool spellPowerScalable=false,DamageType damageType=DamageType.Magic,int radius=1,float projectileSpeed=6,string statusId=null,StatusTargetTeam statusTargetTeam=StatusTargetTeam.Enemy)
        {
            DamageCalculator.Validate(baseValue,true);DamageCalculator.Validate(projectileSpeed,true);
            if(radius<0||projectileSpeed<=0||!Enum.IsDefined(typeof(SkillEffectType),type)||
                !Enum.IsDefined(typeof(EffectTargetSelector),targetSelector)||!Enum.IsDefined(typeof(DamageType),damageType))
                throw new ArgumentOutOfRangeException();
            if(type==SkillEffectType.ApplyStatus&&(string.IsNullOrWhiteSpace(statusId)||baseValue!=0||spellPowerScalable||
                !Enum.IsDefined(typeof(StatusTargetTeam),statusTargetTeam)))throw new ArgumentException("Invalid status effect request.");
            bool enemy=IsDamageType(type)||(type==SkillEffectType.ApplyStatus&&statusTargetTeam==StatusTargetTeam.Enemy);
            if(enemy&&(targetSelector==EffectTargetSelector.Self||targetSelector==EffectTargetSelector.AlliesAroundCaster))
                throw new ArgumentException("Damage effects require enemy selectors.");
            if(!enemy&&targetSelector==EffectTargetSelector.EnemiesAroundCastTarget)
                throw new ArgumentException("Support effects require ally selectors.");
            StatusId=statusId;StatusTargetTeam=statusTargetTeam;Type=type;TargetSelector=targetSelector;BaseValue=baseValue;SpellPowerScalable=spellPowerScalable;
            DamageType=damageType;Radius=radius;ProjectileSpeed=projectileSpeed;
        }
        private static bool IsDamageType(SkillEffectType type)=>type==SkillEffectType.Damage||type==SkillEffectType.ProjectileDamage;
        public float ResolveValue(float spellPower)
        {
            DamageCalculator.Validate(spellPower,true);
            double value=BaseValue*(SpellPowerScalable?1+(double)spellPower/100:1);
            if(value>float.MaxValue)throw new OverflowException("Skill effect exceeds model capacity.");
            return (float)value;
        }
    }
    public readonly struct SkillEffectEvent
    {
        public string SkillId { get; }
        public string CasterId { get; }
        public string TargetId { get; }
        public int EffectIndex { get; }
        public SkillEffectType Type { get; }
        public long Tick { get; }
        public float CalculatedValue { get; }
        public float ActualValue { get; }
        public SkillEffectOutcome Outcome { get; }
        public SkillEffectSkipReason SkipReason { get; }
        public SkillEffectEvent(string skillId,string caster,string target,int index,SkillEffectType type,long tick,
            float calculated,float actual,SkillEffectOutcome outcome,SkillEffectSkipReason skip)
        { SkillId=skillId;CasterId=caster;TargetId=target;EffectIndex=index;Type=type;Tick=tick;
          CalculatedValue=calculated;ActualValue=actual;Outcome=outcome;SkipReason=skip; }
    }
    public readonly struct SkillEffectApplication
    {
        public float ActualValue { get; }
        public SkillEffectOutcome Outcome { get; }
        public SkillEffectSkipReason Reason { get; }
        public SkillEffectApplication(float actual,SkillEffectOutcome outcome=SkillEffectOutcome.Applied,SkillEffectSkipReason reason=SkillEffectSkipReason.None)
        {ActualValue=actual;Outcome=outcome;Reason=reason;}
    }
    public interface ISkillEffectHandler
    {
        SkillEffectType Type { get; }
        SkillEffectApplication Apply(BattleState battle,UnitCombatState caster,UnitCombatState target,
            SkillEffectDefinition effect,float value);
    }
    public sealed class DamageSkillEffectHandler : ISkillEffectHandler
    {
        private readonly IDamageProcessor damage;
        public SkillEffectType Type => SkillEffectType.Damage;
        public DamageSkillEffectHandler(IDamageProcessor damage) {this.damage=damage??throw new ArgumentNullException(nameof(damage));}
        public SkillEffectApplication Apply(BattleState b,UnitCombatState caster,UnitCombatState target,SkillEffectDefinition e,float value) =>
            new SkillEffectApplication(damage.Apply(b,new DamageRequest(caster.UnitInstanceId,target.UnitInstanceId,value,e.DamageType)).AppliedDamage);
    }
    public sealed class HealSkillEffectHandler : ISkillEffectHandler
    {
        public SkillEffectType Type => SkillEffectType.Heal;
        public SkillEffectApplication Apply(BattleState b,UnitCombatState caster,UnitCombatState target,SkillEffectDefinition e,float value)
        {
            float before=target.CurrentHP;
            float after=(float)Math.Min(target.Stats.MaxHP,(double)before+value);
            target.SetVitals(after,target.CurrentEnergy);
            return new SkillEffectApplication(after-before);
        }
    }
    public sealed class ShieldSkillEffectHandler : ISkillEffectHandler
    {
        public SkillEffectType Type => SkillEffectType.Shield;
        public SkillEffectApplication Apply(BattleState b,UnitCombatState caster,UnitCombatState target,SkillEffectDefinition e,float value)
        {
            double total=(double)target.CurrentShield+value;
            if(total>float.MaxValue)throw new OverflowException();
            float before=target.CurrentShield;target.SetShield((float)total);
            return new SkillEffectApplication(target.CurrentShield-before);
        }
    }
    public sealed class ProjectileSkillEffectHandler : ISkillEffectHandler
    {
        public SkillEffectType Type => SkillEffectType.ProjectileDamage;
        public SkillEffectApplication Apply(BattleState b,UnitCombatState caster,UnitCombatState target,SkillEffectDefinition e,float value)
        {
            bool spawned=b.Projectiles.TrySpawnSkill(caster,target,1,e.ProjectileSpeed,
                new DamageRequest(caster.UnitInstanceId,target.UnitInstanceId,value,e.DamageType));
            return new SkillEffectApplication(0,spawned?SkillEffectOutcome.Spawned:SkillEffectOutcome.Skipped);
        }
    }
    public sealed class StatusSkillEffectHandler : ISkillEffectHandler
    {
        public SkillEffectType Type=>SkillEffectType.ApplyStatus;
        public SkillEffectApplication Apply(BattleState b,UnitCombatState caster,UnitCombatState target,SkillEffectDefinition e,float value)
        {
            var applied=b.StatusEffects.Apply(e.StatusId,caster.UnitInstanceId,target.UnitInstanceId);
            return applied==null?new SkillEffectApplication(0,SkillEffectOutcome.Skipped,SkillEffectSkipReason.StackPolicyIgnored):
                new SkillEffectApplication(applied.StackCount);
        }
    }
    public sealed class SkillEffectExecutor : ISkillEffectExecutor
    {
        private readonly Dictionary<SkillEffectType,ISkillEffectHandler> handlers;
        public SkillEffectExecutor(IDamageProcessor damage=null,IEnumerable<ISkillEffectHandler> overrides=null)
        {
            handlers=new ISkillEffectHandler[]{new DamageSkillEffectHandler(damage??new DamageProcessor()),
                new HealSkillEffectHandler(),new ShieldSkillEffectHandler(),new ProjectileSkillEffectHandler(),new StatusSkillEffectHandler()}
                .ToDictionary(h=>h.Type);
            var replacements=(overrides??Array.Empty<ISkillEffectHandler>()).ToArray();
            if(replacements.Any(h=>h==null)||replacements.Select(h=>h.Type).Distinct().Count()!=replacements.Length)
                throw new ArgumentException("Invalid effect handlers.");
            foreach(var h in replacements) {
                if(!Enum.IsDefined(typeof(SkillEffectType),h.Type))throw new ArgumentException("Unknown handler type.");
                handlers[h.Type]=h;
            }
        }
        private static bool Valid(UnitCombatState caster,UnitCombatState target,SkillEffectDefinition effect) =>
            target!=null&&target.IsAlive&&target.IsOnBoard&&
            (effect.RequiresEnemy?target.TeamId!=caster.TeamId&&target.IsTargetable:target.TeamId==caster.TeamId);
        private static UnitCombatState[] Select(BattleState b,UnitCombatState caster,UnitCombatState castTarget,SkillEffectDefinition e)
        {
            IEnumerable<UnitCombatState> targets;
            switch(e.TargetSelector) {
                case EffectTargetSelector.Self: targets=new[]{caster};break;
                case EffectTargetSelector.CastTarget: targets=castTarget==null?Array.Empty<UnitCombatState>():new[]{castTarget};break;
                case EffectTargetSelector.EnemiesAroundCastTarget:
                    if(castTarget==null||!castTarget.IsTargetable)return Array.Empty<UnitCombatState>();
                    targets=b.Units.Where(t=>HexCoordinates.Distance(castTarget.Position,t.Position)<=e.Radius);break;
                default: targets=b.Units.Where(t=>HexCoordinates.Distance(caster.Position,t.Position)<=e.Radius);break;
            }
            return targets.Where(t=>Valid(caster,t,e)).OrderBy(t=>t.UnitInstanceId,StringComparer.Ordinal).ToArray();
        }
        public void Execute(BattleState battle,UnitCombatState caster,UnitCombatState target,SkillDefinition skill)
        {
            if(battle==null||skill==null)throw new ArgumentNullException();
            if(caster==null||!battle.Units.Contains(caster)||(target!=null&&!battle.Units.Contains(target)))
                throw new ArgumentException("Effect participants must belong to battle.");
            foreach(var e in skill.Effects.Where(e=>e.Type==SkillEffectType.ApplyStatus))
                if(battle.StatusEffects.GetDefinition(e.StatusId).TargetTeam!=e.StatusTargetTeam)
                    throw new ArgumentException("Status target team does not match skill effect.");
            // Resolve values before applying any effect; invalid scaling cannot leave partial numeric changes.
            var values=skill.Effects.Select(e=>e.ResolveValue(caster.EffectiveStats.SpellPower)).ToArray();
            for(int i=0;i<skill.Effects.Count;i++) {
                var e=skill.Effects[i];
                if(!caster.IsAlive||!caster.IsOnBoard) {
                    Record(battle,skill,caster,null,i,e,values[i],0,SkillEffectOutcome.Skipped,SkillEffectSkipReason.CasterUnavailable);break;
                }
                var targets=Select(battle,caster,target,e);
                if(targets.Length==0) {
                    Record(battle,skill,caster,null,i,e,values[i],0,SkillEffectOutcome.Skipped,SkillEffectSkipReason.NoValidTarget);continue;
                }
                foreach(var t in targets) {
                    if(!caster.IsAlive||!caster.IsOnBoard) {
                        Record(battle,skill,caster,t,i,e,values[i],0,SkillEffectOutcome.Skipped,SkillEffectSkipReason.CasterUnavailable);return;
                    }
                    if(!Valid(caster,t,e)) {
                        Record(battle,skill,caster,t,i,e,values[i],0,SkillEffectOutcome.Skipped,SkillEffectSkipReason.NoValidTarget);continue;
                    }
                    var applied=handlers[e.Type].Apply(battle,caster,t,e,values[i]);
                    Record(battle,skill,caster,t,i,e,values[i],applied.ActualValue,applied.Outcome,
                        applied.Outcome==SkillEffectOutcome.Skipped?
                            (applied.Reason==SkillEffectSkipReason.None?SkillEffectSkipReason.NoValidTarget:applied.Reason):SkillEffectSkipReason.None);
                }
            }
        }
        private static void Record(BattleState b,SkillDefinition skill,UnitCombatState caster,UnitCombatState target,
            int i,SkillEffectDefinition e,float value,float actual,SkillEffectOutcome outcome,SkillEffectSkipReason reason) =>
            b.RecordSkillEffect(new SkillEffectEvent(skill.Id,caster.UnitInstanceId,target?.UnitInstanceId,i,e.Type,b.CurrentTick,value,actual,outcome,reason));
    }
}
