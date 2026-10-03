using System;
using System.Collections.Generic;
using NUnit.Framework;
using PokeChess.Core.Pokemon;
using PokeChess.Core.Battle;
using PokeChess.Client.Data;
using UnityEditor;
using UnityEngine;

namespace PokeChess.Core.Tests
{
    public sealed class DataModelTests
    {
        private static PokemonStats Stats() => new PokemonStats(100, 10, 0, 1, 0, 0, 1, 2, 100, 20, .25f);
        private static PokemonDefinition Definition(string id = "test") =>
            new PokemonDefinition(id, "Test Pokemon", 1, Stats(), "test-role", "test-skill");
        private static PokemonCatalog Catalog() => new PokemonCatalog(new[] { Definition() });
        private static UnitInstance Unit(PokemonCatalog catalog, string id, string owner = "p1", IEnumerable<string> items = null) =>
            catalog.CreateUnit(id, "test", owner, UnitRank.One, 0, UnitPlacement.OnBoard(new BoardPosition(0, 0)), items);
        private static BattleState Battle(PokemonCatalog catalog, UnitInstance a, UnitInstance b) =>
            BattleStateFactory.Create("battle-1", 1, 123, 30, catalog, new[] {
                new BattleUnitSetup(a, 1, new BoardPosition(0,0)),
                new BattleUnitSetup(b, 2, new BoardPosition(6,7)) });

        [Test]
        public void SameDefinitionCreatesIndependentUnits()
        {
            var catalog = Catalog();
            var inputItems = new List<string> { "item-1" };
            var a = Unit(catalog, "u1", items: inputItems);
            var b = Unit(catalog, "u2");
            inputItems.Add("item-2");
            a.AddItem("item-3");
            Assert.That(a.DefinitionId, Is.EqualTo(b.DefinitionId));
            Assert.That(a.InstanceId, Is.Not.EqualTo(b.InstanceId));
            Assert.That(a.ItemInstanceIds, Is.EqualTo(new[] { "item-1", "item-3" }));
            Assert.That(b.ItemInstanceIds, Is.Empty);
        }

        [Test]
        public void CombatStateDoesNotMutateOwnedUnitOrDefinition()
        {
            var catalog = Catalog();
            var a = Unit(catalog, "u1");
            var battle = Battle(catalog, a, Unit(catalog, "u2", "p2"));
            a.AddItem("item-after-setup");
            battle.Units[0].SetVitals(40, 120);
            Assert.That(battle.TryMoveUnit("u1", new BoardPosition(1, 0)), Is.EqualTo(PokeChess.Core.Board.BoardOperationResult.Success));
            Assert.That(catalog.Get("test").BaseStats.MaxHP, Is.EqualTo(100));
            Assert.That(a.Placement.Position.Value, Is.EqualTo(new BoardPosition(0,0)));
            Assert.That(battle.Units[0].ItemInstanceIds, Is.Empty);
            Assert.That(battle.Units[0].CurrentEnergy, Is.EqualTo(120));
        }

        [Test]
        public void BattleStartsWithConsistentVitalsAndIdentity()
        {
            var catalog = Catalog();
            var battle = Battle(catalog, Unit(catalog, "u1"), Unit(catalog, "u2", "p2"));
            Assert.That(battle.BattleSeed, Is.EqualTo(123UL));
            Assert.That(battle.TickRate, Is.EqualTo(30));
            Assert.That(battle.CurrentTick, Is.Zero);
            Assert.That(battle.ElapsedSeconds, Is.Zero);
            Assert.That(battle.Result, Is.EqualTo(BattleResult.InProgress));
            Assert.That(battle.Units[0].CurrentHP, Is.EqualTo(100));
            Assert.That(battle.Units[0].CurrentEnergy, Is.EqualTo(20));
            Assert.That(battle.Units[1].OwnerPlayerId, Is.EqualTo("p2"));
            Assert.That(battle.Units[1].TeamId, Is.EqualTo(2));
        }

