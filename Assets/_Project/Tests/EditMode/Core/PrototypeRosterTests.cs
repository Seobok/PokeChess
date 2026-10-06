using System;
using System.Linq;
using NUnit.Framework;
using PokeChess.Core.Battle;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Tests
{
    public sealed class PrototypeRosterTests
    {
        private static UnitCombatState U(BattleState b,string id)=>b.Units.Single(u=>u.UnitInstanceId==id);
        private static BattleState Battle(string pokemon,UnitRank rank=UnitRank.One)
        {
            var catalog=PrototypeRoster.CreateCatalog();
            BattleUnitSetup Setup(string id,string species,int team,int x,int y,UnitRank r=UnitRank.One)=>
                new BattleUnitSetup(catalog.CreateUnit(id,species,"p"+team,r,0,UnitPlacement.Unplaced),team,new BoardPosition(x,y));
            return BattleStateFactory.Create("roster",1,123,30,catalog,new[]{
                Setup("caster",pokemon,1,0,0,rank),Setup("ally-a","slowpoke",1,3,0),
                Setup("ally-b","weedle",1,6,0),Setup("enemy","slowpoke",2,1,0),Setup("reserve","slowpoke",2,6,7)});
        }
        private sealed class Policy:SkillCombatBehaviorPolicy
        {
            public Policy():base(PrototypeRoster.CreateSkills()){}
            public override bool CanAttack(BattleState b,UnitCombatState u)=>false;
            public override bool CanMove(BattleState b,UnitCombatState u)=>false;
        }
        private static BattleSimulation Simulation(BattleState b)=>new BattleSimulation(b,new Policy(),statusCatalog:PrototypeRoster.CreateStatuses());
        private static void Ready(BattleState b)=>U(b,"caster").SetVitals(U(b,"caster").CurrentHP,U(b,"caster").Stats.MaxEnergy);
        private static void Step(BattleSimulation s,int count){for(int i=0;i<count;i++)s.Step();}

        [TestCase("slowpoke",1,64f)][TestCase("slowpoke",2,68f)][TestCase("slowpoke",3,72f)]
        [TestCase("mankey",1,.91f)][TestCase("mankey",2,1.105f)][TestCase("mankey",3,1.3f)]
        public void BuffUsesExplicitRankValueAndExpires(string pokemon,int rank,float expected)
        {
            var b=Battle(pokemon,(UnitRank)rank);Ready(b);var s=Simulation(b);Step(s,20);
            var caster=U(b,"caster");
            Assert.That(pokemon=="slowpoke"?caster.EffectiveStats.Armor:caster.EffectiveStats.AttackSpeed,Is.EqualTo(expected).Within(.0001));
            var buff=b.StatusEffects.Active.Single();
            long expiry=buff.ExpireTick;
            Step(s,(int)(expiry-b.CurrentTick)+1);
            Assert.That(b.StatusEffects.Active,Is.Empty);
            Assert.That(pokemon=="slowpoke"?caster.EffectiveStats.Armor:caster.EffectiveStats.AttackSpeed,
                Is.EqualTo(pokemon=="slowpoke"?40:.65f).Within(.0001));
        }
        [TestCase("slowpoke")][TestCase("mankey")]
        public void BuffRefreshDoesNotStack(string pokemon)
        {
            var b=Battle(pokemon);Ready(b);var s=Simulation(b);Step(s,40);
            long expiry=b.StatusEffects.Active.Single().ExpireTick;Ready(b);Step(s,35);
            Assert.That(b.StatusEffects.Active.Count,Is.EqualTo(1));
            Assert.That(b.StatusEffects.Active.Single().StackCount,Is.EqualTo(1));
            Assert.That(b.StatusEffects.Active.Single().ExpireTick,Is.GreaterThan(expiry));
        }
        [TestCase(1,130f)][TestCase(2,254.15f)][TestCase(3,526.5f)]
        public void MissileUsesRankedAttackAndExplicitCoefficient(int rank,float raw)
        {
            var b=Battle("weedle",(UnitRank)rank);Ready(b);var s=Simulation(b);Step(s,12);
            var shot=b.Projectiles.Active.Single();
            Assert.That(shot.SkillDamage.Value.RawDamage,Is.EqualTo(raw).Within(.001));
            Assert.That(shot.SkillDamage.Value.CanCrit,Is.False);
            float hp=U(b,"enemy").CurrentHP;Step(s,5);
            Assert.That(U(b,"enemy").CurrentHP,Is.EqualTo(hp-DamageCalculator.Calculate(raw,DamageType.Physical,40,20)).Within(.001));
        }
        [TestCase(1,175f)][TestCase(2,200f)][TestCase(3,225f)]
        public void HealUsesExplicitRankValueAndGlobalLowestRatio(int rank,float heal)
        {
            var b=Battle("chansey",(UnitRank)rank);U(b,"ally-b").SetVitals(100,0);U(b,"ally-a").SetVitals(200,0);
            Ready(b);var s=Simulation(b);Step(s,14);
            Assert.That(U(b,"ally-b").CurrentHP,Is.EqualTo(100+heal));
            Assert.That(U(b,"ally-a").CurrentHP,Is.EqualTo(200));
        }
        [Test] public void HealTieUsesMissingHPThenOrdinalID()
        {
            var b=Battle("chansey");U(b,"caster").SetVitals(300,90);U(b,"ally-b").SetVitals(300,0);U(b,"ally-a").SetVitals(275,0);
            var s=Simulation(b);s.Step();Assert.That(b.SkillEventsThisTick.Single().TargetId,Is.EqualTo("ally-b"));
        }
        [Test] public void HealEqualMissingHPUsesOrdinalIDAndCanHealSelf()
        {
            var b=Battle("chansey");U(b,"caster").SetVitals(300,90);U(b,"ally-b").SetVitals(300,0);
            var s=Simulation(b);s.Step();Assert.That(b.SkillEventsThisTick.Single().TargetId,Is.EqualTo("ally-b"));
            var self=Battle("chansey");U(self,"caster").SetVitals(100,90);var ss=Simulation(self);Step(ss,14);
            Assert.That(U(self,"caster").CurrentHP,Is.EqualTo(275));
        }
        [Test] public void HealRetargetsDeadTargetOnlyAtEffect()
        {
            var b=Battle("chansey");U(b,"ally-a").SetVitals(50,0);U(b,"ally-b").SetVitals(100,0);Ready(b);var s=Simulation(b);s.Step();
            s.QueueInput(state=>U(state,"ally-a").SetVitals(0,0));Step(s,13);
            Assert.That(U(b,"ally-b").CurrentHP,Is.EqualTo(275));
            Assert.That(b.SkillEventsThisTick.Single(e=>e.Kind==SkillEventKind.EffectApplied).TargetId,Is.EqualTo("ally-b"));
        }
        [Test] public void HealDoesNotSwitchLivingTargetWhenAnotherAllyIsHurt()
        {
            var b=Battle("chansey");U(b,"ally-a").SetVitals(100,0);Ready(b);var s=Simulation(b);s.Step();
            s.QueueInput(state=>U(state,"ally-b").SetVitals(10,0));Step(s,13);
            Assert.That(U(b,"ally-a").CurrentHP,Is.EqualTo(275));Assert.That(U(b,"ally-b").CurrentHP,Is.EqualTo(10));
        }
        [Test] public void FullHPStillCastsWithoutOverheal()
        {
            var b=Battle("chansey");Ready(b);var s=Simulation(b);Step(s,14);
            Assert.That(b.SkillEffectEventsThisTick.Single().ActualValue,Is.Zero);
            Assert.That(U(b,"caster").CurrentEnergy,Is.Zero);
        }
        [Test] public void OvertimeHealIsReducedAndClampedToMaxHP()
        {
            var b=Battle("chansey");var s=Simulation(b);Step(s,900);
            U(b,"ally-b").SetVitals(100,0);Ready(b);Step(s,14);
            Assert.That(b.IsOvertime,Is.True);
            Assert.That(U(b,"ally-b").CurrentHP,Is.EqualTo(159.5f).Within(.001));
            var c=Battle("chansey");U(c,"ally-b").SetVitals(590,0);Ready(c);Step(Simulation(c),14);
            Assert.That(U(c,"ally-b").CurrentHP,Is.EqualTo(600));
        }
        [Test] public void MissilePayloadSurvivesCasterDeathAndDoesNotHitOtherUnits()
        {
            var b=Battle("weedle");Ready(b);var s=Simulation(b);Step(s,12);
            float hp=U(b,"enemy").CurrentHP;
            s.QueueInput(state=>U(state,"caster").SetVitals(0,0));Step(s,5);
            Assert.That(U(b,"enemy").CurrentHP,Is.EqualTo(hp-DamageCalculator.Calculate(130,DamageType.Physical,40,20)).Within(.001));
            Assert.That(U(b,"reserve").CurrentHP,Is.EqualTo(U(b,"reserve").Stats.MaxHP));
        }
        [Test] public void SpellPowerChangesMissileAndHealButNotBuffs()
        {
            var skills=PrototypeRoster.CreateSkills();
            skills.TryGet("pin-missile",out var missile);skills.TryGet("soft-boiled",out var heal);
            Assert.That(missile.Effects[0].ResolveValue(UnitRank.Two,50,110.5f),Is.EqualTo(381.225f).Within(.001));
            Assert.That(heal.Effects[0].ResolveValue(UnitRank.Two,50,68),Is.EqualTo(300));
            var b=Battle("slowpoke",UnitRank.Three);Ready(b);
            var spellBuff=new StatusEffectDefinition("spell",10,StatusTargetTeam.Ally,
                modifiers:new[]{new StatModifierDefinition(CombatStat.SpellPower,StatModifierType.Flat,50)});
            var status=PrototypeRoster.CreateStatuses();
            var cat=new StatusCatalog(new[]{status.Get("harden-r3"),spellBuff});
            var sim=new BattleSimulation(b,new Policy(),statusCatalog:cat);b.StatusEffects.Apply("spell","caster","caster");Step(sim,20);
            Assert.That(U(b,"caster").EffectiveStats.Armor,Is.EqualTo(72));
        }
        [Test] public void LocalRoundActuallyExecutesAllFourSkills()
        {
            var cat=PrototypeRoster.CreateCatalog();var defs=PrototypeRoster.CreateDefinitions();
            var match=MatchStateFactory.CreateWithPool("roster-round",new[]{"p1","p2"},cat,defs.Select(d=>d.Id));
            foreach(var player in match.Players)
            {
                player.SetProgress(20,0,4);
                for(int i=0;i<4;i++)SharedPoolSystem.RegisterUnit(match,cat.CreateUnit(player.PlayerId+"-"+i,defs[i].Id,player.PlayerId,
                    UnitRank.One,0,UnitPlacement.OnBoard(new BoardPosition(i,0))),defs[i].Id);
            }
            match.TransitionTo(MatchPhase.Starting);match.TransitionTo(MatchPhase.Preparation);
            foreach(var p in match.Players)new ShopSystem().RefreshForRound(match,p.PlayerId);
            var loop=new LocalRoundCoordinator(match,cat,skillCatalog:PrototypeRoster.CreateSkills(),statusCatalog:PrototypeRoster.CreateStatuses());
            Assert.That(loop.StartCombat(match.RoundNumber),Is.True);var seen=new System.Collections.Generic.HashSet<string>();
            while(match.Phase==MatchPhase.Combat)
            {
                loop.Step();foreach(var e in loop.Battle.SkillEventsThisTick.Where(e=>e.Kind==SkillEventKind.EffectApplied))seen.Add(e.SkillId);
            }
            Assert.That(seen,Is.EquivalentTo(new[]{"harden","fury-swipes","pin-missile","soft-boiled"}));
            match.Pool.AssertConservation(match);
        }
        [Test] public void ExplicitValuesCopyInputsAndRejectInvalidShape()
        {
            var values=new[]{175f,200f,225f};var e=new SkillEffectDefinition(SkillEffectType.Heal,EffectTargetSelector.Self,175,rankValues:values);
            values[1]=999;Assert.That(e.ResolveValue(UnitRank.Two,0,0),Is.EqualTo(200));
            Assert.Throws<ArgumentException>(()=>new SkillEffectDefinition(SkillEffectType.Heal,EffectTargetSelector.Self,1,rankValues:new[]{1f,2f}));
            Assert.Throws<OverflowException>(()=>new SkillEffectDefinition(SkillEffectType.Damage,EffectTargetSelector.CastTarget,float.MaxValue,
                valueSource:SkillValueSource.Attack).ResolveValue(UnitRank.One,0,2));
        }
        [TestCase("slowpoke",15,34)][TestCase("mankey",10,25)][TestCase("weedle",11,28)][TestCase("chansey",13,28)]
        public void RosterTimingMatchesCeilingTicks(string pokemon,int effectTick,int endTick)
        {
            var b=Battle(pokemon);Ready(b);var s=Simulation(b);s.Step();
            var cast=s.GetRuntime("caster").SkillCast;
            Assert.That(cast.EffectTick,Is.EqualTo(effectTick));Assert.That(cast.EndTick,Is.EqualTo(endTick));
        }
        [TestCase(2)][TestCase(3)][TestCase(8)]
        public void ActualRosterCompletesFullMatchAndPreservesPool(int players)
        {
            var run=new LocalMatchSimulation(new LocalSimulationSettings(123,players,flowRules:new RoundFlowRules(.1,.1)),
                PrototypeRoster.CreateCatalog(),PrototypeRoster.CreateDefinitions().Select(d=>d.Id),new MatchRules(startingHP:10),
                PrototypeRoster.CreateSkills(),PrototypeRoster.CreateStatuses());
            run.RunToEnd();Assert.That(run.Status,Is.EqualTo(SimulationStatus.Completed),run.Error);
            run.Match.Pool.AssertConservation(run.Match);
        }
    }
}
