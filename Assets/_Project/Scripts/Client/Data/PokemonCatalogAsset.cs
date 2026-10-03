using System;
using System.Linq;
using PokeChess.Core.Pokemon;
using UnityEngine;

namespace PokeChess.Client.Data
{
    [CreateAssetMenu(menuName = "PokeChess/Data/Pokemon Catalog")]
    public sealed class PokemonCatalogAsset : ScriptableObject
    {
        [SerializeField] private PokemonDefinitionAsset[] definitions = Array.Empty<PokemonDefinitionAsset>();
        public PokemonCatalog ToCatalog()
        {
            if (definitions == null || definitions.Any(d => d == null))
                throw new ArgumentException("Catalog contains missing definition assets.");
            return new PokemonCatalog(definitions.Select(d => d.ToDefinition()));
        }
    }
}