        [Test]
        public void CatalogRejectsDuplicateAndUnknownDefinitions()
        {
            Assert.Throws<ArgumentException>(() => new PokemonCatalog(new[] { Definition(), Definition() }));
            var catalog = Catalog();
            Assert.Throws<KeyNotFoundException>(() => catalog.CreateUnit("u1", "missing", "p1", UnitRank.One, 0, UnitPlacement.Unplaced));
        }

        [Test]
        public void DefinitionsDefensivelyCopyCollectionsAndValidateEvolutionLinks()
        {
            var types = new[] { "water" };
            var definition = new PokemonDefinition("base", "Base", 1, Stats(), "role", "skill",
                types, evolutions: new[] { new EvolutionOption("evolved", 1) });
            types[0] = "changed";
            Assert.That(definition.TypeIds[0], Is.EqualTo("water"));
            Assert.Throws<ArgumentException>(() => new PokemonCatalog(new[] { definition }));
            Assert.DoesNotThrow(() => new PokemonCatalog(new[] { definition, Definition("evolved") }));
        }

        [Test]
        public void InvalidValuesAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new PokemonStats(0,10,0,1,0,0,1,2,100,0,.25f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PokemonStats(100,10,0,1,0,0,1,2,100,101,.25f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PokemonStats(float.NaN,10,0,1,0,0,1,2,100,0,.25f));
            Assert.Throws<ArgumentOutOfRangeException>(() => Catalog().CreateUnit("u1","test","p1",(UnitRank)4,0,UnitPlacement.Unplaced));
            Assert.Throws<ArgumentOutOfRangeException>(() => UnitPlacement.OnBench(-1));
        }

        [Test]
        public void BattleRejectsDuplicateIdsAndMissingTeam()
        {
            var catalog = Catalog();
            var a = Unit(catalog, "same");
            var b = Unit(catalog, "same", "p2");
            Assert.Throws<ArgumentException>(() => Battle(catalog, a, b));
            Assert.Throws<ArgumentException>(() => BattleStateFactory.Create("b",1,0,30,catalog,
                new[] {new BattleUnitSetup(a,1,new BoardPosition(0,0))}));
        }

        [Test]
        public void RankStatsAreResolvedByExplicitPolicy()
        {
            var catalog = Catalog();
            var a = catalog.CreateUnit("u1","test","p1",UnitRank.Two,0,UnitPlacement.Unplaced);
            var b = Unit(catalog,"u2","p2");
            var battle = BattleStateFactory.Create("b",1,0,30,catalog,
                new[] {new BattleUnitSetup(a,1,new BoardPosition(0,0)),new BattleUnitSetup(b,2,new BoardPosition(6,7))},
                (unit, definition) => new PokemonStats(unit.Rank == UnitRank.Two ? 170 : 100,10,0,1,0,0,1,2,100,20,.25f));
            Assert.That(battle.Units[0].CurrentHP, Is.EqualTo(170));
            Assert.That(catalog.Get("test").BaseStats.MaxHP, Is.EqualTo(100));
        }

        [Test]
        public void AssetConvertsToDetachedCoreDefinition()
        {
            var asset = ScriptableObject.CreateInstance<PokemonDefinitionAsset>();
            try
            {
                var data = new SerializedObject(asset);
                data.FindProperty("definitionId").stringValue = "asset-test";
                data.FindProperty("displayName").stringValue = "Asset Test";
                data.ApplyModifiedPropertiesWithoutUndo();
                var definition = asset.ToDefinition();
                data.Update();
                data.FindProperty("maxHP").floatValue = 999;
                data.ApplyModifiedPropertiesWithoutUndo();
                var catalog = new PokemonCatalog(new[] { definition });
                var a = catalog.CreateUnit("a","asset-test","p1",UnitRank.One,0,UnitPlacement.Unplaced);
                var b = catalog.CreateUnit("b","asset-test","p2",UnitRank.One,0,UnitPlacement.Unplaced);
                Assert.That(Battle(catalog,a,b).Units[0].CurrentHP, Is.EqualTo(100));
            }
            finally { UnityEngine.Object.DestroyImmediate(asset); }
        }
    }
}
