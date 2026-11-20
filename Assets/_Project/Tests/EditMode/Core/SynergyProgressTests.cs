using System;
using System.Linq;
using NUnit.Framework;
using PokeChess.Core.Battle;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;
namespace PokeChess.Core.Tests
{
    public sealed class SynergyProgressTests
    {
        private static PokemonDefinition D(string id,params string[] traits)=>new PokemonDefinition(id,id,1,new PokemonStats(1000,100,100,.5f,0,0,1,2,100,0,.2f),"test","test",traitIds:traits);
        private static MatchState Match(out PokemonCatalog catalog,params PokemonDefinition[] defs)
        {catalog=new PokemonCatalog(defs.Length>0?defs:new[]{D("caterpie",SynergyTraits.Mystic),D("squirtle",SynergyTraits.Torrent)});var m=MatchStateFactory.CreateWithPool("synergy-progress",new[]{"p1","p2"},catalog,(defs.Length>0?defs.Select(d=>d.Id):new[]{"caterpie","squirtle"}));m.TransitionTo(MatchPhase.Starting);m.TransitionTo(MatchPhase.Preparation);return m;}
        private static LocalPairBattle Pair(string trait,int count,bool wild=false)
        {var pair=new LocalPairBattle(new RoundPairing("p1","p2",isWild:wild),null,BattleResult.TeamOneWin);pair.OneSynergies=Array.AsReadOnly(SynergyTraits.All.Select(t=>new SynergyCount(t,t==trait?count:0)).ToArray());return pair;}
        [Test] public void MysticUsesStartingSnapshotAndSettlesEachRoundOnlyOnce(){var m=Match(out _);var p=m.GetPlayer("p1");var pair=Pair(SynergyTraits.Mystic,2);SynergyPreparation.Settle(p,pair,1,RoundOutcome.Loss);Assert.That(p.Synergy.FreeRerollRound,Is.Zero);SynergyPreparation.Settle(p,pair,1,RoundOutcome.Loss);Assert.That(p.Synergy.MysticBattles,Is.EqualTo(1));SynergyPreparation.Settle(p,pair,2,RoundOutcome.Loss);Assert.That(p.Synergy.FreeRerollRound,Is.EqualTo(3));}
        [Test] public void MysticFourGrantsNextPreparationFreeRollAfterEmptyOpponentBattle(){var m=Match(out _);var p=m.GetPlayer("p1");SynergyPreparation.Settle(p,Pair(SynergyTraits.Mystic,4),1,RoundOutcome.Win);Assert.That(p.Synergy.FreeRerollRound,Is.EqualTo(2));}
        [Test] public void FreeRerollWorksAtZeroGoldAndCannotBeReused(){var m=Match(out _);var p=m.GetPlayer("p1");p.SetProgress(0,0,1);var shop=new ShopSystem();shop.RefreshForRound(m,"p1");p.Synergy.FreeRerollRound=1;Assert.DoesNotThrow(()=>shop.Reroll(m,"p1"));Assert.That(p.Gold,Is.Zero);Assert.That(p.Synergy.FreeRerollRound,Is.Zero);Assert.Throws<InvalidOperationException>(()=>shop.Reroll(m,"p1"));}
        [Test] public void PendingFreeRerollCannotBeUsedBeforeItsAwardRound(){var m=Match(out _);var p=m.GetPlayer("p1");p.SetProgress(0,0,1);new ShopSystem().RefreshForRound(m,"p1");p.Synergy.FreeRerollRound=2;Assert.Throws<InvalidOperationException>(()=>new ShopSystem().Reroll(m,"p1"));}

