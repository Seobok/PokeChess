using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace PokeChess.Core.Pokemon
{
    internal static class ModelGuard
    {
        public static string Id(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A nonempty identifier is required.", name);
            return value;
        }
        public static float Number(float value, string name, float minimum = 0)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < minimum)
                throw new ArgumentOutOfRangeException(name);
            return value;
        }
        public static ReadOnlyCollection<string> Ids(IEnumerable<string> values, string name)
        {
            var copy = (values ?? Array.Empty<string>()).Select(v => Id(v, name)).ToArray();
            if (copy.Distinct().Count() != copy.Length) throw new ArgumentException("Duplicate identifiers.", name);
            return Array.AsReadOnly(copy);
        }
    }

    // SpellPower uses percentage points. Total-power skills have a baseline of 100; legacy skills use bonus power.
    // Speeds use attacks/second and hexes/second. Hit time uses seconds.
    public enum AttackDeliveryType { Melee, Projectile }

    public sealed class PokemonStats
    {
        public float MaxHP { get; }
        public float Attack { get; }
        public float SpellPower { get; }
        public float AttackSpeed { get; }
        public float Armor { get; }
        public float MagicResistance { get; }
        public int AttackRange { get; }
        public float MoveSpeed { get; }
        public float MaxEnergy { get; }
        public float StartingEnergy { get; }
        public float AttackHitTime { get; }
        public AttackDeliveryType AttackDelivery { get; }
        public float ProjectileSpeed { get; }
        public float CritChance { get; }
        public float CritMultiplier { get; }
        public float EnergyLockSeconds { get; }

        public PokemonStats(float maxHP, float attack, float spellPower, float attackSpeed,
            float armor, float magicResistance, int attackRange, float moveSpeed,
            float maxEnergy, float startingEnergy, float attackHitTime,
            AttackDeliveryType attackDelivery = AttackDeliveryType.Melee, float projectileSpeed = 6, float critChance = 0, float critMultiplier = 1.5f, float energyLockSeconds = 1)
        {
            MaxHP = ModelGuard.Number(maxHP, nameof(maxHP), float.Epsilon);
            Attack = ModelGuard.Number(attack, nameof(attack));
            SpellPower = ModelGuard.Number(spellPower, nameof(spellPower));
            AttackSpeed = ModelGuard.Number(attackSpeed, nameof(attackSpeed), float.Epsilon);
            Armor = ModelGuard.Number(armor, nameof(armor), float.MinValue);
            MagicResistance = ModelGuard.Number(magicResistance, nameof(magicResistance), float.MinValue);
            if (attackRange < 1) throw new ArgumentOutOfRangeException(nameof(attackRange));
            AttackRange = attackRange;
            MoveSpeed = ModelGuard.Number(moveSpeed, nameof(moveSpeed), float.Epsilon);
            MaxEnergy = ModelGuard.Number(maxEnergy, nameof(maxEnergy));
            StartingEnergy = ModelGuard.Number(startingEnergy, nameof(startingEnergy));
            if (startingEnergy > maxEnergy) throw new ArgumentOutOfRangeException(nameof(startingEnergy));
            AttackHitTime = ModelGuard.Number(attackHitTime, nameof(attackHitTime));
            if(!Enum.IsDefined(typeof(AttackDeliveryType),attackDelivery))throw new ArgumentOutOfRangeException(nameof(attackDelivery));
            AttackDelivery = attackDelivery;
            ProjectileSpeed = ModelGuard.Number(projectileSpeed,nameof(projectileSpeed),float.Epsilon);
            CritChance = ModelGuard.Number(critChance,nameof(critChance));
            if(critChance>1)throw new ArgumentOutOfRangeException(nameof(critChance));
            CritMultiplier = ModelGuard.Number(critMultiplier,nameof(critMultiplier),1);
            EnergyLockSeconds = ModelGuard.Number(energyLockSeconds,nameof(energyLockSeconds));
        }
    }

    public sealed class EvolutionOption
    {
        public string TargetDefinitionId { get; }
        public int EvolutionPointCost { get; }
        public EvolutionOption(string targetDefinitionId, int evolutionPointCost)
        {
            TargetDefinitionId = ModelGuard.Id(targetDefinitionId, nameof(targetDefinitionId));
            if (evolutionPointCost < 0) throw new ArgumentOutOfRangeException(nameof(evolutionPointCost));
            EvolutionPointCost = evolutionPointCost;
        }
    }

    public sealed class PokemonDefinition
    {
        public bool IsBoss { get; }
        public string Id { get; }
        public string DisplayName { get; }
        public int Cost { get; }
        public PokemonStats BaseStats { get; }
        public IReadOnlyList<float> RankHP { get; }
        public IReadOnlyList<float> RankAttack { get; }
        public string RoleId { get; }
        public string SkillId { get; }
        public IReadOnlyList<string> TypeIds { get; }
        public IReadOnlyList<string> TraitIds { get; }
        public IReadOnlyList<EvolutionOption> Evolutions { get; }

        private static IReadOnlyList<float> RankValues(IEnumerable<float> values,string name,float minimum)
        {
            if(values==null)return null;var copy=values.ToArray();
            if(copy.Length!=3)throw new ArgumentException("Exactly three rank stats are required.",name);
            foreach(var value in copy)ModelGuard.Number(value,name,minimum);return Array.AsReadOnly(copy);
        }
        public PokemonDefinition(string id, string displayName, int cost, PokemonStats baseStats,
            string roleId, string skillId, IEnumerable<string> typeIds = null,
            IEnumerable<string> traitIds = null, IEnumerable<EvolutionOption> evolutions = null,bool isBoss=false,IEnumerable<float> rankHP=null,IEnumerable<float> rankAttack=null)
        {
            RankHP=RankValues(rankHP,nameof(rankHP),float.Epsilon);RankAttack=RankValues(rankAttack,nameof(rankAttack),0);
            if((RankHP==null)!=(RankAttack==null))throw new ArgumentException("Both rank stat arrays are required.");
            if(RankHP!=null&&(RankHP[0]!=baseStats?.MaxHP||RankAttack[0]!=baseStats?.Attack))throw new ArgumentException("Rank one must match base stats.");
            IsBoss=isBoss;
            Id = ModelGuard.Id(id, nameof(id));
            DisplayName = ModelGuard.Id(displayName, nameof(displayName));
            if (cost < 0) throw new ArgumentOutOfRangeException(nameof(cost));
            Cost = cost;
            BaseStats = baseStats ?? throw new ArgumentNullException(nameof(baseStats));
            RoleId = ModelGuard.Id(roleId, nameof(roleId));
            SkillId = ModelGuard.Id(skillId, nameof(skillId));
            TypeIds = ModelGuard.Ids(typeIds, nameof(typeIds));
            TraitIds = ModelGuard.Ids(traitIds, nameof(traitIds));
            var copy = (evolutions ?? Array.Empty<EvolutionOption>()).ToArray();
            if (copy.Any(e => e == null) || copy.Select(e => e.TargetDefinitionId).Distinct().Count() != copy.Length)
                throw new ArgumentException("Invalid evolution options.", nameof(evolutions));
            Evolutions = Array.AsReadOnly(copy);
        }
    }

    public sealed class PokemonCatalog
    {
        private readonly Dictionary<string, PokemonDefinition> definitions;
        public PokemonCatalog(IEnumerable<PokemonDefinition> definitions)
        {
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));
            this.definitions = new Dictionary<string, PokemonDefinition>(StringComparer.Ordinal);
            foreach (var definition in definitions)
            {
                if (definition == null || this.definitions.ContainsKey(definition.Id))
                    throw new ArgumentException("Null or duplicate definition.", nameof(definitions));
                this.definitions.Add(definition.Id, definition);
            }
            foreach (var definition in this.definitions.Values)
                foreach (var option in definition.Evolutions)
                {
                    if (!this.definitions.ContainsKey(option.TargetDefinitionId))
                        throw new ArgumentException("Unknown evolution definition: " + option.TargetDefinitionId);
                    if (this.definitions[option.TargetDefinitionId].Cost != definition.Cost)
                        throw new ArgumentException("Evolution must preserve Cost.");
                }
        }
        public PokemonDefinition Get(string id)
        {
            if (!definitions.TryGetValue(ModelGuard.Id(id, nameof(id)), out var definition))
                throw new KeyNotFoundException("Unknown PokemonDefinition: " + id);
            return definition;
        }
        public bool TryGet(string id,out PokemonDefinition definition) => definitions.TryGetValue(ModelGuard.Id(id,nameof(id)),out definition);
        // An authored, single-parent lineage. Reject cycles and converging paths rather than guessing a family.
        public string EvolutionRoot(string id, out int stage)
        {
            Get(id); stage = 0;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (visited.Add(id))
            {
                var parents = definitions.Values.Where(d => d.Evolutions.Any(e => e.TargetDefinitionId == id)).ToArray();
                if (parents.Length == 0) return id;
                if (parents.Length != 1) throw new InvalidOperationException("Ambiguous evolution lineage.");
                id = parents[0].Id; stage = checked(stage + 1);
            }
            throw new InvalidOperationException("Cyclic evolution lineage.");
        }
        public UnitInstance CreateUnit(string instanceId, string definitionId, string ownerPlayerId,
            UnitRank rank, int evolutionStage, UnitPlacement placement, IEnumerable<string> itemIds = null)
        {
            Get(definitionId);
            return new UnitInstance(instanceId, definitionId, ownerPlayerId, rank, evolutionStage, placement, itemIds);
        }
    }
}
