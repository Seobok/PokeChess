using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using PokeChess.Core.Battle;
using PokeChess.Core.Board;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Tests
{
    public sealed class SkillSpatialTests
    {
        private static BoardPosition P(int c,int r)=>new BoardPosition(c,r);
        private static UnitCombatState U(BattleState b,string id)=>b.Units.Single(u=>u.UnitInstanceId==id);
        private static BattleState Battle(params (string id,int team,BoardPosition p)[] extra)
        {
            var stats=new PokemonStats(1000,20,0,1,0,0,7,3,100,0,0);
            var catalog=new PokemonCatalog(new[]{new PokemonDefinition("caster","C",1,stats,"r","spatial"),
                new PokemonDefinition("other","O",1,stats,"r","none")});
            var setup=new[]{("a",1,P(1,3)),("z",2,P(3,3)),("y",2,P(4,3)),("b",1,P(0,0))}.Concat(extra);
            var b=BattleStateFactory.Create("spatial",1,123,8,catalog,setup.Select(u=>new BattleUnitSetup(
                catalog.CreateUnit(u.Item1,u.Item1=="a"?"caster":"other","p"+u.Item2,UnitRank.One,0,UnitPlacement.Unplaced),u.Item2,u.Item3)));
            U(b,"a").SetVitals(1000,100);return b;
        }
        private static SkillEffectDefinition Hit(float value=20)=>new SkillEffectDefinition(SkillEffectType.Damage,EffectTargetSelector.CastTarget,value);
        private static SkillEffectDefinition Area(SkillSpatialDefinition spec,float value=20)=>new SkillEffectDefinition(
            SkillEffectType.Damage,EffectTargetSelector.Spatial,value,spatial:spec);
        private static SkillDefinition Skill(params SkillEffectDefinition[] effects)=>new SkillDefinition("spatial",0,.125f,
            SkillTargetRule.Enemy,TargetLostPolicy.Cancel,effects,range:7);
        private sealed class Policy:SkillCombatBehaviorPolicy
        {
            public Policy(SkillDefinition skill):base(new SkillCatalog(new[]{skill})){}
            public override bool CanAttack(BattleState b,UnitCombatState u)=>false;
            public override bool CanMove(BattleState b,UnitCombatState u)=>false;
        }
        private static BattleSimulation Sim(BattleState b,SkillDefinition skill)=>new BattleSimulation(b,new Policy(skill));
        private static void Through(BattleState b,BattleSimulation sim,int tick)
        {while(b.CurrentTick<=tick&&b.Result==BattleResult.InProgress)sim.Step();}
        private static void TickMovement(BattleState b,int count=30)
        {var sim=new BattleSimulation(b,new StationaryPolicy());for(int i=0;i<count;i++)sim.Step();}
        private sealed class StationaryPolicy:CombatBehaviorPolicy
        {
            public override bool CanAttack(BattleState b,UnitCombatState u)=>false;
            public override bool CanMove(BattleState b,UnitCombatState u)=>false;
        }
        private static bool Reserve(BattleState battle,string id,BoardPosition cell)=>(bool)typeof(MovementReservations)
            .GetMethod("TryReserve",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(battle.Movement.Reservations,new object[]{id,cell});

        [Test] public void EveryRayContainsItsAimHasNoDuplicatesAndMirrorsExactly()
        {
            foreach(var origin in HexBoardBounds.Combat.Cells())foreach(var aim in HexBoardBounds.Combat.Cells()) {
                if(origin.Equals(aim))continue;
                var line=SkillGeometry.Ray(origin,aim,20);Assert.That(line,Does.Contain(aim),origin+" -> "+aim);
                Assert.That(line.Distinct().Count(),Is.EqualTo(line.Count));
                CollectionAssert.AreEqual(line.Select(HexCoordinates.MirrorCombat),SkillGeometry.Ray(
                    HexCoordinates.MirrorCombat(origin),HexCoordinates.MirrorCombat(aim),20));
                var previous=origin;foreach(var p in line){Assert.That(HexCoordinates.Distance(previous,p),Is.EqualTo(1));previous=p;}
            }
        }
        [TestCase(SkillAreaShape.Cone)] [TestCase(SkillAreaShape.Cross)] [TestCase(SkillAreaShape.Radius)] [TestCase(SkillAreaShape.Wave)]
        public void AreasMirrorWithoutChangingVictimCells(SkillAreaShape shape)
        {
            foreach(var origin in HexBoardBounds.Combat.Cells())foreach(var aim in HexBoardBounds.Combat.Cells())
                CollectionAssert.AreEquivalent(SkillGeometry.Cells(shape,origin,aim,3).Select(HexCoordinates.MirrorCombat),
                    SkillGeometry.Cells(shape,HexCoordinates.MirrorCombat(origin),HexCoordinates.MirrorCombat(aim),3));
        }
        [Test] public void CrossHasFourArmsAndRadiusIncludesCenter()
        {
            var center=P(3,3);Assert.That(SkillGeometry.Cells(SkillAreaShape.Cross,center,center,1).Count,Is.EqualTo(5));
            Assert.That(SkillGeometry.Cells(SkillAreaShape.Cross,center,center,2).Count,Is.EqualTo(9));
            Assert.That(SkillGeometry.Cells(SkillAreaShape.Radius,center,center,2).Count,Is.EqualTo(19));
            Assert.That(SkillGeometry.Cells(SkillAreaShape.All,center,center,0).Count,Is.EqualTo(56));
        }
        [Test] public void LineExcludesOriginAndConeExcludesRear()
        {
            var center=P(2,3);var aim=P(5,3);
            Assert.That(SkillGeometry.Ray(center,aim,3).Count,Is.EqualTo(3));
            CollectionAssert.DoesNotContain(SkillGeometry.Ray(center,aim,3),center);
            var cone=SkillGeometry.Cells(SkillAreaShape.Cone,center,aim,3);
            Assert.That(cone,Does.Contain(aim));CollectionAssert.DoesNotContain(cone,P(1,3));CollectionAssert.DoesNotContain(cone,center);
        }
        [Test] public void NearestAlliesExcludeCasterUnlessExplicitlyIncluded()
        {
            var b=Battle(("c",1,P(1,2)));var a=U(b,"a");
            var spec=new SkillSpatialDefinition(SpatialTargetMode.NearestAllies,maxTargets:2);
            CollectionAssert.AreEqual(new[]{"c","b"},SkillSpatialSelector.Select(b,a,a,spec,false,a.Position,a.Position,a.Position).Select(u=>u.UnitInstanceId));
            spec=new SkillSpatialDefinition(SpatialTargetMode.NearestAllies,maxTargets:1,includeSelf:true);
            Assert.That(SkillSpatialSelector.Select(b,a,a,spec,false,a.Position,a.Position,a.Position).Single(),Is.SameAs(a));
        }
        [Test] public void ChainVisitsDistinctEnemiesAndCanExcludeThePrimaryHit()
        {
            var b=Battle(("x",2,P(5,3)));var a=U(b,"a");var z=U(b,"z");
            var spec=new SkillSpatialDefinition(SpatialTargetMode.ChainEnemies,maxTargets:3,chainRange:1);
            CollectionAssert.AreEqual(new[]{"z","y","x"},SkillSpatialSelector.Select(b,a,z,spec,true,a.Position,a.Position,z.Position).Select(u=>u.UnitInstanceId));
            spec=new SkillSpatialDefinition(SpatialTargetMode.ChainEnemies,maxTargets:2,chainRange:1,includePrimary:false);
            CollectionAssert.AreEqual(new[]{"y","x"},SkillSpatialSelector.Select(b,a,z,spec,true,a.Position,a.Position,z.Position).Select(u=>u.UnitInstanceId));
        }
        [Test] public void FarthestEnemyHonorsEligibleTauntAndRange()
        {
            var b=Battle();var a=U(b,"a");Assert.That(SkillSpatialSelector.Farthest(b,a,7),Is.SameAs(U(b,"y")));
            a.SetTauntSource("z");Assert.That(SkillSpatialSelector.Farthest(b,a,7),Is.SameAs(U(b,"z")));
            U(b,"z").SetUntargetable(true);Assert.That(SkillSpatialSelector.Farthest(b,a,1),Is.Null);
        }
        [Test] public void DensestCenterMayBeEmptyAndUsesPreferredTargetToBreakTies()
        {
            var b=Battle();b.TryMoveUnit("z",P(2,3));b.TryMoveUnit("y",P(4,3));
            var center=SkillSpatialSelector.Densest(b,U(b,"a"),1,7,P(3,3));
            Assert.That(center,Is.EqualTo(P(3,3)));Assert.That(b.Board.IsOccupied(center),Is.False);
        }
        [Test] public void SpatialAreaRespectsMaximumTargetsAndNeverDamagesAllies()
        {
            var b=Battle();var e=Area(new SkillSpatialDefinition(extent:7,maxTargets:1));Sim(b,Skill(e)).Step();
            Assert.That(U(b,"z").CurrentHP,Is.EqualTo(980));Assert.That(U(b,"y").CurrentHP,Is.EqualTo(1000));
            Assert.That(U(b,"a").CurrentHP,Is.EqualTo(1000));Assert.That(b.SkillAreasThisTick.Count,Is.EqualTo(1));
        }
        [Test] public void GlobalAreaIgnoresRangeAndHiddenEnemies()
        {
            var b=Battle();U(b,"z").SetUntargetable(true);
            var skill=new SkillDefinition("spatial",0,0,SkillTargetRule.Self,TargetLostPolicy.Cancel,
                new[]{Area(new SkillSpatialDefinition(shape:SkillAreaShape.All))},range:1);
            Sim(b,skill).Step();Assert.That(U(b,"y").CurrentHP,Is.EqualTo(980));Assert.That(U(b,"z").CurrentHP,Is.EqualTo(1000));
        }
        [Test] public void IndependentDensestAreaRetainsItsGeneratedCenter()
        {
            var b=Battle();var e=Area(new SkillSpatialDefinition(center:SkillAreaCenter.DensestEnemies,extent:1));
            var skill=new SkillDefinition("spatial",0,0,SkillTargetRule.Self,TargetLostPolicy.Cancel,Array.Empty<SkillEffectDefinition>(),
                steps:new[]{new SkillStepDefinition(new[]{e},interval:.25f,count:3,lifetime:SkillStepLifetime.Independent)});
            var sim=Sim(b,skill);sim.Step();var area=b.SkillTimeline.Active.Single().Areas.Single();
            sim.QueueInput(_=>{b.TryMoveUnit("z",P(6,6));b.TryMoveUnit("y",P(5,6));});sim.Step();sim.Step();
            Assert.That(b.SkillAreasThisTick.Single().Area.Center,Is.EqualTo(area.Center));
            Assert.That(U(b,"z").CurrentHP,Is.EqualTo(980));Assert.That(U(b,"y").CurrentHP,Is.EqualTo(980));
        }
        [Test] public void BlockedKnockbackStillAppliesDamageAndDoesNotAddCollisionDamage()
        {
            var b=Battle();var e=new SkillEffectDefinition(SkillEffectType.Move,EffectTargetSelector.CastTarget,0,
                movement:new SkillMovementDefinition(SkillMovementKind.Knockback,2,impactEffects:new[]{Hit()}));
            Sim(b,Skill(e)).Step();Assert.That(U(b,"z").CurrentHP,Is.EqualTo(980));Assert.That(U(b,"z").Position,Is.EqualTo(P(3,3)));
            Assert.That(b.Movement.EventsThisTick.Last().Kind,Is.EqualTo(CombatMovementEventKind.Blocked));
        }
        [Test] public void BoardEdgeKnockbackStopsRatherThanMovingBackwards()
        {
            var b=Battle();b.TryMoveUnit("z",P(6,3));b.TryMoveUnit("y",P(5,5));
            Assert.That(b.Movement.TryStart(U(b,"a"),U(b,"z"),new SkillMovementDefinition(SkillMovementKind.Knockback,3)),Is.False);
            Assert.That(U(b,"z").Position,Is.EqualTo(P(6,3)));
        }
        [Test] public void RootBlocksVoluntaryJumpButNotEnemyKnockback()
        {
            var b=Battle();var a=U(b,"a");b.StatusEffects.Apply(new StatusEffectDefinition("root",5,crowdControl:CrowdControlKind.Root),"z","a");
            Assert.That(b.Movement.TryStart(a,U(b,"z"),new SkillMovementDefinition(SkillMovementKind.JumpToTarget)),Is.False);
            Assert.That(b.Movement.TryStart(U(b,"z"),a,new SkillMovementDefinition(SkillMovementKind.Knockback)),Is.True);
            TickMovement(b);Assert.That(a.Position,Is.EqualTo(P(0,3)));
        }
        [Test] public void DashArrivesBeforeDamageAndRecovery()
        {
            var b=Battle();var e=new SkillEffectDefinition(SkillEffectType.Move,EffectTargetSelector.CastTarget,0,
                movement:new SkillMovementDefinition(SkillMovementKind.DashToTarget,speed:1,impactEffects:new[]{Hit(40)}));
            var sim=Sim(b,Skill(e));sim.Step();Assert.That(U(b,"z").CurrentHP,Is.EqualTo(1000));
            Assert.That(b.Movement.IsTravelling(U(b,"a")),Is.True);Through(b,sim,8);
            Assert.That(U(b,"z").CurrentHP,Is.EqualTo(960));Assert.That(HexCoordinates.Distance(U(b,"a").Position,U(b,"z").Position),Is.EqualTo(1));
            Assert.That(U(b,"a").ActionState,Is.EqualTo(CombatActionState.Casting));sim.Step();
            Assert.That(U(b,"a").ActionState,Is.EqualTo(CombatActionState.Idle));
        }
        [Test] public void DashDoesNotDamageTargetThatLeavesTheLandingArea()
        {
            var b=Battle();var e=new SkillEffectDefinition(SkillEffectType.Move,EffectTargetSelector.CastTarget,0,
                movement:new SkillMovementDefinition(SkillMovementKind.DashToTarget,speed:1,impactEffects:new[]{Hit()}));
            var sim=Sim(b,Skill(e));sim.Step();sim.QueueInput(_=>b.TryMoveUnit("z",P(6,7)));Through(b,sim,10);
            Assert.That(U(b,"z").CurrentHP,Is.EqualTo(1000));
        }
        [Test] public void ReservedDestinationPreventsTwoTravellersFromLandingOnSameCell()
        {
            var b=Battle();var a=U(b,"a");var safe=b.Movement.Safest(a).Value;
            Assert.That(Reserve(b,"other",safe),Is.True);
            Assert.That(b.Movement.Safest(a),Is.Not.EqualTo(safe));
            Assert.That(b.Movement.TryStart(a,null,new SkillMovementDefinition(SkillMovementKind.TeleportSafe)),Is.True);
            Assert.That(a.Position,Is.Not.EqualTo(safe));
        }
        [Test] public void ThrowJumpsOverOccupiedIntermediateCellAndDamagesAtLanding()
        {
            var b=Battle();var e=new SkillEffectDefinition(SkillEffectType.Move,EffectTargetSelector.CastTarget,0,
                movement:new SkillMovementDefinition(SkillMovementKind.Throw,2,speed:2,impactEffects:new[]{Hit(50)}));
            var sim=Sim(b,Skill(e));sim.Step();Assert.That(U(b,"z").CurrentHP,Is.EqualTo(1000));
            Through(b,sim,8);Assert.That(U(b,"z").Position,Is.EqualTo(P(5,3)));Assert.That(U(b,"z").CurrentHP,Is.EqualTo(950));
        }
        [Test] public void SafestTileMaximizesNearestEnemyDistanceAndSurroundedCountsAdjacentEnemies()
        {
            var b=Battle();var a=U(b,"a");var safe=b.Movement.Safest(a).Value;
            Func<BoardPosition,int> nearest=p=>b.Units.Where(u=>u.TeamId!=a.TeamId&&u.IsTargetable).Min(u=>HexCoordinates.Distance(p,u.Position));
            Assert.That(nearest(safe),Is.EqualTo(b.Board.Cells.Where(p=>!b.Board.IsOccupied(p)).Max(nearest)));
            Assert.That(b.Movement.IsSurrounded(a),Is.False);b.TryMoveUnit("z",P(2,3));b.TryMoveUnit("y",P(1,2));
            Assert.That(b.Movement.IsSurrounded(a),Is.True);
        }
        [Test] public void BlockedDashDoesNotDealItsImpactDamage()
        {
            var b=Battle();var a=U(b,"a");foreach(var p in HexBoardBounds.Combat.Neighbors(a.Position))
                if(!b.Board.IsOccupied(p))Reserve(b,"block-"+p,p);
            var e=new SkillEffectDefinition(SkillEffectType.Move,EffectTargetSelector.CastTarget,0,
                movement:new SkillMovementDefinition(SkillMovementKind.DashToTarget,impactEffects:new[]{Hit()}));
            Sim(b,Skill(e)).Step();Assert.That(U(b,"z").CurrentHP,Is.EqualTo(1000));Assert.That(b.Movement.Active.Count,Is.Zero);
        }
        [Test] public void ForcedMoveCancelsAnInterruptibleCastAndReleasesOldWalkingReservation()
        {
            var b=Battle();var skill=new SkillDefinition("spatial",2,1,SkillTargetRule.Enemy,TargetLostPolicy.Cancel,new[]{Hit()},range:7);
            var sim=Sim(b,skill);sim.Step();
            sim.QueueInput(_=>b.Movement.TryStart(U(b,"z"),U(b,"a"),new SkillMovementDefinition(SkillMovementKind.Knockback)));
            sim.Step();Assert.That(sim.GetRuntime("a").SkillCast.CancelReason,Is.EqualTo(SkillCancelReason.Interrupted));
            Through(b,sim,20);Assert.That(U(b,"z").CurrentHP,Is.EqualTo(1000));
        }
        [Test] public void RootDuringDashCancelsRemainingTravelAndImpact()
        {
            var b=Battle();var e=new SkillEffectDefinition(SkillEffectType.Move,EffectTargetSelector.CastTarget,0,
                movement:new SkillMovementDefinition(SkillMovementKind.DashToTarget,speed:1,impactEffects:new[]{Hit()}));
            var sim=Sim(b,Skill(e));sim.Step();sim.QueueInput(_=>b.StatusEffects.Apply(new StatusEffectDefinition("root",5,
                crowdControl:CrowdControlKind.Root),"z","a"));Through(b,sim,10);
            Assert.That(b.Movement.Active.Count,Is.Zero);Assert.That(U(b,"z").CurrentHP,Is.EqualTo(1000));
        }
        [Test] public void PathProjectileHitsCurrentOccupantsAndRetainsPathAfterCasterDeath()
        {
            var b=Battle();var e=new SkillEffectDefinition(SkillEffectType.PathProjectileDamage,EffectTargetSelector.Spatial,30,
                projectileSpeed:1,spatial:new SkillSpatialDefinition(shape:SkillAreaShape.Line,extent:4));
            var sim=Sim(b,Skill(e));sim.Step();var path=b.PathProjectiles.Active.Single().Path.ToArray();
            sim.QueueInput(_=>{U(b,"a").SetVitals(0,0);b.TryMoveUnit("z",P(6,6));b.TryMoveUnit("y",path[1]);});
            Through(b,sim,34);Assert.That(U(b,"z").CurrentHP,Is.EqualTo(1000));Assert.That(U(b,"y").CurrentHP,Is.EqualTo(970));
            Assert.That(b.PathProjectiles.Active.Count,Is.Zero);
        }
        [Test] public void ReturningProjectileCanHitEachEnemyOncePerLeg()
        {
            var b=Battle();var e=new SkillEffectDefinition(SkillEffectType.PathProjectileDamage,EffectTargetSelector.Spatial,30,
                projectileSpeed:8,spatial:new SkillSpatialDefinition(shape:SkillAreaShape.Line,extent:4),returns:true);
            var sim=Sim(b,Skill(e));Through(b,sim,10);
            Assert.That(U(b,"z").CurrentHP,Is.EqualTo(940));Assert.That(U(b,"y").CurrentHP,Is.EqualTo(940));
            Assert.That(b.PathProjectiles.Active.Count,Is.Zero);
        }
        [Test] public void BattleEndClearsTravelReservationsAndPathProjectiles()
        {
            var b=Battle();var shot=new SkillEffectDefinition(SkillEffectType.PathProjectileDamage,EffectTargetSelector.Spatial,20,
                spatial:new SkillSpatialDefinition(shape:SkillAreaShape.Line,extent:4));
            var sim=Sim(b,Skill(shot));sim.Step();b.Movement.TryStart(U(b,"b"),U(b,"z"),new SkillMovementDefinition(SkillMovementKind.JumpToTarget,speed:1));
            sim.QueueInput(_=>{U(b,"z").SetVitals(0,0);U(b,"y").SetVitals(0,0);});sim.Step();
            Assert.That(b.PathProjectiles.Active.Count,Is.Zero);Assert.That(b.Movement.Active.Count,Is.Zero);Assert.That(b.Movement.Reservations.Count,Is.Zero);
        }
        [Test] public void InvalidSpatialAndMovementConfigurationsAreRejected()
        {
            Assert.Throws<ArgumentException>(()=>new SkillSpatialDefinition(shape:SkillAreaShape.Cone,center:SkillAreaCenter.CastTarget));
            Assert.Throws<ArgumentOutOfRangeException>(()=>new SkillSpatialDefinition(extent:57));
            Assert.Throws<ArgumentException>(()=>new SkillEffectDefinition(SkillEffectType.Move,EffectTargetSelector.CastTarget,0));
            Assert.Throws<ArgumentException>(()=>new SkillEffectDefinition(SkillEffectType.Heal,EffectTargetSelector.Spatial,1,
                spatial:new SkillSpatialDefinition(SpatialTargetMode.ChainEnemies)));
        }
        [Test] public void ExplicitInterruptDuringDashCancelsTravelAndImpactImmediately()
        {
            var b=Battle();var e=new SkillEffectDefinition(SkillEffectType.Move,EffectTargetSelector.CastTarget,0,
                movement:new SkillMovementDefinition(SkillMovementKind.DashToTarget,speed:1,impactEffects:new[]{Hit()}));
            var sim=Sim(b,Skill(e));sim.Step();sim.QueueSkillInterrupt("a");sim.Step();
            Assert.That(b.Movement.Active.Count,Is.Zero);Assert.That(sim.GetRuntime("a").SkillCast.CancelReason,Is.EqualTo(SkillCancelReason.Interrupted));
            Through(b,sim,12);Assert.That(U(b,"z").CurrentHP,Is.EqualTo(1000));
        }
        [Test] public void ThrowLandingAppliesSplashAroundTheNewTargetPosition()
        {
            var b=Battle(("x",2,P(5,4)));var splash=Area(new SkillSpatialDefinition(center:SkillAreaCenter.CastTarget,extent:1),10);
            var e=new SkillEffectDefinition(SkillEffectType.Move,EffectTargetSelector.CastTarget,0,movement:
                new SkillMovementDefinition(SkillMovementKind.Throw,2,speed:2,impactEffects:new[]{Hit(40),splash}));
            var sim=Sim(b,Skill(e));Through(b,sim,8);
            Assert.That(U(b,"z").Position,Is.EqualTo(P(5,3)));Assert.That(U(b,"z").CurrentHP,Is.EqualTo(950));
            Assert.That(U(b,"x").CurrentHP,Is.EqualTo(990));
        }
        [Test] public void PathProjectileImpactCanPushTheVictimAfterTheCasterDies()
        {
            var b=Battle();b.TryMoveUnit("y",P(6,6));var push=new SkillEffectDefinition(SkillEffectType.Move,
                EffectTargetSelector.CastTarget,0,movement:new SkillMovementDefinition(SkillMovementKind.Knockback));
            var shot=new SkillEffectDefinition(SkillEffectType.PathProjectileDamage,EffectTargetSelector.Spatial,20,projectileSpeed:8,
                spatial:new SkillSpatialDefinition(shape:SkillAreaShape.Line,extent:4),pathImpactEffects:new[]{push});
            var sim=Sim(b,Skill(shot));sim.Step();sim.QueueInput(_=>U(b,"a").SetVitals(0,0));Through(b,sim,8);
            Assert.That(U(b,"z").CurrentHP,Is.EqualTo(980));Assert.That(U(b,"z").Position,Is.EqualTo(P(4,3)));
        }
        [Test] public void ForcedMovementDoesNotCancelAnUninterruptibleCast()
        {
            var b=Battle();var skill=new SkillDefinition("spatial",1,0,SkillTargetRule.Enemy,TargetLostPolicy.Cancel,new[]{Hit()},
                range:7,interruptible:false);var sim=Sim(b,skill);sim.Step();
            sim.QueueInput(_=>b.Movement.TryStart(U(b,"z"),U(b,"a"),new SkillMovementDefinition(SkillMovementKind.Knockback)));
            Through(b,sim,9);Assert.That(U(b,"z").CurrentHP,Is.EqualTo(980));
            Assert.That(sim.GetRuntime("a").SkillCast.CancelReason,Is.EqualTo(SkillCancelReason.None));
        }
        [Test] public void JumpCanReachAcrossABlockedWalkingRing()
        {
            var b=Battle();var a=U(b,"a");foreach(var p in HexBoardBounds.Combat.Neighbors(a.Position))
                if(!b.Board.IsOccupied(p))Reserve(b,"block-"+p,p);
            // Keep one unreserved landing cell adjacent to the target outside the ring.
            Assert.That(b.Movement.TryStart(a,U(b,"z"),new SkillMovementDefinition(SkillMovementKind.JumpToTarget)),Is.True);
            TickMovement(b);Assert.That(HexCoordinates.Distance(a.Position,U(b,"z").Position),Is.EqualTo(1));
        }
        [Test] public void AreaDisplacementProcessesEachVictimAndPreservesBlockedHitDamage()
        {
            var b=Battle();var e=new SkillEffectDefinition(SkillEffectType.Move,EffectTargetSelector.Spatial,0,
                spatial:new SkillSpatialDefinition(extent:5),movement:new SkillMovementDefinition(SkillMovementKind.Knockback,
                    impactEffects:new[]{Hit()}));var sim=Sim(b,Skill(e));Through(b,sim,4);
            Assert.That(U(b,"z").CurrentHP,Is.EqualTo(980));Assert.That(U(b,"y").CurrentHP,Is.EqualTo(980));
            Assert.That(U(b,"z").Position,Is.EqualTo(P(3,3)));Assert.That(U(b,"y").Position,Is.EqualTo(P(5,3)));
        }
        [Test] public void NestedPathThrowImpactUsesTheOriginalOffensiveSnapshot()
        {
            var b=Battle();b.TryMoveUnit("y",P(6,6));
            var attackHit=new SkillEffectDefinition(SkillEffectType.Damage,EffectTargetSelector.CastTarget,1,valueSource:SkillValueSource.Attack);
            var toss=new SkillEffectDefinition(SkillEffectType.Move,EffectTargetSelector.CastTarget,0,
                movement:new SkillMovementDefinition(SkillMovementKind.Throw,2,speed:8,impactEffects:new[]{attackHit}));
            var shot=new SkillEffectDefinition(SkillEffectType.PathProjectileDamage,EffectTargetSelector.Spatial,0,projectileSpeed:8,
                spatial:new SkillSpatialDefinition(shape:SkillAreaShape.Line,extent:4),pathImpactEffects:new[]{toss});
            var sim=Sim(b,Skill(shot));sim.Step();sim.QueueInput(_=>b.StatusEffects.Apply(new StatusEffectDefinition("attack",10,
                StatusTargetTeam.Ally,modifiers:new[]{new StatModifierDefinition(CombatStat.Attack,StatModifierType.Flat,100)}),"a","a"));
            Through(b,sim,8);Assert.That(U(b,"z").CurrentHP,Is.EqualTo(980));
        }
        [Test] public void ClusterJumpUsesNearestEmptyLandingCellAndAppliesAreaOnArrival()
        {
            var b=Battle();var impact=Area(new SkillSpatialDefinition(extent:1),30);
            var move=new SkillEffectDefinition(SkillEffectType.Move,EffectTargetSelector.Self,0,
                movement:new SkillMovementDefinition(SkillMovementKind.JumpToCluster,speed:8,impactEffects:new[]{impact}));
            var skill=new SkillDefinition("spatial",0,0,SkillTargetRule.Self,TargetLostPolicy.Cancel,new[]{move});
            var sim=Sim(b,skill);Through(b,sim,8);
            Assert.That(b.Movement.Active.Count,Is.Zero);Assert.That(U(b,"a").Position,Is.Not.EqualTo(P(1,3)));
            Assert.That(U(b,"z").CurrentHP<1000||U(b,"y").CurrentHP<1000,Is.True);
        }
        [Test] public void PathProjectileHonorsMaximumVictimsPerLeg()
        {
            var b=Battle(("x",2,P(5,3)));var shot=new SkillEffectDefinition(SkillEffectType.PathProjectileDamage,
                EffectTargetSelector.Spatial,20,projectileSpeed:8,returns:true,
                spatial:new SkillSpatialDefinition(shape:SkillAreaShape.Line,extent:4,maxTargets:2));
            var sim=Sim(b,Skill(shot));Through(b,sim,10);
            // Outgoing hits z/y. Returning starts from x and then hits y.
            Assert.That(U(b,"z").CurrentHP,Is.EqualTo(980));Assert.That(U(b,"y").CurrentHP,Is.EqualTo(960));
            Assert.That(U(b,"x").CurrentHP,Is.EqualTo(980));
        }
        [Test] public void DensestCrossMaximizesActualCrossHitsRatherThanRadiusHits()
        {
            var b=Battle(("x",2,P(2,5)),("w",2,P(4,5)));var a=U(b,"a");
            var selected=SkillSpatialSelector.Densest(b,a,2,7,P(3,3),SkillAreaShape.Cross);
            Func<BoardPosition,int> hits=p=>b.Units.Count(u=>u.TeamId!=a.TeamId&&u.IsTargetable&&
                SkillGeometry.Cells(SkillAreaShape.Cross,p,a.Position,2).Contains(u.Position));
            Assert.That(hits(selected),Is.EqualTo(b.Board.Cells.Where(p=>HexCoordinates.Distance(a.Position,p)<=7).Max(hits)));
        }
        [Test] public void PathProjectileDisplacementKeepsLaunchOriginAfterCasterMoves()
        {
            var b=Battle();b.TryMoveUnit("y",P(6,6));var push=new SkillEffectDefinition(SkillEffectType.Move,
                EffectTargetSelector.CastTarget,0,movement:new SkillMovementDefinition(SkillMovementKind.Knockback));
            var shot=new SkillEffectDefinition(SkillEffectType.PathProjectileDamage,EffectTargetSelector.Spatial,20,projectileSpeed:8,
                spatial:new SkillSpatialDefinition(shape:SkillAreaShape.Line,extent:4),pathImpactEffects:new[]{push});
            var sim=Sim(b,Skill(shot));sim.Step();sim.QueueInput(_=>b.TryMoveUnit("a",P(6,7)));Through(b,sim,8);
            Assert.That(U(b,"z").Position,Is.EqualTo(P(4,3)));
        }
    }
}