        [Test]
        public void MysticRerollCarriesOverAndChargingRestartsOnlyAfterUse()
        {
            var match = Match(out _);
            var player = match.GetPlayer("p1");
            var shop = new ShopSystem();
            player.SetProgress(0, 0, 1);
            shop.RefreshForRound(match, "p1");
            var two = Pair(SynergyTraits.Mystic, 2);
            SynergyPreparation.Settle(player, Pair(SynergyTraits.Mystic, 6), 1, RoundOutcome.Win);
            for (int round = 2; round <= 4; round++)
            {
                match.TransitionTo(MatchPhase.Combat);
                match.TransitionTo(MatchPhase.Result);
                match.TransitionTo(MatchPhase.Preparation);
                shop.RefreshForRound(match, "p1");
                if (round < 4)
                    SynergyPreparation.Settle(player, round == 2 ? two : Pair(SynergyTraits.Mystic, 0), round, RoundOutcome.Win);
            }

            Assert.That(player.Synergy.FreeRerollRound, Is.EqualTo(2));
            Assert.That(player.Synergy.MysticBattles, Is.EqualTo(1));
            Assert.That(player.Synergy.MysticTier, Is.EqualTo(6));
            player.Synergy.ProphecyOrigin = SynergyTraits.Torrent;
            Assert.That(shop.Reroll(match, "p1")[0].ProphecyOrigin, Is.EqualTo(SynergyTraits.Torrent));
            Assert.That(player.Synergy.MysticBattles, Is.Zero);
            Assert.Throws<InvalidOperationException>(() => shop.Reroll(match, "p1"));

            SynergyPreparation.Settle(player, two, 4, RoundOutcome.Win);
            Assert.That(player.Synergy.FreeRerollRound, Is.Zero);
            SynergyPreparation.Settle(player, two, 5, RoundOutcome.Win);
            Assert.That(player.Synergy.FreeRerollRound, Is.EqualTo(6));
        }
        [Test] public void ProphecyKeepsSelectedCostAndSharedPoolStock()
        {
            var m = Match(out var catalog);
            var p = m.GetPlayer("p1");
            var shop = new ShopSystem();
            p.Synergy.FreeRerollRound = 1;
            p.Synergy.MysticTier = 6;
            p.Synergy.ProphecyOrigin = SynergyTraits.Torrent;
            shop.RefreshForRound(m, "p1");
            Assert.That(p.Shop.Slots.All(slot => slot.ProphecyOrigin == null), Is.True);
            Assert.That(p.Synergy.FreeRerollRound, Is.EqualTo(1));

            int before = m.Pool.TotalAvailable(1);
            var slots = shop.Reroll(m, "p1");
            Assert.That(slots[0].DefinitionId, Is.EqualTo("squirtle"));
            Assert.That(slots[0].Cost, Is.EqualTo(1));
            Assert.That(slots[0].ProphecyOrigin, Is.EqualTo(SynergyTraits.Torrent));
            Assert.That(slots.Skip(1).All(slot => slot.ProphecyOrigin == null), Is.True);
            Assert.That(m.Pool.TotalAvailable(1), Is.EqualTo(before));
            var owner = new HostCommandProcessor(m, catalog, new LocalRoundCoordinator(m, catalog)).Snapshot("p1");
            Assert.That(owner.shop[0].prophecyOrigin, Is.EqualTo(SynergyTraits.Torrent));
            Assert.That(owner.freeRerollConsumedRound, Is.EqualTo(1));
            Assert.That(shop.Reroll(m, "p1").All(slot => slot.ProphecyOrigin == null), Is.True);
        }

        [Test] public void MysticFallbackConsumesFreeOpportunityWithoutFavoringCard()
        {
            var m = Match(out _);
            var p = m.GetPlayer("p1");
            var shop = new ShopSystem();
            p.Synergy.FreeRerollRound = 1;
            p.Synergy.MysticTier = 6;
            p.Synergy.ProphecyOrigin = SynergyTraits.Storm;
            shop.RefreshForRound(m, "p1");
            int gold = p.Gold;
            Assert.That(shop.Reroll(m, "p1").All(slot => slot.ProphecyOrigin == null), Is.True);
            Assert.That(p.Synergy.FreeRerollConsumedRound, Is.EqualTo(1));
            Assert.That(p.Gold, Is.EqualTo(gold));
        }
        [Test] public void WildBattlesNeverAdvanceEconomyOrGrowth(){var m=Match(out _);var p=m.GetPlayer("p1");SynergyPreparation.Settle(p,Pair(SynergyTraits.Mystic,6,true),1,RoundOutcome.Win);Assert.That(p.Synergy.MysticBattles,Is.Zero);Assert.That(p.Synergy.FreeRerollRound,Is.Zero);Assert.That(p.Synergy.VenomSamples,Is.Zero);}
        [Test]
        public void AdversityOnlyAccumulatesAndPaysWhenActive()
        {
            var match = Match(out _);
            var player = match.GetPlayer("p1");
            int startingGold = player.Gold;
            var active = Pair(SynergyTraits.Adversity, 4);
            SynergyPreparation.Settle(player, active, 1, RoundOutcome.Loss);
            Assert.That(player.Synergy.Resolve, Is.EqualTo(2));
            Assert.That(player.Synergy.LastAdversitySettlement.resolveDelta, Is.EqualTo(2));
            SynergyPreparation.Settle(player, Pair(SynergyTraits.Adversity, 0), 2, RoundOutcome.Win);
            Assert.That(player.Synergy.Resolve, Is.EqualTo(2));
            Assert.That(player.Synergy.LastAdversitySettlement.round, Is.EqualTo(1));
            SynergyPreparation.Settle(player, active, 3, RoundOutcome.Win);
            SynergyPreparation.Settle(player, active, 3, RoundOutcome.Win);
            Assert.That(player.Synergy.Resolve, Is.Zero);
            Assert.That(player.Gold, Is.EqualTo(startingGold + 4));
            Assert.That(player.ItemInventory.Count, Is.EqualTo(1));
            var settlement = player.Synergy.LastAdversitySettlement;
            Assert.That(settlement.round, Is.EqualTo(3));
            Assert.That(settlement.resolveDelta, Is.EqualTo(-2));
            Assert.That(settlement.gold, Is.EqualTo(4));
            Assert.That(settlement.berry, Is.True);
            Assert.That(settlement.choice, Is.False);
        }

