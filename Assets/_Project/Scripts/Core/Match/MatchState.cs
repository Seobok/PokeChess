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
        public SharedPokemonPool Pool { get; private set; }
        internal void InitializePool(SharedPokemonPool pool) => Pool = pool;
        public IReadOnlyList<PlayerState> Players => playerView;
        public int RoundNumber { get; private set; }
        public MatchPhase Phase { get; private set; } = MatchPhase.Waiting;
        public MatchFinalResult FinalResult { get; private set; }
        internal void CommitFinalResult(MatchFinalResult result)
        {
            if(FinalResult!=null)throw new InvalidOperationException("Final result already committed.");
            if(result.MatchId!=MatchId || result.EndRound!=RoundNumber || (Phase!=MatchPhase.Preparation && Phase!=MatchPhase.Result))throw new InvalidOperationException("Stale final result.");
            TransitionTo(MatchPhase.Finished);
            if(result.Reason==MatchEndReason.LastPlayerAlive)GetPlayer(result.WinnerPlayerId).MarkMatchWinner();
            FinalResult=result;
        }
        private readonly Dictionary<int,RoundPairingPlan> pairingHistory = new Dictionary<int,RoundPairingPlan>();
        public RoundPairingPlan PairingPlan { get; private set; }
        public IReadOnlyList<RoundPairing> Pairings => PairingPlan?.Pairs ?? Array.Empty<RoundPairing>();
        public IReadOnlyDictionary<int,RoundPairingPlan> PairingHistory => new ReadOnlyDictionary<int,RoundPairingPlan>(pairingHistory);
        internal void CommitPairings(RoundPairingPlan plan)
        {
            if (Phase != MatchPhase.Preparation || plan.Round != RoundNumber) throw new InvalidOperationException("Stale pairing plan.");
            PairingPlan=plan;pairingHistory[plan.Round]=plan;
            foreach(var round in pairingHistory.Keys.Where(r=>r<RoundNumber-2).ToArray())pairingHistory.Remove(round);
        }

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
        public static MatchState CreateWithPool(string matchId, IEnumerable<string> playerIds,
            PokemonCatalog catalog, IEnumerable<string> shopDefinitionIds, PoolRules poolRules = null,
            MatchRules rules = null, ulong matchSeed = 0)
        {
            var match = Create(matchId, playerIds, rules, matchSeed);
            match.InitializePool(new SharedPokemonPool(catalog, shopDefinitionIds, poolRules));
            return match;
        }
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

