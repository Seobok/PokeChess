using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PokeChess.Core.Battle;
using PokeChess.Core.Board;
using PokeChess.Core.Pokemon;
using PokeChess.Core.Match;

namespace PokeChess.Core.Tests
{
    public sealed class SkillNumericStatusTests
    {
        private static BoardPosition P(int c,int r)=>new BoardPosition(c,r);
        private static UnitCombatState U(BattleState b,string id)=>b.Units.Single(u=>u.UnitInstanceId==id);
        private static BattleState Battle(bool boss=false,bool sash=false,float victimHp=1000)
        {
            var stats=new PokemonStats(1000,100,0,1,0,0,7,2,100,0,0);
            var c=new PokemonCatalog(new[]{new PokemonDefinition("caster","C",1,stats,"r","numeric"),
                new PokemonDefinition("victim","V",1,new PokemonStats(victimHp,100,0,1,0,0,7,2,100,0,0),"r","none",isBoss:boss),new PokemonDefinition("other","O",1,stats,"r","none")});
            var ids=new[]{"a","z","b","y"};var positions=new[]{P(1,3),P(3,3),P(0,0),P(6,7)};
            return BattleStateFactory.Create("numeric-status",1,123,8,c,ids.Select((id,i)=>new BattleUnitSetup(c.CreateUnit(id,
                id=="a"?"caster":id=="z"?"victim":"other",i<2?"p"+i:i==2?"p0":"p1",UnitRank.One,0,UnitPlacement.Unplaced,
                sash&&id=="z"?new[]{"item:"+ItemCatalog.FocusSash+":test:1"}:null),i==0||i==2?1:2,positions[i])));
        }
        private sealed class Policy:SkillCombatBehaviorPolicy
        {
            public Policy(SkillDefinition skill=null):base(new SkillCatalog(skill==null?Array.Empty<SkillDefinition>():new[]{skill})){}
            public override bool CanAttack(BattleState b,UnitCombatState u)=>false;
            public override bool CanMove(BattleState b,UnitCombatState u)=>false;
        }
        private static BattleSimulation Sim(BattleState b,SkillDefinition skill=null,params StatusEffectDefinition[] statuses)=>
            new BattleSimulation(b,new Policy(skill),statusCatalog:new StatusCatalog(statuses));
        private static void Through(BattleState b,BattleSimulation sim,int tick)
        {while(b.CurrentTick<=tick&&b.Result==BattleResult.InProgress)sim.Step();}
        private static StatusEffectDefinition State(StatusBehavior behavior,float duration=4,float recovery=0)=>new StatusEffectDefinition(
            behavior.ToString(),duration,behavior==StatusBehavior.RestSleep||behavior==StatusBehavior.Untargetable?StatusTargetTeam.Ally:StatusTargetTeam.Enemy,
            behavior:behavior,recoveryFraction:recovery);
        private static void Buff(BattleState b,string target,CombatStat stat,float value,float duration=10)=>b.StatusEffects.Apply(
            new StatusEffectDefinition(stat.ToString(),duration,StatusTargetTeam.Ally,
                modifiers:new[]{new StatModifierDefinition(stat,StatModifierType.Flat,value)}),target,target);
        private static DamageResult Hit(BattleState b,float raw=100,bool basic=false,DamageOrigin origin=DamageOrigin.Direct)=>
            new DamageProcessor().Apply(b,new DamageRequest("a","z",raw,DamageType.Physical,isBasicAttack:basic,origin:origin));
        private static SkillDefinition Skill(SkillEffectDefinition effect,SkillTargetRule target=SkillTargetRule.Enemy,float cast=0,float post=0,bool interruptible=true,
            SkillActivationDefinition activation=null)=>new SkillDefinition("numeric",cast,post,target,TargetLostPolicy.Cancel,new[]{effect},range:7,
                interruptible:interruptible,energyLock:0,activation:activation);
        private static void Execute(BattleState b,SkillEffectDefinition effect)=>new SkillEffectExecutor().Execute(b,U(b,"a"),U(b,"z"),Skill(effect));

