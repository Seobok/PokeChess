using System;
using System.Collections.Generic;
using System.Linq;
using PokeChess.Core.Battle;
using PokeChess.Core.Pokemon;
using PokeChess.Core.Board;
namespace PokeChess.Core.Match
{
    [Serializable]
    public struct AdversitySettlement
    {
        public int round, resolveDelta, gold;
        public bool berry, choice;
    }

    [Serializable]
    public struct AdversityChoice
    {
        public int sequence, slot;
    }

    public sealed class SynergyProgress
    {
        public int VenomSamples { get; internal set; }
        public int VenomMinimumRewardRound { get; internal set; }
        public int MysticTier { get; internal set; }
        public int MysticBattles { get; internal set; }
        public int Resolve { get; internal set; }
        public AdversitySettlement LastAdversitySettlement { get; internal set; }
        public AdversityChoice LastAdversityChoice { get; internal set; }
        // Earliest usable round. Zero means no stored reroll.
        public int FreeRerollRound { get; internal set; }
        public int FreeRerollConsumedRound { get; internal set; }
        public static bool IsFreeRerollAvailable(int availableFromRound, int round) =>
            availableFromRound > 0 && availableFromRound <= round;
        public int SettledRound { get; internal set; }
        public int EquipmentFragments { get; internal set; }
        public int EmblemFragments { get; internal set; }
        public int RewardChoices { get; internal set; }
        public int PendingGold { get; internal set; }
        public string ProphecyOrigin { get; internal set; }
        public BoardPosition? GuardianTile { get; internal set; }
        public BoardPosition[] RockTiles { get; internal set; } = Array.Empty<BoardPosition>();
    }
    public static class SynergyPreparation
    {
        public const int GuardianPlacementMode = 4;

        // mode 0: retired manual target selection, 1: rock slot, 2: prophecy, 3: adversity reward.
        public static void Configure(MatchState match,PokemonCatalog catalog,string playerId,int mode,string unitId,string value,int slot=0,int column=0,int row=0)
        {
            var player=match.GetPlayer(playerId);
            if(match.Phase!=MatchPhase.Preparation||player.IsEliminated||player.HP==0)throw new InvalidOperationException("준비 단계에서 설정할 수 있습니다.");
            if(mode==0)
                throw new ArgumentException("텍스처는 폴리곤 양옆 유닛의 공통 계열로 자동 결정됩니다. 승객은 라프라스 오른쪽에 배치하세요.");
            else if(mode==1 || mode==GuardianPlacementMode)
                MoveSpecial(player,catalog,mode,slot,new BoardPosition(column,row));
            else if(mode==2)
            {if(!SynergyTraits.Origins.Contains(value))throw new ArgumentException("계열을 선택하세요.");player.Synergy.ProphecyOrigin=value;}
            else if(mode==3)
            {
                if(player.Synergy.RewardChoices==0||slot<0||slot>2)throw new InvalidOperationException("선택할 보상이 없습니다.");
                int sequence = checked(player.Synergy.LastAdversityChoice.sequence + 1);
                int share=player.Synergy.PendingGold/player.Synergy.RewardChoices;
                if(slot==0)player.SetProgress(checked(player.Gold+share),player.XP,player.Level);
                else if(slot==1)player.Synergy.EquipmentFragments=checked(player.Synergy.EquipmentFragments+1);
                else player.Synergy.EmblemFragments=checked(player.Synergy.EmblemFragments+1);
                player.Synergy.RewardChoices--;player.Synergy.PendingGold-=share;
                player.Synergy.LastAdversityChoice = new AdversityChoice { sequence = sequence, slot = slot };
            }
            else throw new ArgumentOutOfRangeException(nameof(mode));
        }
        internal static void MoveSpecial(PlayerState player, PokemonCatalog catalog, int mode, int slot, BoardPosition destination)
        {
            if (!HexBoardBounds.Preparation.Contains(destination))
                throw new ArgumentException("아군 필드에 배치하세요.");

            var rocks = PreparationRocks(player, catalog);
            var guardian = PreparationGuardian(player, catalog);
            bool movingGuardian = mode == GuardianPlacementMode;
            BoardPosition? source = movingGuardian ? guardian
                : slot >= 0 && slot < rocks.Length ? rocks[slot] : (BoardPosition?)null;
            if (!source.HasValue) throw new ArgumentException("이동할 시너지 오브젝트가 없습니다.");
            if (source.Value.Equals(destination)) return;

            var target = player.GetBoardUnit(destination);
            int targetRock = Array.IndexOf(rocks, destination);
            if (targetRock >= 0) rocks[targetRock] = source.Value;
            if (Nullable.Equals(guardian, destination)) guardian = source.Value;
            if (movingGuardian) guardian = destination;
            else rocks[slot] = destination;

            if (target != null)
                player.ApplyPlacements(new Dictionary<string, UnitPlacement>
                    { [target.InstanceId] = UnitPlacement.OnBoard(source.Value) });
            else
                player.AdvancePlacementRevision();

            player.Synergy.RockTiles = rocks;
            player.Synergy.GuardianTile = guardian;
        }

