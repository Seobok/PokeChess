using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PokeChess.Core.Battle;
using PokeChess.Core.Pokemon;
using PokeChess.Core.Match;

namespace PokeChess.Core.Tests
{
    public sealed class SkillTimelineTests
    {
        private static SkillEffectDefinition Hit(float value=10,EffectTargetSelector selector=EffectTargetSelector.CastTarget,
            SkillValueSource source=SkillValueSource.Flat) => new SkillEffectDefinition(SkillEffectType.Damage,selector,value,
                damageType:DamageType.Physical,radius:1,valueSource:source);
        private static SkillDefinition Skill(SkillStepDefinition[] steps,float cast=0,float post=.125f,
            TargetLostPolicy lost=TargetLostPolicy.Cancel) => new SkillDefinition("timed",cast,post,SkillTargetRule.Enemy,
                lost,Array.Empty<SkillEffectDefinition>(),range:7,energyLock:.5f,steps:steps);
        private static BattleState Battle(bool shareItem=false)
        {
            var stats=new PokemonStats(1000,20,0,1,0,0,7,2,100,0,0);
            var catalog=new PokemonCatalog(new[]{new PokemonDefinition("caster","C",1,stats,"r","timed"),
                new PokemonDefinition("other","O",1,stats,"r","none")});
            var positions=new[]{new BoardPosition(0,0),new BoardPosition(1,0),new BoardPosition(3,2),new BoardPosition(6,7)};
            var ids=new[]{"a","z","y","b"};
            var setup=ids.Select((id,i)=>new BattleUnitSetup(catalog.CreateUnit(id,i==0?"caster":"other",i==0||i==3?"p1":"p2",
                UnitRank.One,0,UnitPlacement.Unplaced,shareItem&&id=="b"?new[]{"item:"+ItemCatalog.ExpShare+":test:b"}:null),i==0||i==3?1:2,positions[i]));
            var battle=BattleStateFactory.Create("timeline",1,123,8,catalog,setup);
            U(battle,"a").SetVitals(1000,100);return battle;
        }
        private static UnitCombatState U(BattleState b,string id)=>b.Units.Single(u=>u.UnitInstanceId==id);
        private sealed class Policy:SkillCombatBehaviorPolicy
        {
            public Policy(SkillDefinition skill):base(new SkillCatalog(new[]{skill})){}
            public override bool CanAttack(BattleState b,UnitCombatState u)=>false;
            public override bool CanMove(BattleState b,UnitCombatState u)=>false;
        }
        private static BattleSimulation Sim(BattleState b,SkillDefinition s)=>new BattleSimulation(b,new Policy(s));
        private static void Through(BattleSimulation sim,BattleState b,int tick)
        {while(b.CurrentTick<=tick&&b.Result==BattleResult.InProgress)sim.Step();}

