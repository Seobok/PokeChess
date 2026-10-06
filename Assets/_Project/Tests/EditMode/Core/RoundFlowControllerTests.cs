using System;
using System.Linq;
using NUnit.Framework;
using PokeChess.Core.Battle;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Tests
{
    public sealed class RoundFlowControllerTests
    {
        private PokemonCatalog catalog;
        private MatchState match;
        private LocalRoundCoordinator loop;
        private RoundFlowController flow;
        private PlayerState One => match.GetPlayer("p1");
        private PlayerState Two => match.GetPlayer("p2");

        [SetUp]
        public void Setup() => CreateFixture();

        private void CreateFixture(bool longBattle = false, bool initializeShops = true,int startingHP=60)
        {
            var stats = new PokemonStats(longBattle ? 100000 : 100, longBattle ? 1 : 20,
                0, 1, 0, 0, 1, 2, 0, 0, .25f);
            catalog = new PokemonCatalog(new[] { new PokemonDefinition("test", "Test", 1, stats, "role", "skill") });
            match = MatchStateFactory.CreateWithPool("clock", new[] { "p1", "p2" }, catalog, new[] { "test" }, rules:new MatchRules(startingHP:startingHP),matchSeed: 123);
            match.TransitionTo(MatchPhase.Starting);
            match.TransitionTo(MatchPhase.Preparation);
            if (initializeShops)
                foreach (var player in match.Players) new ShopSystem().RefreshForRound(match, player.PlayerId);
            loop = new LocalRoundCoordinator(match, catalog);
            flow = new RoundFlowController(loop);
        }

        private void Add(string id, string owner, UnitPlacement placement)
            => SharedPoolSystem.RegisterUnit(match, catalog.CreateUnit(id, "test", owner, UnitRank.One, 0, placement), "test");

        private void AddFighters()
        {
            Add("A", "p1", UnitPlacement.OnBoard(new BoardPosition(2, 0)));
            Add("B", "p2", UnitPlacement.OnBoard(new BoardPosition(2, 0)));
        }

        private void FinishCombat(double frameSeconds = 1d / 30)
        {
            for (int frame = 0; frame < 10000 && match.Phase == MatchPhase.Combat; frame++)
                flow.AdvanceTime(frameSeconds);
            Assert.That(flow.Fault, Is.Null);
            Assert.That(match.Phase, Is.EqualTo(MatchPhase.Result));
            Assert.That(loop.IsSettled, Is.True);
        }

        [Test]
        public void DefaultAndConfiguredDurations()
        {
            Assert.That(flow.Rules.PreparationSeconds, Is.EqualTo(30));
            Assert.That(flow.Rules.ResultSeconds, Is.EqualTo(3));
            flow = new RoundFlowController(loop, new RoundFlowRules(1, 2, 15));
            flow.AdvanceTime(1);
            Assert.That(match.Phase, Is.EqualTo(MatchPhase.Result));
            Assert.That(flow.RemainingSeconds, Is.EqualTo(2));
            Assert.That(flow.Rules.MaxCombatTicksPerUpdate, Is.EqualTo(15));
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void InvalidDurationsAreRejected(double seconds)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new RoundFlowRules(seconds, 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RoundFlowRules(30, seconds));
        }

        [Test]
        public void InvalidTickBudgetAndNullRuntimeAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new RoundFlowRules(maxCombatTicksPerUpdate: 0));
            Assert.Throws<ArgumentNullException>(() => new RoundFlowController(null));
        }

        [TestCase(-1)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void InvalidElapsedTimeCannotChangeState(double seconds)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => flow.AdvanceTime(seconds));
            Assert.That(match.Phase, Is.EqualTo(MatchPhase.Preparation));
            Assert.That(flow.RemainingSeconds, Is.EqualTo(30));
            Assert.That(One.Gold, Is.EqualTo(5));
        }

        [Test]
        public void PreparationStartsCombatAtThirtySecondsWithoutInput()
        {
            AddFighters();
            flow.AdvanceTime(29.999);
            Assert.That(match.Phase, Is.EqualTo(MatchPhase.Preparation));
            flow.AdvanceTime(.001);
            Assert.That(match.Phase, Is.EqualTo(MatchPhase.Combat));
            Assert.That(loop.Battle.CurrentTick, Is.Zero);
            Assert.That(loop.Battle.Units.Count, Is.EqualTo(2));
            Assert.That(flow.PendingCombatSeconds, Is.Zero);
        }

        [Test]
        public void InitialShopReadinessBlocksCountdownAndStart()
        {
            CreateFixture(initializeShops: false);
            flow.AdvanceTime(100);
            Assert.That(flow.IsTimerRunning, Is.False);
            Assert.That(loop.StartCombat(1), Is.False);
            Assert.That(flow.RequestAdvance(1, MatchPhase.Preparation), Is.False);
            new ShopSystem().RefreshForRound(match, "p1");
            flow.AdvanceTime(100);
            Assert.That(flow.IsTimerRunning, Is.False);
            new ShopSystem().RefreshForRound(match, "p2");
            flow.AdvanceTime(100); // Readiness observed now; old time cannot consume the new preparation.
            Assert.That(flow.IsTimerRunning, Is.True);
            Assert.That(flow.RemainingSeconds, Is.EqualTo(30));
            Assert.That(match.Phase, Is.EqualTo(MatchPhase.Preparation));
        }

        [TestCase(false, false, BattleResult.Draw)]
        [TestCase(true, false, BattleResult.TeamOneWin)]
        [TestCase(false, true, BattleResult.TeamTwoWin)]
        public void EmptyBoardAutomaticResultHasFullDisplayTime(bool one, bool two, BattleResult result)
        {
            if (one) Add("A", "p1", UnitPlacement.OnBoard(new BoardPosition(0, 0)));
            if (two) Add("B", "p2", UnitPlacement.OnBoard(new BoardPosition(0, 0)));
            flow.AdvanceTime(300);
            Assert.That(match.Phase, Is.EqualTo(MatchPhase.Result));
            Assert.That(loop.LastResult.Result, Is.EqualTo(result));
            Assert.That(loop.LastResult.EndTick, Is.Zero);
            Assert.That(flow.RemainingSeconds, Is.EqualTo(3));
            Assert.That(loop.IsSettled, Is.True);
            Assert.That(One.LastEconomyRound, Is.EqualTo(1));
        }

        [Test]
        public void ResultAutomaticallyEntersFreshThirtySecondPreparation()
        {
            flow.AdvanceTime(30);
            int gold = One.Gold;
            flow.AdvanceTime(2.999);
            Assert.That(match.Phase, Is.EqualTo(MatchPhase.Result));
            flow.AdvanceTime(.001);
            Assert.That(match.Phase, Is.EqualTo(MatchPhase.Preparation));
            Assert.That(match.RoundNumber, Is.EqualTo(2));
            Assert.That(flow.RemainingSeconds, Is.EqualTo(30));
            Assert.That(One.Shop.LastRefreshRound, Is.EqualTo(2));
            Assert.That(One.Gold, Is.EqualTo(gold));
            Assert.That(One.LastAutomaticXPRound, Is.EqualTo(1));
        }

        [Test]
        public void CombatDebtIsRetainedUnderTickBudgetAndClearedAtResult()
        {
            CreateFixture(longBattle: true);
            AddFighters();
            flow.AdvanceTime(30);
            flow.AdvanceTime(45);
            Assert.That(loop.Battle.CurrentTick, Is.EqualTo(60));
            Assert.That(flow.PendingCombatSeconds, Is.EqualTo(43).Within(1e-8));
            flow.AdvanceTime(0);
            Assert.That(loop.Battle.CurrentTick, Is.EqualTo(120));
            for (int i = 0; i < 30 && match.Phase == MatchPhase.Combat; i++) flow.AdvanceTime(0);
            Assert.That(loop.LastResult.EndTick, Is.EqualTo(1350));
            Assert.That(loop.LastResult.Reason, Is.EqualTo(BattleEndReason.TimeLimit));
            Assert.That(loop.Battle.IsOvertime, Is.True);
            Assert.That(flow.PendingCombatSeconds, Is.Zero);
            Assert.That(flow.RemainingSeconds, Is.EqualTo(3));
        }

        [Test]
        public void LargeResultFrameCannotConsumeNextPreparation()
        {
            flow.AdvanceTime(30);
            flow.AdvanceTime(1000);
            Assert.That(match.RoundNumber, Is.EqualTo(2));
            Assert.That(match.Phase, Is.EqualTo(MatchPhase.Preparation));
            Assert.That(flow.RemainingSeconds, Is.EqualTo(30));
            Assert.That(One.LastEconomyRound, Is.EqualTo(1));
        }

        [Test]
        public void AutomaticAndManualRequestsCannotApplyRoundWorkTwice()
        {
            AddFighters();
            Assert.That(flow.RequestAdvance(1, MatchPhase.Preparation), Is.True);
            var battle = loop.Battle;
            Assert.That(flow.RequestAdvance(1, MatchPhase.Preparation), Is.False);
            Assert.That(loop.Battle, Is.SameAs(battle));
            FinishCombat();
            int gold = One.Gold, xp = One.XP;
            flow.AdvanceTime(3);
            long revision = One.Shop.Revision;
            Assert.That(flow.RequestAdvance(1, MatchPhase.Result), Is.False);
            Assert.That(flow.RequestAdvance(1, MatchPhase.Preparation), Is.False);
            Assert.That(One.Shop.Revision, Is.EqualTo(revision));
            Assert.That(One.Gold, Is.EqualTo(gold));
            Assert.That(One.XP, Is.EqualTo(xp));
            Assert.That(match.RoundNumber, Is.EqualTo(2));
        }

        [Test]
        public void FailedSettlementPausesUntilExplicitRetryAndThenDisplaysFullResult()
        {
            Two.SetProgress(int.MaxValue, 0, 1);
            flow.AdvanceTime(30);
            Assert.That(flow.Fault, Is.TypeOf<OverflowException>());
            Assert.That(match.Phase, Is.EqualTo(MatchPhase.Result));
            Assert.That(flow.IsTimerRunning, Is.False);
            Assert.That(One.Gold, Is.EqualTo(5));
            Two.SetProgress(5, 0, 1);
            flow.AdvanceTime(1000);
            Assert.That(loop.IsSettled, Is.False); // No automatic retry loop.
            Assert.That(flow.RequestAdvance(1, MatchPhase.Preparation), Is.False);
            Assert.That(flow.RequestAdvance(1, MatchPhase.Result), Is.True);
            Assert.That(flow.Fault, Is.Null);
            Assert.That(loop.IsSettled, Is.True);
            Assert.That(flow.RemainingSeconds, Is.EqualTo(3));
            Assert.That(One.Gold, Is.EqualTo(10));
            flow.AdvanceTime(3);
            Assert.That(match.RoundNumber, Is.EqualTo(2));
        }

        [Test]
        public void PartialShopRefreshPausesAndRetryPreservesCompletedPlayer()
        {
            flow.AdvanceTime(30);
            long revision = Two.Shop.Revision;
            typeof(ShopState).GetProperty("Revision").SetValue(Two.Shop, long.MaxValue);
            flow.AdvanceTime(3);
            Assert.That(flow.Fault, Is.TypeOf<OverflowException>());
            Assert.That(loop.NextPreparationPending, Is.True);
            Assert.That(flow.IsTimerRunning, Is.False);
            Assert.That(flow.RemainingSeconds, Is.EqualTo(30));
            var rng = One.Shop.RandomState;
            long completedRevision = One.Shop.Revision;
            typeof(ShopState).GetProperty("Revision").SetValue(Two.Shop, revision);
            flow.AdvanceTime(1000);
            Assert.That(loop.NextPreparationPending, Is.True);
            Assert.That(loop.StartCombat(2), Is.False);
            Assert.That(flow.RequestAdvance(1, MatchPhase.Result), Is.False);
            Assert.That(flow.RequestAdvance(2, MatchPhase.Preparation), Is.True);
            Assert.That(flow.Fault, Is.Null);
            Assert.That(flow.RemainingSeconds, Is.EqualTo(30));
            Assert.That(One.Shop.Revision, Is.EqualTo(completedRevision));
            Assert.That(One.Shop.RandomState, Is.EqualTo(rng));
            Assert.That(One.LastEconomyRound, Is.EqualTo(1));
            match.Pool.AssertConservation(match);
        }

        [Test]
        public void InsufficientSurvivorsWaitWithoutFaultAndCanResume()
        {
            One.SetHP(0);
            flow.AdvanceTime(30);
            var fault = flow.Fault;
            Assert.That(fault, Is.Null);
            Assert.That(flow.AwaitingMatchEnd, Is.True);
            Assert.That(flow.IsTimerRunning, Is.False);
            flow.AdvanceTime(100);
            Assert.That(flow.Fault, Is.Null);
            One.SetHP(60);
            Assert.That(flow.RequestAdvance(1, MatchPhase.Preparation), Is.True);
            Assert.That(flow.Fault, Is.Null);
            Assert.That(match.Phase, Is.EqualTo(MatchPhase.Result));
        }

        [Test]
        public void LockedShopRetainsPurchasedHoleAndRandomStateOnAutomaticNextRound()
        {
            new ShopSystem().SetLocked(match, "p1", true);
            new ShopTransactionSystem(catalog).Buy(match, "p1", 0, One.Shop.Revision);
            var offers = One.Shop.Slots.Select(s => s.DefinitionId).ToArray();
            var rng = One.Shop.RandomState;
            long revision = One.Shop.Revision;
            flow.AdvanceTime(30);
            flow.AdvanceTime(3);
            Assert.That(One.Shop.LastRefreshRound, Is.EqualTo(2));
            Assert.That(One.Shop.Slots[0].IsEmpty, Is.True);
            Assert.That(One.Shop.Revision, Is.EqualTo(revision));
            Assert.That(One.Shop.RandomState, Is.EqualTo(rng));
            Assert.That(One.Shop.Slots.Select(s => s.DefinitionId), Is.EqualTo(offers));
        }

        [Test]
        public void DeferredBoardMergeRunsBeforeNewPreparationCountdown()
        {
            AddFighters();
            Add("bench", "p1", UnitPlacement.OnBench(0));
            flow.AdvanceTime(30);
            var fighter = loop.Battle.Units.Single(u => u.UnitInstanceId == "A");
            var buy = new ShopTransactionSystem(catalog).BuyWithRankUp(match, "p1", 0, One.Shop.Revision);
            Assert.That(buy.MergeNextPreparation, Is.True);
            Assert.That(fighter.Rank, Is.EqualTo(UnitRank.One));
            FinishCombat();
            flow.AdvanceTime(3);
            Assert.That(One.GetUnit("A").Rank, Is.EqualTo(UnitRank.Two));
            Assert.That(loop.PreparationRankUps.Count, Is.EqualTo(1));
            Assert.That(flow.RemainingSeconds, Is.EqualTo(30));
            flow.AdvanceTime(30);
            Assert.That(loop.Battle.Units.Single(u => u.UnitInstanceId == "A").Rank, Is.EqualTo(UnitRank.Two));
            match.Pool.AssertConservation(match);
        }

        [Test]
        public void TenRealBattlesAdvanceWithoutManualRequestsAndPreserveState()
        {
            CreateFixture(startingHP:1000);
            AddFighters();
            for (int round = 1; round <= 10; round++)
            {
                flow.AdvanceTime(30);
                FinishCombat();
                Assert.That(One.LastEconomyRound, Is.EqualTo(round));
                Assert.That(One.LastAutomaticXPRound, Is.EqualTo(round));
                flow.AdvanceTime(3);
                Assert.That(match.RoundNumber, Is.EqualTo(round + 1));
                Assert.That(One.Shop.LastRefreshRound, Is.EqualTo(round + 1));
                Assert.That(flow.RemainingSeconds, Is.EqualTo(30));
                Assert.That(One.GetUnit("A").Placement.Position.Value, Is.EqualTo(new BoardPosition(2, 0)));
                match.Pool.AssertConservation(match);
            }
        }

        [Test]
        public void FramePartitionsProduceSameBattleResultAndEndTick()
        {
            AddFighters();
            flow.AdvanceTime(30);
            FinishCombat(1d / 120);
            var result = loop.LastResult.Result;
            long endTick = loop.LastResult.EndTick;
            var gold = One.Gold;
            CreateFixture();
            AddFighters();
            flow.AdvanceTime(30);
            FinishCombat(.73);
            Assert.That(loop.LastResult.Result, Is.EqualTo(result));
            Assert.That(loop.LastResult.EndTick, Is.EqualTo(endTick));
            Assert.That(One.Gold, Is.EqualTo(gold));
            Assert.That(flow.RemainingSeconds, Is.EqualTo(3));
        }

        [Test]
        public void ExternallyObservedResultDiscardsOldFrameTime()
        {
            loop.StartCombat(1);
            flow.AdvanceTime(1000);
            Assert.That(match.Phase, Is.EqualTo(MatchPhase.Result));
            Assert.That(flow.RemainingSeconds, Is.EqualTo(3));
            Assert.That(match.RoundNumber, Is.EqualTo(1));
        }

        [TestCase(MatchPhase.Waiting)]
        [TestCase(MatchPhase.Starting)]
        [TestCase(MatchPhase.Finished)]
        public void InactivePhasesDoNotRunTimersOrAcceptSkip(MatchPhase phase)
        {
            var inactive = MatchStateFactory.Create("idle", new[] { "p1", "p2" });
            if (phase != MatchPhase.Waiting) inactive.TransitionTo(MatchPhase.Starting);
            if (phase == MatchPhase.Finished) inactive.TransitionTo(MatchPhase.Finished);
            var idleFlow = new RoundFlowController(new LocalRoundCoordinator(inactive, catalog));
            idleFlow.AdvanceTime(1000);
            Assert.That(idleFlow.IsTimerRunning, Is.False);
            Assert.That(idleFlow.RequestAdvance(0, phase), Is.False);
            Assert.That(inactive.Phase, Is.EqualTo(phase));
        }

        [Test]
        public void ResetCreatesFreshClockAndFinishedMatchStopsCombat()
        {
            AddFighters();
            flow.AdvanceTime(30);
            flow.AdvanceTime(.5);
            match.TransitionTo(MatchPhase.Finished);
            flow.AdvanceTime(100);
            Assert.That(flow.PendingCombatSeconds, Is.Zero);
            Assert.That(flow.IsTimerRunning, Is.False);
            CreateFixture();
            Assert.That(flow.RemainingSeconds, Is.EqualTo(30));
            Assert.That(flow.Fault, Is.Null);
            Assert.That(One.LastEconomyRound, Is.Zero);
        }
    }
}


