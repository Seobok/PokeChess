using System;
using System.Linq;

namespace PokeChess.Core.Battle
{
    // Aggregated fractions: 0.2 means 20%. Status lifetimes and stacking are owned by later systems.
    public sealed class DamageModifiers
    {
        public float BonusAttackDamage { get; }
        public float Amplification { get; }
        public float Reduction { get; }
        public DamageModifiers(float bonusAttackDamage=0,float amplification=0,float reduction=0)
        {
            DamageCalculator.Validate(bonusAttackDamage,true);
            DamageCalculator.Validate(amplification,true);
            DamageCalculator.Validate(reduction,true);
            BonusAttackDamage=bonusAttackDamage; Amplification=amplification;
            Reduction=Math.Min(1,reduction);
        }
    }
    public readonly struct DamageCalculation
    {
        public float AfterBonus { get; }
        public bool IsCritical { get; }
        public float CriticalMultiplier { get; }
        public float AfterCritical { get; }
        public float AfterAmplification { get; }
        public float AfterResistance { get; }
        public float AfterReduction { get; }
        public float FinalDamage { get; }
        public DamageCalculation(float bonus,bool critical,float multiplier,float crit,float amp,float resistance,float reduction,float final)
        { AfterBonus=bonus; IsCritical=critical; CriticalMultiplier=multiplier; AfterCritical=crit;
          AfterAmplification=amp; AfterResistance=resistance; AfterReduction=reduction; FinalDamage=final; }
    }
    public static class DamageCalculator
    {
        public static float Calculate(float raw, DamageType type, float armor, float magicResistance) =>
            CalculateDetailed(raw,0,type,armor,magicResistance,false,1,0,0).FinalDamage;
        public static DamageCalculation CalculateDetailed(float raw,float bonus,DamageType type,float armor,
            float magicResistance,bool critical,float critMultiplier,float amplification,float reduction)
        {
            Validate(raw,true); Validate(bonus,true); Validate(armor,false); Validate(magicResistance,false);
            Validate(critMultiplier,true); Validate(amplification,true); Validate(reduction,true);
            if(critMultiplier<1||!Enum.IsDefined(typeof(DamageType),type))throw new ArgumentOutOfRangeException();
            bool isCritical=critical&&type!=DamageType.True;
            double added=(double)raw+bonus;
            double crit=added*(isCritical?critMultiplier:1);
            double amp=type==DamageType.True?crit:crit*(1+(double)amplification);
            double resistance=type==DamageType.Physical?armor:magicResistance;
            double multiplier=type==DamageType.True?1:resistance>=0?1/(1+resistance*.01):2-1/(1-resistance*.01);
            double resisted=amp*multiplier;
            double reduced=type==DamageType.True?resisted:resisted*(1-Math.Min(1,reduction));
            double minimum=type!=DamageType.True&&added>0?Math.Max(1,reduced):reduced;
            double rounded=Math.Round(minimum,0,MidpointRounding.AwayFromZero);
            return new DamageCalculation(Store(added),isCritical,isCritical?critMultiplier:1,
                Store(crit),Store(amp),Store(resisted),Store(reduced),Store(rounded));
        }
        private static float Store(double value)
        {
            if(double.IsNaN(value)||double.IsInfinity(value)||value>float.MaxValue)
                throw new OverflowException("Damage exceeds model capacity.");
            return (float)value;
        }
        internal static void Validate(float value,bool nonnegative)
        {
            if(float.IsNaN(value)||float.IsInfinity(value)||(nonnegative&&value<0))
                throw new ArgumentOutOfRangeException(nameof(value));
        }
    }
    public sealed class DamageProcessor : IDamageProcessor
    {
        public DamageResult Apply(BattleState battle,DamageRequest request)
        {
            if(battle==null)throw new ArgumentNullException(nameof(battle));
            var source=battle.Units.SingleOrDefault(u=>u.UnitInstanceId==request.SourceId);
            var target=battle.Units.SingleOrDefault(u=>u.UnitInstanceId==request.TargetId);
            if(source==null||target==null||source.TeamId==target.TeamId||
                (request.Origin==DamageOrigin.Reflection||request.Origin==DamageOrigin.Debuff||request.Origin==DamageOrigin.FreezeBreak?target.CurrentHP<=0||!target.IsOnBoard:!target.IsTargetable))
                throw new InvalidOperationException("Invalid damage participants.");
            var numeric=request.SkillPolicy?.Numeric;
            float reflection=request.Origin==DamageOrigin.Direct?battle.StatusEffects.Reflection(target):0;
            if(request.IsBasicAttack) {
                // Blind attacks still consume the attack action, but cannot damage or trigger hit effects.
                if(battle.StatusEffects.HasBehavior(source,StatusBehavior.Blind)) {
                    var blocked=new DamageResult(battle.CurrentTick,request,0,0,0,0,target.CurrentHP,target.CurrentShield,false);
                    battle.RecordDamage(blocked);return blocked;
                }
                float chance=Math.Max(0,Math.Min(1,source.EffectiveStats.Accuracy*(1-target.EffectiveStats.Evasion)));
                if(chance<=0||chance<1&&(battle.RngStreams.Hit.NextUInt64()>>11)*(1.0/9007199254740992.0)>=chance) {
                    var miss=new DamageResult(battle.CurrentTick,request,0,0,0,0,target.CurrentHP,target.CurrentShield,false,missed:true);
                    battle.RecordDamage(miss);return miss;
                }
            }
            if(numeric!=null&&numeric.Chance<1&&(numeric.Chance==0||(battle.RngStreams.Skill.NextUInt64()>>11)*(1.0/9007199254740992.0)>=numeric.Chance)) {
                var miss=new DamageResult(battle.CurrentTick,request,0,0,0,0,target.CurrentHP,target.CurrentShield,false,missed:true);battle.RecordDamage(miss);return miss;
            }
            if(numeric?.ExecuteThreshold>0&&target.CurrentHP/target.Stats.MaxHP<=numeric.ExecuteThreshold) {
                float before=target.CurrentHP;target.SetVitals(0,target.CurrentEnergy);target.DeathCause=DeathReason.Damage;battle.TryRemoveUnit(target.UnitInstanceId);
                var executed=new DamageResult(battle.CurrentTick,request,before,before,0,before,0,target.CurrentShield,true);
                battle.RecordDamage(executed);battle.SkillProcs.OnDamage(executed);
                if(numeric.Lifesteal>0)battle.Healing.Apply(source,before*numeric.Lifesteal);
                return executed;
            }
            float rawValue=request.SkillPolicy?.Resolve(request.RawDamage,target)??request.RawDamage;
            if(numeric?.ExecuteThreshold>0&&target.IsBoss&&target.CurrentHP/target.Stats.MaxHP<=numeric.ExecuteThreshold)rawValue+=numeric.BossBonus;
            if(numeric?.DamageCap!=null)rawValue=Math.Min(rawValue,numeric.DamageCap.Value);
            float synergyBonus = request.IsBasicAttack ? battle.Synergies.AttackBonus(source) : 0;
            bool guaranteed=request.IsBasicAttack&&battle.StatusEffects.HasBehavior(source,StatusBehavior.GuaranteedCritical);
            bool canCrit=(request.CanCrit||guaranteed)&&request.Type!=DamageType.True&&
                ((double)rawValue+request.BonusDamage+source.EffectiveDamageModifiers.BonusAttackDamage)>0;
            float bonus=request.BonusDamage+(request.CanCrit?source.EffectiveDamageModifiers.BonusAttackDamage:0)+synergyBonus;
            float raw=rawValue*(request.SourceSnapshot?.OutgoingMultiplier??battle.Items.Outgoing(source,request));
            if(request.Type==DamageType.True&&request.SkillPolicy!=null)raw=rawValue;
            float amplification=request.SourceSnapshot?.Amplification??source.EffectiveDamageModifiers.Amplification;
            float penetration=Math.Min(1,(request.SourceSnapshot?.ArmorPenetration??source.EffectiveStats.ArmorPenetration)+(numeric?.ArmorPenetration??0));
            float armor=target.EffectiveStats.Armor;if(armor>0)armor*=1-penetration;
            float critChance=Math.Min(1,(request.SourceSnapshot?.CritChance??source.EffectiveStats.CritChance)+(numeric?.CritBonus??0));
            float mr=target.EffectiveStats.MagicResistance;if(mr>0)mr*=1-(numeric?.MagicPenetration??0);
            if(!request.IsBasicAttack&&(request.Origin==DamageOrigin.Direct||request.Origin==DamageOrigin.Periodic)&&request.Type!=DamageType.True)raw*=1+battle.StatusEffects.SkillVulnerability(target);
            float reduction=battle.Items.IncomingReduction(target,request,target.EffectiveDamageModifiers.Reduction);
            // Validate arithmetic before consuming RNG or changing state.
            DamageCalculator.CalculateDetailed(raw,bonus,request.Type,armor,
                mr,canCrit,source.Stats.CritMultiplier,
                amplification,reduction);
            if(guaranteed)battle.StatusEffects.ConsumeCritical(source);
            bool critical=canCrit&&(guaranteed||critChance>=1 ||
                (critChance>0 &&
                 (battle.RngStreams.Critical.NextUInt64()>>11)*(1.0/9007199254740992.0)<critChance));
            var calculation=DamageCalculator.CalculateDetailed(raw,bonus,request.Type,armor,
                mr,critical,source.Stats.CritMultiplier,
                amplification,reduction);
            float post=calculation.FinalDamage;
            float shield=Math.Min(target.CurrentShield,post);
            float hpDamage=Math.Min(target.CurrentHP,Math.Max(0,post-shield));
            hpDamage=battle.Items.AdjustHPDamage(target,hpDamage);
            bool killed=target.CurrentHP>0 && hpDamage>=target.CurrentHP;
            battle.Shields.Absorb(target,shield);
            target.SetVitals(target.CurrentHP-hpDamage,target.CurrentEnergy);
            if(target.CurrentHP<=0)target.DeathCause=DeathReason.Damage;
            if(!target.IsAlive)battle.TryRemoveUnit(target.UnitInstanceId);
            var result=new DamageResult(battle.CurrentTick,request,calculation.AfterAmplification,post,shield,hpDamage,
                target.CurrentHP,target.CurrentShield,killed,calculation);
            battle.RecordDamage(result);
            battle.Energy.OnDamage(result);
            if(request.Origin!=DamageOrigin.Reflection){
                if(request.IsBasicAttack)battle.Synergies.OnBasicAttack(source, synergyBonus);
                battle.Synergies.OnDamage(target, shield);
            }
            battle.StatusEffects.OnDamage(result);
            if(numeric?.Lifesteal>0&&request.Origin==DamageOrigin.Direct)battle.Healing.Apply(source,hpDamage*numeric.Lifesteal);
            battle.SkillProcs.OnDamage(result);
            battle.Items.AfterDamage(source,target,result,this);
            if(reflection>0&&source.CurrentHP>0&&source.IsOnBoard&&post>0)Apply(battle,new DamageRequest(target.UnitInstanceId,source.UnitInstanceId,calculation.AfterAmplification*reflection,DamageType.True,origin:DamageOrigin.Reflection));
            return result;
        }
    }
}
