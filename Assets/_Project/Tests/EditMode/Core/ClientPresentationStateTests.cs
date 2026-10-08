using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;
namespace PokeChess.Core.Tests
{
    public sealed class ClientPresentationStateTests
    {
        private MatchState match;private LocalRoundCoordinator rounds;private HostCommandProcessor host;
        [SetUp] public void Setup(){var catalog=PrototypeRoster.CreateCatalog();match=MatchStateFactory.CreateWithPool("presentation",new[]{"p1","p2","p3","p4"},catalog,PrototypeRoster.CreateDefinitions().Select(d=>d.Id),matchSeed:58);match.TransitionTo(MatchPhase.Starting);match.TransitionTo(MatchPhase.Preparation);foreach(var p in match.Players)new ShopSystem().RefreshForRound(match,p.PlayerId);rounds=new LocalRoundCoordinator(match,catalog,skillCatalog:PrototypeRoster.CreateSkills(),statusCatalog:PrototypeRoster.CreateStatuses());host=new HostCommandProcessor(match,catalog,rounds);}
        private void Add(string player,string definition="mankey",UnitRank rank=UnitRank.One){SharedPoolSystem.RegisterUnit(match,PrototypeRoster.CreateCatalog().CreateUnit("u-"+player,definition,player,rank,0,UnitPlacement.OnBench(0)),definition);}
        [Test] public void HostProjectsRankedSalePriceAndPublicMaximumHPWithoutPrivateFields()
        {Add("p1","geodude",UnitRank.Two);Assert.That(host.Snapshot("p1").units.Single().saleGold,Is.EqualTo(5));Assert.That(host.PublicSnapshot().players.Single(p=>p.id=="p1").maxHP,Is.EqualTo(60));var json=JsonUtility.ToJson(host.PublicSnapshot());foreach(var field in new[]{"saleGold","gold","shop","inventory","winStreak","loseStreak"})Assert.That(json,Does.Not.Contain("\""+field+"\""));}
        [Test] public void RewardSnapshotContainsActualDamageIncomeXPAndOwnerStreak()
        {Add("p1");rounds.StartCombat(1);for(int i=0;i<1400&&match.Phase==MatchPhase.Combat;i++)rounds.Step();var winner=match.Players.First(p=>p.WinStreak>0);var loser=match.Players.First(p=>p.LoseStreak>0);var own=host.Snapshot(loser.PlayerId);Assert.That(own.reward.round,Is.EqualTo(1));Assert.That(own.reward.hpBefore,Is.EqualTo(60));Assert.That(own.reward.hpAfter,Is.EqualTo(own.hp));Assert.That(own.reward.damage,Is.EqualTo(60-own.hp));Assert.That(own.reward.xpGranted,Is.EqualTo(2));Assert.That(own.reward.levelBefore,Is.EqualTo(1));Assert.That(own.reward.levelAfter,Is.EqualTo(2));Assert.That(own.loseStreak,Is.EqualTo(1));Assert.That(host.Snapshot(winner.PlayerId).winStreak,Is.EqualTo(1));}
        [Test] public void SelectedPublicCombatPlaybackRejectsOtherPairingAndCarriesHostTimeLimits()
        {foreach(var p in match.Players)Add(p.PlayerId);var runtime=new OnlineCombatRuntime(rounds,host);var frames=new List<CombatFrame>();runtime.FrameProduced+=frames.Add;runtime.StartCombat(1);Assert.That(frames.Count,Is.EqualTo(2));var selected=frames[0];var other=frames[1];var playback=new CombatPlayback();Assert.That(playback.Accept(selected,match.MatchId,selected.one,1),Is.True);Assert.That(playback.Accept(other,match.MatchId,selected.one,1),Is.False);Assert.That(selected.overtimeTick,Is.EqualTo(900));Assert.That(selected.timeLimitTick,Is.GreaterThan(selected.overtimeTick));}
        [Test] public void PublicEliminationReasonAndRoundAreAvailableToResultsUI()
        {rounds.Surrender("p2",1,MatchPhase.Preparation);var p=host.PublicSnapshot().players.First(v=>v.id=="p2");Assert.That(p.eliminated,Is.True);Assert.That(p.eliminationRound,Is.EqualTo(1));Assert.That(p.eliminationReason,Is.EqualTo("Surrender"));Assert.That(p.placement,Is.EqualTo(4));}
    }
}
