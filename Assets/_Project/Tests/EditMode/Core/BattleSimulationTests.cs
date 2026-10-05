using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PokeChess.Core.Battle;
using PokeChess.Core.Board;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Tests
{
    public sealed class BattleSimulationTests
    {
        private static BoardPosition P(int c,int r)=>new BoardPosition(c,r);
        private static BattleState Battle(params (string id,int team,BoardPosition p)[] entries)
        {
            var catalog=new PokemonCatalog(new[]{
                new PokemonDefinition("test","Test",1,new PokemonStats(100,10,0,1,0,0,1,2,100,0,.1f),"r","s")});
            return BattleStateFactory.Create("b",1,0,30,catalog,entries.Select(e=>
                new BattleUnitSetup(catalog.CreateUnit(e.id,"test","p"+e.team,UnitRank.One,0,UnitPlacement.Unplaced),e.team,e.p)));
        }
        private static UnitCombatState U(BattleState b,string id)=>b.Units.Single(u=>u.UnitInstanceId==id);
        private sealed class Policy:CombatBehaviorPolicy
        {
            public bool Movement=true;
            public bool Attacks=true;
            public bool Skill;
            public bool ContinueSkill=true;
            public int Hits,Skills,Starts;
            public override bool CanMove(BattleState b,UnitCombatState u)=>Movement&&u.TeamId==1;
            public override bool CanAttack(BattleState b,UnitCombatState u)=>Attacks&&u.TeamId==1;
            public override bool TryGetSkillTiming(BattleState b,UnitCombatState u,UnitCombatState target,out CombatActionTiming timing)
            {timing=new CombatActionTiming(6,3);return Skill&&u.TeamId==1&&Skills==0;}
            public override bool CanContinueSkill(BattleState b,UnitCombatState u,UnitCombatState t)=>ContinueSkill&&base.CanContinueSkill(b,u,t);
            public override void OnSkillStarted(BattleState b,UnitCombatState u,UnitCombatState t){Starts++;}
            public override void OnAttackTiming(BattleState b,UnitCombatState u,UnitCombatState t){Hits++;}
            public override void OnSkillTiming(BattleState b,UnitCombatState u,UnitCombatState t){Skills++;}
        }
        [Test]
        public void MovementMaintainsSourceUntilTick15ThenCommitsOneHex()
        {
            var b=Battle(("a",1,P(0,0)),("z",2,P(6,7)));var sim=new BattleSimulation(b,new Policy());
            sim.Step();var destination=sim.GetRuntime("a").MoveDestination.Value;
            Assert.That(U(b,"a").ActionState,Is.EqualTo(CombatActionState.Moving));
            Assert.That(sim.GetRuntime("a").EndTick,Is.EqualTo(15));
            for(int i=0;i<14;i++)sim.Step();
            Assert.That(U(b,"a").Position,Is.EqualTo(P(0,0)));
            Assert.That(b.Board.IsOccupied(P(0,0)),Is.True);Assert.That(b.Board.IsOccupied(destination),Is.False);
            sim.Step();
            Assert.That(U(b,"a").Position,Is.EqualTo(destination));
            Assert.That(HexCoordinates.Distance(P(0,0),destination),Is.EqualTo(1));
            Assert.That(sim.Reservations.Count,Is.Zero);
            Assert.That(U(b,"a").ActionState,Is.EqualTo(CombatActionState.Idle));
        }
        [Test]
        public void AlliesCannotReserveSameDestination()
        {
            var b=Battle(("a",1,P(2,1)),("b",1,P(2,3)),("z",2,P(5,2)));
            var sim=new BattleSimulation(b,new Policy());sim.Step();
            var destinations=b.Units.Where(u=>u.ActionState==CombatActionState.Moving)
                .Select(u=>sim.GetRuntime(u.UnitInstanceId).MoveDestination.Value).ToArray();
            Assert.That(destinations.Length,Is.EqualTo(destinations.Distinct().Count()));
            Assert.That(sim.Reservations.Count,Is.EqualTo(destinations.Length));
            foreach(var p in destinations)Assert.That(b.Board.IsOccupied(p),Is.False);
        }
        [Test]
        public void MovementPermissionCancellationReleasesReservation()
        {
            var b=Battle(("a",1,P(0,0)),("z",2,P(6,7)));var policy=new Policy();var sim=new BattleSimulation(b,policy);
            sim.Step();policy.Movement=false;var events=sim.Step();
            Assert.That(sim.Reservations.Count,Is.Zero);Assert.That(U(b,"a").Position,Is.EqualTo(P(0,0)));
            Assert.That(events.Any(e=>e.UnitId=="a"&&e.Kind==CombatActionSignalKind.Cancelled),Is.True);
        }
        [Test]
        public void DeathOrRemovalCancelsMovement()
        {
            foreach(bool remove in new[]{false,true})
            {
                var b=Battle(("a",1,P(0,0)),("z",2,P(6,7)));var sim=new BattleSimulation(b,new Policy());
                sim.Step();
                sim.QueueInput(state=>{if(remove)state.TryRemoveUnit("a");else U(state,"a").SetVitals(0,0);});
                sim.Step();
                Assert.That(sim.Reservations.Count,Is.Zero);
                Assert.That(U(b,"a").ActionState,Is.EqualTo(CombatActionState.Dead));
                Assert.That(U(b,"a").CurrentTargetId,Is.Null);
            }
        }
        [Test]
        public void TargetRemovalDuringMoveFinishesCurrentHexThenRetargets()
        {
            var b=Battle(("a",1,P(0,0)),("y",2,P(3,0)),("z",2,P(6,7)));var sim=new BattleSimulation(b,new Policy());
            sim.Step();var next=sim.GetRuntime("a").MoveDestination.Value;
            b.TryRemoveUnit("y");
            for(int i=0;i<15;i++)sim.Step();
            Assert.That(U(b,"a").Position,Is.EqualTo(next));
            sim.Step();Assert.That(U(b,"a").CurrentTargetId,Is.EqualTo("z"));
        }
        [Test]
        public void AttackTimingOccursExactlyOnceAndDefaultPolicyDoesNotDealDamage()
        {
            var b=Battle(("a",1,P(0,0)),("z",2,P(1,0)));var policy=new Policy();var sim=new BattleSimulation(b,policy);
            for(int i=0;i<=30;i++)sim.Step();
            Assert.That(policy.Hits,Is.EqualTo(1));
            Assert.That(U(b,"z").CurrentHP,Is.EqualTo(100));
            Assert.That(U(b,"a").ActionState,Is.EqualTo(CombatActionState.Idle));
            Assert.That(sim.Reservations.Count,Is.Zero);
        }
        [Test]
        public void AttackBeforeHitCancelsWhenTargetLeavesRange()
        {
            var b=Battle(("a",1,P(0,0)),("z",2,P(1,0)));var policy=new Policy();var sim=new BattleSimulation(b,policy);
            sim.Step();b.TryMoveUnit("z",P(6,7));var events=sim.Step();
            Assert.That(policy.Hits,Is.Zero);
            Assert.That(events.Any(e=>e.Action==CombatActionState.Attacking&&e.Kind==CombatActionSignalKind.Cancelled),Is.True);
            Assert.That(U(b,"a").CurrentTargetId,Is.EqualTo("z"));
            Assert.That(U(b,"a").ActionState,Is.EqualTo(CombatActionState.Moving));
        }
        [Test]
        public void SkillCastHasOneStartAndEffectThenReturnsToIdle()
        {
            var b=Battle(("a",1,P(0,0)),("z",2,P(1,0)));U(b,"a").SetVitals(100,100);var policy=new Policy{Skill=true};var sim=new BattleSimulation(b,policy);
            sim.Step();Assert.That(U(b,"a").ActionState,Is.EqualTo(CombatActionState.Casting));
            for(int i=0;i<6;i++)sim.Step();
            Assert.That(policy.Starts,Is.EqualTo(1));Assert.That(policy.Skills,Is.EqualTo(1));Assert.That(policy.Hits,Is.Zero);
            Assert.That(U(b,"a").ActionState,Is.EqualTo(CombatActionState.Idle));
            Assert.That(U(b,"a").Position,Is.EqualTo(P(0,0)));
        }
        [Test]
        public void InterruptedSkillDoesNotApplyEffect()
        {
            var b=Battle(("a",1,P(0,0)),("z",2,P(1,0)));U(b,"a").SetVitals(100,100);var policy=new Policy{Skill=true};var sim=new BattleSimulation(b,policy);
            sim.Step();policy.ContinueSkill=false;sim.Step();
            Assert.That(policy.Skills,Is.Zero);Assert.That(U(b,"a").ActionState,Is.EqualTo(CombatActionState.Idle));
        }
        [Test]
        public void BlockedUnitWaitsAndResumesWhenPassageOpens()
        {
            var b=Battle(("a",1,P(0,0)),("x",1,P(1,0)),("y",1,P(0,1)),("z",2,P(6,7)));
            var policy=new StillAlliesPolicy();var sim=new BattleSimulation(b,policy);
            sim.Step();Assert.That(U(b,"a").ActionState,Is.EqualTo(CombatActionState.Idle));
            b.TryRemoveUnit("x");sim.Step();Assert.That(U(b,"a").ActionState,Is.EqualTo(CombatActionState.Moving));
        }
        private sealed class StillAlliesPolicy:CombatBehaviorPolicy
        {public override bool CanMove(BattleState b,UnitCombatState u)=>u.UnitInstanceId=="a";public override bool CanAttack(BattleState b,UnitCombatState u)=>false;}

        [Test]
        public void InputsRunBeforeActionsAndEnqueuedInputsWaitForNextTick()
        {
            var b=Battle(("a",1,P(0,0)),("z",2,P(6,7)));var sim=new BattleSimulation(b,new Policy());int calls=0;
            sim.QueueInput(state=>{calls++;sim.QueueInput(s=>calls++);U(state,"z").SetUntargetable(true);});
            sim.Step();Assert.That(calls,Is.EqualTo(1));Assert.That(U(b,"a").ActionState,Is.EqualTo(CombatActionState.Idle));
            sim.Step();Assert.That(calls,Is.EqualTo(2));Assert.That(b.CurrentTick,Is.EqualTo(2));
        }
        [Test]
        public void RepeatedRunsAndReversedSetupProduceIdenticalTrace()
        {
            string Run(bool reverse)
            {
                var entries=new[]{("a",1,P(0,0)),("b",1,P(2,0)),("z",2,P(6,7))};
                var b=Battle(reverse?entries.Reverse().ToArray():entries);var sim=new BattleSimulation(b);
                var trace=new List<string>();
                for(int i=0;i<200;i++)
                {
                    foreach(var e in sim.Step())trace.Add(e.Tick+":"+e.UnitId+":"+e.Action+":"+e.Kind);
                    foreach(var u in b.Units.OrderBy(u=>u.UnitInstanceId))
                        trace.Add(u.UnitInstanceId+":"+u.Position.Column+":"+u.Position.Row+":"+u.ActionState);
                }
                return string.Join("|",trace);
            }
            Assert.That(Run(false),Is.EqualTo(Run(true)));
        }
        [Test]
        public void DestinationOccupiedBeforeArrivalCancelsWithoutChangingSource()
        {
            var b=Battle(("a",1,P(0,0)),("y",2,P(6,6)),("z",2,P(6,7)));
            var sim=new BattleSimulation(b,new Policy());sim.Step();
            var next=sim.GetRuntime("a").MoveDestination.Value;
            Assert.That(b.TryMoveUnit("y",next),Is.EqualTo(BoardOperationResult.Success));
            for(int i=0;i<15;i++)sim.Step();
            Assert.That(U(b,"a").Position,Is.EqualTo(P(0,0)));
            Assert.That(sim.Reservations.Count,Is.Zero);
            Assert.That(U(b,"a").ActionState,Is.EqualTo(CombatActionState.Idle));
        }
        private sealed class ImmediateKillPolicy:CombatBehaviorPolicy
        {
            public override string SelectTarget(BattleState b,UnitCombatState u)=>
                u.UnitInstanceId=="a"?"y":u.UnitInstanceId=="z"?"a":null;
            public override bool CanMove(BattleState b,UnitCombatState u)=>u.UnitInstanceId=="a";
            public override bool CanAttack(BattleState b,UnitCombatState u)=>u.UnitInstanceId=="z";
            public override CombatActionTiming GetAttackTiming(BattleState b,UnitCombatState u)=>new CombatActionTiming(1,0);
            public override void OnAttackTiming(BattleState b,UnitCombatState u,UnitCombatState t)=>t.SetVitals(0,0);
        }
        [Test]
        public void SameTickEffectDeathReleasesEarlierMoversReservation()
        {
            var b=Battle(("a",1,P(0,0)),("y",2,P(6,7)),("z",2,P(1,0)));
            var sim=new BattleSimulation(b,new ImmediateKillPolicy());
            var events=sim.Step();
            Assert.That(U(b,"a").ActionState,Is.EqualTo(CombatActionState.Dead));
            Assert.That(sim.Reservations.Count,Is.Zero);
            Assert.That(events.Count(e=>e.UnitId=="z"&&e.Kind==CombatActionSignalKind.TimingReached),Is.EqualTo(1));
            sim.Step();
            Assert.That(U(b,"a").Position,Is.EqualTo(P(0,0)));
        }

        [Test]
        public void TickConversionAndSingleOwnerAreValidated()
        {
            Assert.That(BattleSimulation.ToTicks(.5,30),Is.EqualTo(15));
            Assert.That(BattleSimulation.ToTicks(0,30,false),Is.Zero);
            Assert.That(BattleSimulation.ToTicks(.001,30),Is.EqualTo(1));
            Assert.Throws<ArgumentOutOfRangeException>(()=>BattleSimulation.ToTicks(double.NaN,30));
            Assert.Throws<ArgumentOutOfRangeException>(()=>new CombatActionTiming(1,2));
            var b=Battle(("a",1,P(0,0)),("z",2,P(6,7)));new BattleSimulation(b);
            Assert.Throws<InvalidOperationException>(()=>new BattleSimulation(b));
        }
    }
}
