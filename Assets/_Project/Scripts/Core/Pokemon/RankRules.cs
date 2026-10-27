using System;
namespace PokeChess.Core.Pokemon
{
    public sealed class RankRules
    {
        public static RankRules Default { get; } = new RankRules();
        private readonly float[] multipliers;
        public RankRules(float one=1f, float two=1.7f, float three=2.7f)
        {
            multipliers=new[]{ModelGuard.Number(one,nameof(one),float.Epsilon),ModelGuard.Number(two,nameof(two),float.Epsilon),ModelGuard.Number(three,nameof(three),float.Epsilon)};
        }
        public float Multiplier(UnitRank rank)
        {
            if(rank<UnitRank.One || rank>UnitRank.Three) throw new ArgumentOutOfRangeException(nameof(rank));
            return multipliers[(int)rank-1];
        }
        public PokemonStats Apply(PokemonDefinition definition,UnitRank rank)
        {
            if(definition==null)throw new ArgumentNullException(nameof(definition));
            Multiplier(rank);
            if(definition.RankHP==null)return Apply(definition.BaseStats,rank);
            var s=definition.BaseStats;int i=(int)rank-1;
            return new PokemonStats(definition.RankHP[i],definition.RankAttack[i],s.SpellPower,s.AttackSpeed,s.Armor,s.MagicResistance,
                s.AttackRange,s.MoveSpeed,s.MaxEnergy,s.StartingEnergy,s.AttackHitTime,s.AttackDelivery,s.ProjectileSpeed,s.CritChance,s.CritMultiplier,s.EnergyLockSeconds);
        }
        public PokemonStats Apply(PokemonStats stats,UnitRank rank)
        {
            if(stats==null) throw new ArgumentNullException(nameof(stats));
            float multiplier=Multiplier(rank);
            if(multiplier==1) return stats;
            return new PokemonStats(stats.MaxHP*multiplier,stats.Attack*multiplier,stats.SpellPower,stats.AttackSpeed,
                stats.Armor,stats.MagicResistance,stats.AttackRange,stats.MoveSpeed,stats.MaxEnergy,stats.StartingEnergy,
                stats.AttackHitTime,stats.AttackDelivery,stats.ProjectileSpeed,stats.CritChance,stats.CritMultiplier,stats.EnergyLockSeconds);
        }
    }
}
