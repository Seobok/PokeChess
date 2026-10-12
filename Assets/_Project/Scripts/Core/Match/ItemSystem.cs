using System;
using System.Collections.Generic;
using System.Linq;
using PokeChess.Core.Pokemon;
using PokeChess.Core.Random;

namespace PokeChess.Core.Match
{
    public sealed class ItemDefinition
    {
        public string Id { get; }
        public string Name { get; }
        public string Description { get; }
        public string Role { get; }
        internal ItemDefinition(string id,string name,string description,string role)
        { Id=id;Name=name;Description=description;Role=role; }
    }
    // Instance IDs carry an immutable definition key so frozen battles and public snapshots
    // can resolve tools without consulting another player's private inventory.
    public static class ItemCatalog
    {
        public const string Leftovers="leftovers",SitrusBerry="sitrus-berry",AssaultVest="assault-vest",
            FocusSash="focus-sash",WiseGlasses="wise-glasses",RockyHelmet="rocky-helmet",
            ChoiceScarf="choice-scarf",ExpShare="exp-share";
        public static IReadOnlyList<ItemDefinition> All { get; }=Array.AsReadOnly(new[]{
            new ItemDefinition(Leftovers,"먹다남은음식","3초마다 최대 HP의 4% 회복","앞줄 / 장기전"),
            new ItemDefinition(SitrusBerry,"자뭉열매","HP 40% 이하에서 최대 HP의 25% 회복 (전투당 1회)","생존"),
            new ItemDefinition(AssaultVest,"돌격조끼","특수(마법) 피해 25% 감소","특수 피해 대응"),
            new ItemDefinition(FocusSash,"기합의띠","첫 치명적 피해를 HP 1로 견디고 1초간 피해 차단","핵심 유닛 보호"),
            new ItemDefinition(WiseGlasses,"박식안경","직접 기술 피해 20% 증가","기술 딜러"),
            new ItemDefinition(RockyHelmet,"울퉁불퉁멧","실제로 받은 직접 피해의 20%를 반사 (지속/반사 제외)","앞줄 / 반격"),
            new ItemDefinition(ChoiceScarf,"구애스카프","공격 속도 25% 증가, 직접 기술 피해 15% 감소","기본 공격 딜러"),
            new ItemDefinition(ExpShare,"학습장치","인접 아군의 기술 발동 시 에너지 10 획득 (2초 쿨다운)","배치 / 기술 지원")});
        public static ItemDefinition Get(string id)=>All.FirstOrDefault(d=>d.Id==id)
            ??throw new ArgumentException("Unknown item definition.",nameof(id));
        public static string DefinitionId(string instanceId)
        {
            if(instanceId==null)return null;
            var parts=instanceId.Split(':');
            return parts.Length>=4&&parts[0]=="item"&&All.Any(d=>d.Id==parts[1])?parts[1]:null;
        }
        public static string DisplayName(string instanceId)=>All.FirstOrDefault(d=>d.Id==DefinitionId(instanceId))?.Name??instanceId;
        public static bool Has(IEnumerable<string> ids,string definition)=>ids.Any(id=>DefinitionId(id)==definition);
    }
    public sealed class ItemRewardOffer
    {
        public int Round { get; }
        public IReadOnlyList<string> Candidates { get; }
        internal ItemRewardOffer(int round,IEnumerable<string> candidates)
        { Round=round;Candidates=Array.AsReadOnly(candidates.ToArray()); }
    }
    public sealed class ItemSystem
    {
        public static IReadOnlyList<string> Candidates(ulong seed,int round)
        {
            var random=new DeterministicRandom(unchecked(seed^0x4954454D52455744UL^(ulong)round*0x9E3779B97F4A7C15UL));
            var pool=ItemCatalog.All.Select(d=>d.Id).ToList();var chosen=new List<string>();
            for(int i=0;i<3;i++){int index=random.NextInt(pool.Count);chosen.Add(pool[index]);pool.RemoveAt(index);}
            return chosen.AsReadOnly();
        }
        internal void Offer(MatchState match,PlayerState player)
        {
            if(player.LastItemRewardRound>=match.RoundNumber)return;
            if(player.PendingItemReward!=null)throw new InvalidOperationException("Unclaimed reward.");
            player.SetItemReward(new ItemRewardOffer(match.RoundNumber,Candidates(match.MatchSeed,match.RoundNumber)));
        }
        public string Choose(MatchState match,string playerId,int slot,long revision)
        {
            var p=match.GetPlayer(playerId);
            if(p.HP==0||p.IsEliminated)throw new InvalidOperationException("Player is eliminated.");
            if(match.Phase!=MatchPhase.Result&&match.Phase!=MatchPhase.Preparation)throw new InvalidOperationException("Reward is unavailable in this phase.");
            if(p.ItemRevision!=revision)throw new InvalidOperationException("Stale item revision.");
            var offer=p.PendingItemReward??throw new InvalidOperationException("No reward available.");
            bool currentOffer=offer.Round==match.RoundNumber || match.Phase==MatchPhase.Preparation && offer.Round==match.RoundNumber-1;
            if(!currentOffer||slot<0||slot>=offer.Candidates.Count)throw new ArgumentException("Invalid reward choice.");
            string id="item:"+offer.Candidates[slot]+":"+offer.Round+":"+p.PlayerId;
            p.ClaimItemReward(id);return id;
        }
        internal void AutoChoose(MatchState match,PlayerState p)
        { if(p.PendingItemReward!=null&&p.HP>0&&!p.IsEliminated)Choose(match,p.PlayerId,0,p.ItemRevision); }
        private static PlayerState Validate(MatchState match,string playerId,long revision)
        {
            var p=match.GetPlayer(playerId);
            if(p.HP==0||p.IsEliminated)throw new InvalidOperationException("Player is eliminated.");
            if(match.Phase!=MatchPhase.Preparation)throw new InvalidOperationException("Requires Preparation.");
            if(p.ItemRevision!=revision)throw new InvalidOperationException("Stale item revision.");
            return p;
        }
        public void Equip(MatchState match,string playerId,string unitId,string itemId,long revision)
        {
            var p=Validate(match,playerId,revision);var unit=p.GetUnit(unitId);
            if(unit.Placement.Kind==PlacementKind.Unplaced||!p.ItemInventory.Contains(itemId)||ItemCatalog.DefinitionId(itemId)==null)
                throw new InvalidOperationException("Item is not available for this unit.");
            p.EquipInventoryItem(unit,itemId);
        }
        public void Unequip(MatchState match,string playerId,string unitId,long revision)
        {
            var p=Validate(match,playerId,revision);var unit=p.GetUnit(unitId);
            if(unit.ItemInstanceIds.Count==0)throw new InvalidOperationException("No equipped item.");
            p.UnequipItem(unit);
        }
    }
}
