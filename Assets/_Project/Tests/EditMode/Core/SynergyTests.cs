using System;
using System.Linq;
using NUnit.Framework;
using PokeChess.Core.Battle;
using PokeChess.Core.Pokemon;
using PokeChess.Core.Match;

namespace PokeChess.Core.Tests
{
    public sealed class SynergyTests
    {
        private sealed class StillPolicy : SkillCombatBehaviorPolicy
        {
            public StillPolicy() : base(PrototypeRoster.CreateSkills()) { }
            public override bool CanMove(BattleState b, UnitCombatState u) => false;
            public override bool CanAttack(BattleState b, UnitCombatState u) => false;
            public override bool CanUseSkill(BattleState b, UnitCombatState u) => u.TeamId == 1;
        }
        private static BattleState Battle(params string[] species)
        {
            var c = PrototypeRoster.CreateCatalog();
            var setup = species.Select((s, i) => new BattleUnitSetup(c.CreateUnit("u" + i, s, "p1", UnitRank.One, 0, UnitPlacement.Unplaced), 1, new BoardPosition(i, 3))).ToList();
            setup.Add(new BattleUnitSetup(c.CreateUnit("enemy", "charmander", "p2", UnitRank.Three, 0, UnitPlacement.Unplaced), 2, new BoardPosition(3, 4)));
            var b = BattleStateFactory.Create("synergy", 1, 123, 30, c, setup);
            U(b, "enemy").SetShield(100000);
            return b;
        }
        private static UnitCombatState U(BattleState b, string id = "u0") => b.Units.Single(u => u.UnitInstanceId == id);
        private static BattleSimulation Sim(BattleState b) => new BattleSimulation(b, new StillPolicy(), statusCatalog: PrototypeRoster.CreateStatuses());
        private static void Cast(BattleState b, BattleSimulation sim, string id = "u0")
        {
            var u = U(b, id); u.SetVitals(u.CurrentHP, u.Stats.MaxEnergy);
            for (int i = 0; i < 100; i++)
            {
                sim.Step();
                if (b.SkillEventsThisTick.Any(e => e.UnitId == id && e.Kind == SkillEventKind.EffectApplied)) return;
            }
            Assert.Fail("Skill did not resolve: " + id);
        }
        [Test] public void RosterHasFourChoicesForEveryTrait()
        {
            var definitions = PrototypeRoster.CreateDefinitions();
            foreach (var t in SynergyTraits.All) Assert.That(definitions.Count(d => d.TraitIds.Contains(t)), Is.EqualTo(4));
            Assert.That(definitions.All(d => d.TraitIds.Count > 0), Is.True);
        }
        [Test] public void DuplicateSpeciesAndRanksOnlyCountOnce()
        {
            var b = Battle("slowpoke", "slowpoke", "geodude"); Sim(b);
            Assert.That(b.Synergies.TeamCounts(1).Single(c => c.TraitId == SynergyTraits.Guardian).Count, Is.EqualTo(2));
            Assert.That(U(b).CurrentShield, Is.EqualTo(80));
            var c = PrototypeRoster.CreateCatalog();
            Assert.That(SynergySystem.Count(new[] { c.Get("slowpoke"), c.Get("slowpoke") }).Single(s => s.TraitId == SynergyTraits.Guardian).Tier, Is.Zero);
        }
        [Test] public void TeamsDoNotShareCountsAndNonMembersDoNotReceiveEffects()
        {
            var b = Battle("slowpoke", "geodude", "mankey"); Sim(b);
            Assert.That(U(b, "u2").CurrentShield, Is.Zero);
            Assert.That(b.Synergies.TeamCounts(2).All(c => c.Tier == 0), Is.True);
        }
        [Test] public void TierRemainsLockedAfterDeaths()
        {
            var b = Battle("slowpoke", "geodude", "squirtle"); Sim(b);
            U(b, "u2").SetVitals(0, 0); b.TryRemoveUnit("u2");
            Assert.That(b.Synergies.Tier(U(b), SynergyTraits.Guardian), Is.EqualTo(3));
        }
        [Test] public void GuardianBreakUsesStartingShieldBeforeAddedShieldAndOnlyTriggersOnce()
        {
            var b = Battle("slowpoke", "geodude", "squirtle"); Sim(b); var u = U(b); u.SetShield(u.CurrentShield + 500);
            var damage = new DamageProcessor();
            damage.Apply(b, new DamageRequest("enemy", "u0", 139, DamageType.True));
            Assert.That(u.EffectiveStats.Armor, Is.EqualTo(40));
            damage.Apply(b, new DamageRequest("enemy", "u0", 1, DamageType.True));
            Assert.That(u.CurrentShield, Is.EqualTo(500));
            Assert.That(u.EffectiveStats.Armor, Is.EqualTo(50));
            Assert.That(u.EffectiveStats.MagicResistance, Is.EqualTo(25));
            damage.Apply(b, new DamageRequest("enemy", "u0", 10, DamageType.True));
            Assert.That(b.Synergies.EventsThisTick.Count(e => e.Detail == "GUARD"), Is.EqualTo(1));
        }
        [Test] public void LethalGuardianBreakDoesNotApplyStatusToDeadUnit()
        {
            var b = Battle("slowpoke", "geodude", "squirtle"); Sim(b);
            new DamageProcessor().Apply(b, new DamageRequest("enemy", "u0", 10000, DamageType.True));
            Assert.That(b.StatusEffects.Active, Is.Empty);
        }
        [TestCase(2, 10f)][TestCase(3, 20f)]
        public void FocusStartingEnergy(int count, float bonus)
        {
            var b = count == 2 ? Battle("gastly", "caterpie") : Battle("gastly", "caterpie", "magnemite"); Sim(b);
            Assert.That(U(b).CurrentEnergy, Is.EqualTo(bonus));
            Assert.That(U(b, "u1").CurrentEnergy, Is.EqualTo(20 + bonus));
        }
        [Test] public void FocusFirstSkillHasteDoesNotRefreshOnSecondSkill()
        {
            var b = Battle("gastly", "caterpie", "magnemite"); var sim = Sim(b); Cast(b, sim);
            long expire = b.StatusEffects.Active.Single(s => s.Definition.Id == "synergy-focus").ExpireTick;
            for (int i = 0; i < 45; i++) sim.Step(); Cast(b, sim);
            Assert.That(b.StatusEffects.Active.Single(s => s.Definition.Id == "synergy-focus").ExpireTick, Is.EqualTo(expire));
        }
        [TestCase(2, 1)][TestCase(3, 2)]
        public void AssaultAddsDamageToOriginalHitAndConsumesOnlyBasicAttacks(int tier, int charges)
        {
            var b = tier == 2 ? Battle("mankey", "weedle") : Battle("mankey", "weedle", "bulbasaur"); var sim = Sim(b); Cast(b, sim);
            var u = U(b); Assert.That(b.Synergies.AssaultCharges(u), Is.EqualTo(charges));
            var damage = new DamageProcessor();
            damage.Apply(b, new DamageRequest("u0", "enemy", 10, DamageType.Magic));
            Assert.That(b.Synergies.AssaultCharges(u), Is.EqualTo(charges));
            float before = U(b, "enemy").CurrentShield;
            float raw = u.EffectiveStats.Attack;
            damage.Apply(b, new DamageRequest("u0", "enemy", raw, DamageType.Physical, isBasicAttack: true));
            Assert.That(before - U(b, "enemy").CurrentShield, Is.EqualTo(DamageCalculator.Calculate(raw * 1.25f, DamageType.Physical, 45, 35)));
            Assert.That(b.Synergies.AssaultCharges(u), Is.EqualTo(charges - 1));
            Assert.That(b.DamageResultsThisTick.Count, Is.EqualTo(2));
        }
        [Test] public void AssaultRefreshDoesNotStackAndExpires()
        {
            var b = Battle("mankey", "weedle", "bulbasaur"); var sim = Sim(b); Cast(b, sim);
            for (int i = 0; i < 45; i++) sim.Step(); Cast(b, sim);
            Assert.That(b.Synergies.AssaultCharges(U(b)), Is.EqualTo(2));
            for (int i = 0; i < 181; i++) sim.Step();
            Assert.That(b.Synergies.AssaultCharges(U(b)), Is.Zero);
        }
        [Test] public void InterruptedCastDoesNotGrantSynergy()
        {
            var b = Battle("mankey", "weedle"); var sim = Sim(b); U(b).SetVitals(500, 50); sim.Step();
            sim.QueueSkillInterrupt("u0"); sim.Step();
            Assert.That(b.Synergies.AssaultCharges(U(b)), Is.Zero);
        }
        [Test] public void LinkChoosesNearestOtherAllyAndOnlyTriggersOnFirstSkill()
        {
            var b = Battle("chansey", "caterpie", "mankey"); var sim = Sim(b); Cast(b, sim);
            Assert.That(U(b).CurrentShield, Is.Zero);
            Assert.That(U(b, "u1").CurrentShield, Is.EqualTo(60));
            Assert.That(U(b, "u2").CurrentShield, Is.Zero);
            for (int i = 0; i < 45; i++) sim.Step(); Cast(b, sim);
            Assert.That(U(b, "u1").CurrentShield, Is.EqualTo(60));
        }
        [Test] public void LinkThreeGivesAttackBuffAndMultipleSourcesDoNotStackBuff()
        {
            var b = Battle("chansey", "caterpie", "bulbasaur", "squirtle"); var sim = Sim(b); Cast(b, sim);
            Assert.That(U(b, "u1").CurrentShield, Is.EqualTo(100));
            Assert.That(U(b, "u1").EffectiveStats.Attack, Is.EqualTo(35 * 1.15f).Within(.001));
            Cast(b, sim, "u2");
            Assert.That(U(b, "u1").CurrentShield, Is.EqualTo(200));
            Assert.That(U(b, "u1").EffectiveStats.Attack, Is.EqualTo(35 * 1.15f).Within(.001));
        }
        [Test] public void LinkDistanceTieUsesOrdinalInstanceId()
        {
            var b = Battle("mankey", "chansey", "caterpie"); var sim = Sim(b); Cast(b, sim, "u1");
            Assert.That(U(b).CurrentShield, Is.EqualTo(60));
            Assert.That(U(b, "u2").CurrentShield, Is.Zero);
        }
        [Test] public void LinkSkipsDeadAndRemovedAllies()
        {
            var b = Battle("chansey", "caterpie", "mankey"); var sim = Sim(b); U(b, "u1").SetVitals(0, 0); Cast(b, sim);
            Assert.That(U(b, "u2").CurrentShield, Is.EqualTo(60));
        }
        [Test] public void LinkShieldUsesOvertimeReduction()
        {
            var b = Battle("chansey", "caterpie"); var sim = Sim(b);
            for (int i = 0; i < 901; i++) sim.Step(); Cast(b, sim);
            Assert.That(U(b, "u1").CurrentShield, Is.EqualTo(60 * .34f).Within(.001));
        }
        [Test] public void InvalidTuningIsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SynergyRules(assaultDuration: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SynergyRules(focusEnergyTwo: float.NaN));
        }
        [Test] public void ProjectileBasicAttackConsumesOneChargeAndCreatesOneEnergyHit()
        {
            var b = Battle("weedle", "mankey"); var sim = Sim(b); Cast(b, sim);
            var source = U(b); new BasicAttackCombatBehaviorPolicy().OnAttackTiming(b, source, U(b, "enemy"));
            Assert.That(b.Synergies.AssaultCharges(source), Is.EqualTo(1), "Launching is not an impact.");
            for (int i = 0; i < 30; i++)
            {
                sim.Step();
                if (!b.DamageResultsThisTick.Any(d => d.Request.IsBasicAttack)) continue;
                Assert.That(b.Synergies.AssaultCharges(source), Is.Zero);
                Assert.That(b.Energy.EventsThisTick.Count(e => e.UnitId == "u0" && e.Reason == EnergyChangeReason.BasicAttackHit), Is.EqualTo(1));
                return;
            }
            Assert.Fail("Projectile did not hit.");
        }
        [Test] public void ExpiredProjectileDoesNotConsumeAssaultCharge()
        {
            var b = Battle("weedle", "mankey"); var sim = Sim(b); Cast(b, sim);
            new BasicAttackCombatBehaviorPolicy().OnAttackTiming(b, U(b), U(b, "enemy"));
            U(b, "enemy").SetUntargetable(true);
            for (int i = 0; i < 30; i++) sim.Step();
            Assert.That(b.Synergies.AssaultCharges(U(b)), Is.EqualTo(1));
            Assert.That(b.DamageResultsThisTick, Is.Empty);
        }
        [Test] public void AuthoritativeOnlineFramesCarryStartingSynergiesAndAssaultCharges()
        {
            var c = PrototypeRoster.CreateCatalog();
            var match = MatchStateFactory.CreateWithPool("synergy-online", new[] { "p1", "p2" }, c, PrototypeRoster.CreateDefinitions().Select(d => d.Id));
            match.TransitionTo(MatchPhase.Starting); match.TransitionTo(MatchPhase.Preparation);
            var species = new[] { "slowpoke", "geodude", "gastly", "caterpie", "weedle", "mankey" };
            foreach (var player in match.Players)
            {
                player.SetProgress(20, 0, 6);
                new ShopSystem().RefreshForRound(match, player.PlayerId);
                for (int i = 0; i < species.Length; i++) SharedPoolSystem.RegisterUnit(match,
                    c.CreateUnit(player.PlayerId + "-" + i, species[i], player.PlayerId, UnitRank.One, 0, UnitPlacement.OnBoard(new BoardPosition(i, 3))), species[i]);
            }
            var rounds = new LocalRoundCoordinator(match, c, skillCatalog: PrototypeRoster.CreateSkills(), statusCatalog: PrototypeRoster.CreateStatuses());
            var host = new HostCommandProcessor(match, c, rounds); var runtime = new OnlineCombatRuntime(rounds, host);
            CombatFrame baseline = null; bool charged = false, link = false, skill = false, damage = false; long sequence = 0;
            runtime.FrameProduced += f =>
            {
                if (f.baseline) baseline = f;
                foreach (var e in f.events) { Assert.That(e.sequence, Is.GreaterThan(sequence)); sequence = e.sequence;
                    if (e.kind == "Synergy" && e.detail.StartsWith("link:")) link = true;
                    skill |= e.kind == "SkillEffectApplied"; damage |= e.kind == "Damage";
                }
                charged |= f.units.Any(u => u.assaultCharges > 0);
            };
            Assert.That(runtime.StartCombat(1), Is.True);
            Assert.That(baseline.events.Count(e => e.kind == "Synergy"), Is.EqualTo(8));
            Assert.That(baseline.units.First(u => u.definitionId == "slowpoke").shield, Is.EqualTo(80));
            Assert.That(baseline.units.First(u => u.definitionId == "gastly").energy, Is.EqualTo(10));
            foreach (var u in rounds.Battle.Units) u.SetVitals(u.CurrentHP, u.Stats.MaxEnergy);
            for (int i = 0; i < 1400 && match.Phase == MatchPhase.Combat; i++) runtime.Step();
            Assert.That(charged, Is.True); Assert.That(link, Is.False, "Only one Link species in this composition.");
            Assert.That(skill, Is.True, "Existing skill events must still be broadcast.");
            Assert.That(damage, Is.True, "Existing damage events must still be broadcast.");
            Assert.That(match.Phase, Is.Not.EqualTo(MatchPhase.Combat)); match.Pool.AssertConservation(match);
        }
    }
}
