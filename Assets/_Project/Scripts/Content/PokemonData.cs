using System;
using System.Linq;
using UnityEngine;
using PokeChess.Core.Pokemon;

namespace PokeChess.Content
{
    [Serializable]
    public sealed class PokemonStatsData
    {
        public float MaxHP=500, Attack=50, SpellPower, AttackSpeed=.5f, Armor=20, MagicResistance=20;
        public int AttackRange=1;
        public float MoveSpeed=2, MaxEnergy=100, StartingEnergy, AttackHitTime=.2f;
        public AttackDeliveryType AttackDelivery;
        public float ProjectileSpeed=8, CritChance=.25f, CritMultiplier=1.3f, EnergyLockSeconds=1;
        public PokemonStats ToCore()=>new PokemonStats(MaxHP,Attack,SpellPower,AttackSpeed,Armor,MagicResistance,
            AttackRange,MoveSpeed,MaxEnergy,StartingEnergy,AttackHitTime,AttackDelivery,ProjectileSpeed,CritChance,CritMultiplier,EnergyLockSeconds);
    }
    [Serializable]
    public sealed class EvolutionData
    {
        public PokemonData Target;
        public int PointCost=1;
    }
    [CreateAssetMenu(menuName="PokeChess/Content/Pokemon")]
    public sealed class PokemonData : ScriptableObject
    {
        public string Id, DisplayName, LocalizedName, RoleId;
        public int Cost=1;
        public PokemonStatsData Stats=new PokemonStatsData();
        public string[] TypeIds=Array.Empty<string>(), TraitIds=Array.Empty<string>();
        public SkillData Skill;
        public PokemonVisualData Visual;
        public EvolutionData[] Evolutions=Array.Empty<EvolutionData>();
        public PokemonDefinition ToCore()=>new PokemonDefinition(Id,DisplayName,Cost,Stats.ToCore(),RoleId,Skill.Id,
            TypeIds,TraitIds,Evolutions.Select(e=>new EvolutionOption(e.Target.Id,e.PointCost)));
    }
}
