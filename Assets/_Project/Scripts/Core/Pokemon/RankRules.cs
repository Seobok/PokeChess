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
