using System;
using System.Linq;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Match
{
    public sealed class PurchasePreview
    {
        public int Price { get; }
        public int RankUpCount { get; }
        public int Quantity { get; }
        public UnitRank FinalRank { get; }
        public UnitRank ProjectedRank { get; }
        public int FinalUnitCount { get; }
        public bool MergeNextPreparation { get; }
        internal PurchasePreview(int price,RankUpPlan plan)
        {
            MergeNextPreparation=plan.PendingPreparationMerge;Price=price;RankUpCount=plan.Events.Count;FinalUnitCount=plan.Final.Length;
            Quantity=plan.Added.Length;
            FinalRank=plan.Final.Single(n=>n.Originals.Contains(plan.Added[0])).Rank;
            ProjectedRank=plan.ProjectedPurchaseRank();
        }
    }
    // Host commands execute serially. Validate before changing owned state.
    public sealed class ShopTransactionSystem
    {
        private readonly PokemonCatalog catalog;
        public ShopTransactionSystem(PokemonCatalog catalog)
        { this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog)); }
        private static PlayerState Player(MatchState match, string playerId, bool buying=false)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            var player = match.GetPlayer(playerId);
            if (player.IsEliminated || player.HP == 0) throw new InvalidOperationException("Player is eliminated.");
            if (match.Phase != MatchPhase.Preparation && !(buying && match.Phase.IsBattlePhase()))
                throw new InvalidOperationException("Trades require Preparation.");
            return player;
        }
        public UnitInstance Buy(MatchState match, string playerId, int slotIndex, long expectedRevision)
        { return BuyWithRankUp(match,playerId,slotIndex,expectedRevision).Unit; }
        private sealed class Checkout
        {
            internal PlayerState Player;internal RankUpPlan Plan;internal PoolHolding[] Holdings;
            internal int[] Slots;
            internal int Price;internal long ShopRevision,Sequence;
        }
        private Checkout PrepareBuy(MatchState match,string playerId,int slotIndex,long expectedRevision)
        {
            var player = Player(match, playerId, buying:true);
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
            bool benchOnly = match.Phase.IsBattlePhase();
            long revision = checked(shop.Revision + 1);
            var matchingSlots = new[] { slotIndex }.Concat(shop.Slots.Where(other =>
                other.Index != slotIndex && other.DefinitionId == slot.DefinitionId &&
                other.EvolutionStage == slot.EvolutionStage && other.Cost == slot.Cost &&
                other.PoolOriginDefinitionId == slot.PoolOriginDefinitionId).Select(other => other.Index)).Take(3).ToArray();
            int ownedSingles = player.Units.Count(unit => unit.DefinitionId == slot.DefinitionId &&
                unit.EvolutionStage == slot.EvolutionStage && unit.Rank == UnitRank.One &&
                (unit.Placement.Kind == PlacementKind.Bench || (!benchOnly && unit.Placement.Kind == PlacementKind.Board)));
            int quantity = 3 - ownedSingles % 3;
            if (quantity > 1 && matchingSlots.Length >= quantity && player.Gold >= quantity * slot.Cost &&
                (match.Pool == null || match.Pool.GetStock(slot.PoolOriginDefinitionId).Available >= quantity))
            {
                try
                {
                    var bundle = Plan(matchingSlots.Take(quantity).ToArray());
                    var survivor = bundle.Plan.Final.Single(node => node.Originals.Contains(bundle.Plan.Added[0]));
                    if (survivor.Rank > UnitRank.One && bundle.Plan.Added.All(survivor.Originals.Contains))
                        return bundle;
                }
                catch (InvalidOperationException)
                {
                    // A failed bundle can still leave a legal single-card purchase.
                }
            }

            return Plan(new[] { slotIndex });

            Checkout Plan(int[] indices)
            {
                var units = new UnitInstance[indices.Length];
                var holdings = match.Pool == null ? null : new PoolHolding[indices.Length];
                long? previousSequence = null;
                for (int i = 0; i < indices.Length; i++)
                {
                    string id = match.NextUnitId(out long sequence, previousSequence);
                    previousSequence = sequence;
                    int position = player.Rules.BenchCapacity + i;
                    units[i] = catalog.CreateUnit(id, definition.Id, playerId, UnitRank.One, slot.EvolutionStage, UnitPlacement.OnBench(position));
                    if (holdings != null) holdings[i] = match.Pool.ValidateAcquire(units[i], slot.PoolOriginDefinitionId);
                }
                var plan = RankUpPlan.Build(player,catalog,units,benchOnly);
                RankUpSystem.Validate(match,player,plan,holdings);
                return new Checkout { Player=player,Plan=plan,Holdings=holdings,Slots=indices,
                    Price=indices.Length * slot.Cost,ShopRevision=revision,Sequence=previousSequence.Value };
            }
        }
        // Read-only, with exactly the same validation and final capacity plan as commit.
        public PurchasePreview PreviewBuy(MatchState match,string playerId,int slotIndex,long expectedRevision)
        {
            var checkout=PrepareBuy(match,playerId,slotIndex,expectedRevision);
            return new PurchasePreview(checkout.Price,checkout.Plan);
        }
        public PurchaseResult BuyWithRankUp(MatchState match,string playerId,int slotIndex,long expectedRevision)
        {
            var checkout=PrepareBuy(match,playerId,slotIndex,expectedRevision);var player=checkout.Player;
            RankUpSystem.Commit(match,player,checkout.Plan,checkout.Holdings);
            player.SetProgress(player.Gold-checkout.Price,player.XP,player.Level);
            player.Shop.ClearSlots(checkout.Slots,checkout.ShopRevision);match.CommitUnitSequence(checkout.Sequence);
            return new PurchaseResult(checkout.Plan.PurchaseSurvivor,checkout.Plan.Events,checkout.Plan.PendingPreparationMerge,checkout.Slots);
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
            var player = Player(match, playerId, buying:true);
            var unit = player.GetUnit(unitId);
            if (unit.Placement.Kind != PlacementKind.Board && unit.Placement.Kind != PlacementKind.Bench)
                throw new InvalidOperationException("Only board or bench units can be sold.");
            if(match.Phase.IsBattlePhase() && unit.Placement.Kind!=PlacementKind.Bench)
                throw new InvalidOperationException("Combat sales require a bench unit.");
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

