using System;
using NUnit.Framework;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Tests
{
    public sealed class EconomyTests
    {
        private static PlayerState Player() => MatchStateFactory.Create("m",new[] {"p1","p2"}).GetPlayer("p1");
        private static int[] State(PlayerState p) => new[] {p.Gold,p.WinStreak,p.LoseStreak,p.LastEconomyRound,p.HP,p.XP,p.Level};

        [TestCase(0,0)] [TestCase(9,0)] [TestCase(10,1)] [TestCase(19,1)]
        [TestCase(20,2)] [TestCase(49,4)] [TestCase(50,5)] [TestCase(99,5)]
        public void InterestUsesGoldBeforePayout(int gold, int interest)
        {
            var p = Player();
            p.SetProgress(gold,0,3);
            var income = new EconomySystem().Settle(p,1,RoundOutcome.Win);
            Assert.That(income.GoldBefore, Is.EqualTo(gold));
            Assert.That(income.Interest, Is.EqualTo(interest));
            Assert.That(income.BaseIncome, Is.EqualTo(5));
            Assert.That(income.TotalIncome, Is.EqualTo(5+interest));
            Assert.That(p.Gold, Is.EqualTo(gold+5+interest));
        }
        [TestCase(RoundOutcome.Win)] [TestCase(RoundOutcome.Loss)]
        public void StreakThresholdsApplyToCurrentResultAndCapOnlyBonus(RoundOutcome outcome)
        {
            var p = Player();
            var system = new EconomySystem();
            int[] bonuses = {0,1,1,2,2,3,3,3};
            for (int round=1; round<=8; round++)
            {
                p.SetProgress(0,0,3);
                var income = system.Settle(p,round,outcome);
                Assert.That(income.StreakBonus, Is.EqualTo(bonuses[round-1]));
                Assert.That(p.WinStreak, Is.EqualTo(outcome == RoundOutcome.Win ? round : 0));
                Assert.That(p.LoseStreak, Is.EqualTo(outcome == RoundOutcome.Loss ? round : 0));
                Assert.That(income.GoldAfter, Is.EqualTo(5+bonuses[round-1]));
            }
        }
        [Test]
        public void ChangingOutcomeResetsOppositeStreak()
        {
            var p = Player(); var system = new EconomySystem();
            system.Settle(p,1,RoundOutcome.Win);
            system.Settle(p,2,RoundOutcome.Win);
            var loss = system.Settle(p,3,RoundOutcome.Loss);
            Assert.That(new[] {p.WinStreak,p.LoseStreak,loss.StreakBonus}, Is.EqualTo(new[] {0,1,0}));
            system.Settle(p,4,RoundOutcome.Loss);
            var win = system.Settle(p,5,RoundOutcome.Win);
            Assert.That(new[] {p.WinStreak,p.LoseStreak,win.StreakBonus}, Is.EqualTo(new[] {1,0,0}));
        }
        [TestCase(RoundOutcome.Win)] [TestCase(RoundOutcome.Loss)]
        public void DrawResetsBothStreaksButPaysBaseAndInterest(RoundOutcome previous)
        {
            var p = Player(); var system = new EconomySystem();
            for (int i=1;i<=6;i++) system.Settle(p,i,previous);
            p.SetProgress(50,0,3);
            var income = system.Settle(p,7,RoundOutcome.Draw);
            Assert.That(new[] {p.WinStreak,p.LoseStreak,income.StreakBonus,income.Interest,p.Gold}, Is.EqualTo(new[] {0,0,0,5,60}));
        }
        [Test]
        public void PreviewIsReadOnlyAndSettlementRecalculatesFromCurrentGold()
        {
            var p = Player(); var system = new EconomySystem();
            system.Settle(p,1,RoundOutcome.Win);
            p.SetProgress(19,2,4);
            var before = State(p);
            var preview = system.Preview(p,2,RoundOutcome.Win);
            Assert.That(State(p), Is.EqualTo(before));
            Assert.That(preview.PlayerId, Is.EqualTo("p1"));
            Assert.That(preview.RoundNumber, Is.EqualTo(2));
            Assert.That(preview.Outcome, Is.EqualTo(RoundOutcome.Win));
            Assert.That(new[] {preview.BaseIncome,preview.Interest,preview.StreakBonus,preview.TotalIncome,preview.GoldAfter}, Is.EqualTo(new[] {5,1,1,7,26}));
            p.SetProgress(9,2,4);
            var actual = system.Settle(p,2,RoundOutcome.Win);
            Assert.That(actual.GoldAfter, Is.EqualTo(15));
            Assert.That(preview.GoldAfter, Is.EqualTo(26));
        }
        [Test]
        public void RepeatedPastAndSkippedRoundsAreRejectedAcrossSystemInstances()
        {
            var p = Player(); var system = new EconomySystem();
            Assert.Throws<InvalidOperationException>(() => system.Settle(p,2,RoundOutcome.Win));
            Assert.That(p.LastEconomyRound, Is.Zero);
            system.Settle(p,1,RoundOutcome.Win);
            var before = State(p);
            Assert.Throws<InvalidOperationException>(() => new EconomySystem().Settle(p,1,RoundOutcome.Loss));
            Assert.Throws<InvalidOperationException>(() => system.Preview(p,3,RoundOutcome.Win));
            Assert.That(State(p), Is.EqualTo(before));
            system.Settle(p,2,RoundOutcome.Win);
            before = State(p);
            Assert.Throws<InvalidOperationException>(() => system.Settle(p,1,RoundOutcome.Win));
            Assert.That(State(p), Is.EqualTo(before));
        }
        [Test]
        public void InvalidInputsLeaveStateUnchanged()
        {
            var p = Player(); var before = State(p); var system = new EconomySystem();
            Assert.Throws<ArgumentNullException>(() => system.Settle(null,1,RoundOutcome.Win));
            Assert.Throws<ArgumentOutOfRangeException>(() => system.Settle(p,0,RoundOutcome.Win));
            Assert.Throws<ArgumentOutOfRangeException>(() => system.Settle(p,-1,RoundOutcome.Win));
            Assert.Throws<ArgumentOutOfRangeException>(() => system.Settle(p,1,(RoundOutcome)99));
            Assert.That(State(p), Is.EqualTo(before));
        }
        [Test]
        public void GoldOverflowDoesNotPartiallyCommitStreakOrRound()
        {
            var p = Player(); var system = new EconomySystem();
            system.Settle(p,1,RoundOutcome.Win);
            p.SetProgress(int.MaxValue,2,4);
            var before = State(p);
            Assert.Throws<OverflowException>(() => system.Settle(p,2,RoundOutcome.Win));
            Assert.That(State(p), Is.EqualTo(before));
            p.SetProgress(19,2,4);
            Assert.That(system.Settle(p,2,RoundOutcome.Win).GoldAfter, Is.EqualTo(26));
        }
        [Test]
        public void TotalIncomeOverflowDoesNotCommitAnyState()
        {
            var p = Player(); p.SetProgress(10,0,3);
            var before = State(p);
            var system = new EconomySystem(new EconomyRules(baseIncome: int.MaxValue));
            Assert.Throws<OverflowException>(() => system.Settle(p,1,RoundOutcome.Win));
            Assert.That(State(p), Is.EqualTo(before));
        }
        [Test]
        public void CustomRulesAndZeroIncomeAreSupported()
        {
            var p = Player(); p.SetProgress(100,0,3);
            var system = new EconomySystem(new EconomyRules(2,5,3,1,2,3,2,4,6));
            var income = system.Settle(p,1,RoundOutcome.Win);
            Assert.That(new[] {income.BaseIncome,income.Interest,income.StreakBonus,income.GoldAfter}, Is.EqualTo(new[] {2,3,2,107}));
            var zero = new EconomySystem(new EconomyRules(baseIncome:0,maxInterest:0,firstStreakBonus:0,secondStreakBonus:0,thirdStreakBonus:0));
            zero.Settle(p,2,RoundOutcome.Win);
            Assert.That(p.Gold, Is.EqualTo(107));
            Assert.That(p.WinStreak, Is.EqualTo(2));
        }
        [Test]
        public void InvalidRulesAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new EconomyRules(baseIncome:-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new EconomyRules(goldPerInterest:0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new EconomyRules(maxInterest:-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new EconomyRules(firstStreakThreshold:0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new EconomyRules(secondStreakThreshold:2));
            Assert.Throws<ArgumentOutOfRangeException>(() => new EconomyRules(thirdStreakThreshold:4));
            Assert.Throws<ArgumentOutOfRangeException>(() => new EconomyRules(firstStreakBonus:-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new EconomyRules(secondStreakBonus:0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new EconomyRules(thirdStreakBonus:1));
        }
        [Test]
        public void MatchInitializationAndSettlementPreserveOtherStateAndPlayers()
        {
            var match = MatchStateFactory.Create("m",new[] {"p1","p2"});
            var p = match.GetPlayer("p1"); var other = match.GetPlayer("p2");
            Assert.That(new[] {p.Gold,p.WinStreak,p.LoseStreak,p.LastEconomyRound}, Is.EqualTo(new[] {5,0,0,0}));
            var catalog = new PokemonCatalog(new[] {new PokemonDefinition("test","Test",1,
                new PokemonStats(100,10,0,1,0,0,1,2,100,20,.25f),"role","skill")});
            var a = p.AddUnit(catalog.CreateUnit("a","test","p1",UnitRank.One,0,UnitPlacement.OnBoard(new BoardPosition(0,0))));
            var b = p.AddUnit(catalog.CreateUnit("b","test","p1",UnitRank.One,0,UnitPlacement.OnBench(0)));
            p.SetHP(40); p.SetProgress(19,2,4);
            var otherBefore = State(other);
            var system = new EconomySystem();
            match.TransitionTo(MatchPhase.Starting); match.TransitionTo(MatchPhase.Preparation);
            Assert.That(p.Gold, Is.EqualTo(19));
            match.TransitionTo(MatchPhase.Combat); match.TransitionTo(MatchPhase.Result);
            system.Settle(p,match.RoundNumber,RoundOutcome.Win);
            match.TransitionTo(MatchPhase.Preparation);
            Assert.That(new[] {p.HP,p.XP,p.Level,p.Gold}, Is.EqualTo(new[] {40,2,4,25}));
            Assert.That(State(other), Is.EqualTo(otherBefore));
            Assert.That(p.Board[0], Is.SameAs(a)); Assert.That(p.Bench[0], Is.SameAs(b));
            system.Settle(other,1,RoundOutcome.Loss);
            Assert.That(other.LoseStreak, Is.EqualTo(1)); Assert.That(p.WinStreak, Is.EqualTo(1));
        }
    }
}
