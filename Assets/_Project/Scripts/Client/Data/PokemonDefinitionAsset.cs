using System;
using System.Linq;
using PokeChess.Core.Pokemon;
using UnityEngine;

namespace PokeChess.Client.Data
{
    [Serializable]
    public sealed class EvolutionOptionData
    {
        public string targetDefinitionId;
        public int evolutionPointCost;
    }

    [CreateAssetMenu(menuName = "PokeChess/Data/Pokemon Definition")]
    public sealed class PokemonDefinitionAsset : ScriptableObject
    {
        [SerializeField] private string definitionId;
        [SerializeField] private string displayName;
        [SerializeField] private int cost = 1;
        [SerializeField] private string roleId = "test";
        [SerializeField] private string skillId = "test-skill";
        [SerializeField] private string[] typeIds = Array.Empty<string>();
        [SerializeField] private string[] traitIds = Array.Empty<string>();
        [SerializeField] private EvolutionOptionData[] evolutions = Array.Empty<EvolutionOptionData>();
        [SerializeField] private float maxHP = 100;
        [SerializeField] private float attack = 10;
        [SerializeField] private float spellPower;
        [SerializeField] private float attackSpeed = 1;
        [SerializeField] private float armor;
        [SerializeField] private float magicResistance;
        [SerializeField] private int attackRange = 1;
        [SerializeField] private float moveSpeed = 2;
        [SerializeField] private float maxEnergy = 100;
        [SerializeField] private float startingEnergy;
        [SerializeField] private float attackHitTime = 0.25f;
        [SerializeField] private AttackDeliveryType attackDelivery;
        [SerializeField] private float projectileSpeed = 6;
        [SerializeField, Range(0,1)] private float critChance;
        [SerializeField] private float critMultiplier = 1.5f;
        [SerializeField] private float energyLockSeconds = 1;
        [SerializeField, TextArea] private string designNotes;

        public PokemonDefinition ToDefinition()
        {
            if (evolutions != null && evolutions.Any(e => e == null))
                throw new ArgumentException("Null evolution entry in " + name);
            return new PokemonDefinition(definitionId, displayName, cost,
                new PokemonStats(maxHP, attack, spellPower, attackSpeed, armor, magicResistance,
                    attackRange, moveSpeed, maxEnergy, startingEnergy, attackHitTime, attackDelivery, projectileSpeed, critChance, critMultiplier, energyLockSeconds),
                roleId, skillId, typeIds, traitIds,
                (evolutions ?? Array.Empty<EvolutionOptionData>()).Select(e => new EvolutionOption(e.targetDefinitionId, e.evolutionPointCost)));
        }
    }
}
