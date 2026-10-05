using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PokeChess.Core.Pokemon
{
    public enum UnitRank { One = 1, Two = 2, Three = 3 }
    public enum PlacementKind { Unplaced, Board, Bench }

    // Offset storage only; axial conversion belongs to WBS 1.3.
    public readonly struct BoardPosition : IEquatable<BoardPosition>
    {
        public int Column { get; }
        public int Row { get; }
        public BoardPosition(int column, int row)
        {
            if (column < 0 || row < 0) throw new ArgumentOutOfRangeException();
            Column = column;
            Row = row;
        }
        public bool Equals(BoardPosition other) => Column == other.Column && Row == other.Row;
        public override bool Equals(object obj) => obj is BoardPosition other && Equals(other);
        public override int GetHashCode() => unchecked(Column * 397 ^ Row);
    }

    public readonly struct UnitPlacement
    {
        public PlacementKind Kind { get; }
        public BoardPosition? Position { get; }
        public int? BenchSlot { get; }
        private UnitPlacement(PlacementKind kind, BoardPosition? position, int? benchSlot)
        { Kind = kind; Position = position; BenchSlot = benchSlot; }
        public static UnitPlacement Unplaced => default;
        public static UnitPlacement OnBoard(BoardPosition position) => new UnitPlacement(PlacementKind.Board, position, null);
        public static UnitPlacement OnBench(int slot)
        {
            if (slot < 0) throw new ArgumentOutOfRangeException(nameof(slot));
            return new UnitPlacement(PlacementKind.Bench, null, slot);
        }
    }

    public sealed class UnitInstance
    {
        public string InstanceId { get; }
        public string DefinitionId { get; }
        public string OwnerPlayerId { get; }
        public UnitRank Rank { get; }
        public int EvolutionStage { get; }
        public UnitPlacement Placement { get; private set; }
        private readonly List<string> items;
        private readonly ReadOnlyCollection<string> itemView;
        public IReadOnlyList<string> ItemInstanceIds => itemView;

        internal UnitInstance(string instanceId, string definitionId, string ownerPlayerId,
            UnitRank rank, int evolutionStage, UnitPlacement placement, IEnumerable<string> itemIds)
        {
            InstanceId = ModelGuard.Id(instanceId, nameof(instanceId));
            DefinitionId = ModelGuard.Id(definitionId, nameof(definitionId));
            OwnerPlayerId = ModelGuard.Id(ownerPlayerId, nameof(ownerPlayerId));
            if (rank < UnitRank.One || rank > UnitRank.Three) throw new ArgumentOutOfRangeException(nameof(rank));
            if (evolutionStage < 0) throw new ArgumentOutOfRangeException(nameof(evolutionStage));
            Rank = rank;
            EvolutionStage = evolutionStage;
            Placement = placement;
            items = new List<string>(ModelGuard.Ids(itemIds, nameof(itemIds)));
            itemView = items.AsReadOnly();
        }
        // Storage operations only. Command ownership/phase/capacity validation comes later.
        private Action<UnitPlacement> placementValidation;
        internal void SetPlacementValidation(Action<UnitPlacement> validation) => placementValidation = validation;
        public void SetPlacement(UnitPlacement placement)
        {
            placementValidation?.Invoke(placement);
            Placement = placement;
        }
        public void AddItem(string itemInstanceId)
        {
            ModelGuard.Id(itemInstanceId, nameof(itemInstanceId));
            if (items.Contains(itemInstanceId)) throw new ArgumentException("Item already equipped.");
            items.Add(itemInstanceId);
        }
    }
}
