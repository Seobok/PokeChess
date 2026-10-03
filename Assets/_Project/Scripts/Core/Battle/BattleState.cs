using System;
using System.Collections.Generic;
using System.Linq;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Battle
{
    public enum CombatActionState { Idle, Moving, Attacking, Casting, Dead }
    public enum BattleResult { InProgress, TeamOneWin, TeamTwoWin, Draw }
    public enum BattleEndReason { None, Elimination, TimeLimit, Surrender }

    public sealed class UnitCombatState
    {
        public string UnitInstanceId { get; }
        public string DefinitionId { get; }
        public string OwnerPlayerId { get; }
        public int TeamId { get; }
        public UnitRank Rank { get; }
        public int EvolutionStage { get; }
        public PokemonStats Stats { get; }
        public BoardPosition Position { get; private set; }
        public float CurrentHP { get; private set; }
        public float CurrentEnergy { get; private set; }
        public bool IsAlive => CurrentHP > 0;
        public bool IsTargetable => IsAlive;
        public string CurrentTargetId { get; internal set; }
        public CombatActionState ActionState { get; internal set; }
        public IReadOnlyList<string> ItemInstanceIds { get; }

        internal UnitCombatState(UnitInstance source, PokemonStats stats, int teamId, BoardPosition position)
        {
            UnitInstanceId = source.InstanceId;
            DefinitionId = source.DefinitionId;
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
        public void SetPosition(BoardPosition position) => Position = position;
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
        public int TickRate { get; }
        public long CurrentTick { get; internal set; }
        public double ElapsedSeconds => (double)CurrentTick / TickRate;
        public bool IsOvertime { get; internal set; }
        public BattleResult Result { get; internal set; } = BattleResult.InProgress;
        public BattleEndReason EndReason { get; internal set; } = BattleEndReason.None;
        public IReadOnlyList<UnitCombatState> Units { get; }

        internal BattleState(string battleId, int roundNumber, ulong seed, int tickRate, UnitCombatState[] units)
        {
            BattleId = ModelGuard.Id(battleId, nameof(battleId));
            if (roundNumber < 1) throw new ArgumentOutOfRangeException(nameof(roundNumber));
            if (tickRate < 1) throw new ArgumentOutOfRangeException(nameof(tickRate));
            RoundNumber = roundNumber;
            BattleSeed = seed;
            TickRate = tickRate;
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
            var states = entries.Select(e =>
            {
                var definition = catalog.Get(e.Unit.DefinitionId);
                var stats = resolveStats == null ? definition.BaseStats : resolveStats(e.Unit, definition);
                if (stats == null) throw new ArgumentException("Stat resolver returned null.");
                return new UnitCombatState(e.Unit, stats, e.TeamId, e.Position);
            }).ToArray();
            return new BattleState(battleId, roundNumber, seed, tickRate, states);
        }
    }
}
