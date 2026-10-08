using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using PokeChess.Core.Match;
using PokeChess.Core.Battle;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Tests
{
    public sealed class OnlineCombatTests
    {
        private MatchState match;private LocalRoundCoordinator rounds;private HostCommandProcessor commands;private OnlineCombatRuntime runtime;
        [SetUp] public void Setup(){Create(2);}
        private void Create(int count,int hp=60)
        {
            var catalog=PrototypeRoster.CreateCatalog();match=MatchStateFactory.CreateWithPool("online-combat",Enumerable.Range(1,count).Select(i=>"p"+i),catalog,PrototypeRoster.CreateDefinitions().Select(d=>d.Id),rules:new MatchRules(startingHP:hp),matchSeed:57);
            match.TransitionTo(MatchPhase.Starting);match.TransitionTo(MatchPhase.Preparation);foreach(var player in match.Players)new ShopSystem().RefreshForRound(match,player.PlayerId);
            rounds=new LocalRoundCoordinator(match,catalog,skillCatalog:PrototypeRoster.CreateSkills(),statusCatalog:PrototypeRoster.CreateStatuses());commands=new HostCommandProcessor(match,catalog,rounds);runtime=new OnlineCombatRuntime(rounds,commands,new RoundFlowRules(.2,.2));
        }
        private void Add(string player,string definition="slowpoke",UnitRank rank=UnitRank.One,string id=null)
        {
            var unit=PrototypeRoster.CreateCatalog().CreateUnit(id??"fighter-"+player,definition,player,rank,0,UnitPlacement.OnBench(0));SharedPoolSystem.RegisterUnit(match,unit,definition);
        }
        [Test] public void RosterThirtySixCasesProduceSkillsAndCompleteAuthoritativeFrames()
        {
            foreach(var definition in PrototypeRoster.CreateDefinitions())foreach(var rank in new[]{UnitRank.One,UnitRank.Two,UnitRank.Three}){
                Create(2);Add("p1",definition.Id,rank,"caster");Add("p2","slowpoke",UnitRank.One,"enemy");int skillEvents=0,frames=0;long sequence=0,eventSequence=0;CombatFrame end=null;
                runtime.FrameProduced+=f=>{Assert.That(f.sequence,Is.EqualTo(++sequence));frames++;foreach(var e in f.events){Assert.That(e.sequence,Is.GreaterThan(eventSequence));eventSequence=e.sequence;if(e.source=="caster"&&e.kind.StartsWith("Skill"))skillEvents++;}if(f.complete)end=f;};
                Assert.That(runtime.StartCombat(1),Is.True);foreach(var u in rounds.Battle.Units)u.SetVitals(u.CurrentHP,1000);
                for(int tick=0;tick<1400&&match.Phase==MatchPhase.Combat;tick++)runtime.Step();
                Assert.That(end,Is.Not.Null,definition.Id+" R"+rank);Assert.That(skillEvents,Is.GreaterThan(0),definition.Id+" R"+rank);Assert.That(frames,Is.GreaterThan(1));
                var json=JsonUtility.ToJson(end);foreach(var key in new[]{"BattleSeed","seed","RngStreams","gold","shop","inventory"})Assert.That(json,Does.Not.Contain("\""+key+"\""));match.Pool.AssertConservation(match);
            }
        }
        [Test] public void PlaybackRejectsDuplicatesForeignBattleAndOldRoundAndRecoversFromFrameGap()
        {
            Add("p1");Add("p2");var frames=new List<CombatFrame>();runtime.FrameProduced+=frames.Add;runtime.StartCombat(1);for(int i=0;i<12;i++)runtime.Step();
            var p=new CombatPlayback();Assert.That(p.Accept(frames[0],match.MatchId,"p1",1),Is.True);Assert.That(p.Accept(frames[0],match.MatchId,"other",1),Is.False);
            Assert.That(p.Accept(frames[0],match.MatchId,"p1",1),Is.False);Assert.That(p.Accept(frames[0],match.MatchId,"p1",2),Is.False);
            Assert.That(p.Accept(frames[10],match.MatchId,"p1",1),Is.True);p.Advance(1);Assert.That(p.RecoveredGaps,Is.EqualTo(1));Assert.That(p.Current.sequence,Is.GreaterThan(1));
            Assert.That(p.Accept(frames[2],match.MatchId,"p1",1),Is.False);p.Clear();Assert.That(p.Current,Is.Null);
            Assert.That(p.Accept(frames[10],match.MatchId,"p1",1),Is.True);p.Advance(0);Assert.That(p.Current.sequence,Is.EqualTo(frames[10].sequence));
        }
        [Test] public void EmptyBattlesAndOddPlayerShadowFinishWholeMatchWithoutDoubleSettlement()
        {
            Create(3,10);Add("p1");var frames=new List<CombatFrame>();runtime.FrameProduced+=frames.Add;
            for(int i=0;i<15000&&match.Phase!=MatchPhase.Finished;i++)runtime.Advance(.1);
            Assert.That(runtime.Clock.Fault,Is.Null);Assert.That(match.Phase,Is.EqualTo(MatchPhase.Finished));Assert.That(frames.Any(f=>f.shadow),Is.True);Assert.That(match.FinalResult.Standings.Count,Is.EqualTo(3));match.Pool.AssertConservation(match);
        }
        [Test] public void CombatPurchasesDoNotJoinFrozenRosterAndSurrenderPublishesEnd()
        {
            Add("p1");Add("p2");var frames=new List<CombatFrame>();runtime.FrameProduced+=frames.Add;runtime.StartCombat(1);int count=rounds.Battle.Units.Count;
            var s=commands.Snapshot("p1");var buy=new MatchCommand{commandId="buy",matchId=match.MatchId,playerId="p1",sequence=s.nextSequence,kind=MatchCommandKind.Buy,phase=s.phase,round=s.round,playerRevision=s.playerRevision,shopRevision=s.shopRevision};
            Assert.That(commands.Process("p1",buy).accepted,Is.True);Assert.That(rounds.Battle.Units.Count,Is.EqualTo(count));
            s=commands.Snapshot("p1");var surrender=new MatchCommand{commandId="surrender",matchId=match.MatchId,playerId="p1",sequence=s.nextSequence,kind=MatchCommandKind.Surrender,phase=s.phase,round=s.round,playerRevision=s.playerRevision};
            Assert.That(commands.Process("p1",surrender).accepted,Is.True);runtime.CaptureCommandEffects();Assert.That(frames.Last().complete,Is.True);Assert.That(frames.Last().reason,Is.EqualTo("Surrender"));
        }
        [Test] public void EightPlayerAllPairingsReceiveFramesAndResultHoldFinishesPlayback()
        {
            Create(8);foreach(var player in match.Players)Add(player.PlayerId);var frames=new List<CombatFrame>();runtime.FrameProduced+=frames.Add;runtime.StartCombat(1);
            Assert.That(frames.Select(f=>f.battleId).Distinct().Count(),Is.EqualTo(4));Assert.That(frames.SelectMany(f=>new[]{f.one,f.two}).Distinct().Count(),Is.EqualTo(8));
            for(int i=0;i<1400&&match.Phase==MatchPhase.Combat;i++)runtime.Step();var own=frames.Where(f=>f.one=="p1"||f.two=="p1").ToArray();var playback=new CombatPlayback();foreach(var frame in own)playback.Accept(frame,match.MatchId,"p1",1);
            for(int i=0;i<100&&playback.IsPlaying;i++)playback.Advance(.1);Assert.That(playback.IsPlaying,Is.False);Assert.That(playback.Current.complete,Is.True);
        }
    }
}
