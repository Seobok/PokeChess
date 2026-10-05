using System;
using System.Linq;
using NUnit.Framework;
using PokeChess.Core.Battle;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Tests
{
    public sealed class DamagePolicyTests
    {
        private static BattleState Create(float chance=.5f,AttackDeliveryType delivery=AttackDeliveryType.Melee)
        {
            var stats=new PokemonStats(10000,100,0,1,0,0,3,2,100,0,0,delivery,6,chance,1.5f);
            var c=new PokemonCatalog(new[]{new PokemonDefinition("t","Test",1,stats,"r","s")});
            return BattleStateFactory.Create("b",1,123,30,c,new[]{
                new BattleUnitSetup(c.CreateUnit("a","t","p1",UnitRank.One,0,UnitPlacement.Unplaced),1,new BoardPosition(0,0)),
                new BattleUnitSetup(c.CreateUnit("z","t","p2",UnitRank.One,0,UnitPlacement.Unplaced),2,new BoardPosition(1,0))});
        }
        private static UnitCombatState U(BattleState b,string id)=>b.Units.Single(u=>u.UnitInstanceId==id);
        [Test] public void BonusCritAmplificationResistanceReductionOrderIsRecorded()
        {
            var d=DamageCalculator.CalculateDetailed(100,20,DamageType.Physical,100,0,true,1.5f,.5f,.25f);
            Assert.That(d.AfterBonus,Is.EqualTo(120));Assert.That(d.AfterCritical,Is.EqualTo(180));
            Assert.That(d.AfterAmplification,Is.EqualTo(270));Assert.That(d.AfterResistance,Is.EqualTo(135));
            Assert.That(d.AfterReduction,Is.EqualTo(101.25));Assert.That(d.FinalDamage,Is.EqualTo(101));
        }
        [TestCase(10.4f,10)] [TestCase(10.5f,11)] [TestCase(10.6f,11)] [TestCase(.4f,0)] [TestCase(.5f,1)]
        public void TrueDamageRoundsOnceAwayFromZero(float raw,float expected)
        { Assert.That(DamageCalculator.Calculate(raw,DamageType.True,0,0),Is.EqualTo(expected)); }
        [Test] public void MinimumIsAppliedAfterFullReductionAndTrueBypassesModifiers()
        {
            var normal=DamageCalculator.CalculateDetailed(100,0,DamageType.Physical,100,0,false,1,0,2);
            Assert.That(normal.AfterReduction,Is.Zero);Assert.That(normal.FinalDamage,Is.EqualTo(1));
            var t=DamageCalculator.CalculateDetailed(10.5f,0,DamageType.True,10000,10000,true,2,10,1);
            Assert.That(t.IsCritical,Is.False);Assert.That(t.AfterAmplification,Is.EqualTo(10.5f));
            Assert.That(t.FinalDamage,Is.EqualTo(11));
        }
        [TestCase(0,false)] [TestCase(1,true)]
        public void CertainChancesDoNotConsumeRng(float chance,bool expected)
        {
            var b=Create(chance);var d=new DamageProcessor().Apply(b,new DamageRequest("a","z",10,DamageType.Physical,true));
            Assert.That(d.IsCritical,Is.EqualTo(expected));Assert.That(b.RngStreams.Critical.DrawCount,Is.Zero);
        }
        [Test] public void SkillZeroTrueAndInvalidTargetsDoNotConsumeCriticalRng()
        {
            var b=Create();var p=new DamageProcessor();
            Assert.That(p.Apply(b,new DamageRequest("a","z",10,DamageType.Magic)).IsCritical,Is.False);
            p.Apply(b,new DamageRequest("a","z",0,DamageType.Physical,true));
            p.Apply(b,new DamageRequest("a","z",10,DamageType.True,true));
            U(b,"z").SetUntargetable(true);
            Assert.Throws<InvalidOperationException>(()=>p.Apply(b,new DamageRequest("a","z",10,DamageType.Physical,true)));
            Assert.That(b.RngStreams.Critical.DrawCount,Is.Zero);
        }
        [Test] public void CriticalSequenceIsStableAndIndependentOfOtherStreams()
        {
            string Run(bool consume)
            {
                var b=Create();if(consume)for(int i=0;i<100;i++){
                    b.RngStreams.Target.NextUInt64();b.RngStreams.Skill.NextUInt64();b.RngStreams.Item.NextUInt64();
                }
                var trace="";for(int i=0;i<30;i++)
                    trace+=new DamageProcessor().Apply(b,new DamageRequest("a","z",10,DamageType.Physical,true)).IsCritical+";";
                Assert.That(b.RngStreams.Critical.DrawCount,Is.EqualTo(30));
                Assert.That(trace.Contains("True"),Is.True);Assert.That(trace.Contains("False"),Is.True);
                return trace;
            }
            Assert.That(Run(false),Is.EqualTo(Run(true)));
        }
        [Test] public void AggregatedModifiersAndPrePostValuesApplyBeforeShield()
        {
            var b=Create(1);U(b,"a").SetDamageModifiers(new DamageModifiers(20,.5f));
            U(b,"z").SetDamageModifiers(new DamageModifiers(reduction:.25f));U(b,"z").SetShield(30);
            var d=new DamageProcessor().Apply(b,new DamageRequest("a","z",100,DamageType.Physical,true,10));
            Assert.That(d.Calculation.AfterBonus,Is.EqualTo(130));
            Assert.That(d.PreMitigationDamage,Is.EqualTo(292.5f));
            Assert.That(d.PostMitigationDamage,Is.EqualTo(219));
            Assert.That(d.ShieldAbsorbed,Is.EqualTo(30));Assert.That(d.AppliedDamage,Is.EqualTo(189));
        }
        private sealed class OneSide:BasicAttackCombatBehaviorPolicy
        {
            public override bool CanAttack(BattleState b,UnitCombatState u)=>u.TeamId==1;
            public override bool CanMove(BattleState b,UnitCombatState u)=>false;
        }
        [TestCase(AttackDeliveryType.Melee,0)] [TestCase(AttackDeliveryType.Projectile,5)]
        public void BasicAttackCritIsResolvedAtActualHit(AttackDeliveryType delivery,int arrival)
        {
            var b=Create(.5f,delivery);var sim=new BattleSimulation(b,new OneSide());
            for(int i=0;i<arrival;i++){sim.Step();Assert.That(b.RngStreams.Critical.DrawCount,Is.Zero);}
            sim.Step();Assert.That(b.RngStreams.Critical.DrawCount,Is.EqualTo(1));
            Assert.That(b.DamageResultsThisTick.Single().Request.CanCrit,Is.True);
        }
        [Test] public void ExpiredProjectileDoesNotRollCrit()
        {
            var b=Create(.5f,AttackDeliveryType.Projectile);var sim=new BattleSimulation(b,new OneSide());sim.Step();
            sim.QueueInput(state=>U(state,"z").SetUntargetable(true));
            for(int i=1;i<=5;i++)sim.Step();
            Assert.That(b.RngStreams.Critical.DrawCount,Is.Zero);
            Assert.That(b.Projectiles.EventsThisTick.Single().Kind,Is.EqualTo(ProjectileEventKind.Expired));
        }
        [Test] public void InvalidPolicyAndOverflowDoNotMutateOrConsumeRng()
        {
            Assert.Throws<ArgumentOutOfRangeException>(()=>new DamageModifiers(amplification:-1));
            Assert.Throws<ArgumentOutOfRangeException>(()=>new DamageModifiers(reduction:float.NaN));
            Assert.That(new DamageModifiers(reduction:2).Reduction,Is.EqualTo(1));
            var b=Create();var p=new DamageProcessor();
            Assert.Throws<OverflowException>(()=>p.Apply(b,new DamageRequest("a","z",float.MaxValue,DamageType.Physical,true)));
            Assert.That(b.RngStreams.Critical.DrawCount,Is.Zero);Assert.That(U(b,"z").CurrentHP,Is.EqualTo(10000));
        }
    }
}
