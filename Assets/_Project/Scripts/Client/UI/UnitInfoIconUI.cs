using System;
using System.Linq;
using System.Collections.Generic;
using PokeChess.Client.Data;
using PokeChess.Core.Battle;
using PokeChess.Core.Match;
using UnityEngine;
using UnityEngine.EventSystems;
using PokeChess.Core.Pokemon;
namespace PokeChess.Client.UI
{
    public sealed partial class PlacementSandboxView
    {
        private RectTransform unitInfoRoot,unitInfoTooltip;
        private UnityEngine.UI.Text unitInfoTitle,unitInfoTraits,unitInfoItems,unitInfoTooltipText;
        private UnityEngine.UI.Text unitDetailHeader, unitDetailCost, unitRoleLabel, unitDetailHPText, unitDetailManaText, unitDetailSkill;
        private UnityEngine.UI.Image unitDetailRank, unitRoleBadgeIcon, unitDetailHP, unitDetailMana, unitOwnedBall;
        private readonly UnityEngine.UI.Text[] unitStatValues=new UnityEngine.UI.Text[6];
        private string[] unitStatHints=new string[6];
        private string unitSkillHint;
        private UnityEngine.UI.Image unitPortrait,unitSkillImage;
        private UnitPanelIconGraphic unitSkillMark;
        private RectTransform unitItemIcons;
        private readonly List<UnityEngine.UI.Image> unitEquippedIcons=new List<UnityEngine.UI.Image>();
        private string hoveredUnitItem;
        private PokemonAnimationLibrary unitPortraitLibrary;
        private UnitInfoIconLibrary unitPanelLibrary;
        private int hoveredUnitInfo=-2;
        private readonly string[] statNames={"공격력","주문력","방어력","마법 저항력","공격 속도","사거리"};
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
        private static readonly Color32 DetailInk = new Color32(14, 24, 55, 255);
        private static Sprite DetailSprite(string name) => Resources.Load<Sprite>("UnitDetail/Detail" + name);

        private UnityEngine.UI.Image DetailImage(string name, Transform parent, Vector2 size, Vector2 point, Sprite sprite)
        {
            var image = HudImage(name, parent, size, point, Color.white);
            image.sprite = sprite;
            return image;
        }

        private UnityEngine.UI.Text UnitDetailLabel(string name, Transform parent, string text, int fontSize, Vector2 size,
            Vector2 position, TextAnchor alignment = TextAnchor.MiddleCenter)
        {
            var label = ItemLabel(name, parent, text, fontSize, size, position);
            label.color = DetailInk;
            label.fontStyle = FontStyle.Bold;
            label.alignment = alignment;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 8;
            label.resizeTextMaxSize = fontSize;
            return label;
        }

