using System;
using System.Linq;
using NUnit.Framework;
using PokeChess.Core.Battle;
using PokeChess.Core.Pokemon;
namespace PokeChess.Core.Tests
{
    public sealed class StatusEffectTests
    {
        private static BattleState Battle(bool far=false)
        {
            var stats=new PokemonStats(1000,100,50,1,100,50,3,2,100,0,.125f);
            var c=new PokemonCatalog(new[]{new PokemonDefinition("t","T",1,stats,"r","s")});
            Func<string,int,int,int,BattleUnitSetup> make=(id,team,x,y)=>new BattleUnitSetup(
                c.CreateUnit(id,"t","p"+team,UnitRank.One,0,UnitPlacement.Unplaced),team,new BoardPosition(x,y));
            return BattleStateFactory.Create("b",1,123,30,c,new[]{make("a",1,0,0),make("b",1,0,1),
                make("z",2,far?6:1,far?7:0),make("x",2,6,6)});
        }
        private static UnitCombatState U(BattleState b,string id)=>b.Units.Single(u=>u.UnitInstanceId==id);
        private static StatusEffectDefinition CC(CrowdControlKind cc,float duration=1,float slow=0) =>
            new StatusEffectDefinition(cc.ToString(),duration,crowdControl:cc,slowFraction:slow);
        private static StatusEffectDefinition Buff(string id="buff",float duration=1,float value=10,
            StackPolicy stack=StackPolicy.Refresh,SourceStackRule source=SourceStackRule.PerSource,string group=null,
            CombatStat stat=CombatStat.Attack,StatModifierType type=StatModifierType.Flat,int max=3) =>
            new StatusEffectDefinition(id,duration,StatusTargetTeam.Ally,stackPolicy:stack,sourceRule:source,maxStack:max,
                modifiers:new[]{new StatModifierDefinition(stat,type,value)},stackGroup:group);
        private sealed class Policy:CombatBehaviorPolicy
        {
            public override bool CanAttack(BattleState b,UnitCombatState u)=>u.UnitInstanceId=="a";
            public override bool CanMove(BattleState b,UnitCombatState u)=>u.UnitInstanceId=="a";
        }
        private sealed class SkillPolicy:SkillCombatBehaviorPolicy
        {
            public SkillPolicy(bool interruptible):base(new SkillCatalog(new[]{new SkillDefinition("s",.125f,.125f,
                SkillTargetRule.Enemy,TargetLostPolicy.Cancel,SkillEffectKind.Damage,20,interruptible:interruptible)})) { }
            public override bool CanUseSkill(BattleState b,UnitCombatState u)=>u.UnitInstanceId=="a";
            public override bool CanAttack(BattleState b,UnitCombatState u)=>false;
            public override bool CanMove(BattleState b,UnitCombatState u)=>false;
        }
        [Test] public void CcLongestNeverShortensAndExpiresAtExactTick()
        {
            var b=Battle();var sim=new BattleSimulation(b,new Policy());
            b.StatusEffects.Apply(CC(CrowdControlKind.Stun,2),"z","a");sim.Step();
            b.StatusEffects.Apply(CC(CrowdControlKind.Stun,1),"z","a");
            Assert.That(b.StatusEffects.Active.Single().ExpireTick,Is.EqualTo(60));
            while(b.CurrentTick<60)sim.Step();
            Assert.That(U(b,"a").HasCrowdControl(CrowdControlKind.Stun),Is.False);
            sim.Step();Assert.That(U(b,"a").ActionState,Is.EqualTo(CombatActionState.Attacking));
            Assert.That(b.StatusEffects.EventsThisTick.Any(e=>e.Kind==StatusEventKind.Expired),Is.True);
        }
        [TestCase(StackPolicy.None,1,100)]
        [TestCase(StackPolicy.Refresh,1,40)]
        [TestCase(StackPolicy.Stack,3,40)]
        public void StackPoliciesAndBundleDuration(StackPolicy policy,int count,int unused)
        {
            var b=Battle();var sim=new BattleSimulation(b,new Policy());
            var d=Buff(duration:2,stack:policy);b.StatusEffects.Apply(d,"a","a");sim.Step();
            for(int i=0;i<4;i++)b.StatusEffects.Apply(Buff(duration:1,stack:policy),"a","a");
            Assert.That(b.StatusEffects.Active.Single().StackCount,Is.EqualTo(count));
            Assert.That(U(b,"a").EffectiveStats.Attack,Is.EqualTo(100+10*count));
            Assert.That(b.StatusEffects.Active.Single().ExpireTick,Is.EqualTo(policy==StackPolicy.None?60:31));
        }
        [TestCase(SourceStackRule.PerSource,120,2)]
        [TestCase(SourceStackRule.Shared,110,1)]
        public void SourceScopeControlsIndependentStacks(SourceStackRule rule,int attack,int instances)
        {
            var b=Battle();var d=Buff(source:rule,stack:StackPolicy.None);
            b.StatusEffects.Apply(d,"a","a");b.StatusEffects.Apply(d,"b","a");
            Assert.That(U(b,"a").EffectiveStats.Attack,Is.EqualTo(attack));
            Assert.That(b.StatusEffects.Active.Count,Is.EqualTo(instances));
        }
        [Test] public void StrongestKeepsWeakAndRestoresItAfterExpiry()
        {
            var b=Battle();var sim=new BattleSimulation(b,new Policy());
            var weak=Buff("weak",2,10,StackPolicy.Strongest,SourceStackRule.Shared,"attack");
            var strong=Buff("strong",.5f,30,StackPolicy.Strongest,SourceStackRule.Shared,"attack");
            b.StatusEffects.Apply(weak,"a","a");b.StatusEffects.Apply(strong,"b","a");
            b.StatusEffects.Apply(strong,"b","a");
            Assert.That(b.StatusEffects.Active.Count,Is.EqualTo(2));
            Assert.That(U(b,"a").EffectiveStats.Attack,Is.EqualTo(130));
            while(b.CurrentTick<15)sim.Step();
            Assert.That(U(b,"a").EffectiveStats.Attack,Is.EqualTo(110));
            while(b.CurrentTick<60)sim.Step();
            Assert.That(U(b,"a").EffectiveStats.Attack,Is.EqualTo(100));
        }
        [Test] public void FlatPercentageMultiplicativeAndRemovalRestoreBase()
        {
            var b=Battle();
            b.StatusEffects.Apply(Buff("flat",value:20),"a","a");
            b.StatusEffects.Apply(Buff("pct",value:.5f,type:StatModifierType.Percentage),"a","a");
            b.StatusEffects.Apply(Buff("mult",value:2,type:StatModifierType.Multiplicative),"a","a");
            Assert.That(U(b,"a").EffectiveStats.Attack,Is.EqualTo(360));
            var snapshots=b.StatusEffects.Active;
            foreach(var e in snapshots)b.StatusEffects.Remove(e.InstanceId);
            Assert.That(U(b,"a").EffectiveStats.Attack,Is.EqualTo(100));
            Assert.That(U(b,"a").Stats.Attack,Is.EqualTo(100));Assert.That(snapshots.Count,Is.EqualTo(3));
        }
        [Test] public void RootImmediatelyCancelsMovementAndReleasesReservation()
        {
            var b=Battle(true);var sim=new BattleSimulation(b,new Policy());sim.Step();
            Assert.That(sim.Reservations.Count,Is.EqualTo(1));
            b.StatusEffects.Apply(CC(CrowdControlKind.Root),"z","a");
            Assert.That(U(b,"a").ActionState,Is.EqualTo(CombatActionState.Idle));
            Assert.That(sim.Reservations.Count,Is.Zero);
            for(int i=0;i<10;i++)sim.Step();
            Assert.That(U(b,"a").Position,Is.EqualTo(new BoardPosition(0,0)));
        }
        [TestCase(CrowdControlKind.Stun)] [TestCase(CrowdControlKind.Disarm)]
        public void CcCancelsWindupAndPreservesAttackInterval(CrowdControlKind cc)
        {
            var b=Battle();var sim=new BattleSimulation(b,new Policy());sim.Step();
            b.StatusEffects.Apply(CC(cc),"z","a");
            Assert.That(U(b,"a").ActionState,Is.EqualTo(CombatActionState.Idle));
            Assert.That(sim.GetRuntime("a").NextAttackTick,Is.EqualTo(30));
            for(int i=0;i<5;i++)sim.Step();
            Assert.That(sim.GetRuntime("a").EffectApplied,Is.False);
        }
        [TestCase(true)] [TestCase(false)]
        public void StunInterruptsOnlyInterruptibleCast(bool interruptible)
        {
            var b=Battle();U(b,"a").SetVitals(1000,115);var sim=new BattleSimulation(b,new SkillPolicy(interruptible));sim.Step();
            b.StatusEffects.Apply(CC(CrowdControlKind.Stun),"z","a");
            Assert.That(sim.GetRuntime("a").SkillCast.Finished,Is.EqualTo(interruptible));
            Assert.That(U(b,"a").CurrentEnergy,Is.EqualTo(15));
            if(interruptible)Assert.That(U(b,"a").EnergyLockUntilTick,Is.EqualTo(31));
            else {for(int i=1;i<=4;i++)sim.Step();Assert.That(U(b,"z").CurrentHP,Is.LessThan(1000));}
        }
        [Test] public void SilenceBlocksNewCastButPreservesOverflowAndCurrentCast()
        {
            var b=Battle();U(b,"a").SetVitals(1000,115);var sim=new BattleSimulation(b,new SkillPolicy(true));
            var silence=b.StatusEffects.Apply(CC(CrowdControlKind.Silence),"z","a");sim.Step();
            Assert.That(U(b,"a").CurrentEnergy,Is.EqualTo(115));
            b.Energy.Gain(U(b,"a"),20,EnergyChangeReason.DamageReceived);
            Assert.That(U(b,"a").CurrentEnergy,Is.EqualTo(135));
            b.StatusEffects.Remove(silence.InstanceId);sim.Step();
            Assert.That(U(b,"a").CurrentEnergy,Is.EqualTo(35));
            b.StatusEffects.Apply(CC(CrowdControlKind.Silence),"z","a");
            Assert.That(sim.GetRuntime("a").SkillCast.Finished,Is.False);
        }
        [Test] public void TauntChoosesLongestValidSourceAndFallsBack()
        {
            var b=Battle();b.StatusEffects.Apply(CC(CrowdControlKind.Taunt,1),"z","a");
            b.StatusEffects.Apply(CC(CrowdControlKind.Taunt,2),"x","a");
            Assert.That(CombatTargetSelector.Select(b,U(b,"a")),Is.EqualTo("x"));
            U(b,"x").SetVitals(0,0);Assert.That(CombatTargetSelector.Select(b,U(b,"a")),Is.EqualTo("z"));
        }
        [Test] public void StrongestSlowAffectsNextMoveAndDoesNotChangeAttackSpeed()
        {
            var b=Battle(true);var sim=new BattleSimulation(b,new Policy());sim.Step();
            long arrival=sim.GetRuntime("a").EndTick;
            b.StatusEffects.Apply(CC(CrowdControlKind.Slow,2,.5f),"z","a");
            b.StatusEffects.Apply(CC(CrowdControlKind.Slow,2,.2f),"x","a");
            Assert.That(U(b,"a").EffectiveStats.MoveSpeed,Is.EqualTo(1));
            Assert.That(U(b,"a").EffectiveStats.AttackSpeed,Is.EqualTo(1));
            Assert.That(sim.GetRuntime("a").EndTick,Is.EqualTo(arrival));
            while(b.CurrentTick<=arrival)sim.Step();sim.Step();
            Assert.That(sim.GetRuntime("a").EndTick-sim.GetRuntime("a").StartTick,Is.EqualTo(30));
        }
        [Test] public void EffectiveAttackArmorSpellPowerAndDamageModifiersAreUsed()
        {
            var b=Battle();
            b.StatusEffects.Apply(Buff(value:100),"a","a");
            b.StatusEffects.Apply(new StatusEffectDefinition("armor",1,modifiers:new[]{
                new StatModifierDefinition(CombatStat.Armor,StatModifierType.Flat,-100)}),"a","z");
            new MeleeAttackResolver().TryResolve(b,U(b,"a"),U(b,"z"),out var result);
            Assert.That(result.Request.RawDamage,Is.EqualTo(200));Assert.That(result.AppliedDamage,Is.EqualTo(200));
            b.StatusEffects.Apply(Buff("spell",value:50,stat:CombatStat.SpellPower),"a","a");
            var skill=new SkillDefinition("s",0,0,SkillTargetRule.Self,TargetLostPolicy.Cancel,new[]{
                new SkillEffectDefinition(SkillEffectType.Shield,EffectTargetSelector.Self,20,true)});
            new SkillEffectExecutor().Execute(b,U(b,"a"),U(b,"a"),skill);
            Assert.That(U(b,"a").CurrentShield,Is.EqualTo(40));

            b=Battle();U(b,"a").SetDamageModifiers(new DamageModifiers(amplification:.5f));
            U(b,"z").SetDamageModifiers(new DamageModifiers(reduction:.25f));
            b.StatusEffects.Apply(Buff("amp",value:.25f,stat:CombatStat.DamageAmplification),"a","a");
            b.StatusEffects.Apply(new StatusEffectDefinition("reduce",1,StatusTargetTeam.Ally,modifiers:new[]{
                new StatModifierDefinition(CombatStat.DamageReduction,StatModifierType.Flat,.25f)}),"z","z");
            result=new DamageProcessor().Apply(b,new DamageRequest("a","z",100,DamageType.Physical));
            Assert.That(result.PreMitigationDamage,Is.EqualTo(175));Assert.That(result.PostMitigationDamage,Is.EqualTo(44));
        }
        [Test] public void DamageThenCcSkillAppliesStatusAndLethalTargetSkipsIt()
        {
            foreach(float hp in new[]{1000,1}) {
                var b=Battle();U(b,"z").SetVitals(hp,0);var stun=CC(CrowdControlKind.Stun);
                b.StatusEffects.UseCatalog(new StatusCatalog(new[]{stun}));
                var skill=new SkillDefinition("s",0,0,SkillTargetRule.Enemy,TargetLostPolicy.Cancel,new[]{
                    new SkillEffectDefinition(SkillEffectType.Damage,EffectTargetSelector.CastTarget,20),
                    new SkillEffectDefinition(SkillEffectType.ApplyStatus,EffectTargetSelector.CastTarget,0,statusId:"Stun")});
                new SkillEffectExecutor().Execute(b,U(b,"a"),U(b,"z"),skill);
                Assert.That(U(b,"z").HasCrowdControl(CrowdControlKind.Stun),Is.EqualTo(hp>1));
                if(hp==1)Assert.That(b.SkillEffectEventsThisTick.Last().Outcome,Is.EqualTo(SkillEffectOutcome.Skipped));
            }
        }
        [Test] public void OvertimeOnlyScalesNewCcAndDeathCleansStatuses()
        {
            var b=Battle();var sim=new BattleSimulation(b,new Policy());
            b.StatusEffects.Apply(CC(CrowdControlKind.Root,1),"z","a");
            typeof(BattleState).GetProperty("IsOvertime").SetValue(b,true);
            b.StatusEffects.Apply(CC(CrowdControlKind.Stun,1),"z","a");
            Assert.That(b.StatusEffects.Active.Single(e=>e.Definition.CrowdControl==CrowdControlKind.Root).ExpireTick,Is.EqualTo(30));
            Assert.That(b.StatusEffects.Active.Single(e=>e.Definition.CrowdControl==CrowdControlKind.Stun).ExpireTick,Is.EqualTo(11));
            U(b,"a").SetVitals(0,0);sim.Step();Assert.That(b.StatusEffects.Active.Count,Is.Zero);
        }
        [Test] public void InvalidDefinitionAndOverflowDoNotMutateStatuses()
        {
            Assert.Throws<ArgumentException>(()=>new StatusEffectDefinition("empty",1));
            Assert.Throws<ArgumentOutOfRangeException>(()=>CC(CrowdControlKind.Stun,0));
            var b=Battle();
            Assert.Throws<OverflowException>(()=>b.StatusEffects.Apply(Buff(value:float.MaxValue,type:StatModifierType.Multiplicative),"a","a"));
            Assert.That(b.StatusEffects.Active.Count,Is.Zero);Assert.That(U(b,"a").EffectiveStats.Attack,Is.EqualTo(100));
        }
    }
}