        [Test] public void SleepShortensOnlyOnSuccessfulBasicHitsIncludingShieldAbsorption()
        {
            var b=Battle();b.StatusEffects.Apply(State(StatusBehavior.Sleep,2),"a","z");
            U(b,"z").SetShield(200);Hit(b,10);Assert.That(b.StatusEffects.Active.Single().ExpireTick,Is.EqualTo(16));
            Hit(b,10,true);Assert.That(b.StatusEffects.Active.Single().ExpireTick,Is.EqualTo(12));
            Buff(b,"a",CombatStat.Accuracy,-1);var result=Hit(b,10,true);
            Assert.That(result.Missed,Is.True);Assert.That(b.StatusEffects.Active.Single(e=>e.Definition.Behavior==StatusBehavior.Sleep).ExpireTick,Is.EqualTo(12));
        }
        [Test] public void RepeatedBasicHitsCanExhaustSleepImmediately()
        {
            var b=Battle();b.StatusEffects.Apply(State(StatusBehavior.Sleep,1),"a","z");Hit(b,1,true);Hit(b,1,true);
            Assert.That(b.StatusEffects.IsSleeping(U(b,"z")),Is.False);
        }
        [TestCase(StatusBehavior.Sleep)] [TestCase(StatusBehavior.Freeze)]
        public void SleepAndFreezeBlockAllActionsAndInterruptCurrentCast(StatusBehavior state)
        {
            var b=Battle();U(b,"a").SetVitals(1000,100);
            var effect=new SkillEffectDefinition(SkillEffectType.Damage,EffectTargetSelector.CastTarget,100);
            var sim=Sim(b,Skill(effect,cast:1));sim.Step();
            sim.QueueInput(_=>b.StatusEffects.Apply(State(state,2),"z","a"));Through(b,sim,8);
            Assert.That(U(b,"z").CurrentHP,Is.EqualTo(1000));
            Assert.That(sim.GetRuntime("a").SkillCast.CancelReason,Is.EqualTo(SkillCancelReason.Interrupted));
            Assert.That(U(b,"a").Position,Is.EqualTo(P(1,3)));
        }
        [Test] public void UninterruptibleCastWaitsThroughSleepRatherThanExecutingWhileAsleep()
        {
            var b=Battle();U(b,"a").SetVitals(1000,100);var e=new SkillEffectDefinition(SkillEffectType.Damage,EffectTargetSelector.CastTarget,50);
            var sim=Sim(b,Skill(e,cast:1,interruptible:false));sim.Step();
            sim.QueueInput(_=>b.StatusEffects.Apply(State(StatusBehavior.Sleep,1),"z","a"));Through(b,sim,8);
            Assert.That(U(b,"z").CurrentHP,Is.EqualTo(1000));Through(b,sim,16);Assert.That(U(b,"z").CurrentHP,Is.EqualTo(950));
        }
        [Test] public void RestHealsFourQuartersAndReductionExpiresAtTwoSeconds()
        {
            var b=Battle();U(b,"z").SetVitals(100,0);b.StatusEffects.Apply(State(StatusBehavior.RestSleep,2,.4f),"z","z");
            Assert.That(U(b,"z").EffectiveDamageModifiers.Reduction,Is.EqualTo(.3f).Within(.0001));
            var sim=Sim(b);Through(b,sim,3);Assert.That(U(b,"z").CurrentHP,Is.EqualTo(100));
            Through(b,sim,4);Assert.That(U(b,"z").CurrentHP,Is.EqualTo(200));
            Through(b,sim,8);Assert.That(U(b,"z").CurrentHP,Is.EqualTo(300));
            Through(b,sim,12);Assert.That(U(b,"z").CurrentHP,Is.EqualTo(400));
            Through(b,sim,16);Assert.That(U(b,"z").CurrentHP,Is.EqualTo(500));
            Assert.That(b.StatusEffects.IsSleeping(U(b,"z")),Is.False);Assert.That(U(b,"z").EffectiveDamageModifiers.Reduction,Is.Zero);
        }
        [Test] public void BasicHitsDoNotShortenRestAndBurnReducesEachHealingPulseByQuarter()
        {
            var b=Battle();U(b,"z").SetVitals(100,0);b.StatusEffects.Apply(State(StatusBehavior.RestSleep,2,.4f),"z","z");
            b.StatusEffects.Apply(State(StatusBehavior.Burn),"a","z");Hit(b,10,true);
            Assert.That(b.StatusEffects.Active.Single(e=>e.Definition.Behavior==StatusBehavior.RestSleep).ExpireTick,Is.EqualTo(16));
            var sim=Sim(b);Through(b,sim,4);Assert.That(U(b,"z").CurrentHP,Is.EqualTo(168));
        }
        [Test] public void RestCanBeAppliedByItsOwnSkillWithoutCancellingItself()
        {
            var b=Battle();U(b,"a").SetVitals(100,100);var rest=State(StatusBehavior.RestSleep,2,.4f);
            var effect=new SkillEffectDefinition(SkillEffectType.ApplyStatus,EffectTargetSelector.Self,0,statusId:rest.Id,statusTargetTeam:StatusTargetTeam.Ally);
            var sim=Sim(b,Skill(effect,SkillTargetRule.Self,post:.25f),rest);Through(b,sim,16);
            Assert.That(U(b,"a").CurrentHP,Is.EqualTo(500));Assert.That(sim.GetRuntime("a").SkillCast.CancelReason,Is.EqualTo(SkillCancelReason.None));
        }
        [Test] public void FreezeAddsTwentyFivePercentOfTheBreakingHitAndIsRemoved()
        {
            var b=Battle();b.StatusEffects.Apply(State(StatusBehavior.Freeze),"a","z");Hit(b,100);
            Assert.That(U(b,"z").CurrentHP,Is.EqualTo(875));Assert.That(b.StatusEffects.HasBehavior(U(b,"z"),StatusBehavior.Freeze),Is.False);
            Hit(b,100);Assert.That(U(b,"z").CurrentHP,Is.EqualTo(775));
        }
        [Test] public void FreezeBreaksThroughAProtectionHitButNotDebuffDamage()
        {
            var b=Battle();b.StatusEffects.Apply(State(StatusBehavior.Freeze),"a","z");
            Hit(b,10,origin:DamageOrigin.Debuff);Assert.That(b.StatusEffects.HasBehavior(U(b,"z"),StatusBehavior.Freeze),Is.True);
            U(b,"z").SetShield(100);Hit(b,100);Assert.That(U(b,"z").CurrentHP,Is.EqualTo(965));
        }
        [Test] public void ParalysisPreventsMovementButHalvesAttackSpeedAndAllowsSkills()
        {
            var b=Battle();b.StatusEffects.Apply(State(StatusBehavior.Paralysis),"z","a");
            Assert.That(U(b,"a").EffectiveStats.AttackSpeed,Is.EqualTo(.5));Assert.That(b.StatusEffects.BlocksActions(U(b,"a")),Is.False);
            Assert.That(b.Movement.TryStart(U(b,"a"),U(b,"z"),new SkillMovementDefinition(SkillMovementKind.JumpToTarget)),Is.False);
            U(b,"a").SetVitals(1000,100);var sim=Sim(b,Skill(new SkillEffectDefinition(SkillEffectType.Damage,EffectTargetSelector.CastTarget,50)));
            sim.Step();Assert.That(U(b,"z").CurrentHP,Is.EqualTo(950));
        }
        [Test] public void ConfusionMovesOneHexEveryHalfSecondAndReplaysIdentically()
        {
            Func<string> run=()=>{var b=Battle();b.StatusEffects.Apply(State(StatusBehavior.Confusion,2),"z","a");var sim=Sim(b);var log=new List<string>();
                var previous=U(b,"a").Position;for(int i=0;i<=16;i++){sim.Step();foreach(var move in b.Movement.EventsThisTick.Where(e=>e.Kind==CombatMovementEventKind.Arrived)){
                    Assert.That(HexCoordinates.Distance(previous,U(b,"a").Position),Is.EqualTo(1));previous=U(b,"a").Position;log.Add(move.Tick+":"+previous.Column+":"+previous.Row);}}
                Assert.That(log.Count,Is.EqualTo(4));return string.Join("|",log);};Assert.That(run(),Is.EqualTo(run()));
        }
        [Test] public void PoisonStacksAcrossSourcesToThreeAndTicksAtExpiration()
        {
            var b=Battle();var poison=State(StatusBehavior.Poison,2);
            b.StatusEffects.Apply(poison,"a","z");b.StatusEffects.Apply(poison,"b","z");b.StatusEffects.Apply(poison,"a","z");b.StatusEffects.Apply(poison,"b","z");
            Assert.That(b.StatusEffects.Active.Single().StackCount,Is.EqualTo(3));var sim=Sim(b);Through(b,sim,7);Assert.That(U(b,"z").CurrentHP,Is.EqualTo(1000));
            Through(b,sim,8);Assert.That(U(b,"z").CurrentHP,Is.EqualTo(925));Through(b,sim,16);Assert.That(U(b,"z").CurrentHP,Is.EqualTo(850));
            Assert.That(b.StatusEffects.Active.Count,Is.Zero);
        }
        [Test] public void BurnDoesNotStackAndReducesReceivedHealingByQuarter()
        {
            var b=Battle();U(b,"z").SetVitals(500,0);var burn=State(StatusBehavior.Burn,2);
            b.StatusEffects.Apply(burn,"a","z");b.StatusEffects.Apply(burn,"b","z");Assert.That(b.Healing.Apply(U(b,"z"),100),Is.EqualTo(75));
            var sim=Sim(b);Through(b,sim,8);Assert.That(U(b,"z").CurrentHP,Is.EqualTo(550));
        }
        [TestCase(StatusBehavior.Sleep)] [TestCase(StatusBehavior.Freeze)]
        public void BlindBasicAttackDoesNoDamageEnergyOrWakeEffects(StatusBehavior state)
        {
            var b=Battle();b.StatusEffects.Apply(State(StatusBehavior.Blind),"z","a");
            var status=b.StatusEffects.Apply(State(state,2),"a","z");U(b,"z").SetShield(100);
            var hit=Hit(b,100,true);
            Assert.That(hit.PostMitigationDamage,Is.Zero);Assert.That(hit.AppliedDamage,Is.Zero);
            Assert.That(U(b,"z").CurrentHP,Is.EqualTo(1000));Assert.That(U(b,"z").CurrentShield,Is.EqualTo(100));
            Assert.That(U(b,"a").CurrentEnergy,Is.Zero);Assert.That(U(b,"z").CurrentEnergy,Is.Zero);
            Assert.That(b.StatusEffects.Active.Single(e=>e.InstanceId==status.InstanceId).ExpireTick,Is.EqualTo(16));
        }
        [Test] public void BlindLeavesSkillsUnaffectedAndRemovalRestoresBasicDamage()
        {
            var b=Battle();var blind=b.StatusEffects.Apply(State(StatusBehavior.Blind),"z","a");
            Assert.That(Hit(b).AppliedDamage,Is.EqualTo(100));
            b.StatusEffects.Remove(blind.InstanceId);Assert.That(Hit(b,100,true).AppliedDamage,Is.EqualTo(100));
        }
        [TestCase(true)] [TestCase(false)]
        public void BleedAndBurnUseStrongestReductionAndRestoreWeakerOnRemoval(bool burnFirst)
        {
            var b=Battle();U(b,"z").SetVitals(100,0);
            var first=b.StatusEffects.Apply(State(burnFirst?StatusBehavior.Burn:StatusBehavior.Bleed),"a","z");
            var second=b.StatusEffects.Apply(State(burnFirst?StatusBehavior.Bleed:StatusBehavior.Burn),"b","z");
            Assert.That(b.Healing.Apply(U(b,"z"),100),Is.EqualTo(50));
            b.StatusEffects.Remove(burnFirst?second.InstanceId:first.InstanceId);
            Assert.That(b.Healing.Apply(U(b,"z"),100),Is.EqualTo(75));
        }
        [Test] public void BleedOnlyReducesHealingWithoutDamageOrStacking()
        {
            var b=Battle();U(b,"z").SetVitals(100,0);var bleed=State(StatusBehavior.Bleed,2);
            b.StatusEffects.Apply(bleed,"a","z");b.StatusEffects.Apply(bleed,"b","z");
            Assert.That(b.StatusEffects.Active.Single().StackCount,Is.EqualTo(1));
            Assert.That(b.Healing.Apply(U(b,"z"),100),Is.EqualTo(50));
            var sim=Sim(b);Through(b,sim,16);Assert.That(U(b,"z").CurrentHP,Is.EqualTo(150));
            Assert.That(b.Healing.Apply(U(b,"z"),100),Is.EqualTo(100));
        }
        [Test] public void UntargetabilityClearsExistingTargetsButKeepsDebuffDamageAndOtherSources()
        {
            var b=Battle();var sim=Sim(b);sim.Step();Assert.That(U(b,"a").CurrentTargetId,Is.EqualTo("z"));
            b.StatusEffects.Apply(State(StatusBehavior.Poison,2),"a","z");
            var shortHide=b.StatusEffects.Apply(State(StatusBehavior.Untargetable,1),"z","z");
            b.StatusEffects.Apply(State(StatusBehavior.Untargetable,2),"y","z");
            Assert.That(U(b,"a").CurrentTargetId,Is.Null);Assert.That(U(b,"z").IsTargetable,Is.False);
            b.StatusEffects.Remove(shortHide.InstanceId);Assert.That(U(b,"z").IsTargetable,Is.False);
            Through(b,sim,9);Assert.That(U(b,"z").CurrentHP,Is.EqualTo(975));
            Through(b,sim,17);Assert.That(U(b,"z").CurrentHP,Is.EqualTo(950));Assert.That(U(b,"z").IsTargetable,Is.True);
        }
        [Test] public void DebuffsContinueAfterTheirSourceDies()
        {
            var b=Battle();b.StatusEffects.Apply(State(StatusBehavior.Poison,1),"a","z");U(b,"a").SetVitals(0,0);
            var sim=Sim(b);Through(b,sim,8);Assert.That(U(b,"z").CurrentHP,Is.EqualTo(975));
        }
        [Test] public void TimedShieldsAbsorbEarliestExpiryFirstAndExpireOnlyTheirOwnRemainder()
        {
            var b=Battle();b.Shields.Add(U(b,"z"),"z","early",100,1);b.Shields.Add(U(b,"z"),"y","late",200,2);
            Hit(b,150);Assert.That(U(b,"z").CurrentShield,Is.EqualTo(150));Assert.That(b.Shields.Active.Single().Group,Is.EqualTo("late"));
            var sim=Sim(b);Through(b,sim,8);Assert.That(U(b,"z").CurrentShield,Is.EqualTo(150));Through(b,sim,16);Assert.That(U(b,"z").CurrentShield,Is.Zero);
        }
        [Test] public void ShieldRefreshReplacesSameSourceWithoutRemovingOtherSources()
        {
            var b=Battle();b.Shields.Add(U(b,"z"),"z","barrier",100,1);b.Shields.Add(U(b,"z"),"y","barrier",80,2);
            b.Shields.Add(U(b,"z"),"z","barrier",50,3);Assert.That(U(b,"z").CurrentShield,Is.EqualTo(130));Assert.That(b.Shields.Active.Count,Is.EqualTo(2));
        }
        [Test] public void ShieldExpiresBeforeDebuffDamageOnTheSameTick()
        {
            var b=Battle();b.Shields.Add(U(b,"z"),"z","barrier",100,1);b.StatusEffects.Apply(State(StatusBehavior.Poison,1),"a","z");
            var sim=Sim(b);Through(b,sim,8);Assert.That(U(b,"z").CurrentHP,Is.EqualTo(975));
        }
        [Test] public void LifestealUsesOnlyActualHpDamageAndExcludesShieldAndOverkill()
        {
            var b=Battle();U(b,"a").SetVitals(200,0);U(b,"z").SetShield(100);
            var e=new SkillEffectDefinition(SkillEffectType.Damage,EffectTargetSelector.CastTarget,200,numeric:new SkillNumericDefinition(lifesteal:.6f));
            Execute(b,e);Assert.That(U(b,"a").CurrentHP,Is.EqualTo(260));
            U(b,"z").SetVitals(20,0);Execute(b,e);Assert.That(U(b,"a").CurrentHP,Is.EqualTo(272));
        }
        [Test] public void TargetHpDamageRequiresAndHonorsItsCapAtImpact()
        {
            var b=Battle();var e=new SkillEffectDefinition(SkillEffectType.Damage,EffectTargetSelector.CastTarget,.2f,
                damageType:DamageType.True,valueSource:SkillValueSource.TargetMaxHP,numeric:new SkillNumericDefinition(damageCap:75));
            Execute(b,e);Assert.That(U(b,"z").CurrentHP,Is.EqualTo(925));
            Assert.Throws<ArgumentException>(()=>new SkillEffectDefinition(SkillEffectType.Damage,EffectTargetSelector.CastTarget,.2f,valueSource:SkillValueSource.TargetMaxHP));
        }
        [Test] public void TargetHpTimedShieldAndCasterHpCostUseDifferentBases()
        {
            var b=Battle();var shield=new SkillEffectDefinition(SkillEffectType.Shield,EffectTargetSelector.Spatial,.2f,
                spatial:new SkillSpatialDefinition(SpatialTargetMode.NearestAllies,maxTargets:1),valueSource:SkillValueSource.TargetMaxHP,
                numeric:new SkillNumericDefinition(shieldDuration:1));
            var sim=Sim(b,Skill(shield,SkillTargetRule.Self));U(b,"a").SetVitals(1000,100);sim.Step();Assert.That(U(b,"b").CurrentShield,Is.EqualTo(200));
        }
        [Test] public void HpCostLeavesOneHpAndNeverProducesDamageOrDefenderMana()
        {
            var b=Battle();U(b,"a").SetVitals(10,100);U(b,"a").SetShield(100);
            var e=new SkillEffectDefinition(SkillEffectType.HealthCost,EffectTargetSelector.Self,.2f,valueSource:SkillValueSource.CasterMaxHP);
            Sim(b,Skill(e,SkillTargetRule.Self)).Step();Assert.That(U(b,"a").CurrentHP,Is.EqualTo(1));Assert.That(U(b,"a").CurrentShield,Is.EqualTo(100));
            Assert.That(b.DamageResultsThisTick.Count,Is.Zero);
        }
        [TestCase(false,250,0)] [TestCase(false,251,71)] [TestCase(true,250,0)]
        public void ExecuteChecksHpBeforeHitAndIncludesBosses(bool boss,float hp,float after)
        {
            var b=Battle(boss);U(b,"z").SetVitals(hp,0);var e=new SkillEffectDefinition(SkillEffectType.Damage,EffectTargetSelector.CastTarget,180,
                numeric:new SkillNumericDefinition(executeThreshold:.25f,bossBonus:50));Execute(b,e);Assert.That(U(b,"z").CurrentHP,Is.EqualTo(after));
        }
        [Test] public void ExecuteBypassesShieldAndFocusSash()
        {
            var b=Battle(sash:true);U(b,"z").SetVitals(250,0);U(b,"z").SetShield(1000);
            Execute(b,new SkillEffectDefinition(SkillEffectType.Damage,EffectTargetSelector.CastTarget,10,
                numeric:new SkillNumericDefinition(executeThreshold:.25f,bossBonus:50)));Assert.That(U(b,"z").CurrentHP,Is.Zero);
        }
        [Test] public void ConditionalDamageUsesTargetStateBeforeApplyingTheHit()
        {
            var b=Battle();b.StatusEffects.Apply(State(StatusBehavior.Sleep),"a","z");
            var e=new SkillEffectDefinition(SkillEffectType.Damage,EffectTargetSelector.CastTarget,100,
                numeric:new SkillNumericDefinition(SkillValueCondition.TargetSleeping,conditionalValues:new float[]{200,200,200}));
            Execute(b,e);Assert.That(U(b,"z").CurrentHP,Is.EqualTo(800));Assert.That(b.StatusEffects.IsSleeping(U(b,"z")),Is.True);
        }
        [Test] public void ArmorPenetrationAffectsOnlyTheRequestedHit()
        {
            var b=Battle();Buff(b,"z",CombatStat.Armor,100);
            Execute(b,new SkillEffectDefinition(SkillEffectType.Damage,EffectTargetSelector.CastTarget,100,damageType:DamageType.Physical,
                numeric:new SkillNumericDefinition(armorPenetration:.3f)));Assert.That(U(b,"z").CurrentHP,Is.EqualTo(941));
            Hit(b,100);Assert.That(U(b,"z").CurrentHP,Is.EqualTo(891));
        }
        [Test] public void CriticalSkillBonusDoesNotChangeLaterBasicAttackChance()
        {
            var b=Battle();Execute(b,new SkillEffectDefinition(SkillEffectType.Damage,EffectTargetSelector.CastTarget,100,
                numeric:new SkillNumericDefinition(canCrit:true,critBonus:1)));Assert.That(U(b,"z").CurrentHP,Is.EqualTo(850));
            new DamageProcessor().Apply(b,new DamageRequest("a","z",100,DamageType.Physical,true,isBasicAttack:true));
            Assert.That(U(b,"z").CurrentHP,Is.EqualTo(750));Assert.That(U(b,"a").EffectiveStats.CritChance,Is.Zero);
        }
        [Test] public void MissesDoNotGrantManaAndDoNotAffectSkillAccuracy()
        {
            var b=Battle();Buff(b,"a",CombatStat.Accuracy,-1);Assert.That(Hit(b,100,true).Missed,Is.True);
            Assert.That(U(b,"a").CurrentEnergy,Is.Zero);Assert.That(U(b,"z").CurrentEnergy,Is.Zero);
            Assert.That(b.RngStreams.Critical.DrawCount,Is.Zero);Hit(b,100);Assert.That(U(b,"z").CurrentHP,Is.EqualTo(900));
        }
        [Test] public void EnergyGainIsAmplifiedButSkillRefundIsNotAndReceivedGainIsCapped()
        {
            var b=Battle();Buff(b,"a",CombatStat.EnergyGain,1);
            b.Energy.Gain(U(b,"a"),10,EnergyChangeReason.BasicAttackHit);Assert.That(U(b,"a").CurrentEnergy,Is.EqualTo(20));
            b.Energy.Gain(U(b,"a"),40,EnergyChangeReason.DamageReceived);Assert.That(U(b,"a").CurrentEnergy,Is.EqualTo(70));
            b.Energy.Refund(U(b,"a"),30);Assert.That(U(b,"a").CurrentEnergy,Is.EqualTo(100));
        }
        [Test] public void ConditionalTeleportConsumesNoManaUntilSurroundedAndRefundsOnSuccess()
        {
            var b=Battle();U(b,"a").SetVitals(1000,100);
            var refund=new SkillEffectDefinition(SkillEffectType.EnergyRefund,EffectTargetSelector.Self,.3f,valueSource:SkillValueSource.CasterMaxEnergy);
            var move=new SkillEffectDefinition(SkillEffectType.Move,EffectTargetSelector.Self,0,
                movement:new SkillMovementDefinition(SkillMovementKind.TeleportSafe,impactEffects:new[]{refund}));
            var sim=Sim(b,Skill(move,SkillTargetRule.Self,activation:new SkillActivationDefinition()));sim.Step();
            Assert.That(U(b,"a").CurrentEnergy,Is.EqualTo(100));sim.QueueInput(_=>{b.TryMoveUnit("z",P(2,3));b.TryMoveUnit("y",P(1,2));});sim.Step();
            Assert.That(U(b,"a").CurrentEnergy,Is.EqualTo(30).Within(.0001));Assert.That(U(b,"a").Position,Is.Not.EqualTo(P(1,3)));
        }
        [Test] public void ProjectileAppliesStatusAtImpactAndRetainsFrozenOffensiveValues()
        {
            var b=Battle();var poison=State(StatusBehavior.Poison,2);var status=new SkillEffectDefinition(SkillEffectType.ApplyStatus,
                EffectTargetSelector.CastTarget,0,statusId:poison.Id);
            var shot=new SkillEffectDefinition(SkillEffectType.ProjectileDamage,EffectTargetSelector.CastTarget,1,projectileSpeed:2,
                valueSource:SkillValueSource.Attack,pathImpactEffects:new[]{status});
            U(b,"a").SetVitals(1000,100);var sim=Sim(b,Skill(shot),poison);sim.Step();Assert.That(b.StatusEffects.Active.Count,Is.Zero);
            sim.QueueInput(_=>Buff(b,"a",CombatStat.Attack,100));Through(b,sim,8);
            Assert.That(U(b,"z").CurrentHP,Is.EqualTo(900));Assert.That(b.StatusEffects.HasBehavior(U(b,"z"),StatusBehavior.Poison),Is.True);
        }
        [Test] public void ProbabilityZeroNeverAppliesAStatus()
        {
            var b=Battle();var status=State(StatusBehavior.Freeze);b.StatusEffects.UseCatalog(new StatusCatalog(new[]{status}));
            Execute(b,new SkillEffectDefinition(SkillEffectType.ApplyStatus,EffectTargetSelector.CastTarget,0,statusId:status.Id,
                numeric:new SkillNumericDefinition(chance:0)));Assert.That(b.StatusEffects.Active.Count,Is.Zero);
            Assert.That(b.SkillEffectEventsThisTick.Single().SkipReason,Is.EqualTo(SkillEffectSkipReason.ChanceFailed));
        }
        [Test] public void PoisonUsesVictimMaximumHpAndIgnoresResistanceAndReduction()
        {
            var b=Battle(victimHp:2000);Buff(b,"z",CombatStat.Armor,10000);Buff(b,"z",CombatStat.DamageReduction,1);
            b.StatusEffects.Apply(State(StatusBehavior.Poison,1),"a","z");var sim=Sim(b);Through(b,sim,8);
            Assert.That(U(b,"z").CurrentHP,Is.EqualTo(1950));
        }
        [Test] public void PoisonRefreshDoesNotPostponeItsNextTick()
        {
            var b=Battle();var poison=State(StatusBehavior.Poison,2);b.StatusEffects.Apply(poison,"a","z");var sim=Sim(b);Through(b,sim,6);
            b.StatusEffects.Apply(poison,"b","z");Through(b,sim,8);Assert.That(U(b,"z").CurrentHP,Is.EqualTo(950));
        }
        [Test] public void UntargetableUnitsAreExcludedFromAreaDamageButNotTheirDebuffs()
        {
            var b=Battle();b.StatusEffects.Apply(State(StatusBehavior.Untargetable,2),"z","z");
            var area=new SkillEffectDefinition(SkillEffectType.Damage,EffectTargetSelector.Spatial,100,
                spatial:new SkillSpatialDefinition(shape:SkillAreaShape.All));
            new SkillEffectExecutor().Execute(b,U(b,"a"),U(b,"a"),Skill(area,SkillTargetRule.Self));
            Assert.That(U(b,"z").CurrentHP,Is.EqualTo(1000));Assert.That(U(b,"y").CurrentHP,Is.EqualTo(900));
        }
        [Test] public void ExpiringAStatusCannotClearManualUntargetability()
        {
            var b=Battle();U(b,"z").SetUntargetable(true);b.StatusEffects.Apply(State(StatusBehavior.Untargetable,1),"z","z");
            var sim=Sim(b);Through(b,sim,8);Assert.That(U(b,"z").IsUntargetable,Is.True);
            U(b,"z").SetUntargetable(false);Assert.That(U(b,"z").IsTargetable,Is.True);
        }
        [Test] public void LegacyAndTimedShieldsKeepSeparateRemainingAmounts()
        {
            var b=Battle();U(b,"z").SetShield(50);b.Shields.Add(U(b,"z"),"z","barrier",100,1);
            U(b,"z").SetShield(U(b,"z").CurrentShield+20);Hit(b,80);Assert.That(U(b,"z").CurrentShield,Is.EqualTo(90));
            var sim=Sim(b);Through(b,sim,8);Assert.That(U(b,"z").CurrentShield,Is.EqualTo(70));
        }
        [Test] public void FreezeBreakDoesNotGrantAdditionalDefenderManaOrChainAnotherBreak()
        {
            var b=Battle();b.StatusEffects.Apply(State(StatusBehavior.Freeze),"a","z");Hit(b,100);
            Assert.That(U(b,"z").CurrentEnergy,Is.EqualTo(8));Assert.That(b.DamageResultsThisTick.Count,Is.EqualTo(2));
            Assert.That(b.DamageResultsThisTick.Last().Request.Origin,Is.EqualTo(DamageOrigin.FreezeBreak));
        }
        [Test] public void ProjectileConditionalDamageChecksHpAtImpact()
        {
            var b=Battle();U(b,"a").SetVitals(1000,100);
            var shot=new SkillEffectDefinition(SkillEffectType.ProjectileDamage,EffectTargetSelector.CastTarget,100,projectileSpeed:2,
                numeric:new SkillNumericDefinition(SkillValueCondition.TargetLowHP,conditionalValues:new float[]{200,200,200}));
            var sim=Sim(b,Skill(shot));sim.Step();sim.QueueInput(_=>U(b,"z").SetVitals(250,0));Through(b,sim,8);
            Assert.That(U(b,"z").CurrentHP,Is.EqualTo(50));
        }
        [Test] public void ProjectileCritChanceIsFrozenBeforeItsBuffExpires()
        {
            var b=Battle();U(b,"a").SetVitals(1000,100);Buff(b,"a",CombatStat.CritChance,.7f,.5f);
            var shot=new SkillEffectDefinition(SkillEffectType.ProjectileDamage,EffectTargetSelector.CastTarget,100,projectileSpeed:2,
                numeric:new SkillNumericDefinition(canCrit:true,critBonus:.3f));var sim=Sim(b,Skill(shot));Through(b,sim,8);
            Assert.That(U(b,"a").EffectiveStats.CritChance,Is.Zero);Assert.That(U(b,"z").CurrentHP,Is.EqualTo(850));
        }
        [Test] public void ConditionalStatusCanRequireHigherTargetMaximumHp()
        {
            var b=Battle(victimHp:2000);var root=new StatusEffectDefinition("root",1,crowdControl:CrowdControlKind.Root);
            b.StatusEffects.UseCatalog(new StatusCatalog(new[]{root}));
            Execute(b,new SkillEffectDefinition(SkillEffectType.ApplyStatus,EffectTargetSelector.CastTarget,0,statusId:root.Id,
                numeric:new SkillNumericDefinition(SkillValueCondition.TargetHigherMaxHP,conditionalValues:new float[3])));
            Assert.That(U(b,"z").HasCrowdControl(CrowdControlKind.Root),Is.True);
        }
        [Test] public void InvalidNumericAndRestConfigurationsAreRejected()
        {
            Assert.DoesNotThrow(()=>new SkillNumericDefinition(executeThreshold:.25f));
            Assert.Throws<ArgumentException>(()=>new SkillNumericDefinition(canCrit:false,critBonus:.3f));
            Assert.Throws<ArgumentException>(()=>State(StatusBehavior.RestSleep,3,.4f));
            Assert.Throws<ArgumentException>(()=>new SkillEffectDefinition(SkillEffectType.Heal,EffectTargetSelector.Self,.4f,true,
                valueSource:SkillValueSource.CasterMaxHP));
        }
        [Test] public void ExecuteLifestealUsesTheExecutedTargetsActualHp()
        {
            var b=Battle();U(b,"a").SetVitals(100,0);U(b,"z").SetVitals(200,0);U(b,"z").SetShield(1000);
            Execute(b,new SkillEffectDefinition(SkillEffectType.Damage,EffectTargetSelector.CastTarget,10,
                numeric:new SkillNumericDefinition(lifesteal:.6f,executeThreshold:.25f,bossBonus:50)));
            Assert.That(U(b,"a").CurrentHP,Is.EqualTo(220));
        }
        private static StatusEffectDefinition Focus(float fraction=.3f)=>new StatusEffectDefinition("focus",2,
            StatusTargetTeam.Ally,behavior:StatusBehavior.Focus,energyRefundFraction:fraction);
        [TestCase(false)] [TestCase(true)]
        public void FocusTeleportsOnDirectHpDamageAndRefundsOnlyOnce(bool basic)
        {
            var b=Battle();var target=U(b,"z");var start=target.Position;
            var expected=b.Movement.Safest(target).Value;
            b.StatusEffects.Apply(Focus(),"z","z");Hit(b,100,basic);
            Assert.That(target.Position,Is.EqualTo(expected));Assert.That(target.Position,Is.Not.EqualTo(start));
            Assert.That(b.StatusEffects.HasBehavior(target,StatusBehavior.Focus),Is.False);
            Assert.That(b.Energy.EventsThisTick.Count(e=>e.Reason==EnergyChangeReason.SkillRefund),Is.EqualTo(1));
            Assert.That(b.Energy.EventsThisTick.Single(e=>e.Reason==EnergyChangeReason.SkillRefund).Requested,Is.EqualTo(30).Within(.001));
            Hit(b,100,basic);Assert.That(b.Energy.EventsThisTick.Count(e=>e.Reason==EnergyChangeReason.SkillRefund),Is.EqualTo(1));
        }
        [TestCase(DamageOrigin.Debuff)] [TestCase(DamageOrigin.Periodic)]
        [TestCase(DamageOrigin.Reflection)] [TestCase(DamageOrigin.FreezeBreak)]
        public void FocusIgnoresIndirectDamage(DamageOrigin origin)
        {
            var b=Battle();var start=U(b,"z").Position;b.StatusEffects.Apply(Focus(),"z","z");
            Hit(b,100,origin:origin);
            Assert.That(U(b,"z").Position,Is.EqualTo(start));
            Assert.That(b.StatusEffects.HasBehavior(U(b,"z"),StatusBehavior.Focus),Is.True);
            Assert.That(b.Energy.EventsThisTick.Any(e=>e.Reason==EnergyChangeReason.SkillRefund),Is.False);
        }
        [Test] public void FocusIgnoresPoisonAndBurnPulsesAndFullyShieldedHit()
        {
            var b=Battle();var target=U(b,"z");var start=target.Position;
            b.StatusEffects.Apply(Focus(),"z","z");b.StatusEffects.Apply(State(StatusBehavior.Poison),"a","z");
            b.StatusEffects.Apply(State(StatusBehavior.Burn),"a","z");var sim=Sim(b);Through(b,sim,8);
            Assert.That(target.CurrentHP,Is.EqualTo(950));Assert.That(target.Position,Is.EqualTo(start));
            target.SetShield(100);Hit(b,100,true);Assert.That(target.CurrentShield,Is.Zero);
            Assert.That(target.Position,Is.EqualTo(start));Assert.That(b.StatusEffects.HasBehavior(target,StatusBehavior.Focus),Is.True);
            target.SetShield(50);Hit(b,100,true);Assert.That(target.Position,Is.Not.EqualTo(start));
            Assert.That(b.StatusEffects.HasBehavior(target,StatusBehavior.Focus),Is.False);
        }
        [Test] public void FocusRetriesAfterMovementBlockAndDoesNotRefundFailure()
        {
            var b=Battle();var target=U(b,"z");var start=target.Position;b.StatusEffects.Apply(Focus(),"z","z");
            var root=b.StatusEffects.Apply(new StatusEffectDefinition("root",1,crowdControl:CrowdControlKind.Root),"a","z");
            Hit(b);Assert.That(target.Position,Is.EqualTo(start));Assert.That(b.StatusEffects.HasBehavior(target,StatusBehavior.Focus),Is.True);
            Assert.That(b.Energy.EventsThisTick.Any(e=>e.Reason==EnergyChangeReason.SkillRefund),Is.False);
            b.StatusEffects.Remove(root.InstanceId);Hit(b);
            Assert.That(target.Position,Is.Not.EqualTo(start));Assert.That(b.StatusEffects.HasBehavior(target,StatusBehavior.Focus),Is.False);
        }
        [Test] public void FocusExpiresWithoutRefundAndCannotPreventLethalDamage()
        {
            var b=Battle();var start=U(b,"z").Position;b.StatusEffects.Apply(Focus(),"z","z");var sim=Sim(b);Through(b,sim,16);
            Assert.That(b.StatusEffects.HasBehavior(U(b,"z"),StatusBehavior.Focus),Is.False);
            Assert.That(U(b,"z").CurrentEnergy,Is.Zero);Assert.That(U(b,"z").Position,Is.EqualTo(start));
            b.StatusEffects.Apply(Focus(),"z","z");Hit(b,1000);
            Assert.That(U(b,"z").IsAlive,Is.False);Assert.That(b.Energy.EventsThisTick.Any(e=>e.Reason==EnergyChangeReason.SkillRefund),Is.False);
        }
        [Test] public void FocusBlocksItsOwnRecastWithoutBlockingMovementOrAttack()
        {
            var b=Battle();U(b,"a").SetVitals(1000,100);var focus=Focus();
            var effect=new SkillEffectDefinition(SkillEffectType.ApplyStatus,EffectTargetSelector.Self,0,
                statusId:focus.Id,statusTargetTeam:StatusTargetTeam.Ally);
            var sim=Sim(b,Skill(effect,SkillTargetRule.Self),focus);sim.Step();
            var id=sim.GetRuntime("a").SkillCast.CastId;U(b,"a").SetVitals(1000,100);Through(b,sim,8);
            Assert.That(sim.GetRuntime("a").SkillCast.CastId,Is.EqualTo(id));Assert.That(U(b,"a").CurrentEnergy,Is.EqualTo(100));
            Assert.That(b.StatusEffects.BlocksActions(U(b,"a")),Is.False);Assert.That(b.StatusEffects.BlocksMovement(U(b,"a")),Is.False);
            Through(b,sim,16);Assert.That(sim.GetRuntime("a").SkillCast.CastId,Is.GreaterThan(id));
        }
        [Test] public void FocusCannotBeAppliedToAnotherAllyOrWithInvalidRefund()
        {
            var b=Battle();Assert.Throws<ArgumentException>(()=>b.StatusEffects.Apply(Focus(),"a","b"));
            Assert.Throws<ArgumentException>(()=>new StatusEffectDefinition("focus",2,StatusTargetTeam.Ally,behavior:StatusBehavior.Focus));
            Assert.Throws<ArgumentException>(()=>new StatusEffectDefinition("focus",3,StatusTargetTeam.Ally,behavior:StatusBehavior.Focus,energyRefundFraction:.3f));
        }
        [Test] public void FocusKeepsItsRemainingWindowWhenNoDestinationExists()
        {
            var b=Battle();var target=U(b,"z");var start=target.Position;
            var reserve=typeof(MovementReservations).GetMethod("TryReserve",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
            var release=typeof(MovementReservations).GetMethod("Release",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
            var empty=b.Board.Cells.Where(p=>!b.Board.IsOccupied(p)).ToArray();
            for(int i=0;i<empty.Length;i++)Assert.That(reserve.Invoke(b.Movement.Reservations,new object[]{"block"+i,empty[i]}),Is.True);
            var focus=b.StatusEffects.Apply(Focus(),"z","z");Hit(b);
            Assert.That(target.Position,Is.EqualTo(start));Assert.That(b.StatusEffects.Active.Single().ExpireTick,Is.EqualTo(focus.ExpireTick));
            Assert.That(b.Energy.EventsThisTick.Any(e=>e.Reason==EnergyChangeReason.SkillRefund),Is.False);
            release.Invoke(b.Movement.Reservations,new object[]{"block0"});Hit(b);
            Assert.That(target.Position,Is.EqualTo(empty[0]));Assert.That(b.StatusEffects.HasBehavior(target,StatusBehavior.Focus),Is.False);
        }
        [Test] public void FocusIgnoresBlindMissesAndOwnHpCosts()
        {
            var b=Battle();b.StatusEffects.Apply(Focus(),"z","z");var start=U(b,"z").Position;
            var blind=b.StatusEffects.Apply(State(StatusBehavior.Blind),"z","a");Hit(b,100,true);
            b.StatusEffects.Remove(blind.InstanceId);Buff(b,"a",CombatStat.Accuracy,-1);Hit(b,100,true);
            Assert.That(U(b,"z").Position,Is.EqualTo(start));Assert.That(b.StatusEffects.HasBehavior(U(b,"z"),StatusBehavior.Focus),Is.True);
            b.StatusEffects.Apply(Focus(),"a","a");start=U(b,"a").Position;
            Execute(b,new SkillEffectDefinition(SkillEffectType.HealthCost,EffectTargetSelector.Self,.2f,valueSource:SkillValueSource.CasterMaxHP));
            Assert.That(U(b,"a").CurrentHP,Is.EqualTo(800));Assert.That(U(b,"a").Position,Is.EqualTo(start));
            Assert.That(b.StatusEffects.HasBehavior(U(b,"a"),StatusBehavior.Focus),Is.True);
        }
    }
}
