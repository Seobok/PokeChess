using System;
using System.Linq;
using NUnit.Framework;
using PokeChess.Core.Battle;
using PokeChess.Core.Board;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Tests
{
    public sealed class AutomaticBoardDeploymentTests
    {
        private PokemonCatalog catalog;
        private MatchState match;
        private LocalRoundCoordinator loop;
        private PlayerState One => match.GetPlayer("p1");
        private PlayerState Two => match.GetPlayer("p2");

        [SetUp]
        public void Setup()
        {
            catalog = new PokemonCatalog(new[] { new PokemonDefinition("test", "Test", 1,
                new PokemonStats(100, 20, 0, 1, 0, 0, 1, 2, 0, 0, .25f), "role", "skill") });
            match = MatchStateFactory.CreateWithPool("auto-deploy", new[] { "p1", "p2" },
                catalog, new[] { "test" }, matchSeed: 123);
            match.TransitionTo(MatchPhase.Starting);
            match.TransitionTo(MatchPhase.Preparation);
            foreach (var player in match.Players) new ShopSystem().RefreshForRound(match, player.PlayerId);
            loop = new LocalRoundCoordinator(match, catalog);
        }

        private UnitInstance Add(string id, string owner, UnitPlacement placement)
        {
            SharedPoolSystem.RegisterUnit(match, catalog.CreateUnit(id, "test", owner, UnitRank.One, 0, placement), "test");
            return match.GetPlayer(owner).GetUnit(id);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ManualAndTimerStartDeployBothBenchOnlyTeamsBeforeSnapshot(bool timer)
        {
            var one = Add("A", "p1", UnitPlacement.OnBench(8));
            var two = Add("B", "p2", UnitPlacement.OnBench(4));
            if (timer) new RoundFlowController(loop).AdvanceTime(30);
            else Assert.That(loop.StartCombat(1), Is.True);
            Assert.That(match.Phase, Is.EqualTo(MatchPhase.Combat));
            Assert.That(one.Placement.Position.Value, Is.EqualTo(new BoardPosition(0, 0)));
            Assert.That(two.Placement.Position.Value, Is.EqualTo(new BoardPosition(0, 0)));
            Assert.That(loop.Battle.Units.Count, Is.EqualTo(2));
            Assert.That(loop.Battle.Units.Single(u => u.UnitInstanceId == "B").Position,
                Is.EqualTo(HexCoordinates.MirrorCombat(new BoardPosition(0, 0))));
            Assert.That(loop.Battle.CurrentTick, Is.Zero);
            Assert.That(loop.LastResult, Is.Null);
            match.Pool.AssertConservation(match);
        }

        [Test]
        public void LowestBenchSlotsFillRowFirstEmptyCellsUpToLevelWithoutMovingBoardUnits()
        {
            One.SetProgress(5, 0, 4);
            var existing = Add("board", "p1", UnitPlacement.OnBoard(new BoardPosition(1, 0)));
            Add("high", "p1", UnitPlacement.OnBench(8)); // Deliberately register out of bench order.
            Add("mid", "p1", UnitPlacement.OnBench(4));
            Add("low", "p1", UnitPlacement.OnBench(1));
            Add("extra", "p1", UnitPlacement.OnBench(7));
            Add("foe", "p2", UnitPlacement.OnBoard(new BoardPosition(2, 0)));
            long revision = One.PlacementRevision;
            Assert.That(loop.StartCombat(1), Is.True);
            Assert.That(existing.Placement.Position.Value, Is.EqualTo(new BoardPosition(1, 0)));
            Assert.That(One.GetUnit("low").Placement.Position.Value, Is.EqualTo(new BoardPosition(0, 0)));
            Assert.That(One.GetUnit("mid").Placement.Position.Value, Is.EqualTo(new BoardPosition(2, 0)));
            Assert.That(One.GetUnit("extra").Placement.Position.Value, Is.EqualTo(new BoardPosition(3, 0)));
            Assert.That(One.GetUnit("high").Placement.BenchSlot, Is.EqualTo(8));
            Assert.That(One.DeployedUnitCount, Is.EqualTo(4));
            Assert.That(One.PlacementRevision, Is.EqualTo(revision + 1));
            Assert.That(loop.Battle.Units.Count, Is.EqualTo(5));
        }

        [Test]
        public void FullDeploymentCapacityLeavesBenchAndRevisionUnchanged()
        {
            Add("board", "p1", UnitPlacement.OnBoard(new BoardPosition(2, 0)));
            var bench = Add("bench", "p1", UnitPlacement.OnBench(0));
            Add("foe", "p2", UnitPlacement.OnBoard(new BoardPosition(2, 0)));
            long revision = One.PlacementRevision;
            loop.StartCombat(1);
            Assert.That(One.BoardCells.Count(c => c.IsEmpty), Is.EqualTo(27));
            Assert.That(bench.Placement.BenchSlot, Is.Zero);
            Assert.That(One.PlacementRevision, Is.EqualTo(revision));
            Assert.That(loop.Battle.Units.Any(u => u.UnitInstanceId == "bench"), Is.False);
        }

        [Test]
        public void InsufficientBenchUnitsLeaveRemainingCapacityEmptyAndIgnoreUnplacedUnits()
        {
            One.SetProgress(5, 0, 3);
            Add("only", "p1", UnitPlacement.OnBench(2));
            Add("unplaced", "p1", UnitPlacement.Unplaced);
            Add("foe", "p2", UnitPlacement.OnBoard(new BoardPosition(2, 0)));
            loop.StartCombat(1);
            Assert.That(One.DeployedUnitCount, Is.EqualTo(1));
            Assert.That(One.RemainingDeploymentCapacity, Is.EqualTo(2));
            Assert.That(One.GetUnit("unplaced").Placement.Kind, Is.EqualTo(PlacementKind.Unplaced));
            Assert.That(loop.Battle.Units.Count, Is.EqualTo(2));
        }

        [Test]
        public void AutoDeploymentPreservesOwnedReferencesItemsGoldShopAndPool()
        {
            var unit = Add("A", "p1", UnitPlacement.OnBench(3));
            unit.AddItem("item");
            Add("B", "p2", UnitPlacement.OnBoard(new BoardPosition(2, 0)));
            int stock = match.Pool.GetStock("test").Available, gold = One.Gold;
            long shopRevision = One.Shop.Revision;
            ulong rng = One.Shop.RandomState;
            loop.StartCombat(1);
            Assert.That(One.GetUnit("A"), Is.SameAs(unit));
            Assert.That(unit.ItemInstanceIds, Does.Contain("item"));
            Assert.That(One.Gold, Is.EqualTo(gold));
            Assert.That(One.Shop.Revision, Is.EqualTo(shopRevision));
            Assert.That(One.Shop.RandomState, Is.EqualTo(rng));
            Assert.That(match.Pool.GetStock("test").Available, Is.EqualTo(stock));
            var stale = new PlayerPlacementSystem().Move(match, "p1", "A", UnitPlacement.OnBench(1), One.PlacementRevision - 1);
            Assert.That(stale.Accepted, Is.False);
            match.Pool.AssertConservation(match);
        }

        [Test]
        public void StaleRejectedAndRepeatedStartRequestsDoNotDeployExtraUnits()
        {
            Add("A", "p1", UnitPlacement.OnBench(0));
            Add("extra", "p1", UnitPlacement.OnBench(1));
            Add("B", "p2", UnitPlacement.OnBench(0));
            long revision = One.PlacementRevision;
            Assert.That(loop.StartCombat(0), Is.False);
            Assert.That(One.PlacementRevision, Is.EqualTo(revision));
            One.SetHP(0);
            Assert.That(loop.StartCombat(1), Is.False);
            Assert.That(One.GetUnit("A").Placement.Kind, Is.EqualTo(PlacementKind.Bench));
            One.SetHP(60);
            Assert.That(loop.StartCombat(1), Is.True);
            revision = One.PlacementRevision;
            var battle = loop.Battle;
            Assert.That(loop.StartCombat(1), Is.False);
            Assert.That(One.GetUnit("extra").Placement.BenchSlot, Is.EqualTo(1));
            Assert.That(One.PlacementRevision, Is.EqualTo(revision));
            Assert.That(loop.Battle, Is.SameAs(battle));
        }

        [Test]
        public void SecondPlayerRevisionOverflowDoesNotPartiallyDeployFirstPlayerAndCanRetry()
        {
            Add("A", "p1", UnitPlacement.OnBench(0));
            Add("B", "p2", UnitPlacement.OnBench(0));
            long firstRevision = One.PlacementRevision, secondRevision = Two.PlacementRevision;
            typeof(PlayerState).GetProperty("PlacementRevision").SetValue(Two, long.MaxValue);
            var flow = new RoundFlowController(loop);
            flow.AdvanceTime(30);
            Assert.That(flow.Fault, Is.TypeOf<OverflowException>());
            Assert.That(match.Phase, Is.EqualTo(MatchPhase.Preparation));
            Assert.That(One.PlacementRevision, Is.EqualTo(firstRevision));
            Assert.That(One.GetUnit("A").Placement.Kind, Is.EqualTo(PlacementKind.Bench));
            Assert.That(Two.GetUnit("B").Placement.Kind, Is.EqualTo(PlacementKind.Bench));
            Assert.That(loop.Battle, Is.Null);
            typeof(PlayerState).GetProperty("PlacementRevision").SetValue(Two, secondRevision);
            Assert.That(flow.RequestAdvance(1, MatchPhase.Preparation), Is.True);
            Assert.That(flow.Fault, Is.Null);
            Assert.That(One.DeployedUnitCount, Is.EqualTo(1));
            Assert.That(Two.DeployedUnitCount, Is.EqualTo(1));
        }

        [Test]
        public void FailedBattleCreationDoesNotCommitAnyAutomaticPlacement()
        {
            Add("A", "p1", UnitPlacement.OnBench(0));
            var otherCatalog = new PokemonCatalog(new[] { new PokemonDefinition("missing", "Missing", 1,
                new PokemonStats(100, 20, 0, 1, 0, 0, 1, 2, 0, 0, .25f), "role", "skill") });
            Two.AddUnit(otherCatalog.CreateUnit("B", "missing", "p2", UnitRank.One, 0, UnitPlacement.OnBench(0)));
            long oneRevision = One.PlacementRevision, twoRevision = Two.PlacementRevision;
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(() => loop.StartCombat(1));
            Assert.That(match.Phase, Is.EqualTo(MatchPhase.Preparation));
            Assert.That(One.GetUnit("A").Placement.Kind, Is.EqualTo(PlacementKind.Bench));
            Assert.That(Two.GetUnit("B").Placement.Kind, Is.EqualTo(PlacementKind.Bench));
            Assert.That(One.PlacementRevision, Is.EqualTo(oneRevision));
            Assert.That(Two.PlacementRevision, Is.EqualTo(twoRevision));
            Assert.That(loop.Battle, Is.Null);
        }

        [Test]
        public void NextRoundFillsNewLevelCapacityAndRetainsPreviouslyDeployedPosition()
        {
            Add("A", "p1", UnitPlacement.OnBench(0));
            Add("extra", "p1", UnitPlacement.OnBench(1));
            Add("B", "p2", UnitPlacement.OnBoard(new BoardPosition(2, 0)));
            var flow = new RoundFlowController(loop);
            flow.AdvanceTime(30);
            var position = One.GetUnit("A").Placement.Position.Value;
            for (int i = 0; i < 1400 && match.Phase == MatchPhase.Combat; i++) flow.AdvanceTime(1d / 30);
            Assert.That(match.Phase, Is.EqualTo(MatchPhase.Result));
            Assert.That(One.Level, Is.EqualTo(2));
            Assert.That(One.GetUnit("extra").Placement.Kind, Is.EqualTo(PlacementKind.Bench));
            flow.AdvanceTime(3);
            flow.AdvanceTime(30);
            Assert.That(match.Phase, Is.EqualTo(MatchPhase.Combat));
            Assert.That(One.GetUnit("A").Placement.Position.Value, Is.EqualTo(position));
            Assert.That(One.GetUnit("extra").Placement.Position.Value, Is.EqualTo(new BoardPosition(1, 0)));
            Assert.That(loop.Battle.Units.Count(u => u.TeamId == 1), Is.EqualTo(2));
            match.Pool.AssertConservation(match);
        }
    }
}
