using System;
using System.Linq;
using NUnit.Framework;
using PokeChess.Core.Battle;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Tests
{
    public sealed class AdvancedRosterTests
    {
        private static UnitCombatState U(BattleState b,string id)=>b.Units.Single(u=>u.UnitInstanceId==id);
        private static BattleState Battle(string species,UnitRank rank=UnitRank.One)
        {
            var c=PrototypeRoster.CreateCatalog();
            BattleUnitSetup Setup(string id,string pokemon,int team,int x,int y,UnitRank r=UnitRank.One)=>
                new BattleUnitSetup(c.CreateUnit(id,pokemon,"p"+team,r,0,UnitPlacement.Unplaced),team,new BoardPosition(x,y));
            return BattleStateFactory.Create("advanced",1,123,30,c,new[]{Setup("caster",species,1,3,3,rank),
                Setup("ally","slowpoke",1,0,0),Setup("near","slowpoke",2,4,3),Setup("second","slowpoke",2,3,4),
                Setup("hidden","slowpoke",2,3,2),Setup("far","slowpoke",2,6,7)});
        }
        private sealed class Policy:SkillCombatBehaviorPolicy
        {
            public Policy():base(PrototypeRoster.CreateSkills()){}
            public override bool CanMove(BattleState b,UnitCombatState u)=>false;
            public override bool CanAttack(BattleState b,UnitCombatState u)=>false;
        }
        private static BattleSimulation Simulation(BattleState b)=>new BattleSimulation(b,new Policy(),statusCatalog:PrototypeRoster.CreateStatuses());
        private static void Step(BattleSimulation s,int n){for(int i=0;i<n;i++)s.Step();}
        private static void Execute(BattleState b,string skill)
        {
            PrototypeRoster.CreateSkills().TryGet(skill,out var definition);
            new SkillEffectExecutor().Execute(b,U(b,"caster"),U(b,"near"),definition);
        }
        [TestCase(1,250f)][TestCase(2,400f)][TestCase(3,650f)]
        public void ShieldPrecedesNearbyTauntAndUsesExplicitRank(int rank,float shield)
        {
            var b=Battle("geodude",(UnitRank)rank);var s=Simulation(b);U(b,"hidden").SetUntargetable(true);
            Execute(b,"rock-guard");Assert.That(U(b,"caster").CurrentShield,Is.EqualTo(shield));
            Assert.That(b.SkillEffectEventsThisTick.Select(e=>e.EffectIndex),Is.EqualTo(new[]{0,1,1}));
            Assert.That(b.StatusEffects.Active.Select(e=>e.TargetId),Is.EquivalentTo(new[]{"near","second"}));
            Assert.That(new CombatBehaviorPolicy().SelectTarget(b,U(b,"near")),Is.EqualTo("caster"));
            Step(s,61);Assert.That(b.StatusEffects.Active,Is.Empty);
            Assert.That(U(b,"caster").CurrentShield,Is.EqualTo(shield),"Shield remains after taunt expires.");
        }
        [Test] public void ShieldStacksAndAbsorbsDamageBeforeHP()
        {
            var b=Battle("geodude");Simulation(b);Execute(b,"rock-guard");Execute(b,"rock-guard");
            Assert.That(U(b,"caster").CurrentShield,Is.EqualTo(500));
            new DamageProcessor().Apply(b,new DamageRequest("near","caster",100,DamageType.True));
            Assert.That(U(b,"caster").CurrentShield,Is.EqualTo(400));Assert.That(U(b,"caster").CurrentHP,Is.EqualTo(800));
        }
        [Test] public void ShieldWorksWithNoEnemiesInRadiusAndTauntStopsWhenCasterDies()
        {
            var b=Battle("geodude");Simulation(b);
            U(b,"near").SetUntargetable(true);U(b,"second").SetUntargetable(true);U(b,"hidden").SetUntargetable(true);
            Execute(b,"rock-guard");Assert.That(U(b,"caster").CurrentShield,Is.EqualTo(250));Assert.That(b.StatusEffects.Active,Is.Empty);
            U(b,"near").SetUntargetable(false);Execute(b,"rock-guard");
            Assert.That(U(b,"near").EffectiveTauntSource,Is.EqualTo("caster"));
            U(b,"caster").SetVitals(0,0);Assert.That(U(b,"near").EffectiveTauntSource,Is.Null);
        }
        [Test] public void OvertimeScalesNewShieldAndTauntDuration()
        {
            var b=Battle("geodude");var s=Simulation(b);Step(s,901);Execute(b,"rock-guard");
            Assert.That(U(b,"caster").CurrentShield,Is.EqualTo(85).Within(.001));
            Assert.That(b.StatusEffects.Active.All(e=>e.ExpireTick-b.CurrentTick==21),Is.True);
        }
        [TestCase(1,300f)][TestCase(2,450f)][TestCase(3,700f)]
        public void BurstUsesExplicitMagicDamageWithNoCritOrProjectile(int rank,float raw)
        {
            var b=Battle("gastly",(UnitRank)rank);Simulation(b);float hp=U(b,"near").CurrentHP;Execute(b,"shadow-burst");
            Assert.That(U(b,"near").CurrentHP,Is.EqualTo(Math.Max(0,hp-DamageCalculator.Calculate(raw,DamageType.Magic,40,20))).Within(.001));
            Assert.That(U(b,"second").CurrentHP,Is.EqualTo(550));Assert.That(b.Projectiles.Active,Is.Empty);
        }
        [TestCase(1,1.4f)][TestCase(2,1.2f)][TestCase(3,1f)]
        public void SlowOnlyAffectsMoveSpeedAndExpires(int rank,float speed)
        {
            var b=Battle("caterpie",(UnitRank)rank);var s=Simulation(b);Execute(b,"string-shot");
            Assert.That(U(b,"near").EffectiveStats.MoveSpeed,Is.EqualTo(speed).Within(.0001));
            Assert.That(U(b,"near").EffectiveStats.AttackSpeed,Is.EqualTo(.5f));
            Step(s,121);Assert.That(U(b,"near").EffectiveStats.MoveSpeed,Is.EqualTo(2));
        }
        [Test] public void SlowRefreshesAndStrongestSourceWinsWithoutMultiplication()
        {
            var b=Battle("caterpie",UnitRank.Three);var s=Simulation(b);Execute(b,"string-shot");
            long expiry=b.StatusEffects.Active.Single().ExpireTick;Step(s,5);Execute(b,"string-shot");
            b.StatusEffects.Apply("string-shot-r1","hidden","caster");
            b.StatusEffects.Apply("string-shot-r3","second","caster");
            Assert.That(U(b,"near").EffectiveStats.MoveSpeed,Is.EqualTo(1));
            Assert.That(b.StatusEffects.Active.Single(e=>e.TargetId=="near").ExpireTick,Is.GreaterThan(expiry));
            Assert.That(U(b,"caster").EffectiveStats.MoveSpeed,Is.EqualTo(1));
        }
        [TestCase(1,85f,.98f)][TestCase(2,145.5f,1.12f)][TestCase(3,230.5f,1.33f)]
        public void GrowthUsesRankedAttackPlusFlatBuffAndRefreshes(int rank,float attack,float speed)
        {
            var b=Battle("bulbasaur",(UnitRank)rank);var s=Simulation(b);Execute(b,"growth");
            Step(s,5);Execute(b,"growth");
            Assert.That(U(b,"caster").EffectiveStats.Attack,Is.EqualTo(attack).Within(.001));
            Assert.That(U(b,"caster").EffectiveStats.AttackSpeed,Is.EqualTo(speed).Within(.001));
            Assert.That(b.StatusEffects.Active.Count,Is.EqualTo(1));
            Step(s,121);Assert.That(U(b,"caster").EffectiveStats.Attack,Is.EqualTo(U(b,"caster").Stats.Attack));
        }
        [Test] public void SpellPowerScalesShieldAndBurstButNotSlowOrGrowth()
        {
            foreach(var species in new[]{"geodude","gastly","caterpie","bulbasaur"})
            {
                var b=Battle(species);Simulation(b);b.StatusEffects.Apply(new StatusEffectDefinition("sp",10,StatusTargetTeam.Ally,
                    modifiers:new[]{new StatModifierDefinition(CombatStat.SpellPower,StatModifierType.Flat,50)}),"caster","caster");
                Execute(b, U(b,"caster").SkillId);
                if(species=="geodude")Assert.That(U(b,"caster").CurrentShield,Is.EqualTo(375));
                if(species=="gastly")Assert.That(U(b,"near").CurrentHP,Is.EqualTo(175).Within(.001));
                if(species=="caterpie")Assert.That(U(b,"near").EffectiveStats.MoveSpeed,Is.EqualTo(1.4f).Within(.001));
                if(species=="bulbasaur")Assert.That(U(b,"caster").EffectiveStats.Attack,Is.EqualTo(85));
            }
        }
        [TestCase("geodude")][TestCase("gastly")][TestCase("caterpie")][TestCase("bulbasaur")]
        public void CastAppliesOnceAtEffectTickAndCanBeInterrupted(string species)
        {
            var b=Battle(species);var s=Simulation(b);var caster=U(b,"caster");caster.SetVitals(caster.CurrentHP,caster.Stats.MaxEnergy);
            s.Step();var cast=s.GetRuntime("caster").SkillCast;Assert.That(cast,Is.Not.Null);
            Step(s,(int)(cast.EffectTick-b.CurrentTick));
            Assert.That(b.SkillEventsThisTick.Any(e=>e.SkillId==caster.SkillId&&e.Kind==SkillEventKind.EffectApplied),Is.False);
            s.Step();Assert.That(b.SkillEventsThisTick.Count(e=>e.SkillId==caster.SkillId&&e.Kind==SkillEventKind.EffectApplied),Is.EqualTo(1));
            var interrupted=Battle(species);var other=Simulation(interrupted);var c=U(interrupted,"caster");c.SetVitals(c.CurrentHP,c.Stats.MaxEnergy);other.Step();
            interrupted.StatusEffects.Apply(new StatusEffectDefinition("stun",1,crowdControl:CrowdControlKind.Stun),"near","caster");
            Step(other,20);Assert.That(c.CurrentShield,Is.EqualTo(0));Assert.That(c.EffectiveStats.Attack,Is.EqualTo(c.Stats.Attack));
            Assert.That(U(interrupted,"near").CurrentHP,Is.EqualTo(550));
        }
        [Test] public void BurstRetargetsIfOriginalTargetDiesBeforeEffect()
        {
            var b=Battle("gastly");var s=Simulation(b);var c=U(b,"caster");c.SetVitals(c.CurrentHP,c.Stats.MaxEnergy);s.Step();
            string original=s.GetRuntime("caster").SkillCast.TargetId;
            U(b,original).SetVitals(0,0);Step(s,14);
            Assert.That(b.Units.Count(u=>u.TeamId==2&&u.IsAlive&&u.CurrentHP<550),Is.EqualTo(1));
        }
        [Test] public void EnemyCenteredSelectorRejectsSupportEffects()
        {
            Assert.Throws<ArgumentException>(()=>new SkillEffectDefinition(SkillEffectType.Heal,EffectTargetSelector.EnemiesAroundCaster,10));
        }
        [Test] public void CatalogHasEightBaseFormsAndCostSpecificStocks()
        {
            var c=PrototypeRoster.CreateCatalog();var defs=PrototypeRoster.CreateDefinitions().Take(8).ToArray();
            Assert.That(defs.Length,Is.EqualTo(8));Assert.That(defs.Select(d=>d.Cost),Is.EqualTo(new[]{1,1,1,1,2,2,2,3}));
            var m=MatchStateFactory.CreateWithPool("pool",new[]{"p1","p2"},c,defs.Select(d=>d.Id));
            foreach(var d in defs)Assert.That(m.Pool.GetStock(d.Id).Initial,Is.EqualTo(m.Pool.Rules.SizeForCost(d.Cost)));
            m.Pool.AssertConservation(m);
        }
        [TestCase(1)][TestCase(2)][TestCase(3)]
        public void MixedEightSpeciesExecuteEverySkillAndFinishWithPoolConservation(int rank)
        {
            var c=PrototypeRoster.CreateCatalog();var defs=PrototypeRoster.CreateDefinitions().Take(8).ToArray();
            var match=MatchStateFactory.CreateWithPool("mixed",new[]{"p1","p2"},c,defs.Select(d=>d.Id));
            foreach(var player in match.Players)
            {
                player.SetProgress(20,0,8);
                var r=(UnitRank)(rank==3&&player.PlayerId=="p2"?2:rank);
                for(int i=0;i<defs.Length;i++)SharedPoolSystem.RegisterUnit(match,c.CreateUnit(player.PlayerId+"-"+i,defs[i].Id,
                    player.PlayerId,r,0,UnitPlacement.OnBoard(new BoardPosition(i%7,i/7))),defs[i].Id);
            }
            match.TransitionTo(MatchPhase.Starting);match.TransitionTo(MatchPhase.Preparation);
            foreach(var p in match.Players)new ShopSystem().RefreshForRound(match,p.PlayerId);
            var loop=new LocalRoundCoordinator(match,c,skillCatalog:PrototypeRoster.CreateSkills(),statusCatalog:PrototypeRoster.CreateStatuses());
            Assert.That(loop.StartCombat(match.RoundNumber),Is.True);
            foreach(var unit in loop.Battle.Units)unit.SetVitals(unit.CurrentHP,unit.Stats.MaxEnergy);
            var seen=new System.Collections.Generic.HashSet<string>();int ticks=0;
            while(match.Phase==MatchPhase.Combat&&ticks++<1400)
            {loop.Step();foreach(var e in loop.Battle.SkillEventsThisTick.Where(e=>e.Kind==SkillEventKind.EffectApplied))seen.Add(e.SkillId);}
            Assert.That(match.Phase,Is.EqualTo(MatchPhase.Result));
            Assert.That(seen,Is.EquivalentTo(defs.Select(d=>d.SkillId)));match.Pool.AssertConservation(match);
        }
    }
}
