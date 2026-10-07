using System;
using System.Linq;
using System.Collections.Generic;
using NUnit.Framework;
using PokeChess.Core.Battle;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;
namespace PokeChess.Core.Tests
{
    public sealed class HighCostRosterTests
    {
        private static UnitCombatState U(BattleState b,string id)=>b.Units.Single(u=>u.UnitInstanceId==id);
        private sealed class Policy:SkillCombatBehaviorPolicy {
            public Policy():base(PrototypeRoster.CreateSkills()){}
            public override bool CanMove(BattleState b,UnitCombatState u)=>false;
            public override bool CanAttack(BattleState b,UnitCombatState u)=>false;
        }
        private static BattleState Battle(string species,UnitRank rank=UnitRank.One) {
            var c=PrototypeRoster.CreateCatalog();
            BattleUnitSetup Setup(string id,string pokemon,int team,int x,int y,UnitRank r=UnitRank.One)=>
                new BattleUnitSetup(c.CreateUnit(id,pokemon,"p"+team,r,0,UnitPlacement.Unplaced),team,new BoardPosition(x,y));
            return BattleStateFactory.Create("high",1,123,30,c,new[]{Setup("caster",species,1,3,3,rank),
                Setup("ally","slowpoke",1,2,3),Setup("outside","slowpoke",1,0,0),
                Setup("center","slowpoke",2,4,3),Setup("near","slowpoke",2,4,4),
                Setup("hidden","slowpoke",2,5,3),Setup("far","slowpoke",2,6,7)});
        }
        private static BattleSimulation Sim(BattleState b)=>new BattleSimulation(b,new Policy(),statusCatalog:PrototypeRoster.CreateStatuses());
        private static void Step(BattleSimulation s,int n){for(int i=0;i<n;i++)s.Step();}
        private static void Execute(BattleState b) {
            PrototypeRoster.CreateSkills().TryGet(U(b,"caster").SkillId,out var skill);
            new SkillEffectExecutor().Execute(b,U(b,"caster"),U(b,"center"),skill);
        }
        [TestCase(1,180f,15f)][TestCase(2,280f,25f)][TestCase(3,450f,40f)]
        public void AllyShieldBuffIncludesSelfAndOnlyAlliesInRadius(int rank,float shield,float buff) {
            var b=Battle("squirtle",(UnitRank)rank);var s=Sim(b);Execute(b);Execute(b);
            foreach(string id in new[]{"caster","ally"}) {
                Assert.That(U(b,id).CurrentShield,Is.EqualTo(shield*2));
                Assert.That(U(b,id).EffectiveStats.Attack,Is.EqualTo(U(b,id).Stats.Attack+buff).Within(.001));
            }
            Assert.That(U(b,"outside").CurrentShield,Is.Zero);Assert.That(U(b,"center").CurrentShield,Is.Zero);
            Step(s,121);Assert.That(U(b,"ally").EffectiveStats.Attack,Is.EqualTo(50));
        }
        [Test] public void AllyBuffStrongestAcrossRanksAndSourcesDoesNotStack() {
            var b=Battle("squirtle");Sim(b);
            b.StatusEffects.Apply("aqua-guard-r3","caster","ally");
            b.StatusEffects.Apply("aqua-guard-r1","outside","ally");
            Assert.That(U(b,"ally").EffectiveStats.Attack,Is.EqualTo(90));
        }
        [TestCase(1,350f,1f)][TestCase(2,550f,1.25f)][TestCase(3,850f,1.5f)]
        public void PsychicFreezesVictimsBeforeDamageAndStunsSurvivors(int rank,float raw,float duration) {
            var b=Battle("abra",(UnitRank)rank);Sim(b);U(b,"center").SetVitals(1,0);
            U(b,"near").SetShield(2000);U(b,"hidden").SetUntargetable(true);Execute(b);
            Assert.That(U(b,"center").IsAlive,Is.False);
            Assert.That(U(b,"near").CurrentShield,Is.EqualTo(2000-DamageCalculator.Calculate(raw,DamageType.Magic,40,20)).Within(.001));
            Assert.That(b.StatusEffects.Active.Single().TargetId,Is.EqualTo("near"));
            Assert.That(b.StatusEffects.Active.Single().Definition.Duration,Is.EqualTo(duration));
            Assert.That(U(b,"far").CurrentHP,Is.EqualTo(550));Assert.That(U(b,"hidden").CurrentHP,Is.EqualTo(550));
        }
        [TestCase(1,250f)][TestCase(2,400f)][TestCase(3,650f)]
        public void ProjectileSpawnsOnceThenHitsAreaOnceAtImpact(int rank,float raw) {
            var b=Battle("magnemite",(UnitRank)rank);var s=Sim(b);U(b,"hidden").SetUntargetable(true);
            U(b,"center").SetShield(2000);U(b,"near").SetShield(2000);Execute(b);
            Assert.That(b.Projectiles.Active.Count,Is.EqualTo(1));Assert.That(U(b,"near").CurrentShield,Is.EqualTo(2000));
            var p=b.Projectiles.Active.Single();Step(s,(int)(p.ArrivalTick-b.CurrentTick)+1);
            foreach(string id in new[]{"center","near"})Assert.That(U(b,id).CurrentShield,Is.EqualTo(2000-DamageCalculator.Calculate(raw,DamageType.Physical,40,20)).Within(.001));
            Assert.That(b.Projectiles.Active,Is.Empty);Assert.That(U(b,"hidden").CurrentHP,Is.EqualTo(550));
            Assert.That(U(b,"ally").CurrentHP,Is.EqualTo(550));Step(s,3);
            Assert.That(U(b,"near").CurrentShield,Is.EqualTo(2000-DamageCalculator.Calculate(raw,DamageType.Physical,40,20)).Within(.001));
        }
        [TestCase("dead")][TestCase("hidden")][TestCase("removed")]
        public void LostImpactTargetExpiresWithoutSplash(string kind) {
            var b=Battle("magnemite");var s=Sim(b);Execute(b);
            if(kind=="dead")U(b,"center").SetVitals(0,0);
            else if(kind=="hidden")U(b,"center").SetUntargetable(true);
            else b.TryRemoveUnit("center");
            Step(s,8);Assert.That(b.Projectiles.Active,Is.Empty);Assert.That(U(b,"near").CurrentHP,Is.EqualTo(550));
        }
        [Test] public void ProjectileUsesImpactPositionAndSurvivesSourceDeath() {
            var b=Battle("magnemite");var s=Sim(b);Execute(b);
            b.TryMoveUnit("center",new BoardPosition(6,6));U(b,"caster").SetVitals(0,0);
            Step(s,8);Assert.That(U(b,"near").CurrentHP,Is.EqualTo(550));
            Assert.That(U(b,"far").CurrentHP,Is.LessThan(550));
        }
        [TestCase(1,350f,30f,.3f)][TestCase(2,550f,50f,.45f)][TestCase(3,900f,80f,.6f)]
        public void FlameAppliesShieldThenBuffThenNearbyDamage(int rank,float shield,float attack,float speed) {
            var b=Battle("charmander",(UnitRank)rank);var s=Sim(b);Execute(b);
            Assert.That(U(b,"caster").CurrentShield,Is.EqualTo(shield));
            Assert.That(U(b,"caster").EffectiveStats.Attack,Is.EqualTo(U(b,"caster").Stats.Attack+attack).Within(.001));
            Assert.That(U(b,"caster").EffectiveStats.AttackSpeed,Is.EqualTo(.7f*(1+speed)).Within(.001));
            Assert.That(b.SkillEffectEventsThisTick.Select(e=>e.EffectIndex).Distinct(),Is.EqualTo(new[]{0,1,2}));
            Assert.That(U(b,"center").CurrentHP,Is.LessThan(550));Assert.That(U(b,"far").CurrentHP,Is.EqualTo(550));
            Step(s,151);Assert.That(U(b,"caster").EffectiveStats.Attack,Is.EqualTo(U(b,"caster").Stats.Attack));
        }
        [TestCase("squirtle")][TestCase("charmander")]
        public void SelfSupportWorksWithoutNearbyEnemies(string species) {
            var b=Battle(species);Sim(b);foreach(var u in b.Units.Where(u=>u.TeamId==2))u.SetUntargetable(true);
            Execute(b);Assert.That(U(b,"caster").CurrentShield,Is.GreaterThan(0));
        }
        [TestCase("magnemite")][TestCase("squirtle")][TestCase("abra")][TestCase("charmander")]
        public void CastCanBeInterruptedBeforeEffects(string species) {
            var b=Battle(species);var s=Sim(b);var caster=U(b,"caster");caster.SetVitals(caster.CurrentHP,caster.Stats.MaxEnergy);s.Step();
            Assert.That(s.GetRuntime("caster").SkillCast,Is.Not.Null);
            b.StatusEffects.Apply(new StatusEffectDefinition("interrupt",1,crowdControl:CrowdControlKind.Stun),"center","caster");
            Step(s,25);Assert.That(caster.CurrentShield,Is.Zero);Assert.That(caster.EffectiveStats.Attack,Is.EqualTo(caster.Stats.Attack));
            Assert.That(b.Projectiles.Active,Is.Empty);Assert.That(U(b,"center").CurrentHP,Is.EqualTo(550));
        }
        [TestCase("magnemite")][TestCase("squirtle")][TestCase("abra")][TestCase("charmander")]
        public void CastAppliesOnceAtAuthoredEffectTick(string species) {
            var b=Battle(species);var s=Sim(b);var c=U(b,"caster");c.SetVitals(c.CurrentHP,c.Stats.MaxEnergy);s.Step();
            var cast=s.GetRuntime("caster").SkillCast;Step(s,(int)(cast.EffectTick-b.CurrentTick));
            Assert.That(b.SkillEffectEventsThisTick,Is.Empty);s.Step();
            Assert.That(b.SkillEventsThisTick.Count(e=>e.SkillId==c.SkillId&&e.Kind==SkillEventKind.EffectApplied),Is.EqualTo(1));
            s.Step();Assert.That(b.SkillEventsThisTick.Any(e=>e.SkillId==c.SkillId&&e.Kind==SkillEventKind.EffectApplied),Is.False);
        }
        [TestCase("magnemite")][TestCase("abra")]
        public void EnemyCastRetargetsBeforeEffect(string species) {
            var b=Battle(species);var s=Sim(b);var c=U(b,"caster");c.SetVitals(c.CurrentHP,c.Stats.MaxEnergy);s.Step();
            var cast=s.GetRuntime("caster").SkillCast;U(b,cast.TargetId).SetVitals(0,0);
            Step(s,(int)(cast.EffectTick-b.CurrentTick)+1);
            Assert.That(b.SkillEffectEventsThisTick.Any(e=>e.Outcome!=SkillEffectOutcome.Skipped),Is.True);
        }
        [Test] public void SplashMetadataRejectsInvalidEffectsAndNegativeRadius() {
            Assert.Throws<ArgumentException>(()=>new SkillEffectDefinition(SkillEffectType.Damage,EffectTargetSelector.CastTarget,1,impactRadius:1));
            Assert.Throws<ArgumentException>(()=>new SkillEffectDefinition(SkillEffectType.ProjectileDamage,EffectTargetSelector.CastTarget,1,impactRadius:-1));
        }
        [Test] public void OvertimeScalesShieldAndStunButNotDamageOrBuff() {
            var b=Battle("squirtle");var s=Sim(b);Step(s,901);Execute(b);
            Assert.That(U(b,"caster").CurrentShield,Is.EqualTo(61.2f).Within(.001));Assert.That(U(b,"ally").EffectiveStats.Attack,Is.EqualTo(130));
            b=Battle("abra");s=Sim(b);Step(s,901);U(b,"center").SetShield(1000);Execute(b);
            Assert.That(U(b,"center").CurrentShield,Is.EqualTo(1000-DamageCalculator.Calculate(350,DamageType.Magic,40,20)).Within(.001));
            Assert.That(b.StatusEffects.Active.All(e=>e.ExpireTick-b.CurrentTick==11),Is.True);
        }
        [Test] public void SpellPowerScalesNumericEffectsOnly() {
            foreach(string species in new[]{"squirtle","abra","magnemite","charmander"}) {
                var b=Battle(species);Sim(b);b.StatusEffects.Apply(new StatusEffectDefinition("sp",4,StatusTargetTeam.Ally,
                    modifiers:new[]{new StatModifierDefinition(CombatStat.SpellPower,StatModifierType.Flat,100)}),"caster","caster");
                Execute(b);PrototypeRoster.CreateSkills().TryGet(U(b,"caster").SkillId,out var skill);
                foreach(var e in b.SkillEffectEventsThisTick.Where(e=>e.Type!=SkillEffectType.ApplyStatus))
                    Assert.That(e.CalculatedValue,Is.EqualTo(skill.Effects[e.EffectIndex].BaseValue*2));
            }
        }
        [TestCase(1)][TestCase(2)][TestCase(3)]
        public void MixedTwelveSpeciesCompleteEverySkillWithNormalPool(int rank) {
            var c=PrototypeRoster.CreateCatalog();var defs=PrototypeRoster.CreateDefinitions();
            Assert.That(defs.Length,Is.EqualTo(12));Assert.That(defs.Select(d=>d.Cost),Is.EqualTo(new[]{1,1,1,1,2,2,2,3,3,3,4,5}));
            var m=MatchStateFactory.CreateWithPool("mixed12",new[]{"p1","p2"},c,defs.Select(d=>d.Id));
            foreach(var player in m.Players) {
                player.SetProgress(20,0,9);
                for(int i=0;i<defs.Length;i++) {
                    if((player.PlayerId=="p1")!=(i<6))continue;
                    var r=(UnitRank)rank;
                    SharedPoolSystem.RegisterUnit(m,c.CreateUnit(player.PlayerId+"-"+i,defs[i].Id,player.PlayerId,r,0,
                        UnitPlacement.OnBoard(new BoardPosition(i%7,i/7))),defs[i].Id);
                }
            }
            m.TransitionTo(MatchPhase.Starting);m.TransitionTo(MatchPhase.Preparation);
            foreach(var p in m.Players)new ShopSystem().RefreshForRound(m,p.PlayerId);
            var loop=new LocalRoundCoordinator(m,c,skillCatalog:PrototypeRoster.CreateSkills(),statusCatalog:PrototypeRoster.CreateStatuses());
            Assert.That(loop.StartCombat(m.RoundNumber),Is.True);
            foreach(var u in loop.Battle.Units)u.SetVitals(u.CurrentHP,u.Stats.MaxEnergy);
            var seen=new HashSet<string>();int tick=0;
            while(m.Phase==MatchPhase.Combat&&tick++<1400) {
                loop.Step();foreach(var e in loop.Battle.SkillEventsThisTick.Where(e=>e.Kind==SkillEventKind.EffectApplied))seen.Add(e.SkillId);
            }
            Assert.That(m.Phase,Is.EqualTo(MatchPhase.Result));Assert.That(seen,Is.EquivalentTo(defs.Select(d=>d.SkillId)));m.Pool.AssertConservation(m);
        }
    }
}

