using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using PokeChess.Core.Pokemon;

namespace PokeChess.Client.UI
{
    public sealed partial class PlacementSandboxView
    {
        private const int ToolsPerPage = 7;
        private int inventoryPage;
        private UnityEngine.UI.Button inventoryPrev, inventoryNext;
        private UnityEngine.UI.Text inventoryPageText;
        private Sprite inventoryToolSlot, inventoryEmptySlot, inventoryMoonStoneSlot, inventoryMoonStone;
        private Sprite evolutionBoardTarget, evolutionBoardHovered, evolutionBenchTarget, evolutionBenchHovered;
        private readonly Dictionary<Slot, UnityEngine.UI.Image> evolutionMarks = new Dictionary<Slot, UnityEngine.UI.Image>();
        private readonly Dictionary<Slot, PokemonDefinition> evolutionTargets = new Dictionary<Slot, PokemonDefinition>();
        private RectTransform evolutionDragPreview;
        private EvolutionCard primaryEvolutionCard, nextEvolutionCard;
        private Slot evolutionHoveredSlot;

        private sealed class EvolutionCard
        {
            public RectTransform Root;
            public UnityEngine.UI.Text Header, Forms, Skill, Cost;
            public UnityEngine.UI.Image From, To, SkillIcon;
        }

        private static Sprite InventorySprite(string name) => Resources.Load<Sprite>("InventoryEvolution/" + name);

        private UnityEngine.UI.Image InventoryImage(string name, Transform parent, Vector2 size, Vector2 position, Sprite sprite)
        {
            var image = HudImage(name, parent, size, position, Color.white);
            image.sprite = sprite;
            return image;
        }

        private void BindInventoryArt()
        {
            inventoryToolSlot = InventorySprite("InventoryToolSlot");
            inventoryEmptySlot = InventorySprite("InventoryEmptySlot");
            inventoryMoonStoneSlot = InventorySprite("InventoryMoonStoneSlot");
            inventoryMoonStone = InventorySprite("InventoryMoonStone");

            itemInventoryPanel = UiPrefab.Instantiate("ItemIcons", canvasRect, itemFont);
            inventoryPageText = UiPrefab.Get<UnityEngine.UI.Text>(itemInventoryPanel, "PageBadge/Page");
            for (int i = 0; i < inventoryChoices.Length; i++)
            {
                var button = inventoryChoices[i] = UiPrefab.Get<UnityEngine.UI.Button>(itemInventoryPanel, $"InventoryItem_{i}");
                var drag = button.GetComponent<ItemDragToken>();
                drag.View = this;
                drag.Slot = i;
            }
            inventoryPrev = BindInventoryPageButton("PreviousPage", -1);
            inventoryNext = BindInventoryPageButton("NextPage", 1);
        }

        private UnityEngine.UI.Button BindInventoryPageButton(string name, int direction)
        {
            var button = UiPrefab.Get<UnityEngine.UI.Button>(itemInventoryPanel, name);
            button.onClick.AddListener(() =>
            {
                if (IsItemDragging || !button.interactable) return;
                inventoryPage += direction;
                HideItemHint();
                RenderItemWildUI();
            });
            return button;
        }

        private void BindEvolutionDragPreview()
        {
            evolutionBoardTarget = InventorySprite("EvolutionBoardTarget");
            evolutionBoardHovered = InventorySprite("EvolutionBoardHovered");
            evolutionBenchTarget = InventorySprite("EvolutionBenchTarget");
            evolutionBenchHovered = InventorySprite("EvolutionBenchHovered");
            evolutionDragPreview = UiPrefab.Instantiate("MoonStonePreview", canvasRect, itemFont);
            primaryEvolutionCard = BindEvolutionCard("CurrentEvolution", true);
            nextEvolutionCard = BindEvolutionCard("NextEvolution", false);
        }

        private EvolutionCard BindEvolutionCard(string name, bool primary)
        {
            var root = UiPrefab.Get<RectTransform>(evolutionDragPreview, name);
            return new EvolutionCard
            {
                Root = root,
                Header = UiPrefab.Get<UnityEngine.UI.Text>(root, "Title"),
                From = UiPrefab.Get<UnityEngine.UI.Image>(root, "From"),
                To = UiPrefab.Get<UnityEngine.UI.Image>(root, "To"),
                SkillIcon = UiPrefab.Get<UnityEngine.UI.Image>(root, "SkillIcon"),
                Skill = UiPrefab.Get<UnityEngine.UI.Text>(root, "Skill"),
                Cost = primary ? UiPrefab.Get<UnityEngine.UI.Text>(root, "Cost") : null,
                Forms = primary ? null : UiPrefab.Get<UnityEngine.UI.Text>(root, "Forms")
            };
        }

