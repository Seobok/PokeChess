using System;
using System.Linq;
using System.Collections.Generic;
using PokeChess.Client.Data;
using PokeChess.Core.Match;
using UnityEngine;
using UnityEngine.EventSystems;
using PokeChess.Core.Pokemon;
namespace PokeChess.Client.UI
{
    public sealed partial class PlacementSandboxView
    {
        private RectTransform unitInfoRoot,unitInfoTooltip;
        private UnityEngine.UI.Text unitInfoTitle,unitInfoTypes,unitInfoTraits,unitInfoItems,unitInfoTooltipText;
        private readonly UnityEngine.UI.Text[] unitStatValues=new UnityEngine.UI.Text[7];
        private string[] unitStatHints=new string[7];
        private string unitSkillHint;
        private UnityEngine.UI.Image unitPortrait,unitSkillImage;
        private UnitPanelIconGraphic unitSkillMark,unitPositionMark;
        private RectTransform unitItemIcons;
        private UnityEngine.UI.Text unitPositionLabel;
        private readonly List<UnityEngine.UI.Image> unitEquippedIcons=new List<UnityEngine.UI.Image>();
        private string hoveredUnitItem;
        private PokemonAnimationLibrary unitPortraitLibrary;
        private UnitInfoIconLibrary unitPanelLibrary;
        private int hoveredUnitInfo=-2;
        private readonly string[] statNames={"체력","공격력","공격속도","방어력","마법저항","사거리","에너지"};
        private static string KoreanInfo(string id)
        {
            var contentName=PokeChess.Content.GameContent.DisplayName(id);
            if(contentName!=null)return contentName;
            switch(id){
                case "normal":return "노말";case "fire":return "불꽃";case "water":return "물";case "grass":return "풀";case "poison":return "독";case "bug":return "벌레";case "fighting":return "격투";case "psychic":return "에스퍼";case "rock":return "바위";case "ground":return "땅";case "electric":return "전기";case "steel":return "강철";case "ghost":return "고스트";
                case "tank":return "탱커";case "support":return "지원";case "carry":return "딜러";case "caster":return "기술 딜러";
                case "guardian":return "수호";case "assault":return "돌격";case "focus":return "집중";case "link":return "연결";
                case "bruiser":return "전사";
                case "growth":return "성장";
                default:return Readable(id);
            }
        }
        private void BuildUnitInfoIcons()
        {
            if(unitInfoRoot!=null)return;
            var library=Resources.Load<UnitInfoIconLibrary>("UnitInfoIcons");
            unitPanelLibrary=library;
            unitInfoRoot=Rect("UnitInfoIcons",canvasRect,new Vector2(215,200),new Vector2(490,170));
            unitInfoTitle=ItemLabel("Name",unitInfoRoot,"",15,new Vector2(120,26),new Vector2(43,110));
            unitInfoTypes=ItemLabel("Types",unitInfoRoot,"",11,new Vector2(120,18),new Vector2(43,89));
            unitInfoTraits=ItemLabel("Traits",unitInfoRoot,"",10,new Vector2(120,16),new Vector2(43,72));
            for(int i=0;i<7;i++){
                int index=i;var cell=Rect("Stat_"+i,unitInfoRoot,new Vector2(104,20),new Vector2(i%2==0?-53:53,35-i/2*20));
                var hit=cell.gameObject.AddComponent<UnityEngine.UI.Image>();hit.color=new Color(.1f,.17f,.24f,.5f);
                var icon=Rect("Icon",cell,new Vector2(24,20),new Vector2(-39,0)).gameObject.AddComponent<UnityEngine.UI.Image>();icon.sprite=library?.icons!=null&&i<library.icons.Length?library.icons[i]:null;icon.preserveAspect=true;icon.raycastTarget=false;
                unitStatValues[i]=ItemLabel("Value",cell,"",11,new Vector2(72,20),new Vector2(13,0));
                AddUnitInfoHover(cell,index);
            }
            var portrait=HudImage("PortraitSlot",unitInfoRoot,new Vector2(80,74),new Vector2(-62,91),new Color(.09f,.15f,.2f));
            unitPortrait=HudImage("UnitPortrait",portrait.transform,new Vector2(72,66),Vector2.zero,Color.white);
            unitPortrait.preserveAspect=true;unitPortraitLibrary=PokemonAnimationLibrary.Load();
            var skill=HudImage("SkillSlot",unitInfoRoot,new Vector2(62,58),new Vector2(-73,-74),new Color(.1f,.18f,.25f));skill.raycastTarget=true;
            unitSkillMark=Rect("SkillMark",skill.transform,new Vector2(34,34),new Vector2(0,5)).gameObject.AddComponent<UnitPanelIconGraphic>();
            unitSkillMark.color=new Color(.65f,.45f,1);unitSkillMark.raycastTarget=false;
            unitSkillImage=HudImage("SkillIcon",skill.transform,new Vector2(38,38),new Vector2(0,5),Color.white);unitSkillImage.preserveAspect=true;
            ItemLabel("Caption",skill.transform,"기술",10,new Vector2(58,16),new Vector2(0,-20));
            AddUnitInfoHover(skill.transform,-1);
            unitItemIcons=HudImage("EquippedItemSlot",unitInfoRoot,new Vector2(62,58),new Vector2(0,-74),new Color(.1f,.18f,.25f)).rectTransform;
            unitInfoItems=ItemLabel("Equipped",unitItemIcons,"",9,new Vector2(60,16),new Vector2(0,-20));
            var position=HudImage("PositionSlot",unitInfoRoot,new Vector2(62,58),new Vector2(73,-74),new Color(.1f,.18f,.25f));
            unitPositionMark=Rect("PositionIcon",position.transform,new Vector2(34,34),new Vector2(0,5)).gameObject.AddComponent<UnitPanelIconGraphic>();
            unitPositionMark.raycastTarget=false;
            unitPositionLabel=ItemLabel("PositionLabel",position.transform,"",10,new Vector2(58,16),new Vector2(0,-20));
            unitInfoTooltip=Panel("UnitInfoTooltip",new Vector2(370,175),new Vector2(175,174));unitInfoTooltip.GetComponent<UnityEngine.UI.Image>().raycastTarget=false;
            unitInfoTooltipText=ItemLabel("Hint",unitInfoTooltip,"",12,new Vector2(346,157),Vector2.zero);unitInfoTooltipText.alignment=TextAnchor.MiddleLeft;unitInfoTooltip.gameObject.SetActive(false);
        }
        private void RenderUnitPanelImages(PokemonDefinition definition,PokemonStats stats,string[] items)
        {
            var idle=unitPortraitLibrary?.Find(definition.Id,"Idle")??unitPortraitLibrary?.Find(PokeChess.Content.GameContent.BaseForm(definition.Id),"Idle");
            unitPortrait.sprite=idle?.Frames?.FirstOrDefault();unitPortrait.enabled=unitPortrait.sprite!=null;
            unitSkillImage.sprite=unitPanelLibrary?.GetSkillIcon(definition.SkillId)??Resources.Load<Sprite>("SkillIcons/"+definition.SkillId);
            unitSkillImage.enabled=unitSkillImage.sprite!=null;unitSkillMark.gameObject.SetActive(!unitSkillImage.enabled);
            bool front=stats.AttackRange<=2;
            unitPositionMark.Kind=front?1:2;unitPositionMark.color=front?new Color(.35f,.75f,1):new Color(.45f,.85f,.55f);unitPositionMark.SetVerticesDirty();
            unitPositionLabel.text=front?"전방":"후방";
            unitInfoItems.text=items.Length==0?"도구 없음":"장착 도구";
            int columns=Math.Max(1,(int)Math.Ceiling(Math.Sqrt(items.Length))),rows=Math.Max(1,(int)Math.Ceiling(items.Length/(double)columns));
            float cell=Math.Min(54f/columns,40f/rows);
            for(int i=0;i<items.Length;i++){
                if(i>=unitEquippedIcons.Count){
                    var icon=HudImage("EquippedIcon_"+i,unitItemIcons,new Vector2(24,24),Vector2.zero,Color.white);icon.preserveAspect=true;icon.raycastTarget=true;unitEquippedIcons.Add(icon);
                    int slot=i;var trigger=icon.gameObject.AddComponent<EventTrigger>();
                    var enter=new EventTrigger.Entry{eventID=EventTriggerType.PointerEnter};
                    enter.callback.AddListener(_=>{var id=unitEquippedIcons[slot].GetComponent<UnitPanelItemId>().Id;ShowEquippedUnitHint(id);});
                    var leave=new EventTrigger.Entry{eventID=EventTriggerType.PointerExit};leave.callback.AddListener(_=>HideUnitInfoHint());
                    trigger.triggers.Add(enter);trigger.triggers.Add(leave);icon.gameObject.AddComponent<UnitPanelItemId>();
                }
                var image=unitEquippedIcons[i];image.gameObject.SetActive(true);image.sprite=itemIcons?.Get(ItemCatalog.DefinitionId(items[i]));
                image.color=image.sprite==null?new Color(.4f,.5f,.6f):Color.white;
                image.GetComponent<UnitPanelItemId>().Id=items[i];
                image.rectTransform.sizeDelta=Vector2.one*(cell-2);
                image.rectTransform.anchoredPosition=new Vector2((i%columns-(columns-1)/2f)*cell,5+((rows-1)/2f-i/columns)*cell);
            }
            for(int i=items.Length;i<unitEquippedIcons.Count;i++)unitEquippedIcons[i].gameObject.SetActive(false);
        }
        private void ShowEquippedUnitHint(string id)
        {
            hoveredUnitItem=id;hoveredUnitInfo=-2;
            string key=ItemCatalog.DefinitionId(id);
            unitInfoTooltipText.text=key==null?ItemCatalog.DisplayName(id):ItemCatalog.Get(key).Name+"\n"+ItemCatalog.Get(key).Description;
            unitInfoTooltip.sizeDelta=new Vector2(370,90);unitInfoTooltipText.rectTransform.sizeDelta=new Vector2(346,72);
            unitInfoTooltip.gameObject.SetActive(true);unitInfoTooltip.SetAsLastSibling();
        }
        private void AddUnitInfoHover(Transform target,int index)
        {
            var trigger=target.gameObject.AddComponent<EventTrigger>();
            var enter=new EventTrigger.Entry{eventID=EventTriggerType.PointerEnter};enter.callback.AddListener(_=>ShowUnitInfoHint(index));
            var exit=new EventTrigger.Entry{eventID=EventTriggerType.PointerExit};exit.callback.AddListener(_=>HideUnitInfoHint());trigger.triggers.Add(enter);trigger.triggers.Add(exit);
        }
        private void ShowUnitInfoHint(int index)
        {hoveredUnitInfo=index;unitInfoTooltip.sizeDelta=new Vector2(370,index<0?175:90);unitInfoTooltipText.rectTransform.sizeDelta=new Vector2(346,index<0?157:72);unitInfoTooltipText.text=index<0?unitSkillHint:unitStatHints[index];unitInfoTooltip.gameObject.SetActive(true);unitInfoTooltip.SetAsLastSibling();}
        private void HideUnitInfoHint(){hoveredUnitItem=null;hoveredUnitInfo=-2;if(unitInfoTooltip!=null)unitInfoTooltip.gameObject.SetActive(false);}
        private void RenderUnitInfoIcons(PokemonDefinition definition,UnitRank rank,PokemonStats stats,double hp,double maxHP,double energy,double maxEnergy,double shield,string[] items,bool baseStats=false)
        {
            BuildUnitInfoIcons();bool has=definition!=null;unitInfoRoot.gameObject.SetActive(has);detailLabel.gameObject.SetActive(!has);
            if(!has){detailLabel.font=itemFont;detailLabel.text="포켓몬을 선택하세요\n보드 또는 벤치의 포켓몬을\n클릭하면 정보를 볼 수 있습니다.";HideUnitInfoHint();return;}
            unitInfoTitle.text=KoreanInfo(definition.Id)+"  "+new string('★',(int)rank);
            unitInfoTypes.text=KoreanInfo(definition.RoleId)+" · "+string.Join(" / ",definition.TypeIds.Select(KoreanInfo));
            unitInfoTraits.text=string.Join(" · ",definition.TraitIds.Select(KoreanInfo));
            string[] values={Number((float)hp)+"/"+Number((float)maxHP),Number(stats.Attack),Number(stats.AttackSpeed),Number(stats.Armor),Number(stats.MagicResistance),stats.AttackRange.ToString(),Number((float)energy)+"/"+Number((float)maxEnergy)};
            string[] hints={"현재 체력 / 최대 체력"+(shield>0?"\n보호막: "+Number((float)shield):""),"기본 공격의 피해량","초당 기본 공격 횟수","물리 피해를 줄이는 능력치","마법 피해를 줄이는 능력치","기본 공격이 닿는 칸 수","현재 에너지 / 기술에 필요한 에너지"};
            for(int i=0;i<7;i++){unitStatValues[i].text=values[i];unitStatHints[i]=statNames[i]+"\n"+hints[i]+(baseStats&&i>0&&i<6?"\n성급 기준 수치 (전투 중 강화 효과 제외)":"");}
            RenderUnitPanelImages(definition,stats,items);
            unitSkillHint=ContextSkill(definition,rank,stats);
            unitSkillHint=unitSkillHint.Replace(Readable(definition.SkillId),KoreanInfo(definition.SkillId));
            foreach(var pair in new[]{("Lowest HP ally","체력이 가장 낮은 아군"),("Enemies r","범위 내 적 · 반경 "),("Allies r","범위 내 아군 · 반경 "),(" + splash r"," · 폭발 반경 "),("True damage","고정 피해"),("AttackSpeed","공격속도"),("Attack","공격력"),("SpellPower","주문력"),("Duration","지속 시간")})unitSkillHint=unitSkillHint.Replace(pair.Item1,pair.Item2);
            foreach(var pair in new[]{("Self","자신"),("Target","대상"),("Enemies","적"),("Allies","아군"),("Physical damage","물리 피해"),("Magic damage","마법 피해"),("Shield","보호막"),("Heal","회복"),("cast","시전"),("Stun","기절"),("Taunt","도발"),("Slow","둔화"),("AttackSpeed","공격속도"),("Armor","방어력"),("MagicResistance","마법저항")})unitSkillHint=unitSkillHint.Replace(pair.Item1,pair.Item2);
            if(hoveredUnitItem!=null){if(items.Contains(hoveredUnitItem))ShowEquippedUnitHint(hoveredUnitItem);else HideUnitInfoHint();}else if(hoveredUnitInfo!=-2)ShowUnitInfoHint(hoveredUnitInfo);
        }
    }
}

