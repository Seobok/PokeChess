using System;
using System.Linq;
using NUnit.Framework;
using PokeChess.Core.Battle;
using PokeChess.Core.Board;
using PokeChess.Core.Pokemon;
using PokeChess.Core.Random;

namespace PokeChess.Core.Tests
{
    public sealed class CombatTargetingTests
    {
        private static BoardPosition P(int c,int r)=>new BoardPosition(c,r);
        private static BattleState Battle(ulong seed,params (string id,int team,BoardPosition p)[] entries)
        {
            var catalog=new PokemonCatalog(new[]{
                new PokemonDefinition("test","Test",1,new PokemonStats(100,10,0,1,0,0,1,2,100,0,.1f),"any-role","s")});
            return BattleStateFactory.Create("b",1,seed,30,catalog,entries.Select(e=>
                new BattleUnitSetup(catalog.CreateUnit(e.id,"test","p"+e.team,UnitRank.One,0,UnitPlacement.Unplaced),e.team,e.p)));
        }
        private static UnitCombatState U(BattleState b,string id)=>b.Units.Single(u=>u.UnitInstanceId==id);
        private sealed class StationaryPolicy:CombatBehaviorPolicy
        {
            public override bool CanMove(BattleState b,UnitCombatState u)=>u.TeamId==1;
            public override bool CanAttack(BattleState b,UnitCombatState u)=>u.TeamId==1;
        }
        [Test]
        public void NearestEnemyWinsRegardlessOfCentralityAndIds()
        {
            var b=Battle(1,("self",1,P(0,0)),("near",2,P(1,0)),("central",2,P(3,3)));
            Assert.That(new CombatBehaviorPolicy().SelectTarget(b,U(b,"self")),Is.EqualTo("near"));
            Assert.That(b.RngStreams.Target.DrawCount,Is.Zero);
        }
        [Test]
        public void EqualHexDistanceChoosesMoreCentralEnemyWithoutRng()
        {
            var b=Battle(1,("self",1,P(0,0)),("central",2,P(3,3)),("edge",2,P(4,1)));
            Assert.That(HexCoordinates.Distance(P(0,0),P(3,3)),Is.EqualTo(HexCoordinates.Distance(P(0,0),P(4,1))));
            Assert.That(new CombatBehaviorPolicy().SelectTarget(b,U(b,"self")),Is.EqualTo("central"));
            Assert.That(b.RngStreams.Target.DrawCount,Is.Zero);
        }
        [Test]
        public void CenterScoresAreSymmetricUnderCombatMirror()
        {
            foreach(var p in HexBoardBounds.Combat.Cells())
                Assert.That(CombatTargetSelector.CenterDistanceScore(p),
                    Is.EqualTo(CombatTargetSelector.CenterDistanceScore(HexCoordinates.MirrorCombat(p))));
            Assert.That(CombatTargetSelector.CenterDistanceScore(P(3,3)),Is.EqualTo(12));
            Assert.That(CombatTargetSelector.CenterDistanceScore(P(3,4)),Is.EqualTo(12));
            Assert.Throws<ArgumentOutOfRangeException>(()=>CombatTargetSelector.CenterDistanceScore(P(7,0)));
        }
        [Test]
        public void FinalTiesUseOnlyTargetRngAndIgnoreSetupOrder()
        {
            var entries=new[]{("self",1,P(0,3)),("left",2,P(3,3)),("right",2,P(3,4))};
            var a=Battle(44,entries);var b=Battle(44,entries.Reverse().ToArray());
            var policy=new CombatBehaviorPolicy();
            for(int i=0;i<20;i++)
                Assert.That(policy.SelectTarget(a,U(a,"self")),Is.EqualTo(policy.SelectTarget(b,U(b,"self"))));
            Assert.That(a.RngStreams.Target.DrawCount,Is.EqualTo(20));
            Assert.That(a.RngStreams.Critical.DrawCount,Is.Zero);
            Assert.That(a.RngStreams.Skill.DrawCount,Is.Zero);
            Assert.That(a.RngStreams.Item.DrawCount,Is.Zero);
        }
        [Test]
        public void PurposeStreamsAreIndependentAndSplitMixVectorIsStable()
        {
            var rng=new DeterministicRandom(0);
            Assert.That(rng.NextUInt64(),Is.EqualTo(0xE220A8397B1DCDAFUL));
            Assert.That(rng.NextUInt64(),Is.EqualTo(0x6E789E6AA1B965F4UL));
            var a=new BattleRandomStreams(123);var b=new BattleRandomStreams(123);
            for(int i=0;i<100;i++){a.Critical.NextUInt64();a.Skill.NextUInt64();a.Item.NextUInt64();}
            for(int i=0;i<50;i++)Assert.That(a.Target.NextInt(7),Is.EqualTo(b.Target.NextInt(7)));
            Assert.Throws<ArgumentOutOfRangeException>(()=>rng.NextInt(0));
            long before=rng.DrawCount;Assert.That(rng.NextInt(1),Is.Zero);Assert.That(rng.DrawCount,Is.EqualTo(before));
        }
        [Test]
        public void DifferentSeedsCanSelectBothTiedCandidates()
        {
            var results=Enumerable.Range(0,32).Select(seed=>{
                var b=Battle((ulong)seed,("self",1,P(0,3)),("left",2,P(3,3)),("right",2,P(3,4)));
                return new CombatBehaviorPolicy().SelectTarget(b,U(b,"self"));
            }).Distinct().ToArray();
            Assert.That(results,Is.EquivalentTo(new[]{"left","right"}));
        }
        [Test]
        public void AlliesDeadRemovedAndUntargetableCandidatesAreExcluded()
        {
            var b=Battle(1,("self",1,P(0,0)),("ally",1,P(1,0)),("dead",2,P(0,1)),
                ("hidden",2,P(1,1)),("removed",2,P(2,0)),("valid",2,P(6,7)));
            U(b,"dead").SetVitals(0,0);U(b,"hidden").SetUntargetable(true);b.TryRemoveUnit("removed");
            var policy=new CombatBehaviorPolicy();
            Assert.That(policy.SelectTarget(b,U(b,"self")),Is.EqualTo("valid"));
            b.TryRemoveUnit("valid");Assert.That(policy.SelectTarget(b,U(b,"self")),Is.Null);
            Assert.That(b.RngStreams.Target.DrawCount,Is.Zero);
        }
        [Test]
        public void ValidTauntOverridesDistanceAndInvalidSourceFallsBack()
        {
            var b=Battle(1,("self",1,P(0,0)),("near",2,P(1,0)),("far",2,P(6,7)),("ally",1,P(0,1)));
            var self=U(b,"self");var policy=new CombatBehaviorPolicy();
            self.SetTauntSource("far");Assert.That(policy.SelectTarget(b,self),Is.EqualTo("far"));
            Assert.That(b.RngStreams.Target.DrawCount,Is.Zero);
            U(b,"far").SetUntargetable(true);Assert.That(policy.SelectTarget(b,self),Is.EqualTo("near"));
            self.SetTauntSource("ally");Assert.That(policy.SelectTarget(b,self),Is.EqualTo("near"));
            self.SetTauntSource("missing");Assert.That(policy.SelectTarget(b,self),Is.EqualTo("near"));
        }
        [Test]
        public void MovingTargetIsReevaluatedAfterEachCompletedHex()
        {
            var b=Battle(1,("self",1,P(0,0)),("old",2,P(3,0)),("new",2,P(6,7)));
            var sim=new BattleSimulation(b,new StationaryPolicy());sim.Step();
            Assert.That(sim.GetRuntime("self").MovementTargetId,Is.EqualTo("old"));
            Assert.That(sim.GetRuntime("self").IsAttackTargetLocked,Is.False);
            b.TryMoveUnit("old",P(6,6));b.TryMoveUnit("new",P(2,1));
            for(int i=0;i<15;i++)sim.Step();
            sim.Step();Assert.That(U(b,"self").CurrentTargetId,Is.EqualTo("new"));
        }
        [Test]
        public void ValidAttackLockIsRetainedWithoutRepeatedRng()
        {
            var b=Battle(17,("self",1,P(2,3)),("a",2,P(3,3)),("b",2,P(3,4)));
            var sim=new BattleSimulation(b,new StationaryPolicy());sim.Step();
            string selected=U(b,"self").CurrentTargetId;long draws=b.RngStreams.Target.DrawCount;
            for(int i=0;i<70;i++)sim.Step();
            Assert.That(U(b,"self").CurrentTargetId,Is.EqualTo(selected));
            Assert.That(sim.GetRuntime("self").IsAttackTargetLocked,Is.True);
            Assert.That(b.RngStreams.Target.DrawCount,Is.EqualTo(draws));
        }
        [Test]
        public void NewTauntCancelsOldAttackAndChangesTargetInSameTick()
        {
            var b=Battle(1,("self",1,P(0,0)),("near",2,P(1,0)),("far",2,P(6,7)));
            var sim=new BattleSimulation(b,new StationaryPolicy());sim.Step();
            U(b,"self").SetTauntSource("far");
            var events=sim.Step();
            Assert.That(U(b,"self").CurrentTargetId,Is.EqualTo("far"));
            Assert.That(U(b,"self").ActionState,Is.EqualTo(CombatActionState.Moving));
            Assert.That(events.Any(e=>e.UnitId=="self"&&e.Action==CombatActionState.Attacking&&e.Kind==CombatActionSignalKind.Cancelled),Is.True);
        }
        [Test]
        public void UntargetableLockedTargetIsReplacedImmediately()
        {
            var b=Battle(1,("self",1,P(0,0)),("near",2,P(1,0)),("far",2,P(6,7)));
            var sim=new BattleSimulation(b,new StationaryPolicy());sim.Step();
            U(b,"near").SetUntargetable(true);sim.Step();
            Assert.That(U(b,"self").CurrentTargetId,Is.EqualTo("far"));
            Assert.That(sim.GetRuntime("self").IsAttackTargetLocked,Is.False);
        }
        private sealed class KillAtHitPolicy:CombatBehaviorPolicy
        {
            public int Hits;
            public override bool CanMove(BattleState b,UnitCombatState u)=>false;
            public override bool CanAttack(BattleState b,UnitCombatState u)=>u.TeamId==1;
            public override CombatActionTiming GetAttackTiming(BattleState b,UnitCombatState u)=>new CombatActionTiming(30,0);
            public override void OnAttackTiming(BattleState b,UnitCombatState u,UnitCombatState target)
            {Hits++;target.SetVitals(0,0);}
        }
        [Test]
        public void RetargetingAfterHitDoesNotResetAttackInterval()
        {
            var b=Battle(1,("self",1,P(0,0)),("victim",2,P(1,0)),("survivor",2,P(0,1)));
            U(b,"self").SetTauntSource("victim");var policy=new KillAtHitPolicy();var sim=new BattleSimulation(b,policy);
            sim.Step();
            Assert.That(policy.Hits,Is.EqualTo(1));
            Assert.That(U(b,"self").CurrentTargetId,Is.EqualTo("survivor"));
            for(int i=0;i<29;i++)sim.Step();
            Assert.That(policy.Hits,Is.EqualTo(1));
            sim.Step();Assert.That(policy.Hits,Is.EqualTo(2));
        }

