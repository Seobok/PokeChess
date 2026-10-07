using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using PokeChess.Core.Battle;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;

namespace PokeChess.Client.UI
{
    public sealed partial class PlacementSandboxView
    {
        private readonly SkillCatalog contextSkills=PrototypeRoster.CreateSkills();
        private readonly StatusCatalog contextStatuses=PrototypeRoster.CreateStatuses();
        private MatchState contextMatch;
        private readonly HashSet<string> contextItems=new HashSet<string>();
        private string rankNotice="",itemNotice="";
        private float rankNoticeUntil,itemNoticeUntil;
        public string RankFeedbackText => Time.unscaledTime<rankNoticeUntil ? rankNotice : "";
        public string ItemFeedbackText => Time.unscaledTime<itemNoticeUntil ? itemNotice : "";
        private static string Readable(string id) => System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(id.Replace('-',' '));
        private static string Number(float value) => value.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture);
        private static string RankBadge(UnitRank rank) => new string('★',(int)rank)+" (R"+(int)rank+")";
        private void RecordContextRankUps(IEnumerable<UnitRankedUp> events)
        {
            var merges=events.ToArray();
            foreach(var merge in merges)
            {
                if(merge.ConsumedUnitIds.Contains(selectedUnitId))selectedUnitId=merge.ResultUnitId;
                highlights[merge.ResultUnitId]=Time.unscaledTime+1.5f;
            }
            if(merges.Length==0)return;
            rankNotice="RANK UP "+string.Join(" → ",merges.Select(m=>"R"+(int)m.Rank));rankNoticeUntil=Time.unscaledTime+2;
        }
        private string ContextSkill(PokemonDefinition definition,UnitRank rank,PokemonStats stats)
        {
            if(!contextSkills.TryGet(definition.SkillId,out var skill))return Readable(definition.SkillId)+"\nDescription unavailable";
            var lines=new List<string>{Readable(skill.Id)+" · cast "+Number(skill.CastTime)+"s"};
            foreach(var effect in skill.Effects)
            {
                string target=effect.TargetSelector==EffectTargetSelector.Self?"Self":effect.TargetSelector==EffectTargetSelector.CastTarget?
                    (skill.TargetRule==SkillTargetRule.LowestHPAlly?"Lowest HP ally":"Target"):
                    effect.TargetSelector==EffectTargetSelector.AlliesAroundCaster?"Allies r"+effect.Radius:"Enemies r"+(effect.ImpactRadius??effect.Radius);
                if(effect.Type==SkillEffectType.ApplyStatus)
                {
                    var status=contextStatuses.Get(effect.ResolveStatusId(rank));
                    string text=status.CrowdControl==CrowdControlKind.None?string.Join(", ",status.Modifiers.Select(m=>
                        m.Stat+" "+(m.Value>=0?"+":"")+Number(m.Type==StatModifierType.Percentage?m.Value*100:m.Value)+(m.Type==StatModifierType.Percentage?"%":""))):
                        status.CrowdControl+(status.CrowdControl==CrowdControlKind.Slow?" "+Number(status.SlowFraction*100)+"%":"");
                    lines.Add(target+": "+text+" / "+Number(status.Duration)+"s");
                }
                else
                {
                    string kind=effect.IsDamage?effect.DamageType+" damage":effect.Type.ToString();
                    if(effect.ImpactRadius.HasValue)target+=" + splash r"+effect.ImpactRadius;
                    lines.Add(target+": "+kind+" "+Number(effect.ResolveValue(rank,stats.SpellPower,stats.Attack)));
                }
            }
            return string.Join("\n",lines);
        }
        private void RenderUnitContext()
        {
            if(contextMatch!=Match)
            {
                contextMatch=Match;contextItems.Clear();contextItems.UnionWith(Player.ItemInventory);
                rankNoticeUntil=itemNoticeUntil=0;
            }
            int added=Player.ItemInventory.Count(id=>!contextItems.Contains(id));
            if(added>0){itemNotice="+"+added+" item(s) returned";itemNoticeUntil=Time.unscaledTime+2;}
            contextItems.Clear();contextItems.UnionWith(Player.ItemInventory);
            inventoryLabel.color=ItemFeedbackText.Length>0?new Color(.4f,1,.7f):Color.white;
            inventoryLabel.text="ITEMS ("+Player.ItemInventory.Count+")"+(ItemFeedbackText.Length>0?" · "+ItemFeedbackText:"")+"\n"+
                (Player.ItemInventory.Count==0?"No items":string.Join("\n",Player.ItemInventory.Take(3)))+
                (Player.ItemInventory.Count>3?"\n+"+(Player.ItemInventory.Count-3)+" more":"");
            var selected=Player.Units.FirstOrDefault(u=>u.InstanceId==selectedUnitId);
            if(selected==null){selectedUnitId=null;detailLabel.text="SELECT YOUR UNIT\nClick board or bench to inspect.\n"+RankFeedbackText;sellButton.interactable=false;ButtonText(sellButton,"Sell");return;}
            var definition=catalog.Get(selected.DefinitionId);var rank=selected.Rank;
            var stats=RankRules.Default.Apply(definition.BaseStats,rank);
            var fighter=Match.Phase==MatchPhase.Combat?RoundLoop.GetBattleFor("p1")?.Battle?.Units.FirstOrDefault(u=>u.UnitInstanceId==selectedUnitId):null;
            if(fighter!=null){rank=fighter.Rank;stats=fighter.Stats;}
            string vitals=fighter==null?"Base HP "+Number(stats.MaxHP):"HP "+Number(fighter.CurrentHP)+" / "+Number(stats.MaxHP)+" · Shield "+Number(fighter.CurrentShield);
            string state=fighter==null?selected.Placement.Kind.ToString():fighter.ActionState+" · Energy "+Number(fighter.CurrentEnergy)+" / "+Number(stats.MaxEnergy);
            detailLabel.text=definition.DisplayName+"  "+RankBadge(rank)+"\n"+Readable(definition.RoleId)+" · "+string.Join(" / ",definition.TypeIds.Select(Readable))+"\n"+
                (definition.TraitIds.Count>0?"Traits: "+string.Join(", ",definition.TraitIds.Select(Readable))+"\n":"")+
                vitals+"\nBase AT "+Number(stats.Attack)+" · Range "+stats.AttackRange+"\n"+state+"\n"+ContextSkill(definition,rank,stats)+
                "\nItems ("+selected.ItemInstanceIds.Count+"): "+(selected.ItemInstanceIds.Count==0?"None":string.Join(", ",selected.ItemInstanceIds))+
                "\n"+(RankFeedbackText.Length>0?RankFeedbackText:selected.ItemInstanceIds.Count>0?"Sale returns equipped items":"");
            bool allowed=commands.CanTrade&&!busy&&!RoundLoop.NextPreparationPending&&selected.Placement.Kind!=PlacementKind.Unplaced;
            sellButton.interactable=allowed;ButtonText(sellButton,allowed?"Sell / +"+commands.SalePrice(selectedUnitId)+"G":"Sell unavailable / "+Match.Phase);
        }
    }
}
