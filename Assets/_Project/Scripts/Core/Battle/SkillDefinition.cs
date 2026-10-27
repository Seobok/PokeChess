using System;
using System.Collections.Generic;
using System.Linq;
using PokeChess.Core.Board;

namespace PokeChess.Core.Battle
{
    public enum SkillVariantMode { Random, Rank }
    public enum SkillTargetRule { Enemy, Self, LowestHPAlly, FarthestEnemy, NearestAlly }
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
        public IReadOnlyList<SkillStepDefinition> Steps { get; }
        public SkillActivationDefinition Activation { get; }
        public IReadOnlyList<string> VariantIds { get; }
        public SkillVariantMode VariantMode { get; }
        internal bool UsesAuthoredSteps { get; }
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
            int range=3,bool interruptible=true,float? energyLock=null,IEnumerable<SkillStepDefinition> steps=null,SkillActivationDefinition activation=null,IEnumerable<string> variantIds=null,SkillVariantMode variantMode=SkillVariantMode.Random)
        {
            if(string.IsNullOrWhiteSpace(id))throw new ArgumentException(nameof(id));
            DamageCalculator.Validate(castTime,true);DamageCalculator.Validate(postCastTime,true);
            if(energyLock.HasValue)DamageCalculator.Validate(energyLock.Value,true);
            if(range<1||!Enum.IsDefined(typeof(SkillTargetRule),targetRule)||!Enum.IsDefined(typeof(TargetLostPolicy),targetLostPolicy))
                throw new ArgumentOutOfRangeException();
            var authored=(steps??Array.Empty<SkillStepDefinition>()).ToArray();
            if(authored.Any(s=>s==null||castTime+(double)s.Delay<0))throw new ArgumentException("Invalid skill step time.");
            var copy=(effects??throw new ArgumentNullException(nameof(effects))).ToArray();
            if(authored.Length>0&&copy.Length>0)throw new ArgumentException("Use legacy effects or steps, not both.");
            if(authored.Length>0)copy=authored.SelectMany(s=>s.Effects).ToArray();
            var variants=(variantIds??Array.Empty<string>()).ToArray();
            if(!Enum.IsDefined(typeof(SkillVariantMode),variantMode)||variants.Any(string.IsNullOrWhiteSpace)||variants.Distinct().Count()!=variants.Length||variantMode==SkillVariantMode.Rank&&variants.Length!=3)throw new ArgumentException("Invalid skill variants.");
            VariantIds=Array.AsReadOnly(variants);VariantMode=variantMode;
            if(copy.Length==0&&variants.Length==0||copy.Any(e=>e==null))throw new ArgumentException("Effects cannot be empty/null.");
            foreach(var e in copy) {
                if(e.TargetSelector==EffectTargetSelector.CastTarget &&
                    ((e.RequiresEnemy&&targetRule!=SkillTargetRule.Enemy&&targetRule!=SkillTargetRule.FarthestEnemy)||(!e.RequiresEnemy&&(targetRule==SkillTargetRule.Enemy||targetRule==SkillTargetRule.FarthestEnemy))))
                    throw new ArgumentException("Cast target team is incompatible with effect.");
                if(e.NeedsCastTarget&&e.RequiresEnemy&&targetRule!=SkillTargetRule.Enemy&&targetRule!=SkillTargetRule.FarthestEnemy)
                    throw new ArgumentException("Enemy area requires enemy cast target.");
            }
            if(targetLostPolicy==TargetLostPolicy.ContinueWithoutTarget&&copy.All(e=>e.NeedsCastTarget))
                throw new ArgumentException("ContinueWithoutTarget requires an independent effect.");
            Id=id;CastTime=castTime;PostCastTime=postCastTime;EnergyLock=energyLock;Interruptible=interruptible;
            TargetRule=targetRule;TargetLostPolicy=targetLostPolicy;Range=range;Effects=Array.AsReadOnly(copy);
            Steps=Array.AsReadOnly(authored.Length==0?(copy.Length==0?Array.Empty<SkillStepDefinition>():new[]{new SkillStepDefinition(copy)}):authored);
            UsesAuthoredSteps=authored.Length>0;Activation=activation;
        }
        internal int LastCastOffset(int rate) => Steps.Max(s=>checked(s.StartOffset(this,rate)+
            (s.Lifetime==SkillStepLifetime.Cast?s.RepeatOffset(s.Count-1,rate):0)));
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
        public long EffectTick { get; internal set; }
        internal long PausedTicks { get; set; }
        public long EndTick { get; internal set; }
        public int EnergyLockTicks { get; }
        public bool EffectApplied { get; internal set; }
        public bool Finished { get; internal set; }
        public SkillCancelReason CancelReason { get; internal set; }
        public long CastId { get; internal set; }
        internal int[] Iterations { get; set; }
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
        public long CastId { get; }
        public int StepIndex { get; }
        public int Iteration { get; }
        public SkillEvent(string unitId,SkillCastRuntime cast,long tick,SkillEventKind kind,SkillCancelReason reason)
            :this(unitId,cast,tick,kind,reason,-1,0) { }
        public SkillEvent(string unitId,SkillCastRuntime cast,long tick,SkillEventKind kind,SkillCancelReason reason,int stepIndex,int iteration)
        {UnitId=unitId;SkillId=cast.Definition.Id;TargetId=cast.TargetId;Tick=tick;Kind=kind;Reason=reason;
         CastId=cast.CastId;StepIndex=stepIndex;Iteration=iteration;}
    }
    public interface ISkillEffectExecutor
    {
        void Execute(BattleState battle,UnitCombatState caster,UnitCombatState target,SkillDefinition skill);
    }
    public interface ISkillStepExecutor : ISkillEffectExecutor
    {
        void ExecuteStep(BattleState battle,UnitCombatState caster,UnitCombatState target,SkillDefinition skill,
            SkillStepDefinition step,long castId,int stepIndex,int iteration);
        PreparedSkillStep PrepareStep(BattleState battle,UnitCombatState caster,UnitCombatState target,SkillDefinition skill,
            SkillStepDefinition step,long castId,int stepIndex);
    }
    public class SkillCombatBehaviorPolicy : BasicAttackCombatBehaviorPolicy
    {
        private readonly SkillCatalog catalog;
        private readonly ISkillEffectExecutor executor;
        public SkillCombatBehaviorPolicy(SkillCatalog catalog,ISkillEffectExecutor executor=null,IDamageProcessor damage=null) : base(damage)
        { this.catalog=catalog??throw new ArgumentNullException(nameof(catalog));this.executor=executor??new SkillEffectExecutor(damage); }
        public override bool TryGetSkillDefinition(BattleState battle,UnitCombatState unit,out SkillDefinition skill) =>
            catalog.TryGet(unit.SkillId,out skill);
        public override bool TryGetSkillById(string id,out SkillDefinition skill)=>catalog.TryGet(id,out skill);
        public override void ExecuteSkillEffect(BattleState battle,UnitCombatState unit,UnitCombatState target,SkillDefinition skill) =>
            executor.Execute(battle,unit,target,skill);
        public override void ExecuteSkillStep(BattleState battle,UnitCombatState unit,UnitCombatState target,SkillDefinition skill,
            SkillStepDefinition step,long castId,int index,int iteration)
        {
            if(executor is ISkillStepExecutor timed)timed.ExecuteStep(battle,unit,target,skill,step,castId,index,iteration);
            else if(skill.Steps.Count==1&&step.Count==1&&step.Delay==0&&step.Lifetime==SkillStepLifetime.Cast)
                executor.Execute(battle,unit,target,skill);
            else throw new InvalidOperationException("Timed skills require ISkillStepExecutor.");
        }
        public override PreparedSkillStep PrepareSkillStep(BattleState battle,UnitCombatState unit,UnitCombatState target,
            SkillDefinition skill,SkillStepDefinition step,long castId,int index) =>
            executor is ISkillStepExecutor timed?timed.PrepareStep(battle,unit,target,skill,step,castId,index):
            throw new InvalidOperationException("Independent skills require ISkillStepExecutor.");
    }
}
