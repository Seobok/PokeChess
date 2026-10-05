using System;
using System.Linq;
using NUnit.Framework;
using PokeChess.Core.Battle;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Tests
{
    public sealed class DamagePipelineTests
    {
        [TestCase(DamageType.Physical,0,999,100)]
        [TestCase(DamageType.Physical,100,0,50)]
        [TestCase(DamageType.Physical,-50,0,133)]
        [TestCase(DamageType.Magic,999,100,50)]
        [TestCase(DamageType.Magic,999,-50,133)]
        [TestCase(DamageType.True,999,999,100)]
        public void ResistanceUsesCorrectType(DamageType type,float armor,float mr,double expected)
        { Assert.That(DamageCalculator.Calculate(100,type,armor,mr),Is.EqualTo(expected).Within(.0001)); }
        [TestCase(DamageType.Physical,.1f,1)]
        [TestCase(DamageType.Magic,.1f,1)]
        [TestCase(DamageType.True,.1f,0)]
        [TestCase(DamageType.Physical,0,0)]
        [TestCase(DamageType.Magic,0,0)]
        [TestCase(DamageType.True,0,0)]
        public void MinimumAndZeroDamageRules(DamageType type,float raw,float expected)
        { Assert.That(DamageCalculator.Calculate(raw,type,10000,10000),Is.EqualTo(expected)); }
        private static BattleState Create(AttackDeliveryType delivery=AttackDeliveryType.Melee,float attack=100)
        {
            var stats=new PokemonStats(100,attack,0,1,100,300,3,2,100,0,0,delivery,6);
            var catalog=new PokemonCatalog(new[]{new PokemonDefinition("test","Test",1,stats,"test","s")});
            return BattleStateFactory.Create("b",1,123,30,catalog,new[]{
                new BattleUnitSetup(catalog.CreateUnit("a","test","p1",UnitRank.One,0,UnitPlacement.Unplaced),1,new BoardPosition(0,0)),
                new BattleUnitSetup(catalog.CreateUnit("z","test","p2",UnitRank.One,0,UnitPlacement.Unplaced),2,new BoardPosition(1,0))});
        }
        private static UnitCombatState U(BattleState b,string id)=>b.Units.Single(u=>u.UnitInstanceId==id);
        [TestCase(DamageType.Physical,30,50,30,20,80)]
        [TestCase(DamageType.Magic,30,25,25,0,100)]
        [TestCase(DamageType.True,30,100,30,70,30)]
        [TestCase(DamageType.Physical,100,50,50,0,100)]
        [TestCase(DamageType.Physical,0,50,0,50,50)]
        public void ShieldThenHpAndStageResults(DamageType type,float shield,float post,float absorbed,float hpDamage,float hp)
        {
            var b=Create();var target=U(b,"z");target.SetShield(shield);target.SetVitals(100,17);
            var d=new DamageProcessor().Apply(b,new DamageRequest("a","z",100,type));
            Assert.That(d.PreMitigationDamage,Is.EqualTo(100));
            Assert.That(d.PostMitigationDamage,Is.EqualTo(post));
            Assert.That(d.ShieldAbsorbed,Is.EqualTo(absorbed));
            Assert.That(d.AppliedDamage,Is.EqualTo(hpDamage));
            Assert.That(d.RemainingHP,Is.EqualTo(hp));
            Assert.That(d.RemainingShield,Is.EqualTo(shield-absorbed));
            Assert.That(target.CurrentEnergy,Is.EqualTo(17));
            Assert.That(d.Killed,Is.False);
            Assert.That(b.DamageResultsThisTick.Count,Is.EqualTo(1));
        }
        [Test] public void OverkillClampsHpAndRemovesOccupancy()
        {
            var b=Create();U(b,"z").SetShield(30);
            var d=new DamageProcessor().Apply(b,new DamageRequest("a","z",500,DamageType.True));
            Assert.That(d.PostMitigationDamage,Is.EqualTo(500));
            Assert.That(d.AppliedDamage,Is.EqualTo(100));Assert.That(d.ShieldAbsorbed,Is.EqualTo(30));
            Assert.That(d.RemainingHP,Is.Zero);Assert.That(d.Killed,Is.True);
            Assert.That(U(b,"z").IsOnBoard,Is.False);
            Assert.That(b.Board.IsOccupied(new BoardPosition(1,0)),Is.False);
        }
        [Test] public void ZeroDamageDoesNotConsumeShield()
        {
            var b=Create();U(b,"z").SetShield(30);
            var d=new DamageProcessor().Apply(b,new DamageRequest("a","z",0,DamageType.Physical));
            Assert.That(d.AppliedDamage,Is.Zero);Assert.That(d.RemainingShield,Is.EqualTo(30));
        }
        [Test] public void InvalidInputsAreRejectedBeforeMutation()
        {
            var b=Create();var t=U(b,"z");t.SetShield(30);
            foreach(float bad in new[]{-1,float.NaN,float.PositiveInfinity})
            {
                Assert.Throws<ArgumentOutOfRangeException>(()=>t.SetShield(bad));
                Assert.Throws<ArgumentOutOfRangeException>(()=>new DamageRequest("a","z",bad,DamageType.Physical));
                Assert.Throws<ArgumentOutOfRangeException>(()=>DamageCalculator.Calculate(bad,DamageType.Physical,0,0));
            }
            Assert.Throws<ArgumentOutOfRangeException>(()=>new DamageRequest("a","z",10,(DamageType)99));
            Assert.Throws<ArgumentOutOfRangeException>(()=>DamageCalculator.Calculate(10,DamageType.Physical,float.NaN,0));
            Assert.Throws<OverflowException>(()=>DamageCalculator.Calculate(float.MaxValue,DamageType.Physical,-100,0));
            Assert.That(t.CurrentHP,Is.EqualTo(100));Assert.That(t.CurrentShield,Is.EqualTo(30));
            Assert.That(b.DamageResultsThisTick.Count,Is.Zero);
        }
        [TestCase("dead")] [TestCase("removed")] [TestCase("hidden")] [TestCase("ally")]
        public void InvalidTargetCannotReceiveDamage(string reason)
        {
            var b=Create();
            if(reason=="dead")U(b,"z").SetVitals(0,0);
            if(reason=="removed")b.TryRemoveUnit("z");
            if(reason=="hidden")U(b,"z").SetUntargetable(true);
            Assert.Throws<InvalidOperationException>(()=>new DamageProcessor().Apply(b,
                new DamageRequest("a",reason=="ally"?"a":"z",10,DamageType.Physical)));
            Assert.That(b.DamageResultsThisTick.Count,Is.Zero);
        }
        private sealed class OneSide:BasicAttackCombatBehaviorPolicy
        {
            public override bool CanAttack(BattleState b,UnitCombatState u)=>u.TeamId==1;
            public override bool CanMove(BattleState b,UnitCombatState u)=>false;
        }
        [TestCase(AttackDeliveryType.Melee,0)]
        [TestCase(AttackDeliveryType.Projectile,5)]
        public void BasicAttacksUseSharedPipeline(AttackDeliveryType delivery,int arrival)
        {
            var b=Create(delivery);U(b,"z").SetShield(30);
            var sim=new BattleSimulation(b,new OneSide());
            for(int i=0;i<=arrival;i++)sim.Step();
            Assert.That(U(b,"z").CurrentHP,Is.EqualTo(80));
            Assert.That(U(b,"z").CurrentShield,Is.Zero);
            Assert.That(b.DamageResultsThisTick.Single().PostMitigationDamage,Is.EqualTo(50));
        }
        [Test] public void LaunchedProjectileStillDamagesAfterSourceDeath()
        {
            var b=Create(AttackDeliveryType.Projectile);var sim=new BattleSimulation(b,new OneSide());sim.Step();
            sim.QueueInput(state=>U(state,"a").SetVitals(0,0));
            for(int i=1;i<=5;i++)sim.Step();
            Assert.That(U(b,"z").CurrentHP,Is.EqualTo(50));
        }
        [Test] public void LethalDefaultAttackCleansUpAndRetargets()
        {
            var b=Create(attack:500);var sim=new BattleSimulation(b,new OneSide());sim.Step();
            Assert.That(U(b,"z").IsOnBoard,Is.False);
            Assert.That(U(b,"z").ActionState,Is.EqualTo(CombatActionState.Dead));
            Assert.That(U(b,"a").CurrentTargetId,Is.Null);
            Assert.That(sim.Reservations.Count,Is.Zero);
        }
    }
}
