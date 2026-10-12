using System;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Match
{
    public sealed class ShopEvolution
    {
        public string RootDefinitionId { get; }
        public string DefinitionId { get; }
        public int Stage { get; }
        internal ShopEvolution(string root, string definition, int stage)
        { RootDefinitionId = root; DefinitionId = definition; Stage = stage; }
    }

    public static class EvolutionPointRules
    {
        public const int RoundInterval = 5;
        public const int PointsPerGrant = 1;
        public static int NextGrantRound(int round) => checked((round / RoundInterval + 1) * RoundInterval);
        // Host preparation hook; safe to retry after a partially completed round refresh.
        public static bool GrantForPreparation(MatchState match, string playerId)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            if (match.Phase != MatchPhase.Preparation) throw new InvalidOperationException("Requires Preparation.");
            var player = match.GetPlayer(playerId);
            if (player.HP == 0 || player.IsEliminated || match.RoundNumber % RoundInterval != 0 ||
                player.LastEvolutionPointRound >= match.RoundNumber) return false;
            player.GrantEvolutionPoints(match.RoundNumber, PointsPerGrant);
            return true;
        }
    }

    public sealed class EvolutionSystem
    {
        private readonly PokemonCatalog catalog;
        public EvolutionSystem(PokemonCatalog catalog)
        { this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog)); }

        public PokemonDefinition Preview(MatchState match, string playerId, string unitId,
            long expectedEvolutionRevision, long expectedPlacementRevision)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            var player = match.GetPlayer(playerId);
            if (player.HP == 0 || player.IsEliminated) throw new InvalidOperationException("Player is eliminated.");
            if (match.Phase != MatchPhase.Preparation) throw new InvalidOperationException("Requires Preparation.");
            if (player.EvolutionRevision != expectedEvolutionRevision || player.PlacementRevision != expectedPlacementRevision)
                throw new InvalidOperationException("Stale evolution state.");
            var unit = player.GetUnit(unitId);
            if (unit.Placement.Kind != PlacementKind.Board && unit.Placement.Kind != PlacementKind.Bench)
                throw new InvalidOperationException("Only board or bench units can evolve.");
            var definition = catalog.Get(unit.DefinitionId);
            if (definition.Evolutions.Count != 1) throw new InvalidOperationException("No single-path evolution available.");
            var option = definition.Evolutions[0];
            if (option.EvolutionPointCost < 1) throw new InvalidOperationException("Evolution requires a positive EP cost.");
            if (player.EvolutionPoints < option.EvolutionPointCost) throw new InvalidOperationException("Not enough evolution points.");
            var target = catalog.Get(option.TargetDefinitionId);
            var root = catalog.EvolutionRoot(unit.DefinitionId, out int stage);
            if (catalog.EvolutionRoot(target.Id, out int targetStage) != root || targetStage != stage + 1 ||
                unit.EvolutionStage != stage || target.Cost != definition.Cost)
                throw new InvalidOperationException("Invalid evolution lineage.");
            checked { var nextRevision = player.EvolutionRevision + 1; var placement = player.PlacementRevision + 1; }
            if (match.Pool != null) match.Pool.ValidateRelease(new[] { unit });
            else if (unit.PoolOriginDefinitionId != null) throw new InvalidOperationException("Pool-origin unit has no owning pool.");
            return target;
        }

        public UnitInstance Evolve(MatchState match, string playerId, string unitId,
            long expectedEvolutionRevision, long expectedPlacementRevision)
        {
            var target = Preview(match, playerId, unitId, expectedEvolutionRevision, expectedPlacementRevision);
            var player = match.GetPlayer(playerId); var unit = player.GetUnit(unitId);
            int cost = catalog.Get(unit.DefinitionId).Evolutions[0].EvolutionPointCost;
            string root = catalog.EvolutionRoot(target.Id, out int stage);
            // Keep the owned instance, rank, placement, items and pool ledger intact.
            player.CommitEvolution(unit, target, root, stage, cost);
            return unit;
        }
    }
}
