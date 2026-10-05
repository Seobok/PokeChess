using System;
using System.Collections.Generic;
using System.Linq;
using PokeChess.Core.Pokemon;
using PokeChess.Core.Board;

namespace PokeChess.Core.Battle
{
    public enum CombatActionState { Idle, Moving, Attacking, Casting, Dead }
    public enum BattleResult { InProgress, TeamOneWin, TeamTwoWin, Draw }
    public enum BattleEndReason { None, Elimination, TimeLimit, Surrender }

    public sealed class UnitCombatState
    {
        public string UnitInstanceId { get; }
        public string DefinitionId { get; }
        public string SkillId { get; }
        public string OwnerPlayerId { get; }
        public int TeamId { get; }
        public UnitRank Rank { get; }
        public int EvolutionStage { get; }
        public PokemonStats Stats { get; }
        internal StatusEffectSystem StatusSystem { get; set; }
        public EffectiveCombatStats EffectiveStats => StatusSystem.Resolve(this);
        public DamageModifiers EffectiveDamageModifiers => StatusSystem.ResolveDamageModifiers(this);
        public bool HasCrowdControl(CrowdControlKind kind) => StatusSystem.Has(this,kind);
        public string EffectiveTauntSource => StatusSystem.TauntSource(this) ?? TauntSourceId;
        public BoardPosition Position { get; private set; }
        public float CurrentHP { get; private set; }
        public float CurrentEnergy { get; private set; }
        public long EnergyLockUntilTick { get; internal set; }
        internal void SetEnergy(float value) { CurrentEnergy=ModelGuard.Number(value,nameof(value)); }
        public float CurrentShield { get; private set; }
        public DamageModifiers DamageModifiers { get; private set; } = new DamageModifiers();
        public void SetDamageModifiers(DamageModifiers modifiers)
        { DamageModifiers = modifiers ?? throw new ArgumentNullException(nameof(modifiers)); }
        public void SetShield(float shield) { CurrentShield = ModelGuard.Number(shield, nameof(shield)); }
        public bool IsAlive => CurrentHP > 0;
        public bool IsOnBoard { get; private set; } = true;
        public bool IsUntargetable { get; private set; }
        public string TauntSourceId { get; private set; }
        public bool IsTargetable => IsAlive && IsOnBoard && !IsUntargetable;
        // Status application/duration/stack rules are supplied by WBS 1.15.
        public void SetUntargetable(bool value) => IsUntargetable = value;
        public void SetTauntSource(string sourceId)
        {
            if(sourceId != null && string.IsNullOrWhiteSpace(sourceId))throw new ArgumentException("Invalid taunt source.");
            TauntSourceId = sourceId;
        }
        public string CurrentTargetId { get; internal set; }
        public CombatActionState ActionState { get; internal set; }
        public IReadOnlyList<string> ItemInstanceIds { get; }

        internal UnitCombatState(UnitInstance source, PokemonStats stats, int teamId, BoardPosition position, string skillId)
        {
            UnitInstanceId = source.InstanceId;
            DefinitionId = source.DefinitionId;
            SkillId = skillId;
            OwnerPlayerId = source.OwnerPlayerId;
            Rank = source.Rank;
            EvolutionStage = source.EvolutionStage;
            TeamId = teamId;
            Stats = stats;
            Position = position;
            CurrentHP = stats.MaxHP;
            CurrentEnergy = stats.StartingEnergy;
            ItemInstanceIds = Array.AsReadOnly(source.ItemInstanceIds.ToArray());
        }
        // No damage formula or command validation here; this only stores validated state.
        public void SetVitals(float hp, float energy)
        {
            ModelGuard.Number(hp, nameof(hp));
            ModelGuard.Number(energy, nameof(energy));
            if (hp > Stats.MaxHP) throw new ArgumentOutOfRangeException(nameof(hp));
            CurrentHP = hp;
            CurrentEnergy = energy; // Energy overflow is supported by the combat specification.
            if (!IsAlive) ActionState = CombatActionState.Dead;
        }
        internal void SetPosition(BoardPosition position) => Position = position;
        internal void MarkRemovedFromBoard() => IsOnBoard = false;
    }

    public sealed class BattleUnitSetup
    {
        public UnitInstance Unit { get; }
        public int TeamId { get; }
        public BoardPosition Position { get; }
        public BattleUnitSetup(UnitInstance unit, int teamId, BoardPosition position)
        {
            Unit = unit ?? throw new ArgumentNullException(nameof(unit));
            if (teamId != 1 && teamId != 2) throw new ArgumentOutOfRangeException(nameof(teamId));
            TeamId = teamId;
            Position = position;
        }
    }

