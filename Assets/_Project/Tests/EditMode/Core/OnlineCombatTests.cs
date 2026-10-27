using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using PokeChess.Core.Match;
using PokeChess.Core.Battle;
using PokeChess.Core.Pokemon;
using PokeChess.Core.Board;

namespace PokeChess.Core.Tests
{
    public sealed class OnlineCombatTests
    {
        [Test] public void NumericStatusFramesRecoverStacksExpiryShieldAndUntargetability()
        {
            Add("p1");Add("p2");var catalog=PrototypeRoster.CreateCatalog();
            var poison=new StatusEffectDefinition("net-poison",4,behavior:StatusBehavior.Poison);
            var hide=new StatusEffectDefinition("net-hide",2,StatusTargetTeam.Ally,behavior:StatusBehavior.Untargetable);
            var frames=new List<CombatFrame>();runtime.FrameProduced+=frames.Add;runtime.StartCombat(1);
            var source=rounds.Battle.Units.First(u=>u.TeamId==1);var victim=rounds.Battle.Units.First(u=>u.TeamId==2);
            rounds.Battle.StatusEffects.Apply(poison,source.UnitInstanceId,victim.UnitInstanceId);
            rounds.Battle.StatusEffects.Apply(poison,source.UnitInstanceId,victim.UnitInstanceId);
            rounds.Battle.StatusEffects.Apply(hide,victim.UnitInstanceId,victim.UnitInstanceId);
            rounds.Battle.Shields.Add(victim,victim.UnitInstanceId,"net-shield",100,1);
            for(int i=0;i<8;i++)runtime.Step();
            var restored=JsonUtility.FromJson<CombatFrame>(JsonUtility.ToJson(frames.Last()));
            var unit=restored.units.Single(u=>u.id==victim.UnitInstanceId);Assert.That(unit.untargetable,Is.True);
            Assert.That(unit.statusDetails.Single(s=>s.definition==poison.Id).stacks,Is.EqualTo(2));
            Assert.That(unit.statusDetails.Single(s=>s.definition==poison.Id).nextTick,Is.EqualTo(30));
            Assert.That(unit.shields.Single().remaining,Is.EqualTo(100));Assert.That(unit.shields.Single().endTick,Is.EqualTo(30));
            var playback=new CombatPlayback();playback.Accept(frames[0],match.MatchId,"p1",1);Assert.That(playback.Accept(restored,match.MatchId,"p1",1),Is.True);
            Assert.That(playback.Current.units.Single(u=>u.id==victim.UnitInstanceId).statusDetails.Length,Is.EqualTo(2));
        }
        [Test] public void SpatialFramesPreserveAreaPathsAndPendingMovementsAcrossRecovery()
        {
            Add("p1");Add("p2");var catalog=PrototypeRoster.CreateCatalog();
            var area=new SkillEffectDefinition(SkillEffectType.Damage,EffectTargetSelector.Spatial,1,
                spatial:new SkillSpatialDefinition(center:SkillAreaCenter.DensestEnemies,extent:1));
            var shot=new SkillEffectDefinition(SkillEffectType.PathProjectileDamage,EffectTargetSelector.Spatial,1,
                projectileSpeed:1,spatial:new SkillSpatialDefinition(shape:SkillAreaShape.Line,extent:10));
            var move=new SkillEffectDefinition(SkillEffectType.Move,EffectTargetSelector.CastTarget,0,
                movement:new SkillMovementDefinition(SkillMovementKind.DashToTarget,speed:1));
            var skill=new SkillDefinition(catalog.Get("slowpoke").SkillId,0,0,SkillTargetRule.Enemy,TargetLostPolicy.Cancel,
                Array.Empty<SkillEffectDefinition>(),range:56,steps:new[]{
                    new SkillStepDefinition(new[]{area},interval:1,count:3,lifetime:SkillStepLifetime.Independent,firstDelay:1),
                    new SkillStepDefinition(new[]{area,shot,move})});
            rounds=new LocalRoundCoordinator(match,catalog,skillCatalog:new SkillCatalog(new[]{skill}));
            commands=new HostCommandProcessor(match,catalog,rounds);runtime=new OnlineCombatRuntime(rounds,commands);
            var frames=new List<CombatFrame>();runtime.FrameProduced+=frames.Add;
            Assert.That(runtime.StartCombat(1),Is.True);foreach(var u in rounds.Battle.Units)u.SetVitals(u.CurrentHP,100);
            for(int i=0;i<12;i++)runtime.Step();
            var initial=frames.First(f=>f.movements.Length>0);Assert.That(initial.events.Any(e=>e.area!=null),Is.True);
            var frame=JsonUtility.FromJson<CombatFrame>(JsonUtility.ToJson(frames.Last()));
            Assert.That(frame.movements.Length,Is.EqualTo(2));Assert.That(frame.movements[0].path.Length,Is.GreaterThan(1));
            Assert.That(frame.pathProjectiles.Length,Is.EqualTo(2));Assert.That(frame.pathProjectiles[0].path.Length,Is.GreaterThan(1));
            Assert.That(frame.independentSkills[0].areas.Length,Is.EqualTo(1));
            Assert.That(frame.independentSkills[0].areas[0].cells.Length,Is.GreaterThan(0));
            var playback=new CombatPlayback();playback.Accept(frames[0],match.MatchId,"p1",1);
            Assert.That(playback.Accept(frame,match.MatchId,"p1",1),Is.True);Assert.That(playback.RecoveredGaps,Is.EqualTo(1));
            Assert.That(playback.Current.movements[0].endTick,Is.EqualTo(frame.movements[0].endTick));
        }
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
        [Test] public void IndependentSkillFramesRoundTripAndRecoverAcrossFrameGaps()
        {
            Add("p1");Add("p2");var catalog=PrototypeRoster.CreateCatalog();
            var skill=new SkillDefinition(catalog.Get("slowpoke").SkillId,0,0,SkillTargetRule.Self,TargetLostPolicy.Cancel,
                Array.Empty<SkillEffectDefinition>(),steps:new[]{new SkillStepDefinition(new[]{
                    new SkillEffectDefinition(SkillEffectType.Damage,EffectTargetSelector.EnemiesAroundCaster,5,radius:20)},
                    interval:1,count:3,lifetime:SkillStepLifetime.Independent,firstDelay:1)});
            rounds=new LocalRoundCoordinator(match,catalog,skillCatalog:new SkillCatalog(new[]{skill}));
            commands=new HostCommandProcessor(match,catalog,rounds);runtime=new OnlineCombatRuntime(rounds,commands);
            var frames=new List<CombatFrame>();runtime.FrameProduced+=frames.Add;
            runtime.StartCombat(1);foreach(var u in rounds.Battle.Units)u.SetVitals(u.CurrentHP,100);
            for(int i=0;i<12;i++)runtime.Step();
            var first=frames.First(f=>f.independentSkills.Length>0);
            Assert.That(first.independentSkills.Length,Is.EqualTo(2));
            Assert.That(first.events.Any(e=>e.kind=="IndependentSkillCreated"&&e.castId>0&&e.effectId>0),Is.True);
            var restored=JsonUtility.FromJson<CombatFrame>(JsonUtility.ToJson(frames.Last()));
            Assert.That(restored.independentSkills.Length,Is.EqualTo(2));
            Assert.That(restored.independentSkills[0].lastTick,Is.GreaterThan(restored.tick));
            var playback=new CombatPlayback();Assert.That(playback.Accept(frames[0],match.MatchId,"p1",1),Is.True);
            Assert.That(playback.Accept(restored,match.MatchId,"p1",1),Is.True);
            Assert.That(playback.RecoveredGaps,Is.EqualTo(1));
            Assert.That(playback.Current.independentSkills[0].castId,Is.EqualTo(restored.independentSkills[0].castId));
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
