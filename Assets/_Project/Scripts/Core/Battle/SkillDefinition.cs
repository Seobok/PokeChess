using System;
using System.Collections.Generic;
using System.Linq;
using PokeChess.Core.Board;

namespace PokeChess.Core.Battle
{
    public enum SkillTargetRule { Enemy, Self, LowestHPAlly }
    public enum TargetLostPolicy { Cancel, RetargetAtEffect, ContinueWithoutTarget }
    public enum SkillEffectKind { Damage, SelfShield, ProjectileDamage }
    public enum SkillEventKind { CastStarted, EffectApplied, CastCompleted, CastCancelled }
    public enum SkillCancelReason { None, TargetLost, Interrupted, CasterUnavailable, BattleEnded }
    public sealed class SkillDefinition
    {
        public string Id { get; }
        public float CastTime { get; }
        public float PostCastTime { get; }
        public float? EnergyLock { get; }
        public bool Interruptible { get; }
        public SkillTargetRule TargetRule { get; }
        public TargetLostPolicy TargetLostPolicy { get; }
        public int Range { get; }
        public IReadOnlyList<SkillEffectDefinition> Effects { get; }
        public SkillDefinition(string id,float castTime,float postCastTime,SkillTargetRule targetRule,
            TargetLostPolicy targetLostPolicy,SkillEffectKind effectKind,float effectValue,
            int range=3,bool interruptible=true,float? energyLock=null,DamageType damageType=DamageType.Magic,float projectileSpeed=6)
            : this(id,castTime,postCastTime,targetRule,targetLostPolicy,Legacy(effectKind,effectValue,damageType,projectileSpeed),
                range,interruptible,energyLock) { }
        private static SkillEffectDefinition[] Legacy(SkillEffectKind kind,float value,DamageType damage,float speed)
        {
            if(!Enum.IsDefined(typeof(SkillEffectKind),kind))throw new ArgumentOutOfRangeException(nameof(kind));
            return new[]{new SkillEffectDefinition(kind==SkillEffectKind.SelfShield?SkillEffectType.Shield:
                kind==SkillEffectKind.Damage?SkillEffectType.Damage:SkillEffectType.ProjectileDamage,
                kind==SkillEffectKind.SelfShield?EffectTargetSelector.Self:EffectTargetSelector.CastTarget,
                value,false,damage,projectileSpeed:speed)};
        }
        public SkillDefinition(string id,float castTime,float postCastTime,SkillTargetRule targetRule,
            TargetLostPolicy targetLostPolicy,IEnumerable<SkillEffectDefinition> effects,
            int range=3,bool interruptible=true,float? energyLock=null)
        {
            if(string.IsNullOrWhiteSpace(id))throw new ArgumentException(nameof(id));
            DamageCalculator.Validate(castTime,true);DamageCalculator.Validate(postCastTime,true);
            if(energyLock.HasValue)DamageCalculator.Validate(energyLock.Value,true);
            if(range<1||!Enum.IsDefined(typeof(SkillTargetRule),targetRule)||!Enum.IsDefined(typeof(TargetLostPolicy),targetLostPolicy))
                throw new ArgumentOutOfRangeException();
            var copy=(effects??throw new ArgumentNullException(nameof(effects))).ToArray();
            if(copy.Length==0||copy.Any(e=>e==null))throw new ArgumentException("Effects cannot be empty/null.");
            foreach(var e in copy) {
                if(e.TargetSelector==EffectTargetSelector.CastTarget &&
                    ((e.RequiresEnemy&&targetRule!=SkillTargetRule.Enemy)||(!e.RequiresEnemy&&targetRule==SkillTargetRule.Enemy)))
                    throw new ArgumentException("Cast target team is incompatible with effect.");
                if(e.TargetSelector==EffectTargetSelector.EnemiesAroundCastTarget&&targetRule!=SkillTargetRule.Enemy)
                    throw new ArgumentException("Enemy area requires enemy cast target.");
            }
            if(targetLostPolicy==TargetLostPolicy.ContinueWithoutTarget&&copy.All(e=>e.NeedsCastTarget))
                throw new ArgumentException("ContinueWithoutTarget requires an independent effect.");
            Id=id;CastTime=castTime;PostCastTime=postCastTime;EnergyLock=energyLock;Interruptible=interruptible;
            TargetRule=targetRule;TargetLostPolicy=targetLostPolicy;Range=range;Effects=Array.AsReadOnly(copy);
        }
    }
    public sealed class SkillCatalog
    {
        private readonly Dictionary<string,SkillDefinition> skills;
        public SkillCatalog(IEnumerable<SkillDefinition> definitions)
        {
            if(definitions==null)throw new ArgumentNullException(nameof(definitions));
            skills=new Dictionary<string,SkillDefinition>(StringComparer.Ordinal);
            foreach(var d in definitions) {
                if(d==null||skills.ContainsKey(d.Id))throw new ArgumentException("Null/duplicate skill.");
                skills.Add(d.Id,d);
            }
        }
        public bool TryGet(string id,out SkillDefinition skill) => skills.TryGetValue(id,out skill);
    }
    public sealed class SkillCastRuntime
    {
        public SkillDefinition Definition { get; }
        public string TargetId { get; internal set; }
        public long StartTick { get; }
        public long EffectTick { get; }
        public long EndTick { get; }
        public int EnergyLockTicks { get; }
        public bool EffectApplied { get; internal set; }
        public bool Finished { get; internal set; }
        public SkillCancelReason CancelReason { get; internal set; }
        internal SkillCastRuntime(SkillDefinition skill,string target,long start,int hit,int duration,int lockTicks)
        {
            Definition=skill;TargetId=target;StartTick=start;
            EffectTick=checked(start+hit);EndTick=checked(start+duration);EnergyLockTicks=lockTicks;
            checked { var maximumLock=EndTick+lockTicks; }
        }
    }
    public readonly struct SkillEvent
    {
        public string UnitId { get; }
        public string SkillId { get; }
        public string TargetId { get; }
        public long Tick { get; }
        public SkillEventKind Kind { get; }
        public SkillCancelReason Reason { get; }
        public SkillEvent(string unitId,SkillCastRuntime cast,long tick,SkillEventKind kind,SkillCancelReason reason)
        {UnitId=unitId;SkillId=cast.Definition.Id;TargetId=cast.TargetId;Tick=tick;Kind=kind;Reason=reason;}
    }
    public interface ISkillEffectExecutor
    {
        void Execute(BattleState battle,UnitCombatState caster,UnitCombatState target,SkillDefinition skill);
    }
    public class SkillCombatBehaviorPolicy : BasicAttackCombatBehaviorPolicy
    {
        private readonly SkillCatalog catalog;
        private readonly ISkillEffectExecutor executor;
        public SkillCombatBehaviorPolicy(SkillCatalog catalog,ISkillEffectExecutor executor=null,IDamageProcessor damage=null) : base(damage)
        { this.catalog=catalog??throw new ArgumentNullException(nameof(catalog));this.executor=executor??new SkillEffectExecutor(damage); }
        public override bool TryGetSkillDefinition(BattleState battle,UnitCombatState unit,out SkillDefinition skill) =>
            catalog.TryGet(unit.SkillId,out skill);
        public override void ExecuteSkillEffect(BattleState battle,UnitCombatState unit,UnitCombatState target,SkillDefinition skill) =>
            executor.Execute(battle,unit,target,skill);
    }
}
