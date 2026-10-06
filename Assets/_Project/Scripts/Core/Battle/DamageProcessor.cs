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
            if(source==null||target==null||source.TeamId==target.TeamId||!target.IsTargetable)
                throw new InvalidOperationException("Invalid damage participants.");
            bool canCrit=request.CanCrit&&request.Type!=DamageType.True&&
                ((double)request.RawDamage+request.BonusDamage+source.EffectiveDamageModifiers.BonusAttackDamage)>0;
            float bonus=request.BonusDamage+(request.CanCrit?source.EffectiveDamageModifiers.BonusAttackDamage:0);
            // Validate arithmetic before consuming RNG or changing state.
            DamageCalculator.CalculateDetailed(request.RawDamage,bonus,request.Type,target.EffectiveStats.Armor,
                target.EffectiveStats.MagicResistance,canCrit,source.Stats.CritMultiplier,
                source.EffectiveDamageModifiers.Amplification,target.EffectiveDamageModifiers.Reduction);
            bool critical=canCrit&&(source.Stats.CritChance>=1 ||
                (source.Stats.CritChance>0 &&
                 (battle.RngStreams.Critical.NextUInt64()>>11)*(1.0/9007199254740992.0)<source.Stats.CritChance));
            var calculation=DamageCalculator.CalculateDetailed(request.RawDamage,bonus,request.Type,target.EffectiveStats.Armor,
                target.EffectiveStats.MagicResistance,critical,source.Stats.CritMultiplier,
                source.EffectiveDamageModifiers.Amplification,target.EffectiveDamageModifiers.Reduction);
            float post=calculation.FinalDamage;
            float shield=Math.Min(target.CurrentShield,post);
            float hpDamage=Math.Min(target.CurrentHP,Math.Max(0,post-shield));
            bool killed=target.CurrentHP>0 && hpDamage>=target.CurrentHP;
            target.SetShield(target.CurrentShield-shield);
            target.SetVitals(target.CurrentHP-hpDamage,target.CurrentEnergy);
            if(target.CurrentHP<=0)target.DeathCause=DeathReason.Damage;
            if(!target.IsAlive)battle.TryRemoveUnit(target.UnitInstanceId);
            var result=new DamageResult(battle.CurrentTick,request,calculation.AfterAmplification,post,shield,hpDamage,
                target.CurrentHP,target.CurrentShield,killed,calculation);
            battle.RecordDamage(result);
            battle.Energy.OnDamage(result);
            return result;
        }
    }
}
