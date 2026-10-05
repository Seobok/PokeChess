using System;
using System.Linq;
using NUnit.Framework;
using PokeChess.Core.Battle;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Tests
{
    public sealed class MeleeAttackTests
    {
        private static BattleState Create(float attack=10,float hit=.25f,float speed=1,int range=1,bool reversed=false)
        {
            var catalog=new PokemonCatalog(new[]{
                new PokemonDefinition("test","Test",1,new PokemonStats(100,attack,0,speed,0,0,range,2,100,0,hit,range>1?AttackDeliveryType.Projectile:AttackDeliveryType.Melee),"r","s")});
            var setup=new[]{
                new BattleUnitSetup(catalog.CreateUnit("a","test","p1",UnitRank.One,0,UnitPlacement.Unplaced),1,new BoardPosition(0,0)),
                new BattleUnitSetup(catalog.CreateUnit("z","test","p2",UnitRank.One,0,UnitPlacement.Unplaced),2,new BoardPosition(1,0))};
            return BattleStateFactory.Create("b",1,123,30,catalog,reversed?setup.Reverse():setup);
        }
        private static UnitCombatState U(BattleState b,string id)=>b.Units.Single(u=>u.UnitInstanceId==id);
        private sealed class OneSide:MeleeCombatBehaviorPolicy
        {
            public override bool CanAttack(BattleState b,UnitCombatState u)=>u.TeamId==1&&base.CanAttack(b,u);
            public override bool CanMove(BattleState b,UnitCombatState u)=>false;
        }
        [Test] public void DamageOccursOnceAtHitTickAndResultsAreDetached()
        {
            var b=Create();var sim=new BattleSimulation(b,new OneSide());
            for(int i=0;i<8;i++){sim.Step();Assert.That(U(b,"z").CurrentHP,Is.EqualTo(100));}
            sim.Step();var results=b.DamageResultsThisTick;
            Assert.That(results.Count,Is.EqualTo(1));
            Assert.That(results[0].Tick,Is.EqualTo(8));
            Assert.That(results[0].AppliedDamage,Is.EqualTo(10));
            Assert.That(results[0].Request.Type,Is.EqualTo(DamageType.Physical));
            for(int i=0;i<22;i++)sim.Step();
            Assert.That(U(b,"z").CurrentHP,Is.EqualTo(90));
            Assert.That(b.DamageResultsThisTick.Count,Is.Zero);
            Assert.That(results.Count,Is.EqualTo(1));
            sim.Step();Assert.That(sim.GetRuntime("a").StartTick,Is.EqualTo(31));
        }
        [TestCase("dead")] [TestCase("hidden")] [TestCase("removed")] [TestCase("range")]
        public void InvalidTargetBeforeHitProducesNoDamage(string reason)
        {
            var b=Create();var sim=new BattleSimulation(b,new OneSide());sim.Step();
            sim.QueueInput(state=>{
                if(reason=="dead")U(state,"z").SetVitals(0,0);
                if(reason=="hidden")U(state,"z").SetUntargetable(true);
                if(reason=="removed")state.TryRemoveUnit("z");
                if(reason=="range")state.TryMoveUnit("z",new BoardPosition(6,7));
            });
            for(int i=0;i<9;i++){sim.Step();Assert.That(b.DamageResultsThisTick.Count,Is.Zero);}
            Assert.That(sim.GetRuntime("a").NextAttackTick,Is.EqualTo(30));
        }
        [Test] public void LethalHitClampsHpRemovesBoardAndCancelsVictimAttack()
        {
            var b=Create(200,0);var sim=new BattleSimulation(b);sim.Step();
            Assert.That(U(b,"z").CurrentHP,Is.Zero);Assert.That(U(b,"z").IsOnBoard,Is.False);
            Assert.That(U(b,"z").ActionState,Is.EqualTo(CombatActionState.Dead));
            Assert.That(b.Board.IsOccupied(new BoardPosition(1,0)),Is.False);
            Assert.That(sim.Reservations.Count,Is.Zero);
            Assert.That(b.DamageResultsThisTick.Single().AppliedDamage,Is.EqualTo(100));
            Assert.That(b.DamageResultsThisTick.Single().Killed,Is.True);
            Assert.That(U(b,"a").CurrentHP,Is.EqualTo(100));
        }
        [TestCase(.01f,300)] [TestCase(20f,6)]
        public void AttackSpeedIsClamped(float speed,int expected)
        {
            var b=Create(hit:0,speed:speed);
            Assert.That(new CombatBehaviorPolicy().GetAttackTiming(b,U(b,"a")).DurationTicks,Is.EqualTo(expected));
        }
        [Test] public void HitLongerThanIntervalStillOccursBeforeCompletion()
        {
            var b=Create(hit:.5f,speed:5);var sim=new BattleSimulation(b,new OneSide());
            Assert.That(new CombatBehaviorPolicy().GetAttackTiming(b,U(b,"a")).DurationTicks,Is.EqualTo(15));
            for(int i=0;i<15;i++)sim.Step();
            Assert.That(U(b,"z").CurrentHP,Is.EqualTo(100));
            sim.Step();Assert.That(U(b,"z").CurrentHP,Is.EqualTo(90));
        }
        [Test] public void RangedUnitsDoNotApplyInstantMeleeDamage()
        {
            var b=Create(range:3);var sim=new BattleSimulation(b,new MeleeCombatBehaviorPolicy());
            for(int i=0;i<60;i++)sim.Step();
            Assert.That(U(b,"z").CurrentHP,Is.EqualTo(100));
            Assert.That(U(b,"a").CurrentHP,Is.EqualTo(100));
        }
        [Test] public void SameInputsAndReversedSetupProduceSameDamageTrace()
        {
            string Run(bool reverse)
            {
                var b=Create(reversed:reverse);var sim=new BattleSimulation(b);var trace="";
                for(int i=0;i<100;i++){
                    sim.Step();
                    foreach(var d in b.DamageResultsThisTick)
                        trace+=d.Tick+":"+d.Request.SourceId+":"+d.Request.TargetId+":"+d.RemainingHP+";";
                }
                return trace;
            }
            Assert.That(Run(false),Is.EqualTo(Run(true)));
        }

        [Test] public void DeadAttackerCannotHit()
        {
            var b=Create(200,.25f);var sim=new BattleSimulation(b,new OneSide());sim.Step();
            sim.QueueInput(state=>U(state,"a").SetVitals(0,0));
            for(int i=0;i<9;i++)sim.Step();
            Assert.That(U(b,"z").CurrentHP,Is.EqualTo(100));
            Assert.That(U(b,"a").IsOnBoard,Is.False);

        }
        [Test] public void FractionalDamagePreservesEnergy()
        {
            var b=Create(10.5f,0);U(b,"z").SetVitals(100,17);
            var sim=new BattleSimulation(b,new OneSide());sim.Step();
            Assert.That(U(b,"z").CurrentHP,Is.EqualTo(89.5f));
            Assert.That(U(b,"z").CurrentEnergy,Is.EqualTo(17));
            Assert.That(b.DamageResultsThisTick.Single().Request.RawDamage,Is.EqualTo(10.5f));
        }
    }
}