        private void BuildUnitInfoIcons()
        {
            if (unitInfoRoot != null) return;
            unitPanelLibrary = Resources.Load<UnitInfoIconLibrary>("UnitInfoIcons");
            unitPortraitLibrary = PokemonAnimationLibrary.Load();
            LoadUnitOverheadSprites();

            var frame = DetailImage("UnitInfoIcons", detailPopup, new Vector2(340, 373), Vector2.zero, DetailSprite("PanelFrame"));
            unitInfoRoot = frame.rectTransform;
            var header = DetailImage("Header", unitInfoRoot, new Vector2(320, 39), new Vector2(0, 156), DetailSprite("Header"));
            unitDetailHeader = UnitDetailLabel("Title", header.transform, "", 20, new Vector2(292, 37), Vector2.zero, TextAnchor.MiddleLeft);
            unitDetailHeader.color = Color.white;

            unitPortrait = DetailImage("UnitPortrait", unitInfoRoot, new Vector2(72, 64), new Vector2(-118, 104), null);
            unitPortrait.preserveAspect = true;
            unitInfoTitle = UnitDetailLabel("Name", unitInfoRoot, "", 20, new Vector2(101, 27), new Vector2(-29, 118), TextAnchor.MiddleLeft);
            unitDetailRank = DetailImage("Rank", unitInfoRoot, new Vector2(23, 20), new Vector2(38, 118), null);
            var role = DetailImage("RoleBadge", unitInfoRoot, new Vector2(101, 25), new Vector2(108, 118), DetailSprite("RoleBadge"));
            unitRoleBadgeIcon = DetailImage("Icon", role.transform, new Vector2(16, 16), new Vector2(-37, 0), null);
            unitRoleBadgeIcon.preserveAspect = true;
            unitRoleLabel = UnitDetailLabel("Role", role.transform, "", 12, new Vector2(75, 22), new Vector2(9, 0));

            var traits = DetailImage("TraitsBadge", unitInfoRoot, new Vector2(157, 24), new Vector2(1, 87), ShopSprite("SynergyBadge"));
            traits.color = new Color32(210, 233, 249, 255);
            unitInfoTraits = UnitDetailLabel("Traits", traits.transform, "", 12, new Vector2(149, 21), Vector2.zero);
            var cost = DetailImage("CostBadge", unitInfoRoot, new Vector2(68, 26), new Vector2(125, 87), ShopSprite("PriceBadge"));
            DetailImage("Coin", cost.transform, new Vector2(16, 16), new Vector2(-20, 0), ShopSprite("Coin"));
            unitDetailCost = UnitDetailLabel("Cost", cost.transform, "", 16, new Vector2(42, 23), new Vector2(9, 0));

            unitDetailHP = BuildDetailMeter("HP", 54, unitAllyHP, out unitDetailHPText);
            unitDetailMana = BuildDetailMeter("마나", 32, unitMana, out unitDetailManaText);

            var grid = DetailImage("StatGrid", unitInfoRoot, new Vector2(318, 95), new Vector2(0, -27), DetailSprite("StatGrid"));
            string[] assets = { "Attack", "SpellPower", "Armor", "MagicResistance", "AttackSpeed", "Range" };
            for (int i = 0; i < statNames.Length; i++)
            {
                var cell = HudImage("Stat_" + i, grid.transform, new Vector2(150, 30),
                    new Vector2(i % 2 == 0 ? -79 : 79, 31 - i / 2 * 31), Color.clear);
                cell.raycastTarget = true;
                DetailImage("Icon", cell.transform, new Vector2(17, 17), new Vector2(-62, 0), DetailSprite("Stat" + assets[i])).preserveAspect = true;
                UnitDetailLabel("Caption", cell.transform, statNames[i], 12, new Vector2(74, 24), new Vector2(-14, 0), TextAnchor.MiddleLeft);
                unitStatValues[i] = UnitDetailLabel("Value", cell.transform, "", 14, new Vector2(47, 24), new Vector2(49, 0), TextAnchor.MiddleRight);
                AddUnitInfoHover(cell.transform, i);
            }

            var skill = DetailImage("SkillSlot", unitInfoRoot, new Vector2(320, 34), new Vector2(0, -95), DetailSprite("SkillRow"));
            skill.raycastTarget = true;
            unitSkillMark = Rect("SkillMark", skill.transform, new Vector2(27, 27), new Vector2(-137, 0)).gameObject.AddComponent<UnitPanelIconGraphic>();
            unitSkillMark.color = new Color(.65f, .45f, 1);
            unitSkillMark.raycastTarget = false;
            unitSkillImage = DetailImage("SkillIcon", skill.transform, new Vector2(26, 26), new Vector2(-137, 0), null);
            unitSkillImage.preserveAspect = true;
            unitDetailSkill = UnitDetailLabel("SkillName", skill.transform, "", 16, new Vector2(254, 28), new Vector2(18, 0), TextAnchor.MiddleLeft);
            AddUnitInfoHover(skill.transform, -1);

            var footer = DetailImage("Footer", unitInfoRoot, new Vector2(320, 35), new Vector2(0, -133), DetailSprite("Footer"));
            unitItemIcons = Rect("EquippedItemSlot", footer.transform, new Vector2(75, 32), new Vector2(-114, 0));
            unitOwnedBall = DetailImage("OwnedBall", footer.transform, new Vector2(24, 24), new Vector2(-44, 0), DetailSprite("OwnedBall"));
            unitInfoItems = UnitDetailLabel("FooterText", footer.transform, "", 13, new Vector2(230, 30), new Vector2(34, 0), TextAnchor.MiddleLeft);

            unitInfoTooltip = Panel("UnitInfoTooltip", new Vector2(370, 175), new Vector2(-138, 159));
            unitInfoTooltip.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
            unitInfoTooltipText = ItemLabel("Hint", unitInfoTooltip, "", 12, new Vector2(346, 157), Vector2.zero);
            unitInfoTooltipText.alignment = TextAnchor.MiddleLeft;
            unitInfoTooltip.gameObject.SetActive(false);
        }

