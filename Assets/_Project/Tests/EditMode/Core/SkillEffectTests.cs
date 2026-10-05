using System;
using System.Linq;
using NUnit.Framework;
using PokeChess.Core.Battle;
using PokeChess.Core.Pokemon;
namespace PokeChess.Core.Tests
{
    public sealed class SkillEffectTests
    {
        private static BattleState Battle(bool reverse=false)
        {
            var stats=new PokemonStats(100,10,50,1,0,0,3,2,100,0,0);
            var c=new PokemonCatalog(new[]{new PokemonDefinition("t","T",1,stats,"r","s")});
            Func<string,int,int,int,BattleUnitSetup> make=(id,team,x,y)=>new BattleUnitSetup(
                c.CreateUnit(id,"t","p"+team,UnitRank.One,0,UnitPlacement.Unplaced),team,new BoardPosition(x,y));
            var setup=new[]{make("a",1,0,0),make("b",1,1,0),make("z",2,2,0),make("y",2,2,1),make("w",2,6,7)};
            return BattleStateFactory.Create("b",1,123,30,c,reverse?setup.Reverse():setup);
        }
        private static UnitCombatState U(BattleState b,string id)=>b.Units.Single(u=>u.UnitInstanceId==id);
        private static SkillEffectDefinition E(SkillEffectType t,EffectTargetSelector selector,float value,
            bool scale=false,int radius=1) => new SkillEffectDefinition(t,selector,value,scale,radius:radius);
        private static SkillDefinition Skill(params SkillEffectDefinition[] effects) =>
            new SkillDefinition("s",0,.125f,SkillTargetRule.Enemy,TargetLostPolicy.Cancel,effects);
        private static void Execute(BattleState b,params SkillEffectDefinition[] effects) =>
            new SkillEffectExecutor().Execute(b,U(b,"a"),U(b,"z"),Skill(effects));
        [Test] public void CompositionRunsInOrderAndUsesPerEffectScaling()
        {
            var b=Battle();U(b,"a").SetVitals(50,17);
            Execute(b,E(SkillEffectType.Damage,EffectTargetSelector.CastTarget,20,true),
                E(SkillEffectType.Shield,EffectTargetSelector.Self,40),
                E(SkillEffectType.Heal,EffectTargetSelector.Self,20,true));
            Assert.That(U(b,"z").CurrentHP,Is.EqualTo(70));Assert.That(U(b,"a").CurrentShield,Is.EqualTo(40));
            Assert.That(U(b,"a").CurrentHP,Is.EqualTo(80));Assert.That(U(b,"a").CurrentEnergy,Is.EqualTo(17));
            Assert.That(b.SkillEffectEventsThisTick.Select(e=>e.EffectIndex),Is.EqualTo(new[]{0,1,2}));
            Assert.That(b.SkillEffectEventsThisTick.Select(e=>e.CalculatedValue),Is.EqualTo(new float[]{30,40,30}));
        }
        [Test] public void KilledTargetIsSkippedButSelfEffectsContinue()
        {
            var b=Battle();U(b,"z").SetVitals(20,0);U(b,"a").SetVitals(50,0);
            Execute(b,E(SkillEffectType.Damage,EffectTargetSelector.CastTarget,30),
                E(SkillEffectType.Damage,EffectTargetSelector.CastTarget,30),
                E(SkillEffectType.Shield,EffectTargetSelector.Self,40),
                E(SkillEffectType.Heal,EffectTargetSelector.Self,20));
            Assert.That(U(b,"z").IsOnBoard,Is.False);Assert.That(b.DamageResultsThisTick.Count,Is.EqualTo(1));
            Assert.That(b.SkillEffectEventsThisTick[1].SkipReason,Is.EqualTo(SkillEffectSkipReason.NoValidTarget));
            Assert.That(U(b,"a").CurrentShield,Is.EqualTo(40));Assert.That(U(b,"a").CurrentHP,Is.EqualTo(70));
        }
        [Test] public void EnemyAreaUsesRadiusTeamsVisibilityAndOrdinalOrder()
        {
            var b=Battle();Execute(b,E(SkillEffectType.Damage,EffectTargetSelector.EnemiesAroundCastTarget,10));
            Assert.That(b.SkillEffectEventsThisTick.Select(e=>e.TargetId),Is.EqualTo(new[]{"y","z"}));
            Assert.That(U(b,"a").CurrentHP,Is.EqualTo(100));Assert.That(U(b,"w").CurrentHP,Is.EqualTo(100));
            b=Battle();U(b,"y").SetUntargetable(true);
            Execute(b,E(SkillEffectType.Damage,EffectTargetSelector.EnemiesAroundCastTarget,10));
            Assert.That(b.SkillEffectEventsThisTick.Single().TargetId,Is.EqualTo("z"));
        }
        [Test] public void AllyAreaIncludesCasterAndUntargetableAllyButNoEnemy()
        {
            var b=Battle();U(b,"a").SetVitals(50,0);U(b,"b").SetVitals(70,0);U(b,"b").SetUntargetable(true);
            Execute(b,E(SkillEffectType.Heal,EffectTargetSelector.AlliesAroundCaster,20));
            Assert.That(U(b,"a").CurrentHP,Is.EqualTo(70));Assert.That(U(b,"b").CurrentHP,Is.EqualTo(90));
            Assert.That(b.SkillEffectEventsThisTick.Select(e=>e.TargetId),Is.EqualTo(new[]{"a","b"}));
            Assert.That(U(b,"z").CurrentEnergy,Is.Zero);
        }
        [Test] public void HealCapsAndShieldStacksWithFractionalValues()
        {
            var b=Battle();U(b,"a").SetVitals(99,17);U(b,"a").SetShield(10);
            Execute(b,E(SkillEffectType.Heal,EffectTargetSelector.Self,2.5f),
                E(SkillEffectType.Shield,EffectTargetSelector.Self,2.5f),
                E(SkillEffectType.Shield,EffectTargetSelector.Self,2.5f));
            Assert.That(U(b,"a").CurrentHP,Is.EqualTo(100));Assert.That(U(b,"a").CurrentShield,Is.EqualTo(15));
            Assert.That(b.SkillEffectEventsThisTick[0].ActualValue,Is.EqualTo(1));
            Assert.That(U(b,"a").CurrentEnergy,Is.EqualTo(17));
        }
        [Test] public void DeadRemovedAlliesAreNotRevived()
        {
            foreach(bool dead in new[]{true,false}) {
                var b=Battle();if(dead)U(b,"b").SetVitals(0,0);else b.TryRemoveUnit("b");
                Execute(b,E(SkillEffectType.Heal,EffectTargetSelector.AlliesAroundCaster,100));
                Assert.That(b.SkillEffectEventsThisTick.Single().TargetId,Is.EqualTo("a"));
            }
        }
        [Test] public void NoCastTargetSkipsDependentEffectsAndRunsIndependentOnes()
        {
            var b=Battle();
            var skill=new SkillDefinition("s",0,0,SkillTargetRule.Enemy,TargetLostPolicy.ContinueWithoutTarget,new[]{
                E(SkillEffectType.Damage,EffectTargetSelector.CastTarget,20),E(SkillEffectType.Shield,EffectTargetSelector.Self,40)});
            new SkillEffectExecutor().Execute(b,U(b,"a"),null,skill);
            Assert.That(b.SkillEffectEventsThisTick[0].Outcome,Is.EqualTo(SkillEffectOutcome.Skipped));
            Assert.That(U(b,"a").CurrentShield,Is.EqualTo(40));Assert.That(b.DamageResultsThisTick.Count,Is.Zero);
        }
        private sealed class NoBasic:SkillCombatBehaviorPolicy
        {
            public NoBasic(SkillDefinition s):base(new SkillCatalog(new[]{s})) { }
            public override bool CanMove(BattleState b,UnitCombatState u)=>false;
            public override bool CanAttack(BattleState b,UnitCombatState u)=>false;
            public override bool CanUseSkill(BattleState b,UnitCombatState u)=>u.UnitInstanceId=="a";
        }
        [Test] public void ProjectileCapturesScaledPayloadAndHitsLater()
        {
            var b=Battle();U(b,"a").SetVitals(100,100);
            var sim=new BattleSimulation(b,new NoBasic(Skill(E(SkillEffectType.ProjectileDamage,EffectTargetSelector.CastTarget,20,true))));
            sim.Step();Assert.That(b.SkillEffectEventsThisTick.Single().Outcome,Is.EqualTo(SkillEffectOutcome.Spawned));
            Assert.That(b.Projectiles.Active.Single().SkillDamage.Value.RawDamage,Is.EqualTo(30));
            for(int i=1;i<=10;i++)sim.Step();
            Assert.That(U(b,"z").CurrentHP,Is.EqualTo(70));Assert.That(U(b,"a").CurrentEnergy,Is.Zero);
            Assert.That(b.DamageResultsThisTick.Single().Request.CanCrit,Is.False);
        }
        [Test] public void CancelledCastExecutesNoEffects()
        {
            var b=Battle();U(b,"a").SetVitals(100,100);
            var skill=new SkillDefinition("s",.125f,.125f,SkillTargetRule.Enemy,TargetLostPolicy.Cancel,new[]{
                E(SkillEffectType.Damage,EffectTargetSelector.CastTarget,20),E(SkillEffectType.Shield,EffectTargetSelector.Self,40)});
            var sim=new BattleSimulation(b,new NoBasic(skill));sim.Step();sim.QueueSkillInterrupt("a");sim.Step();
            Assert.That(b.SkillEffectEventsThisTick.Count,Is.Zero);Assert.That(U(b,"a").CurrentShield,Is.Zero);
        }
        private sealed class KillCasterHandler:ISkillEffectHandler
        {
            public SkillEffectType Type=>SkillEffectType.Damage;
            public SkillEffectApplication Apply(BattleState b,UnitCombatState caster,UnitCombatState target,SkillEffectDefinition e,float value)
            {caster.SetVitals(0,caster.CurrentEnergy);return new SkillEffectApplication(0);}
        }
        [Test] public void CasterDeathStopsRemainingEffects()
        {
            var b=Battle();new SkillEffectExecutor(overrides:new[]{new KillCasterHandler()}).Execute(b,U(b,"a"),U(b,"z"),
                Skill(E(SkillEffectType.Damage,EffectTargetSelector.CastTarget,20),E(SkillEffectType.Shield,EffectTargetSelector.Self,40)));
            Assert.That(U(b,"a").CurrentShield,Is.Zero);
            Assert.That(b.SkillEffectEventsThisTick.Last().SkipReason,Is.EqualTo(SkillEffectSkipReason.CasterUnavailable));
        }
        [Test] public void EffectListIsDefensivelyCopiedAndInvalidCombinationsRejected()
        {
            var effects=new[]{E(SkillEffectType.Damage,EffectTargetSelector.CastTarget,20)};
            var skill=Skill(effects);effects[0]=E(SkillEffectType.Damage,EffectTargetSelector.CastTarget,99);
            Assert.That(skill.Effects[0].BaseValue,Is.EqualTo(20));
            Assert.Throws<ArgumentException>(()=>Skill(Array.Empty<SkillEffectDefinition>()));
            Assert.Throws<ArgumentException>(()=>E(SkillEffectType.Damage,EffectTargetSelector.Self,1));
            Assert.Throws<ArgumentException>(()=>E(SkillEffectType.Heal,EffectTargetSelector.EnemiesAroundCastTarget,1));
            Assert.Throws<ArgumentOutOfRangeException>(()=>E(SkillEffectType.Shield,EffectTargetSelector.Self,-1));
            Assert.Throws<ArgumentException>(()=>Skill(E(SkillEffectType.Heal,EffectTargetSelector.CastTarget,1)));
        }
        [Test] public void ScalingOverflowIsRejectedBeforeEarlierEffectsApply()
        {
            var b=Battle();
            Assert.Throws<OverflowException>(()=>Execute(b,E(SkillEffectType.Shield,EffectTargetSelector.Self,1),
                E(SkillEffectType.Damage,EffectTargetSelector.CastTarget,float.MaxValue,true)));
            Assert.That(U(b,"a").CurrentShield,Is.Zero);Assert.That(b.SkillEffectEventsThisTick.Count,Is.Zero);
        }
        [Test] public void ReversedRegistrationHasSameOrderedEffectAndDamageTrace()
        {
            string Run(bool reverse) {
                var b=Battle(reverse);Execute(b,E(SkillEffectType.Damage,EffectTargetSelector.EnemiesAroundCastTarget,10),
                    E(SkillEffectType.Shield,EffectTargetSelector.AlliesAroundCaster,10));
                return string.Join(";",b.SkillEffectEventsThisTick.Select(e=>e.EffectIndex+":"+e.TargetId+":"+e.ActualValue))+
                    string.Join(";",b.DamageResultsThisTick.Select(d=>d.Request.TargetId+":"+d.AppliedDamage));
            }
            Assert.That(Run(false),Is.EqualTo(Run(true)));
        }
        [Test] public void ContinuePolicySkipsLostTargetEffectAtActualEffectFrame()
        {
            var b=Battle();U(b,"a").SetVitals(100,100);
            var skill=new SkillDefinition("s",.125f,.125f,SkillTargetRule.Enemy,TargetLostPolicy.ContinueWithoutTarget,new[]{
                E(SkillEffectType.Damage,EffectTargetSelector.CastTarget,20),E(SkillEffectType.Shield,EffectTargetSelector.Self,40)});
            var sim=new BattleSimulation(b,new NoBasic(skill));sim.Step();
            sim.QueueInput(state=>state.TryRemoveUnit("z"));
            for(int i=1;i<=4;i++)sim.Step();
            Assert.That(b.SkillEffectEventsThisTick.Count,Is.EqualTo(2));
            Assert.That(b.SkillEffectEventsThisTick[0].Outcome,Is.EqualTo(SkillEffectOutcome.Skipped));
            Assert.That(b.SkillEffectEventsThisTick[1].Outcome,Is.EqualTo(SkillEffectOutcome.Applied));
            Assert.That(U(b,"a").CurrentShield,Is.EqualTo(40));
            Assert.That(sim.GetRuntime("a").SkillCast.EffectApplied,Is.True);
        }
    }
}
