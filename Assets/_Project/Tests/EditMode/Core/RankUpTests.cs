using System;
using System.Linq;
using System.Collections.Generic;
using NUnit.Framework;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;
using PokeChess.Core.Battle;

namespace PokeChess.Core.Tests
{
    public sealed class RankUpTests
    {
        private PokemonCatalog catalog;
        private MatchState match;
        private PlayerState player;
        private RankUpSystem ranks;
        private ShopTransactionSystem trades;
        private static PokemonDefinition Definition(string id,float hp=100) => new PokemonDefinition(id,id,1,
            new PokemonStats(hp,10,25,1.2f,7,9,2,3,100,20,.25f,AttackDeliveryType.Projectile,8,.1f,1.6f,.5f),"role","skill");
        [SetUp] public void Setup()
        {
            catalog=new PokemonCatalog(new[]{Definition("test"),Definition("other"),Definition("evolved")});
            match=MatchStateFactory.CreateWithPool("m",new[]{"p1","p2"},catalog,new[]{"test"},poolRules:new PoolRules(new[]{1000,1000,1000,1000,1000}));
            player=match.GetPlayer("p1");player.SetProgress(100,0,1);
            match.TransitionTo(MatchPhase.Starting);match.TransitionTo(MatchPhase.Preparation);new ShopSystem().RefreshForRound(match,"p1");
            ranks=new RankUpSystem(catalog);trades=new ShopTransactionSystem(catalog);
        }
        private UnitInstance Add(string id,int slot,UnitRank rank=UnitRank.One,UnitPlacement? placement=null,
            string definition="test",int stage=0,string[] items=null,bool tracked=true,string owner="p1")
        {
            var source=catalog.CreateUnit(id,definition,owner,rank,stage,placement ?? UnitPlacement.OnBench(slot),items);
            return tracked ? SharedPoolSystem.RegisterUnit(match,source,"test") : match.GetPlayer(owner).AddUnit(source);
        }
        private IReadOnlyList<UnitRankedUp> Resolve() => ranks.Resolve(match,"p1",player.PlacementRevision);
        private string Snapshot() => player.Gold+"|"+player.PlacementRevision+"|"+player.Shop.Revision+"|"+player.Shop.RandomState+"|"+player.Shop.RandomDrawCount+
            "|"+string.Join(",",player.Shop.Slots.Select(s=>s.DefinitionId))+"|"+string.Join(",",player.ItemInventory)+"|"+
            string.Join(",",player.Units.Select(u=>u.InstanceId+":"+u.Rank+":"+u.PoolOriginDefinitionId+":"+u.Placement.Kind+":"+u.Placement.BenchSlot+":"+u.Placement.Position?.Column+":"+u.Placement.Position?.Row+":"+string.Join("/",u.ItemInstanceIds)))+
            "|"+match.Pool.GetStock("test").Available;
        [TestCase(false)] [TestCase(true)]
        public void PurchasePreviewIsPureAndMatchesCommitEvenAtFullBench(bool fullBench)
        {
            Add("a",0);Add("b",1);
            if(fullBench) for(int i=2;i<9;i++) Add("f"+i,i,UnitRank.Three);
            var before=Snapshot();var preview=trades.PreviewBuy(match,"p1",0,player.Shop.Revision);
            Assert.That(preview.Price,Is.EqualTo(1));Assert.That(preview.RankUpCount,Is.EqualTo(1));Assert.That(preview.FinalRank,Is.EqualTo(UnitRank.Two));
            Assert.That(Snapshot(),Is.EqualTo(before));Assert.That(trades.PreviewBuy(match,"p1",0,player.Shop.Revision).FinalUnitCount,Is.EqualTo(preview.FinalUnitCount));
            var result=trades.BuyWithRankUp(match,"p1",0,player.Shop.Revision);
            Assert.That(result.Unit.Rank,Is.EqualTo(preview.FinalRank));Assert.That(result.RankUps.Count,Is.EqualTo(preview.RankUpCount));Assert.That(player.Units.Count,Is.EqualTo(preview.FinalUnitCount));
            match.Pool.AssertConservation(match);
        }
        [Test] public void RepeatedPreviewDoesNotConsumeIdRngOrRevisions()
        {
            var before=Snapshot();for(int i=0;i<10;i++) trades.PreviewBuy(match,"p1",0,player.Shop.Revision);
            Assert.That(Snapshot(),Is.EqualTo(before));Assert.That(trades.Buy(match,"p1",0,player.Shop.Revision).InstanceId,Is.EqualTo("unit-1"));
        }
        [TestCase("gold")] [TestCase("stale")] [TestCase("full")] [TestCase("result")]
        public void RejectedPreviewAndBuyUseSameValidationWithoutMutation(string reason)
        {
            if(reason=="gold") player.SetProgress(0,0,1);
            if(reason=="full") for(int i=0;i<9;i++) Add("f"+i,i,UnitRank.Three);
            if(reason=="result") { match.TransitionTo(MatchPhase.Combat);match.TransitionTo(MatchPhase.Result); }
            long revision=player.Shop.Revision-(reason=="stale" ? 1 : 0);var before=Snapshot();
            var preview=Assert.Throws<InvalidOperationException>(()=>trades.PreviewBuy(match,"p1",0,revision));Assert.That(Snapshot(),Is.EqualTo(before));
            var buy=Assert.Throws<InvalidOperationException>(()=>trades.Buy(match,"p1",0,revision));Assert.That(buy.Message,Is.EqualTo(preview.Message));Assert.That(Snapshot(),Is.EqualTo(before));
        }
        [Test] public void TwoCopiesAreNoOpAndThirdCopyUpgradesOnce()
        {
            var a=Add("a",0);Add("b",1);var before=Snapshot();Assert.That(Resolve(),Is.Empty);Assert.That(Snapshot(),Is.EqualTo(before));
            var c=Add("c",2);var revision=player.PlacementRevision;int stock=match.Pool.GetStock("test").Available;
            var events=Resolve();Assert.That(events.Count,Is.EqualTo(1));Assert.That(events[0].ConsumedUnitIds,Is.EqualTo(new[]{"b","c"}));
            Assert.That(player.Units.Single(),Is.SameAs(a));Assert.That(a.Rank,Is.EqualTo(UnitRank.Two));Assert.That(a.Placement.BenchSlot,Is.Zero);
            Assert.That(c.Placement.Kind,Is.EqualTo(PlacementKind.Unplaced));Assert.That(c.PoolOriginDefinitionId,Is.Null);
            Assert.That(player.PlacementRevision,Is.EqualTo(revision+1));Assert.That(match.Pool.GetStock("test").Available,Is.EqualTo(stock));match.Pool.AssertConservation(match);
            before=Snapshot();Assert.That(Resolve(),Is.Empty);Assert.That(Snapshot(),Is.EqualTo(before));
        }
        [TestCase(UnitRank.Two,UnitRank.Three,9)] [TestCase(UnitRank.Three,UnitRank.Three,27)]
        public void HigherRanksUpgradeAndThreeIsTerminal(UnitRank initial,UnitRank expected,int copies)
        {
            var a=Add("a",0,initial);Add("b",1,initial);Add("c",2,initial);
            var events=Resolve();Assert.That(a.Rank,Is.EqualTo(expected));Assert.That(events.Count,Is.EqualTo(initial==UnitRank.Two ? 1 : 0));
            Assert.That(match.Pool.GetStock("test").Held,Is.EqualTo(copies));match.Pool.AssertConservation(match);
        }
        [Test] public void BuyChainsOneToTwoToThreeAndReturnsFinalSurvivor()
        {
            var a=Add("a",0,UnitRank.Two,items:new[]{"keep"});Add("b",1,UnitRank.Two,items:new[]{"return-b"});Add("c",2,items:new[]{"return-c"});Add("d",3,items:new[]{"return-d"});
            var revision=player.PlacementRevision;var shopRevision=player.Shop.Revision;var rng=player.Shop.RandomState;
            var result=trades.BuyWithRankUp(match,"p1",0,shopRevision);
            Assert.That(result.Unit,Is.SameAs(a));Assert.That(a.Rank,Is.EqualTo(UnitRank.Three));Assert.That(result.RankUps.Select(e=>e.Rank),Is.EqualTo(new[]{UnitRank.Two,UnitRank.Three}));
            Assert.That(result.RankUps[1].ConsumedUnitIds,Is.EqualTo(new[]{"b","c"}));Assert.That(player.Units.Count,Is.EqualTo(1));
            Assert.That(a.ItemInstanceIds,Is.EqualTo(new[]{"keep"}));Assert.That(player.ItemInventory,Is.EquivalentTo(new[]{"return-b","return-c","return-d"}));
            Assert.That(result.RankUps.SelectMany(e=>e.ReturnedItemIds),Is.EquivalentTo(player.ItemInventory));
            Assert.That(player.Gold,Is.EqualTo(99));Assert.That(player.Shop.Revision,Is.EqualTo(shopRevision+1));Assert.That(player.PlacementRevision,Is.EqualTo(revision+1));
            Assert.That(player.Shop.RandomState,Is.EqualTo(rng));Assert.That(match.Pool.GetStock("test").Held,Is.EqualTo(9));match.Pool.AssertConservation(match);
            Assert.That(trades.Sell(match,"p1","a"),Is.EqualTo(9));Assert.That(match.Pool.GetStock("test").Held,Is.Zero);match.Pool.AssertConservation(match);
        }
        [Test] public void FullBenchPurchaseCanMergeWithoutTemporarySlot()
        {
            var a=Add("a",0);Add("b",1);for(int i=2;i<9;i++) Add("f"+i,i,UnitRank.Three);
            Assert.That(player.FindFirstEmptyBenchSlot(),Is.Null);var result=trades.BuyWithRankUp(match,"p1",0,player.Shop.Revision);
            Assert.That(result.Unit,Is.SameAs(a));Assert.That(player.Units.Count,Is.EqualTo(8));Assert.That(a.Rank,Is.EqualTo(UnitRank.Two));
            Assert.That(player.Units.All(u=>u.Placement.BenchSlot<9),Is.True);Assert.That(player.FindFirstEmptyBenchSlot(),Is.EqualTo(1));match.Pool.AssertConservation(match);
        }
        [Test] public void FullBenchWithoutMergeRejectsEverythingAndKeepsUnitSequence()
        {
            for(int i=0;i<9;i++) Add("f"+i,i,UnitRank.Three);var before=Snapshot();
            Assert.Throws<InvalidOperationException>(()=>trades.Buy(match,"p1",0,player.Shop.Revision));Assert.That(Snapshot(),Is.EqualTo(before));
            trades.Sell(match,"p1","f8");Assert.That(trades.Buy(match,"p1",0,player.Shop.Revision).InstanceId,Is.EqualTo("unit-1"));match.Pool.AssertConservation(match);
        }
        [Test] public void FailedPurchaseDoesNotConsumeGeneratedId()
        {
            player.SetProgress(0,0,1);Assert.Throws<InvalidOperationException>(()=>trades.Buy(match,"p1",0,player.Shop.Revision));player.SetProgress(100,0,1);
            Assert.That(trades.Buy(match,"p1",0,player.Shop.Revision).InstanceId,Is.EqualTo("unit-1"));match.Pool.AssertConservation(match);
        }
        [Test] public void BoardWinsOverBenchAndBoardOrderIsDeterministic()
        {
            player.SetProgress(100,0,3);Add("a",0);var first=Add("z",0,placement:UnitPlacement.OnBoard(new BoardPosition(6,0)));
            Add("b",0,placement:UnitPlacement.OnBoard(new BoardPosition(0,1)));var events=Resolve();
            Assert.That(events.Single().ResultUnitId,Is.EqualTo("z"));Assert.That(player.Board.Single(),Is.SameAs(first));Assert.That(player.DeployedUnitCount,Is.EqualTo(1));
            Assert.That(first.Placement.Position.Value,Is.EqualTo(new BoardPosition(6,0)));match.Pool.AssertConservation(match);
        }
        [Test] public void RegistrationOrderDoesNotChangeSurvivorOrEventOrder()
        {
            string Run(bool reverse)
            {
                Setup();var slots=new[]{0,1,2,3,4,5};foreach(var i in reverse ? slots.Reverse() : slots) Add("u"+i,i,items:new[]{"item"+i});
                var events=Resolve();return string.Join("|",events.Select(e=>e.ResultUnitId+":"+string.Join(",",e.ConsumedUnitIds)))+
                    "|"+string.Join(",",player.Units.OrderBy(u=>u.InstanceId).Select(u=>u.InstanceId+":"+u.Rank))+"|"+string.Join(",",player.ItemInventory);
            }
            Assert.That(Run(true),Is.EqualTo(Run(false)));
        }
        [TestCase("definition")] [TestCase("stage")] [TestCase("rank")] [TestCase("unplaced")] [TestCase("owner")]
        public void IncompatibleUnitsDoNotMerge(string difference)
        {
            Add("a",0);Add("b",1);Add("c",2,rank:difference=="rank" ? UnitRank.Two : UnitRank.One,
                definition:difference=="definition" ? "evolved" : "test",stage:difference=="stage" ? 1 : 0,
                placement:difference=="unplaced" ? UnitPlacement.Unplaced : UnitPlacement.OnBench(2),owner:difference=="owner" ? "p2" : "p1");
            var before=Snapshot();Assert.That(Resolve(),Is.Empty);Assert.That(Snapshot(),Is.EqualTo(before));match.Pool.AssertConservation(match);
        }
        [Test] public void EvolvedCopiesMergeWithoutChangingDefinitionStageOrSalePrice()
        {
            var a=Add("a",0,definition:"evolved",stage:1);Add("b",1,definition:"evolved",stage:1);Add("c",2,definition:"evolved",stage:1);
            Resolve();Assert.That(a.DefinitionId,Is.EqualTo("evolved"));Assert.That(a.EvolutionStage,Is.EqualTo(1));
            Assert.That(trades.SalePrice(a),Is.EqualTo(3));Assert.That(trades.Sell(match,"p1","a"),Is.EqualTo(3));Assert.That(match.Pool.GetStock("test").Held,Is.Zero);
        }
        [Test] public void UnpooledGiftsAndTrackedCopiesReturnOnlyAcquiredStock()
        {
            var a=Add("a",0,tracked:false);Add("b",1,tracked:false);Add("c",2);Resolve();
            Assert.That(a.Rank,Is.EqualTo(UnitRank.Two));Assert.That(a.PoolOriginDefinitionId,Is.EqualTo("test"));Assert.That(match.Pool.GetStock("test").Held,Is.EqualTo(1));
            trades.Sell(match,"p1","a");Assert.That(match.Pool.GetStock("test").Held,Is.Zero);match.Pool.AssertConservation(match);
        }
        [Test] public void UnpooledMatchCanMerge()
        {
            match=MatchStateFactory.Create("m2",new[]{"p1","p2"});player=match.GetPlayer("p1");match.TransitionTo(MatchPhase.Starting);match.TransitionTo(MatchPhase.Preparation);
            var a=Add("a",0,tracked:false);Add("b",1,tracked:false);Add("c",2,tracked:false);Resolve();Assert.That(player.Units.Single(),Is.SameAs(a));Assert.That(a.Rank,Is.EqualTo(UnitRank.Two));
        }
        [TestCase("stale")] [TestCase("combat")] [TestCase("hp")] [TestCase("revision")]
        public void InvalidResolveLeavesStateUntouched(string reason)
        {
            Add("a",0);Add("b",1);Add("c",2);long revision=player.PlacementRevision;
            if(reason=="combat") match.TransitionTo(MatchPhase.Combat);
            if(reason=="hp") player.SetHP(0);
            if(reason=="revision") { typeof(PlayerState).GetField("<PlacementRevision>k__BackingField",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(player,long.MaxValue);revision=long.MaxValue; }
            if(reason=="stale") revision--;var before=Snapshot();
            Assert.That(()=>ranks.Resolve(match,"p1",revision),Throws.Exception);Assert.That(Snapshot(),Is.EqualTo(before));match.Pool.AssertConservation(match);
        }
        [TestCase("gold")] [TestCase("shop")] [TestCase("placement")] [TestCase("stock")] [TestCase("items")]
        public void FailedMergingBuyIsAtomic(string reason)
        {
            Add("a",0,items:new[]{"item"});Add("b",1,items:reason=="items" ? new[]{"item"} : null);long revision=player.Shop.Revision;
            if(reason=="gold") player.SetProgress(0,0,1);
            if(reason=="shop") revision--;
            if(reason=="placement") typeof(PlayerState).GetField("<PlacementRevision>k__BackingField",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(player,long.MaxValue);
            if(reason=="stock") for(int i=0;i<998;i++) Add("reserve"+i,0,placement:UnitPlacement.Unplaced,owner:"p2");
            var before=Snapshot();Assert.That(()=>trades.Buy(match,"p1",0,revision),Throws.Exception);Assert.That(Snapshot(),Is.EqualTo(before));match.Pool.AssertConservation(match);
        }
        [Test] public void EliminationReturnsNineCopiesAfterChain()
        {
            Add("a",0,UnitRank.Two);Add("b",1,UnitRank.Two);Add("c",2);Add("d",3);trades.Buy(match,"p1",0,player.Shop.Revision);
            player.SetHP(0);SharedPoolSystem.ReleasePlayerHoldings(match,"p1");Assert.That(match.Pool.GetStock("test").Held,Is.Zero);Assert.That(player.Units,Is.Empty);match.Pool.AssertConservation(match);
        }
        [TestCase(UnitRank.One,100,10)] [TestCase(UnitRank.Two,170,17)] [TestCase(UnitRank.Three,270,27)]
        public void BattleDefaultScalesOnlyHpAndAttack(UnitRank rank,float hp,float attack)
        {
            var a=catalog.CreateUnit("a","test","p1",rank,0,UnitPlacement.Unplaced);
            var b=catalog.CreateUnit("b","test","p2",UnitRank.One,0,UnitPlacement.Unplaced);
            var state=BattleStateFactory.Create("b",1,123,30,catalog,new[]{new BattleUnitSetup(a,1,new BoardPosition(0,0)),new BattleUnitSetup(b,2,new BoardPosition(1,0))});
            var unit=state.Units.Single(u=>u.UnitInstanceId=="a");var stats=unit.Stats;var baseline=catalog.Get("test").BaseStats;
            Assert.That(stats.MaxHP,Is.EqualTo(hp).Within(.001));Assert.That(unit.CurrentHP,Is.EqualTo(hp).Within(.001));Assert.That(stats.Attack,Is.EqualTo(attack).Within(.001));
            Assert.That(stats.SpellPower,Is.EqualTo(baseline.SpellPower));Assert.That(stats.AttackSpeed,Is.EqualTo(baseline.AttackSpeed));Assert.That(stats.Armor,Is.EqualTo(baseline.Armor));
            Assert.That(stats.MagicResistance,Is.EqualTo(baseline.MagicResistance));Assert.That(stats.AttackRange,Is.EqualTo(baseline.AttackRange));Assert.That(stats.MoveSpeed,Is.EqualTo(baseline.MoveSpeed));
            Assert.That(stats.MaxEnergy,Is.EqualTo(baseline.MaxEnergy));Assert.That(stats.StartingEnergy,Is.EqualTo(baseline.StartingEnergy));Assert.That(stats.AttackHitTime,Is.EqualTo(baseline.AttackHitTime));
            Assert.That(stats.AttackDelivery,Is.EqualTo(baseline.AttackDelivery));Assert.That(stats.ProjectileSpeed,Is.EqualTo(baseline.ProjectileSpeed));Assert.That(stats.CritChance,Is.EqualTo(baseline.CritChance));
            Assert.That(stats.CritMultiplier,Is.EqualTo(baseline.CritMultiplier));Assert.That(stats.EnergyLockSeconds,Is.EqualTo(baseline.EnergyLockSeconds));Assert.That(baseline.MaxHP,Is.EqualTo(100));
        }
        [Test] public void RankStatOverflowRejectsBeforeMerging()
        {
            catalog=new PokemonCatalog(new[]{Definition("test",float.MaxValue)});ranks=new RankUpSystem(catalog);
            Add("a",0);Add("b",1);var before=Snapshot();
            Assert.Throws<ArgumentOutOfRangeException>(()=>tradesWithOverflow().Buy(match,"p1",0,player.Shop.Revision));Assert.That(Snapshot(),Is.EqualTo(before));
        }
        private ShopTransactionSystem tradesWithOverflow() => new ShopTransactionSystem(catalog);
        [Test] public void RankedBattleTerminatesAndReplaysAfterRegistrationOrderReversal()
        {
            string Run(bool reverse)
            {
                var setup=new[]{new BattleUnitSetup(catalog.CreateUnit("a","test","p1",UnitRank.Three,0,UnitPlacement.Unplaced),1,new BoardPosition(0,0)),new BattleUnitSetup(catalog.CreateUnit("b","test","p2",UnitRank.One,0,UnitPlacement.Unplaced),2,new BoardPosition(1,0))};
                var state=BattleStateFactory.Create("b",1,123,30,catalog,reverse ? setup.Reverse() : setup);var simulation=new BattleSimulation(state);
                for(int i=0;i<1400 && state.Result==BattleResult.InProgress;i++) simulation.Step();
                Assert.That(state.Result,Is.EqualTo(BattleResult.TeamOneWin));return state.Result+":"+state.EndTick+":"+string.Join(",",state.Units.OrderBy(u=>u.UnitInstanceId).Select(u=>u.CurrentHP));
            }
            Assert.That(Run(true),Is.EqualTo(Run(false)));
        }
        [Test] public void CustomFinalStatsResolverIsNotScaledTwice()
        {
            var setup=new[]{new BattleUnitSetup(catalog.CreateUnit("a","test","p1",UnitRank.Three,0,UnitPlacement.Unplaced),1,new BoardPosition(0,0)),new BattleUnitSetup(catalog.CreateUnit("b","test","p2",UnitRank.One,0,UnitPlacement.Unplaced),2,new BoardPosition(1,0))};
            var custom=new PokemonStats(999,33,0,1,0,0,1,2,100,0,.25f);var state=BattleStateFactory.Create("b",1,123,30,catalog,setup,(u,d)=>custom);
            Assert.That(state.Units[0].Stats,Is.SameAs(custom));
        }
        [Test] public void ExistingBattleSnapshotDoesNotChangeAfterRankUp()
        {
            var a=Add("a",0,placement:UnitPlacement.OnBoard(new BoardPosition(0,0)));Add("b",0);Add("c",1);
            var enemy=catalog.CreateUnit("enemy","test","p2",UnitRank.One,0,UnitPlacement.Unplaced);
            var state=BattleStateFactory.Create("b",1,123,30,catalog,new[]{new BattleUnitSetup(a,1,new BoardPosition(0,0)),new BattleUnitSetup(enemy,2,new BoardPosition(1,0))});
            Resolve();Assert.That(a.Rank,Is.EqualTo(UnitRank.Two));Assert.That(state.Units[0].Rank,Is.EqualTo(UnitRank.One));Assert.That(state.Units[0].Stats.MaxHP,Is.EqualTo(100));
        }
    }
}
