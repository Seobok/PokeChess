using System;
using System.Collections.Generic;
using System.Linq;
namespace PokeChess.Core.Battle
{
    public enum CrowdControlKind { None, Stun, Root, Silence, Disarm, Taunt, Slow }
    public enum StackPolicy { None, Refresh, Stack, Strongest }
    public enum SourceStackRule { PerSource, Shared }
    public enum StatusTargetTeam { Ally, Enemy }
    public enum CombatStat { Attack, SpellPower, Armor, MagicResistance, AttackSpeed, MoveSpeed, DamageAmplification, DamageReduction }
    public enum StatModifierType { Flat, Percentage, Multiplicative }
    public sealed class StatModifierDefinition
    {
        public CombatStat Stat { get; }
        public StatModifierType Type { get; }
        public float Value { get; }
        public StatModifierDefinition(CombatStat stat,StatModifierType type,float value)
        {
            DamageCalculator.Validate(value,false);
            if(!Enum.IsDefined(typeof(CombatStat),stat)||!Enum.IsDefined(typeof(StatModifierType),type)||
                (type==StatModifierType.Multiplicative&&value<0))throw new ArgumentOutOfRangeException();
            Stat=stat;Type=type;Value=value;
        }
    }
    public sealed class StatusEffectDefinition
    {
        public string Id { get; }
        public string StackGroup { get; }
        public float Duration { get; }
        public CrowdControlKind CrowdControl { get; }
        public float SlowFraction { get; }
        public StackPolicy StackPolicy { get; }
        public SourceStackRule SourceRule { get; }
        public int MaxStack { get; }
        public StatusTargetTeam TargetTeam { get; }
        public IReadOnlyList<StatModifierDefinition> Modifiers { get; }
        public StatusEffectDefinition(string id,float duration,StatusTargetTeam targetTeam=StatusTargetTeam.Enemy,
            CrowdControlKind crowdControl=CrowdControlKind.None,StackPolicy stackPolicy=StackPolicy.Refresh,
            SourceStackRule sourceRule=SourceStackRule.PerSource,int maxStack=1,
            IEnumerable<StatModifierDefinition> modifiers=null,string stackGroup=null,float slowFraction=0)
        {
            if(string.IsNullOrWhiteSpace(id))throw new ArgumentException(nameof(id));
            DamageCalculator.Validate(duration,true);DamageCalculator.Validate(slowFraction,true);
            if(duration<=0||slowFraction>1||maxStack<1||!Enum.IsDefined(typeof(CrowdControlKind),crowdControl)||
                !Enum.IsDefined(typeof(StackPolicy),stackPolicy)||!Enum.IsDefined(typeof(SourceStackRule),sourceRule)||
                !Enum.IsDefined(typeof(StatusTargetTeam),targetTeam))throw new ArgumentOutOfRangeException();
            var copy=(modifiers??Array.Empty<StatModifierDefinition>()).ToArray();
            if(copy.Any(m=>m==null)||(crowdControl!=CrowdControlKind.None&&(targetTeam!=StatusTargetTeam.Enemy||copy.Length>0))||
                (crowdControl==CrowdControlKind.None&&copy.Length==0)||
                (stackPolicy==StackPolicy.Strongest&&crowdControl==CrowdControlKind.None&&copy.Length!=1))
                throw new ArgumentException("Invalid status configuration.");
            if(stackGroup!=null&&string.IsNullOrWhiteSpace(stackGroup))throw new ArgumentException(nameof(stackGroup));
            Id=id;StackGroup=stackGroup??id;Duration=duration;TargetTeam=targetTeam;CrowdControl=crowdControl;
            SlowFraction=slowFraction;StackPolicy=stackPolicy;SourceRule=sourceRule;MaxStack=maxStack;
            Modifiers=Array.AsReadOnly(copy);
        }
    }
    public sealed class StatusCatalog
    {
        private readonly Dictionary<string,StatusEffectDefinition> definitions=new Dictionary<string,StatusEffectDefinition>(StringComparer.Ordinal);
        public StatusCatalog(IEnumerable<StatusEffectDefinition> entries)
        {
            if(entries==null)throw new ArgumentNullException(nameof(entries));
            foreach(var e in entries) {
                if(e==null||definitions.ContainsKey(e.Id))throw new ArgumentException("Null/duplicate status.");
                definitions.Add(e.Id,e);
            }
        }
        public StatusEffectDefinition Get(string id)
        { if(id==null||!definitions.TryGetValue(id,out var d))throw new ArgumentException("Unknown status: "+id);return d; }
    }
    public sealed class EffectiveCombatStats
    {
        public float Attack { get; }
        public float SpellPower { get; }
        public float Armor { get; }
        public float MagicResistance { get; }
        public float AttackSpeed { get; }
        public float MoveSpeed { get; }
        internal EffectiveCombatStats(float attack,float spell,float armor,float mr,float attackSpeed,float moveSpeed)
        {Attack=attack;SpellPower=spell;Armor=armor;MagicResistance=mr;AttackSpeed=attackSpeed;MoveSpeed=moveSpeed;}
    }
    public sealed class StatusEffectSnapshot
    {
        public long InstanceId { get; }
        public StatusEffectDefinition Definition { get; }
        public string SourceId { get; }
        public string TargetId { get; }
        public long StartTick { get; }
        public long ExpireTick { get; }
        public int StackCount { get; }
        internal StatusEffectSnapshot(long id,StatusEffectDefinition d,string source,string target,long start,long end,int count)
        {InstanceId=id;Definition=d;SourceId=source;TargetId=target;StartTick=start;ExpireTick=end;StackCount=count;}
    }
    public enum StatusEventKind { Applied, Ignored, Refreshed, Stacked, Expired, Removed }
    public readonly struct StatusEvent
    {
        public StatusEffectSnapshot Effect { get; }
        public long Tick { get; }
        public StatusEventKind Kind { get; }
        public StatusEvent(StatusEffectSnapshot effect,long tick,StatusEventKind kind) {Effect=effect;Tick=tick;Kind=kind;}
    }
}
