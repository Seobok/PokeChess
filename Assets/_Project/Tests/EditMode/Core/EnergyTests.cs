using System;
using System.Linq;
using NUnit.Framework;
using PokeChess.Core.Battle;
using PokeChess.Core.Pokemon;
namespace PokeChess.Core.Tests
{
    public sealed class EnergyTests
    {
        private static BattleState Create(float max=100,float lockSeconds=1,AttackDeliveryType delivery=AttackDeliveryType.Melee)
        {
            var stats=new PokemonStats(1000,100,0,1,0,0,3,2,max,0,0,delivery,6,0,1.5f,lockSeconds);
            var c=new PokemonCatalog(new[]{new PokemonDefinition("t","T",1,stats,"r","s")});
            return BattleStateFactory.Create("b",1,123,30,c,new[]{
                new BattleUnitSetup(c.CreateUnit("a","t","p1",UnitRank.One,0,UnitPlacement.Unplaced),1,new BoardPosition(0,0)),
                new BattleUnitSetup(c.CreateUnit("z","t","p2",UnitRank.One,0,UnitPlacement.Unplaced),2,new BoardPosition(1,0))});
        }
        private static UnitCombatState U(BattleState b,string id)=>b.Units.Single(u=>u.UnitInstanceId==id);
        private sealed class Policy:BasicAttackCombatBehaviorPolicy
        {
            public bool Skill,Continue=true;
            public int Starts;
            public override bool CanAttack(BattleState b,UnitCombatState u)=>u.TeamId==1&&!Skill;
            public override bool CanMove(BattleState b,UnitCombatState u)=>false;
            public override bool TryGetSkillTiming(BattleState b,UnitCombatState u,UnitCombatState t,out CombatActionTiming timing)
            {timing=new CombatActionTiming(6,3);return Skill&&u.TeamId==1;}
            public override bool CanContinueSkill(BattleState b,UnitCombatState u,UnitCombatState t)=>Continue&&base.CanContinueSkill(b,u,t);
            public override void OnSkillStarted(BattleState b,UnitCombatState u,UnitCombatState t){Starts++;}
        }
        [TestCase(AttackDeliveryType.Melee,0)] [TestCase(AttackDeliveryType.Projectile,5)]
        public void BasicHitGrantsEnergyExactlyOnce(AttackDeliveryType delivery,int hit)
        {
            var b=Create(delivery:delivery);var sim=new BattleSimulation(b,new Policy());
            for(int i=0;i<=hit;i++)sim.Step();
            Assert.That(U(b,"a").CurrentEnergy,Is.EqualTo(10));Assert.That(U(b,"z").CurrentEnergy,Is.EqualTo(8));
            Assert.That(b.Energy.EventsThisTick.Count,Is.EqualTo(2));
            var events=b.Energy.EventsThisTick;sim.Step();
            Assert.That(U(b,"a").CurrentEnergy,Is.EqualTo(10));Assert.That(events.Count,Is.EqualTo(2));
            Assert.That(b.Energy.EventsThisTick.Count,Is.Zero);
        }
        [Test] public void FormulaUsesPrePostAndShieldAndCapsEachEvent()
        {
            var b=Create();U(b,"a").SetDamageModifiers(new DamageModifiers(amplification:.5f));
            U(b,"z").SetDamageModifiers(new DamageModifiers(reduction:1f/3f));U(b,"z").SetShield(1000);
            var p=new DamageProcessor();p.Apply(b,new DamageRequest("a","z",200,DamageType.Physical));
            Assert.That(U(b,"z").CurrentHP,Is.EqualTo(1000));
            Assert.That(U(b,"z").CurrentEnergy,Is.EqualTo(17).Within(.0001));
            p.Apply(b,new DamageRequest("a","z",10000,DamageType.Physical));
            Assert.That(U(b,"z").IsAlive,Is.False);
            Assert.That(U(b,"z").CurrentEnergy,Is.EqualTo(17).Within(.0001));

            b=Create();U(b,"z").SetShield(100000);
            for(int i=0;i<2;i++)p.Apply(b,new DamageRequest("a","z",10000,DamageType.Physical));
            Assert.That(U(b,"z").CurrentEnergy,Is.EqualTo(100));
        }
        [Test] public void SkillCritEligibilityDoesNotGrantBasicAttackEnergy()
        {
            var b=Create();new DamageProcessor().Apply(b,new DamageRequest("a","z",100,DamageType.Physical,true));
            Assert.That(U(b,"a").CurrentEnergy,Is.Zero);Assert.That(U(b,"z").CurrentEnergy,Is.EqualTo(8));
        }
        [Test] public void ZeroDamageAndExpiredProjectileGiveNoEnergy()
        {
            var b=Create();new DamageProcessor().Apply(b,new DamageRequest("a","z",0,DamageType.Physical,true,isBasicAttack:true));
            Assert.That(b.Energy.EventsThisTick.Count,Is.Zero);
            b=Create(delivery:AttackDeliveryType.Projectile);var sim=new BattleSimulation(b,new Policy());sim.Step();
            sim.QueueInput(state=>U(state,"z").SetUntargetable(true));
            for(int i=1;i<=5;i++)sim.Step();
            Assert.That(U(b,"a").CurrentEnergy,Is.Zero);Assert.That(U(b,"z").CurrentEnergy,Is.Zero);
        }
        [Test] public void OverflowConsumedOnceAndCastingBlocksGain()
        {
            var b=Create();var a=U(b,"a");a.SetVitals(1000,95);
            b.Energy.Gain(a,20,EnergyChangeReason.BasicAttackHit);
            Assert.That(a.CurrentEnergy,Is.EqualTo(115));
            var p=new Policy{Skill=true};var sim=new BattleSimulation(b,p);sim.Step();
            Assert.That(a.CurrentEnergy,Is.EqualTo(15));Assert.That(p.Starts,Is.EqualTo(1));
            Assert.That(b.Energy.Gain(a,10,EnergyChangeReason.BasicAttackHit),Is.False);
            Assert.That(b.Energy.EventsThisTick.Last().BlockReason,Is.EqualTo(EnergyBlockReason.Casting));
            for(int i=1;i<=3;i++)sim.Step();
            Assert.That(a.CurrentEnergy,Is.EqualTo(15));Assert.That(p.Starts,Is.EqualTo(1));
        }
        [Test] public void CompletionStartsLockAndExactExpirationAllowsGain()
        {
            var b=Create();var a=U(b,"a");a.SetVitals(1000,100);
            var sim=new BattleSimulation(b,new Policy{Skill=true});
            for(int i=0;i<=6;i++)sim.Step();
            Assert.That(a.EnergyLockUntilTick,Is.EqualTo(36));
            Assert.That(b.Energy.Gain(a,10,EnergyChangeReason.BasicAttackHit),Is.False);
            Assert.That(b.Energy.EventsThisTick.Last().BlockReason,Is.EqualTo(EnergyBlockReason.Locked));
            while(b.CurrentTick<36)sim.Step();
            Assert.That(b.Energy.Gain(a,10,EnergyChangeReason.BasicAttackHit),Is.True);
            Assert.That(a.CurrentEnergy,Is.EqualTo(10));
        }
        [Test] public void InterruptedCastDoesNotRefundAndStartsLock()
        {
            var b=Create();var a=U(b,"a");a.SetVitals(1000,115);var p=new Policy{Skill=true};
            var sim=new BattleSimulation(b,p);sim.Step();p.Continue=false;sim.Step();
            Assert.That(a.CurrentEnergy,Is.EqualTo(15));Assert.That(a.EnergyLockUntilTick,Is.EqualTo(31));
            Assert.That(a.ActionState,Is.EqualTo(CombatActionState.Idle));
        }
        [Test] public void DeadAndRemovedUnitsCannotGain()
        {
            foreach(bool dead in new[]{true,false})
            {
                var b=Create();var a=U(b,"a");if(dead)a.SetVitals(0,0);else b.TryRemoveUnit("a");
                Assert.That(b.Energy.Gain(a,10,EnergyChangeReason.BasicAttackHit),Is.False);
                Assert.That(b.Energy.EventsThisTick.Single().BlockReason,Is.EqualTo(EnergyBlockReason.Unavailable));
            }
        }
        [Test] public void MaxZeroAndUnavailableSkillNeverConsume()
        {
            foreach(float max in new[]{0,100})
            {
                var b=Create(max);U(b,"a").SetVitals(1000,150);
                var p=new Policy{Skill=max==0};var sim=new BattleSimulation(b,p);sim.Step();
                Assert.That(U(b,"a").CurrentEnergy,Is.GreaterThanOrEqualTo(150));Assert.That(p.Starts,Is.Zero);
            }
        }
        [Test] public void LockZeroAndFractionalGainAndNoPassiveEnergy()
        {
            var b=Create(lockSeconds:0);var a=U(b,"a");a.SetVitals(1000,100);
            var sim=new BattleSimulation(b,new Policy{Skill=true});for(int i=0;i<=6;i++)sim.Step();
            Assert.That(a.EnergyLockUntilTick,Is.EqualTo(6));
            b.Energy.Gain(a,.25f,EnergyChangeReason.DamageReceived);
            for(int i=0;i<30;i++)sim.Step();
            Assert.That(a.CurrentEnergy,Is.EqualTo(.25f));
        }
        [Test] public void InvalidGainDoesNotChangeEnergy()
        {
            var b=Create();foreach(float bad in new[]{-1,float.NaN,float.PositiveInfinity})
                Assert.Throws<ArgumentOutOfRangeException>(()=>b.Energy.Gain(U(b,"a"),bad,EnergyChangeReason.DamageReceived));
            Assert.That(U(b,"a").CurrentEnergy,Is.Zero);
        }
    }
}
