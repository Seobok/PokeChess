using System;
using System.Collections.Generic;
using NUnit.Framework;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Tests
{
    public sealed class LevelTests
    {
        private static MatchState Match(MatchRules rules = null) => MatchStateFactory.Create("m",new[] {"p1","p2"},rules);
        private static MatchState Prepared()
        {
            var m = Match(); m.TransitionTo(MatchPhase.Starting); m.TransitionTo(MatchPhase.Preparation); return m;
        }
        private static int[] State(PlayerState p) => new[] {p.Gold,p.XP,p.Level,p.LastAutomaticXPRound,p.WinStreak,p.LoseStreak,p.LastEconomyRound,p.HP};
        private static UnitInstance Unit(string id, UnitPlacement placement)
        {
            var catalog = new PokemonCatalog(new[] {new PokemonDefinition("test","Test",1,
                new PokemonStats(100,10,0,1,0,0,1,2,100,20,.25f),"role","skill")});
            return catalog.CreateUnit(id,"test","p1",UnitRank.One,0,placement);
        }
        [TestCase(1,2)] [TestCase(2,2)] [TestCase(3,6)] [TestCase(4,10)]
        [TestCase(5,20)] [TestCase(6,36)] [TestCase(7,60)] [TestCase(8,68)] [TestCase(9,68)]
        public void EveryThresholdHandlesBelowExactAndExcess(int level, int requirement)
        {
            var system = new LevelSystem(); var p = Match().GetPlayer("p1");
            Assert.That(system.Rules.XPToNextLevel(level), Is.EqualTo(requirement));
            p.SetProgress(5,0,level);
            var before = State(p); var preview = system.PreviewXP(p,requirement-1);
            Assert.That(preview.LevelAfter, Is.EqualTo(level)); Assert.That(preview.XPAfter, Is.EqualTo(requirement-1));
            Assert.That(State(p), Is.EqualTo(before));
            var exact = system.GrantXP(p,requirement);
            Assert.That(exact.LevelAfter, Is.EqualTo(level+1)); Assert.That(exact.XPAfter, Is.Zero);
            p.SetProgress(5,0,level);
            var excess = system.GrantXP(p,requirement+1);
            Assert.That(excess.LevelAfter, Is.EqualTo(level+1)); Assert.That(excess.XPAfter, Is.EqualTo(level==9 ? 0 : 1));
        }
        [Test]
        public void DefaultPurchaseCanAdvanceTwoLevelsAndPreservesOtherState()
        {
            var m = Prepared(); var p = m.GetPlayer("p1"); var other = State(m.GetPlayer("p2"));
            var change = new LevelSystem().BuyXP(m,"p1");
            Assert.That(new[] {change.GoldBefore,change.GoldAfter,change.GoldSpent,change.LevelBefore,change.LevelAfter,change.XPGranted,change.XPAfter}, Is.EqualTo(new[] {5,1,4,1,3,4,0}));
            Assert.That(p.BoardCapacity, Is.EqualTo(3)); Assert.That(p.LastAutomaticXPRound, Is.Zero);
            Assert.That(p.HP, Is.EqualTo(60)); Assert.That(State(m.GetPlayer("p2")), Is.EqualTo(other));
        }
        [Test]
        public void RemainderAndLargeGrantsReachMaximumSafely()
        {
            var p = Match().GetPlayer("p1"); var system = new LevelSystem();
            p.SetProgress(20,4,3);
            var change = system.GrantXP(p,4);
            Assert.That(new[] {p.Level,p.XP,p.Gold}, Is.EqualTo(new[] {4,2,20}));
            p.SetProgress(20,int.MaxValue,1);
            system.GrantXP(p,int.MaxValue);
            Assert.That(new[] {p.Level,p.XP,p.Gold,p.BoardCapacity}, Is.EqualTo(new[] {10,0,20,10}));
            Assert.That(system.GrantXP(p,2).XPGranted, Is.Zero);
        }
        [Test]
        public void MaximumPurchaseDoesNotChargeGold()
        {
            var m = Prepared(); var p=m.GetPlayer("p1"); p.SetProgress(20,67,9);
            new LevelSystem().BuyXP(m,"p1");
            Assert.That(new[] {p.Gold,p.Level,p.XP}, Is.EqualTo(new[] {16,10,0}));
            var before=State(p);
            Assert.Throws<InvalidOperationException>(() => new LevelSystem().BuyXP(m,"p1"));
            Assert.That(State(p), Is.EqualTo(before));
        }
        [TestCase(MatchPhase.Waiting)] [TestCase(MatchPhase.Starting)] [TestCase(MatchPhase.Combat)]
        [TestCase(MatchPhase.Result)] [TestCase(MatchPhase.Finished)]
        public void PurchaseRejectsAllOtherPhases(MatchPhase phase)
        {
            var m=Match();
            if(phase!=MatchPhase.Waiting) m.TransitionTo(MatchPhase.Starting);
            if(phase==MatchPhase.Combat || phase==MatchPhase.Result) {m.TransitionTo(MatchPhase.Preparation);m.TransitionTo(MatchPhase.Combat);}
            if(phase==MatchPhase.Result)m.TransitionTo(MatchPhase.Result);
            if(phase==MatchPhase.Finished)m.TransitionTo(MatchPhase.Finished);
            var p=m.GetPlayer("p1");var before=State(p);
            Assert.Throws<InvalidOperationException>(() => new LevelSystem().BuyXP(m,"p1"));
            Assert.That(State(p), Is.EqualTo(before));
        }
        [Test]
        public void PurchaseValidatesFundsPlayerAndMatchAtomically()
        {
            var m=Prepared();var p=m.GetPlayer("p1");p.SetProgress(3,1,1);var before=State(p);var system=new LevelSystem();
            Assert.Throws<InvalidOperationException>(() => system.BuyXP(m,"p1"));
            Assert.Throws<KeyNotFoundException>(() => system.BuyXP(m,"missing"));
            Assert.Throws<ArgumentNullException>(() => system.BuyXP(null,"p1"));
            Assert.That(State(p), Is.EqualTo(before));
            p.SetProgress(4,0,1);system.BuyXP(m,"p1");Assert.That(p.Gold, Is.Zero);
        }
        [Test]
        public void AutomaticXPIsSequentialAndIndependentOfEconomyAndPurchases()
        {
            var m=Prepared();var p=m.GetPlayer("p1");var levels=new LevelSystem();
            Assert.That(new[] {p.Level,p.XP,p.LastAutomaticXPRound}, Is.EqualTo(new[] {1,0,0}));
            m.TransitionTo(MatchPhase.Combat);m.TransitionTo(MatchPhase.Result);
            new EconomySystem().Settle(p,1,RoundOutcome.Win);
            levels.AwardAutomaticXP(p,1);
            Assert.That(new[] {p.Gold,p.Level,p.XP,p.LastEconomyRound,p.LastAutomaticXPRound,p.WinStreak}, Is.EqualTo(new[] {10,2,0,1,1,1}));
            m.TransitionTo(MatchPhase.Preparation);levels.BuyXP(m,"p1");
            Assert.That(new[] {p.Gold,p.Level,p.XP,p.LastAutomaticXPRound}, Is.EqualTo(new[] {6,3,2,1}));
            var before=State(p);
            Assert.Throws<InvalidOperationException>(() => new LevelSystem().AwardAutomaticXP(p,1));
            Assert.Throws<InvalidOperationException>(() => levels.AwardAutomaticXP(p,3));
            Assert.Throws<ArgumentOutOfRangeException>(() => levels.AwardAutomaticXP(p,0));
            Assert.That(State(p), Is.EqualTo(before));
            levels.AwardAutomaticXP(p,2);before=State(p);
            Assert.Throws<InvalidOperationException>(() => levels.AwardAutomaticXP(p,1));
            Assert.That(State(p), Is.EqualTo(before));
            Assert.That(m.GetPlayer("p2").LastAutomaticXPRound, Is.Zero);
        }
        [Test]
        public void MaxLevelStillRecordsAutomaticRound()
        {
            var p=Match().GetPlayer("p1");p.SetProgress(5,0,10);
            var system=new LevelSystem();var change=system.AwardAutomaticXP(p,1);
            Assert.That(change.XPGranted, Is.Zero);Assert.That(p.LastAutomaticXPRound, Is.EqualTo(1));
            system.AwardAutomaticXP(p,2);Assert.That(p.XP, Is.Zero);
        }
        [Test]
        public void LevelRulesAreCopiedValidatedAndMatchConfigurationMustAgree()
        {
            var requirements=new[] {2,3};var rules=new LevelRules(requirements,2,1,0);requirements[0]=99;
            Assert.That(rules.XPToNextLevel(1), Is.EqualTo(2));Assert.That(rules.XPToNextLevel(3), Is.Zero);
            Assert.Throws<NotSupportedException>(() => ((IList<int>)rules.Requirements)[0]=99);
            Assert.Throws<ArgumentException>(() => new LevelRules(new[] {0}));
            Assert.Throws<ArgumentException>(() => new LevelRules(new[] {-1}));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LevelRules(purchaseGoldCost:0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LevelRules(purchaseXP:0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LevelRules(automaticXP:-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => rules.XPToNextLevel(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => rules.XPToNextLevel(4));
            var mismatch=Match().GetPlayer("p1");var before=State(mismatch);
            Assert.Throws<ArgumentException>(() => new LevelSystem(rules).GrantXP(mismatch,2));Assert.That(State(mismatch), Is.EqualTo(before));
            var m=Match(new MatchRules(maxLevel:3));m.TransitionTo(MatchPhase.Starting);m.TransitionTo(MatchPhase.Preparation);
            var system=new LevelSystem(rules);system.BuyXP(m,"p1");
            Assert.That(new[] {m.GetPlayer("p1").Gold,m.GetPlayer("p1").Level,m.GetPlayer("p1").XP}, Is.EqualTo(new[] {3,1,1}));
            system.AwardAutomaticXP(m.GetPlayer("p1"),1);Assert.That(m.GetPlayer("p1").LastAutomaticXPRound, Is.EqualTo(1));
        }
        [Test]
        public void InvalidGrantsAndMaximumXPStorageAreRejected()
        {
            var p=Match().GetPlayer("p1");var before=State(p);var system=new LevelSystem();
            Assert.Throws<ArgumentNullException>(() => system.GrantXP(null,2));
            Assert.Throws<ArgumentNullException>(() => system.AwardAutomaticXP(null,1));
            Assert.Throws<ArgumentOutOfRangeException>(() => system.GrantXP(p,-1));
            Assert.Throws<ArgumentException>(() => p.SetProgress(20,1,10));
            Assert.Throws<ArgumentException>(() => new MatchRules(startingLevel:10,startingXP:1));
            Assert.That(State(p), Is.EqualTo(before));
        }
        [Test]
        public void DeploymentLimitCannotBeBypassedAndExistingMovesRemainAllowed()
        {
            var p=Match().GetPlayer("p1");
            var a=p.AddUnit(Unit("a",UnitPlacement.OnBoard(new BoardPosition(0,0))));
            var b=p.AddUnit(Unit("b",UnitPlacement.OnBench(0)));
            var c=p.AddUnit(Unit("c",UnitPlacement.Unplaced));
            Assert.Throws<InvalidOperationException>(() => b.SetPlacement(UnitPlacement.OnBoard(new BoardPosition(1,0))));
            Assert.Throws<InvalidOperationException>(() => p.SetPlacement("c",UnitPlacement.OnBoard(new BoardPosition(1,0))));
            Assert.Throws<InvalidOperationException>(() => p.AddUnit(Unit("d",UnitPlacement.OnBoard(new BoardPosition(1,0)))));
            a.SetPlacement(UnitPlacement.OnBoard(new BoardPosition(2,0)));
            a.SetPlacement(a.Placement);
            Assert.That(p.Board.Count, Is.EqualTo(1));Assert.That(b.Placement.BenchSlot, Is.EqualTo(0));
            a.SetPlacement(UnitPlacement.Unplaced);b.SetPlacement(UnitPlacement.OnBoard(new BoardPosition(0,0)));
            new LevelSystem().GrantXP(p,2);c.SetPlacement(UnitPlacement.OnBoard(new BoardPosition(1,0)));
            Assert.That(p.BoardCapacity, Is.EqualTo(2));Assert.That(p.Board.Count, Is.EqualTo(2));
            var before=State(p);Assert.Throws<InvalidOperationException>(() => p.SetProgress(20,0,1));Assert.That(State(p), Is.EqualTo(before));
            p.RemoveUnit("b");p.SetPlacement("a",UnitPlacement.OnBoard(new BoardPosition(0,0)));
        }
        [Test]
        public void AutomaticXPReachesLevelTenAcrossSequentialRounds()
        {
            var p=Match().GetPlayer("p1");var system=new LevelSystem();
            // Total XP for 1 -> 10 is 272, so +2 XP reaches max at round 136.
            for(int round=1;round<=135;round++)system.AwardAutomaticXP(p,round);
            Assert.That(new[] {p.Level,p.XP}, Is.EqualTo(new[] {9,66}));
            system.AwardAutomaticXP(p,136);Assert.That(new[] {p.Level,p.XP,p.BoardCapacity}, Is.EqualTo(new[] {10,0,10}));
        }
    }
}