    // Host-internal state; never serialize this directly as a client snapshot (Seed is private information).
    public sealed class BattleState
    {
        public string BattleId { get; }
        public int RoundNumber { get; }
        public ulong BattleSeed { get; }
        public PokeChess.Core.Random.BattleRandomStreams RngStreams { get; }
        public int TickRate { get; }
        public ProjectileSystem Projectiles { get; }
        public EnergySystem Energy { get; }
        public StatusEffectSystem StatusEffects { get; }
        private readonly List<SkillEvent> skillEvents = new List<SkillEvent>();
        public IReadOnlyList<SkillEvent> SkillEventsThisTick => Array.AsReadOnly(skillEvents.ToArray());
        private readonly List<SkillEffectEvent> skillEffectEvents = new List<SkillEffectEvent>();
        public IReadOnlyList<SkillEffectEvent> SkillEffectEventsThisTick => Array.AsReadOnly(skillEffectEvents.ToArray());
        internal void RecordSkillEffect(SkillEffectEvent signal) => skillEffectEvents.Add(signal);
        internal void BeginSkillTick() { skillEvents.Clear(); skillEffectEvents.Clear(); }
        internal void RecordSkill(SkillEvent signal) => skillEvents.Add(signal);
        internal bool HasSimulation { get; set; }
        public long CurrentTick { get; internal set; }
        public double ElapsedSeconds => (double)CurrentTick / TickRate;
        public bool IsOvertime { get; internal set; }
        public BattleResult Result { get; internal set; } = BattleResult.InProgress;
        public BattleEndReason EndReason { get; internal set; } = BattleEndReason.None;
        public IReadOnlyList<UnitCombatState> Units { get; }
        private readonly List<DamageResult> damageResults = new List<DamageResult>();
        public IReadOnlyList<DamageResult> DamageResultsThisTick => Array.AsReadOnly(damageResults.ToArray());
        internal void BeginDamageTick() => damageResults.Clear();
        internal void RecordDamage(DamageResult result) => damageResults.Add(result);
        private readonly CombatBoard board;
        private readonly Dictionary<string, UnitCombatState> unitsById;
        public IReadOnlyCombatBoard Board => board.ReadOnly;

        // Occupancy operations only: AI movement timing and death rules are implemented later.
        public BoardOperationResult TryMoveUnit(string unitId, BoardPosition destination)
        {
            var result = board.TryMove(unitId, destination);
            if (result == BoardOperationResult.Success) unitsById[unitId].SetPosition(destination);
            return result;
        }
        public BoardOperationResult TryRemoveUnit(string unitId)
        {
            var result = board.TryRemove(unitId);
            if (result == BoardOperationResult.Success) unitsById[unitId].MarkRemovedFromBoard();
            return result;
        }

        internal BattleState(string battleId, int roundNumber, ulong seed, int tickRate, UnitCombatState[] units, CombatBoard board)
        {
            BattleId = ModelGuard.Id(battleId, nameof(battleId));
            if (roundNumber < 1) throw new ArgumentOutOfRangeException(nameof(roundNumber));
            if (tickRate < 1) throw new ArgumentOutOfRangeException(nameof(tickRate));
            RoundNumber = roundNumber;
            BattleSeed = seed;
            RngStreams = new PokeChess.Core.Random.BattleRandomStreams(seed);
            TickRate = tickRate;
            Projectiles = new ProjectileSystem(this);
            Energy = new EnergySystem(this);
            StatusEffects = new StatusEffectSystem(this);
            foreach(var unit in units)unit.StatusSystem=StatusEffects;
            this.board = board ?? throw new ArgumentNullException(nameof(board));
            unitsById = units.ToDictionary(u => u.UnitInstanceId, StringComparer.Ordinal);
            Units = Array.AsReadOnly(units);
        }
    }

    public static class BattleStateFactory
    {
        public static BattleState Create(string battleId, int roundNumber, ulong seed, int tickRate,
            PokemonCatalog catalog, IEnumerable<BattleUnitSetup> setup,
            Func<UnitInstance, PokemonDefinition, PokemonStats> resolveStats = null)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (setup == null) throw new ArgumentNullException(nameof(setup));
            var entries = setup.ToArray();
            if (entries.Length == 0 || entries.Any(e => e == null)) throw new ArgumentException("Empty or invalid setup.");
            if (entries.Select(e => e.Unit.InstanceId).Distinct().Count() != entries.Length)
                throw new ArgumentException("Duplicate unit instance IDs.");
            if (!entries.Any(e => e.TeamId == 1) || !entries.Any(e => e.TeamId == 2))
                throw new ArgumentException("Both combat teams are required.");
            // Validate the entire setup before invoking stat policies or exposing a battle.
            var board = new CombatBoard();
            foreach (var entry in entries)
            {
                var placement = board.TryPlace(entry.Unit.InstanceId, entry.Position);
                if (placement != BoardOperationResult.Success)
                    throw new ArgumentException("Invalid initial placement: " + placement, nameof(setup));
            }
            var states = entries.Select(e =>
            {
                var definition = catalog.Get(e.Unit.DefinitionId);
                var stats = resolveStats == null ? definition.BaseStats : resolveStats(e.Unit, definition);
                if (stats == null) throw new ArgumentException("Stat resolver returned null.");
                return new UnitCombatState(e.Unit, stats, e.TeamId, e.Position, definition.SkillId);
            }).ToArray();
            return new BattleState(battleId, roundNumber, seed, tickRate, states, board);
        }
    }
}
