using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;
using PokeChess.Core.Random;

namespace PokeChess.Core.Tests
{
    public sealed class SharedPoolTests
    {
        private static PokemonDefinition Definition(string id, int cost, string evolvesTo = null) => new PokemonDefinition(
            id,"Test",cost,new PokemonStats(100,10,0,1,0,0,1,2,100,20,.25f),"role","skill",
            evolutions:evolvesTo == null ? null : new[]{new EvolutionOption(evolvesTo,0)});
        private static PokemonCatalog Catalog() => new PokemonCatalog(Enumerable.Range(1,5)
            .Select(c => Definition("c"+c,c,c==2 ? "evolved" : null))
            .Concat(new[]{Definition("one-b",1),Definition("evolved",2)}));
        private static readonly string[] Ids = {"c1","c2","c3","c4","c5","one-b"};
        private static MatchState Create(PokemonCatalog catalog = null, PoolRules rules = null, bool reverse = false) =>
            MatchStateFactory.CreateWithPool("match",reverse ? new[]{"p2","p1"}:new[]{"p1","p2"},catalog ?? Catalog(),
                reverse ? Ids.Reverse() : Ids,poolRules:rules,matchSeed:123);
        private static void Prepare(MatchState match)
        { match.TransitionTo(MatchPhase.Starting);match.TransitionTo(MatchPhase.Preparation); }
        private static void NextRound(MatchState match)
        { match.TransitionTo(MatchPhase.Combat);match.TransitionTo(MatchPhase.Result);match.TransitionTo(MatchPhase.Preparation); }
        private static UnitInstance Register(MatchState m,PokemonCatalog c,string id,string origin,UnitRank rank=UnitRank.One,
            UnitPlacement? placement=null,string definition=null,string owner="p1",string[] items=null) =>
            SharedPoolSystem.RegisterUnit(m,c.CreateUnit(id,definition ?? origin,owner,rank,definition==null ? 0 : 1,
                placement ?? UnitPlacement.OnBench(0),items),origin);
        private static string Snapshot(MatchState m) => string.Join("|",m.Pool.Stocks.Select(s=>s.DefinitionId+":"+s.Initial+":"+s.Available))+
            "|"+string.Join("|",m.Players.OrderBy(p=>p.PlayerId).Select(p=>p.PlayerId+":"+p.Gold+":"+p.HP+":"+p.IsEliminated+
                ":"+p.Shop.Revision+":"+p.Shop.IsLocked+":"+p.Shop.LastRefreshRound+":"+p.Shop.RandomState+":"+p.Shop.RandomDrawCount+
                ":"+string.Join(",",p.Shop.Slots.Select(s=>s.DefinitionId+"/"+s.Cost))+
                ":"+string.Join(",",p.Units.Select(u=>u.InstanceId+"/"+u.DefinitionId+"/"+u.PoolOriginDefinitionId+"/"+u.Rank+"/"+u.Placement.Kind+
                    "/"+u.Placement.BenchSlot+"/"+u.Placement.Position+"/"+string.Join("+",u.ItemInstanceIds)))+
                ":"+string.Join(",",p.ItemInventory)));
        [TestCase(1,22)] [TestCase(2,18)] [TestCase(3,14)] [TestCase(4,10)] [TestCase(5,9)]
        public void InitialStockIsPerSpecies(int cost,int count)
        {
            var m=Create();Assert.That(m.Pool.GetStock("c"+cost).Initial,Is.EqualTo(count));
            Assert.That(m.Pool.GetStock("c"+cost).Available,Is.EqualTo(count));m.Pool.AssertConservation(m);
            if(cost==1) Assert.That(m.Pool.GetStock("one-b").Available,Is.EqualTo(count));
        }
        [Test] public void RulesAndStockSnapshotsAreDefensiveAndReadOnly()
        {
            var sizes=new[]{3,2,1,0,0};var rules=new PoolRules(sizes);sizes[0]=99;
            var c=Catalog();var m=Create(c,rules);var stocks=m.Pool.Stocks;var stock=m.Pool.GetStock("c1");
            Assert.That(stock.Available,Is.EqualTo(3));Register(m,c,"a","c1");
            Assert.That(stock.Available,Is.EqualTo(3));Assert.That(m.Pool.GetStock("c1").Available,Is.EqualTo(2));
            Assert.Throws<NotSupportedException>(()=>((IList<PoolStock>)stocks).Clear());
            Assert.That(m.Pool.TotalAvailable(4),Is.Zero);m.Pool.AssertConservation(m);
        }
        [Test] public void InvalidRulesCatalogAndIdentifiersAreRejected()
        {
            Assert.Throws<ArgumentException>(()=>new PoolRules(new[]{1,2}));
            Assert.Throws<ArgumentException>(()=>new PoolRules(new[]{1,2,-1,4,5}));
            Assert.Throws<ArgumentOutOfRangeException>(()=>new PoolRules().SizeForCost(0));
            Assert.Throws<ArgumentOutOfRangeException>(()=>new PoolRules().SizeForCost(6));
            Assert.Throws<ArgumentNullException>(()=>MatchStateFactory.CreateWithPool("m",new[]{"p1","p2"},null,Ids));
            Assert.Throws<ArgumentNullException>(()=>MatchStateFactory.CreateWithPool("m",new[]{"p1","p2"},Catalog(),null));
            Assert.Throws<ArgumentException>(()=>MatchStateFactory.CreateWithPool("m",new[]{"p1","p2"},Catalog(),Array.Empty<string>()));
            Assert.Throws<ArgumentException>(()=>MatchStateFactory.CreateWithPool("m",new[]{"p1","p2"},Catalog(),new[]{"c1","c1"}));
            Assert.Throws<KeyNotFoundException>(()=>MatchStateFactory.CreateWithPool("m",new[]{"p1","p2"},Catalog(),new[]{"missing"}));
            Assert.Throws<ArgumentException>(()=>MatchStateFactory.CreateWithPool("m",new[]{"p1","p2"},new PokemonCatalog(new[]{Definition("free",0)}),new[]{"free"}));
            Assert.Throws<OverflowException>(()=>Create(rules:new PoolRules(new[]{int.MaxValue,1,1,1,1})));
        }
        [Test] public void ExactWeightedIntervalsFollowRemainingCopiesAndExcludeZeroStock()
        {
            var c=Catalog();var m=Create(c,new PoolRules(new[]{3,3,3,3,3}));
            Register(m,c,"a","c1");Register(m,c,"b","c1",placement:UnitPlacement.OnBench(1));
            Assert.That(m.Pool.TotalAvailable(1),Is.EqualTo(4));
            for(int roll=0;roll<4;roll++) Assert.That(m.Pool.SelectDefinition(1,roll).Id,Is.EqualTo(roll==0 ? "c1":"one-b"));
            Register(m,c,"c","c1",placement:UnitPlacement.OnBench(2));
            for(int roll=0;roll<3;roll++) Assert.That(m.Pool.SelectDefinition(1,roll).Id,Is.EqualTo("one-b"));
            Assert.Throws<ArgumentOutOfRangeException>(()=>m.Pool.SelectDefinition(1,-1));
            Assert.Throws<ArgumentOutOfRangeException>(()=>m.Pool.SelectDefinition(1,3));
            Assert.Throws<ArgumentOutOfRangeException>(()=>m.Pool.TotalAvailable(6));m.Pool.AssertConservation(m);
        }
        [Test] public void MatchesAreIndependentAndCrossMatchRegistrationIsRejected()
        {
            var c=Catalog();var a=Create(c);var b=Create(c);var unit=Register(a,c,"a","c1");
            Assert.That(a.Pool.GetStock("c1").Available,Is.EqualTo(21));Assert.That(b.Pool.GetStock("c1").Available,Is.EqualTo(22));
            var before=Snapshot(b);Assert.Throws<InvalidOperationException>(()=>SharedPoolSystem.RegisterUnit(b,unit,"c1"));
            Assert.That(Snapshot(b),Is.EqualTo(before));Assert.Throws<ArgumentException>(()=>a.Pool.AssertConservation(b));
        }
        [Test] public void OffersDoNotReserveAndOnlyFirstBuyerGetsLastCopy()
        {
            var c=new PokemonCatalog(new[]{Definition("c1",1)});
            var m=MatchStateFactory.CreateWithPool("m",new[]{"p1","p2"},c,new[]{"c1"},new PoolRules(new[]{1,0,0,0,0}));Prepare(m);
            var shop=new ShopSystem();shop.RefreshForRound(m,"p1");shop.RefreshForRound(m,"p2");
            Assert.That(m.Players.All(p=>p.Shop.Slots.All(s=>s.DefinitionId=="c1")),Is.True);
            Assert.That(m.Pool.GetStock("c1").Available,Is.EqualTo(1));var trades=new ShopTransactionSystem(c);
            var p1=m.GetPlayer("p1");var p2=m.GetPlayer("p2");var rng=p2.Shop.RandomState;
            var purchased=trades.Buy(m,"p1",0,p1.Shop.Revision);Assert.That(purchased.PoolOriginDefinitionId,Is.EqualTo("c1"));
            Assert.That(p2.Shop.RandomState,Is.EqualTo(rng));var before=Snapshot(m);
            var error=Assert.Throws<InvalidOperationException>(()=>trades.Buy(m,"p2",0,p2.Shop.Revision));
            Assert.That(error.Message,Does.Contain("PoolUnavailable"));Assert.That(Snapshot(m),Is.EqualTo(before));m.Pool.AssertConservation(m);
            trades.Sell(m,"p1",purchased.InstanceId);var next=trades.Buy(m,"p2",0,p2.Shop.Revision);
            Assert.That(next.InstanceId,Is.EqualTo("unit-2"));Assert.That(m.Pool.GetStock("c1").Available,Is.Zero);m.Pool.AssertConservation(m);
        }
        [TestCase(1,1,1)] [TestCase(1,2,3)] [TestCase(1,3,9)]
        [TestCase(2,1,2)] [TestCase(2,2,5)] [TestCase(2,3,17)]
        [TestCase(3,1,3)] [TestCase(3,2,8)] [TestCase(3,3,26)]
        [TestCase(4,1,4)] [TestCase(4,2,11)] [TestCase(4,3,35)]
        [TestCase(5,1,5)] [TestCase(5,2,14)] [TestCase(5,3,44)]
        public void RankSalesReturnCopiesIndependentlyOfGold(int cost,int rank,int gold)
        {
            var c=Catalog();var m=Create(c);Prepare(m);var pool=m.Pool;int initial=pool.GetStock("c"+cost).Initial;
            int copies=rank==1 ? 1 : rank==2 ? 3 : 9;var unit=Register(m,c,"a","c"+cost,(UnitRank)rank);
            Assert.That(pool.GetStock("c"+cost).Available,Is.EqualTo(initial-copies));pool.AssertConservation(m);
            Assert.That(new ShopTransactionSystem(c).Sell(m,"p1",unit.InstanceId),Is.EqualTo(gold));
            Assert.That(pool.GetStock("c"+cost).Available,Is.EqualTo(initial));Assert.That(unit.PoolOriginDefinitionId,Is.Null);pool.AssertConservation(m);
        }
        [Test] public void EvolvedItemEquippedUnitReturnsToOriginalSpecies()
        {
            var c=Catalog();var m=Create(c);Prepare(m);
            var unit=Register(m,c,"e","c2",UnitRank.Three,definition:"evolved",items:new[]{"item-a","item-b"});
            Assert.That(m.Pool.GetStock("c2").Available,Is.EqualTo(9));
            Assert.That(new ShopTransactionSystem(c).Sell(m,"p1","e"),Is.EqualTo(17));
            Assert.That(m.Pool.GetStock("c2").Available,Is.EqualTo(18));
            Assert.Throws<KeyNotFoundException>(()=>m.Pool.GetStock("evolved"));
            Assert.That(m.GetPlayer("p1").ItemInventory,Is.EqualTo(new[]{"item-a","item-b"}));Assert.That(unit.ItemInstanceIds,Is.Empty);m.Pool.AssertConservation(m);
        }
        [Test] public void RewardUnitSaleDoesNotInventStock()
        {
            var c=Catalog();var m=Create(c);Prepare(m);var p=m.GetPlayer("p1");
            p.AddUnit(c.CreateUnit("gift","c1","p1",UnitRank.Three,0,UnitPlacement.OnBench(0)));
            new ShopTransactionSystem(c).Sell(m,"p1","gift");Assert.That(m.Pool.GetStock("c1").Available,Is.EqualTo(22));m.Pool.AssertConservation(m);
        }
        [Test] public void RegisterFailuresDoNotConsumeStock()
        {
            var c=Catalog();var m=Create(c,new PoolRules(new[]{1,1,1,1,1}));var before=Snapshot(m);
            Assert.Throws<InvalidOperationException>(()=>Register(m,c,"a","c1",UnitRank.Two));Assert.That(Snapshot(m),Is.EqualTo(before));
            Assert.Throws<ArgumentException>(()=>Register(m,c,"a","c1",definition:"c2"));Assert.That(Snapshot(m),Is.EqualTo(before));
            Assert.Throws<ArgumentOutOfRangeException>(()=>Register(m,c,"a","c1",placement:UnitPlacement.OnBench(99)));Assert.That(Snapshot(m),Is.EqualTo(before));
            Register(m,c,"a","c1");before=Snapshot(m);
            Assert.Throws<InvalidOperationException>(()=>Register(m,c,"a","one-b",placement:UnitPlacement.OnBench(1)));Assert.That(Snapshot(m),Is.EqualTo(before));m.Pool.AssertConservation(m);
        }
        [Test] public void BuyValidationFailuresKeepStockGoldAndOffers()
        {
            var c=Catalog();var m=Create(c);Prepare(m);var p=m.GetPlayer("p1");var shop=new ShopSystem();shop.RefreshForRound(m,"p1");
            var trades=new ShopTransactionSystem(c);var before=Snapshot(m);
            Assert.Throws<InvalidOperationException>(()=>trades.Buy(m,"p1",0,p.Shop.Revision-1));Assert.That(Snapshot(m),Is.EqualTo(before));
            Assert.Throws<ArgumentOutOfRangeException>(()=>trades.Buy(m,"p1",5,p.Shop.Revision));Assert.That(Snapshot(m),Is.EqualTo(before));
            p.SetProgress(0,0,1);before=Snapshot(m);Assert.Throws<InvalidOperationException>(()=>trades.Buy(m,"p1",0,p.Shop.Revision));Assert.That(Snapshot(m),Is.EqualTo(before));
            p.SetProgress(5,0,1);for(int i=0;i<p.Bench.Count;i++) p.AddUnit(c.CreateUnit("gift"+i,"c1","p1",UnitRank.One,0,UnitPlacement.OnBench(i)));
            before=Snapshot(m);Assert.Throws<InvalidOperationException>(()=>trades.Buy(m,"p1",0,p.Shop.Revision));Assert.That(Snapshot(m),Is.EqualTo(before));m.Pool.AssertConservation(m);
        }
        [Test] public void FailedSaleKeepsPoolAndItems()
        {
            var c=Catalog();var m=Create(c);Prepare(m);var p=m.GetPlayer("p1");Register(m,c,"a","c1",items:new[]{"item"});
            p.SetProgress(int.MaxValue,0,1);var trades=new ShopTransactionSystem(c);var before=Snapshot(m);
            Assert.Throws<OverflowException>(()=>trades.Sell(m,"p1","a"));Assert.That(Snapshot(m),Is.EqualTo(before));
            p.SetProgress(5,0,1);p.AddUnit(c.CreateUnit("gift","c1","p1",UnitRank.One,0,UnitPlacement.OnBench(1),new[]{"item"}));before=Snapshot(m);
            Assert.Throws<InvalidOperationException>(()=>trades.Sell(m,"p1","a"));Assert.That(Snapshot(m),Is.EqualTo(before));m.Pool.AssertConservation(m);
        }
        [Test] public void ExhaustedCostProducesEmptySlotsWithoutProbabilityRedistribution()
        {
            var c=Catalog();var m=Create(c,new PoolRules(new[]{2,2,2,2,0}));Prepare(m);var p=m.GetPlayer("p1");
            var probabilities=Enumerable.Range(0,10).Select(_=>new[]{0,0,0,0,100});
            var shop=ShopSystem.ForSharedPool(new ShopRules(probabilities));shop.RefreshForRound(m,"p1");
            Assert.That(p.Shop.Slots.All(s=>s.IsEmpty),Is.True);Assert.That(p.Shop.RandomDrawCount,Is.EqualTo(5));
            shop.Reroll(m,"p1");Assert.That(p.Gold,Is.EqualTo(3));Assert.That(p.Shop.Slots.All(s=>s.IsEmpty),Is.True);
            Assert.That(p.Shop.RandomDrawCount,Is.EqualTo(10));Assert.That(m.Pool.TotalAvailable(1),Is.EqualTo(4));m.Pool.AssertConservation(m);
        }
        [Test] public void RealShopGenerationUsesExactPoolWeightedDraws()
        {
            var c=Catalog();var m=Create(c,new PoolRules(new[]{3,3,3,3,3}));Prepare(m);var p=m.GetPlayer("p1");
            Register(m,c,"a","c1");Register(m,c,"b","c1",placement:UnitPlacement.OnBench(1));
            var rng=new DeterministicRandom(p.Shop.RandomState);var expected=new string[5];
            for(int i=0;i<5;i++) { rng.NextInt(100);expected[i]=rng.NextInt(4)==0 ? "c1":"one-b"; }
            new ShopSystem().RefreshForRound(m,"p1");Assert.That(p.Shop.Slots.Select(s=>s.DefinitionId),Is.EqualTo(expected));
            Assert.That(p.Shop.RandomState,Is.EqualTo(rng.State));Assert.That(m.Pool.TotalAvailable(1),Is.EqualTo(4));m.Pool.AssertConservation(m);
        }
        [Test] public void LockRefreshAndRerollNeverReserveStock()
        {
            var c=Catalog();var m=Create(c);Prepare(m);var p=m.GetPlayer("p1");var shop=new ShopSystem();shop.RefreshForRound(m,"p1");
            var stocks=m.Pool.Stocks.Select(s=>s.Available).ToArray();shop.SetLocked(m,"p1",true);var rng=p.Shop.RandomState;var revision=p.Shop.Revision;
            NextRound(m);Assert.That(shop.RefreshForRound(m,"p1"),Is.False);Assert.That(p.Shop.RandomState,Is.EqualTo(rng));Assert.That(p.Shop.Revision,Is.EqualTo(revision));
            shop.Reroll(m,"p1");Assert.That(p.Shop.IsLocked,Is.True);Assert.That(m.Pool.Stocks.Select(s=>s.Available),Is.EqualTo(stocks));m.Pool.AssertConservation(m);
        }
        [Test] public void EliminationReturnsBoardBenchUnplacedAndNeverReturnsSoldCopiesTwice()
        {
            var c=Catalog();var m=Create(c);Prepare(m);var p=m.GetPlayer("p1");var shop=new ShopSystem();shop.RefreshForRound(m,"p1");shop.SetLocked(m,"p1",true);
            var sold=Register(m,c,"sold","c1");new ShopTransactionSystem(c).Sell(m,"p1",sold.InstanceId);
            var board=Register(m,c,"board","c1",placement:UnitPlacement.OnBoard(new BoardPosition(0,0)));
            Register(m,c,"bench","c1",UnitRank.Two,UnitPlacement.OnBench(0));
            Register(m,c,"unplaced","c1",UnitRank.Three,UnitPlacement.Unplaced);
            p.AddUnit(c.CreateUnit("gift","c1","p1",UnitRank.Three,0,UnitPlacement.Unplaced));m.Pool.AssertConservation(m);
            int gold=p.Gold;var rng=p.Shop.RandomState;p.SetHP(0);Assert.That(SharedPoolSystem.ReleasePlayerHoldings(m,"p1"),Is.True);
            Assert.That(p.Units,Is.Empty);Assert.That(p.Shop.Slots.All(s=>s.IsEmpty),Is.True);Assert.That(p.Shop.IsLocked,Is.False);
            Assert.That(board.Placement.Kind,Is.EqualTo(PlacementKind.Unplaced));Assert.That(p.IsEliminated,Is.True);
            Assert.That(p.Gold,Is.EqualTo(gold));Assert.That(p.Shop.RandomState,Is.EqualTo(rng));Assert.That(m.Pool.GetStock("c1").Available,Is.EqualTo(22));m.Pool.AssertConservation(m);
            var before=Snapshot(m);Assert.That(SharedPoolSystem.ReleasePlayerHoldings(m,"p1"),Is.False);Assert.That(Snapshot(m),Is.EqualTo(before));
            Assert.Throws<InvalidOperationException>(()=>p.SetHP(1));
            Assert.Throws<InvalidOperationException>(()=>shop.Reroll(m,"p1"));
            Assert.Throws<InvalidOperationException>(()=>shop.SetLocked(m,"p1",true));
            Assert.Throws<InvalidOperationException>(()=>shop.RefreshForRound(m,"p1"));
            Assert.Throws<InvalidOperationException>(()=>new ShopTransactionSystem(c).Buy(m,"p1",0,p.Shop.Revision));
            Assert.Throws<InvalidOperationException>(()=>Register(m,c,"new","c1"));Assert.That(Snapshot(m),Is.EqualTo(before));
        }
        [Test] public void AlivePlayerAndOverflowEliminationDoNotReleaseAnything()
        {
            var c=Catalog();var m=Create(c);var p=m.GetPlayer("p1");Register(m,c,"a","c1");var before=Snapshot(m);
            Assert.Throws<InvalidOperationException>(()=>SharedPoolSystem.ReleasePlayerHoldings(m,"p1"));Assert.That(Snapshot(m),Is.EqualTo(before));
            p.SetHP(0);typeof(ShopState).GetField("<Revision>k__BackingField",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(p.Shop,long.MaxValue);
            before=Snapshot(m);Assert.Throws<OverflowException>(()=>SharedPoolSystem.ReleasePlayerHoldings(m,"p1"));Assert.That(Snapshot(m),Is.EqualTo(before));m.Pool.AssertConservation(m);
        }
        [Test] public void FakeCopiedProvenanceCannotReturnAnotherMatchesStock()
        {
            var c=Catalog();var source=Create(c);var unit=Register(source,c,"foreign","c1");var target=Create(c);Prepare(target);
            target.GetPlayer("p1").AddUnit(unit);var before=Snapshot(target);
            Assert.Throws<InvalidOperationException>(()=>new ShopTransactionSystem(c).Sell(target,"p1","foreign"));Assert.That(Snapshot(target),Is.EqualTo(before));
            Assert.Throws<InvalidOperationException>(()=>target.Pool.AssertConservation(target));source.Pool.AssertConservation(source);
        }
        [Test] public void SameSeedAndCommandOrderRemainIdenticalWhenRegistrationOrderReverses()
        {
            var c=Catalog();var a=Create(c,new PoolRules(new[]{3,3,3,3,3}));var b=Create(c,new PoolRules(new[]{3,3,3,3,3}),true);
            Prepare(a);Prepare(b);var shop=new ShopSystem();var trades=new ShopTransactionSystem(c);
            foreach(var m in new[]{a,b}) { shop.RefreshForRound(m,"p1");shop.RefreshForRound(m,"p2"); }
            Assert.That(Snapshot(a),Is.EqualTo(Snapshot(b)));
            for(int step=0;step<30;step++)
            {
                string player=step%2==0 ? "p1":"p2";
                foreach(var m in new[]{a,b})
                {
                    var p=m.GetPlayer(player);var u=trades.Buy(m,player,step%5,p.Shop.Revision);m.Pool.AssertConservation(m);
                    trades.Sell(m,player,u.InstanceId);p.SetProgress(100,p.XP,p.Level);shop.Reroll(m,player);m.Pool.AssertConservation(m);
                }
                Assert.That(Snapshot(a),Is.EqualTo(Snapshot(b)));
            }
        }
        [Test] public void UnpooledMatchCannotUsePoolOnlyServices()
        {
            var m=MatchStateFactory.Create("m",new[]{"p1","p2"});Prepare(m);
            Assert.Throws<InvalidOperationException>(()=>new ShopSystem().RefreshForRound(m,"p1"));
            Assert.Throws<InvalidOperationException>(()=>SharedPoolSystem.ReleasePlayerHoldings(m,"p1"));
        }
    }
}
