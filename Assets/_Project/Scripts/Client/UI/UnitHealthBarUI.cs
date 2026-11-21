using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using PokeChess.Client.Data;
using PokeChess.Core.Battle;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;

namespace PokeChess.Client.UI
{
    public sealed partial class PlacementSandboxView
    {
        private const float UnitHealthHeight = 48, MeterWidth = 49, ManaOffset = -7, ItemOffset = -18;
        private const float ItemIconSize = 15, ItemIconSpacing = 17;
        private static Vector2 EquippedItemPosition(float barHeight) => new Vector2(-MeterWidth * .5f, barHeight + ItemOffset);
        private Sprite[] unitRankSprites;
        private Sprite unitVitalsFrame, unitAllyHP, unitEnemyHP, unitMana, unitShield, unitEquipment,
            unitBuffFrame, unitDebuffFrame, unitBuffIcon;
        private Dictionary<string, Sprite> unitCombatStatusSprites;
        private static readonly Color32 UnitOutline = new Color32(12, 20, 40, 255);

        private void LoadUnitOverheadSprites()
        {
            if (unitRankSprites != null) return;
            Sprite Load(string name) => Resources.Load<Sprite>("UnitOverhead/Unit" + name);
            unitRankSprites = new[] { Load("RankBronze"), Load("RankSilver"), Load("RankGold") };
            unitVitalsFrame = Load("VitalsFrame");
            unitAllyHP = Load("HpAllyFill");
            unitEnemyHP = Load("HpEnemyFill");
            unitMana = Load("ManaFill");
            unitShield = Load("ShieldFill");
            unitEquipment = Load("EquipmentFrame");
            unitBuffFrame = Load("BuffFrame");
            unitDebuffFrame = Load("DebuffFrame");
            unitBuffIcon = Load("BuffArrowExample");
            unitCombatStatusSprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);
            var visuals = Resources.Load<StatusVisualLibrary>("StatusVisuals");
            if (visuals == null) return;
            foreach (var icon in visuals.OverheadIcons)
                if (icon.Sprite != null) unitCombatStatusSprites[icon.Key] = icon.Sprite;
        }

        private UnityEngine.UI.Image UnitHudImage(string name, Transform parent, Vector2 size, Vector2 position, Sprite sprite)
        {
            var image = HudImage(name, parent, size, position, Color.white);
            image.sprite = sprite;
            return image;
        }

        private float UnitBarHeight(Transform unit)
        {
            var character = unit.Find("PokemonSprite")?.GetComponent<PokemonSpriteView>() ?? unit.GetComponentInChildren<PokemonSpriteView>();
            bool equipped = unit.Find("EquippedItems")?.gameObject.activeSelf == true;
            float clearance = equipped ? 29 : 15;
            return Mathf.Ceil(Mathf.Max(UnitHealthHeight, (character?.StableOverheadHeight ?? 0) + clearance));
        }

        private void RenderUnitRank(Transform unit, int rank, int evolutionStage, bool visible = true)
        {
            LoadUnitOverheadSprites();
            var badge = unit.Find("RankIndicator")?.GetComponent<UnityEngine.UI.Image>() ??
                UnitHudImage("RankIndicator", unit, new Vector2(19, 17), Vector2.zero, null);
            badge.sprite = unitRankSprites[Mathf.Clamp(rank, 1, 3) - 1];
            badge.rectTransform.anchoredPosition = new Vector2(-38, UnitBarHeight(unit) - 3);
            badge.gameObject.SetActive(visible && rank > 0);
            if (!badge.gameObject.activeSelf) return;
            var stage = badge.transform.Find("EvolutionStage")?.GetComponent<UnityEngine.UI.Text>();
            if (stage != null) stage.gameObject.SetActive(false);
            badge.transform.SetAsLastSibling();
        }

        private void RenderPreparationVitals(Transform unit, string definition, int rank, bool friendly)
        {
            var stats = RankRules.Default.Apply(catalog.Get(definition), (UnitRank)rank);
            RenderPreparationVitals(unit, stats, rank, PokeChess.Content.GameContent.Stage(definition), friendly);
        }

        private void RenderPreparationVitals(Transform unit, PokemonStats stats, int rank, int evolutionStage, bool friendly)
        {
            RenderUnitHealthBar(unit, stats.MaxHP, stats.MaxHP, friendly);
            RenderUnitEnergyBar(unit, stats.StartingEnergy, stats.MaxEnergy, true);
            RenderUnitRank(unit, rank, evolutionStage);
        }

