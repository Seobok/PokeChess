using System;
using System.Collections.Generic;
using System.Linq;
namespace PokeChess.Core.Battle
{
    public enum CrowdControlKind { None, Stun, Root, Silence, Disarm, Taunt, Slow }
    public enum StatusBehavior { None, Sleep, RestSleep, Freeze, Paralysis, Confusion, Poison, Burn, Untargetable, Blind, Bleed, Focus, GuaranteedCritical, SwordDance, AttackSplash, AttackRecovery, ActionRecovery }
    public enum StackPolicy { None, Refresh, Stack, Strongest }
    public enum SourceStackRule { PerSource, Shared }
    public enum StatusTargetTeam { Ally, Enemy }
    public enum CombatStat { Attack, SpellPower, Armor, MagicResistance, AttackSpeed, MoveSpeed, DamageAmplification, DamageReduction, Accuracy, Evasion, CritChance, EnergyGain, HealingReceived, ArmorPenetration, AttackRange }
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
        public StatusBehavior Behavior { get; }
        public float RecoveryFraction { get; }
        public float EnergyRefundFraction { get; }
        public float EffectValue { get; }
        public bool FixedSpellDamage { get; }
        public float RestReduction { get; }
        public float ReflectionMultiplier { get; }
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
            IEnumerable<StatModifierDefinition> modifiers=null,string stackGroup=null,float slowFraction=0,StatusBehavior behavior=StatusBehavior.None,float recoveryFraction=0,float energyRefundFraction=0,float effectValue=0,float restReduction=.3f,float reflectionMultiplier=0,bool fixedSpellDamage=false)
        {
            if(string.IsNullOrWhiteSpace(id))throw new ArgumentException(nameof(id));
            DamageCalculator.Validate(duration,true);DamageCalculator.Validate(slowFraction,true);
            if(duration<=0||slowFraction>1||maxStack<1||!Enum.IsDefined(typeof(CrowdControlKind),crowdControl)||
                !Enum.IsDefined(typeof(StackPolicy),stackPolicy)||!Enum.IsDefined(typeof(SourceStackRule),sourceRule)||
                !Enum.IsDefined(typeof(StatusTargetTeam),targetTeam))throw new ArgumentOutOfRangeException();
            DamageCalculator.Validate(recoveryFraction,true);
            DamageCalculator.Validate(effectValue,true);DamageCalculator.Validate(restReduction,true);DamageCalculator.Validate(reflectionMultiplier,true);
            if(restReduction>1)throw new ArgumentOutOfRangeException();
            FixedSpellDamage=fixedSpellDamage;EffectValue=effectValue;RestReduction=restReduction;ReflectionMultiplier=reflectionMultiplier;
            DamageCalculator.Validate(energyRefundFraction,true);
            if(energyRefundFraction>1||behavior!=StatusBehavior.Focus&&energyRefundFraction!=0||
                behavior==StatusBehavior.Focus&&(targetTeam!=StatusTargetTeam.Ally||duration!=2||energyRefundFraction<=0))
                throw new ArgumentException("Focus requires two seconds and a positive energy refund fraction.");
            if(!Enum.IsDefined(typeof(StatusBehavior),behavior)||
                behavior==StatusBehavior.RestSleep&&(targetTeam!=StatusTargetTeam.Ally||duration!=2||recoveryFraction<=0)||
                behavior!=StatusBehavior.RestSleep&&recoveryFraction!=0||
                behavior!=StatusBehavior.None&&behavior!=StatusBehavior.RestSleep&&behavior!=StatusBehavior.Untargetable&&behavior!=StatusBehavior.Focus&&behavior!=StatusBehavior.GuaranteedCritical&&behavior!=StatusBehavior.SwordDance&&behavior!=StatusBehavior.AttackSplash&&behavior!=StatusBehavior.AttackRecovery&&behavior!=StatusBehavior.ActionRecovery&&targetTeam!=StatusTargetTeam.Enemy||
                behavior==StatusBehavior.Untargetable&&targetTeam!=StatusTargetTeam.Ally)
                throw new ArgumentException("Invalid status behavior.");
            var copy=(modifiers??Array.Empty<StatModifierDefinition>()).ToArray();
            if(copy.Any(m=>m==null)||(crowdControl!=CrowdControlKind.None&&(targetTeam!=StatusTargetTeam.Enemy||copy.Length>0))||
                (crowdControl==CrowdControlKind.None&&copy.Length==0&&behavior==StatusBehavior.None)||
                (stackPolicy==StackPolicy.Strongest&&crowdControl==CrowdControlKind.None&&copy.Length!=1&&behavior==StatusBehavior.None))
                throw new ArgumentException("Invalid status configuration.");
            if(stackGroup!=null&&string.IsNullOrWhiteSpace(stackGroup))throw new ArgumentException(nameof(stackGroup));
            Behavior=behavior;RecoveryFraction=recoveryFraction;EnergyRefundFraction=energyRefundFraction;Id=id;StackGroup=stackGroup??id;Duration=duration;TargetTeam=targetTeam;CrowdControl=crowdControl;
            SlowFraction=slowFraction;StackPolicy=stackPolicy;SourceRule=sourceRule;MaxStack=behavior==StatusBehavior.Poison?3:maxStack;
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
        public float Accuracy { get; }
        public float Evasion { get; }
        public float CritChance { get; }
        public float EnergyGain { get; }
        public float ArmorPenetration { get; }
        public float Attack { get; }
        public float SpellPower { get; }
        public float Armor { get; }
        public float MagicResistance { get; }
        public float AttackSpeed { get; }
        public float MoveSpeed { get; }
        internal EffectiveCombatStats(float attack,float spell,float armor,float mr,float attackSpeed,float moveSpeed,float accuracy=1,float evasion=0,float critChance=0,float energyGain=1,float armorPenetration=0)
        {Accuracy=accuracy;Evasion=evasion;CritChance=critChance;EnergyGain=energyGain;ArmorPenetration=armorPenetration;Attack=attack;SpellPower=spell;Armor=armor;MagicResistance=mr;AttackSpeed=attackSpeed;MoveSpeed=moveSpeed;}
    }
    public sealed class StatusEffectSnapshot
    {
        public long NextTick { get; }
        public int Pulses { get; }
        public long InstanceId { get; }
        public StatusEffectDefinition Definition { get; }
        public string SourceId { get; }
        public string TargetId { get; }
        public long StartTick { get; }
        public long ExpireTick { get; }
        public int StackCount { get; }
        internal StatusEffectSnapshot(long id,StatusEffectDefinition d,string source,string target,long start,long end,int count,long nextTick=0,int pulses=0)
        {NextTick=nextTick;Pulses=pulses;InstanceId=id;Definition=d;SourceId=source;TargetId=target;StartTick=start;ExpireTick=end;StackCount=count;}
    }
    public enum StatusEventKind { Applied, Ignored, Refreshed, Stacked, Expired, Removed, Shortened, Pulse }
    public readonly struct StatusEvent
    {
        public StatusEffectSnapshot Effect { get; }
        public long Tick { get; }
        public StatusEventKind Kind { get; }
        public StatusEvent(StatusEffectSnapshot effect,long tick,StatusEventKind kind) {Effect=effect;Tick=tick;Kind=kind;}
    }
}