        [Test]
        public void AdversitySixDefersChoiceAndConsumesItOnce()
        {
            var match = Match(out var catalog);
            var player = match.GetPlayer("p1");
            player.Synergy.Resolve = 7;
            SynergyPreparation.Settle(player, Pair(SynergyTraits.Adversity, 6), 1, RoundOutcome.Win);
            Assert.That(player.Synergy.RewardChoices, Is.EqualTo(1));
            Assert.That(player.Synergy.LastAdversitySettlement.choice, Is.True);
            Assert.That(player.Synergy.LastAdversitySettlement.gold, Is.Zero);
            Assert.That(player.Synergy.LastAdversitySettlement.berry, Is.False);
            SynergyPreparation.Configure(match, catalog, "p1", 3, null, null, 2);
            Assert.That(player.Synergy.EmblemFragments, Is.EqualTo(1));
            Assert.That(player.ItemInventory, Is.Empty);
            Assert.Throws<InvalidOperationException>(() => SynergyPreparation.Configure(match, catalog, "p1", 3, null, null, 0));
            Assert.That(player.Synergy.LastAdversityChoice.slot, Is.EqualTo(2));
            Assert.That(player.Synergy.LastAdversityChoice.sequence, Is.EqualTo(1));
        }
        [Test] public void TextureAndEmblemsOnlyAddOriginsAndCountFamiliesOnce(){var m=Match(out var catalog,D("porygon",SynergyTraits.Mystic,SynergyTraits.Invoker),D("squirtle",SynergyTraits.Torrent,SynergyTraits.Guardian));var p=m.GetPlayer("p1");p.SetProgress(0,0,2);foreach(var id in new[]{"porygon","squirtle"})SharedPoolSystem.RegisterUnit(m,catalog.CreateUnit(id,id,"p1",UnitRank.One,0,UnitPlacement.OnBoard(new BoardPosition(id=="porygon"?0:1,0))),id);SynergyPreparation.Configure(m,catalog,"p1",0,"porygon","squirtle");var counts=SynergyPreparation.Count(p,catalog);Assert.That(counts.Single(c=>c.TraitId==SynergyTraits.Torrent).Count,Is.EqualTo(2));Assert.That(counts.Single(c=>c.TraitId==SynergyTraits.Guardian).Count,Is.EqualTo(1));p.GetUnit("porygon").AddItem("item:emblem-torrent:1:p1");Assert.That(SynergyPreparation.Count(p,catalog).Single(c=>c.TraitId==SynergyTraits.Torrent).Count,Is.EqualTo(2));}
        [Test] public void ConfigurationRejectsCombatPhase(){var m=Match(out var catalog);m.TransitionTo(MatchPhase.Combat);Assert.Throws<InvalidOperationException>(()=>SynergyPreparation.Configure(m,catalog,"p1",2,null,SynergyTraits.Flame));}
        [Test] public void RockAndUnitCanSwapInBothDirections(){var m=Match(out var catalog,D("a",SynergyTraits.Earth),D("b",SynergyTraits.Earth));m.GetPlayer("p1").SetProgress(0,0,2);foreach(var id in new[]{"a","b"})SharedPoolSystem.RegisterUnit(m,catalog.CreateUnit(id,id,"p1",UnitRank.One,0,UnitPlacement.OnBoard(new BoardPosition(id=="a"?0:1,0))),id);var player=m.GetPlayer("p1");var origin=SynergyPreparation.PreparationRocks(player,catalog).Single();
            SynergyPreparation.Configure(m,catalog,"p1",1,null,null,0,0,0);
            Assert.That(player.GetUnit("a").Placement.Position,Is.EqualTo(origin));
            Assert.That(player.Synergy.RockTiles.Single(),Is.EqualTo(new BoardPosition(0,0)));
            var result=new PlayerPlacementSystem(catalog).Move(m,"p1","a",UnitPlacement.OnBoard(new BoardPosition(0,0)),player.PlacementRevision);
            Assert.That(result.Outcome,Is.EqualTo(PlacementOutcome.Swapped));
            Assert.That(player.Synergy.RockTiles.Single(),Is.EqualTo(origin));}
        [Test] public void ActiveEarthAutomaticallyPreviewsRocksAndDragChoicePersists()
        {
            var m=Match(out var catalog,D("a",SynergyTraits.Earth),D("b",SynergyTraits.Earth),D("c",SynergyTraits.Earth),D("d",SynergyTraits.Earth));
            var p=m.GetPlayer("p1");p.SetProgress(0,0,4);
            for(int i=0;i<4;i++) {string id=((char)('a'+i)).ToString();SharedPoolSystem.RegisterUnit(m,catalog.CreateUnit(id,id,"p1",UnitRank.One,0,UnitPlacement.OnBoard(new BoardPosition(i,0))),id);}
            var rocks=SynergyPreparation.PreparationRocks(p,catalog);
            Assert.That(rocks.Length,Is.EqualTo(2));Assert.That(rocks.Distinct().Count(),Is.EqualTo(2));
            Assert.That(rocks.Any(t=>p.Units.Any(u=>u.Placement.Position.Value.Equals(t))),Is.False);
            SynergyPreparation.Configure(m,catalog,"p1",1,null,null,0,6,3);
            Assert.That(SynergyPreparation.PreparationRocks(p,catalog)[0],Is.EqualTo(new BoardPosition(6,3)));
            var secondRock = SynergyPreparation.PreparationRocks(p,catalog)[1];
            SynergyPreparation.Configure(m,catalog,"p1",1,null,null,1,6,3);
            Assert.That(SynergyPreparation.PreparationRocks(p,catalog), Is.EqualTo(new[]{secondRock,new BoardPosition(6,3)}));
            var snapshot=new HostCommandProcessor(m,catalog,new LocalRoundCoordinator(m,catalog)).Snapshot("p1");
            Assert.That(snapshot.rockTiles.Select(t=>new BoardPosition(t.column,t.row)),Is.EqualTo(SynergyPreparation.PreparationRocks(p,catalog)));
        }
        [Test] public void InactiveEarthHasNoPreparationRockDespiteStoredSelection()
        {
            var m=Match(out var catalog);var p=m.GetPlayer("p1");p.Synergy.RockTiles=new[]{new BoardPosition(2,2)};
            Assert.That(SynergyPreparation.PreparationRocks(p,catalog),Is.Empty);
        }
        [Test]
        public void GuardianPreparationPlacementIsPrivateAndMirrorsIntoBattle()
        {
            var m = Match(out var catalog, D("a", SynergyTraits.Nature, SynergyTraits.Earth),
                D("b", SynergyTraits.Nature, SynergyTraits.Earth));
            foreach (var player in m.Players)
            {
                player.SetProgress(0, 0, 2);
                new ShopSystem().RefreshForRound(m, player.PlayerId);
                int column = 0;
                foreach (var id in new[] { "a", "b" })
                    SharedPoolSystem.RegisterUnit(m, catalog.CreateUnit(player.PlayerId + id, id, player.PlayerId,
                        UnitRank.One, 0, UnitPlacement.OnBoard(new BoardPosition(column++, 0))), id);
            }
            var p1 = m.GetPlayer("p1");
            var preview = SynergyPreparation.PreparationGuardian(p1, catalog);
            Assert.That(preview.HasValue, Is.True);
            var rock = SynergyPreparation.PreparationRocks(p1, catalog).Single();
            Assert.That(preview.Value, Is.Not.EqualTo(rock));
            const int mode = SynergyPreparation.GuardianPlacementMode;
            SynergyPreparation.Configure(m,catalog,"p1",mode,null,null,0,rock.Column,rock.Row);
            Assert.That(SynergyPreparation.PreparationGuardian(p1,catalog), Is.EqualTo(rock));
            Assert.That(SynergyPreparation.PreparationRocks(p1,catalog).Single(), Is.EqualTo(preview.Value));
            SynergyPreparation.Configure(m,catalog,"p1",mode,null,null,0,0,0);
            Assert.That(p1.GetUnit("p1a").Placement.Position, Is.EqualTo(rock));

            var expected = new BoardPosition(6, 3);
            SynergyPreparation.Configure(m,catalog,"p1",mode,null,null,0,expected.Column,expected.Row);
            SynergyPreparation.Configure(m,catalog,"p2",mode,null,null,0,expected.Column,expected.Row);
            var coordinator = new LocalRoundCoordinator(m, catalog);
            var owner = new HostCommandProcessor(m, catalog, coordinator).Snapshot("p1");
            Assert.That(new BoardPosition(owner.guardianTile.column, owner.guardianTile.row), Is.EqualTo(expected));
            Assert.That(p1.Units.Count, Is.EqualTo(2));
            Assert.That(coordinator.StartCombat(1), Is.True);
            foreach (var unit in coordinator.Battle.Units.Where(unit => unit.IsSynergySummon))
                Assert.That(unit.Position, Is.EqualTo(unit.TeamId == 1 ? expected : PokeChess.Core.Board.HexCoordinates.MirrorCombat(expected)));
            Assert.That(coordinator.Battle.Units.Count(unit => unit.IsSynergySummon), Is.EqualTo(2));
        }
    }
}
