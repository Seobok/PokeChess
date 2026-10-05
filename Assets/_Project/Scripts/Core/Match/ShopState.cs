using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using PokeChess.Core.Random;

namespace PokeChess.Core.Match
{
    public sealed class ShopSlot
    {
        public int Index { get; }
        public string DefinitionId { get; }
        public int Cost { get; }
        public bool IsEmpty => DefinitionId == null;
        internal ShopSlot(int index, string definitionId = null, int cost = 0)
        { Index = index; DefinitionId = definitionId; Cost = cost; }
    }

    // Host-only RNG state. Expose filtered slot/lock DTOs to the owner, never the RNG.
    public sealed class ShopState
    {
        private ReadOnlyCollection<ShopSlot> slots;
        public IReadOnlyList<ShopSlot> Slots => slots;
        public bool IsInitialized { get; private set; }
        public bool IsLocked { get; private set; }
        public int LastRefreshRound { get; private set; }
        public ulong RandomState { get; private set; }
        public long RandomDrawCount { get; private set; }
        internal ShopState(ulong matchSeed, string playerId)
        {
            // Stable FNV-1a over UTF-16 bytes; independent of culture/runtime string hashing.
            ulong hash = 14695981039346656037UL;
            foreach (char c in playerId)
            {
                hash = unchecked((hash ^ (byte)c) * 1099511628211UL);
                hash = unchecked((hash ^ (byte)(c >> 8)) * 1099511628211UL);
            }
            RandomState = DeterministicRandom.Mix(matchSeed ^ hash ^ 0x53484F5000000001UL);
            var empty = new ShopSlot[ShopRules.SlotCount];
            for (int i=0;i<empty.Length;i++) empty[i] = new ShopSlot(i);
            slots = Array.AsReadOnly(empty);
        }
        internal void SetLocked(bool locked) => IsLocked = locked;
        internal void RecordLockedRefresh(int roundNumber) => LastRefreshRound = roundNumber;
        internal void Apply(ReadOnlyCollection<ShopSlot> replacement, ulong randomState, long drawCount, int? roundNumber)
        {
            slots = replacement;
            RandomState = randomState;
            RandomDrawCount = drawCount;
            IsInitialized = true;
            if (roundNumber.HasValue) LastRefreshRound = roundNumber.Value;
        }
    }
}
