using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;
using PokeChess.Core.Battle;
using PokeChess.Core.Board;

namespace PokeChess.Core.Tests
{
    public sealed class MatchStateTests
    {
        private static MatchState Create(MatchRules rules = null) => MatchStateFactory.Create("match", new[] { "p1", "p2" }, rules);
        private static PokemonCatalog Catalog() => new PokemonCatalog(new[] {
            new PokemonDefinition("test", "Test", 1, new PokemonStats(100,10,0,1,0,0,1,2,100,20,.25f), "role", "skill") });
        private static UnitInstance Unit(string id, string owner = "p1", UnitPlacement placement = default) =>
            Catalog().CreateUnit(id, "test", owner, UnitRank.One, 0, placement);

        [Test]
        public void DefaultMatchHasIndependentInitialPlayerStates()
        {
            var match = Create();
            Assert.That(match.Phase, Is.EqualTo(MatchPhase.Waiting));
            Assert.That(match.RoundNumber, Is.Zero);
            Assert.That(match.Players.Count, Is.EqualTo(2));
            var a = match.GetPlayer("p1");
            var b = match.GetPlayer("p2");
            Assert.That(new[] { a.HP,a.Gold,a.XP,a.Level }, Is.EqualTo(new[] {60,5,0,3}));
            a.SetHP(0);
            a.SetProgress(20,7,4);
            a.AddUnit(Unit("u1"));
            Assert.That(new[] { b.HP,b.Gold,b.XP,b.Level }, Is.EqualTo(new[] {60,5,0,3}));
            Assert.That(b.Units, Is.Empty);
            Assert.That(a.Board, Is.Empty);
            Assert.That(a.Bench.Count, Is.EqualTo(9));
            Assert.That(a.Bench.All(u => u == null), Is.True);
        }
        [Test]
        public void ExplicitRulesAndInputPlayerListAreIndependent()
        {
            var ids = new List<string> {"p1","p2"};
            var rules = new MatchRules(80,9,2,4,9,5,3,2);
            var match = MatchStateFactory.Create("custom", ids, rules);
            ids[0] = "changed";
            var player = match.GetPlayer("p1");
            Assert.That(new[] {player.HP,player.Gold,player.XP,player.Level}, Is.EqualTo(new[] {80,9,2,4}));
            player.AddUnit(Unit("u", placement: UnitPlacement.OnBoard(new BoardPosition(4,2))));
            Assert.Throws<ArgumentOutOfRangeException>(() => player.SetPlacement("u", UnitPlacement.OnBoard(new BoardPosition(5,2))));
            Assert.That(player.Bench.Count, Is.EqualTo(2));
            Assert.That(Create().GetPlayer("p1").Units, Is.Empty);
        }
        [Test]
        public void FactoryRejectsNullBlankDuplicateAndInvalidParticipantCounts()
        {
            Assert.Throws<ArgumentNullException>(() => MatchStateFactory.Create("m", null));
            Assert.Throws<ArgumentException>(() => MatchStateFactory.Create(" ", new[] {"a","b"}));
            Assert.Throws<ArgumentException>(() => MatchStateFactory.Create("m", new[] {"a"," "}));
            Assert.Throws<ArgumentException>(() => MatchStateFactory.Create("m", new[] {"a","a"}));
            Assert.Throws<ArgumentException>(() => MatchStateFactory.Create("m", new[] {"a"}));
            Assert.Throws<ArgumentException>(() => MatchStateFactory.Create("m", Enumerable.Range(0,9).Select(i => i.ToString())));
            Assert.DoesNotThrow(() => MatchStateFactory.Create("m", Enumerable.Range(0,8).Select(i => i.ToString())));
        }
        [Test]
        public void RulesRejectInvalidValues()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new MatchRules(startingHP: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MatchRules(startingGold: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MatchRules(startingXP: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MatchRules(startingLevel: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MatchRules(startingLevel: 9));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MatchRules(maxLevel: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MatchRules(boardWidth: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MatchRules(boardHeight: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MatchRules(benchCapacity: 0));
        }
        [Test]
        public void FailedResourceUpdatesLeaveAllFieldsUnchanged()
        {
            var player = Create().GetPlayer("p1");
            Assert.Throws<ArgumentOutOfRangeException>(() => player.SetHP(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => player.SetHP(61));
            Assert.Throws<ArgumentOutOfRangeException>(() => player.SetProgress(-1,0,3));
            Assert.Throws<ArgumentOutOfRangeException>(() => player.SetProgress(10,-1,3));
            Assert.Throws<ArgumentOutOfRangeException>(() => player.SetProgress(10,4,9));
            Assert.Throws<ArgumentOutOfRangeException>(() => player.SetProgress(10,4,0));
            Assert.That(new[] {player.HP,player.Gold,player.XP,player.Level}, Is.EqualTo(new[] {60,5,0,3}));
            player.SetProgress(int.MaxValue,int.MaxValue,8);
            Assert.That(player.XP, Is.EqualTo(int.MaxValue));
        }
        [Test]
        public void RegistrationCopiesUnitAndItemsAndProtectsOwnedPlacement()
        {
            var player = Create().GetPlayer("p1");
            var source = Unit("u", placement: UnitPlacement.OnBoard(new BoardPosition(0,0)));
            source.AddItem("item1");
            var owned = player.AddUnit(source);
            source.SetPlacement(UnitPlacement.OnBoard(new BoardPosition(100,100)));
            source.AddItem("item2");
            Assert.That(owned, Is.Not.SameAs(source));
            Assert.That(owned.ItemInstanceIds, Is.EqualTo(new[] {"item1"}));
            Assert.That(owned.Placement.Position.Value, Is.EqualTo(new BoardPosition(0,0)));
            Assert.Throws<ArgumentOutOfRangeException>(() => owned.SetPlacement(UnitPlacement.OnBoard(new BoardPosition(0,4))));
            Assert.That(owned.Placement.Position.Value, Is.EqualTo(new BoardPosition(0,0)));
        }
        [Test]
        public void RegistrationRejectsOwnershipAndMatchWideDuplicateIds()
        {
            var match = Create();
            var player = match.GetPlayer("p1");
            Assert.Throws<ArgumentNullException>(() => player.AddUnit(null));
            Assert.Throws<ArgumentException>(() => player.AddUnit(Unit("x","p2")));
            player.AddUnit(Unit("same"));
            Assert.Throws<ArgumentException>(() => player.AddUnit(Unit("same")));
            Assert.Throws<ArgumentException>(() => match.GetPlayer("p2").AddUnit(Unit("same","p2")));
            Assert.That(player.Units.Count, Is.EqualTo(1));
            Assert.That(match.GetPlayer("p2").Units, Is.Empty);
            Assert.DoesNotThrow(() => Create().GetPlayer("p1").AddUnit(Unit("same")));
        }
        [TestCase(7,0)]
        [TestCase(0,4)]
        public void RegistrationRejectsOutsidePlayerBoard(int column, int row)
        {
            var player = Create().GetPlayer("p1");
            Assert.Throws<ArgumentOutOfRangeException>(() => player.AddUnit(Unit("u", placement: UnitPlacement.OnBoard(new BoardPosition(column,row)))));
            Assert.That(player.Units, Is.Empty);
        }
        [Test]
        public void OccupiedDestinationsAreRejectedWithoutMovingSource()
        {
            var player = Create().GetPlayer("p1");
            var a = player.AddUnit(Unit("a", placement: UnitPlacement.OnBoard(new BoardPosition(0,0))));
            player.AddUnit(Unit("b", placement: UnitPlacement.OnBench(0)));
            Assert.Throws<InvalidOperationException>(() => a.SetPlacement(UnitPlacement.OnBench(0)));
            Assert.Throws<InvalidOperationException>(() => player.SetPlacement("b", UnitPlacement.OnBoard(new BoardPosition(0,0))));
            Assert.Throws<InvalidOperationException>(() => player.AddUnit(Unit("c", placement: UnitPlacement.OnBoard(new BoardPosition(0,0)))));
            Assert.Throws<InvalidOperationException>(() => player.AddUnit(Unit("c", placement: UnitPlacement.OnBench(0))));
            Assert.That(a.Placement.Position.Value, Is.EqualTo(new BoardPosition(0,0)));
            Assert.That(player.GetUnit("b").Placement.BenchSlot, Is.EqualTo(0));
            Assert.That(player.Units.Count, Is.EqualTo(2));
            Assert.DoesNotThrow(() => a.SetPlacement(a.Placement));
        }
        [Test]
        public void BoardBenchAndUnplacedViewsFollowMovesAndRemoval()
        {
            var player = Create().GetPlayer("p1");
            var a = player.AddUnit(Unit("a", placement: UnitPlacement.OnBoard(new BoardPosition(6,3))));
            var b = player.AddUnit(Unit("b", placement: UnitPlacement.OnBench(8)));
            Assert.That(player.Board, Is.EqualTo(new[] {a}));
            Assert.That(player.Bench[8], Is.SameAs(b));
            b.SetPlacement(UnitPlacement.OnBoard(new BoardPosition(0,0)));
            Assert.That(player.Board, Is.EqualTo(new[] {b,a}));
            Assert.That(player.Bench[8], Is.Null);
            player.SetPlacement("a",UnitPlacement.OnBench(8));
            player.SetPlacement("b",UnitPlacement.Unplaced);
            Assert.That(player.Board, Is.Empty);
            Assert.That(player.RemoveUnit("a"), Is.SameAs(a));
            Assert.That(a.Placement.Kind, Is.EqualTo(PlacementKind.Unplaced));
            Assert.That(player.Bench[8], Is.Null);
            Assert.Throws<KeyNotFoundException>(() => player.GetUnit("a"));
            Assert.DoesNotThrow(() => a.SetPlacement(UnitPlacement.OnBoard(new BoardPosition(99,99))));
            Assert.DoesNotThrow(() => player.AddUnit(Unit("a")));
        }
        [Test]
        public void FullBenchAndBoundsAreValidated()
        {
            var player = Create(new MatchRules(benchCapacity: 2)).GetPlayer("p1");
            player.AddUnit(Unit("a", placement: UnitPlacement.OnBench(0)));
            player.AddUnit(Unit("b", placement: UnitPlacement.OnBench(1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => player.AddUnit(Unit("c", placement: UnitPlacement.OnBench(2))));
            Assert.Throws<InvalidOperationException>(() => player.AddUnit(Unit("c", placement: UnitPlacement.OnBench(1))));
            player.SetPlacement("b",UnitPlacement.Unplaced);
            Assert.DoesNotThrow(() => player.AddUnit(Unit("c", placement: UnitPlacement.OnBench(1))));
        }
        [Test]
        public void CollectionsAreReadOnlyAndUnknownIdsAreRejected()
        {
            var match = Create();
            var player = match.GetPlayer("p1");
            Assert.Throws<NotSupportedException>(() => ((IList<PlayerState>)match.Players).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList<UnitInstance>)player.Units).Add(Unit("u")));
            Assert.Throws<NotSupportedException>(() => ((IList<UnitInstance>)player.Bench)[0] = Unit("u"));
            Assert.Throws<NotSupportedException>(() => ((IList<UnitInstance>)player.Board).Add(Unit("u")));
            Assert.Throws<KeyNotFoundException>(() => match.GetPlayer("missing"));
            Assert.Throws<KeyNotFoundException>(() => player.RemoveUnit("missing"));
            Assert.Throws<KeyNotFoundException>(() => player.SetPlacement("missing",UnitPlacement.Unplaced));
            Assert.Throws<ArgumentException>(() => player.GetUnit(" "));
        }
        [Test]
        public void RoundAdvancesOnlyWhenEnteringPreparation()
        {
            var match = Create();
            match.TransitionTo(MatchPhase.Starting);
            Assert.That(match.RoundNumber, Is.Zero);
            for (int round = 1; round <= 3; round++)
            {
                match.TransitionTo(MatchPhase.Preparation);
                Assert.That(match.RoundNumber, Is.EqualTo(round));
                match.TransitionTo(MatchPhase.Combat);
                match.TransitionTo(MatchPhase.Result);
                Assert.That(match.RoundNumber, Is.EqualTo(round));
            }
            match.TransitionTo(MatchPhase.Finished);
            Assert.That(match.RoundNumber, Is.EqualTo(3));
            Assert.Throws<InvalidOperationException>(() => match.TransitionTo(MatchPhase.Preparation));
            Assert.Throws<InvalidOperationException>(() => match.TransitionTo(MatchPhase.Finished));
        }
        [Test]
        public void InvalidPhaseTransitionsAreAtomic()
        {
            var match = Create();
            Assert.Throws<InvalidOperationException>(() => match.TransitionTo(MatchPhase.Combat));
            Assert.Throws<ArgumentOutOfRangeException>(() => match.TransitionTo((MatchPhase)99));
            Assert.That(match.Phase, Is.EqualTo(MatchPhase.Waiting));
            Assert.That(match.RoundNumber, Is.Zero);
            match.TransitionTo(MatchPhase.Starting);
            match.TransitionTo(MatchPhase.Preparation);
            Assert.Throws<InvalidOperationException>(() => match.TransitionTo(MatchPhase.Result));
            Assert.Throws<InvalidOperationException>(() => match.TransitionTo(MatchPhase.Preparation));
            Assert.That(match.Phase, Is.EqualTo(MatchPhase.Preparation));
            Assert.That(match.RoundNumber, Is.EqualTo(1));
        }
        [Test]
        public void BattleSnapshotDoesNotMutatePlayersOrOwnedUnits()
        {
            var match = Create();
            var a = match.GetPlayer("p1").AddUnit(Unit("a",placement: UnitPlacement.OnBoard(new BoardPosition(0,0))));
            var b = match.GetPlayer("p2").AddUnit(Unit("b","p2",UnitPlacement.OnBoard(new BoardPosition(6,3))));
            a.AddItem("before");
            var battle = BattleStateFactory.Create("battle",1,123,30,Catalog(),new[] {
                new BattleUnitSetup(a,1,new BoardPosition(0,0)),new BattleUnitSetup(b,2,new BoardPosition(6,7)) });
            battle.Units[0].SetVitals(0,120);
            Assert.That(battle.TryMoveUnit("b",new BoardPosition(5,7)), Is.EqualTo(BoardOperationResult.Success));
            a.AddItem("after");
            Assert.That(a.Placement.Position.Value, Is.EqualTo(new BoardPosition(0,0)));
            Assert.That(b.Placement.Position.Value, Is.EqualTo(new BoardPosition(6,3)));
            Assert.That(match.GetPlayer("p1").HP, Is.EqualTo(60));
            Assert.That(battle.Units[0].ItemInstanceIds, Is.EqualTo(new[] {"before"}));
        }
    }
}
