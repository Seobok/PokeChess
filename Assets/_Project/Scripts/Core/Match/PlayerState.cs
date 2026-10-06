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
        private readonly List<string> inventoryItems = new List<string>();
        public IReadOnlyList<string> ItemInventory => inventoryItems.AsReadOnly();
        public void AddInventoryItem(string itemId)
        {
            ModelGuard.Id(itemId, nameof(itemId));
            if (inventoryItems.Contains(itemId) || units.Any(u => u.ItemInstanceIds.Contains(itemId)))
                throw new InvalidOperationException("Item is already owned.");
            inventoryItems.Add(itemId);
        }
        internal void ReturnSoldItems(UnitInstance unit)
        {
            inventoryItems.AddRange(unit.ItemInstanceIds);
            unit.ClearItems();
        }
        public string PlayerId { get; }
        public MatchRules Rules { get; }
        public int HP { get; private set; }
        public bool IsEliminated { get; private set; }
        public PlayerElimination Elimination { get; private set; }
        private bool isMatchWinner;
        public int? FinalPlacement => isMatchWinner ? 1 : Elimination?.Placement;
        internal void MarkMatchWinner() => isMatchWinner=true;
        internal void RecordElimination(PlayerElimination record) => Elimination=record;
        internal void MarkEliminated() => IsEliminated = true;
        public int Gold { get; private set; }
        // Progress within the current level; maximum level stores zero XP.
        public int XP { get; private set; }
        public int Level { get; private set; }
        public int WinStreak { get; private set; }
        public int LoseStreak { get; private set; }
        public int LastEconomyRound { get; private set; }
        public int LastAutomaticXPRound { get; private set; }
        public int LastDamageRound { get; private set; }
        public PlayerDamageResult LastDamage { get; private set; }
        internal void ApplyDamage(PlayerDamageResult plan)
        {
            if(plan.PlayerId!=PlayerId)throw new InvalidOperationException("Wrong damage recipient.");
            if(LastDamageRound==plan.Round)
            {
                if(!ReferenceEquals(LastDamage,plan))throw new InvalidOperationException("Conflicting damage application.");
                return;
            }
            if((long)LastDamageRound+1!=plan.Round || HP!=plan.HPBefore)throw new InvalidOperationException("Stale damage plan.");
            SetHP(plan.HPAfter);LastDamage=plan;LastDamageRound=plan.Round;
        }
        public long PlacementRevision { get; private set; }
        internal void ValidatePlacementChanges(int count) { checked { var next = PlacementRevision + count; } }
        internal static bool SamePlacement(UnitPlacement a, UnitPlacement b) => a.Kind == b.Kind
            && Nullable.Equals(a.Position, b.Position) && a.BenchSlot == b.BenchSlot;
        // Validate both final placements before writing either, preserving owned references.
        internal void ApplyPlacements(IReadOnlyDictionary<string, UnitPlacement> replacements)
        {
            foreach (var id in replacements.Keys) GetUnit(id);
            var board = new HashSet<BoardPosition>();
            var bench = new HashSet<int>();
            bool changed = false;
            foreach (var unit in units)
            {
                var final = replacements.TryGetValue(unit.InstanceId, out var requested) ? requested : unit.Placement;
                changed |= !SamePlacement(unit.Placement, final);
                switch (final.Kind)
                {
                    case PlacementKind.Board:
                        GetBoardUnit(final.Position.Value);
                        if (!board.Add(final.Position.Value)) throw new InvalidOperationException("Duplicate final board cell.");
                        break;
                    case PlacementKind.Bench:
                        GetBenchUnit(final.BenchSlot.Value);
                        if (!bench.Add(final.BenchSlot.Value)) throw new InvalidOperationException("Duplicate final bench slot.");
                        break;
                    case PlacementKind.Unplaced: break;
                    default: throw new ArgumentOutOfRangeException(nameof(replacements));
                }
            }
            if (board.Count > BoardCapacity) throw new InvalidOperationException("Board deployment limit reached.");
            if (!changed) return;
            long revision = checked(PlacementRevision + 1);
            foreach (var pair in replacements) GetUnit(pair.Key).ApplyPlacementUnchecked(pair.Value);
            PlacementRevision = revision;
        }
        internal long ValidateRankUp(RankUpPlan plan)
        {
            if(!units.SequenceEqual(plan.Before)) throw new InvalidOperationException("Stale rank-up plan.");
            if(plan.Added!=null && (plan.Added.OwnerPlayerId!=PlayerId || matchContainsUnit(plan.Added.InstanceId)))
                throw new InvalidOperationException("Invalid acquired unit identity.");
            var board=new HashSet<BoardPosition>();var bench=new HashSet<int>();var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var node in plan.Final)
            {
                if(!ids.Add(node.Unit.InstanceId)) throw new InvalidOperationException("Duplicate result identity.");
                if(node.Placement.Kind==PlacementKind.Board) { GetBoardUnit(node.Placement.Position.Value);if(!board.Add(node.Placement.Position.Value)) throw new InvalidOperationException("Duplicate board cell."); }
                else if(node.Placement.Kind==PlacementKind.Bench) { GetBenchUnit(node.Placement.BenchSlot.Value);if(!bench.Add(node.Placement.BenchSlot.Value)) throw new InvalidOperationException("Duplicate bench slot."); }
            }
            if(board.Count>BoardCapacity) throw new InvalidOperationException("Deployment cap exceeded.");
            var all=plan.Before.Concat(plan.Added==null ? Array.Empty<UnitInstance>() : new[]{plan.Added});
            var items=new HashSet<string>(inventoryItems,StringComparer.Ordinal);
            foreach(var item in all.SelectMany(u=>u.ItemInstanceIds))
                if(!items.Add(item)) throw new InvalidOperationException("Duplicate item ownership.");
            return plan.Changed ? checked(PlacementRevision+1) : PlacementRevision;
        }
        internal void ApplyRankUp(RankUpPlan plan,long revision)
        {
            var survivors=new HashSet<UnitInstance>(plan.Final.Select(n=>n.Unit));
            foreach(var unit in plan.Events.SelectMany(e=>e.ConsumedUnitIds)
                .Select(id=>plan.Before.FirstOrDefault(u=>u.InstanceId==id)).Where(u=>u!=null))
            {
                units.Remove(unit);unit.SetPlacementValidation(null);unit.ApplyPlacementUnchecked(UnitPlacement.Unplaced);ReturnSoldItems(unit);
            }
            if(plan.Added!=null && survivors.Contains(plan.Added))
            {
                var owned=plan.Added;
                owned.SetPlacementValidation(placement=> { ValidatePlacement(owned.InstanceId,placement);if(!SamePlacement(owned.Placement,placement)) PlacementRevision=checked(PlacementRevision+1); });
                units.Add(owned);
            }
            foreach(var node in plan.Final) { node.Unit.ApplyRankUnchecked(node.Rank);node.Unit.ApplyPlacementUnchecked(node.Placement); }
            PlacementRevision=revision;
        }
        public int BoardCapacity => Level;
        public int DeployedUnitCount => units.Count(u => u.Placement.Kind == PlacementKind.Board);
        public int RemainingDeploymentCapacity => BoardCapacity - DeployedUnitCount;
        // Includes every cell, row first, then column. Placement remains the only store.
        public IReadOnlyList<PlayerBoardCell> BoardCells
        {
            get
            {
                var cells = new PlayerBoardCell[checked(Rules.BoardWidth * Rules.BoardHeight)];
                for (int row = 0; row < Rules.BoardHeight; row++)
                    for (int column = 0; column < Rules.BoardWidth; column++)
                    {
                        var position = new BoardPosition(column, row);
                        cells[row * Rules.BoardWidth + column] = new PlayerBoardCell(position, GetBoardUnit(position));
                    }
                return Array.AsReadOnly(cells);
            }
        }
        public UnitInstance GetBoardUnit(BoardPosition position)
        {
            if (position.Column >= Rules.BoardWidth || position.Row >= Rules.BoardHeight)
                throw new ArgumentOutOfRangeException(nameof(position), "Outside player board.");
            return units.FirstOrDefault(u => u.Placement.Kind == PlacementKind.Board && u.Placement.Position.Value.Equals(position));
        }
        public UnitInstance GetBenchUnit(int slot)
        {
            if (slot < 0 || slot >= Rules.BenchCapacity)
                throw new ArgumentOutOfRangeException(nameof(slot), "Outside bench.");
            return units.FirstOrDefault(u => u.Placement.Kind == PlacementKind.Bench && u.Placement.BenchSlot.Value == slot);
        }
        // null means every bench slot is occupied. The lowest free index is preferred.
        public int? FindFirstEmptyBenchSlot()
        {
            for (int slot = 0; slot < Rules.BenchCapacity; slot++)
                if (GetBenchUnit(slot) == null) return slot;
            return null;
        }
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
            if (IsEliminated && hp != 0) throw new InvalidOperationException("Elimination is terminal.");
            HP = hp;
        }
        public void SetProgress(int gold, int xp, int level)
        {
            if (gold < 0) throw new ArgumentOutOfRangeException(nameof(gold));
            if (xp < 0) throw new ArgumentOutOfRangeException(nameof(xp));
            if (level < 1 || level > Rules.MaxLevel) throw new ArgumentOutOfRangeException(nameof(level));
            if (level == Rules.MaxLevel && xp != 0)
                throw new ArgumentException("Maximum level must have zero XP.", nameof(xp));
            if (DeployedUnitCount > level)
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
            owned.SetPoolOrigin(source.PoolOriginDefinitionId);
            long revision = checked(PlacementRevision + 1);
            owned.SetPlacementValidation(placement =>
            {
                ValidatePlacement(owned.InstanceId, placement);
                if (!SamePlacement(owned.Placement, placement)) PlacementRevision = checked(PlacementRevision + 1);
            });
            units.Add(owned);
            PlacementRevision = revision;
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
            long revision = checked(PlacementRevision + 1);
            units.Remove(unit);
            unit.SetPlacementValidation(null);
            unit.SetPlacement(UnitPlacement.Unplaced);
            PlacementRevision = revision;
            return unit;
        }

        private void ValidatePlacement(string instanceId, UnitPlacement placement)
        {
            switch (placement.Kind)
            {
                case PlacementKind.Unplaced: return;
                case PlacementKind.Board:
                    var position = placement.Position.Value;
                    var boardOccupant = GetBoardUnit(position);
                    if (boardOccupant != null && boardOccupant.InstanceId != instanceId)
                        throw new InvalidOperationException("Board cell is occupied.");
                    if (units.Count(u => u.InstanceId != instanceId && u.Placement.Kind == PlacementKind.Board) >= BoardCapacity)
                        throw new InvalidOperationException("Board deployment limit reached.");
                    return;
                case PlacementKind.Bench:
                    var benchOccupant = GetBenchUnit(placement.BenchSlot.Value);
                    if (benchOccupant != null && benchOccupant.InstanceId != instanceId)
                        throw new InvalidOperationException("Bench slot is occupied.");
                    return;
                default: throw new ArgumentOutOfRangeException(nameof(placement));
            }
        }
    }
}

