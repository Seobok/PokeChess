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
        private Sprite[] unitRankSprites;
        private Sprite unitVitalsFrame, unitAllyHP, unitEnemyHP, unitMana, unitShield, unitEquipment,
            unitBuffFrame, unitDebuffFrame, unitBuffIcon, unitPoisonIcon;
        private StatusVisualLibrary unitStatusVisuals;
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
            unitPoisonIcon = Load("DebuffPoisonExample");
            unitStatusVisuals = Resources.Load<StatusVisualLibrary>("StatusVisuals");
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
            var image = character?.CharacterImage;
            float head = 0;
            if (image != null && image.enabled && image.sprite != null)
            {
                var rect = image.rectTransform;
                float scale = rect.rect.height / image.sprite.rect.height;
                head = rect.anchoredPosition.y + image.sprite.bounds.max.y * image.sprite.pixelsPerUnit * scale;
            }
            bool equipped = unit.Find("EquippedItemIcon")?.gameObject.activeSelf == true;
            float headEffect = unit.GetComponentInChildren<StatusSpriteView>()?.Top ?? 0;
            float clearance = equipped ? 29 : 15;
            return Mathf.Max(UnitHealthHeight, Mathf.Max(head, headEffect) + clearance);
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
            if (stage == null && evolutionStage > 0)
                stage = Label("EvolutionStage", badge.transform, "", 8, new Vector2(22, 11), new Vector2(0, -13));
            if (stage != null)
            {
                stage.text = $"E{evolutionStage}";
                stage.gameObject.SetActive(evolutionStage > 0);
            }
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
            Sprite sprite = null;
            if (visible && items != null)
                foreach (var item in items)
                {
                    sprite = itemIcons?.Get(ItemCatalog.DefinitionId(item));
                    if (sprite != null) break;
                }
            var frame = unit.Find("EquippedItemIcon")?.GetComponent<UnityEngine.UI.Image>();
            if (sprite == null)
            {
                if (frame != null) frame.gameObject.SetActive(false);
                return;
            }
            if (frame == null)
            {
                LoadUnitOverheadSprites();
                frame = UnitHudImage("EquippedItemIcon", unit, new Vector2(16, 16), Vector2.zero, unitEquipment);
                UnitHudImage("Item", frame.transform, new Vector2(12, 12), Vector2.zero, null).preserveAspect = true;
            }
            frame.transform.Find("Item").GetComponent<UnityEngine.UI.Image>().sprite = sprite;
            frame.gameObject.SetActive(true);
            frame.rectTransform.anchoredPosition = new Vector2(0, UnitBarHeight(unit) + ItemOffset);
            frame.transform.SetAsLastSibling();
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
            var item = fighter.Find("EquippedItemIcon") as RectTransform;
            if (item != null) item.anchoredPosition = new Vector2(0, height + ItemOffset);
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
            var statuses = fighter.IsAlive && fighter.IsOnBoard
                ? battle.StatusEffects.Active.Where(s => s.TargetId == fighter.UnitInstanceId &&
                    s.StartTick <= battle.CurrentTick && s.ExpireTick > battle.CurrentTick)
                    .Select(s => (s.Definition.Id, s.Definition.TargetTeam == StatusTargetTeam.Ally,
                        s.Definition.Behavior, s.Definition.CrowdControl))
                : null;
            RenderUnitStatusIcons(unit, statuses);
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
                ? fighter.statusDetails.Where(s => s.startTick <= tick && s.endTick > tick).Select(s =>
                    (s.definition, s.polarity != 0 ? s.polarity > 0 :
                        definitions.FirstOrDefault(d => d.Id == s.definition)?.TargetTeam == StatusTargetTeam.Ally,
                        (StatusBehavior)s.behavior, (CrowdControlKind)s.crowdControl))
                : (fighter.statuses ?? Array.Empty<string>()).Select(id =>
                {
                    var definition = definitions.FirstOrDefault(d => d.Id == id);
                    return (id, definition?.TargetTeam == StatusTargetTeam.Ally,
                        definition?.Behavior ?? StatusBehavior.None, definition?.CrowdControl ?? CrowdControlKind.None);
                });
            RenderUnitStatusIcons(unit, statuses);
        }

        private float UnitStatusStackHeight(Transform unit)
        {
            var row = unit.Find("StatusIcons") as RectTransform;
            return row != null && row.gameObject.activeSelf ? row.sizeDelta.y : 0;
        }

        private void RenderUnitStatusIcons(Transform unit,
            IEnumerable<(string id, bool buff, StatusBehavior behavior, CrowdControlKind control)> statuses)
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
                var tile = i < row.childCount ? row.GetChild(i).GetComponent<UnityEngine.UI.Image>() :
                    UnitHudImage("StatusSlot_" + i, row, new Vector2(15, 15), Vector2.zero, null);
                var icon = tile.transform.Find("Icon")?.GetComponent<UnityEngine.UI.Image>();
                if (icon == null)
                {
                    icon = UnitHudImage("Icon", tile.transform, new Vector2(10, 10), Vector2.zero, null);
                    icon.preserveAspect = true;
                    Label("Fallback", tile.transform, "!", 10, new Vector2(13, 13), Vector2.zero);
                }
                var art = unitStatusVisuals?.Effects.FirstOrDefault(effect => effect.Frames.Length > 0 &&
                    (status.control != CrowdControlKind.None ? effect.CrowdControl == status.control :
                        status.behavior != StatusBehavior.None && effect.Behavior == status.behavior));
                icon.sprite = status.behavior == StatusBehavior.Poison ? unitPoisonIcon :
                    art?.Frames[0] ?? (status.buff ? unitBuffIcon : null);
                icon.enabled = icon.sprite != null;
                tile.transform.Find("Fallback").gameObject.SetActive(!icon.enabled);
                tile.sprite = status.buff ? unitBuffFrame : unitDebuffFrame;
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