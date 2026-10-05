using System;
using System.Linq;

namespace PokeChess.Core.Battle
{
    public static class DamageCalculator
    {
        public static float Calculate(float raw, DamageType type, float armor, float magicResistance)
        {
            Validate(raw,true); Validate(armor,false); Validate(magicResistance,false);
            if(!Enum.IsDefined(typeof(DamageType),type))throw new ArgumentOutOfRangeException(nameof(type));
            if(raw==0 || type==DamageType.True)return raw;
            double resistance=type==DamageType.Physical?armor:magicResistance;
            double multiplier=resistance>=0?1/(1+resistance*.01):2-1/(1-resistance*.01);
            double value=Math.Max(1,raw*multiplier);
            if(value>float.MaxValue)throw new OverflowException("Damage exceeds model capacity.");
            return (float)value;
        }
        private static void Validate(float value,bool nonnegative)
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
            float post=DamageCalculator.Calculate(request.RawDamage,request.Type,target.Stats.Armor,target.Stats.MagicResistance);
            float shield=Math.Min(target.CurrentShield,post);
            float hpDamage=Math.Min(target.CurrentHP,Math.Max(0,post-shield));
            target.SetShield(target.CurrentShield-shield);
            target.SetVitals(target.CurrentHP-hpDamage,target.CurrentEnergy);
            if(!target.IsAlive)battle.TryRemoveUnit(target.UnitInstanceId);
            var result=new DamageResult(battle.CurrentTick,request,request.RawDamage,post,shield,hpDamage,
                target.CurrentHP,target.CurrentShield,!target.IsAlive);
            battle.RecordDamage(result);
            return result;
        }
    }
}
