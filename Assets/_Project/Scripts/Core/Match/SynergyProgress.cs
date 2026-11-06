using System;
using System.Linq;
using PokeChess.Core.Battle;
using PokeChess.Core.Pokemon;
using PokeChess.Core.Board;
namespace PokeChess.Core.Match
{
    public sealed class SynergyProgress
    {
        public int NatureXP { get; internal set; }
        public int VenomSamples { get; internal set; }
        public int MysticTier { get; internal set; }
        public int MysticBattles { get; internal set; }
        public int Resolve { get; internal set; }
        public int FreeRerollRound { get; internal set; }
        public int SettledRound { get; internal set; }
        public int EquipmentFragments { get; internal set; }
        public int EmblemFragments { get; internal set; }
        public int RewardChoices { get; internal set; }
        public int PendingGold { get; internal set; }
        public string ProphecyOrigin { get; internal set; }
        public BoardPosition[] RockTiles { get; internal set; } = Array.Empty<BoardPosition>();
    }
    public static class SynergyPreparation
    {
        // mode 0: texture/passenger, 1: rock slot, 2: prophecy, 3: adversity reward.
        public static void Configure(MatchState match,PokemonCatalog catalog,string playerId,int mode,string unitId,string value,int slot=0,int column=0,int row=0)
        {
            var player=match.GetPlayer(playerId);
            if(match.Phase!=MatchPhase.Preparation||player.IsEliminated)throw new InvalidOperationException("준비 단계에서 설정할 수 있습니다.");
            if(mode==0)
            {
                var source=player.Units.Single(u=>u.InstanceId==unitId&&u.Placement.Kind==PlacementKind.Board);
                var target=player.Units.Single(u=>u.InstanceId==value&&u.Placement.Kind==PlacementKind.Board&&u!=source);
                var family=SynergyTraits.Family(source.DefinitionId);
                if(family!=SynergyTraits.Family("porygon")&&family!=SynergyTraits.Family("lapras"))throw new ArgumentException("텍스처 또는 승선 대상만 지정할 수 있습니다.");
                if(family==SynergyTraits.Family("lapras")&&HexCoordinates.Distance(source.Placement.Position.Value,target.Placement.Position.Value)!=1)throw new ArgumentException("승객은 인접해야 합니다.");
                source.SynergyTargetId=target.InstanceId;
            }
            else if(mode==1)
            {
                var counts=Count(player,catalog);int tier=counts.First(c=>c.TraitId==SynergyTraits.Earth).Tier;
                if(tier==0||slot<0||slot>=(tier>=4?2:1))throw new ArgumentException("대지 구조물 슬롯이 없습니다.");
                var tile=new BoardPosition(column,row);
                if(!HexBoardBounds.Preparation.Contains(tile)||player.Units.Any(u=>u.Placement.Kind==PlacementKind.Board&&u.Placement.Position.Value.Equals(tile)))throw new ArgumentException("빈 아군 칸을 지정하세요.");
                var next=player.Synergy.RockTiles.ToList();if(next.Where((p,i)=>i!=slot).Contains(tile))throw new ArgumentException("구조물 위치가 겹칩니다.");
                while(next.Count<slot)next.Add(HexBoardBounds.Preparation.Cells().First(p=>!p.Equals(tile)&&!next.Contains(p)&&!player.Units.Any(u=>u.Placement.Kind==PlacementKind.Board&&u.Placement.Position.Value.Equals(p))));if(next.Count==slot)next.Add(tile);else next[slot]=tile;player.Synergy.RockTiles=next.ToArray();
            }
            else if(mode==2)
            {if(!SynergyTraits.Origins.Contains(value))throw new ArgumentException("계열을 선택하세요.");player.Synergy.ProphecyOrigin=value;}
            else if(mode==3)
            {
                if(player.Synergy.RewardChoices==0||slot<0||slot>2)throw new InvalidOperationException("선택할 보상이 없습니다.");
                int share=player.Synergy.PendingGold/player.Synergy.RewardChoices;
                if(slot==0)player.SetProgress(checked(player.Gold+share),player.XP,player.Level);
                else if(slot==1)player.Synergy.EquipmentFragments=checked(player.Synergy.EquipmentFragments+1);
                else player.Synergy.EmblemFragments=checked(player.Synergy.EmblemFragments+1);
                player.Synergy.RewardChoices--;player.Synergy.PendingGold-=share;
            }
            else throw new ArgumentOutOfRangeException(nameof(mode));
        }
        public static string[] Traits(UnitInstance unit,PokemonCatalog catalog,PlayerState player=null)
        {
            var traits=catalog.Get(unit.DefinitionId).TraitIds.ToList();
            foreach(var item in unit.ItemInstanceIds){var id=ItemCatalog.DefinitionId(item);if(id!=null&&id.StartsWith("emblem-",StringComparison.Ordinal))traits.Add(id.Substring(7));}
            if(player!=null&&SynergyTraits.Family(unit.DefinitionId)==SynergyTraits.Family("porygon")&&unit.SynergyTargetId!=null){var target=player.Units.FirstOrDefault(u=>u.InstanceId==unit.SynergyTargetId&&u.Placement.Kind==PlacementKind.Board);if(target!=null)traits.AddRange(catalog.Get(target.DefinitionId).TraitIds.Where(SynergyTraits.Origins.Contains).Take(1));}
            return traits.Distinct(StringComparer.Ordinal).ToArray();
        }
        public static System.Collections.Generic.IReadOnlyList<SynergyCount> Count(PlayerState player,PokemonCatalog catalog)
        {var units=player.Units.Where(u=>u.Placement.Kind==PlacementKind.Board).ToArray();return Array.AsReadOnly(SynergyTraits.All.Select(t=>new SynergyCount(t,units.Where(u=>Traits(u,catalog,player).Contains(t)).Select(u=>SynergyTraits.Family(u.DefinitionId)).Distinct(StringComparer.Ordinal).Count())).ToArray());}
        internal static System.Collections.Generic.IReadOnlyList<SynergyCount> CountUnits(System.Collections.Generic.IEnumerable<UnitInstance> source,PokemonCatalog catalog) {
            var units=source.ToArray();
            return SynergySystem.CountAssignments(units.Select(u=> {
                var traits=Traits(u,catalog).ToList();
                if(SynergyTraits.Family(u.DefinitionId)==SynergyTraits.Family("porygon")){var target=units.FirstOrDefault(t=>t.InstanceId==u.SynergyTargetId);if(target!=null)traits.AddRange(catalog.Get(target.DefinitionId).TraitIds.Where(SynergyTraits.Origins.Contains).Take(1));}
                return new System.Collections.Generic.KeyValuePair<string,System.Collections.Generic.IReadOnlyList<string>>(u.DefinitionId,traits);
            }));
        }
        internal static void Settle(PlayerState player,LocalPairBattle pair,int round,RoundOutcome outcome)
        {
            var p=player.Synergy;if(p.SettledRound>=round)return;p.SettledRound=round;
            if(pair.IsWild)return;
            int team=pair.Pairing.PlayerOneId==player.PlayerId?1:2;var battle=pair.Battle;
            // Empty-board battles still snapshot the persistent deployed board at combat start in the coordinator.
            int Tier(string id)=>battle?.Synergies.TeamTier(team,id)??(team==1?pair.OneSynergies:pair.TwoSynergies)?.First(c=>c.TraitId==id).Tier??0;
            int nature=Tier(SynergyTraits.Nature),venom=Tier(SynergyTraits.Venom),mystic=Tier(SynergyTraits.Mystic),adversity=Tier(SynergyTraits.Adversity);
            if(nature>0&&battle!=null){var guardian=battle.Units.FirstOrDefault(u=>u.TeamId==team&&u.IsSynergySummon);int kills=battle.NatureEarned.TryGetValue(team,out var earned)?earned:0;int alive=guardian!=null&&guardian.CurrentHP>0&&guardian.IsOnBoard?1:0;int heal=battle.NatureHealBonus.Contains(team)?1:0;p.NatureXP=checked(p.NatureXP+Math.Min(3,kills+alive+(nature>=6?alive:0))+heal);}
            if(venom>0&&battle!=null&&battle.VenomEarned.TryGetValue(team,out var samples))p.VenomSamples=checked(p.VenomSamples+samples);
            p.MysticTier=mystic;
            if(mystic>0){p.MysticBattles++;if(mystic>=4||p.MysticBattles%2==0)p.FreeRerollRound=round+1;}
            if(adversity>0&&outcome==RoundOutcome.Loss)p.Resolve=checked(p.Resolve+(adversity==6?3:adversity==4?2:1));
            if(adversity>0&&outcome==RoundOutcome.Win&&p.Resolve>0)
            {
                int gold=checked(p.Resolve*(adversity>=4?2:1));
                if(adversity==6&&p.Resolve>=7){p.RewardChoices=checked(p.RewardChoices+1);p.PendingGold=checked(p.PendingGold+gold);}
                else {player.SetProgress(checked(player.Gold+gold),player.XP,player.Level);if(adversity>=4)player.AddInventoryItem("item:"+ItemCatalog.SitrusBerry+":"+round+":"+player.PlayerId+":adversity");}
                p.Resolve=0;
            }
        }
    }
}