        [Test] public void RepeatsAtExactTicksAndLocksAfterLastHitAndRecovery()
        {
            var b=Battle();var sim=Sim(b,Skill(new[]{new SkillStepDefinition(new[]{Hit()},interval:.25f,count:3)},cast:.125f));
            var hits=new List<long>();var ids=new List<long>();
            for(int tick=0;tick<=7;tick++){sim.Step();foreach(var e in b.SkillEffectEventsThisTick){hits.Add(e.Tick);ids.Add(e.CastId);}}
            CollectionAssert.AreEqual(new long[]{1,3,5},hits);
            Assert.That(ids.Distinct().Count(),Is.EqualTo(1));Assert.That(ids[0],Is.GreaterThan(0));
            Assert.That(U(b,"z").CurrentHP,Is.EqualTo(970));
            Assert.That(sim.GetRuntime("a").SkillCast.EndTick,Is.EqualTo(6));
            Assert.That(U(b,"a").EnergyLockUntilTick,Is.EqualTo(10));
            Assert.That(U(b,"a").CurrentEnergy,Is.Zero);
        }
        [TestCase(false)] [TestCase(true)] public void InterruptOrCasterDeathCancelsRemainingCastHits(bool death)
        {
            var b=Battle();var sim=Sim(b,Skill(new[]{new SkillStepDefinition(new[]{Hit()},interval:.25f,count:3)}));
            sim.Step();if(death)sim.QueueInput(_=>U(b,"a").SetVitals(0,0));else sim.QueueSkillInterrupt("a");
            Through(sim,b,6);
            Assert.That(U(b,"z").CurrentHP,Is.EqualTo(990));
            Assert.That(sim.GetRuntime("a").SkillCast.CancelReason,Is.EqualTo(death?SkillCancelReason.CasterUnavailable:SkillCancelReason.Interrupted));
        }
        [Test] public void LostTargetCancelsRemainingHitsButDoesNotUndoFirstHit()
        {
            var b=Battle();var sim=Sim(b,Skill(new[]{new SkillStepDefinition(new[]{Hit()},interval:.25f,count:3)}));
            sim.Step();sim.QueueInput(_=>U(b,"z").SetUntargetable(true));sim.Step();
            Assert.That(sim.GetRuntime("a").SkillCast.CancelReason,Is.EqualTo(SkillCancelReason.TargetLost));
            Assert.That(U(b,"z").CurrentHP,Is.EqualTo(990));
        }
        [Test] public void ExplicitReselectCanContinueWhenOriginalTargetIsLost()
        {
            var b=Battle();var sim=Sim(b,Skill(new[]{new SkillStepDefinition(new[]{Hit()},interval:.25f,count:3,
                targeting:SkillStepTargeting.Reselect)}));
            sim.Step();sim.QueueInput(_=>U(b,"z").SetUntargetable(true));Through(sim,b,5);
            Assert.That(U(b,"y").CurrentHP,Is.EqualTo(980));
            Assert.That(sim.GetRuntime("a").SkillCast.CancelReason,Is.EqualTo(SkillCancelReason.None));
        }
        [Test] public void RetargetPolicyResolvesAtNextHitRatherThanEveryTick()
        {
            var b=Battle();var sim=Sim(b,Skill(new[]{new SkillStepDefinition(new[]{Hit()},interval:.25f,count:3)},lost:TargetLostPolicy.RetargetAtEffect));
            sim.Step();sim.QueueInput(_=>U(b,"z").SetUntargetable(true));sim.Step();
            Assert.That(sim.GetRuntime("a").SkillCast.TargetId,Is.EqualTo("z"));sim.Step();
            Assert.That(sim.GetRuntime("a").SkillCast.TargetId,Is.EqualTo("y"));
        }
        [Test] public void PreparationStepRunsBeforeChargedAttackAndHasDistinctIdentity()
        {
            var b=Battle();var shield=new SkillEffectDefinition(SkillEffectType.Shield,EffectTargetSelector.Self,30);
            var sim=Sim(b,Skill(new[]{new SkillStepDefinition(new[]{shield},delay:-1),new SkillStepDefinition(new[]{Hit()})},cast:1));
            sim.Step();Assert.That(U(b,"a").CurrentShield,Is.EqualTo(30));
            Assert.That(U(b,"z").CurrentHP,Is.EqualTo(1000));
            Through(sim,b,8);Assert.That(U(b,"z").CurrentHP,Is.EqualTo(990));
            Assert.That(b.SkillEffectEventsThisTick.Single().StepIndex,Is.EqualTo(1));
        }
        [TestCase(false)] [TestCase(true)] public void GeneratedEffectSurvivesCastInterruptAndCasterDeath(bool death)
        {
            var b=Battle();var sim=Sim(b,Skill(new[]{new SkillStepDefinition(new[]{Hit()},interval:.25f,count:3,
                lifetime:SkillStepLifetime.Independent)},post:1));
            sim.Step();if(death)sim.QueueInput(_=>U(b,"a").SetVitals(0,0));else sim.QueueSkillInterrupt("a");
            Through(sim,b,4);Assert.That(U(b,"z").CurrentHP,Is.EqualTo(970));
            Assert.That(b.SkillTimeline.Active.Count,Is.Zero);
        }
        [Test] public void NonSurvivingEffectIsRemovedWhenCasterDies()
        {
            var b=Battle();var sim=Sim(b,Skill(new[]{new SkillStepDefinition(new[]{Hit()},interval:.25f,count:3,
                lifetime:SkillStepLifetime.Independent,survivesCaster:false)}));
            sim.Step();sim.QueueInput(_=>U(b,"a").SetVitals(0,0));sim.Step();
            Assert.That(b.SkillTimeline.Active.Count,Is.Zero);
            Assert.That(b.SkillTimeline.EventsThisTick.Single().Kind,Is.EqualTo(IndependentSkillEventKind.Cancelled));
            Through(sim,b,5);Assert.That(U(b,"z").CurrentHP,Is.EqualTo(990));
        }
        [Test] public void IndependentEffectDoesNotKeepCasterBusyAndSupportsDelayedFirstPulse()
        {
            var b=Battle();var sim=Sim(b,Skill(new[]{new SkillStepDefinition(new[]{Hit()},interval:1,count:3,
                lifetime:SkillStepLifetime.Independent,firstDelay:1)}));
            Through(sim,b,1);Assert.That(U(b,"a").ActionState,Is.EqualTo(CombatActionState.Idle));
            Assert.That(U(b,"a").EnergyLockUntilTick,Is.EqualTo(5));Assert.That(U(b,"z").CurrentHP,Is.EqualTo(1000));
            Assert.That(b.SkillTimeline.Active.Single().NextTick,Is.EqualTo(8));
            Through(sim,b,24);Assert.That(U(b,"z").CurrentHP,Is.EqualTo(970));Assert.That(b.SkillTimeline.Active.Count,Is.Zero);
        }
        [Test] public void AreaAnchorAndPowerAreFrozenButVictimResistanceIsLive()
        {
            var b=Battle();var sim=Sim(b,Skill(new[]{new SkillStepDefinition(new[]{Hit(1,EffectTargetSelector.EnemiesAroundCastTarget,SkillValueSource.Attack)},
                interval:.25f,count:3,lifetime:SkillStepLifetime.Independent)}));
            sim.Step();Assert.That(U(b,"z").CurrentHP,Is.EqualTo(980));
            sim.QueueInput(_=>{
                b.TryMoveUnit("z",new BoardPosition(4,4));b.TryMoveUnit("y",new BoardPosition(1,0));
                b.TryMoveUnit("a",new BoardPosition(5,5));
                b.StatusEffects.Apply(new StatusEffectDefinition("power",10,StatusTargetTeam.Ally,
                    modifiers:new[]{new StatModifierDefinition(CombatStat.Attack,StatModifierType.Flat,100)}),"a","a");
                b.StatusEffects.Apply(new StatusEffectDefinition("armor",10,StatusTargetTeam.Ally,
                    modifiers:new[]{new StatModifierDefinition(CombatStat.Armor,StatModifierType.Flat,100)}),"y","y");
            });
            Through(sim,b,4);
            Assert.That(U(b,"z").CurrentHP,Is.EqualTo(980));Assert.That(U(b,"y").CurrentHP,Is.EqualTo(980));
            Assert.That(U(b,"a").EffectiveStats.Attack,Is.EqualTo(120));
        }
        [Test] public void GeneratedStatusCanBeAppliedAfterCasterDeath()
        {
            var b=Battle();var status=new StatusEffectDefinition("root",1,crowdControl:CrowdControlKind.Root);
            var effect=new SkillEffectDefinition(SkillEffectType.ApplyStatus,EffectTargetSelector.EnemiesAroundCastTarget,0,statusId:"root");
            var skill=Skill(new[]{new SkillStepDefinition(new[]{effect},lifetime:SkillStepLifetime.Independent,firstDelay:.25f)});
            b.StatusEffects.UseCatalog(new StatusCatalog(new[]{status}));var sim=Sim(b,skill);sim.Step();
            sim.QueueInput(_=>U(b,"a").SetVitals(0,0));Through(sim,b,2);
            Assert.That(U(b,"z").HasCrowdControl(CrowdControlKind.Root),Is.True);
        }
        [Test] public void BattleEndClearsIndependentEffectsAndNeverRunsAnotherPulse()
        {
            var b=Battle();var sim=Sim(b,Skill(new[]{new SkillStepDefinition(new[]{Hit()},interval:.25f,count:100,
                lifetime:SkillStepLifetime.Independent)}));sim.Step();
            sim.QueueInput(_=>{U(b,"z").SetVitals(0,0);U(b,"y").SetVitals(0,0);});sim.Step();
            Assert.That(b.Result,Is.EqualTo(BattleResult.TeamOneWin));Assert.That(b.SkillTimeline.Active.Count,Is.Zero);
            Assert.That(b.SkillTimeline.EventsThisTick.Last().Kind,Is.EqualTo(IndependentSkillEventKind.Cancelled));
            long tick=b.CurrentTick;sim.Step();Assert.That(b.CurrentTick,Is.EqualTo(tick));
        }
        [Test] public void SameTickStepsRunInAuthoredOrderAndEachIterationExactlyOnce()
        {
            var b=Battle();var sim=Sim(b,Skill(new[]{new SkillStepDefinition(new[]{Hit()},interval:.01f,count:3),
                new SkillStepDefinition(new[]{Hit(5)},delay:.125f)}));sim.Step();sim.Step();
            CollectionAssert.AreEqual(new[]{0,0,1},b.SkillEffectEventsThisTick.Select(e=>e.StepIndex));
            CollectionAssert.AreEqual(new[]{1,2,0},b.SkillEffectEventsThisTick.Select(e=>e.Iteration));
            Through(sim,b,5);Assert.That(U(b,"z").CurrentHP,Is.EqualTo(965));
        }
        [Test] public void IdenticalSeedsProduceIdenticalTimedEventsAndFinalVitals()
        {
            Func<string> run=()=>{var b=Battle();var sim=Sim(b,Skill(new[]{new SkillStepDefinition(new[]{Hit()},interval:.25f,count:3),
                new SkillStepDefinition(new[]{Hit(4)},interval:.125f,count:5,lifetime:SkillStepLifetime.Independent)}));
                var log=new List<string>();for(int i=0;i<12;i++){sim.Step();log.AddRange(b.SkillEffectEventsThisTick.Select(e=>
                    e.Tick+":"+e.CastId+":"+e.StepIndex+":"+e.Iteration+":"+e.TargetId+":"+e.ActualValue));}
                return string.Join("|",log)+U(b,"z").CurrentHP;};Assert.That(run(),Is.EqualTo(run()));
        }
        [Test] public void InvalidTimingAndMixedAuthoringAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(()=>new SkillStepDefinition(new[]{Hit()},count:2));
            Assert.Throws<ArgumentOutOfRangeException>(()=>new SkillStepDefinition(new[]{Hit()},interval:-1));
            Assert.Throws<ArgumentException>(()=>Skill(new[]{new SkillStepDefinition(new[]{Hit()},delay:-1)}));
            Assert.Throws<ArgumentException>(()=>new SkillDefinition("s",0,0,SkillTargetRule.Enemy,TargetLostPolicy.Cancel,
                new[]{Hit()},steps:new[]{new SkillStepDefinition(new[]{Hit()})}));
        }
        [Test] public void ItemSkillTriggerRunsOnlyOnceEvenWhenRepeatIntervalExceedsItemCooldown()
        {
            var b=Battle(shareItem:true);b.TryMoveUnit("b",new BoardPosition(0,1));
            var sim=Sim(b,Skill(new[]{new SkillStepDefinition(new[]{Hit()},interval:3,count:3)}));
            int triggers=0;for(int i=0;i<=50;i++){sim.Step();triggers+=b.Items.EventsThisTick.Count(e=>e.ItemId==ItemCatalog.ExpShare);}
            Assert.That(triggers,Is.EqualTo(1));Assert.That(U(b,"b").CurrentEnergy,Is.EqualTo(10));
        }
        [Test] public void ProjectileAlreadyLaunchedSurvivesCancellationAndCasterDeath()
        {
            var b=Battle();var shot=new SkillEffectDefinition(SkillEffectType.ProjectileDamage,EffectTargetSelector.CastTarget,30,projectileSpeed:1);
            var sim=Sim(b,Skill(new[]{new SkillStepDefinition(new[]{shot},interval:1,count:3)},post:1));
            sim.Step();Assert.That(b.Projectiles.Active.Count,Is.EqualTo(1));
            sim.QueueInput(_=>U(b,"a").SetVitals(0,0));Through(sim,b,12);
            Assert.That(U(b,"z").CurrentHP,Is.EqualTo(970));Assert.That(b.Projectiles.Active.Count,Is.Zero);
        }
        [Test] public void GeneratedDamageRetainsOffensiveAmplificationAfterBuffExpires()
        {
            var b=Battle();b.StatusEffects.Apply(new StatusEffectDefinition("amp",.125f,StatusTargetTeam.Ally,
                modifiers:new[]{new StatModifierDefinition(CombatStat.DamageAmplification,StatModifierType.Flat,1)}),"a","a");
            var sim=Sim(b,Skill(new[]{new SkillStepDefinition(new[]{Hit()},interval:.25f,count:3,lifetime:SkillStepLifetime.Independent)}));
            Through(sim,b,4);Assert.That(U(b,"z").CurrentHP,Is.EqualTo(940));
            Assert.That(U(b,"a").EffectiveDamageModifiers.Amplification,Is.Zero);
        }
        [Test] public void LegacyEnemyTargetedSelfShieldStillCancelsBeforeEffectOnTargetLoss()
        {
            var b=Battle();var skill=new SkillDefinition("timed",1,0,SkillTargetRule.Enemy,TargetLostPolicy.Cancel,
                SkillEffectKind.SelfShield,50,range:7);var sim=Sim(b,skill);sim.Step();
            sim.QueueInput(_=>U(b,"z").SetUntargetable(true));sim.Step();
            Assert.That(sim.GetRuntime("a").SkillCast.CancelReason,Is.EqualTo(SkillCancelReason.TargetLost));
            Assert.That(U(b,"a").CurrentShield,Is.Zero);
        }
        [Test] public void SingleIndependentPulseDoesNotEvaluateUnusedRepeatInterval()
        {
            var b=Battle();var sim=Sim(b,Skill(new[]{new SkillStepDefinition(new[]{Hit()},interval:float.MaxValue,
                lifetime:SkillStepLifetime.Independent)}));
            Assert.DoesNotThrow(()=>sim.Step());Assert.That(b.SkillTimeline.Active.Count,Is.Zero);
            Assert.That(U(b,"z").CurrentHP,Is.EqualTo(990));
        }
        [Test] public void ExplicitReselectAlsoSurvivesTargetLossDuringCharge()
        {
            var b=Battle();var sim=Sim(b,Skill(new[]{new SkillStepDefinition(new[]{Hit()},
                targeting:SkillStepTargeting.Reselect)},cast:.5f));
            sim.Step();sim.QueueInput(_=>U(b,"z").SetUntargetable(true));Through(sim,b,4);
            Assert.That(U(b,"y").CurrentHP,Is.EqualTo(990));
            Assert.That(sim.GetRuntime("a").SkillCast.CancelReason,Is.EqualTo(SkillCancelReason.None));
        }
    }
}