        public static BoardPosition[] PreparationRocks(PlayerState player,PokemonCatalog catalog)
        {
            int tier=Count(player,catalog).First(c=>c.TraitId==SynergyTraits.Earth).Tier;
            if(tier==0)return Array.Empty<BoardPosition>();
            int count=tier>=4?2:1;
            var occupied=player.Units.Where(u=>u.Placement.Kind==PlacementKind.Board).Select(u=>u.Placement.Position.Value).ToArray();
            var result=player.Synergy.RockTiles.Where(p=>HexBoardBounds.Preparation.Contains(p)&&!occupied.Contains(p)).Distinct().Take(count).ToList();
            var members=player.Units.Where(u=>u.Placement.Kind==PlacementKind.Board&&Traits(u,catalog,player).Contains(SynergyTraits.Earth)).OrderBy(u=>u.InstanceId,StringComparer.Ordinal);
            foreach(var unit in members) {
                if(result.Count>=count)break;
                var tile=HexCoordinates.Neighbors(HexCoordinates.ToAxial(unit.Placement.Position.Value)).Select(a=>HexCoordinates.TryToOffset(a,out var p)?(BoardPosition?)p:null)
                    .Where(p=>p.HasValue&&HexBoardBounds.Preparation.Contains(p.Value)&&!occupied.Contains(p.Value)&&!result.Contains(p.Value)).OrderBy(p=>p.Value.Row).ThenBy(p=>p.Value.Column).FirstOrDefault();
                if(tile.HasValue)result.Add(tile.Value);
            }
            foreach(var tile in HexBoardBounds.Preparation.Cells()) {
                if(result.Count>=count)break;
                if(!occupied.Contains(tile)&&!result.Contains(tile))result.Add(tile);
            }
            return result.ToArray();
        }
        public static BoardPosition? PreparationGuardian(PlayerState player, PokemonCatalog catalog)
        {
            if (player.IsEliminated || Count(player, catalog).First(count => count.TraitId == SynergyTraits.Nature).Tier == 0)
                return null;

            var occupied = player.Units.Where(unit => unit.Placement.Kind == PlacementKind.Board)
                .Select(unit => unit.Placement.Position.Value).Concat(PreparationRocks(player, catalog)).ToHashSet();
            var preferred = player.Synergy.GuardianTile;
            if (preferred.HasValue && HexBoardBounds.Preparation.Contains(preferred.Value) && !occupied.Contains(preferred.Value))
                return preferred;

            return HexBoardBounds.Preparation.Cells().Where(tile => !occupied.Contains(tile))
                .Select(tile => (BoardPosition?)tile).FirstOrDefault();
        }

        public static string[] Traits(UnitInstance unit,PokemonCatalog catalog,PlayerState player=null)
        {
            var traits=catalog.Get(unit.DefinitionId).TraitIds.ToList();
            foreach(var item in unit.ItemInstanceIds){var id=ItemCatalog.DefinitionId(item);if(id!=null&&id.StartsWith("emblem-",StringComparison.Ordinal))traits.Add(id.Substring(7));}
            if (player != null)
            {
                var origin = TextureOrigin(unit, player.Units, catalog);
                if (origin != null) traits.Add(origin);
            }
            return traits.Distinct(StringComparer.Ordinal).ToArray();
        }
        public static System.Collections.Generic.IReadOnlyList<SynergyCount> Count(PlayerState player,PokemonCatalog catalog)
        {var units=player.Units.Where(u=>u.Placement.Kind==PlacementKind.Board).ToArray();return Array.AsReadOnly(SynergyTraits.All.Select(t=>new SynergyCount(t,units.Where(u=>Traits(u,catalog,player).Contains(t)).Select(u=>SynergyTraits.Family(u.DefinitionId)).Distinct(StringComparer.Ordinal).Count())).ToArray());}
        public static string TextureOrigin(UnitInstance unit, IEnumerable<UnitInstance> source, PokemonCatalog catalog,
            Func<UnitInstance, BoardPosition?> position = null)
        {
            if (SynergyTraits.Family(unit.DefinitionId) != SynergyTraits.Family("porygon")) return null;
            position ??= u => u.Placement.Kind == PlacementKind.Board ? u.Placement.Position : null;
            var center = position(unit);
            if (!center.HasValue || center.Value.Column == 0 || center.Value.Column >= 6) return null;
            var neighbors = source.Where(u => u != unit && u.OwnerPlayerId == unit.OwnerPlayerId)
                .Select(u => (unit: u, cell: position(u))).Where(u => u.cell.HasValue && u.cell.Value.Row == center.Value.Row).ToArray();
            var left = neighbors.FirstOrDefault(u => u.cell.Value.Column == center.Value.Column - 1).unit;
            var right = neighbors.FirstOrDefault(u => u.cell.Value.Column == center.Value.Column + 1).unit;
            if (left == null || right == null) return null;
            // Only native origins can be copied; never recurse through another Porygon or copy emblems/jobs.
            return catalog.Get(left.DefinitionId).TraitIds.Intersect(catalog.Get(right.DefinitionId).TraitIds)
                .FirstOrDefault(SynergyTraits.Origins.Contains);
        }

