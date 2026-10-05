using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Match
{
    public sealed class PoolRules
    {
        private readonly int[] sizes;
        public PoolRules(IEnumerable<int> sizesByCost = null)
        {
            sizes = (sizesByCost ?? new[] { 22, 18, 14, 10, 9 }).ToArray();
            if (sizes.Length != 5 || sizes.Any(n => n < 0))
                throw new ArgumentException("Five nonnegative pool sizes are required.", nameof(sizesByCost));
        }
        public int SizeForCost(int cost)
        {
            if (cost < 1 || cost > 5) throw new ArgumentOutOfRangeException(nameof(cost));
            return sizes[cost - 1];
        }
    }

    // Immutable host-only snapshot; held copies include all players, not shop offers.
    public sealed class PoolStock
    {
        public string DefinitionId { get; }
        public int Cost { get; }
        public int Initial { get; }
        public int Available { get; }
        public int Held => Initial - Available;
        internal PoolStock(string id, int cost, int initial, int available)
        { DefinitionId = id; Cost = cost; Initial = initial; Available = available; }
    }

    internal sealed class PoolHolding
    {
        internal readonly string Origin;
        internal readonly int Copies;
        internal UnitInstance Unit;
        internal PoolHolding(string origin, int copies) { Origin = origin; Copies = copies; }
    }

    public sealed class SharedPokemonPool
    {
        private readonly PokemonCatalog catalog;
        private readonly PokemonDefinition[] definitions;
        private readonly Dictionary<string, int> available = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, PoolHolding> holdings = new Dictionary<string, PoolHolding>(StringComparer.Ordinal);
        public PoolRules Rules { get; }
        internal SharedPokemonPool(PokemonCatalog catalog, IEnumerable<string> shopDefinitionIds, PoolRules rules)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            if (shopDefinitionIds == null) throw new ArgumentNullException(nameof(shopDefinitionIds));
            Rules = rules ?? new PoolRules();
            definitions = ModelGuard.Ids(shopDefinitionIds, nameof(shopDefinitionIds)).Select(catalog.Get)
                .OrderBy(d => d.Id, StringComparer.Ordinal).ToArray();
            if (definitions.Length == 0 || definitions.Any(d => d.Cost < 1 || d.Cost > 5))
                throw new ArgumentException("Pool needs shop definitions with Cost 1..5.");
            foreach (var definition in definitions) available.Add(definition.Id, Rules.SizeForCost(definition.Cost));
            // Validate aggregate int bounds before RNG can use the totals.
            for (int cost = 1; cost <= 5; cost++) TotalAvailable(cost);
        }
        public IReadOnlyList<PoolStock> Stocks => Array.AsReadOnly(definitions.Select(d => GetStock(d.Id)).ToArray());
        public PoolStock GetStock(string definitionId)
        {
            ModelGuard.Id(definitionId, nameof(definitionId));
            if (!available.TryGetValue(definitionId, out var count)) throw new KeyNotFoundException("Unknown pool origin: " + definitionId);
            var definition = catalog.Get(definitionId);
            return new PoolStock(definitionId, definition.Cost, Rules.SizeForCost(definition.Cost), count);
        }
        public int TotalAvailable(int cost)
        {
            if (cost < 1 || cost > 5) throw new ArgumentOutOfRangeException(nameof(cost));
            int total = 0;
            foreach (var definition in definitions)
                if (definition.Cost == cost) total = checked(total + available[definition.Id]);
            return total;
        }
        // A pure interval lookup, also useful for exact weighted-draw tests.
        public PokemonDefinition SelectDefinition(int cost, int roll)
        {
            int total = TotalAvailable(cost);
            if (roll < 0 || roll >= total) throw new ArgumentOutOfRangeException(nameof(roll));
            foreach (var definition in definitions)
            {
                if (definition.Cost != cost) continue;
                int count = available[definition.Id];
                if (roll < count) return definition;
                roll -= count;
            }
            throw new InvalidOperationException("Invalid pool interval.");
        }
        internal PoolHolding ValidateAcquire(UnitInstance source, string origin)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (source.PoolOriginDefinitionId != null || holdings.ContainsKey(source.InstanceId))
                throw new InvalidOperationException("Unit already has pool ownership.");
            var stock = GetStock(origin);
            if (catalog.Get(source.DefinitionId).Cost != stock.Cost)
                throw new ArgumentException("Pool origin must preserve Cost.");
            int copies = source.Rank == UnitRank.One ? 1 : source.Rank == UnitRank.Two ? 3 : 9;
            if (stock.Available < copies) throw new InvalidOperationException("PoolUnavailable: insufficient copies.");
            return new PoolHolding(origin, copies);
        }
        internal void Acquire(PoolHolding holding, UnitInstance owned)
        {
            available[holding.Origin] -= holding.Copies;
            holding.Unit = owned;
            owned.SetPoolOrigin(holding.Origin);
            holdings.Add(owned.InstanceId, holding);
        }
        internal PoolHolding[] ValidateRelease(IEnumerable<UnitInstance> units)
        {
            var result = new List<PoolHolding>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var returned = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var unit in units)
            {
                if (!ids.Add(unit.InstanceId)) throw new InvalidOperationException("Duplicate unit return.");
                bool tracked = holdings.TryGetValue(unit.InstanceId, out var holding);
                if (!tracked && unit.PoolOriginDefinitionId == null) continue;
                if (!tracked || !ReferenceEquals(holding.Unit, unit) || holding.Origin != unit.PoolOriginDefinitionId)
                    throw new InvalidOperationException("Invalid pool ownership.");
                returned.TryGetValue(holding.Origin, out int count);
                returned[holding.Origin] = checked(count + holding.Copies);
                result.Add(holding);
            }
            foreach (var pair in returned)
                if (checked(available[pair.Key] + pair.Value) > GetStock(pair.Key).Initial)
                    throw new InvalidOperationException("Pool return exceeds initial stock.");
            return result.ToArray();
        }
        internal void Release(PoolHolding[] returned)
        {
            foreach (var holding in returned)
            {
                available[holding.Origin] += holding.Copies;
                holdings.Remove(holding.Unit.InstanceId);
                holding.Unit.SetPoolOrigin(null);
            }
        }
        public void AssertConservation(MatchState match)
        {
            if (match == null || !ReferenceEquals(match.Pool, this)) throw new ArgumentException("Pool belongs to another match.");
            var units = match.Players.SelectMany(p => p.Units).ToArray();
            var tracked = units.Where(u => u.PoolOriginDefinitionId != null).ToArray();
            var verified = ValidateRelease(tracked);
            if (verified.Length != holdings.Count) throw new InvalidOperationException("Pool holding is missing from match ownership.");
            foreach (var definition in definitions)
            {
                int held = 0;
                foreach (var holding in verified.Where(h => h.Origin == definition.Id)) held = checked(held + holding.Copies);
                if (checked(available[definition.Id] + held) != Rules.SizeForCost(definition.Cost))
                    throw new InvalidOperationException("Pool conservation failed: " + definition.Id);
            }
        }
    }

    public static class SharedPoolSystem
    {
        // Trusted host setup/import path. Unlike a reward grant, this consumes stock.
        // Rank/evolution game commands remain in their later WBS stages.
        public static UnitInstance RegisterUnit(MatchState match, UnitInstance source, string originDefinitionId)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            if (source == null) throw new ArgumentNullException(nameof(source));
            var pool = match.Pool ?? throw new InvalidOperationException("Match has no shared pool.");
            var player = match.GetPlayer(source.OwnerPlayerId);
            if (player.IsEliminated || player.HP == 0 || match.Phase == MatchPhase.Finished)
                throw new InvalidOperationException("Cannot register for an eliminated player or finished match.");
            var holding = pool.ValidateAcquire(source, originDefinitionId);
            var owned = player.AddUnit(source);
            pool.Acquire(holding, owned);
            return owned;
        }
        // Called by the host after HP reaches zero. Includes temporarily unplaced units.
        public static bool ReleasePlayerHoldings(MatchState match, string playerId)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            var pool = match.Pool ?? throw new InvalidOperationException("Match has no shared pool.");
            var player = match.GetPlayer(playerId);
            if (player.IsEliminated) return false;
            if (player.HP != 0) throw new InvalidOperationException("Only zero-HP players can be eliminated.");
            var units = player.Units.ToArray();
            var returned = pool.ValidateRelease(units);
            long revision = checked(player.Shop.Revision + 1);
            player.ValidatePlacementChanges(units.Length);
            foreach (var unit in units) player.RemoveUnit(unit.InstanceId);
            pool.Release(returned);
            player.Shop.ClearForElimination(revision);
            player.MarkEliminated();
            return true;
        }
    }
}