        private void RenderEquippedItemIcon(Transform unit, IEnumerable<string> items, bool visible = true)
        {
            var row = unit.Find("EquippedItems") as RectTransform;
            int count = 0;
            if (visible && items != null)
            {
                foreach (string item in items)
                {
                    LoadUnitOverheadSprites();
                    row ??= Rect("EquippedItems", unit, Vector2.zero, Vector2.zero);
                    var frame = count < row.childCount ? row.GetChild(count).GetComponent<UnityEngine.UI.Image>() :
                        UnitHudImage("ItemSlot_" + count, row, new Vector2(ItemIconSize, ItemIconSize), Vector2.zero, unitEquipment);
                    var icon = frame.transform.Find("Item")?.GetComponent<UnityEngine.UI.Image>();
                    if (icon == null)
                    {
                        icon = UnitHudImage("Item", frame.transform, new Vector2(12, 12), Vector2.zero, null);
                        icon.preserveAspect = true;
                    }
                    string definition = ItemCatalog.DefinitionId(item);
                    icon.sprite = itemIcons?.Get(definition);
                    icon.enabled = icon.sprite != null;
                    var fallback = frame.transform.Find("Fallback")?.GetComponent<UnityEngine.UI.Text>();
                    if (!icon.enabled)
                    {
                        fallback ??= ItemLabel("Fallback", frame.transform, "", 8, new Vector2(14, 14), Vector2.zero);
                        fallback.text = definition == null ? "?" : ItemCatalog.Get(definition).Name.Substring(0, 1);
                    }
                    if (fallback != null) fallback.gameObject.SetActive(!icon.enabled);

                    frame.rectTransform.anchoredPosition = new Vector2(ItemIconSize * .5f + count * ItemIconSpacing, 0);
                    frame.gameObject.SetActive(true);
                    count++;
                }
            }

            if (row == null) return;
            for (int i = count; i < row.childCount; i++) row.GetChild(i).gameObject.SetActive(false);
            row.gameObject.SetActive(count > 0);
            if (count == 0) return;
            row.anchoredPosition = EquippedItemPosition(UnitBarHeight(unit));
            row.SetAsLastSibling();
        }

        private void RenderUnitEnergyBar(Transform fighter, double energy, double maxEnergy, bool alive)
        {
            var track = fighter.Find("HealthBar");
            if (track == null) return;
            var fill = track.Find("EnergyBar").GetComponent<UnityEngine.UI.Image>();
            fill.enabled = alive && maxEnergy > 0;
            fill.fillAmount = maxEnergy > 0 ? Mathf.Clamp01((float)(energy / maxEnergy)) : 0;
        }

        private RectTransform RenderUnitHealthBar(Transform fighter, double hp, double maxHP, bool friendly, double shield = 0)
        {
            LoadUnitOverheadSprites();
            var track = fighter.Find("HealthBar") as RectTransform;
            if (track == null)
            {
                track = Rect("HealthBar", fighter, new Vector2(55, 17), Vector2.zero);
                UnitHudImage("VitalsFrame", track, track.sizeDelta, new Vector2(0, -3), unitVitalsFrame);
                var fill = UnitHudImage("Fill", track, new Vector2(MeterWidth, 5.5f), new Vector2(0, 0), unitAllyHP);
                ConfigureUnitMeter(fill);
                var protection = UnitHudImage("Shield", track, new Vector2(0, 5.5f), Vector2.zero, unitShield);
                protection.rectTransform.pivot = new Vector2(0, .5f);
                var mana = UnitHudImage("EnergyBar", track, new Vector2(MeterWidth, 4.2f), new Vector2(0, ManaOffset), unitMana);
                ConfigureUnitMeter(mana);
            }

            float height = UnitBarHeight(fighter);
            track.anchoredPosition = new Vector2(0, height);
            track.gameObject.SetActive(hp > 0);
            var equipment = fighter.Find("EquippedItems") as RectTransform;
            if (equipment != null) equipment.anchoredPosition = EquippedItemPosition(height);
            var image = track.Find("Fill").GetComponent<UnityEngine.UI.Image>();
            double capacity = maxHP > 0 ? Math.Max(maxHP, Math.Max(0, hp) + Math.Max(0, shield)) : 0;
            float hpRatio = capacity > 0 ? Mathf.Clamp01((float)(hp / capacity)) : 0;
            float shieldRatio = capacity > 0 ? Mathf.Clamp01((float)(shield / capacity)) : 0;
            image.sprite = friendly ? unitAllyHP : unitEnemyHP;
            image.fillAmount = hpRatio;
            var shieldImage = track.Find("Shield").GetComponent<UnityEngine.UI.Image>();
            shieldImage.enabled = shieldRatio > 0;
            shieldImage.rectTransform.anchoredPosition = new Vector2(MeterWidth * (hpRatio - .5f), 0);
            shieldImage.rectTransform.sizeDelta = new Vector2(MeterWidth * shieldRatio, 5.5f);
            track.SetAsLastSibling();
            return track;
        }

