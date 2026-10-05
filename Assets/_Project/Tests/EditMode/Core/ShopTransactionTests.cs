using System;
using System.Linq;
using NUnit.Framework;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Tests
{
    public sealed class ShopTransactionTests
    {
        private PokemonCatalog catalog;
        private MatchState match;
        private PlayerState player;
        private ShopSystem shop;
        private ShopTransactionSystem trades;
        private static PokemonDefinition Definition(int cost, string id = null, EvolutionOption[] evolutions = null) =>
            new PokemonDefinition(id ?? "c" + cost, "Test", cost,
                new PokemonStats(100,10,0,1,0,0,1,2,100,20,.25f), "role", "skill", evolutions:evolutions);
        [SetUp] public void Setup()
        {
            catalog = new PokemonCatalog(Enumerable.Range(1,5).Select(c => Definition(c)).Concat(new[]{Definition(2,"evolved")}));
            match = MatchStateFactory.Create("m",new[]{"p1","p2"});
            match.TransitionTo(MatchPhase.Starting); match.TransitionTo(MatchPhase.Preparation);
            player = match.GetPlayer("p1");
            shop = new ShopSystem(new CatalogShopCandidateSource(catalog,Enumerable.Range(1,5).Select(c=>"c"+c)));
            shop.RefreshForRound(match,"p1"); trades = new ShopTransactionSystem(catalog);
        }
        private UnitInstance Add(string id, int cost, UnitRank rank = UnitRank.One, int bench = 0,
            string[] items = null, bool evolved = false) => player.AddUnit(catalog.CreateUnit(id,
                evolved ? "evolved" : "c"+cost,"p1",rank,evolved ? 1 : 0,UnitPlacement.OnBench(bench),items));
        private string Snapshot() => player.Gold+"|"+player.Shop.Revision+"|"+player.Shop.RandomState+"|"+
            string.Join(",",player.Shop.Slots.Select(s=>s.DefinitionId))+"|"+
            string.Join(",",player.Units.Select(u=>u.InstanceId+":"+u.Placement.Kind+":"+u.Placement.BenchSlot+":"+string.Join("/",u.ItemInstanceIds)))+
            "|"+string.Join(",",player.ItemInventory);
        [TestCase(1,1,1)] [TestCase(1,2,3)] [TestCase(1,3,9)]
        [TestCase(2,1,2)] [TestCase(2,2,5)] [TestCase(2,3,17)]
        [TestCase(3,1,3)] [TestCase(3,2,8)] [TestCase(3,3,26)]
        [TestCase(4,1,4)] [TestCase(4,2,11)] [TestCase(4,3,35)]
        [TestCase(5,1,5)] [TestCase(5,2,14)] [TestCase(5,3,44)]
        public void SalePaysConfirmedRankPrice(int cost,int rank,int price)
        {
            var unit=Add("owned",cost,(UnitRank)rank);
            Assert.That(trades.Sell(match,"p1",unit.InstanceId),Is.EqualTo(price));
            Assert.That(player.Gold,Is.EqualTo(5+price)); Assert.That(player.Units,Is.Empty);
            Assert.That(unit.Placement.Kind,Is.EqualTo(PlacementKind.Unplaced));
        }
        [Test] public void BuyUsesFirstEmptyBenchAndDoesNotConsumeRng()
        {
            Add("existing",1,bench:0); var rng=player.Shop.RandomState; var revision=player.Shop.Revision;
            var unit=trades.Buy(match,"p1",2,revision);
            Assert.That(unit.Placement.BenchSlot,Is.EqualTo(1)); Assert.That(unit.Rank,Is.EqualTo(UnitRank.One));
            Assert.That(unit.EvolutionStage,Is.Zero); Assert.That(unit.ItemInstanceIds,Is.Empty);
            Assert.That(player.Gold,Is.EqualTo(4)); Assert.That(player.Shop.Slots[2].IsEmpty,Is.True);
            Assert.That(player.Shop.Revision,Is.EqualTo(revision+1)); Assert.That(player.Shop.RandomState,Is.EqualTo(rng));
        }
        [Test] public void FullBenchRejectsWithoutAutoDeployAndSaleFreesSlot()
        {
            for(int i=0;i<player.Bench.Count;i++) Add("b"+i,1,rank:UnitRank.Three,bench:i);
            var before=Snapshot(); Assert.Throws<InvalidOperationException>(()=>trades.Buy(match,"p1",0,player.Shop.Revision));
            Assert.That(Snapshot(),Is.EqualTo(before)); Assert.That(player.Board,Is.Empty);
            trades.Sell(match,"p1","b3"); Assert.That(trades.Buy(match,"p1",0,player.Shop.Revision).Placement.BenchSlot,Is.EqualTo(3));
        }
        [Test] public void FailedBuyDoesNotConsumeIdAndOtherPlayerIdsAreSkipped()
        {
            match.GetPlayer("p2").AddUnit(catalog.CreateUnit("unit-1","c1","p2",UnitRank.One,0,UnitPlacement.OnBench(0)));
            player.SetProgress(0,0,1); var before=Snapshot();
            Assert.Throws<InvalidOperationException>(()=>trades.Buy(match,"p1",0,player.Shop.Revision));
            Assert.That(Snapshot(),Is.EqualTo(before)); player.SetProgress(5,0,1);
            Assert.That(trades.Buy(match,"p1",0,player.Shop.Revision).InstanceId,Is.EqualTo("unit-2"));
        }
        [Test] public void RevisionRejectsRequestsAfterPurchaseRerollAndRoundRefresh()
        {
            var revision=player.Shop.Revision; trades.Buy(match,"p1",0,revision);
            var before=Snapshot(); Assert.Throws<InvalidOperationException>(()=>trades.Buy(match,"p1",1,revision));
            Assert.That(Snapshot(),Is.EqualTo(before)); revision=player.Shop.Revision; shop.Reroll(match,"p1");
            Assert.Throws<InvalidOperationException>(()=>trades.Buy(match,"p1",1,revision));
            revision=player.Shop.Revision; match.TransitionTo(MatchPhase.Combat);match.TransitionTo(MatchPhase.Result);match.TransitionTo(MatchPhase.Preparation);
            shop.RefreshForRound(match,"p1"); Assert.Throws<InvalidOperationException>(()=>trades.Buy(match,"p1",1,revision));
        }
        [Test] public void LockedPurchaseHoleSurvivesRoundAndRerollRefills()
        {
            shop.SetLocked(match,"p1",true); trades.Buy(match,"p1",0,player.Shop.Revision); var revision=player.Shop.Revision;
            match.TransitionTo(MatchPhase.Combat);match.TransitionTo(MatchPhase.Result);match.TransitionTo(MatchPhase.Preparation);
            Assert.That(shop.RefreshForRound(match,"p1"),Is.False); Assert.That(player.Shop.Revision,Is.EqualTo(revision));
            Assert.That(player.Shop.Slots[0].IsEmpty,Is.True);shop.Reroll(match,"p1");
            Assert.That(player.Shop.Slots.All(s=>!s.IsEmpty),Is.True);Assert.That(player.Shop.IsLocked,Is.True);
        }
        [Test] public void EvolvedBoardSaleReturnsAllItemsAndClearsRetiredReference()
        {
            var unit=Add("e",2,UnitRank.Three,items:new[]{"item-a","item-b"},evolved:true);
            var items=unit.ItemInstanceIds; player.SetPlacement("e",UnitPlacement.OnBoard(new BoardPosition(0,0)));
            Assert.That(trades.Sell(match,"p1","e"),Is.EqualTo(17));
            Assert.That(player.ItemInventory,Is.EqualTo(new[]{"item-a","item-b"}));Assert.That(items,Is.Empty);Assert.That(player.Board,Is.Empty);
        }
        [Test] public void OverflowAndDuplicateItemsRejectEntireSale()
        {
            var unit=Add("a",1,items:new[]{"item"});player.SetProgress(int.MaxValue,0,1);var before=Snapshot();
            Assert.Throws<OverflowException>(()=>trades.Sell(match,"p1","a"));Assert.That(Snapshot(),Is.EqualTo(before));
            player.SetProgress(5,0,1);Add("b",1,bench:1,items:new[]{"item"});before=Snapshot();
            Assert.Throws<InvalidOperationException>(()=>trades.Sell(match,"p1","a"));Assert.That(Snapshot(),Is.EqualTo(before));
        }
        [Test] public void UnplacedForeignAndMissingUnitsCannotBeSold()
        {
            Add("a",1);player.SetPlacement("a",UnitPlacement.Unplaced);var before=Snapshot();
            Assert.Throws<InvalidOperationException>(()=>trades.Sell(match,"p1","a"));
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(()=>trades.Sell(match,"p2","a"));
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(()=>trades.Sell(match,"p1","missing"));Assert.That(Snapshot(),Is.EqualTo(before));
        }
        [Test] public void CombatAllowsBuyButBlocksSell()
        {
            Add("a",1);match.TransitionTo(MatchPhase.Combat);var before=Snapshot();
            Assert.That(trades.Buy(match,"p1",0,player.Shop.Revision),Is.Not.Null);before=Snapshot();
            Assert.Throws<InvalidOperationException>(()=>trades.Sell(match,"p1","a"));Assert.That(Snapshot(),Is.EqualTo(before));
        }
        [Test] public void CatalogCostMismatchAndEmptySlotAreAtomic()
        {
            var wrong=new ShopTransactionSystem(new PokemonCatalog(new[]{Definition(2,"c1")}));var before=Snapshot();
            Assert.Throws<InvalidOperationException>(()=>wrong.Buy(match,"p1",0,player.Shop.Revision));Assert.That(Snapshot(),Is.EqualTo(before));
            trades.Buy(match,"p1",0,player.Shop.Revision);before=Snapshot();
            Assert.Throws<InvalidOperationException>(()=>trades.Buy(match,"p1",0,player.Shop.Revision));Assert.That(Snapshot(),Is.EqualTo(before));
        }
        [Test] public void EvolutionCatalogRequiresEqualCost()
        {
            Assert.Throws<ArgumentException>(()=>new PokemonCatalog(new[]{Definition(1,"base",new[]{new EvolutionOption("next",0)}),Definition(2,"next")}));
            Assert.DoesNotThrow(()=>new PokemonCatalog(new[]{Definition(2,"base",new[]{new EvolutionOption("next",0)}),Definition(2,"next")}));
        }
        [TestCase(MatchPhase.Waiting)] [TestCase(MatchPhase.Starting)] [TestCase(MatchPhase.Combat)]
        [TestCase(MatchPhase.Result)] [TestCase(MatchPhase.Finished)]
        public void AllOtherPhasesRejectTrades(MatchPhase phase)
        {
            var other=MatchStateFactory.Create("other",new[]{"p1","p2"});
            var p=other.GetPlayer("p1");p.AddUnit(catalog.CreateUnit("a","c1","p1",UnitRank.One,0,UnitPlacement.OnBench(0)));
            if(phase!=MatchPhase.Waiting) other.TransitionTo(MatchPhase.Starting);
            if(phase==MatchPhase.Combat || phase==MatchPhase.Result) { other.TransitionTo(MatchPhase.Preparation);other.TransitionTo(MatchPhase.Combat); }
            if(phase==MatchPhase.Result) other.TransitionTo(MatchPhase.Result);
            if(phase==MatchPhase.Finished) other.TransitionTo(MatchPhase.Finished);
            Assert.Throws<InvalidOperationException>(()=>trades.Buy(other,"p1",0,0));
            Assert.Throws<InvalidOperationException>(()=>trades.Sell(other,"p1","a"));
            Assert.That(p.Gold,Is.EqualTo(5));Assert.That(p.Units.Count,Is.EqualTo(1));
        }
        [Test] public void RevisionOverflowRejectsBuyAndRerollWithoutPayment()
        {
            typeof(ShopState).GetField("<Revision>k__BackingField",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)
                .SetValue(player.Shop,long.MaxValue);
            var before=Snapshot();Assert.Throws<OverflowException>(()=>trades.Buy(match,"p1",0,long.MaxValue));
            Assert.That(Snapshot(),Is.EqualTo(before));Assert.Throws<OverflowException>(()=>shop.Reroll(match,"p1"));
            Assert.That(Snapshot(),Is.EqualTo(before));
        }
        [Test] public void InvalidSlotsAndMissingRoundRefreshRejectBuy()
        {
            var before=Snapshot();Assert.Throws<ArgumentOutOfRangeException>(()=>trades.Buy(match,"p1",-1,player.Shop.Revision));
            Assert.Throws<ArgumentOutOfRangeException>(()=>trades.Buy(match,"p1",5,player.Shop.Revision));Assert.That(Snapshot(),Is.EqualTo(before));
            match.TransitionTo(MatchPhase.Combat);match.TransitionTo(MatchPhase.Result);match.TransitionTo(MatchPhase.Preparation);
            Assert.Throws<InvalidOperationException>(()=>trades.Buy(match,"p1",0,player.Shop.Revision));Assert.That(Snapshot(),Is.EqualTo(before));
        }
        [Test] public void ReturnedInventoryIsReadOnlyAndRepeatedSaleCannotPayTwice()
        {
            Add("a",1,items:new[]{"item"});var inventory=player.ItemInventory;
            trades.Sell(match,"p1","a");Assert.That(inventory,Is.EqualTo(new[]{"item"}));var before=Snapshot();
            Assert.Throws<NotSupportedException>(()=>((System.Collections.Generic.IList<string>)inventory).Add("injected"));
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(()=>trades.Sell(match,"p1","a"));Assert.That(Snapshot(),Is.EqualTo(before));
            Assert.Throws<InvalidOperationException>(()=>player.AddInventoryItem("item"));
        }
    }
}