        private UnityEngine.UI.Image BuildDetailMeter(string caption, float y, Sprite sprite, out UnityEngine.UI.Text value)
        {
            UnitDetailLabel(caption + "Label", unitInfoRoot, caption, 13, new Vector2(43, 19), new Vector2(-134, y), TextAnchor.MiddleLeft);
            value = UnitDetailLabel(caption + "Value", unitInfoRoot, "", 13, new Vector2(95, 19), new Vector2(-66, y));
            var track = DetailImage(caption + "Track", unitInfoRoot, new Vector2(167, 13), new Vector2(65, y), DetailSprite("MeterTrack"));
            var fill = DetailImage("Fill", track.transform, new Vector2(159, 8), Vector2.zero, sprite);
            ConfigureUnitMeter(fill);
            return fill;
        }

        private static string DetailRole(PokemonDefinition definition, PokemonStats stats)
        {
            switch (definition.RoleId)
            {
                case "melee-dps": return "근거리 딜러";
                case "ranged-dps": return "원거리 딜러";
                case "tank": return "탱커";
                case "support": return "지원가";
                case "bruiser": return "전사";
                case "caster": return "기술 딜러";
                case "carry": return stats.AttackRange > 1 ? "원거리 딜러" : "근거리 딜러";
                default: return KoreanInfo(definition.RoleId);
            }
        }

