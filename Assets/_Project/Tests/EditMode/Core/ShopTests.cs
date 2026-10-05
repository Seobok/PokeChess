using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Tests
{
    public sealed class ShopTests
    {
        private static PokemonDefinition Definition(int cost, string suffix="a") => new PokemonDefinition("cost"+cost+suffix,
            "Test",cost,new PokemonStats(100,10,0,1,0,0,1,2,100,20,.25f),"role","skill");
        private static IShopCandidateSource Source(bool reverse=false)
        {
            var definitions=Enumerable.Range(1,5).SelectMany(c=>new[]{Definition(c),Definition(c,"b")}).ToArray();
            return new CatalogShopCandidateSource(new PokemonCatalog(definitions),
                (reverse ? definitions.Reverse() : definitions).Select(d=>d.Id));
        }
        private static MatchState Prepared(ulong seed=123, bool reverse=false)
        {
            var m=MatchStateFactory.Create("match",reverse ? new[]{"p2","p1"}:new[]{"p1","p2"},matchSeed:seed);
            m.TransitionTo(MatchPhase.Starting);m.TransitionTo(MatchPhase.Preparation);return m;
        }
        private static void NextRound(MatchState m)
        {m.TransitionTo(MatchPhase.Combat);m.TransitionTo(MatchPhase.Result);m.TransitionTo(MatchPhase.Preparation);}
        private static string Snapshot(PlayerState p) => string.Join("|",p.Shop.Slots.Select(s=>s.Index+":"+s.DefinitionId+":"+s.Cost))
            +"|"+p.Gold+"|"+p.XP+"|"+p.Level+"|"+p.Shop.IsLocked+"|"+p.Shop.IsInitialized+"|"+p.Shop.LastRefreshRound
            +"|"+p.Shop.RandomState+"|"+p.Shop.RandomDrawCount;
        private sealed class BadSource : IShopCandidateSource
        {
            public int Mode;
            public IReadOnlyList<PokemonDefinition> GetCandidates(int cost)
            {
                if(cost==2)
                {
                    if(Mode==0) return Array.Empty<PokemonDefinition>();
                    if(Mode==1) return null;
                    if(Mode==2) return new[]{Definition(1)};
                    if(Mode==3) return new[]{Definition(2),Definition(2)};
                    if(Mode==4) return new PokemonDefinition[]{null};
                    throw new InvalidOperationException("Candidate source unavailable.");
                }
                return new[]{Definition(cost)};
            }
        }
        [TestCase(1,100,0,0,0,0)] [TestCase(2,100,0,0,0,0)] [TestCase(3,75,25,0,0,0)]
        [TestCase(4,55,30,15,0,0)] [TestCase(5,45,33,20,2,0)] [TestCase(6,30,40,25,5,0)]
        [TestCase(7,19,35,35,10,1)] [TestCase(8,18,25,36,18,3)] [TestCase(9,10,20,25,35,10)]
        [TestCase(10,5,10,20,40,25)]
        public void AllLevelsUseExactCumulativeIntervals(int level,int a,int b,int c,int d,int e)
        {
            var rules=new ShopRules();var expected=new[]{a,b,c,d,e};
            Assert.That(rules.ForLevel(level),Is.EqualTo(expected));
            int start=0;
            for(int cost=1;cost<=5;cost++)
            {
                for(int roll=start;roll<start+expected[cost-1];roll++) Assert.That(rules.SelectCost(level,roll),Is.EqualTo(cost));
                start+=expected[cost-1];
            }
            Assert.That(start,Is.EqualTo(100));
            var m=Prepared();var p=m.GetPlayer("p1");p.SetProgress(5,0,level);
            new ShopSystem(Source()).RefreshForRound(m,"p1");
            Assert.That(p.Shop.Slots.Count,Is.EqualTo(5));
            foreach(var slot in p.Shop.Slots)
            {Assert.That(slot.IsEmpty,Is.False);Assert.That(slot.DefinitionId,Does.StartWith("cost"+slot.Cost));Assert.That(expected[slot.Cost-1],Is.GreaterThan(0));}
        }
        [Test]
        public void InitialShopIsEmptyAndFirstRefreshIsFree()
        {
            var m=Prepared();var p=m.GetPlayer("p1");var shop=new ShopSystem(Source());
            Assert.That(p.Shop.IsInitialized,Is.False);Assert.That(p.Shop.Slots.Select(s=>s.Index),Is.EqualTo(new[]{0,1,2,3,4}));
            Assert.That(p.Shop.Slots.All(s=>s.IsEmpty),Is.True);Assert.That(p.Shop.RandomDrawCount,Is.Zero);
            var before=p.Shop.RandomState;
            Assert.That(shop.RefreshForRound(m,"p1"),Is.True);
            Assert.That(p.Gold,Is.EqualTo(5));Assert.That(p.Shop.LastRefreshRound,Is.EqualTo(1));
            Assert.That(p.Shop.RandomState,Is.Not.EqualTo(before));Assert.That(p.Shop.RandomDrawCount,Is.GreaterThanOrEqualTo(5));
            Assert.That(m.GetPlayer("p2").Shop.IsInitialized,Is.False);
        }
        [Test]
        public void SameSeedAndReversedRegistrationsReproduceEveryRefresh()
        {
            var m=Prepared();var other=Prepared(reverse:true);var system=new ShopSystem(Source());var reversed=new ShopSystem(Source(true));
            system.RefreshForRound(m,"p1");reversed.RefreshForRound(other,"p1");
            Assert.That(Snapshot(m.GetPlayer("p1")),Is.EqualTo(Snapshot(other.GetPlayer("p1"))));
            for(int i=0;i<8;i++)
            {
                m.GetPlayer("p1").SetProgress(100,0,8);other.GetPlayer("p1").SetProgress(100,0,8);
                system.Reroll(m,"p1");reversed.Reroll(other,"p1");
                Assert.That(Snapshot(m.GetPlayer("p1")),Is.EqualTo(Snapshot(other.GetPlayer("p1"))));
            }
            Assert.That(Prepared(124).GetPlayer("p1").Shop.RandomState,Is.Not.EqualTo(Prepared(123).GetPlayer("p1").Shop.RandomState));
        }
        [Test]
        public void PlayerRandomStreamsRemainIndependent()
        {
            var m=Prepared();var control=Prepared();var shop=new ShopSystem(Source());
            shop.RefreshForRound(m,"p1");shop.Reroll(m,"p1");
            shop.RefreshForRound(m,"p2");shop.RefreshForRound(control,"p2");
            Assert.That(Snapshot(m.GetPlayer("p2")),Is.EqualTo(Snapshot(control.GetPlayer("p2"))));
            Assert.That(m.GetPlayer("p1").Shop.RandomState,Is.Not.EqualTo(m.GetPlayer("p2").Shop.RandomState));
        }
        [Test]
        public void RerollChargesExactlyTwoAndOldSlotSnapshotsRemainStable()
        {
            var m=Prepared();var p=m.GetPlayer("p1");var system=new ShopSystem(Source());system.RefreshForRound(m,"p1");
            var old=p.Shop.Slots;var ids=old.Select(s=>s.DefinitionId).ToArray();long draws=p.Shop.RandomDrawCount;
            var current=system.Reroll(m,"p1");Assert.That(p.Gold,Is.EqualTo(3));
            Assert.That(current,Is.SameAs(p.Shop.Slots));Assert.That(current,Is.Not.SameAs(old));
            Assert.That(old.Select(s=>s.DefinitionId),Is.EqualTo(ids));Assert.That(p.Shop.RandomDrawCount,Is.GreaterThan(draws));
            Assert.That(p.Shop.LastRefreshRound,Is.EqualTo(1));
            p.SetProgress(2,0,1);system.Reroll(m,"p1");Assert.That(p.Gold,Is.Zero);
            var before=Snapshot(p);Assert.Throws<InvalidOperationException>(()=>system.Reroll(m,"p1"));Assert.That(Snapshot(p),Is.EqualTo(before));
        }
        [Test]
        public void PersistentLockSkipsAutomaticRefreshAndAllowsManualReroll()
        {
            var m=Prepared();var p=m.GetPlayer("p1");var system=new ShopSystem(Source());system.RefreshForRound(m,"p1");
            ulong state=p.Shop.RandomState;long draws=p.Shop.RandomDrawCount;int gold=p.Gold;
            system.SetLocked(m,"p1",true);system.SetLocked(m,"p1",true);
            Assert.That(p.Shop.RandomState,Is.EqualTo(state));Assert.That(p.Gold,Is.EqualTo(gold));
            var old=p.Shop.Slots;NextRound(m);
            Assert.That(system.RefreshForRound(m,"p1"),Is.False);Assert.That(p.Shop.Slots,Is.SameAs(old));
            Assert.That(p.Shop.RandomDrawCount,Is.EqualTo(draws));Assert.That(p.Shop.LastRefreshRound,Is.EqualTo(2));
            var before=Snapshot(p);Assert.Throws<InvalidOperationException>(()=>system.RefreshForRound(m,"p1"));Assert.That(Snapshot(p),Is.EqualTo(before));
            system.Reroll(m,"p1");Assert.That(p.Shop.IsLocked,Is.True);Assert.That(p.Gold,Is.EqualTo(gold-2));
            system.SetLocked(m,"p1",false);NextRound(m);Assert.That(system.RefreshForRound(m,"p1"),Is.True);
        }
        [Test]
        public void RefreshDuplicateAndSkippedRoundsAreRejectedWithoutChanges()
        {
            var m=Prepared();var p=m.GetPlayer("p1");var system=new ShopSystem(Source());system.RefreshForRound(m,"p1");
            var before=Snapshot(p);Assert.Throws<InvalidOperationException>(()=>new ShopSystem(Source()).RefreshForRound(m,"p1"));Assert.That(Snapshot(p),Is.EqualTo(before));
            NextRound(m);NextRound(m);before=Snapshot(p);
            Assert.Throws<InvalidOperationException>(()=>system.RefreshForRound(m,"p1"));Assert.That(Snapshot(p),Is.EqualTo(before));
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)]
        public void InvalidCandidateSourceDoesNotCommitGoldSlotsRandomOrRound(int mode)
        {
            var m=Prepared();var p=m.GetPlayer("p1");var valid=new ShopSystem(Source());valid.RefreshForRound(m,"p1");
            p.SetProgress(10,0,3);var invalid=new ShopSystem(new BadSource{Mode=mode});var before=Snapshot(p);
            Assert.Catch(()=>invalid.Reroll(m,"p1"));Assert.That(Snapshot(p),Is.EqualTo(before));
            NextRound(m);Assert.Catch(()=>invalid.RefreshForRound(m,"p1"));Assert.That(Snapshot(p),Is.EqualTo(before));
            Assert.That(valid.RefreshForRound(m,"p1"),Is.True);
        }
        [Test]
        public void MissingZeroProbabilityCostsAreAllowedAndSamePokemonCanRepeat()
        {
            var one=Definition(1);var source=new CatalogShopCandidateSource(new PokemonCatalog(new[]{one}),new[]{one.Id});
            var m=Prepared();var p=m.GetPlayer("p1");new ShopSystem(source).RefreshForRound(m,"p1");
            Assert.That(p.Shop.Slots.All(s=>s.DefinitionId==one.Id),Is.True);
        }
        [Test]
        public void LevelUpDoesNotAlterOffersAndNextRefreshUsesNewProbabilities()
        {
            var m=Prepared();var p=m.GetPlayer("p1");var system=new ShopSystem(Source());system.RefreshForRound(m,"p1");var old=p.Shop.Slots;
            new LevelSystem().BuyXP(m,"p1");Assert.That(p.Level,Is.EqualTo(3));Assert.That(p.Shop.Slots,Is.SameAs(old));
            m.TransitionTo(MatchPhase.Combat);m.TransitionTo(MatchPhase.Result);
            new EconomySystem().Settle(p,1,RoundOutcome.Win);new LevelSystem().AwardAutomaticXP(p,1);
            m.TransitionTo(MatchPhase.Preparation);system.RefreshForRound(m,"p1");
            Assert.That(p.Shop.LastRefreshRound,Is.EqualTo(2));Assert.That(p.Shop.Slots,Is.Not.SameAs(old));
            Assert.That(p.Shop.Slots.All(s=>s.Cost<=2),Is.True);
        }
        [TestCase(MatchPhase.Waiting)] [TestCase(MatchPhase.Starting)] [TestCase(MatchPhase.Combat)]
        [TestCase(MatchPhase.Result)] [TestCase(MatchPhase.Finished)]
        public void AllShopMutationsRequirePreparation(MatchPhase phase)
        {
            var m=Prepared();var p=m.GetPlayer("p1");var system=new ShopSystem(Source());system.RefreshForRound(m,"p1");
            if(phase==MatchPhase.Waiting || phase==MatchPhase.Starting)
            {
                m=MatchStateFactory.Create("m",new[]{"p1","p2"});if(phase==MatchPhase.Starting)m.TransitionTo(phase);p=m.GetPlayer("p1");
            }
            else {m.TransitionTo(MatchPhase.Combat);if(phase==MatchPhase.Result)m.TransitionTo(MatchPhase.Result);if(phase==MatchPhase.Finished)m.TransitionTo(MatchPhase.Finished);}
            var before=Snapshot(p);
            Assert.Throws<InvalidOperationException>(()=>system.RefreshForRound(m,"p1"));
            Assert.Throws<InvalidOperationException>(()=>system.Reroll(m,"p1"));
            Assert.Throws<InvalidOperationException>(()=>system.SetLocked(m,"p1",true));Assert.That(Snapshot(p),Is.EqualTo(before));
        }
        [Test]
        public void ManualCommandsWaitForCurrentRoundRefresh()
        {
            var m=Prepared();var p=m.GetPlayer("p1");var system=new ShopSystem(Source());
            system.RefreshForRound(m,"p1");NextRound(m);var before=Snapshot(p);
            Assert.Throws<InvalidOperationException>(()=>system.Reroll(m,"p1"));
            Assert.Throws<InvalidOperationException>(()=>system.SetLocked(m,"p1",true));
            Assert.That(Snapshot(p),Is.EqualTo(before));
            system.RefreshForRound(m,"p1");system.Reroll(m,"p1");
            Assert.That(p.Gold,Is.EqualTo(3));
        }
        [Test]
        public void RulesCandidateCatalogAndReadOnlyCollectionsAreValidated()
        {
            var rows=new[]{new[]{100,0,0,0,0}};var rules=new ShopRules(rows);rows[0][0]=0;
            Assert.That(rules.ForLevel(1)[0],Is.EqualTo(100));
            Assert.Throws<NotSupportedException>(()=>((IList<int>)rules.ForLevel(1))[0]=0);
            Assert.Throws<ArgumentException>(()=>new ShopRules(Array.Empty<int[]>()));
            Assert.Throws<ArgumentException>(()=>new ShopRules(new[]{new[]{99,0,0,0,0}}));
            Assert.Throws<ArgumentException>(()=>new ShopRules(new[]{new[]{101,-1,0,0,0}}));
            Assert.Throws<ArgumentException>(()=>new ShopRules(new[]{new[]{100,0}}));
            Assert.Throws<ArgumentException>(()=>new ShopRules(new int[][]{null}));
            Assert.Throws<ArgumentOutOfRangeException>(()=>new ShopRules(rerollGoldCost:-1));
            Assert.Throws<ArgumentOutOfRangeException>(()=>rules.SelectCost(1,100));
            Assert.Throws<ArgumentOutOfRangeException>(()=>rules.SelectCost(1,-1));
            Assert.Throws<ArgumentOutOfRangeException>(()=>rules.ForLevel(0));
            Assert.Throws<ArgumentOutOfRangeException>(()=>rules.ForLevel(2));
            var one=Definition(1);var catalog=new PokemonCatalog(new[]{one});
            Assert.Throws<ArgumentException>(()=>new CatalogShopCandidateSource(catalog,new[]{one.Id,one.Id}));
            Assert.Throws<KeyNotFoundException>(()=>new CatalogShopCandidateSource(catalog,new[]{"missing"}));
            Assert.Throws<ArgumentNullException>(()=>new CatalogShopCandidateSource(null,new[]{one.Id}));
            Assert.Throws<ArgumentNullException>(()=>new CatalogShopCandidateSource(catalog,null));
            var zero=Definition(0);Assert.Throws<ArgumentException>(()=>new CatalogShopCandidateSource(new PokemonCatalog(new[]{zero}),new[]{zero.Id}));
            var m=Prepared();var p=m.GetPlayer("p1");var system=new ShopSystem(Source());
            Assert.Throws<NotSupportedException>(()=>((IList<ShopSlot>)p.Shop.Slots).Clear());
            Assert.Throws<InvalidOperationException>(()=>system.Reroll(m,"p1"));
            Assert.Throws<InvalidOperationException>(()=>system.SetLocked(m,"p1",true));
            Assert.Throws<ArgumentNullException>(()=>system.RefreshForRound(null,"p1"));
            Assert.Throws<KeyNotFoundException>(()=>system.Reroll(m,"missing"));
            var before=Snapshot(p);Assert.Throws<ArgumentException>(()=>new ShopSystem(Source(),rules).RefreshForRound(m,"p1"));Assert.That(Snapshot(p),Is.EqualTo(before));
        }
    }
}
