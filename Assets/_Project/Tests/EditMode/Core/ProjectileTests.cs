using System;
using System.Linq;
using NUnit.Framework;
using PokeChess.Core.Battle;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Tests
{
    public sealed class ProjectileTests
    {
        private static BattleState Create(float hit=0,float speed=6,float attack=10,bool reverse=false,bool multiple=false,bool extraEnemy=false)
        {
            var stats=new PokemonStats(100,attack,0,1,0,0,3,2,100,0,hit,AttackDeliveryType.Projectile,speed);
            var catalog=new PokemonCatalog(new[]{new PokemonDefinition("r","R",1,stats,"test","s")});
            Func<string,int,BoardPosition,BattleUnitSetup> make=(id,team,pos)=>new BattleUnitSetup(
                catalog.CreateUnit(id,"r","p"+team,UnitRank.One,0,UnitPlacement.Unplaced),team,pos);
            var setup=multiple?new[]{make("a",1,new BoardPosition(0,0)),make("b",1,new BoardPosition(0,1)),
                make("z",2,new BoardPosition(1,0))}:new[]{make("a",1,new BoardPosition(0,0)),make("z",2,new BoardPosition(3,0))};
            if(extraEnemy)setup=setup.Concat(new[]{make("y",2,new BoardPosition(6,7))}).ToArray();
            return BattleStateFactory.Create("b",1,123,30,catalog,reverse?setup.Reverse():setup);
        }
        private static UnitCombatState U(BattleState b,string id)=>b.Units.Single(u=>u.UnitInstanceId==id);
        private sealed class OneSide:BasicAttackCombatBehaviorPolicy
        {
            public override bool CanAttack(BattleState b,UnitCombatState u)=>u.TeamId==1;
            public override bool CanMove(BattleState b,UnitCombatState u)=>false;
        }
        [Test] public void SpawnAtAttackFrameAndHitOnlyAtArrival()
        {
            var b=Create(hit:.25f);var sim=new BattleSimulation(b,new OneSide());
            for(int i=0;i<8;i++){sim.Step();Assert.That(b.Projectiles.Active.Count,Is.Zero);}
            sim.Step();var spawn=b.Projectiles.EventsThisTick;var p=b.Projectiles.Active.Single();
            Assert.That(p.SpawnTick,Is.EqualTo(8));Assert.That(p.ArrivalTick,Is.EqualTo(23));
            Assert.That(p.LaunchPosition,Is.EqualTo(new BoardPosition(0,0)));
            Assert.That(p.TargetPositionAtLaunch,Is.EqualTo(new BoardPosition(3,0)));
            for(int i=9;i<23;i++){sim.Step();Assert.That(U(b,"z").CurrentHP,Is.EqualTo(100));}
            sim.Step();Assert.That(U(b,"z").CurrentHP,Is.EqualTo(90));
            Assert.That(b.Projectiles.Active.Count,Is.Zero);
            Assert.That(b.Projectiles.EventsThisTick.Single().Kind,Is.EqualTo(ProjectileEventKind.Hit));
            Assert.That(b.DamageResultsThisTick.Single().Tick,Is.EqualTo(23));
            sim.Step();Assert.That(b.Projectiles.EventsThisTick.Count,Is.Zero);
            Assert.That(spawn.Single().Kind,Is.EqualTo(ProjectileEventKind.Spawned));
        }
        [TestCase("dead",ProjectileExpireReason.TargetDead)]
        [TestCase("removed",ProjectileExpireReason.TargetRemoved)]
        [TestCase("hidden",ProjectileExpireReason.TargetUntargetable)]
        public void InvalidArrivalExpiresWithoutDamage(string reason,ProjectileExpireReason expected)
        {
            var b=Create();var sim=new BattleSimulation(b,new OneSide());sim.Step();
            sim.QueueInput(state=>{
                if(reason=="dead")U(state,"z").SetVitals(0,0);
                if(reason=="removed")state.TryRemoveUnit("z");
                if(reason=="hidden")U(state,"z").SetUntargetable(true);
            });
            for(int i=1;i<=15;i++)sim.Step();
            var e=b.Projectiles.EventsThisTick.Single();
            Assert.That(e.Kind,Is.EqualTo(ProjectileEventKind.Expired));Assert.That(e.Reason,Is.EqualTo(expected));
            Assert.That(b.Projectiles.Active.Count,Is.Zero);Assert.That(b.DamageResultsThisTick.Count,Is.Zero);
        }
        [Test] public void MovingBeyondRangeDoesNotChangeArrivalOrTarget()
        {
            var b=Create();var sim=new BattleSimulation(b,new OneSide());sim.Step();
            sim.QueueInput(state=>state.TryMoveUnit("z",new BoardPosition(6,7)));
            for(int i=1;i<=15;i++)sim.Step();
            Assert.That(U(b,"z").CurrentHP,Is.EqualTo(90));
            Assert.That(b.Projectiles.EventsThisTick.Single().Projectile.ArrivalTick,Is.EqualTo(15));
        }
        [Test] public void DeadOrRemovedSourceDoesNotCancelLaunchedProjectile()
        {
            foreach(bool dead in new[]{true,false})
            {
                var b=Create();var sim=new BattleSimulation(b,new OneSide());sim.Step();
                sim.QueueInput(state=>{if(dead)U(state,"a").SetVitals(0,0);else state.TryRemoveUnit("a");});
                for(int i=1;i<=15;i++)sim.Step();
                Assert.That(U(b,"z").CurrentHP,Is.EqualTo(90));
                Assert.That(b.DamageResultsThisTick.Single().Request.SourceId,Is.EqualTo("a"));
            }
        }
        [Test] public void TargetSwitchDoesNotRedirectExistingProjectile()
        {
            var b=Create(extraEnemy:true);var sim=new BattleSimulation(b,new OneSide());sim.Step();
            sim.QueueInput(state=>U(state,"a").SetTauntSource("y"));
            sim.Step();
            Assert.That(U(b,"a").CurrentTargetId,Is.EqualTo("y"));
            Assert.That(b.Projectiles.Active.Single().TargetId,Is.EqualTo("z"));
            for(int i=2;i<=15;i++)sim.Step();
            Assert.That(b.Projectiles.EventsThisTick.Single().Projectile.TargetId,Is.EqualTo("z"));
            Assert.That(U(b,"z").CurrentHP,Is.EqualTo(90));
            Assert.That(U(b,"y").CurrentHP,Is.EqualTo(100));
        }
        [Test] public void SameTickLethalArrivalsResolveByIdAndLaterProjectileExpires()
        {
            var b=Create(attack:100,multiple:true);var sim=new BattleSimulation(b,new OneSide());sim.Step();
            for(int i=1;i<=5;i++)sim.Step();
            var events=b.Projectiles.EventsThisTick;
            Assert.That(events.Count,Is.EqualTo(2));
            Assert.That(events[0].Projectile.SourceId,Is.EqualTo("a"));
            Assert.That(events[0].Kind,Is.EqualTo(ProjectileEventKind.Hit));
            Assert.That(events[1].Kind,Is.EqualTo(ProjectileEventKind.Expired));
            Assert.That(events[1].Reason,Is.EqualTo(ProjectileExpireReason.TargetDead));
            Assert.That(b.DamageResultsThisTick.Count,Is.EqualTo(1));
            Assert.That(U(b,"z").IsOnBoard,Is.False);
        }
        [TestCase("dead")] [TestCase("hidden")] [TestCase("range")]
        public void InvalidTargetBeforeFrameNeverSpawns(string reason)
        {
            var b=Create(hit:.25f);var sim=new BattleSimulation(b,new OneSide());sim.Step();
            sim.QueueInput(state=>{
                if(reason=="dead")U(state,"z").SetVitals(0,0);
                if(reason=="hidden")U(state,"z").SetUntargetable(true);
                if(reason=="range")state.TryMoveUnit("z",new BoardPosition(6,7));
            });
            for(int i=1;i<=10;i++){sim.Step();Assert.That(b.Projectiles.Active.Count,Is.Zero);}
        }
        [Test] public void VeryFastProjectileStillWaitsOneTick()
        {
            var b=Create(speed:10000);var sim=new BattleSimulation(b,new OneSide());sim.Step();
            Assert.That(U(b,"z").CurrentHP,Is.EqualTo(100));
            Assert.That(b.Projectiles.Active.Single().ArrivalTick,Is.EqualTo(1));
            sim.Step();Assert.That(U(b,"z").CurrentHP,Is.EqualTo(90));
        }
        [Test] public void InvalidDeliveryAndSpeedAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(()=>new PokemonStats(100,10,0,1,0,0,1,2,100,0,0,(AttackDeliveryType)99));
            foreach(float speed in new[]{0,-1,float.NaN,float.PositiveInfinity})
                Assert.Throws<ArgumentOutOfRangeException>(()=>new PokemonStats(100,10,0,1,0,0,1,2,100,0,0,AttackDeliveryType.Projectile,speed));
        }
        [Test] public void ReversedRegistrationProducesSameProjectileAndDamageTrace()
        {
            string Run(bool reverse)
            {
                var b=Create(reverse:reverse,multiple:true);var sim=new BattleSimulation(b,new OneSide());var trace="";
                for(int i=0;i<70;i++){
                    sim.Step();foreach(var e in b.Projectiles.EventsThisTick)
                        trace+=e.Tick+":"+e.Projectile.Id+":"+e.Projectile.SourceId+":"+e.Kind+":"+e.Reason+";";
                    foreach(var d in b.DamageResultsThisTick)trace+=d.RemainingHP+";";
                }
                return trace;
            }
            Assert.That(Run(false),Is.EqualTo(Run(true)));
        }
    }
}