        internal static string[] RosterTraits(UnitInstance unit, IEnumerable<UnitInstance> source, PokemonCatalog catalog,
            Func<UnitInstance, BoardPosition?> position = null)
        {
            var traits = Traits(unit, catalog).ToList();
            var origin = TextureOrigin(unit, source, catalog, position);
            if (origin != null) traits.Add(origin);
            return traits.Distinct(StringComparer.Ordinal).ToArray();
        }

        internal static System.Collections.Generic.IReadOnlyList<SynergyCount> CountUnits(IEnumerable<UnitInstance> source,PokemonCatalog catalog)
        {
            var units=source.ToArray();
            return SynergySystem.CountAssignments(units.Select(u =>
                new KeyValuePair<string, System.Collections.Generic.IReadOnlyList<string>>(u.DefinitionId, RosterTraits(u,units,catalog))));
        }
        internal static void Settle(PlayerState player,LocalPairBattle pair,int round,RoundOutcome outcome)
        {
            var p=player.Synergy;if(p.SettledRound>=round)return;p.SettledRound=round;
            if(pair.IsWild)return;
            int team=pair.Pairing.PlayerOneId==player.PlayerId?1:2;var battle=pair.Battle;
            // Empty-board battles still snapshot the persistent deployed board at combat start in the coordinator.
            int Tier(string id)=>battle?.Synergies.TeamTier(team,id)??(team==1?pair.OneSynergies:pair.TwoSynergies)?.First(c=>c.TraitId==id).Tier??0;
            int venom=Tier(SynergyTraits.Venom),mystic=Tier(SynergyTraits.Mystic),adversity=Tier(SynergyTraits.Adversity);
            if (venom > 0 && battle != null)
            {
                int reward = battle.Synergies.VenomReward(team);
                p.VenomSamples = checked(p.VenomSamples + reward);
                if (reward > 0 && (!battle.VenomEarned.TryGetValue(team, out int earned) || earned == 0))
                    p.VenomMinimumRewardRound = round;
            }
            // Keep the earned tier and pause charging while a reroll is stored.
            if (p.FreeRerollRound == 0)
            {
                p.MysticTier = mystic;
                if (mystic > 0)
                {
                    p.MysticBattles++;
                    if (mystic >= 4 || p.MysticBattles >= 2)
                        p.FreeRerollRound = round + 1;
                }
            }
            if (adversity > 0 && outcome == RoundOutcome.Loss)
            {
                int earned = adversity == 6 ? 3 : adversity == 4 ? 2 : 1;
                p.Resolve = checked(p.Resolve + earned);
                p.LastAdversitySettlement = new AdversitySettlement { round = round, resolveDelta = earned };
            }
            if (adversity > 0 && outcome == RoundOutcome.Win && p.Resolve > 0)
            {
                int gold = checked(p.Resolve * (adversity >= 4 ? 2 : 1));
                bool choice = adversity == 6 && p.Resolve >= 7;
                bool berry = adversity >= 4 && !choice;
                if (choice)
                {
                    int choices = checked(p.RewardChoices + 1);
                    p.PendingGold = checked(p.PendingGold + gold);
                    p.RewardChoices = choices;
                }
                else
                {
                    player.SetProgress(checked(player.Gold + gold), player.XP, player.Level);
                    if (berry) player.AddInventoryItem("item:" + ItemCatalog.SitrusBerry + ":" + round + ":" + player.PlayerId + ":adversity");
                }
                p.LastAdversitySettlement = new AdversitySettlement
                    { round = round, resolveDelta = -p.Resolve, gold = choice ? 0 : gold, berry = berry, choice = choice };
                p.Resolve = 0;
            }
        }
    }
}
