using System;
using System.Linq;
using NUnit.Framework;
using PokeChess.Core.Battle;
using PokeChess.Core.Board;
using PokeChess.Core.Pokemon;
using PokeChess.Core.Match;
namespace PokeChess.Core.Tests
{
    public sealed class SkillContentRuntimeTests
    {
        private static BoardPosition P(int c,int r)=>new BoardPosition(c,r);
        private static UnitCombatState U(BattleState b,string id)=>b.Units.Single(u=>u.UnitInstanceId==id);
        private static BattleState Battle(int enemies=3,UnitRank rank=UnitRank.One)
        {
            var stats=new PokemonStats(1000,100,0,1,0,0,3,2,100,0,.2f,critChance:0,critMultiplier:2);
            var catalog=new PokemonCatalog(new[]{new PokemonDefinition("unit","Unit",1,stats,"role","content")});
            return BattleStateFactory.Create("content",1,17,20,catalog,
                Enumerable.Range(0,enemies+1).Select(i=>new BattleUnitSetup(catalog.CreateUnit(i==0?"a":"z"+i,"unit",i==0?"p1":"p2",rank,0,UnitPlacement.Unplaced),i==0?1:2,i==0?P(1,3):i==1?P(2,3):P(i,4))));
        }
        private sealed class Policy:SkillCombatBehaviorPolicy
        {public Policy(params SkillDefinition[] skills):base(new SkillCatalog(skills)){}public override bool CanAttack(BattleState b,UnitCombatState u)=>false;public override bool CanMove(BattleState b,UnitCombatState u)=>false;}
        private static void Through(BattleState b,BattleSimulation s,int tick){while(b.CurrentTick<=tick&&b.Result==BattleResult.InProgress)s.Step();}
        private static DamageResult Hit(BattleState b,string target="z1",float damage=10,bool basic=true,SkillNumericDefinition numeric=null)=>new DamageProcessor().Apply(b,new DamageRequest("a",target,damage,DamageType.Physical,
            isBasicAttack:basic,skillPolicy:numeric==null?null:new SkillDamagePolicy(numeric,false,0,1000)));
        private static StatusEffectDefinition Self(StatusBehavior kind,float seconds=4,float value=0)=>new StatusEffectDefinition(kind.ToString(),seconds,StatusTargetTeam.Ally,behavior:kind,effectValue:value);
        private static SkillDefinition Skill(params SkillEffectDefinition[] effects)=>new SkillDefinition("content",0,0,SkillTargetRule.Enemy,TargetLostPolicy.Cancel,effects,range:7);
        [Test] public void CriticalChargeSurvivesBlindAndMissAndIsConsumedByShieldHit()
        {
            var b=Battle();b.StatusEffects.Apply(Self(StatusBehavior.GuaranteedCritical),"a","a");
            var blind=b.StatusEffects.Apply(new StatusEffectDefinition("blind",2,behavior:StatusBehavior.Blind),"z1","a");Hit(b);
            Assert.That(b.StatusEffects.HasBehavior(U(b,"a"),StatusBehavior.GuaranteedCritical),Is.True);b.StatusEffects.Remove(blind.InstanceId);
            U(b,"z1").SetShield(100);var result=Hit(b,damage:10);
            Assert.That(result.Calculation.IsCritical,Is.True);Assert.That(result.ShieldAbsorbed,Is.EqualTo(20));
            Assert.That(b.StatusEffects.HasBehavior(U(b,"a"),StatusBehavior.GuaranteedCritical),Is.False);
        }
        [Test] public void SwordDanceProcsEveryThirdHitAndRetainsCountOnRecast()
        {
            var b=Battle();b.StatusEffects.Apply(Self(StatusBehavior.SwordDance,6,1),"a","a");Hit(b);Hit(b);
            b.StatusEffects.Apply(Self(StatusBehavior.SwordDance,6,1),"a","a");Hit(b);
            Assert.That(U(b,"z1").CurrentHP,Is.EqualTo(870));
        }
        [Test] public void SplashUsesOriginalCriticalDamageOnceAndDoesNotRecurse()
        {
            var b=Battle();b.TryMoveUnit("z2",P(3,3));b.StatusEffects.Apply(Self(StatusBehavior.AttackSplash,3,.5f),"a","a");
            b.StatusEffects.Apply(Self(StatusBehavior.GuaranteedCritical),"a","a");Hit(b,damage:100);
            Assert.That(U(b,"z1").CurrentHP,Is.EqualTo(800));Assert.That(U(b,"z2").CurrentHP,Is.EqualTo(900));
            Assert.That(b.DamageResultsThisTick.Count(r=>r.Request.TargetId=="z1"),Is.EqualTo(1));
        }
        [Test] public void BountyCreditsSkillParticipationOncePerVictimAndAwardsAtFive()
        {
            var b=Battle(5);int gold=0;b.SkillProcs.GoldAwarded+=(u,g)=>gold+=g;
            foreach(var target in b.Units.Where(u=>u.TeamId==2).ToArray()) {
                Hit(b,target.UnitInstanceId,10,false,new SkillNumericDefinition(bounty:true));
                Hit(b,target.UnitInstanceId,10,false,new SkillNumericDefinition(bounty:true));Hit(b,target.UnitInstanceId,2000,false);
            }
            Assert.That(gold,Is.EqualTo(1));Assert.That(U(b,"a").BountyStacks,Is.Zero);
        }
        [Test] public void MagicPenetrationOnlyAffectsTheCurrentSpecialHit()
        {
            var b=Battle();b.StatusEffects.Apply(new StatusEffectDefinition("mr",4,StatusTargetTeam.Ally,modifiers:new[]{new StatModifierDefinition(CombatStat.MagicResistance,StatModifierType.Flat,100)}),"z1","z1");
            var request=new DamageRequest("a","z1",100,DamageType.Magic,skillPolicy:new SkillDamagePolicy(new SkillNumericDefinition(magicPenetration:.3f),false,0,1000));
            Assert.That(new DamageProcessor().Apply(b,request).AppliedDamage,Is.EqualTo(59));
            Assert.That(new DamageProcessor().Apply(b,new DamageRequest("a","z1",100,DamageType.Magic)).AppliedDamage,Is.EqualTo(50));
        }
        [Test] public void RestReflectsPreDefenseDamageEvenWhenTheHitKillsTheSleeper()
        {
            var b=Battle();U(b,"z1").SetVitals(1,0);
            b.StatusEffects.Apply(new StatusEffectDefinition("rest",2,StatusTargetTeam.Ally,behavior:StatusBehavior.RestSleep,recoveryFraction:4,restReduction:.99f,reflectionMultiplier:9.99f),"z1","z1");
            Hit(b,damage:1000);Assert.That(U(b,"z1").CurrentHP,Is.Zero);Assert.That(U(b,"a").CurrentHP,Is.Zero);
            Assert.That(b.DamageResultsThisTick.Count(r=>r.Request.Origin==DamageOrigin.Reflection),Is.EqualTo(1));
        }
        [Test] public void GroundExitRemovesOnlyItsOwnBlindAndAllowsReentry()
        {
            var b=Battle();var standalone=b.StatusEffects.Apply(new StatusEffectDefinition("other-blind",4,behavior:StatusBehavior.Blind),"a","z1");
            var ground=new StatusEffectDefinition("ground",4,behavior:StatusBehavior.Blind);var sim=new BattleSimulation(b,new Policy(),statusCatalog:new StatusCatalog(new[]{ground}));
            var effect=new SkillEffectDefinition(SkillEffectType.GroundStatus,EffectTargetSelector.Spatial,2,spatial:new SkillSpatialDefinition(center:SkillAreaCenter.CastTarget,extent:1),statusId:ground.Id);
            new SkillEffectExecutor().Execute(b,U(b,"a"),U(b,"z1"),Skill(effect));Through(b,sim,1);
            Assert.That(b.StatusEffects.Active.Count(s=>s.TargetId=="z1"),Is.EqualTo(2));b.TryMoveUnit("z1",P(6,6));sim.Step();
            Assert.That(b.StatusEffects.Active.Single(s=>s.TargetId=="z1").InstanceId,Is.EqualTo(standalone.InstanceId));
            b.TryMoveUnit("z1",P(2,3));sim.Step();Assert.That(b.StatusEffects.Active.Count(s=>s.TargetId=="z1"),Is.EqualTo(2));
        }
        [TestCase(false)] [TestCase(true)] public void ChainWaitsAndNeverRevisitsPrimary(bool sequential)
        {
            var b=Battle();var effect=new SkillEffectDefinition(SkillEffectType.ChainDamage,EffectTargetSelector.Spatial,100,
                spatial:new SkillSpatialDefinition(SpatialTargetMode.ChainEnemies,maxTargets:2,includePrimary:false),chainDelay:.15f,chainSequential:sequential);
            new SkillEffectExecutor().Execute(b,U(b,"a"),U(b,"z1"),Skill(effect));var sim=new BattleSimulation(b,new Policy());int first=BattleSimulation.ToTicks(effect.ChainDelay,b.TickRate);Through(b,sim,first-1);
            Assert.That(U(b,"z2").CurrentHP,Is.EqualTo(1000));Through(b,sim,first);
            Assert.That(U(b,"z2").CurrentHP,Is.EqualTo(900));Assert.That(U(b,"z3").CurrentHP,Is.EqualTo(sequential?1000:900));
            Through(b,sim,9);Assert.That(U(b,"z1").CurrentHP,Is.EqualTo(1000));Assert.That(U(b,"z3").CurrentHP,Is.EqualTo(900));
        }
        [Test] public void RankedThenRandomSkillUsesCasterStatsAndConsumesEnergyOnce()
        {
            var b=Battle(rank:UnitRank.Two);U(b,"a").SetVitals(U(b,"a").Stats.MaxHP,100);
            var leaf=new SkillDefinition("leaf",.2f,0,SkillTargetRule.Enemy,TargetLostPolicy.Cancel,new[]{new SkillEffectDefinition(SkillEffectType.Damage,EffectTargetSelector.CastTarget,2,valueSource:SkillValueSource.Attack)},range:7);
            var random=new SkillDefinition("random",0,0,SkillTargetRule.Enemy,TargetLostPolicy.Cancel,Array.Empty<SkillEffectDefinition>(),variantIds:new[]{"leaf"});
            var parent=new SkillDefinition("content",0,0,SkillTargetRule.Enemy,TargetLostPolicy.Cancel,Array.Empty<SkillEffectDefinition>(),variantIds:new[]{"leaf","random","leaf3"},variantMode:SkillVariantMode.Rank);
            var third=new SkillDefinition("leaf3",0,0,SkillTargetRule.Enemy,TargetLostPolicy.Cancel,leaf.Effects);
            var sim=new BattleSimulation(b,new Policy(parent,random,leaf,third));Through(b,sim,BattleSimulation.ToTicks(leaf.CastTime,b.TickRate));
            Assert.That(sim.GetRuntime("a").SkillCast.Definition.Id,Is.EqualTo("leaf"));Assert.That(U(b,"a").CurrentEnergy,Is.Zero);
            Assert.That(U(b,"z1").Stats.MaxHP-U(b,"z1").CurrentHP,Is.EqualTo(2*U(b,"a").Stats.Attack));
        }
        [Test] public void WaveMatchesApprovedSevenAndNineCellExamples()
        {
            Assert.That(SkillGeometry.Cells(SkillAreaShape.Wave,P(3,6),P(3,4),3).Count,Is.EqualTo(7));
            Assert.That(SkillGeometry.Cells(SkillAreaShape.Wave,P(3,6),P(2,4),3).Count,Is.EqualTo(9));
            Assert.That(SkillGeometry.Cells(SkillAreaShape.Wave,P(3,6),P(4,4),3).Count,Is.EqualTo(9));
            var cells=SkillGeometry.Cells(SkillAreaShape.Wave,P(3,6),P(2,4),3);
            CollectionAssert.AreEquivalent(cells.Select(HexCoordinates.MirrorCombat),SkillGeometry.Cells(SkillAreaShape.Wave,HexCoordinates.MirrorCombat(P(3,6)),HexCoordinates.MirrorCombat(P(2,4)),3));
        }
        [Test] public void SwordDanceRetainsItsCounterAfterExpiryAndNewCast()
        {
            var b=Battle();b.StatusEffects.Apply(Self(StatusBehavior.SwordDance,1,1),"a","a");Hit(b);Hit(b);
            var sim=new BattleSimulation(b,new Policy());Through(b,sim,20);b.StatusEffects.Apply(Self(StatusBehavior.SwordDance,1,1),"a","a");Hit(b);
            Assert.That(U(b,"z1").CurrentHP,Is.EqualTo(870));
        }
        [Test] public void BoundRootDealsOnlyScheduledDamageIncludingItsFinalPulse()
        {
            var b=Battle();b.StatusEffects.Apply(new StatusEffectDefinition("binding",2,crowdControl:CrowdControlKind.Root,effectValue:.5f),"a","z1");
            var sim=new BattleSimulation(b,new Policy());Through(b,sim,19);Assert.That(U(b,"z1").CurrentHP,Is.EqualTo(1000));
            Through(b,sim,20);Assert.That(U(b,"z1").CurrentHP,Is.EqualTo(950));Through(b,sim,40);Assert.That(U(b,"z1").CurrentHP,Is.EqualTo(900));
            Through(b,sim,60);Assert.That(U(b,"z1").CurrentHP,Is.EqualTo(900));
        }
        [Test] public void RestCompressesAllFourPulsesDuringOvertime()
        {
            var b=Battle();var sim=new BattleSimulation(b,new Policy());Through(b,sim,600);U(b,"z1").SetVitals(100,0);
            b.StatusEffects.Apply(new StatusEffectDefinition("rest",2,StatusTargetTeam.Ally,behavior:StatusBehavior.RestSleep,recoveryFraction:.4f),"z1","z1");
            int end=(int)b.StatusEffects.Active.Single().ExpireTick;Through(b,sim,end);
            Assert.That(U(b,"z1").CurrentHP,Is.EqualTo(236).Within(.01));Assert.That(b.StatusEffects.IsSleeping(U(b,"z1")),Is.False);
        }
        [Test] public void CarryMovesBothUnitsAndAppliesItsImpactOnlyOnArrival()
        {
            var b=Battle();int impacts=0;var source=U(b,"a");var target=U(b,"z1");
            var before=target.Position;Assert.That(b.Movement.TryStart(source,target,new SkillMovementDefinition(SkillMovementKind.CarryTarget,2),()=>impacts++),Is.True);
            Assert.That(impacts,Is.Zero);var sim=new BattleSimulation(b,new Policy());Through(b,sim,40);
            Assert.That(impacts,Is.EqualTo(1));Assert.That(target.Position,Is.Not.EqualTo(before));Assert.That(HexCoordinates.Distance(source.Position,target.Position),Is.EqualTo(1));
            Assert.That(b.Movement.Active,Is.Empty);
        }
        [Test] public void BountyPersistsBetweenBattlesMergesAndIsDiscardedOnSale()
        {
            var stats=new PokemonStats(1000,100,0,1,0,0,1,2,100,0,0);
            var c=new PokemonCatalog(new[]{new PokemonDefinition("unit","Unit",1,stats,"role","content")});
            var match=MatchStateFactory.CreateWithPool("bounty",new[]{"p1","p2"},c,new[]{"unit"});
            match.TransitionTo(MatchPhase.Starting);match.TransitionTo(MatchPhase.Preparation);var player=match.GetPlayer("p1");
            foreach(int i in Enumerable.Range(0,3)) {
                var owned=player.AddUnit(c.CreateUnit("owned"+i,"unit","p1",UnitRank.One,0,UnitPlacement.OnBench(i)));
                foreach(int round in new[]{1,2}) {
                    var b=BattleStateFactory.Create("owned-"+i+"-"+round,round,1,20,c,new[]{new BattleUnitSetup(owned,1,P(1,3))}
                        .Concat(Enumerable.Range(1,2).Select(n=>new BattleUnitSetup(c.CreateUnit("enemy"+n,"unit","p2",UnitRank.One,0,UnitPlacement.Unplaced),2,P(n+1,3)))));
                    foreach(var enemy in b.Units.Where(u=>u.TeamId==2))new DamageProcessor().Apply(b,new DamageRequest(owned.InstanceId,enemy.UnitInstanceId,1000,DamageType.Physical,
                        skillPolicy:new SkillDamagePolicy(new SkillNumericDefinition(bounty:true),false,0,1000)));
                }
                Assert.That(owned.BountyStacks,Is.EqualTo(4));
            }
            int gold=player.Gold;new RankUpSystem(c).Resolve(match,"p1",player.PlacementRevision);
            var result=player.Units.Single();Assert.That(result.Rank,Is.EqualTo(UnitRank.Two));Assert.That(result.BountyStacks,Is.EqualTo(2));Assert.That(player.Gold,Is.EqualTo(gold+2));
            player.RemoveUnit(result.InstanceId);Assert.That(result.BountyStacks,Is.Zero);
        }
        [Test] public void ProjectileGroupAppliesFinalStatusOnlyAfterEveryProjectileResolves()
        {
            var b=Battle();var freeze=new StatusEffectDefinition("freeze",4,behavior:StatusBehavior.Freeze);
            var state=new SkillEffectDefinition(SkillEffectType.ApplyStatus,EffectTargetSelector.CastTarget,0,statusId:freeze.Id);
            var shots=new[]{new SkillEffectDefinition(SkillEffectType.ProjectileDamage,EffectTargetSelector.CastTarget,100,projectileSpeed:1,projectileGroupSize:3),
                new SkillEffectDefinition(SkillEffectType.ProjectileDamage,EffectTargetSelector.CastTarget,100,projectileSpeed:10,projectileGroupSize:3),
                new SkillEffectDefinition(SkillEffectType.ProjectileDamage,EffectTargetSelector.CastTarget,100,projectileSpeed:10,projectileGroupSize:3,pathImpactEffects:new[]{state})};
            var sim=new BattleSimulation(b,new Policy(),statusCatalog:new StatusCatalog(new[]{freeze}));
            new SkillEffectExecutor().Execute(b,U(b,"a"),U(b,"z1"),Skill(shots));Through(b,sim,2);
            Assert.That(U(b,"z1").CurrentHP,Is.EqualTo(800));Assert.That(b.StatusEffects.HasBehavior(U(b,"z1"),StatusBehavior.Freeze),Is.False);
            Through(b,sim,20);Assert.That(U(b,"z1").CurrentHP,Is.EqualTo(700));Assert.That(b.StatusEffects.HasBehavior(U(b,"z1"),StatusBehavior.Freeze),Is.True);
        }
        [Test] public void ChainSelectionSurvivesPrimaryTargetDeathInTheSameSkillStep()
        {
            var b=Battle();var chain=new SkillEffectDefinition(SkillEffectType.Damage,EffectTargetSelector.Spatial,50,
                spatial:new SkillSpatialDefinition(SpatialTargetMode.ChainEnemies,maxTargets:2,chainRange:56,includePrimary:false));
            new SkillEffectExecutor().Execute(b,U(b,"a"),U(b,"z1"),Skill(new SkillEffectDefinition(SkillEffectType.Damage,EffectTargetSelector.CastTarget,1000),chain));
            Assert.That(U(b,"z1").CurrentHP,Is.Zero);Assert.That(U(b,"z2").CurrentHP,Is.EqualTo(950));Assert.That(U(b,"z3").CurrentHP,Is.EqualTo(950));
        }
    }
}
