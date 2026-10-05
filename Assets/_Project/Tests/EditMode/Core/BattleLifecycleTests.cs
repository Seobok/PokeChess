using System;
using System.Linq;
using NUnit.Framework;
using PokeChess.Core.Battle;
using PokeChess.Core.Pokemon;
namespace PokeChess.Core.Tests
{
    public sealed class BattleLifecycleTests
    {
        private static BattleState Battle(int rate=30,bool ranged=false,bool reverse=false,bool far=false)
        {
            var stats=new PokemonStats(100,100,50,2,0,0,3,2,100,0,0,
                ranged?AttackDeliveryType.Projectile:AttackDeliveryType.Melee,6);
            var catalog=new PokemonCatalog(new[]{new PokemonDefinition("t","T",1,stats,"r","s")});
            var setup=new[]{new BattleUnitSetup(catalog.CreateUnit("a","t","p1",UnitRank.One,0,UnitPlacement.Unplaced),1,new BoardPosition(0,0)),
                new BattleUnitSetup(catalog.CreateUnit("z","t","p2",UnitRank.One,0,UnitPlacement.Unplaced),2,new BoardPosition(far?6:1,far?7:0))};
            return BattleStateFactory.Create("b",1,123,rate,catalog,reverse?setup.Reverse():setup);
        }
        private static UnitCombatState U(BattleState b,string id)=>b.Units.Single(u=>u.UnitInstanceId==id);
        private sealed class Passive:CombatBehaviorPolicy
        {
            public override bool CanAttack(BattleState b,UnitCombatState u)=>false;
            public override bool CanMove(BattleState b,UnitCombatState u)=>false;
        }
        [Test] public void LethalHitEndsOnceAndPreservesFinalEvents()
        {
            var b=Battle();var sim=new BattleSimulation(b);sim.Step();
            Assert.That(b.Result,Is.EqualTo(BattleResult.TeamOneWin));Assert.That(b.EndReason,Is.EqualTo(BattleEndReason.Elimination));
            Assert.That(b.EndTick,Is.EqualTo(0));Assert.That(U(b,"z").IsOnBoard,Is.False);
            var events=b.LifecycleEventsThisTick;Assert.That(events.Count,Is.EqualTo(2));
            Assert.That(events[0].Kind,Is.EqualTo(BattleLifecycleEventKind.UnitDied));Assert.That(events[0].DeathReason,Is.EqualTo(DeathReason.Damage));
            Assert.That(events[1].Kind,Is.EqualTo(BattleLifecycleEventKind.BattleEnded));
            for(int i=0;i<5;i++)Assert.That(sim.Step(),Is.Empty);
            Assert.That(b.CurrentTick,Is.EqualTo(0));Assert.That(b.LifecycleEventsThisTick.Count,Is.EqualTo(2));Assert.That(b.DamageResultsThisTick.Count,Is.EqualTo(1));
            Assert.Throws<InvalidOperationException>(()=>sim.QueueInput(_=>{}));
        }
        [TestCase(20)] [TestCase(30)] [TestCase(60)]
        public void OvertimeAndLimitHaveExactTickBoundaries(int rate)
        {
            var b=Battle(rate);var sim=new BattleSimulation(b,new Passive());
            for(int i=0;i<30*rate;i++)sim.Step();
            Assert.That(b.IsOvertime,Is.False);Assert.That(b.CurrentTick,Is.EqualTo(30*rate));
            sim.Step();Assert.That(b.IsOvertime,Is.True);var start=b.LifecycleEventsThisTick;
            Assert.That(start.Single().Tick,Is.EqualTo(30*rate));
            while(b.Result==BattleResult.InProgress)sim.Step();
            Assert.That(b.CurrentTick,Is.EqualTo(45*rate));Assert.That(b.EndTick,Is.EqualTo(45*rate));
            Assert.That(b.Result,Is.EqualTo(BattleResult.Draw));Assert.That(b.EndReason,Is.EqualTo(BattleEndReason.TimeLimit));
            Assert.That(b.LifecycleEventsThisTick.Single().Kind,Is.EqualTo(BattleLifecycleEventKind.BattleEnded));
            Assert.That(start.Single().Kind,Is.EqualTo(BattleLifecycleEventKind.OvertimeStarted));
        }
        [Test] public void OvertimeIsGlobalAfterBuffsAndBeforeSpeedCap()
        {
            var b=Battle();var sim=new BattleSimulation(b,new Passive());
            b.StatusEffects.Apply(new StatusEffectDefinition("attack",40,StatusTargetTeam.Ally,modifiers:new[]{
                new StatModifierDefinition(CombatStat.Attack,StatModifierType.Flat,20),
                new StatModifierDefinition(CombatStat.Attack,StatModifierType.Percentage,.5f)}),"a","a");
            for(int i=0;i<=900;i++)sim.Step();
            var s=U(b,"a").EffectiveStats;Assert.That(s.Attack,Is.EqualTo(360));Assert.That(s.SpellPower,Is.EqualTo(100));
            Assert.That(s.AttackSpeed,Is.EqualTo(5));Assert.That(s.MoveSpeed,Is.EqualTo(6));Assert.That(U(b,"a").Stats.Attack,Is.EqualTo(100));
            foreach(var status in b.StatusEffects.Active)b.StatusEffects.Remove(status.InstanceId);
            Assert.That(U(b,"a").EffectiveStats.Attack,Is.EqualTo(200));Assert.That(b.IsOvertime,Is.True);
        }
        [Test] public void OvertimeScalesNewSupportAndCcButPreservesExistingValues()
        {
            var b=Battle();var sim=new BattleSimulation(b,new Passive());for(int i=0;i<899;i++)sim.Step();
            var old=b.StatusEffects.Apply(new StatusEffectDefinition("root",10,crowdControl:CrowdControlKind.Root),"z","a");
            U(b,"a").SetShield(20);sim.Step();sim.Step();Assert.That(old.ExpireTick,Is.EqualTo(1199));Assert.That(U(b,"a").CurrentShield,Is.EqualTo(20));
            var cc=b.StatusEffects.Apply(new StatusEffectDefinition("stun",1,crowdControl:CrowdControlKind.Stun),"z","a");
            Assert.That(cc.ExpireTick-b.CurrentTick,Is.EqualTo(11));
            U(b,"a").SetVitals(10,0);
            new HealSkillEffectHandler().Apply(b,U(b,"a"),U(b,"a"),new SkillEffectDefinition(SkillEffectType.Heal,EffectTargetSelector.Self,100),100);
            new ShieldSkillEffectHandler().Apply(b,U(b,"a"),U(b,"a"),new SkillEffectDefinition(SkillEffectType.Shield,EffectTargetSelector.Self,100),100);
            Assert.That(U(b,"a").CurrentHP,Is.EqualTo(44));Assert.That(U(b,"a").CurrentShield,Is.EqualTo(54));
        }
        [TestCase(false)] [TestCase(true)]
        public void SameTickLastProjectilesProduceDrawIndependentlyOfSetupOrder(bool reverse)
        {
            var b=Battle(ranged:true,reverse:reverse);var sim=new BattleSimulation(b);
            for(int i=0;i<=5;i++)sim.Step();
            Assert.That(b.Result,Is.EqualTo(BattleResult.Draw));Assert.That(b.EndReason,Is.EqualTo(BattleEndReason.Elimination));
            Assert.That(b.EndTick,Is.EqualTo(5));Assert.That(b.DamageResultsThisTick.Count,Is.EqualTo(2));
            Assert.That(b.LifecycleEventsThisTick.Select(e=>e.UnitId).ToArray(),Is.EqualTo(new string[]{"a","z",null}));
        }
        [Test] public void FinalEliminationClearsFlyingProjectilesAndQueuedInputs()
        {
            var b=Battle(ranged:true);var sim=new BattleSimulation(b);sim.Step();Assert.That(b.Projectiles.Active.Count,Is.EqualTo(2));int calls=0;
            sim.QueueInput(state=>{U(state,"a").SetVitals(0,0);sim.QueueInput(_=>calls++);});sim.Step();
            Assert.That(b.Result,Is.EqualTo(BattleResult.TeamTwoWin));Assert.That(b.Projectiles.Active,Is.Empty);
            Assert.That(b.Projectiles.EventsThisTick.All(e=>e.Reason==ProjectileExpireReason.BattleEnded),Is.True);
            sim.Step();Assert.That(calls,Is.Zero);Assert.That(U(b,"z").CurrentHP,Is.EqualTo(100));
        }
        [Test] public void RemovedAliveUnitIsNotReportedAsDeathAndHiddenUnitStillCountsAlive()
        {
            var b=Battle();var sim=new BattleSimulation(b,new Passive());U(b,"z").SetUntargetable(true);sim.Step();
            Assert.That(b.Result,Is.EqualTo(BattleResult.InProgress));b.TryRemoveUnit("z");sim.Step();
            Assert.That(b.Result,Is.EqualTo(BattleResult.TeamOneWin));Assert.That(b.LifecycleEventsThisTick.Count,Is.EqualTo(1));
        }
        [Test] public void PreStepEliminationAtThirtySecondsSkipsOvertime()
        {
            var b=Battle();var sim=new BattleSimulation(b,new Passive());for(int i=0;i<900;i++)sim.Step();U(b,"z").SetVitals(0,0);sim.Step();
            Assert.That(b.Result,Is.EqualTo(BattleResult.TeamOneWin));Assert.That(b.IsOvertime,Is.False);Assert.That(b.EndTick,Is.EqualTo(900));
        }
        private sealed class Casting:SkillCombatBehaviorPolicy
        {
            public Casting(SkillDefinition skill):base(new SkillCatalog(new[]{skill})) { }
            public override bool CanAttack(BattleState b,UnitCombatState u)=>false;
            public override bool CanMove(BattleState b,UnitCombatState u)=>false;
        }
        [Test] public void TimeLimitCancelsNonInterruptibleCastWithoutRefund()
        {
            var b=Battle();U(b,"a").SetVitals(100,115);
            var skill=new SkillDefinition("s",50,1,SkillTargetRule.Enemy,TargetLostPolicy.Cancel,SkillEffectKind.Damage,100,interruptible:false);
            var sim=new BattleSimulation(b,new Casting(skill));
            while(b.Result==BattleResult.InProgress)sim.Step();
            Assert.That(b.EndReason,Is.EqualTo(BattleEndReason.TimeLimit));Assert.That(sim.GetRuntime("a").SkillCast.Finished,Is.True);
            Assert.That(b.SkillEventsThisTick.Single().Reason,Is.EqualTo(SkillCancelReason.BattleEnded));
            Assert.That(U(b,"a").CurrentEnergy,Is.EqualTo(15));Assert.That(U(b,"z").CurrentHP,Is.EqualTo(100));
        }
        private sealed class Moving:CombatBehaviorPolicy
        {
            public bool Enabled;
            public override bool CanAttack(BattleState b,UnitCombatState u)=>false;
            public override bool CanMove(BattleState b,UnitCombatState u)=>Enabled&&u.UnitInstanceId=="a";
        }
        [Test] public void OvertimePreservesCurrentMoveAndEndReleasesReservations()
        {
            var b=Battle(far:true);var p=new Moving();var sim=new BattleSimulation(b,p);for(int i=0;i<899;i++)sim.Step();
            p.Enabled=true;sim.Step();long end=sim.GetRuntime("a").EndTick;Assert.That(end,Is.EqualTo(914));
            sim.Step();Assert.That(b.IsOvertime,Is.True);Assert.That(sim.GetRuntime("a").EndTick,Is.EqualTo(end));
            p.Enabled=false;sim.Step();Assert.That(sim.Reservations.Count,Is.Zero);
            while(b.CurrentTick<1349)sim.Step();p.Enabled=true;sim.Step();
            Assert.That(b.EndReason,Is.EqualTo(BattleEndReason.TimeLimit));Assert.That(sim.Reservations.Count,Is.Zero);
            Assert.That(U(b,"a").ActionState,Is.EqualTo(CombatActionState.Idle));Assert.That(U(b,"a").CurrentTargetId,Is.Null);
        }
        [Test] public void ProjectileArrivingAtLimitCannotHitAfterEnd()
        {
            var b=Battle();var sim=new BattleSimulation(b,new Passive());while(b.CurrentTick<1345)sim.Step();
            sim.QueueInput(state=>new ProjectileSkillEffectHandler().Apply(state,U(state,"a"),U(state,"z"),
                new SkillEffectDefinition(SkillEffectType.ProjectileDamage,EffectTargetSelector.CastTarget,100,projectileSpeed:6),100));
            sim.Step();Assert.That(b.Projectiles.Active.Single().ArrivalTick,Is.EqualTo(1350));
            while(b.Result==BattleResult.InProgress)sim.Step();
            Assert.That(U(b,"z").CurrentHP,Is.EqualTo(100));Assert.That(b.EndTick,Is.EqualTo(1350));
            Assert.That(b.Projectiles.EventsThisTick.Single().Reason,Is.EqualTo(ProjectileExpireReason.BattleEnded));
        }
        [Test] public void DamageBeforeStepRetainsDeathReason()
        {
            var b=Battle();new DamageProcessor().Apply(b,new DamageRequest("a","z",100,DamageType.True));
            new BattleSimulation(b,new Passive()).Step();
            Assert.That(b.LifecycleEventsThisTick.First().DeathReason,Is.EqualTo(DeathReason.Damage));
        }
    }
}
