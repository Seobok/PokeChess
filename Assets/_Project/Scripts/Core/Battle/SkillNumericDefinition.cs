using System;
using System.Collections.Generic;
using System.Linq;

namespace PokeChess.Core.Battle
{
    public sealed class SkillActivationDefinition
    {
        public int MinimumAdjacentEnemies { get; }
        public bool RequireSafeDestination { get; }
        public SkillActivationDefinition(int minimumAdjacentEnemies=2,bool requireSafeDestination=true)
        {if(minimumAdjacentEnemies<0||minimumAdjacentEnemies>6)throw new ArgumentOutOfRangeException();
         MinimumAdjacentEnemies=minimumAdjacentEnemies;RequireSafeDestination=requireSafeDestination;}
    }
    public enum SkillValueCondition { None, TargetSleeping, TargetLowHP, TargetHigherMaxHP }
    public sealed class SkillNumericDefinition
    {
        public SkillValueCondition Condition { get; }
        public float Threshold { get; }
        public IReadOnlyList<float> ConditionalValues { get; }
        public float? DamageCap { get; }
        public float Lifesteal { get; }
        public float ArmorPenetration { get; }
        public bool CanCrit { get; }
        public float CritBonus { get; }
        public float Chance { get; }
        public float? ShieldDuration { get; }
        public float ExecuteThreshold { get; }
        public float BossBonus { get; }
        public bool Bounty { get; }
        public float MagicPenetration { get; }
        public SkillNumericDefinition(SkillValueCondition condition=SkillValueCondition.None,float threshold=.25f,
            IEnumerable<float> conditionalValues=null,float? damageCap=null,float lifesteal=0,float armorPenetration=0,
            bool canCrit=false,float critBonus=0,float chance=1,float? shieldDuration=null,float executeThreshold=0,float bossBonus=0,bool bounty=false,float magicPenetration=0)
        {
            DamageCalculator.Validate(magicPenetration,true);if(magicPenetration>1)throw new ArgumentOutOfRangeException();
            Bounty=bounty;MagicPenetration=magicPenetration;
            foreach(var v in new[]{threshold,lifesteal,armorPenetration,critBonus,chance,executeThreshold,bossBonus})DamageCalculator.Validate(v,true);
            if(!Enum.IsDefined(typeof(SkillValueCondition),condition)||threshold>1||lifesteal>1||armorPenetration>1||critBonus>1||chance>1||executeThreshold>1||
                critBonus>0&&!canCrit||shieldDuration.HasValue&&shieldDuration.Value<=0||damageCap.HasValue&&damageCap.Value<=0)throw new ArgumentException();
            if(shieldDuration.HasValue)DamageCalculator.Validate(shieldDuration.Value,true);
            if(damageCap.HasValue)DamageCalculator.Validate(damageCap.Value,true);
            var values=(conditionalValues??new float[3]).ToArray();if(values.Length!=3)throw new ArgumentException();
            foreach(var v in values)DamageCalculator.Validate(v,true);
            if(condition!=SkillValueCondition.None&&conditionalValues==null)throw new ArgumentException("Conditional values are required.");
            Condition=condition;Threshold=threshold;ConditionalValues=Array.AsReadOnly(values);DamageCap=damageCap;Lifesteal=lifesteal;
            ArmorPenetration=armorPenetration;CanCrit=canCrit;CritBonus=critBonus;Chance=chance;ShieldDuration=shieldDuration;
            ExecuteThreshold=executeThreshold;BossBonus=bossBonus;
        }
    }
    // Values are frozen at release; target HP/state are deliberately evaluated on impact.
    public sealed class SkillDamagePolicy
    {
        public SkillNumericDefinition Numeric { get; }
        public bool TargetMaxHP { get; }
        public float ConditionalValue { get; }
        public float SourceMaxHP { get; }
        public SkillDamagePolicy(SkillNumericDefinition numeric,bool targetMaxHP,float conditionalValue,float sourceMaxHP)
        {Numeric=numeric;TargetMaxHP=targetMaxHP;ConditionalValue=conditionalValue;SourceMaxHP=sourceMaxHP;}
        internal float Resolve(float original,UnitCombatState target)
        {
            var n=Numeric;bool condition=n!=null&&(n.Condition==SkillValueCondition.TargetSleeping&&target.StatusSystem.IsSleeping(target)||
                n.Condition==SkillValueCondition.TargetLowHP&&target.CurrentHP/target.Stats.MaxHP<=n.Threshold||
                n.Condition==SkillValueCondition.TargetHigherMaxHP&&target.Stats.MaxHP>SourceMaxHP);
            double raw=condition?ConditionalValue:original;if(TargetMaxHP)raw*=target.Stats.MaxHP;
            if(n?.DamageCap!=null)raw=Math.Min(raw,n.DamageCap.Value);
            if(raw>float.MaxValue)throw new OverflowException();return (float)raw;
        }
    }
    public static class SkillNumericMath
    {
        // Shared by normal hits, conditional hits and status damage snapshots.
        public static float Magnitude(float baseValue,SkillValueSource source,float attack,float spellPower,bool scalable,SkillPowerScaling scaling)
        {
            DamageCalculator.Validate(baseValue,true);DamageCalculator.Validate(attack,true);DamageCalculator.Validate(spellPower,true);
            if(!Enum.IsDefined(typeof(SkillPowerScaling),scaling))throw new ArgumentOutOfRangeException(nameof(scaling));
            double value=baseValue*(source==SkillValueSource.Attack?(double)attack:1);
            if(scalable)value*=scaling==SkillPowerScaling.TotalSpellPower?(double)spellPower/100:1+(double)spellPower/100;
            if(value>float.MaxValue)throw new OverflowException("Skill magnitude exceeds model capacity.");
            return (float)value;
        }
        public static float SourceValue(UnitCombatState caster,SkillEffectDefinition effect)
        {
            double value=effect.ResolveValue(caster.Rank,caster.EffectiveStats.SpellPower,caster.EffectiveStats.Attack);
            if(effect.ValueSource==SkillValueSource.CasterMaxHP)value*=caster.Stats.MaxHP;
            if(effect.ValueSource==SkillValueSource.CasterMaxEnergy)value*=caster.Stats.MaxEnergy;
            if(value>float.MaxValue)throw new OverflowException();return (float)value;
        }
        public static DamageSourceSnapshot Capture(BattleState b,UnitCombatState caster,DamageOrigin origin=DamageOrigin.Direct)=>
            new DamageSourceSnapshot(caster.EffectiveDamageModifiers.Amplification,
                b.Items.Outgoing(caster,new DamageRequest(caster.UnitInstanceId,caster.UnitInstanceId,0,DamageType.Magic,origin:origin)),
                caster.EffectiveStats.CritChance,caster.EffectiveStats.ArmorPenetration,caster.EffectiveStats.Attack,
                caster.EffectiveStats.SpellPower,caster.Stats.MaxHP);
        public static DamageRequest Request(BattleState b,UnitCombatState caster,UnitCombatState target,SkillEffectDefinition effect,float value,
            DamageOrigin origin=DamageOrigin.Direct,DamageSourceSnapshot? snapshot=null)
        {
            SkillDamagePolicy policy=null;var n=effect.Numeric;
            if(n!=null||effect.ValueSource==SkillValueSource.TargetMaxHP) {
                double alternative=Magnitude(n?.ConditionalValues[(int)caster.Rank-1]??0,effect.ValueSource,
                    snapshot?.Attack??caster.EffectiveStats.Attack,snapshot?.SpellPower??caster.EffectiveStats.SpellPower,effect.SpellPowerScalable,effect.PowerScaling);
                if(effect.ValueSource==SkillValueSource.CasterMaxHP)alternative*=snapshot?.MaxHP??caster.Stats.MaxHP;
                if(alternative>float.MaxValue)throw new OverflowException();
                policy=new SkillDamagePolicy(n,effect.ValueSource==SkillValueSource.TargetMaxHP,(float)alternative,snapshot?.MaxHP??caster.Stats.MaxHP);
            }
            return new DamageRequest(caster.UnitInstanceId,target.UnitInstanceId,value,effect.DamageType,n?.CanCrit??false,
                origin:origin,sourceSnapshot:snapshot,skillPolicy:policy);
        }
        public static bool Matches(SkillNumericDefinition n,UnitCombatState caster,UnitCombatState target)=>n==null||n.Condition==SkillValueCondition.None||
            n.Condition==SkillValueCondition.TargetSleeping&&target.StatusSystem.IsSleeping(target)||
            n.Condition==SkillValueCondition.TargetLowHP&&target.CurrentHP/target.Stats.MaxHP<=n.Threshold||
            n.Condition==SkillValueCondition.TargetHigherMaxHP&&target.Stats.MaxHP>caster.Stats.MaxHP;
    }
}
