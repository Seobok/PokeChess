using System;
using System.Collections.Generic;
using System.Linq;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Match
{
    // Queries must not mutate pool/state. Stock reservation policy is added in WBS 2.6.
    public interface IShopCandidateSource
    {
        IReadOnlyList<PokemonDefinition> GetCandidates(int cost);
    }

    public sealed class CatalogShopCandidateSource : IShopCandidateSource
    {
        private readonly IReadOnlyList<PokemonDefinition>[] groups = new IReadOnlyList<PokemonDefinition>[5];
        public CatalogShopCandidateSource(PokemonCatalog catalog, IEnumerable<string> definitionIds)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (definitionIds == null) throw new ArgumentNullException(nameof(definitionIds));
            var ids = ModelGuard.Ids(definitionIds, nameof(definitionIds));
            var definitions = ids.Select(catalog.Get).ToArray();
            if (definitions.Any(d => d.Cost < 1 || d.Cost > 5))
                throw new ArgumentException("Shop candidates must have Cost 1..5.", nameof(definitionIds));
            for (int i=0;i<5;i++)
                groups[i] = Array.AsReadOnly(definitions.Where(d => d.Cost == i+1)
                    .OrderBy(d => d.Id, StringComparer.Ordinal).ToArray());
        }
        public IReadOnlyList<PokemonDefinition> GetCandidates(int cost)
        {
            if (cost < 1 || cost > 5) throw new ArgumentOutOfRangeException(nameof(cost));
            return groups[cost-1];
        }
    }
}
