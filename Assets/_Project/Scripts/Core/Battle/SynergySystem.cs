using System;
using System.Collections.Generic;
using System.Linq;
using PokeChess.Core.Board;

namespace PokeChess.Core.Battle
{
    public static class SynergyTraits
    {
        public const string Guardian = "guardian", Assault = "assault", Focus = "focus", Link = "link";
        public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[] { Guardian, Assault, Focus, Link });
        public static string[] ForPokemon(string id)
        {
            switch (id)
            {
                case "slowpoke": case "geodude": return new[] { Guardian };
                case "mankey": case "weedle": return new[] { Assault };
                case "chansey": return new[] { Link };
                case "gastly": case "magnemite": case "abra": return new[] { Focus };
                case "caterpie": return new[] { Focus, Link };
                case "bulbasaur": return new[] { Assault, Link };
                case "squirtle": return new[] { Guardian, Link };
                case "charmander": return new[] { Guardian, Assault };
                default: return Array.Empty<string>();
            }
        }
    }

    // Alpha tuning values, independent of rank and spell power.
    public sealed class SynergyRules
    {
        public static SynergyRules Disabled { get; } = new SynergyRules(enabled: false);
        public bool Enabled { get; }
        public float GuardianShieldTwo { get; }
        public float GuardianShieldThree { get; }
        public float GuardianResistance { get; }
        public float GuardianDuration { get; }
        public float AssaultFraction { get; }
        public float AssaultDuration { get; }
        public float FocusEnergyTwo { get; }
        public float FocusEnergyThree { get; }
        public float FocusSpeed { get; }
        public float FocusDuration { get; }
        public float LinkShieldTwo { get; }
        public float LinkShieldThree { get; }
        public float LinkAttack { get; }
        public float LinkDuration { get; }
        public SynergyRules(float guardianShieldTwo = 80, float guardianShieldThree = 140,
            float guardianResistance = .25f, float guardianDuration = 4,
            float assaultFraction = .25f, float assaultDuration = 6,
            float focusEnergyTwo = 10, float focusEnergyThree = 20, float focusSpeed = .25f, float focusDuration = 4,
            float linkShieldTwo = 60, float linkShieldThree = 100, float linkAttack = .15f, float linkDuration = 4, bool enabled = true)
        {
            Enabled = enabled;
            foreach (var value in new[] { guardianShieldTwo, guardianShieldThree, guardianResistance, assaultFraction,
                focusEnergyTwo, focusEnergyThree, focusSpeed, linkShieldTwo, linkShieldThree, linkAttack })
                DamageCalculator.Validate(value, true);
            foreach (var duration in new[] { guardianDuration, assaultDuration, focusDuration, linkDuration })
            { DamageCalculator.Validate(duration, true); if (duration <= 0) throw new ArgumentOutOfRangeException(nameof(duration)); }
            GuardianShieldTwo = guardianShieldTwo; GuardianShieldThree = guardianShieldThree;
            GuardianResistance = guardianResistance; GuardianDuration = guardianDuration;
            AssaultFraction = assaultFraction; AssaultDuration = assaultDuration;
            FocusEnergyTwo = focusEnergyTwo; FocusEnergyThree = focusEnergyThree;
            FocusSpeed = focusSpeed; FocusDuration = focusDuration;
            LinkShieldTwo = linkShieldTwo; LinkShieldThree = linkShieldThree; LinkAttack = linkAttack; LinkDuration = linkDuration;
        }
    }

    public readonly struct SynergyCount
    {
        public string TraitId { get; }
        public int Count { get; }
        public int Tier => Count >= 3 ? 3 : Count >= 2 ? 2 : 0;
        public int Needed => Count >= 3 ? 0 : (Count >= 2 ? 3 : 2) - Count;
        public SynergyCount(string traitId, int count) { TraitId = traitId; Count = count; }
    }
    public readonly struct SynergyEvent
    {
        public string TraitId { get; }
        public string SourceId { get; }
        public string TargetId { get; }
        public string Detail { get; }
        public float Value { get; }
        public long Tick { get; }
        public SynergyEvent(string trait, string source, string target, string detail, float value, long tick)
        { TraitId = trait; SourceId = source; TargetId = target; Detail = detail; Value = value; Tick = tick; }
    }

    public sealed class SynergySystem
    {
        private sealed class Runtime
        {
            public float GuardianRemaining;
            public int Charges;
            public long ChargeEnd;
            public bool FirstSkill;
        }
        private readonly BattleState battle;
        private readonly SynergyRules rules;
        private readonly Dictionary<string, Runtime> runtime;
        private readonly Dictionary<int, IReadOnlyList<SynergyCount>> teams;
        private readonly List<SynergyEvent> events = new List<SynergyEvent>();
        private bool initialized;
        public IReadOnlyList<SynergyEvent> EventsThisTick => events.AsReadOnly();
        public static IReadOnlyList<SynergyCount> Count(IEnumerable<PokeChess.Core.Pokemon.PokemonDefinition> definitions)
        {
            var unique = definitions.GroupBy(d => d.Id, StringComparer.Ordinal).Select(g => g.First()).ToArray();
            return Array.AsReadOnly(SynergyTraits.All.Select(t => new SynergyCount(t, unique.Count(d => d.TraitIds.Contains(t)))).ToArray());
        }
        internal SynergySystem(BattleState battle, SynergyRules rules)
        {
            this.battle = battle; this.rules = rules ?? new SynergyRules();
            runtime = battle.Units.ToDictionary(u => u.UnitInstanceId, _ => new Runtime(), StringComparer.Ordinal);
            teams = new Dictionary<int, IReadOnlyList<SynergyCount>>();
            foreach (int team in new[] { 1, 2 })
            {
                var unique = battle.Units.Where(u => u.TeamId == team && u.IsOnBoard).GroupBy(u => u.DefinitionId, StringComparer.Ordinal).Select(g => g.First()).ToArray();
                teams[team] = Array.AsReadOnly(SynergyTraits.All.Select(t => new SynergyCount(t, unique.Count(u => u.TraitIds.Contains(t)))).ToArray());
            }
        }
        public IReadOnlyList<SynergyCount> TeamCounts(int team) => teams[team];
        public int Tier(UnitCombatState unit, string trait) => rules.Enabled && unit.TraitIds.Contains(trait) ? teams[unit.TeamId].First(c => c.TraitId == trait).Tier : 0;
        private void Record(string trait, UnitCombatState source, UnitCombatState target, string detail, float value) =>
            events.Add(new SynergyEvent(trait, source.UnitInstanceId, target.UnitInstanceId, detail, value, battle.CurrentTick));
        private float Shield(UnitCombatState target, float value)
        {
            float added = value * (battle.IsOvertime ? .34f : 1);
            double total = (double)target.CurrentShield + added;
            if (total > float.MaxValue) throw new OverflowException();
            target.SetShield((float)total); return added;
        }
        private void Buff(string id, UnitCombatState source, UnitCombatState target, CombatStat stat, float value, float duration)
        {
            battle.StatusEffects.Apply(new StatusEffectDefinition(id, duration, StatusTargetTeam.Ally,
                sourceRule: SourceStackRule.Shared,
                modifiers: new[] { new StatModifierDefinition(stat, StatModifierType.Percentage, value) }), source.UnitInstanceId, target.UnitInstanceId);
        }
        internal void Initialize()
        {
            if (initialized) return;
            initialized = true;
            foreach (var unit in battle.Units.OrderBy(u => u.UnitInstanceId, StringComparer.Ordinal))
            {
                int guardian = Tier(unit, SynergyTraits.Guardian), focus = Tier(unit, SynergyTraits.Focus);
                if (guardian > 0)
                {
                    float value = Shield(unit, guardian == 3 ? rules.GuardianShieldThree : rules.GuardianShieldTwo);
                    runtime[unit.UnitInstanceId].GuardianRemaining = value;
                    Record(SynergyTraits.Guardian, unit, unit, "SHIELD", value);
                }
                if (focus > 0)
                {
                    float before = unit.CurrentEnergy;
                    unit.SetEnergy(Math.Min(unit.Stats.MaxEnergy, before + (focus == 3 ? rules.FocusEnergyThree : rules.FocusEnergyTwo)));
                    Record(SynergyTraits.Focus, unit, unit, "ENERGY", unit.CurrentEnergy - before);
                }
            }
        }
        internal void BeginTick() => events.Clear();
        public int AssaultCharges(UnitCombatState unit) => battle.CurrentTick < runtime[unit.UnitInstanceId].ChargeEnd ? runtime[unit.UnitInstanceId].Charges : 0;
        internal float AttackBonus(UnitCombatState unit) => AssaultCharges(unit) > 0 ? unit.EffectiveStats.Attack * rules.AssaultFraction : 0;
        internal void OnBasicAttack(UnitCombatState unit, float bonus)
        {
            if (AssaultCharges(unit) <= 0) return;
            runtime[unit.UnitInstanceId].Charges--;
            Record(SynergyTraits.Assault, unit, unit, "STRIKE", bonus);
        }
        internal void OnDamage(UnitCombatState target, float absorbed)
        {
            var state = runtime[target.UnitInstanceId];
            if (state.GuardianRemaining <= 0) return;
            // The starting synergy shield is consumed before shields added by skills.
            state.GuardianRemaining = Math.Max(0, state.GuardianRemaining - absorbed);
            if (state.GuardianRemaining > 0 || target.CurrentHP <= 0 || !target.IsOnBoard || Tier(target, SynergyTraits.Guardian) < 3) return;
            Buff("synergy-guardian-armor", target, target, CombatStat.Armor, rules.GuardianResistance, rules.GuardianDuration);
            Buff("synergy-guardian-resistance", target, target, CombatStat.MagicResistance, rules.GuardianResistance, rules.GuardianDuration);
            Record(SynergyTraits.Guardian, target, target, "GUARD", rules.GuardianResistance);
        }
        internal void OnSkillEffect(UnitCombatState unit)
        {
            if (unit.CurrentHP <= 0 || !unit.IsOnBoard) return;
            var state = runtime[unit.UnitInstanceId];
            int assault = Tier(unit, SynergyTraits.Assault);
            if (assault > 0)
            {
                state.Charges = assault == 3 ? 2 : 1;
                state.ChargeEnd = checked(battle.CurrentTick + BattleSimulation.ToTicks(rules.AssaultDuration, battle.TickRate));
                Record(SynergyTraits.Assault, unit, unit, "READY", state.Charges);
            }
            if (state.FirstSkill) return;
            state.FirstSkill = true;
            if (Tier(unit, SynergyTraits.Focus) == 3)
            {
                Buff("synergy-focus", unit, unit, CombatStat.AttackSpeed, rules.FocusSpeed, rules.FocusDuration);
                Record(SynergyTraits.Focus, unit, unit, "HASTE", rules.FocusSpeed);
            }
            int link = Tier(unit, SynergyTraits.Link);
            if (link == 0) return;
            var ally = battle.Units.Where(u => u.TeamId == unit.TeamId && u.UnitInstanceId != unit.UnitInstanceId && u.CurrentHP > 0 && u.IsOnBoard)
                .OrderBy(u => HexCoordinates.Distance(unit.Position, u.Position)).ThenBy(u => u.UnitInstanceId, StringComparer.Ordinal).FirstOrDefault();
            if (ally == null) return;
            float shield = Shield(ally, link == 3 ? rules.LinkShieldThree : rules.LinkShieldTwo);
            if (link == 3) Buff("synergy-link", unit, ally, CombatStat.Attack, rules.LinkAttack, rules.LinkDuration);
            Record(SynergyTraits.Link, unit, ally, "LINK", shield);
        }
    }
}
