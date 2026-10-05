using System;
using System.Linq;
using NUnit.Framework;
using PokeChess.Core.Battle;
using PokeChess.Core.Pokemon;
namespace PokeChess.Core.Tests
{
    public sealed class SkillCastTests
    {
        private static SkillDefinition Skill(TargetLostPolicy lost=TargetLostPolicy.Cancel,bool interruptible=true,
            SkillEffectKind kind=SkillEffectKind.Damage,SkillTargetRule rule=SkillTargetRule.Enemy,float cast=.125f,float post=.125f,float? energyLock=.5f) =>
            new SkillDefinition("s",cast,post,rule,lost,kind,40,3,interruptible,energyLock);
        private static BattleState Battle(bool extra=false,bool selfOnly=false,bool reserve=false)
        {
            var stats=new PokemonStats(1000,10,0,1,0,0,3,2,100,0,0);
            var c=new PokemonCatalog(new[]{new PokemonDefinition("t","T",1,stats,"r","s"),
                new PokemonDefinition("none","N",1,stats,"r","missing")});
            Func<string,int,int,int,BattleUnitSetup> make=(id,team,col,row)=>new BattleUnitSetup(
                c.CreateUnit(id,id=="a"?"t":"none","p"+team,UnitRank.One,0,UnitPlacement.Unplaced),team,new BoardPosition(col,row));
            var setup=new[]{make("a",1,0,0),make("z",2,selfOnly?6:1,selfOnly?7:0)};
            if(extra)setup=setup.Concat(new[]{make("y",2,0,2)}).ToArray();
            if(reserve)setup=setup.Concat(new[]{make("reserve",2,6,6)}).ToArray();
            var b=BattleStateFactory.Create("b",1,123,30,c,setup);U(b,"a").SetVitals(1000,115);if(reserve)U(b,"reserve").SetUntargetable(true);return b;
        }
        private static UnitCombatState U(BattleState b,string id)=>b.Units.Single(u=>u.UnitInstanceId==id);
        private sealed class Policy:SkillCombatBehaviorPolicy
        {
            public Policy(SkillDefinition skill):base(new SkillCatalog(new[]{skill})) { }
            public bool Use=true;
            public override bool CanUseSkill(BattleState b,UnitCombatState u)=>Use;
            public override bool CanAttack(BattleState b,UnitCombatState u)=>false;
            public override bool CanMove(BattleState b,UnitCombatState u)=>false;
        }
        [Test] public void EffectFrameOnceThenPostCastAndSnapshotLock()
        {
            var b=Battle();var sim=new BattleSimulation(b,new Policy(Skill()));
            sim.Step();var started=b.SkillEventsThisTick;
            Assert.That(U(b,"a").CurrentEnergy,Is.EqualTo(15));
            Assert.That(started.Single().Kind,Is.EqualTo(SkillEventKind.CastStarted));
            for(int i=1;i<4;i++){sim.Step();Assert.That(U(b,"z").CurrentHP,Is.EqualTo(1000));}
            sim.Step();Assert.That(U(b,"z").CurrentHP,Is.EqualTo(960));
            Assert.That(b.SkillEventsThisTick.Single().Kind,Is.EqualTo(SkillEventKind.EffectApplied));
            Assert.That(U(b,"a").CurrentEnergy,Is.EqualTo(15));
            for(int i=5;i<8;i++){sim.Step();Assert.That(U(b,"a").ActionState,Is.EqualTo(CombatActionState.Casting));}
            sim.Step();Assert.That(U(b,"a").EnergyLockUntilTick,Is.EqualTo(23));
            Assert.That(U(b,"a").ActionState,Is.EqualTo(CombatActionState.Idle));
            Assert.That(started.Single().Kind,Is.EqualTo(SkillEventKind.CastStarted));
            Assert.That(U(b,"z").CurrentHP,Is.EqualTo(960));
        }
        [TestCase("dead")] [TestCase("removed")] [TestCase("hidden")]
        public void CancelBeforeEffectNoRefund(string reason)
        {
            var b=Battle(reserve:true);var sim=new BattleSimulation(b,new Policy(Skill()));sim.Step();
            sim.QueueInput(state=>{
                if(reason=="dead")U(state,"z").SetVitals(0,0);
                if(reason=="removed")state.TryRemoveUnit("z");
                if(reason=="hidden")U(state,"z").SetUntargetable(true);
            });
            sim.Step();Assert.That(b.SkillEventsThisTick.Single().Reason,Is.EqualTo(SkillCancelReason.TargetLost));
            Assert.That(U(b,"a").CurrentEnergy,Is.EqualTo(15));Assert.That(U(b,"a").EnergyLockUntilTick,Is.EqualTo(16));
            Assert.That(sim.GetRuntime("a").SkillCast.EffectApplied,Is.False);
        }
        [Test] public void RetargetOccursOnlyAtEffectAndSelectsNewEnemy()
        {
            var b=Battle(extra:true);var sim=new BattleSimulation(b,new Policy(Skill(TargetLostPolicy.RetargetAtEffect)));sim.Step();
            sim.QueueInput(state=>state.TryRemoveUnit("z"));sim.Step();
            Assert.That(sim.GetRuntime("a").SkillCast.TargetId,Is.EqualTo("z"));
            for(int i=2;i<=4;i++)sim.Step();
            Assert.That(sim.GetRuntime("a").SkillCast.TargetId,Is.EqualTo("y"));
            Assert.That(U(b,"y").CurrentHP,Is.EqualTo(960));
        }
        [Test] public void NoRetargetCandidateCancelsAtEffect()
        {
            var b=Battle(reserve:true);var sim=new BattleSimulation(b,new Policy(Skill(TargetLostPolicy.RetargetAtEffect)));sim.Step();
            sim.QueueInput(state=>state.TryRemoveUnit("z"));for(int i=1;i<=4;i++)sim.Step();
            Assert.That(b.SkillEventsThisTick.Single().Reason,Is.EqualTo(SkillCancelReason.TargetLost));
            Assert.That(U(b,"a").EnergyLockUntilTick,Is.EqualTo(19));
        }
        [Test] public void ContinueWithoutTargetExecutesSelfShield()
        {
            var b=Battle(reserve:true);var sim=new BattleSimulation(b,new Policy(Skill(TargetLostPolicy.ContinueWithoutTarget,kind:SkillEffectKind.SelfShield)));
            sim.Step();sim.QueueInput(state=>state.TryRemoveUnit("z"));for(int i=1;i<=4;i++)sim.Step();
            Assert.That(U(b,"a").CurrentShield,Is.EqualTo(40));
            Assert.That(b.SkillEventsThisTick.Single().Kind,Is.EqualTo(SkillEventKind.EffectApplied));
        }
        [Test] public void SelfSkillNeedsNoEnemyInRangeAndZeroCastExecutesImmediately()
        {
            var b=Battle(selfOnly:true);
            var sim=new BattleSimulation(b,skillCatalog:new SkillCatalog(new[]{Skill(kind:SkillEffectKind.SelfShield,rule:SkillTargetRule.Self,cast:0,post:0)}));
            sim.Step();Assert.That(U(b,"a").CurrentShield,Is.EqualTo(40));
            Assert.That(b.SkillEventsThisTick.Count,Is.EqualTo(2));
            Assert.That(sim.GetRuntime("a").SkillCast.TargetId,Is.EqualTo("a"));
            sim.Step();Assert.That(sim.GetRuntime("a").SkillCast.Finished,Is.True);
        }
        [TestCase(true)] [TestCase(false)]
        public void InterruptRespectsFlag(bool interruptible)
        {
            var b=Battle();var sim=new BattleSimulation(b,new Policy(Skill(interruptible:interruptible)));sim.Step();
            sim.QueueSkillInterrupt("a");sim.Step();
            Assert.That(sim.GetRuntime("a").SkillCast.Finished,Is.EqualTo(interruptible));
            if(interruptible)Assert.That(b.SkillEventsThisTick.Single().Reason,Is.EqualTo(SkillCancelReason.Interrupted));
            else {for(int i=2;i<=4;i++)sim.Step();Assert.That(U(b,"z").CurrentHP,Is.EqualTo(960));}
        }
        [Test] public void PostEffectInterruptDoesNotUndoDamage()
        {
            var b=Battle();var sim=new BattleSimulation(b,new Policy(Skill()));for(int i=0;i<=4;i++)sim.Step();
            sim.QueueSkillInterrupt("a");sim.Step();
            Assert.That(U(b,"z").CurrentHP,Is.EqualTo(960));
            Assert.That(U(b,"a").EnergyLockUntilTick,Is.EqualTo(20));
            Assert.That(sim.GetRuntime("a").SkillCast.EffectApplied,Is.True);
        }
        [TestCase("dead")] [TestCase("removed")]
        public void CasterUnavailableAlwaysCancels(string reason)
        {
            var b=Battle();var sim=new BattleSimulation(b,new Policy(Skill(interruptible:false)));sim.Step();
            sim.QueueInput(state=>{if(reason=="dead")U(state,"a").SetVitals(0,15);else state.TryRemoveUnit("a");});
            sim.Step();Assert.That(b.SkillEventsThisTick.Single().Reason,Is.EqualTo(SkillCancelReason.CasterUnavailable));
            Assert.That(U(b,"a").EnergyLockUntilTick,Is.EqualTo(16));
        }
        [Test] public void MovementAfterStartAndPostEffectTargetLossDoNotCancel()
        {
            var b=Battle(reserve:true);var sim=new BattleSimulation(b,new Policy(Skill()));sim.Step();
            sim.QueueInput(state=>state.TryMoveUnit("z",new BoardPosition(6,7)));
            for(int i=1;i<=4;i++)sim.Step();Assert.That(U(b,"z").CurrentHP,Is.EqualTo(960));
            sim.QueueInput(state=>state.TryRemoveUnit("z"));
            for(int i=5;i<=8;i++)sim.Step();
            Assert.That(b.SkillEventsThisTick.Single().Kind,Is.EqualTo(SkillEventKind.CastCompleted));
        }
        [Test] public void NotReadyBlockedOutOfRangeAndMissingSkillDoNotConsume()
        {
            var b=Battle();U(b,"a").SetVitals(1000,99);new BattleSimulation(b,new Policy(Skill())).Step();
            Assert.That(U(b,"a").CurrentEnergy,Is.EqualTo(99));
            b=Battle();var p=new Policy(Skill()){Use=false};new BattleSimulation(b,p).Step();
            Assert.That(U(b,"a").CurrentEnergy,Is.EqualTo(115));
            b=Battle(selfOnly:true);new BattleSimulation(b,new Policy(Skill())).Step();
            Assert.That(U(b,"a").CurrentEnergy,Is.EqualTo(115));
            b=Battle();new BattleSimulation(b,skillCatalog:new SkillCatalog(Array.Empty<SkillDefinition>())).Step();
            Assert.That(b.SkillEventsThisTick.Count,Is.Zero);
        }
        [Test] public void SkillProjectileHitsLaterWithoutBasicAttackEnergyOrCrit()
        {
            var b=Battle();var sim=new BattleSimulation(b,new Policy(Skill(kind:SkillEffectKind.ProjectileDamage)));
            for(int i=0;i<=4;i++)sim.Step();
            Assert.That(U(b,"z").CurrentHP,Is.EqualTo(1000));
            Assert.That(b.Projectiles.Active.Single().ArrivalTick,Is.EqualTo(9));
            for(int i=5;i<=9;i++)sim.Step();
            Assert.That(U(b,"z").CurrentHP,Is.EqualTo(960));
            Assert.That(U(b,"a").CurrentEnergy,Is.EqualTo(15));
            Assert.That(b.DamageResultsThisTick.Single().Request.IsBasicAttack,Is.False);
            Assert.That(b.RngStreams.Critical.DrawCount,Is.Zero);
        }
        [Test] public void DefinitionValidationAndCatalogDuplicates()
        {
            Assert.Throws<ArgumentException>(()=>Skill(TargetLostPolicy.ContinueWithoutTarget));
            Assert.Throws<ArgumentOutOfRangeException>(()=>Skill(cast:-1));
            Assert.Throws<ArgumentException>(()=>new SkillCatalog(new[]{Skill(),Skill()}));
        }
        [Test] public void RepeatedRunsHaveSameSkillAndDamageEvents()
        {
            string Run() {
                var b=Battle(extra:true);var sim=new BattleSimulation(b,new Policy(Skill()));var trace="";
                for(int i=0;i<12;i++) {sim.Step();foreach(var e in b.SkillEventsThisTick)
                    trace+=e.Tick+":"+e.TargetId+":"+e.Kind+";";
                    foreach(var d in b.DamageResultsThisTick)trace+=d.AppliedDamage+";";
                }return trace;
            }
            Assert.That(Run(),Is.EqualTo(Run()));
        }
    }
}