        private static void ConfigureUnitMeter(UnityEngine.UI.Image image)
        {
            image.type = UnityEngine.UI.Image.Type.Filled;
            image.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
        }

        private void RenderLocalUnitStatuses(Transform unit, BattleState battle, UnitCombatState fighter)
        {
            RenderUnitStatusIcons(unit, fighter.IsAlive && fighter.IsOnBoard
                ? CombatStatusIcons.ForUnit(battle, fighter)
                : null);
        }

        private void RenderOnlineUnitStatuses(Transform unit, CombatUnitView fighter, double tick)
        {
            if (fighter.hp <= 0 || !fighter.onBoard)
            {
                RenderUnitStatusIcons(unit, null);
                return;
            }

            var definitions = PokeChess.Content.GameContent.Roster.Statuses;
            var statuses = fighter.statusDetails != null
                ? fighter.statusDetails.Where(s => s.startTick <= tick && s.endTick > tick)
                    .SelectMany(s => s.iconKeys ?? LegacyStatusIcons(s.definition, s.polarity > 0))
                : (fighter.statuses ?? Array.Empty<string>()).SelectMany(id => LegacyStatusIcons(id, false));

            RenderUnitStatusIcons(unit, statuses.Concat(fighter.otherStatusIcons ?? Array.Empty<string>()));

            IEnumerable<string> LegacyStatusIcons(string id, bool buff)
            {
                var definition = definitions.FirstOrDefault(d => d.Id == id);
                return definition != null
                    ? CombatStatusIcons.ForStatus(definition.ToCore())
                    : new[] { buff ? "buff" : "debuff" };
            }
        }

        private float UnitStatusStackHeight(Transform unit)
        {
            var row = unit.Find("StatusIcons") as RectTransform;
            return row != null && row.gameObject.activeSelf ? row.sizeDelta.y : 0;
        }

        private void RenderUnitStatusIcons(Transform unit,
            IEnumerable<string> statuses)
        {
            var row = unit.Find("StatusIcons") as RectTransform;
            var visible = statuses?.Distinct().ToArray();
            int count = visible?.Length ?? 0;
            if (count == 0)
            {
                if (row != null) row.gameObject.SetActive(false);
                return;
            }

            LoadUnitOverheadSprites();
            row ??= Rect("StatusIcons", unit, Vector2.zero, Vector2.zero);
            const int columns = 5;
            row.sizeDelta = new Vector2(0, ((count - 1) / columns) * 17);
            row.anchoredPosition = new Vector2(0, UnitBarHeight(unit) + 14);
            row.gameObject.SetActive(true);
            row.SetAsLastSibling();
            for (int i = 0; i < count; i++)
            {
                var status = visible[i];
                bool buff = CombatStatusIcons.UsesBuffFrame(status);
                var tile = i < row.childCount ? row.GetChild(i).GetComponent<UnityEngine.UI.Image>() :
                    UnitHudImage("StatusSlot_" + i, row, new Vector2(15, 15), Vector2.zero, null);
                var icon = tile.transform.Find("Icon")?.GetComponent<UnityEngine.UI.Image>();
                if (icon == null)
                {
                    icon = UnitHudImage("Icon", tile.transform, new Vector2(10, 10), Vector2.zero, null);
                    icon.preserveAspect = true;
                    Label("Fallback", tile.transform, "!", 10, new Vector2(13, 13), Vector2.zero);
                }
                icon.sprite = unitCombatStatusSprites.TryGetValue(status, out var sprite)
                    ? sprite : buff ? unitBuffIcon : null;
                icon.enabled = icon.sprite != null;
                tile.transform.Find("Fallback").gameObject.SetActive(!icon.enabled);
                tile.sprite = buff ? unitBuffFrame : unitDebuffFrame;
                int column = i % columns;
                int rowIndex = i / columns;
                int rowCount = Math.Min(columns, count - rowIndex * columns);
                tile.rectTransform.anchoredPosition = new Vector2((column - (rowCount - 1) * .5f) * 17, rowIndex * 17);
                tile.gameObject.SetActive(true);
            }
            for (int i = count; i < row.childCount; i++) row.GetChild(i).gameObject.SetActive(false);
        }
    }
}