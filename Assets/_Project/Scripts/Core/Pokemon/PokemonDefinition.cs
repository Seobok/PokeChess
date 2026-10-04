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

    // SpellPower is a percentage: 50 means +50%, not 0.5.
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

        public PokemonStats(float maxHP, float attack, float spellPower, float attackSpeed,
            float armor, float magicResistance, int attackRange, float moveSpeed,
            float maxEnergy, float startingEnergy, float attackHitTime,
            AttackDeliveryType attackDelivery = AttackDeliveryType.Melee, float projectileSpeed = 6)
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
        public string Id { get; }
        public string DisplayName { get; }
        public int Cost { get; }
        public PokemonStats BaseStats { get; }
        public string RoleId { get; }
        public string SkillId { get; }
        public IReadOnlyList<string> TypeIds { get; }
        public IReadOnlyList<string> TraitIds { get; }
        public IReadOnlyList<EvolutionOption> Evolutions { get; }

        public PokemonDefinition(string id, string displayName, int cost, PokemonStats baseStats,
            string roleId, string skillId, IEnumerable<string> typeIds = null,
            IEnumerable<string> traitIds = null, IEnumerable<EvolutionOption> evolutions = null)
        {
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
                    if (!this.definitions.ContainsKey(option.TargetDefinitionId))
                        throw new ArgumentException("Unknown evolution definition: " + option.TargetDefinitionId);
        }
        public PokemonDefinition Get(string id)
        {
            if (!definitions.TryGetValue(ModelGuard.Id(id, nameof(id)), out var definition))
                throw new KeyNotFoundException("Unknown PokemonDefinition: " + id);
            return definition;
        }
        public UnitInstance CreateUnit(string instanceId, string definitionId, string ownerPlayerId,
            UnitRank rank, int evolutionStage, UnitPlacement placement, IEnumerable<string> itemIds = null)
        {
            Get(definitionId);
            return new UnitInstance(instanceId, definitionId, ownerPlayerId, rank, evolutionStage, placement, itemIds);
        }
    }
}