        private void RenderUnitPanelImages(PokemonDefinition definition, PokemonStats stats, string[] items, int? shopOwned)
        {
            var idle=unitPortraitLibrary?.Find(definition.Id,"Idle")??unitPortraitLibrary?.Find(PokeChess.Content.GameContent.BaseForm(definition.Id),"Idle");
            unitPortrait.sprite=idle?.Frames?.FirstOrDefault();unitPortrait.enabled=unitPortrait.sprite!=null;
            unitSkillImage.sprite=unitPanelLibrary?.GetSkillIcon(definition.SkillId)??Resources.Load<Sprite>("SkillIcons/"+definition.SkillId);
            unitSkillImage.enabled=unitSkillImage.sprite!=null;unitSkillMark.gameObject.SetActive(!unitSkillImage.enabled);
            unitRoleLabel.text = DetailRole(definition, stats);
            unitRoleBadgeIcon.sprite = definition.RoleId == "tank" ? DetailSprite("StatArmor")
                : DetailSprite(stats.AttackRange > 1 ? "RoleRangedIcon" : "RoleMeleeIcon");
            unitOwnedBall.gameObject.SetActive(shopOwned.HasValue);
            unitItemIcons.gameObject.SetActive(!shopOwned.HasValue);
            unitInfoItems.text = shopOwned.HasValue ? $"보유 {shopOwned.Value}마리" :
                items.Length == 0 ? "장착 도구 없음" : string.Join(" · ", items.Select(ItemCatalog.DisplayName));
            unitInfoItems.rectTransform.anchoredPosition = new Vector2(shopOwned.HasValue ? 82 : 32, 0);
            float cell = Mathf.Min(25f, 72f / Math.Max(1, items.Length));
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
                image.rectTransform.anchoredPosition=new Vector2((i-(items.Length-1)*.5f)*cell,0);
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
        private static string NatureGuardianSkillHint(int tier) => tier switch
        {
            2 => "수호자 포효\n주변 1칸의 적을 1.25초 도발하고 자신에게 3초 동안 보호막 120을 부여합니다.",
            4 => "수호자 포효\n주변 1칸의 적을 도발하고 2칸 안의 가까운 적 최대 3명을 속박합니다. 제어는 1.25초, 자신 보호막 200은 3초 동안 지속됩니다.",
            6 => "강화 수호자 포효\n주변 1칸의 적을 도발하고 2칸 안의 가까운 적 최대 4명을 속박합니다. 제어는 1.5초, 자신 보호막 300은 3초 동안 지속됩니다.",
            _ => "세계수의 심판\n주변 2칸의 적을 1.5초 도발합니다. 모든 적에게 특수 피해를 주고 아군의 잃은 체력 중 15%를 회복합니다."
        };

        private void RenderUnitInfoIcons(PokemonDefinition definition,UnitRank rank,PokemonStats stats,double hp,double maxHP,double energy,double maxEnergy,double shield,string[] items,bool baseStats=false,int natureTier=0,int? shopCost=null,int? shopOwned=null)
        {
            BuildUnitInfoIcons();bool has=definition!=null;unitInfoRoot.gameObject.SetActive(has);detailLabel.gameObject.SetActive(!has);
            if(!has){detailLabel.font=itemFont;detailLabel.text="포켓몬을 선택하세요\n보드 또는 벤치의 포켓몬을\n클릭하면 정보를 볼 수 있습니다.";HideUnitInfoHint();return;}
            unitDetailHeader.text = shopCost.HasValue ? "상점 상품" : "배치 유닛";
            unitInfoTitle.text = natureTier > 0 ? SynergySystem.GuardianForm(natureTier) : KoreanInfo(definition.Id);
            unitDetailRank.sprite = unitRankSprites[Mathf.Clamp((int)rank, 1, 3) - 1];
            unitDetailCost.text = $"{shopCost ?? definition.Cost}G";
            unitInfoTraits.text = natureTier > 0 ? "자연 소환수" : TraitList(definition);
            unitDetailHPText.text = $"{Number((float)hp)} / {Number((float)maxHP)}";
            if (shield > 0) unitDetailHPText.text += $" +{Number((float)shield)}";
            unitDetailManaText.text = $"{Number((float)energy)} / {Number((float)maxEnergy)}";
            unitDetailHP.fillAmount = maxHP > 0 ? Mathf.Clamp01((float)(hp / maxHP)) : 0;
            unitDetailMana.fillAmount = maxEnergy > 0 ? Mathf.Clamp01((float)(energy / maxEnergy)) : 0;
            string[] values = { Number(stats.Attack), Number(stats.SpellPower), Number(stats.Armor),
                Number(stats.MagicResistance), Number(stats.AttackSpeed), $"{stats.AttackRange}칸" };
            string[] hints = { "기본 공격의 피해량", "기술 효과의 주문력 비례 수치에 사용됩니다.", "물리 피해를 줄이는 능력치",
                "마법 피해를 줄이는 능력치", "초당 기본 공격 횟수", "기본 공격이 닿는 칸 수" };
            for (int i = 0; i < statNames.Length; i++)
            {
                unitStatValues[i].text = values[i];
                unitStatHints[i] = statNames[i] + "\n" + hints[i] + (baseStats ? "\n성급 기준 수치" : "");
            }
            RenderUnitPanelImages(definition, stats, items, shopOwned);
            unitSkillHint=natureTier>0?NatureGuardianSkillHint(natureTier):ContextSkill(definition,rank,stats);
            unitSkillHint=unitSkillHint.Replace(Readable(definition.SkillId),KoreanInfo(definition.SkillId));
            unitDetailSkill.text = natureTier > 0 ? unitSkillHint.Split('\n')[0] : KoreanInfo(definition.SkillId);
            foreach(var pair in new[]{("Lowest HP ally","체력이 가장 낮은 아군"),("Enemies r","범위 내 적 · 반경 "),("Allies r","범위 내 아군 · 반경 "),(" + splash r"," · 폭발 반경 "),("True damage","고정 피해"),("AttackSpeed","공격속도"),("Attack","공격력"),("SpellPower","주문력"),("Duration","지속 시간")})unitSkillHint=unitSkillHint.Replace(pair.Item1,pair.Item2);
            foreach(var pair in new[]{("Self","자신"),("Target","대상"),("Enemies","적"),("Allies","아군"),("Physical damage","물리 피해"),("Magic damage","마법 피해"),("Shield","보호막"),("Heal","회복"),("cast","시전"),("Stun","기절"),("Taunt","도발"),("Slow","둔화"),("AttackSpeed","공격속도"),("Armor","방어력"),("MagicResistance","마법저항")})unitSkillHint=unitSkillHint.Replace(pair.Item1,pair.Item2);
            if(hoveredUnitItem!=null){if(items.Contains(hoveredUnitItem))ShowEquippedUnitHint(hoveredUnitItem);else HideUnitInfoHint();}else if(hoveredUnitInfo!=-2)ShowUnitInfoHint(hoveredUnitInfo);
        }
    }
}

