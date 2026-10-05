using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Match
{
    public enum MatchPhase { Waiting, Starting, Preparation, Combat, Result, Finished }

    // Host-authoritative model. Project into filtered DTOs before network transmission.
    public sealed class MatchState
    {
        private readonly List<PlayerState> players = new List<PlayerState>();
        private readonly ReadOnlyCollection<PlayerState> playerView;
        private long unitSequence;
        internal string NextUnitId(out long sequence)
        {
            sequence = unitSequence;
            string id;
            do { sequence = checked(sequence + 1); id = "unit-" + sequence.ToString(System.Globalization.CultureInfo.InvariantCulture); }
            while (players.Any(p => p.Units.Any(u => u.InstanceId == id)));
            return id;
        }
        internal void CommitUnitSequence(long sequence) => unitSequence = sequence;
        public string MatchId { get; }
        public ulong MatchSeed { get; }
        public MatchRules Rules { get; }
        public IReadOnlyList<PlayerState> Players => playerView;
        public int RoundNumber { get; private set; }
        public MatchPhase Phase { get; private set; } = MatchPhase.Waiting;

        internal MatchState(string matchId, MatchRules rules, IReadOnlyList<string> playerIds, ulong matchSeed)
        {
            MatchId = ModelGuard.Id(matchId, nameof(matchId));
            Rules = rules;
            MatchSeed = matchSeed;
            playerView = players.AsReadOnly();
            foreach (var id in playerIds)
                players.Add(new PlayerState(id, rules,
                    unitId => players.Any(p => p.Units.Any(u => u.InstanceId == unitId)), matchSeed));
        }
        public PlayerState GetPlayer(string playerId)
        {
            ModelGuard.Id(playerId, nameof(playerId));
            return players.FirstOrDefault(p => StringComparer.Ordinal.Equals(p.PlayerId, playerId))
                ?? throw new KeyNotFoundException("Unknown player: " + playerId);
        }

        // Explicit storage transitions; timers, battle completion and winner checks are caller responsibilities.
        public void TransitionTo(MatchPhase next)
        {
            if (!Enum.IsDefined(typeof(MatchPhase), next)) throw new ArgumentOutOfRangeException(nameof(next));
            bool allowed = (Phase == MatchPhase.Waiting && next == MatchPhase.Starting)
                || (Phase == MatchPhase.Starting && next == MatchPhase.Preparation)
                || (Phase == MatchPhase.Preparation && next == MatchPhase.Combat)
                || (Phase == MatchPhase.Combat && next == MatchPhase.Result)
                || (Phase == MatchPhase.Result && next == MatchPhase.Preparation)
                || (Phase != MatchPhase.Waiting && Phase != MatchPhase.Finished && next == MatchPhase.Finished);
            if (!allowed) throw new InvalidOperationException("Invalid match phase transition.");
            int round = RoundNumber;
            if (next == MatchPhase.Preparation) round = checked(round + 1);
            RoundNumber = round;
            Phase = next;
        }
    }

    public static class MatchStateFactory
    {
        public static MatchState Create(string matchId, IEnumerable<string> playerIds, MatchRules rules = null, ulong matchSeed = 0)
        {
            ModelGuard.Id(matchId, nameof(matchId));
            if (playerIds == null) throw new ArgumentNullException(nameof(playerIds));
            var ids = ModelGuard.Ids(playerIds, nameof(playerIds));
            if (ids.Count < 2 || ids.Count > 8)
                throw new ArgumentException("A match requires 2 to 8 players.", nameof(playerIds));
            return new MatchState(matchId, rules ?? new MatchRules(), ids, matchSeed);
        }
    }
}
