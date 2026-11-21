using System;
using PokeChess.Core.Match;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PokeChess.Client.UI
{
    public sealed partial class PlacementSandboxView
    {
        private const float RecipeRowHeight = 25;
        private RectTransform componentRecipes;
        private UnityEngine.UI.Text componentRecipeTitle;
        private UnityEngine.UI.Text componentRecipeStats;
        private UnityEngine.UI.Image componentRecipeIcon;
        private RectTransform componentRecipeHeader;
        private ComponentRecipeRow highlightedRecipe;
        private ComponentRecipeRow[] componentRecipeRows;
        private int hoveredComponentSlot;
        private string hoveredComponentItem;
        private RectTransform equippedRecipeSource;

        private sealed class ComponentRecipeRow
        {
            public RectTransform Rect;
            public UnityEngine.UI.Image Ingredient, Result;
            public UnityEngine.UI.Text Name;
            public ItemDefinition Recipe;
        }

        private void StyleItemSurface(UnityEngine.UI.Image image, Sprite sprite)
        {
            image.sprite = sprite;
            image.type = UnityEngine.UI.Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 6;
            image.color = Color.white;
        }

        private void HighlightRecipe(ComponentRecipeRow row)
        {
            if (highlightedRecipe != null)
                StyleItemSurface(highlightedRecipe.Rect.GetComponent<UnityEngine.UI.Image>(), itemIcons.recipeRow);
            highlightedRecipe = row;
            if (row != null)
                StyleItemSurface(row.Rect.GetComponent<UnityEngine.UI.Image>(), itemIcons.recipeRowHover);
        }

        private void BuildComponentRecipeHint()
        {
            componentRecipes = HudImage("ComponentRecipes", canvasRect, new Vector2(230, 0), Vector2.zero,
                new Color(.045f, .085f, .15f, .98f)).rectTransform;
            componentRecipes.GetComponent<UnityEngine.UI.Image>().raycastTarget = true;
            StyleItemSurface(componentRecipes.GetComponent<UnityEngine.UI.Image>(), itemIcons.tooltipPanel);
            componentRecipeHeader = HudImage("Header", componentRecipes, new Vector2(218, 30), Vector2.zero, Color.white).rectTransform;
            StyleItemSurface(componentRecipeHeader.GetComponent<UnityEngine.UI.Image>(), itemIcons.tooltipHeader);
            componentRecipeIcon = HudImage("ComponentIcon", componentRecipes, new Vector2(28, 28), Vector2.zero, Color.white);
            componentRecipeIcon.preserveAspect = true;
            componentRecipeTitle = RecipeListText("Component", componentRecipes, 13, new Vector2(171, 46), Vector2.zero);
            componentRecipeTitle.fontStyle = FontStyle.Bold;
            componentRecipeTitle.color = Color.white;
            componentRecipeTitle.rectTransform.sizeDelta = new Vector2(165, 26);
            componentRecipeStats = RecipeListText("Stats", componentRecipes, 11, new Vector2(204, 38), Vector2.zero);

            componentRecipeRows = new ComponentRecipeRow[ItemCatalog.Components.Count];
            for (int i = 0; i < componentRecipeRows.Length; i++)
            {
                var image = HudImage("Recipe_" + i, componentRecipes, new Vector2(componentRecipes.rect.width, RecipeRowHeight - 2),
                    Vector2.zero, new Color(.12f, .2f, .31f));
                image.raycastTarget = true;
                image.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 214);
                StyleItemSurface(image, itemIcons.recipeRow);
                var row = componentRecipeRows[i] = new ComponentRecipeRow { Rect = image.rectTransform };
                row.Ingredient = HudImage("Ingredient", row.Rect, new Vector2(20, 20), new Vector2(-76, 0), Color.white);
                row.Result = HudImage("Result", row.Rect, new Vector2(20, 20), new Vector2(-37, 0), Color.white);
                row.Ingredient.preserveAspect = row.Result.preserveAspect = true;
                ItemLabel("Plus", row.Rect, "+", 12, new Vector2(14, 22), new Vector2(-96, 0)).color = new Color(.08f, .12f, .23f);
                ItemLabel("Arrow", row.Rect, "→", 12, new Vector2(15, 22), new Vector2(-57, 0)).color = new Color(.08f, .12f, .23f);
                row.Name = RecipeListText("Name", row.Rect, 12, new Vector2(119, 21), new Vector2(37, 0));
                var events = row.Rect.gameObject.AddComponent<EventTrigger>();
                AddSynergyRowEvent(events, EventTriggerType.PointerEnter, _ => ShowHoveredRecipe(row));
                AddSynergyRowEvent(events, EventTriggerType.PointerExit, data => EndRecipeRowHover((PointerEventData)data));
            }

            itemInventoryPanel.GetComponent<UnityEngine.UI.Image>().raycastTarget = true;
            AddSynergyRowEvent(itemInventoryPanel.gameObject.AddComponent<EventTrigger>(),
                EventTriggerType.PointerExit, data => EndItemHint((PointerEventData)data));
            AddSynergyRowEvent(componentRecipes.gameObject.AddComponent<EventTrigger>(),
                EventTriggerType.PointerExit, data => EndItemHint((PointerEventData)data));
            AddSynergyRowEvent(itemRecipePreview.gameObject.AddComponent<EventTrigger>(),
                EventTriggerType.PointerExit, data => EndRecipeDetailHover((PointerEventData)data));
            componentRecipes.gameObject.SetActive(false);
        }

        private UnityEngine.UI.Text RecipeListText(string name, Transform parent, int size, Vector2 bounds, Vector2 position)
        {
            var label = ItemLabel(name, parent, "", size, bounds, position);
            label.alignment = TextAnchor.MiddleLeft;
            label.color = new Color(.08f, .12f, .23f);
            return label;
        }

        private void ShowComponentRecipeHint(ItemDefinition component, int slot, string instanceId, RectTransform equippedIcon = null)
        {
            bool hasDescription = !string.IsNullOrWhiteSpace(component.Description) && component.Description.Trim() != "없음";
            float headerHeight = hasDescription ? 78 : 43;
            HighlightRecipe(null);
            componentRecipeTitle.text = component.Name;
            componentRecipeStats.text = hasDescription ? component.Description : "";
            componentRecipeStats.gameObject.SetActive(hasDescription);
            componentRecipeIcon.sprite = itemIcons?.Get(component.Id);
            componentRecipeIcon.enabled = componentRecipeIcon.sprite != null;
            int count = 0;
            foreach (var other in ItemCatalog.Components)
            {
                var result = ItemCatalog.Recipe(component.Id, other.Id);
                if (result == null) continue;
                var row = componentRecipeRows[count];
                row.Recipe = result;
                row.Rect.gameObject.SetActive(true);
                row.Name.text = result.Name;
                row.Ingredient.sprite = itemIcons?.Get(other.Id);
                row.Result.sprite = itemIcons?.Get(result.Id);
                row.Ingredient.enabled = row.Ingredient.sprite != null;
                row.Result.enabled = row.Result.sprite != null;
                count++;
            }
            for (int i = count; i < componentRecipeRows.Length; i++)
                componentRecipeRows[i].Rect.gameObject.SetActive(false);

            if (count == 0)
            {
                itemTooltipLabel.text = component.Name + "\n" + component.Description;
                itemTooltip.gameObject.SetActive(true);
                itemTooltip.SetAsLastSibling();
                return;
            }
            float height = headerHeight + count * RecipeRowHeight + 6;
            componentRecipes.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            componentRecipeTitle.rectTransform.anchoredPosition = new Vector2(19, height * .5f - 22);
            componentRecipeIcon.rectTransform.anchoredPosition = new Vector2(-87, height * .5f - 22);
            componentRecipeStats.rectTransform.anchoredPosition = new Vector2(0, height * .5f - 56);
            componentRecipeHeader.anchoredPosition = new Vector2(0, height * .5f - 22);
            componentRecipeTitle.rectTransform.SetAsLastSibling();
            componentRecipeIcon.rectTransform.SetAsLastSibling();
            for (int i = 0; i < count; i++)
                componentRecipeRows[i].Rect.anchoredPosition =
                    new Vector2(0, height * .5f - headerHeight - (i + .5f) * RecipeRowHeight);

            var bounds = canvasRect.rect;
            var sourcePanel = equippedIcon != null ? unitInfoRoot : itemInventoryPanel;
            var sourceIcon = equippedIcon != null ? equippedIcon : (RectTransform)inventoryChoices[slot].transform;
            var left = canvasRect.InverseTransformPoint(sourcePanel.TransformPoint(
                new Vector2(sourcePanel.rect.xMin, sourcePanel.rect.yMax)));
            var right = canvasRect.InverseTransformPoint(sourcePanel.TransformPoint(
                new Vector2(sourcePanel.rect.xMax, sourcePanel.rect.yMax)));
            var slotCenter = sourcePanel.InverseTransformPoint(sourceIcon.position);
            float width = componentRecipes.rect.width;
            float leftSpace = left.x - bounds.xMin - 4;
            bool openLeft = (equippedIcon != null || slotCenter.x < sourcePanel.rect.center.x) && leftSpace >= width * .7f;
            float scale = openLeft ? Mathf.Min(1, leftSpace / width) : 1;
            componentRecipes.localScale = new Vector3(scale, scale, 1);
            float x = openLeft ? left.x - width * scale : Mathf.Min(right.x, bounds.xMax - width);
            float slotY = canvasRect.InverseTransformPoint(sourceIcon.position).y;
            float top = Mathf.Clamp(slotY + height * scale * .5f, bounds.yMin + height * scale, bounds.yMax);
            componentRecipes.localPosition = new Vector3(x - componentRecipes.rect.xMin * scale,
                top - componentRecipes.rect.yMax * scale, 0);
            equippedRecipeSource = equippedIcon;
            hoveredComponentSlot = slot;
            hoveredComponentItem = instanceId;
            componentRecipes.gameObject.SetActive(true);
            componentRecipes.SetAsLastSibling();
        }

        private void ShowHoveredRecipe(ComponentRecipeRow row)
        {
            HighlightRecipe(row);
            var point = canvasRect.InverseTransformPoint(componentRecipes.TransformPoint(
                new Vector2(componentRecipes.rect.xMax, row.Rect.anchoredPosition.y + row.Rect.rect.yMax)));
            UpdateItemRecipePreview(row.Recipe, point, besideRecipeList: true);
        }

        internal void EndItemHint(PointerEventData data)
        {
            var hit = data.pointerCurrentRaycast.gameObject;
            if (hoveredComponentItem != null && hit != null &&
                (hit.transform.IsChildOf(equippedRecipeSource != null ? unitInfoRoot : itemInventoryPanel) || hit.transform.IsChildOf(componentRecipes) ||
                    hit.transform.IsChildOf(itemRecipePreview))) return;
            HideItemHint();
        }

        private void EndRecipeRowHover(PointerEventData data)
        {
            var hit = data.pointerCurrentRaycast.gameObject;
            if (hit == null || !hit.transform.IsChildOf(itemRecipePreview))
            {
                HighlightRecipe(null);
                UpdateItemRecipePreview(null, Vector2.zero);
            }
        }

        private void EndRecipeDetailHover(PointerEventData data)
        {
            if (hoveredComponentItem == null) return;
            UpdateItemRecipePreview(null, Vector2.zero);
            EndItemHint(data);
        }

        private void ValidateComponentRecipeHint()
        {
            if (hoveredComponentItem == null || equippedRecipeSource != null) return;
            int index = InventoryIndex(hoveredComponentSlot);
            if (!itemInventoryPanel.gameObject.activeInHierarchy || index >= visibleInventory.Length ||
                !string.Equals(visibleInventory[index], hoveredComponentItem, StringComparison.Ordinal))
                HideItemHint();
        }
    }
}
