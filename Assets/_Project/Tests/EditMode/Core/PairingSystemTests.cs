using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Tests
{
    public sealed class PairingSystemTests
    {
        private MatchState Create(int count,ulong seed=123,bool reverse=false)
        {
            var ids=Enumerable.Range(1,count).Select(i=>"p"+i);
            var match=MatchStateFactory.Create("pairing",reverse ? ids.Reverse() : ids,matchSeed:seed);
            match.TransitionTo(MatchPhase.Starting);match.TransitionTo(MatchPhase.Preparation);return match;
        }
        private static string Signature(RoundPairingPlan plan) =>
            string.Join("|",plan.Pairs.Select(p=>p.PlayerOneId+":"+p.PlayerTwoId))+"/"+plan.UnpairedPlayerId;
        private static void Advance(MatchState match)
        { match.TransitionTo(MatchPhase.Combat);match.TransitionTo(MatchPhase.Result);match.TransitionTo(MatchPhase.Preparation); }
        [TestCase(2)][TestCase(4)][TestCase(6)][TestCase(8)]
        public void EvenSurvivorsAppearExactlyOnce(int count)
        {
            var match=Create(count);var plan=new PairingSystem().PrepareRound(match,1);
            Assert.That(plan.Pairs.Count,Is.EqualTo(count/2));Assert.That(plan.UnpairedPlayerId,Is.Null);
            Assert.That(plan.Pairs.SelectMany(p=>new[]{p.PlayerOneId,p.PlayerTwoId}).OrderBy(x=>x),Is.EqualTo(plan.SurvivorIds));
            Assert.That(plan.Pairs.All(p=>p.PlayerOneId!=p.PlayerTwoId),Is.True);
        }
        [TestCase(3)][TestCase(5)][TestCase(7)]
        public void OddSurvivorsHaveExplicitShadowRecipient(int count)
        {
            var plan=new PairingSystem().PrepareRound(Create(count),1);
            Assert.That(plan.RequiresShadow,Is.True);Assert.That(plan.Pairs.Count,Is.EqualTo(count/2));
            var participants=plan.Pairs.SelectMany(p=>new[]{p.PlayerOneId,p.PlayerTwoId}).Concat(new[]{plan.UnpairedPlayerId}).ToArray();
            Assert.That(participants.Distinct().Count(),Is.EqualTo(count));Assert.That(participants.OrderBy(x=>x),Is.EqualTo(plan.SurvivorIds));
        }
        [TestCase(0)][TestCase(1)]
        public void ZeroOrOneSurvivorRequiresNoBattle(int count)
        {
            var match=Create(2);foreach(var p in match.Players.Skip(count))p.SetHP(0);
            var plan=new PairingSystem().PrepareRound(match,1);
            Assert.That(plan.NoBattleRequired,Is.True);Assert.That(plan.RequiresShadow,Is.False);Assert.That(plan.Pairs,Is.Empty);
        }
        [Test] public void EliminatedAndZeroHpPlayersAreExcluded()
        {
            var match=Create(4);match.GetPlayer("p3").SetHP(0);
            typeof(PlayerState).GetMethod("MarkEliminated",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(match.GetPlayer("p4"),null);
            var plan=new PairingSystem().PrepareRound(match,1);
            Assert.That(plan.SurvivorIds,Is.EqualTo(new[]{"p1","p2"}));
        }
        [TestCase(2)][TestCase(4)][TestCase(6)][TestCase(8)]
        public void SameSeedIgnoresInputOrderAcrossRounds(int count)
        {
            var a=Create(count);var b=Create(count,reverse:true);var system=new PairingSystem();
            for(int round=1;round<=12;round++)
            {
                Assert.That(Signature(system.PrepareRound(a,round)),Is.EqualTo(Signature(system.PrepareRound(b,round))));
                Advance(a);Advance(b);
            }
        }
        [Test] public void FourPlayersAvoidBothPreviousRoundsWhenPossible()
        {
            var match=Create(4);var system=new PairingSystem();var opponents=new Dictionary<string,HashSet<string>>();
            foreach(var p in match.Players)opponents[p.PlayerId]=new HashSet<string>();
            for(int round=1;round<=3;round++)
            {
                var plan=system.PrepareRound(match,round);
                Assert.That(plan.PreviousRoundRematches,Is.Zero);Assert.That(plan.TwoRoundsAgoRematches,Is.Zero);
                foreach(var p in plan.Pairs) { Assert.That(opponents[p.PlayerOneId].Add(p.PlayerTwoId),Is.True);Assert.That(opponents[p.PlayerTwoId].Add(p.PlayerOneId),Is.True); }
                Advance(match);
            }
        }
        [Test] public void TwoPlayersAllowUnavoidableRematches()
        {
            var match=Create(2);var system=new PairingSystem();system.PrepareRound(match,1);Advance(match);
            Assert.That(system.PrepareRound(match,2).PreviousRoundRematches,Is.EqualTo(1));Advance(match);
            var plan=system.PrepareRound(match,3);Assert.That(plan.PreviousRoundRematches,Is.EqualTo(1));Assert.That(plan.TwoRoundsAgoRematches,Is.EqualTo(1));
        }
        [Test] public void SameRoundPreparationIsIdempotentAndRosterChangeReplacesHistory()
        {
            var match=Create(6);var system=new PairingSystem();var original=system.PrepareRound(match,1);
            Assert.That(system.PrepareRound(match,1),Is.SameAs(original));
            match.GetPlayer("p5").SetHP(0);match.GetPlayer("p6").SetHP(0);
            var updated=system.PrepareRound(match,1);
            Assert.That(updated.SurvivorIds.Count,Is.EqualTo(4));Assert.That(updated.PreviousRoundRematches,Is.Zero);
            Assert.That(match.PairingHistory.Count,Is.EqualTo(1));Assert.That(match.PairingHistory[1],Is.SameAs(updated));
            Assert.That(system.PrepareRound(match,1),Is.SameAs(updated));
        }
        [Test] public void HistoryKeepsOnlyCurrentAndTwoPriorRounds()
        {
            var match=Create(4);var system=new PairingSystem();
            for(int round=1;round<=10;round++) { system.PrepareRound(match,round);if(round<10)Advance(match); }
            Assert.That(match.PairingHistory.Keys.OrderBy(x=>x),Is.EqualTo(new[]{8,9,10}));
        }
        [Test] public void StaleOrNonPreparationRequestsCannotMutateHistory()
        {
            var match=Create(4);var system=new PairingSystem();
            Assert.Throws<InvalidOperationException>(()=>system.PrepareRound(match,0));Assert.That(match.PairingHistory,Is.Empty);
            var plan=system.PrepareRound(match,1);match.TransitionTo(MatchPhase.Combat);
            Assert.Throws<InvalidOperationException>(()=>system.PrepareRound(match,1));Assert.That(match.PairingPlan,Is.SameAs(plan));
        }
        // Independent bitmask oracle verifies global lexicographic optimum, including survivor reductions.
        [Test] public void MatchingAchievesGlobalMinimumAfterRosterChanges()
        {
            for(ulong seed=0;seed<20;seed++)
            {
                var match=Create(8,seed);var system=new PairingSystem();
                for(int round=1;round<=8;round++)
                {
                    if(round==4) { match.GetPlayer("p7").SetHP(0);match.GetPlayer("p8").SetHP(0); }
                    if(round==6) { match.GetPlayer("p5").SetHP(0);match.GetPlayer("p6").SetHP(0); }
                    var plan=system.Calculate(match,round);
                    int optimum=Oracle(match,plan.SurvivorIds,round,(1<<plan.SurvivorIds.Count)-1,new Dictionary<int,int>());
                    Assert.That(plan.PreviousRoundRematches*100+plan.TwoRoundsAgoRematches,Is.EqualTo(optimum));
                    system.PrepareRound(match,round);Advance(match);
                }
            }
        }
        private static int Oracle(MatchState match,IReadOnlyList<string> ids,int round,int mask,Dictionary<int,int> memo)
        {
            if(mask==0)return 0;if(memo.TryGetValue(mask,out int known))return known;
            int first=0;while((mask&(1<<first))==0)first++;
            int best=int.MaxValue;
            for(int other=first+1;other<ids.Count;other++)if((mask&(1<<other))!=0)
            {
                int cost=0;
                for(int ago=1;ago<=2;ago++)if(match.PairingHistory.TryGetValue(round-ago,out var prior) && prior.Pairs.Any(p=>p.Contains(ids[first]) && p.Contains(ids[other])))cost+=ago==1 ? 100 : 1;
                best=Math.Min(best,cost+Oracle(match,ids,round,mask^(1<<first)^(1<<other),memo));
            }
            return memo[mask]=best;
        }
    }
}