        private sealed class KillAfterSelfPolicy:CombatBehaviorPolicy
        {
            public override string SelectTarget(BattleState b,UnitCombatState u)=>
                u.UnitInstanceId=="killer"?"victim":base.SelectTarget(b,u);
            public override bool CanMove(BattleState b,UnitCombatState u)=>u.UnitInstanceId=="a-self";
            public override bool CanAttack(BattleState b,UnitCombatState u)=>u.UnitInstanceId=="a-self"||u.UnitInstanceId=="killer";
            public override CombatActionTiming GetAttackTiming(BattleState b,UnitCombatState u)=>
                u.UnitInstanceId=="killer"?new CombatActionTiming(1,0):base.GetAttackTiming(b,u);
            public override void OnAttackTiming(BattleState b,UnitCombatState u,UnitCombatState target)
            {if(u.UnitInstanceId=="killer")target.SetVitals(0,0);}
        }
        [Test]
        public void TargetKilledByLaterUnitRetargetsEarlierAttackerBeforeTickEnds()
        {
            var b=Battle(1,("a-self",1,P(0,0)),("killer",1,P(2,0)),("victim",2,P(1,0)),("survivor",2,P(6,7)));
            var sim=new BattleSimulation(b,new KillAfterSelfPolicy());sim.Step();
            Assert.That(U(b,"victim").IsAlive,Is.False);
            Assert.That(U(b,"a-self").CurrentTargetId,Is.EqualTo("survivor"));
            Assert.That(U(b,"a-self").ActionState,Is.EqualTo(CombatActionState.Moving));
        }
    }
}
