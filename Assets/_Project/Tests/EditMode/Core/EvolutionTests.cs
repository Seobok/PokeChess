using System;
using System.Linq;
using NUnit.Framework;
using PokeChess.Core.Battle;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Tests
{
    public sealed class EvolutionTests
    {
        private static PokemonCatalog Catalog() => new PokemonCatalog(new[] {
            Definition("bulbasaur",100,"ivysaur"), Definition("ivysaur",125,"venusaur"), Definition("venusaur",160)
        });
        private static PokemonDefinition Definition(string id,float hp,string next=null) => new PokemonDefinition(id,id,1,
            new PokemonStats(hp,10,0,1,0,0,1,2,60,0,.2f),"role","growth",
            evolutions:next==null?null:new[]{new EvolutionOption(next,1)});
        private static (MatchState m, PokemonCatalog c, LocalRoundCoordinator rounds) Fixture(int round=5)
        {
            var c=Catalog(); var m=MatchStateFactory.CreateWithPool("evolution",new[]{"p1","p2"},c,new[]{"bulbasaur"},matchSeed:123);
            m.TransitionTo(MatchPhase.Starting);m.TransitionTo(MatchPhase.Preparation);
            foreach(var p in m.Players)new ShopSystem().RefreshForRound(m,p.PlayerId);
            var rounds=new LocalRoundCoordinator(m,c);
            while(m.RoundNumber<round){Assert.That(rounds.StartCombat(m.RoundNumber),Is.True);Assert.That(rounds.NextRound(m.RoundNumber),Is.True);}
            // This fixture intentionally stocks only Cost 1. Isolate form replacement from cost odds.
            foreach(var p in m.Players){p.SetProgress(p.Gold,0,1);new ShopSystem().Reroll(m,p.PlayerId);}
            return(m,c,rounds);
        }
        private static UnitInstance Add(MatchState m,PokemonCatalog c,string id="a",UnitRank rank=UnitRank.One,UnitPlacement? placement=null,string[] items=null)
            => SharedPoolSystem.RegisterUnit(m,c.CreateUnit(id,"bulbasaur","p1",rank,0,placement??UnitPlacement.OnBench(0),items),"bulbasaur");
        private static UnitInstance Evolve(MatchState m,PokemonCatalog c,string id="a")
        {var p=m.GetPlayer("p1");return new EvolutionSystem(c).Evolve(m,"p1",id,p.EvolutionRevision,p.PlacementRevision);}

        [Test] public void PreparationGrantsAtFiveAndTenOnceToEveryLivingPlayer()
        {
            var f=Fixture(4);foreach(var p in f.m.Players)Assert.That(p.EvolutionPoints,Is.Zero);
            f.rounds.StartCombat(4);f.rounds.NextRound(4);
            foreach(var p in f.m.Players){Assert.That(p.EvolutionPoints,Is.EqualTo(1));Assert.That(p.LastEvolutionPointRound,Is.EqualTo(5));}
            Assert.That(EvolutionPointRules.GrantForPreparation(f.m,"p1"),Is.False);
            new LocalRoundCoordinator(f.m,f.c);Assert.That(f.m.GetPlayer("p1").EvolutionPoints,Is.EqualTo(1));
            while(f.m.RoundNumber<10){f.rounds.StartCombat(f.m.RoundNumber);f.rounds.NextRound(f.m.RoundNumber);}
            foreach(var p in f.m.Players)Assert.That(p.EvolutionPoints,Is.EqualTo(2));
            Assert.That(EvolutionPointRules.NextGrantRound(10),Is.EqualTo(15));
        }
        [Test] public void EliminatedPlayersCannotReceivePoints()
        {var f=Fixture(4);f.m.GetPlayer("p1").SetHP(0);f.m.TransitionTo(MatchPhase.Combat);f.m.TransitionTo(MatchPhase.Result);f.m.TransitionTo(MatchPhase.Preparation);Assert.That(EvolutionPointRules.GrantForPreparation(f.m,"p1"),Is.False);}
        [TestCase(PlacementKind.Board)] [TestCase(PlacementKind.Bench)]
        public void EvolutionPreservesOwnedReferenceRankItemsPlacementAndPool(PlacementKind kind)
        {
            var f=Fixture();var place=kind==PlacementKind.Board?UnitPlacement.OnBoard(new BoardPosition(0,0)):UnitPlacement.OnBench(0);
            var unit=Add(f.m,f.c,rank:UnitRank.Two,placement:place,items:new[]{"item:choice-scarf:1:p1"});
            var before=f.m.Pool.GetStock("bulbasaur").Available;var p=f.m.GetPlayer("p1");long revision=p.PlacementRevision;
            var evolved=Evolve(f.m,f.c);Assert.That(evolved,Is.SameAs(unit));Assert.That(unit.DefinitionId,Is.EqualTo("ivysaur"));
            Assert.That(unit.EvolutionStage,Is.EqualTo(1));Assert.That(unit.Rank,Is.EqualTo(UnitRank.Two));Assert.That(unit.Placement.Kind,Is.EqualTo(kind));
            Assert.That(unit.ItemInstanceIds,Is.EqualTo(new[]{"item:choice-scarf:1:p1"}));Assert.That(unit.PoolOriginDefinitionId,Is.EqualTo("bulbasaur"));
            Assert.That(p.EvolutionPoints,Is.Zero);Assert.That(p.PlacementRevision,Is.EqualTo(revision+1));Assert.That(f.m.Pool.GetStock("bulbasaur").Available,Is.EqualTo(before));f.m.Pool.AssertConservation(f.m);
        }
        [Test] public void InsufficientPointsAndStaleRevisionLeaveStateUnchanged()
        {
            var f=Fixture(4);var unit=Add(f.m,f.c);var p=f.m.GetPlayer("p1");var system=new EvolutionSystem(f.c);long revision=p.PlacementRevision;
            Assert.Throws<InvalidOperationException>(()=>system.Evolve(f.m,"p1","a",p.EvolutionRevision,revision));
            Assert.That(unit.DefinitionId,Is.EqualTo("bulbasaur"));Assert.That(p.PlacementRevision,Is.EqualTo(revision));
            f.m.TransitionTo(MatchPhase.Combat);f.m.TransitionTo(MatchPhase.Result);f.m.TransitionTo(MatchPhase.Preparation);EvolutionPointRules.GrantForPreparation(f.m,"p1");
            Assert.Throws<InvalidOperationException>(()=>system.Evolve(f.m,"p1","a",p.EvolutionRevision-1,revision));
            Assert.Throws<InvalidOperationException>(()=>system.Evolve(f.m,"p1","a",p.EvolutionRevision,revision-1));
            Assert.That(p.EvolutionPoints,Is.EqualTo(1));Assert.That(p.ShopEvolutions,Is.Empty);
        }
        [Test] public void PreviewIsReadOnlyAndForeignOrCombatEvolutionIsRejected()
        {
            var f=Fixture();Add(f.m,f.c);var p=f.m.GetPlayer("p1");var system=new EvolutionSystem(f.c);
            Assert.That(system.Preview(f.m,"p1","a",p.EvolutionRevision,p.PlacementRevision).Id,Is.EqualTo("ivysaur"));Assert.That(p.EvolutionPoints,Is.EqualTo(1));
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(()=>system.Evolve(f.m,"p2","a",f.m.GetPlayer("p2").EvolutionRevision,0));
            f.m.TransitionTo(MatchPhase.Combat);Assert.Throws<InvalidOperationException>(()=>Evolve(f.m,f.c));Assert.That(p.EvolutionPoints,Is.EqualTo(1));
        }
        [Test] public void RefreshReplacesOnlyOwnersShopAndBuyingConsumesBasePool()
        {
            var f=Fixture();Add(f.m,f.c);var p=f.m.GetPlayer("p1");var revision=p.Shop.Revision;var random=p.Shop.RandomState;var draws=p.Shop.RandomDrawCount;Evolve(f.m,f.c);
            Assert.That(p.Shop.Slots.All(s=>s.DefinitionId=="ivysaur"&&s.EvolutionStage==1&&s.PoolOriginDefinitionId=="bulbasaur"),Is.True);
            Assert.That(p.Shop.Revision,Is.EqualTo(revision+1));Assert.That(p.Shop.RandomState,Is.EqualTo(random));Assert.That(p.Shop.RandomDrawCount,Is.EqualTo(draws));
            Assert.Throws<InvalidOperationException>(()=>new ShopTransactionSystem(f.c).Buy(f.m,"p1",0,revision));
            Assert.That(f.m.GetPlayer("p2").Shop.Slots.All(s=>s.DefinitionId=="bulbasaur"),Is.True);
            var trades=new ShopTransactionSystem(f.c);var bought=trades.Buy(f.m,"p1",0,p.Shop.Revision);
            Assert.That(bought.DefinitionId,Is.EqualTo("ivysaur"));Assert.That(bought.EvolutionStage,Is.EqualTo(1));Assert.That(bought.PoolOriginDefinitionId,Is.EqualTo("bulbasaur"));f.m.Pool.AssertConservation(f.m);
            trades.Sell(f.m,"p1",bought.InstanceId);trades.Sell(f.m,"p1","a");Assert.That(p.ShopEvolutions["bulbasaur"].Stage,Is.EqualTo(1));f.m.Pool.AssertConservation(f.m);
        }
        [Test] public void LockedOffersEvolveImmediatelyAndStayEvolvedNextRound()
        {
            var f=Fixture();Add(f.m,f.c);var p=f.m.GetPlayer("p1");new ShopSystem().SetLocked(f.m,"p1",true);Evolve(f.m,f.c);
            // Empty both boards so advancing the fixture stays deterministic.
            f.rounds.StartCombat(5);f.rounds.NextRound(5);
            Assert.That(p.Shop.IsLocked,Is.True);
            Assert.That(p.Shop.Slots.All(s=>s.DefinitionId=="ivysaur"&&s.EvolutionStage==1),Is.True);
            var unit=new ShopTransactionSystem(f.c).Buy(f.m,"p1",0,p.Shop.Revision);Assert.That(unit.DefinitionId,Is.EqualTo("ivysaur"));Assert.That(unit.EvolutionStage,Is.EqualTo(1));
            new ShopSystem().Reroll(f.m,"p1");Assert.That(p.Shop.Slots.All(s=>s.DefinitionId=="ivysaur"),Is.True);
        }
        [Test] public void EvolutionDoesNotRefillAlreadyPurchasedShopSlot()
        {
            var f=Fixture();var p=f.m.GetPlayer("p1");
            var bought=new ShopTransactionSystem(f.c).Buy(f.m,"p1",0,p.Shop.Revision);
            new EvolutionSystem(f.c).Evolve(f.m,"p1",bought.InstanceId,p.EvolutionRevision,p.PlacementRevision);
            Assert.That(p.Shop.Slots[0].IsEmpty,Is.True);
            Assert.That(p.Shop.Slots.Skip(1).All(s=>s.DefinitionId=="ivysaur"&&s.Cost==1&&s.EvolutionStage==1),Is.True);
            f.m.Pool.AssertConservation(f.m);
        }
        [TestCase(PlacementKind.Board)] [TestCase(PlacementKind.Bench)]
        public void EvolutionAlsoUpdatesSameFormBoardAndBenchCopiesWithOnePoint(PlacementKind selectedPlacement)
        {
            var f=Fixture(10);var p=f.m.GetPlayer("p1");p.SetProgress(100,0,2);
            var selected=Add(f.m,f.c,placement:selectedPlacement==PlacementKind.Board?UnitPlacement.OnBoard(new BoardPosition(0,0)):UnitPlacement.OnBench(0));
            var copy=Add(f.m,f.c,"copy",UnitRank.Two,UnitPlacement.OnBench(1),new[]{"item:choice-scarf:99:p1"});
            var field=Add(f.m,f.c,"field",placement:UnitPlacement.OnBoard(new BoardPosition(1,0)));
            var enemy=SharedPoolSystem.RegisterUnit(f.m,f.c.CreateUnit("enemy","bulbasaur","p2",UnitRank.One,0,UnitPlacement.OnBench(0)),"bulbasaur");
            var originalStock=f.m.Pool.GetStock("bulbasaur").Available;var revision=p.PlacementRevision;
            Evolve(f.m,f.c);
            Assert.That(selected.DefinitionId,Is.EqualTo("ivysaur"));Assert.That(copy.DefinitionId,Is.EqualTo("ivysaur"));
            Assert.That(copy.EvolutionStage,Is.EqualTo(1));Assert.That(copy.Rank,Is.EqualTo(UnitRank.Two));
            Assert.That(copy.Placement.BenchSlot,Is.EqualTo(1));Assert.That(copy.ItemInstanceIds,Is.EqualTo(new[]{"item:choice-scarf:99:p1"}));
            Assert.That(copy.PoolOriginDefinitionId,Is.EqualTo("bulbasaur"));
            Assert.That(field.DefinitionId,Is.EqualTo("ivysaur"));Assert.That(field.EvolutionStage,Is.EqualTo(1));
            Assert.That(field.Placement.Position,Is.EqualTo(new BoardPosition(1,0)));Assert.That(enemy.DefinitionId,Is.EqualTo("bulbasaur"));
            Assert.That(p.EvolutionPoints,Is.EqualTo(1));Assert.That(p.PlacementRevision,Is.EqualTo(revision+1));
            Evolve(f.m,f.c);
            Assert.That(copy.DefinitionId,Is.EqualTo("venusaur"));Assert.That(copy.EvolutionStage,Is.EqualTo(2));
            Assert.That(field.DefinitionId,Is.EqualTo("venusaur"));Assert.That(field.EvolutionStage,Is.EqualTo(2));
            Assert.That(p.EvolutionPoints,Is.Zero);Assert.That(f.m.Pool.GetStock("bulbasaur").Available,Is.EqualTo(originalStock));
            f.m.Pool.AssertConservation(f.m);
        }
        [Test] public void FinalEvolutionDoesNotRegressWhenAnotherBaseUnitEvolves()
        {
            var f=Fixture(15);Add(f.m,f.c);Evolve(f.m,f.c);Evolve(f.m,f.c);Add(f.m,f.c,"b",placement:UnitPlacement.OnBench(1));
            Assert.That(f.m.GetPlayer("p1").GetUnit("a").DefinitionId,Is.EqualTo("venusaur"));
            Evolve(f.m,f.c,"b");Assert.That(f.m.GetPlayer("p1").ShopEvolutions["bulbasaur"].DefinitionId,Is.EqualTo("venusaur"));
            Assert.That(f.m.GetPlayer("p1").Shop.Slots.All(s=>s.DefinitionId=="venusaur"&&s.EvolutionStage==2),Is.True);
            Assert.Throws<InvalidOperationException>(()=>Evolve(f.m,f.c));f.m.Pool.AssertConservation(f.m);
        }
        [Test] public void EvolvedShopCopiesMergeWithoutChangingFamilyPool()
        {
            var f=Fixture();Add(f.m,f.c);var p=f.m.GetPlayer("p1");Evolve(f.m,f.c);new ShopSystem().Reroll(f.m,"p1");var trades=new ShopTransactionSystem(f.c);
            trades.Buy(f.m,"p1",0,p.Shop.Revision);var result=trades.BuyWithRankUp(f.m,"p1",1,p.Shop.Revision);
            Assert.That(result.Unit.Rank,Is.EqualTo(UnitRank.Two));Assert.That(result.Unit.DefinitionId,Is.EqualTo("ivysaur"));Assert.That(result.Unit.EvolutionStage,Is.EqualTo(1));
            f.m.Pool.AssertConservation(f.m);trades.Sell(f.m,"p1",result.Unit.InstanceId);Assert.That(f.m.Pool.GetStock("bulbasaur").Available,Is.EqualTo(f.m.Pool.GetStock("bulbasaur").Initial));
        }
        [Test] public void NextBattleUsesEvolvedStatsAndStage()
        {
            var f=Fixture();Add(f.m,f.c);Evolve(f.m,f.c);
            SharedPoolSystem.RegisterUnit(f.m,f.c.CreateUnit("enemy","bulbasaur","p2",UnitRank.One,0,UnitPlacement.OnBench(0)),"bulbasaur");
            f.rounds.StartCombat(5);var unit=f.rounds.Battle.Units.Single(u=>u.UnitInstanceId=="a");Assert.That(unit.DefinitionId,Is.EqualTo("ivysaur"));Assert.That(unit.EvolutionStage,Is.EqualTo(1));Assert.That(unit.Stats.MaxHP,Is.EqualTo(125));
        }
        [Test] public void HostRetryDeduplicatesEvolutionAndPublishesOwnerAndPublicState()
        {
            var f=Fixture();Add(f.m,f.c,placement:UnitPlacement.OnBoard(new BoardPosition(0,0)));var host=new HostCommandProcessor(f.m,f.c,f.rounds);var p=f.m.GetPlayer("p1");
            var command=new MatchCommand{matchId=f.m.MatchId,playerId="p1",commandId="evolve-1",sequence=1,kind=MatchCommandKind.Evolve,round=5,phase=MatchPhase.Preparation,evolutionRevision=p.EvolutionRevision,placementRevision=p.PlacementRevision,unitId="a"};
            var ack=host.Process("p1",command);Assert.That(ack.accepted,Is.True);Assert.That(host.Process("p1",command),Is.SameAs(ack));
            Assert.That(p.EvolutionPoints,Is.Zero);Assert.That(ack.state.units.Single().evolutionStage,Is.EqualTo(1));Assert.That(ack.state.shopEvolutions.Single().definitionId,Is.EqualTo("ivysaur"));
            var snapshot=host.FullSnapshot("p1");Assert.That(snapshot.publicState.players.Single(v=>v.id=="p1").board.Single().definitionId,Is.EqualTo("ivysaur"));
            Assert.That(new MatchSnapshotStore().Apply(snapshot,f.m.MatchId,"p1"),Is.True);
            var conflict=command.Copy();conflict.evolutionRevision++;Assert.That(host.Process("p1",conflict).code,Is.EqualTo("DuplicateConflict"));
        }
        [TestCase("slowpoke", "slowbro")]
        [TestCase("mankey", "primeape")]
        [TestCase("weedle", "kakuna", "beedrill")]
        [TestCase("geodude", "graveler", "golem")]
        [TestCase("gastly", "haunter", "gengar")]
        [TestCase("caterpie", "metapod", "butterfree")]
        [TestCase("magnemite", "magneton")]
        [TestCase("abra", "kadabra", "alakazam")]
        public void AdditionalFamiliesEvolveAndReplaceOnlyOwnersShop(string root,string first,string final=null)
        {
            var c=PrototypeRoster.CreateCatalog();
            var m=MatchStateFactory.CreateWithPool("additional-evolution",new[]{"p1","p2"},c,new[]{root},matchSeed:123);
            m.TransitionTo(MatchPhase.Starting);m.TransitionTo(MatchPhase.Preparation);
            foreach(var player in m.Players)new ShopSystem().RefreshForRound(m,player.PlayerId);
            while(m.RoundNumber<10)
            {
                m.TransitionTo(MatchPhase.Combat);m.TransitionTo(MatchPhase.Result);m.TransitionTo(MatchPhase.Preparation);
                EvolutionPointRules.GrantForPreparation(m,"p1");
                foreach(var player in m.Players)new ShopSystem().RefreshForRound(m,player.PlayerId);
            }
            var p=m.GetPlayer("p1");p.SetProgress(100,0,c.Get(root).Cost==1?1:10);
            var unit=SharedPoolSystem.RegisterUnit(m,c.CreateUnit("extra",root,"p1",UnitRank.Two,0,UnitPlacement.OnBench(0)),root);
            var system=new EvolutionSystem(c);
            var costOdds=new int[5];costOdds[c.Get(root).Cost-1]=100;
            var shop=ShopSystem.ForSharedPool(new ShopRules(Enumerable.Range(0,10).Select(_=>costOdds)));
            var forms=final==null?new[]{first}:new[]{first,final};
            for(int i=0;i<forms.Length;i++)
            {
                system.Evolve(m,"p1",unit.InstanceId,p.EvolutionRevision,p.PlacementRevision);
                Assert.That(unit.DefinitionId,Is.EqualTo(forms[i]));
                Assert.That(unit.EvolutionStage,Is.EqualTo(i+1));
                Assert.That(unit.Rank,Is.EqualTo(UnitRank.Two));
                Assert.That(PrototypeEvolutionRoster.BaseForm(forms[i]),Is.EqualTo(root));
                Assert.That(PrototypeEvolutionRoster.Stage(forms[i]),Is.EqualTo(i+1));
                Assert.That(c.Get(forms[i]).SkillId,Is.EqualTo(c.Get(root).SkillId));
                Assert.That(c.Get(forms[i]).BaseStats.MaxHP,Is.GreaterThan(c.Get(root).BaseStats.MaxHP));
                shop.Reroll(m,"p1");
                var offers=p.Shop.Slots.Where(s=>!s.IsEmpty).ToArray();
                Assert.That(offers,Is.Not.Empty);
                Assert.That(offers.All(s=>s.DefinitionId==forms[i]&&s.PoolOriginDefinitionId==root),Is.True);
                m.Pool.AssertConservation(m);
            }
            Assert.That(c.Get(unit.DefinitionId).Evolutions,Is.Empty);
            Assert.That(m.GetPlayer("p2").ShopEvolutions,Is.Empty);
            Assert.That(p.EvolutionPoints,Is.EqualTo(2-forms.Length));
        }
        [Test] public void PrototypeCatalogHasSinglePathsAndOnlyBasesEnterInitialPool()
        {
            var c=PrototypeRoster.CreateCatalog();foreach(var id in new[]{"bulbasaur","squirtle","charmander"}){
                var one=c.Get(id);var two=c.Get(one.Evolutions.Single().TargetDefinitionId);var three=c.Get(two.Evolutions.Single().TargetDefinitionId);
                Assert.That(three.Evolutions,Is.Empty);Assert.That(three.Cost,Is.EqualTo(one.Cost));Assert.That(three.BaseStats.MaxHP,Is.GreaterThan(two.BaseStats.MaxHP));
                Assert.That(three.SkillId,Is.EqualTo(one.SkillId));Assert.That(three.TraitIds,Is.EqualTo(one.TraitIds));
            }
            Assert.That(PrototypeRoster.CreateDefinitions().Length,Is.EqualTo(12));
        }
    }
}
