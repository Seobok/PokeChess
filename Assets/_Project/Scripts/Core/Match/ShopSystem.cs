using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using PokeChess.Core.Pokemon;
using PokeChess.Core.Random;

namespace PokeChess.Core.Match
{
    public sealed class ShopSystem
    {
        private readonly IShopCandidateSource candidates;
        public ShopRules Rules { get; }
        // Pool-only mode: use MatchStateFactory.CreateWithPool.
        public ShopSystem() : this(null, true) { }
        private ShopSystem(ShopRules rules, bool poolOnly) { Rules = rules ?? new ShopRules(); }
        public static ShopSystem ForSharedPool(ShopRules rules = null) => new ShopSystem(rules, true);
        public ShopSystem(IShopCandidateSource candidates, ShopRules rules = null)
        {
            this.candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
            Rules = rules ?? new ShopRules();
        }
        private PlayerState PreparationPlayer(MatchState match, string playerId)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            var player = match.GetPlayer(playerId);
            if (player.IsEliminated || player.HP == 0) throw new InvalidOperationException("Player is eliminated.");
            if (match.Phase != MatchPhase.Preparation)
                throw new InvalidOperationException("Shop commands require Preparation.");
            return player;
        }
        private ReadOnlyCollection<ShopSlot> Generate(MatchState match, PlayerState player, out ulong state, out long drawCount)
        {
            if (player.Rules.MaxLevel != Rules.MaxLevel)
                throw new ArgumentException("Match maximum level must match the shop probability table.");
            if (match.Pool == null && candidates == null) throw new InvalidOperationException("Match has no shared pool.");
            var probabilities = Rules.ForLevel(player.Level);
            var lists = new PokemonDefinition[5][];
            for (int cost=1;cost<=5;cost++)
            {
                if (probabilities[cost-1] == 0 || match.Pool != null) continue;
                var input = candidates.GetCandidates(cost);
                if (input == null) throw new ArgumentException("Null candidate list.");
                var copy = input.ToArray();
                if (copy.Length == 0 || copy.Any(d => d == null || d.Cost != cost)
                    || copy.Select(d => d.Id).Distinct(StringComparer.Ordinal).Count() != copy.Length)
                    throw new ArgumentException("Invalid, empty or duplicate candidates for a positive-probability cost.");
                lists[cost-1] = copy.OrderBy(d => d.Id, StringComparer.Ordinal).ToArray();
            }
            var random = new DeterministicRandom(player.Shop.RandomState);
            var slots = new ShopSlot[ShopRules.SlotCount];
            for (int i=0;i<slots.Length;i++)
            {
                int cost = Rules.SelectCost(player.Level, random.NextInt(100));
                PokemonDefinition definition;
                if (match.Pool != null)
                {
                    int total = match.Pool.TotalAvailable(cost);
                    if (total == 0) { slots[i] = new ShopSlot(i); continue; }
                    definition = match.Pool.SelectDefinition(cost, random.NextInt(total));
                }
                else
                {
                    var list = lists[cost-1];
                    definition = list[random.NextInt(list.Length)];
                }
                slots[i] = new ShopSlot(i, definition.Id, cost);
            }
            state = random.State;
            drawCount = checked(player.Shop.RandomDrawCount + random.DrawCount);
            return Array.AsReadOnly(slots);
        }
        // Returns false when locked; even a skipped refresh completes the round once.
        public bool RefreshForRound(MatchState match, string playerId)
        {
            var player = PreparationPlayer(match, playerId);
            var shop = player.Shop;
            if ((long)match.RoundNumber != (long)shop.LastRefreshRound + 1)
                throw new InvalidOperationException("Automatic shop refresh must run once per round, in order.");
            if (shop.IsInitialized && shop.IsLocked)
            {
                shop.RecordLockedRefresh(match.RoundNumber);
                return false;
            }
            var revision = checked(player.Shop.Revision + 1);
            var slots = Generate(match, player, out var state, out var count);
            shop.Apply(slots, state, count, match.RoundNumber, revision);
            return true;
        }
        public IReadOnlyList<ShopSlot> Reroll(MatchState match, string playerId)
        {
            var player = PreparationPlayer(match, playerId);
            if (!player.Shop.IsInitialized || player.Shop.LastRefreshRound != match.RoundNumber)
                throw new InvalidOperationException("Automatic shop refresh must complete before shop commands.");
            if (player.Gold < Rules.RerollGoldCost) throw new InvalidOperationException("Insufficient gold.");
            var revision = checked(player.Shop.Revision + 1);
            var slots = Generate(match, player, out var state, out var count);
            player.SetProgress(player.Gold - Rules.RerollGoldCost, player.XP, player.Level);
            player.Shop.Apply(slots, state, count, null, revision);
            return slots;
        }
        public void SetLocked(MatchState match, string playerId, bool locked)
        {
            var player = PreparationPlayer(match, playerId);
            if (!player.Shop.IsInitialized || player.Shop.LastRefreshRound != match.RoundNumber)
                throw new InvalidOperationException("Automatic shop refresh must complete before shop commands.");
            player.Shop.SetLocked(locked);
        }
    }
}
