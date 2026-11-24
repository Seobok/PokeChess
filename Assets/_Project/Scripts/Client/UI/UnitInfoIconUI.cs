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
        private UnityEngine.UI.Image unitCostIcon;
        private UnityEngine.UI.Image unitDetailRank, unitRoleBadgeIcon, unitDetailHP, unitDetailMana, unitOwnedBall;
        private readonly UnityEngine.UI.Text[] unitStatValues=new UnityEngine.UI.Text[6];
        private string[] unitStatHints=new string[6];
        private string unitSkillHint;
        private UnityEngine.UI.Image unitPortrait,unitSkillImage;
        private Vector2 unitPortraitAreaSize, unitPortraitAreaCenter;
        private readonly Dictionary<Sprite, Bounds> unitPortraitVisibleBounds = new Dictionary<Sprite, Bounds>();
        private UnitPanelIconGraphic unitSkillMark;
        private RectTransform unitItemIcons;
        private readonly List<UnityEngine.UI.Image> unitEquippedIcons=new List<UnityEngine.UI.Image>();
        private string hoveredUnitItem;
        private string hoveredUnitTrait;
        private PokemonDefinition unitTraitDefinition;
        private bool unitTraitsAreSummon;
        private static readonly string[] NatureUnitTrait = { SynergyTraits.Nature };
        private IReadOnlyList<string> unitTraitIds = Array.Empty<string>();
        private readonly List<UnityEngine.UI.Text> unitTraitLabels = new List<UnityEngine.UI.Text>();
        private PokemonAnimationLibrary unitPortraitLibrary;
        private UnitInfoIconLibrary unitPanelLibrary;
        private int hoveredUnitInfo=-2;
        private readonly string[] statNames={"공격력","주문력","방어력","마법 저항력","공격 속도","사거리"};
        private static string KoreanInfo(string id)
        {
            var contentName=PokeChess.Content.GameContent.DisplayName(id);
            if(contentName!=null)return contentName;
            switch(id){
                case WildRoundSystem.BasicAttackSkillId:return "일반 공격";
                case "wild":return "야생 포켓몬";
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

        private void BindUnitInfoIcons()
        {
            unitPanelLibrary = Resources.Load<UnitInfoIconLibrary>("UnitInfoIcons");
            unitPortraitLibrary = PokemonAnimationLibrary.Load();
            LoadUnitOverheadSprites();

            detailPopup = UiPrefab.Instantiate("UnitPopup", canvasRect, itemFont);
            unitInfoRoot = UiPrefab.Get<RectTransform>(detailPopup, "UnitInfoIcons");
            unitInfoRoot.GetComponent<UnityEngine.UI.Image>().raycastTarget = true;
            AddSynergyRowEvent(unitInfoRoot.gameObject.AddComponent<EventTrigger>(), EventTriggerType.PointerExit,
                data => { if (equippedRecipeSource != null) EndItemHint((PointerEventData)data); });
            UiPrefab.Bind(unitInfoRoot, "Header/CloseButton", DeselectUnit);
            sellButton = UiPrefab.Bind(detailPopup, "Sell", SellSelected);
            unitDetailHeader = UiPrefab.Get<UnityEngine.UI.Text>(unitInfoRoot, "Header/Title");
            unitPortrait = UiPrefab.Get<UnityEngine.UI.Image>(unitInfoRoot, "UnitPortrait");
            unitPortraitAreaSize = unitPortrait.rectTransform.sizeDelta;
            unitPortraitAreaCenter = unitPortrait.rectTransform.anchoredPosition;
            unitPortrait.preserveAspect = false;
            unitPortrait.useSpriteMesh = true;
            unitInfoTitle = UiPrefab.Get<UnityEngine.UI.Text>(unitInfoRoot, "Name");
            unitDetailRank = UiPrefab.Get<UnityEngine.UI.Image>(unitInfoRoot, "Rank");
            unitCostIcon = UiPrefab.Get<UnityEngine.UI.Image>(unitInfoRoot, "CostIcon");
            unitRoleBadgeIcon = UiPrefab.Get<UnityEngine.UI.Image>(unitInfoRoot, "RoleBadge/Icon");
            unitRoleLabel = UiPrefab.Get<UnityEngine.UI.Text>(unitInfoRoot, "RoleBadge/Role");
            unitInfoTraits = UiPrefab.Get<UnityEngine.UI.Text>(unitInfoRoot, "TraitsBadge/Traits");
            var traitBadgeRect = (RectTransform)unitInfoTraits.transform.parent;
            traitBadgeRect.sizeDelta += new Vector2(0, 18);
            traitBadgeRect.anchoredPosition += Vector2.down * 9;
            unitInfoTraits.rectTransform.sizeDelta += new Vector2(0, 18);
            unitInfoTraits.enabled = false;
            unitDetailCost = UiPrefab.Get<UnityEngine.UI.Text>(unitInfoRoot, "CostBadge/Cost");
            unitDetailHP = UiPrefab.Get<UnityEngine.UI.Image>(unitInfoRoot, "HPTrack/Fill");
            unitDetailHPText = UiPrefab.Get<UnityEngine.UI.Text>(unitInfoRoot, "HPValue");
            unitDetailMana = UiPrefab.Get<UnityEngine.UI.Image>(unitInfoRoot, "마나Track/Fill");
            unitDetailManaText = UiPrefab.Get<UnityEngine.UI.Text>(unitInfoRoot, "마나Value");

            for (int i = 0; i < unitStatValues.Length; i++)
            {
                var cell = UiPrefab.Get<RectTransform>(unitInfoRoot, $"StatGrid/Stat_{i}");
                unitStatValues[i] = UiPrefab.Get<UnityEngine.UI.Text>(cell, "Value");
                AddUnitInfoHover(cell, i);
            }

            var skill = UiPrefab.Get<RectTransform>(unitInfoRoot, "SkillSlot");
            unitSkillMark = UiPrefab.Get<UnitPanelIconGraphic>(skill, "SkillMark");
            unitSkillImage = UiPrefab.Get<UnityEngine.UI.Image>(skill, "SkillIcon");
            unitDetailSkill = UiPrefab.Get<UnityEngine.UI.Text>(skill, "SkillName");
            AddUnitInfoHover(skill, -1);

            unitItemIcons = UiPrefab.Get<RectTransform>(unitInfoRoot, "Footer/EquippedItemSlot");
            unitOwnedBall = UiPrefab.Get<UnityEngine.UI.Image>(unitInfoRoot, "Footer/OwnedBall");
            unitInfoItems = UiPrefab.Get<UnityEngine.UI.Text>(unitInfoRoot, "Footer/FooterText");
            unitInfoTooltip = UiPrefab.Instantiate("UnitInfoTooltip", canvasRect, itemFont);
            unitInfoTooltipText = UiPrefab.Get<UnityEngine.UI.Text>(unitInfoTooltip, "Hint");
            BuildSkillTooltip();
        }

        private static string DetailRole(PokemonDefinition definition, PokemonStats stats)
        {
            switch (definition.RoleId)
            {
                case "ad-tank": return "공격력 탱커";
                case "ap-tank": return "주문력 탱커";
                case "ad-fighter": return "공격력 전사";
                case "ap-fighter": return "주문력 전사";
                case "ad-ranged": return "공격력 원거리 딜러";
                case "ap-ranged": return "주문력 원거리 딜러";
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

        private void FitUnitPortrait(Sprite sprite)
        {
            unitPortrait.sprite = sprite;
            unitPortrait.enabled = sprite != null;
            if (sprite == null) return;
            if (!unitPortraitVisibleBounds.TryGetValue(sprite, out var bounds))
            {
                // The tight sprite mesh excludes the transparent padding in animation cells.
                var vertices = sprite.vertices;
                bounds = vertices.Length > 0 ? new Bounds(vertices[0], Vector3.zero) : sprite.bounds;
                foreach (var vertex in vertices) bounds.Encapsulate(vertex);
                unitPortraitVisibleBounds.Add(sprite, bounds);
            }
            var visible = (Vector2)bounds.size * sprite.pixelsPerUnit;
            float scale = Mathf.Min(unitPortraitAreaSize.x * .9f / Mathf.Max(1, visible.x),
                unitPortraitAreaSize.y * .9f / Mathf.Max(1, visible.y));
            unitPortrait.rectTransform.sizeDelta = sprite.rect.size * scale;
            unitPortrait.rectTransform.anchoredPosition = unitPortraitAreaCenter -
                (Vector2)(bounds.center - sprite.bounds.center) * sprite.pixelsPerUnit * scale;
        }

        private void RenderUnitPanelImages(PokemonDefinition definition, PokemonStats stats, string[] items, int? shopOwned)
        {
            var idle=unitPortraitLibrary?.Find(definition.Id,"Idle")??unitPortraitLibrary?.Find(PokeChess.Content.GameContent.BaseForm(definition.Id),"Idle");
            FitUnitPortrait(idle?.Frames?.FirstOrDefault());
            unitSkillImage.sprite=unitPanelLibrary?.GetSkillIcon(definition.SkillId)??Resources.Load<Sprite>("SkillIcons/"+definition.SkillId);
            unitSkillImage.enabled=unitSkillImage.sprite!=null;unitSkillMark.gameObject.SetActive(!unitSkillImage.enabled);
            unitRoleLabel.text = DetailRole(definition, stats);
            string roleIcon = definition.RoleId switch
            {
                "tank" or "ad-tank" or "ap-tank" => "StatArmor",
                "ad-fighter" or "ap-fighter" => "RoleMeleeIcon",
                "ad-ranged" or "ap-ranged" => "RoleRangedIcon",
                _ => stats.AttackRange > 1 ? "RoleRangedIcon" : "RoleMeleeIcon"
            };
            unitRoleBadgeIcon.sprite = roleIcon == "StatArmor"
                ? UiPrefab.Get<UnityEngine.UI.Image>(unitInfoRoot, "StatGrid/Stat_2/Icon").sprite
                : DetailSprite(roleIcon);
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
                    enter.callback.AddListener(_ => ShowEquippedItemHover(unitEquippedIcons[slot]));
                    var leave=new EventTrigger.Entry{eventID=EventTriggerType.PointerExit};leave.callback.AddListener(data =>
                    {
                        if (equippedRecipeSource != null) EndItemHint((PointerEventData)data);
                        else HideUnitInfoHint();
                    });
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
        private void ShowEquippedItemHover(UnityEngine.UI.Image icon)
        {
            HideUnitInfoHint();
            var id = icon.GetComponent<UnitPanelItemId>().Id;
            if (id != null && ItemCatalog.IsComponent(id))
            {
                HideItemHint();
                ShowComponentRecipeHint(ItemCatalog.Get(ItemCatalog.DefinitionId(id)), -1, id, icon.rectTransform);
                return;
            }
            ShowEquippedUnitHint(id);
        }

        private void ShowEquippedUnitHint(string id)
        {
            hoveredUnitItem=id;hoveredUnitInfo=-2;
            string key=ItemCatalog.DefinitionId(id);
            if (key == null) return;
            var source = unitEquippedIcons.FirstOrDefault(image => image.gameObject.activeSelf &&
                image.GetComponent<UnitPanelItemId>().Id == id);
            if (source == null) return;
            hoveredComponentItem = id;
            equippedRecipeSource = source.rectTransform;
            var point = (Vector2)canvasRect.InverseTransformPoint(source.transform.position);
            UpdateItemRecipePreview(ItemCatalog.Get(key), point);
            itemRecipePreview.GetComponent<UnityEngine.UI.Image>().raycastTarget = true;
        }
        private void RenderUnitTraitLabels(PokemonDefinition definition, bool summon)
        {
            if (ReferenceEquals(unitTraitDefinition, definition) && unitTraitsAreSummon == summon) return;
            HideUnitTraitHint();
            unitTraitDefinition = definition;
            unitTraitsAreSummon = summon;
            var uniqueTraits = UniqueSynergies.Select((data, index) => (data, index))
                .Where(entry => entry.data.species.Any(id => SynergyTraits.Family(id) == SynergyTraits.Family(definition.Id))).ToArray();
            unitTraitIds = summon ? NatureUnitTrait : definition.TraitIds
                .Concat(uniqueTraits.Select(entry => "unique-" + entry.index)).ToArray();
            var names = unitTraitIds.Select(id => id.StartsWith("unique-", StringComparison.Ordinal)
                ? UniqueSynergies[int.Parse(id.Substring(7))].name : summon ? "자연 소환수" : TraitName(id)).ToArray();

            int count = unitTraitIds.Count;
            float totalWidth = unitInfoTraits.rectTransform.rect.width;
            int normalCount = summon ? 1 : definition.TraitIds.Count;
            float normalWeight = names.Take(normalCount).Sum(name => name.Length + 3);
            float left = -totalWidth * .5f;
            for (int i = 0; i < count; i++)
            {
                if (i == unitTraitLabels.Count)
                {
                    var label = ItemLabel("Trait_" + i, unitInfoTraits.transform, "", unitInfoTraits.fontSize, Vector2.zero, Vector2.zero);
                    label.font = unitInfoTraits.font;
                    label.fontStyle = unitInfoTraits.fontStyle;
                    label.color = DetailInk;
                    label.resizeTextForBestFit = true;
                    label.resizeTextMinSize = unitInfoTraits.resizeTextMinSize;
                    label.resizeTextMaxSize = unitInfoTraits.resizeTextMaxSize;
                    label.raycastTarget = true;
                    int index = i;
                    var events = label.gameObject.AddComponent<EventTrigger>();
                    AddSynergyRowEvent(events, EventTriggerType.PointerEnter, _ => ShowUnitTraitHint(unitTraitIds[index]));
                    AddSynergyRowEvent(events, EventTriggerType.PointerExit, _ => HideUnitTraitHint());
                    var iconView = HudImage("Icon", label.transform, new Vector2(22, 22), Vector2.zero, Color.white);
                    iconView.preserveAspect = true;
                    var nameView = ItemLabel("Name", label.transform, "", unitInfoTraits.fontSize, Vector2.zero, Vector2.zero);
                    nameView.font = label.font;nameView.color = label.color;nameView.alignment = TextAnchor.MiddleLeft;
                    nameView.resizeTextForBestFit = true;nameView.resizeTextMinSize = 9;nameView.resizeTextMaxSize = unitInfoTraits.fontSize;
                    nameView.raycastTarget = false;
                    var glyphView = ItemLabel("Glyph", label.transform, "", 16, new Vector2(22, 22), Vector2.zero);
                    glyphView.font = label.font;glyphView.color = label.color;glyphView.raycastTarget = false;
                    unitTraitLabels.Add(label);
                }

                var trait = unitTraitLabels[i];
                bool uniqueRow = i >= normalCount;
                float width = uniqueRow ? totalWidth : totalWidth * (names[i].Length + 3) / Mathf.Max(1, normalWeight);
                trait.text = "";
                trait.rectTransform.sizeDelta = new Vector2(width, 18);
                trait.rectTransform.anchoredPosition = new Vector2(uniqueRow ? 0 : left + width * .5f, uniqueRow ? -9 : 9);
                if (!uniqueRow) left += width;
                var icon = trait.transform.Find("Icon").GetComponent<UnityEngine.UI.Image>();
                icon.rectTransform.anchoredPosition = new Vector2(-width * .5f + 12, 0);
                icon.sprite = synergyPanelIcons?.Find(unitTraitIds[i]);
                icon.enabled = icon.sprite != null;
                var name = trait.transform.Find("Name").GetComponent<UnityEngine.UI.Text>();
                name.text = names[i];
                name.rectTransform.sizeDelta = new Vector2(Mathf.Max(1, width - 28), 18);
                name.rectTransform.anchoredPosition = new Vector2(13, 0);
                var glyph = trait.transform.Find("Glyph").GetComponent<UnityEngine.UI.Text>();
                glyph.gameObject.SetActive(!icon.enabled);
                glyph.text = SynergyMark(unitTraitIds[i], names[i]);
                glyph.rectTransform.anchoredPosition = icon.rectTransform.anchoredPosition;
                trait.gameObject.SetActive(true);
            }
            for (int i = count; i < unitTraitLabels.Count; i++)
                unitTraitLabels[i].gameObject.SetActive(false);
        }

        private void ShowUnitTraitHint(string trait)
        {
            HideUnitInfoHint();
            hoveredUnitTrait = trait;
            RefreshSynergyRows();
        }

        private void HideUnitTraitHint()
        {
            if (hoveredUnitTrait == null) return;
            hoveredUnitTrait = null;
            RefreshSynergyRows();
        }
        private void AddUnitInfoHover(Transform target,int index)
        {
            var trigger=target.gameObject.AddComponent<EventTrigger>();
            var enter=new EventTrigger.Entry{eventID=EventTriggerType.PointerEnter};enter.callback.AddListener(_=>ShowUnitInfoHint(index));
            var exit=new EventTrigger.Entry{eventID=EventTriggerType.PointerExit};exit.callback.AddListener(_=>HideUnitInfoHint());trigger.triggers.Add(enter);trigger.triggers.Add(exit);
        }
        private void ShowUnitInfoHint(int index)
        {
            if(equippedRecipeSource!=null)HideItemHint();
            hoveredUnitTrait=null;hoveredUnitInfo=index;
            if (index < 0)
            {
                unitInfoTooltip.gameObject.SetActive(false);
                ShowSkillTooltip();
                return;
            }
            skillTooltip.gameObject.SetActive(false);
            unitInfoTooltip.sizeDelta=new Vector2(370,90);
            unitInfoTooltipText.rectTransform.sizeDelta=new Vector2(346,72);
            unitInfoTooltipText.text=unitStatHints[index];
            unitInfoTooltip.gameObject.SetActive(true);unitInfoTooltip.SetAsLastSibling();
        }
        private void HideUnitInfoHint(){if(equippedRecipeSource!=null)HideItemHint();hoveredUnitItem=null;hoveredUnitTrait=null;hoveredUnitInfo=-2;if(unitInfoTooltip!=null)unitInfoTooltip.gameObject.SetActive(false);if(skillTooltip!=null)skillTooltip.gameObject.SetActive(false);}
        private static string NatureGuardianSkillHint(int tier) => tier switch
        {
            2 => "수호자 포효\n주변 1칸의 적을 1.25초 도발하고 자신에게 3초 동안 보호막 120을 부여합니다.",
            4 => "수호자 포효\n주변 1칸의 적을 도발하고 2칸 안의 가까운 적 최대 3명을 속박합니다. 제어는 1.25초, 자신 보호막 200은 3초 동안 지속됩니다.",
            6 => "강화 수호자 포효\n주변 1칸의 적을 도발하고 2칸 안의 가까운 적 최대 4명을 속박합니다. 제어는 1.5초, 자신 보호막 300은 3초 동안 지속됩니다.",
            _ => "세계수의 심판\n주변 2칸의 적을 1.5초 도발합니다. 모든 적에게 특수 피해를 주고 아군의 잃은 체력 중 15%를 회복합니다."
        };

        private void RenderUnitInfoIcons(PokemonDefinition definition,UnitRank rank,PokemonStats stats,double hp,double maxHP,double energy,double maxEnergy,double shield,string[] items,bool baseStats=false,int natureTier=0,int? shopCost=null,int? shopOwned=null,int? saleGold=null)
        {
            bool has=definition!=null;unitInfoRoot.gameObject.SetActive(has);detailLabel.gameObject.SetActive(!has);
            if(!has){detailLabel.font=itemFont;detailLabel.text="포켓몬을 선택하세요\n보드 또는 벤치의 포켓몬을\n클릭하면 정보를 볼 수 있습니다.";HideUnitInfoHint();return;}
            unitDetailHeader.text = shopCost.HasValue ? "상점 상품" : "배치 유닛";
            unitInfoTitle.text = natureTier > 0 ? SynergySystem.GuardianForm(natureTier) : KoreanInfo(definition.Id);
            unitDetailRank.sprite = unitRankSprites[Mathf.Clamp((int)rank, 1, 3) - 1];
            unitCostIcon.sprite = synergyPanelIcons?.FindCostBallIcon(definition.Cost);
            unitCostIcon.enabled = unitCostIcon.sprite != null;
            unitDetailCost.text = saleGold.HasValue ? $"{saleGold.Value}G" : shopCost.HasValue ? $"{shopCost.Value}G" : "—";
            RenderUnitTraitLabels(definition, natureTier > 0);
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
            if (equippedRecipeSource != null && (!equippedRecipeSource.gameObject.activeSelf ||
                equippedRecipeSource.GetComponent<UnitPanelItemId>().Id != hoveredComponentItem))
                HideItemHint();
            unitSkillHint=natureTier>0?NatureGuardianSkillHint(natureTier):
                (PokeChess.Content.GameContent.FindPokemon(definition.Id)?.Skill is PokeChess.Content.SkillData skillData
                    ? PokeChess.Content.SkillDescription.Format(skillData,rank,stats,contextStatuses) : ContextSkill(definition,rank,stats));
            unitSkillHint=unitSkillHint.Replace(Readable(definition.SkillId),KoreanInfo(definition.SkillId));
            unitDetailSkill.text = natureTier > 0 ? unitSkillHint.Split('\n')[0] : KoreanInfo(definition.SkillId);
            foreach(var pair in new[]{("Lowest HP ally","체력이 가장 낮은 아군"),("Enemies r","범위 내 적 · 반경 "),("Allies r","범위 내 아군 · 반경 "),(" + splash r"," · 폭발 반경 "),("True damage","고정 피해"),("AttackSpeed","공격속도"),("Attack","공격력"),("SpellPower","주문력"),("Duration","지속 시간")})unitSkillHint=unitSkillHint.Replace(pair.Item1,pair.Item2);
            foreach(var pair in new[]{("Self","자신"),("Target","대상"),("Enemies","적"),("Allies","아군"),("Physical damage","물리 피해"),("Magic damage","마법 피해"),("Shield","보호막"),("Heal","회복"),("cast","시전"),("Stun","기절"),("Taunt","도발"),("Slow","둔화"),("AttackSpeed","공격속도"),("Armor","방어력"),("MagicResistance","마법저항")})unitSkillHint=unitSkillHint.Replace(pair.Item1,pair.Item2);
            if(hoveredUnitItem!=null){if(items.Contains(hoveredUnitItem))ShowEquippedUnitHint(hoveredUnitItem);else HideUnitInfoHint();}else if(hoveredUnitInfo!=-2)ShowUnitInfoHint(hoveredUnitInfo);
        }
    }
}