        private void SetEvolutionCard(EvolutionCard card, PokemonDefinition from, PokemonDefinition to, int cost)
        {
            string forms = KoreanInfo(from.Id) + " → " + KoreanInfo(to.Id);
            if (card.Forms == null) card.Header.text = forms;
            else card.Forms.text = forms;
            SetEvolutionPortrait(card.From, from.Id);
            SetEvolutionPortrait(card.To, to.Id);
            card.SkillIcon.sprite = unitPanelLibrary?.GetSkillIcon(to.SkillId) ?? Resources.Load<Sprite>("SkillIcons/" + to.SkillId);
            card.SkillIcon.enabled = card.SkillIcon.sprite != null;
            card.Skill.text = (card.Cost != null ? "진화 후 기술 · " : "") + KoreanInfo(to.SkillId);
            if (card.Cost != null) card.Cost.text = $"{cost} EP";
        }

        private void SetEvolutionPortrait(UnityEngine.UI.Image image, string id)
        {
            var idle = unitPortraitLibrary?.Find(id, "Idle")
                ?? unitPortraitLibrary?.Find(PokeChess.Content.GameContent.BaseForm(id), "Idle");
            image.sprite = idle?.Frames?.FirstOrDefault();
            image.enabled = image.sprite != null;
        }

        private void BeginEvolutionHighlights()
        {
            if (IsOnlineMatch)
            {
                foreach (var unit in onlineService.LocalMatchState.units)
                    if (CanEvolveTarget(unit.id))
                        evolutionTargets.Add(FindSlot(OnlinePlacement(unit)), catalog.Get(unit.definitionId));
            }
            else
            {
                foreach (var unit in Player.Units)
                    if (CanEvolveTarget(unit.InstanceId))
                        evolutionTargets.Add(FindSlot(unit.Placement), catalog.Get(unit.DefinitionId));
            }
            var layer = canvasRect.Find("PlacementArt");
            foreach (var slot in evolutionTargets.Keys)
            {
                if (!evolutionMarks.TryGetValue(slot, out var mark))
                {
                    mark = InventoryImage("EvolutionTarget", layer, slot.Rect.sizeDelta, slot.Rect.anchoredPosition, null);
                    evolutionMarks.Add(slot, mark);
                }
                mark.sprite = slot.Placement.Kind == PlacementKind.Board ? evolutionBoardTarget : evolutionBenchTarget;
                mark.gameObject.SetActive(true);
            }
        }

        private Slot EvolutionSlotAt(Vector2 point, Slot tile)
        {
            if (ItemTarget(tile) != null) return tile;
            Slot hit = null;
            int frontmost = -1;
            foreach (var slot in evolutionTargets.Keys)
            {
                var id = ItemTarget(slot);
                if (id == null || !tokens.TryGetValue(id, out var token) || !token.gameObject.activeInHierarchy) continue;
                var sprite = token.Find("PokemonSprite")?.GetComponent<PokemonSpriteView>()?.CharacterImage;
                if (sprite == null || !sprite.enabled || !RectTransformUtility.RectangleContainsScreenPoint(sprite.rectTransform, point, null)) continue;
                int order = token.GetSiblingIndex();
                if (order <= frontmost) continue;
                frontmost = order;
                hit = slot;
            }
            return hit;
        }

        private void UpdateEvolutionHover(Slot slot)
        {
            if (slot == evolutionHoveredSlot) return;
            if (evolutionHoveredSlot != null && evolutionMarks.TryGetValue(evolutionHoveredSlot, out var previous))
                previous.sprite = evolutionHoveredSlot.Placement.Kind == PlacementKind.Board ? evolutionBoardTarget : evolutionBenchTarget;
            evolutionHoveredSlot = slot;
            if (slot == null || !evolutionTargets.TryGetValue(slot, out var definition))
            {
                evolutionDragPreview.gameObject.SetActive(false);
                return;
            }

            evolutionMarks[slot].sprite = slot.Placement.Kind == PlacementKind.Board ? evolutionBoardHovered : evolutionBenchHovered;
            var option = definition.Evolutions[0];
            var target = catalog.Get(option.TargetDefinitionId);
            SetEvolutionCard(primaryEvolutionCard, definition, target, option.EvolutionPointCost);
            bool hasNext = target.Evolutions.Count == 1;
            nextEvolutionCard.Root.gameObject.SetActive(hasNext);
            if (hasNext)
            {
                var next = target.Evolutions[0];
                SetEvolutionCard(nextEvolutionCard, target, catalog.Get(next.TargetDefinitionId), next.EvolutionPointCost);
            }
            var targetPoint = slot.Rect.anchoredPosition;
            float x = targetPoint.x + (targetPoint.x > 65 ? -155 : 155);
            float y = Mathf.Clamp(targetPoint.y + 55, hasNext ? 62 : -43, 230);
            evolutionDragPreview.anchoredPosition = new Vector2(Mathf.Clamp(x, -200, 250), y);
            evolutionDragPreview.gameObject.SetActive(true);
            evolutionDragPreview.SetAsLastSibling();
            itemDragGhost.SetAsLastSibling();
        }

        private void ClearEvolutionHighlights()
        {
            foreach (var mark in evolutionMarks.Values) mark.gameObject.SetActive(false);
            evolutionTargets.Clear();
            evolutionHoveredSlot = null;
            if (evolutionDragPreview != null) evolutionDragPreview.gameObject.SetActive(false);
        }
    }
}