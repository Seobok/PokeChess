using System;
using System.Linq;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Match
{
    // Host commands execute serially. Validate before changing owned state.
    public sealed class ShopTransactionSystem
    {
        private readonly PokemonCatalog catalog;
        public ShopTransactionSystem(PokemonCatalog catalog)
        { this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog)); }
        private static PlayerState Player(MatchState match, string playerId)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            var player = match.GetPlayer(playerId);
            if (player.IsEliminated || player.HP == 0) throw new InvalidOperationException("Player is eliminated.");
            if (match.Phase != MatchPhase.Preparation)
                throw new InvalidOperationException("Trades require Preparation.");
            return player;
        }
        public UnitInstance Buy(MatchState match, string playerId, int slotIndex, long expectedRevision)
        { return BuyWithRankUp(match,playerId,slotIndex,expectedRevision).Unit; }
        public PurchaseResult BuyWithRankUp(MatchState match,string playerId,int slotIndex,long expectedRevision)
        {
            var player = Player(match, playerId);
            var shop = player.Shop;
            if (!shop.IsInitialized || shop.LastRefreshRound != match.RoundNumber)
                throw new InvalidOperationException("Refresh the shop before buying.");
            if (expectedRevision != shop.Revision) throw new InvalidOperationException("Stale shop revision.");
            if (slotIndex < 0 || slotIndex >= ShopRules.SlotCount) throw new ArgumentOutOfRangeException(nameof(slotIndex));
            var slot = shop.Slots[slotIndex];
            if (slot.IsEmpty) throw new InvalidOperationException("Empty shop slot.");
            var definition = catalog.Get(slot.DefinitionId);
            if (definition.Cost != slot.Cost || slot.Cost < 1 || slot.Cost > 5)
                throw new InvalidOperationException("Shop cost does not match catalog.");
            if (player.Gold < slot.Cost) throw new InvalidOperationException("Insufficient gold.");
            int benchSlot = player.FindFirstEmptyBenchSlot() ?? player.Rules.BenchCapacity;
            long revision = checked(shop.Revision + 1);
            string id = match.NextUnitId(out var sequence);
            var unit = catalog.CreateUnit(id, definition.Id, playerId, UnitRank.One, 0, UnitPlacement.OnBench(benchSlot));
            var holding = match.Pool?.ValidateAcquire(unit, definition.Id);
            var plan = RankUpPlan.Build(player,catalog,unit);
            RankUpSystem.Commit(match,player,plan,holding);
            player.SetProgress(player.Gold - slot.Cost, player.XP, player.Level);
            shop.ClearSlot(slotIndex, revision);
            match.CommitUnitSequence(sequence);
            return new PurchaseResult(plan.PurchaseSurvivor,plan.Events);
        }
        public int SalePrice(UnitInstance unit)
        {
            if (unit == null) throw new ArgumentNullException(nameof(unit));
            int cost = catalog.Get(unit.DefinitionId).Cost;
            if (cost < 1 || cost > 5) throw new InvalidOperationException("Only shop-cost units can be sold.");
            int copies = unit.Rank == UnitRank.One ? 1 : unit.Rank == UnitRank.Two ? 3 : 9;
            return cost * copies - (copies > 1 && cost > 1 ? 1 : 0);
        }
        public int Sell(MatchState match, string playerId, string unitId)
        {
            var player = Player(match, playerId);
            var unit = player.GetUnit(unitId);
            if (unit.Placement.Kind != PlacementKind.Board && unit.Placement.Kind != PlacementKind.Bench)
                throw new InvalidOperationException("Only board or bench units can be sold.");
            int price = SalePrice(unit);
            int gold = checked(player.Gold + price);
            foreach (var item in unit.ItemInstanceIds)
                if (player.ItemInventory.Contains(item) || player.Units.Any(u => u != unit && u.ItemInstanceIds.Contains(item)))
                    throw new InvalidOperationException("Duplicate item ownership.");
            var returned = match.Pool?.ValidateRelease(new[] { unit });
            if (match.Pool == null && unit.PoolOriginDefinitionId != null)
                throw new InvalidOperationException("Pool-origin unit has no owning pool.");
            player.RemoveUnit(unitId);
            if (returned != null) match.Pool.Release(returned);
            player.ReturnSoldItems(unit);
            player.SetProgress(gold, player.XP, player.Level);
            return price;
        }
    }
}
