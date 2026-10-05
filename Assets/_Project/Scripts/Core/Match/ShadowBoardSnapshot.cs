using System;
using System.Collections.Generic;
using System.Linq;
using PokeChess.Core.Battle;
using PokeChess.Core.Board;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Match
{
    public sealed class ShadowUnitSnapshot
    {
        public string UnitInstanceId { get; }
        public string DefinitionId { get; }
        public UnitRank Rank { get; }
        public int EvolutionStage { get; }
        public BoardPosition Position { get; }
        public PokemonStats Stats { get; }
        public IReadOnlyList<string> ItemInstanceIds { get; }
        internal ShadowUnitSnapshot(UnitInstance unit,BoardPosition position,PokemonStats stats)
        {
            UnitInstanceId=unit.InstanceId;DefinitionId=unit.DefinitionId;Rank=unit.Rank;
            EvolutionStage=unit.EvolutionStage;Position=position;Stats=stats;
            ItemInstanceIds=Array.AsReadOnly(unit.ItemInstanceIds.ToArray());
        }
        internal BattleUnitSetup CreateSetup(string owner)
        {
            var unit=new UnitInstance(UnitInstanceId,DefinitionId,owner,Rank,EvolutionStage,UnitPlacement.OnBoard(Position),ItemInstanceIds);
            return new BattleUnitSetup(unit,2,HexCoordinates.MirrorCombat(Position));
        }
    }

    // Detached and immutable: can survive source elimination and be used by a later surrender handler.
    // Never register these copies in PlayerState or the shared pool.
    public sealed class ShadowBoardSnapshot
    {
        public string SourcePlayerId { get; }
        public int Round { get; }
        public IReadOnlyList<ShadowUnitSnapshot> Units { get; }
        private ShadowBoardSnapshot(string source,int round,ShadowUnitSnapshot[] units)
        { SourcePlayerId=source;Round=round;Units=Array.AsReadOnly(units); }

        public static ShadowBoardSnapshot Capture(PlayerState source,int round,PokemonCatalog catalog)
            => Capture(source,round,catalog,new Dictionary<string,UnitPlacement>());
        internal static ShadowBoardSnapshot Capture(PlayerState source,int round,PokemonCatalog catalog,IReadOnlyDictionary<string,UnitPlacement> deployments)
        {
            if(source==null)throw new ArgumentNullException(nameof(source));
            if(catalog==null)throw new ArgumentNullException(nameof(catalog));
            if(round<1)throw new ArgumentOutOfRangeException(nameof(round));
            var units=source.Units.Where(u=>u.Placement.Kind==PlacementKind.Board || deployments.ContainsKey(u.InstanceId))
                .Select(u=>new ShadowUnitSnapshot(u,deployments.TryGetValue(u.InstanceId,out var placement) ? placement.Position.Value : u.Placement.Position.Value,
                    RankRules.Default.Apply(catalog.Get(u.DefinitionId).BaseStats,u.Rank))).ToArray();
            return new ShadowBoardSnapshot(source.PlayerId,round,units);
        }
        public BattleState CreateBattle(string battleId,ulong seed,int tickRate,PokemonCatalog catalog,IEnumerable<BattleUnitSetup> challenger)
        {
            if(challenger==null)throw new ArgumentNullException(nameof(challenger));
            var entries=challenger.ToArray();
            if(entries.Any(e=>e==null || e.TeamId!=1 || e.Unit.OwnerPlayerId==SourcePlayerId))
                throw new ArgumentException("Shadow challenger must be a different player's team one.",nameof(challenger));
            var stats=Units.ToDictionary(u=>u.UnitInstanceId,u=>u.Stats,StringComparer.Ordinal);
            return BattleStateFactory.Create(battleId,Round,seed,tickRate,catalog,entries.Concat(Units.Select(u=>u.CreateSetup(SourcePlayerId))),
                (unit,definition)=>unit.OwnerPlayerId==SourcePlayerId ? stats[unit.InstanceId] : RankRules.Default.Apply(definition.BaseStats,unit.Rank));
        }
    }
}

