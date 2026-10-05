using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Match
{
    public sealed class PlayerState
    {
        private readonly List<UnitInstance> units = new List<UnitInstance>();
        private readonly ReadOnlyCollection<UnitInstance> unitView;
        private readonly Func<string, bool> matchContainsUnit;
        public string PlayerId { get; }
        public MatchRules Rules { get; }
        public int HP { get; private set; }
        public int Gold { get; private set; }
        // Progress within the current level; maximum level stores zero XP.
        public int XP { get; private set; }
        public int Level { get; private set; }
        public int WinStreak { get; private set; }
        public int LoseStreak { get; private set; }
        public int LastEconomyRound { get; private set; }
        public int LastAutomaticXPRound { get; private set; }
        public int BoardCapacity => Level;
        public ShopState Shop { get; }
        public IReadOnlyList<UnitInstance> Units => unitView;
        // Derived snapshots, not a second placement store. Bench includes empty slots.
        public IReadOnlyList<UnitInstance> Board => Array.AsReadOnly(units
            .Where(u => u.Placement.Kind == PlacementKind.Board)
            .OrderBy(u => u.Placement.Position.Value.Row)
            .ThenBy(u => u.Placement.Position.Value.Column).ToArray());
        public IReadOnlyList<UnitInstance> Bench
        {
            get
            {
                var slots = new UnitInstance[Rules.BenchCapacity];
                foreach (var unit in units)
                    if (unit.Placement.Kind == PlacementKind.Bench)
                        slots[unit.Placement.BenchSlot.Value] = unit;
                return Array.AsReadOnly(slots);
            }
        }

        internal PlayerState(string playerId, MatchRules rules, Func<string, bool> matchContainsUnit, ulong matchSeed)
        {
            PlayerId = ModelGuard.Id(playerId, nameof(playerId));
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            this.matchContainsUnit = matchContainsUnit ?? throw new ArgumentNullException(nameof(matchContainsUnit));
            Shop = new ShopState(matchSeed, PlayerId);
            HP = rules.StartingHP;
            Gold = rules.StartingGold;
            XP = rules.StartingXP;
            Level = rules.StartingLevel;
            unitView = units.AsReadOnly();
        }

        // Storage only: purchase, elimination and command phase checks belong to their services.
        public void SetHP(int hp)
        {
            if (hp < 0 || hp > Rules.StartingHP) throw new ArgumentOutOfRangeException(nameof(hp));
            HP = hp;
        }
        public void SetProgress(int gold, int xp, int level)
        {
            if (gold < 0) throw new ArgumentOutOfRangeException(nameof(gold));
            if (xp < 0) throw new ArgumentOutOfRangeException(nameof(xp));
            if (level < 1 || level > Rules.MaxLevel) throw new ArgumentOutOfRangeException(nameof(level));
            if (level == Rules.MaxLevel && xp != 0)
                throw new ArgumentException("Maximum level must have zero XP.", nameof(xp));
            if (units.Count(u => u.Placement.Kind == PlacementKind.Board) > level)
                throw new InvalidOperationException("Level cannot be below the deployed unit count.");
            Gold = gold;
            XP = xp;
            Level = level;
        }

        internal void ApplyLevelProgress(LevelProgress change, int? automaticRound = null)
        {
            SetProgress(change.GoldAfter, change.XPAfter, change.LevelAfter);
            if (automaticRound.HasValue) LastAutomaticXPRound = automaticRound.Value;
        }

        // Called only with a fully calculated, checked result by EconomySystem.
        internal void ApplyRoundIncome(RoundIncome income)
        {
            Gold = income.GoldAfter;
            WinStreak = income.WinStreak;
            LoseStreak = income.LoseStreak;
            LastEconomyRound = income.RoundNumber;
        }

        // Copy on registration prevents caller-owned or other-match aliases.
        // Use the returned instance for subsequent owned-state changes.
        public UnitInstance AddUnit(UnitInstance source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (!StringComparer.Ordinal.Equals(source.OwnerPlayerId, PlayerId))
                throw new ArgumentException("Unit owner does not match player.", nameof(source));
            if (matchContainsUnit(source.InstanceId))
                throw new ArgumentException("Duplicate unit identifier in match.", nameof(source));
            ValidatePlacement(source.InstanceId, source.Placement);
            var owned = new UnitInstance(source.InstanceId, source.DefinitionId, source.OwnerPlayerId,
                source.Rank, source.EvolutionStage, source.Placement, source.ItemInstanceIds);
            owned.SetPlacementValidation(placement => ValidatePlacement(owned.InstanceId, placement));
            units.Add(owned);
            return owned;
        }
        public UnitInstance GetUnit(string instanceId)
        {
            ModelGuard.Id(instanceId, nameof(instanceId));
            return units.FirstOrDefault(u => StringComparer.Ordinal.Equals(u.InstanceId, instanceId))
                ?? throw new KeyNotFoundException("Unknown owned unit: " + instanceId);
        }
        public void SetPlacement(string instanceId, UnitPlacement placement) => GetUnit(instanceId).SetPlacement(placement);
        public UnitInstance RemoveUnit(string instanceId)
        {
            var unit = GetUnit(instanceId);
            units.Remove(unit);
            unit.SetPlacementValidation(null);
            unit.SetPlacement(UnitPlacement.Unplaced);
            return unit;
        }

        private void ValidatePlacement(string instanceId, UnitPlacement placement)
        {
            switch (placement.Kind)
            {
                case PlacementKind.Unplaced: return;
                case PlacementKind.Board:
                    var position = placement.Position.Value;
                    if (position.Column >= Rules.BoardWidth || position.Row >= Rules.BoardHeight)
                        throw new ArgumentOutOfRangeException(nameof(placement), "Outside player board.");
                    if (units.Any(u => u.InstanceId != instanceId && u.Placement.Kind == PlacementKind.Board
                        && u.Placement.Position.Value.Equals(position)))
                        throw new InvalidOperationException("Board cell is occupied.");
                    if (units.Count(u => u.InstanceId != instanceId && u.Placement.Kind == PlacementKind.Board) >= BoardCapacity)
                        throw new InvalidOperationException("Board deployment limit reached.");
                    return;
                case PlacementKind.Bench:
                    if (placement.BenchSlot.Value >= Rules.BenchCapacity)
                        throw new ArgumentOutOfRangeException(nameof(placement), "Outside bench.");
                    if (units.Any(u => u.InstanceId != instanceId && u.Placement.Kind == PlacementKind.Bench
                        && u.Placement.BenchSlot == placement.BenchSlot))
                        throw new InvalidOperationException("Bench slot is occupied.");
                    return;
                default: throw new ArgumentOutOfRangeException(nameof(placement));
            }
        }
    }
}
