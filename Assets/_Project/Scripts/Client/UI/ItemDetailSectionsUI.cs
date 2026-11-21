using PokeChess.Core.Match;
using UnityEngine;

namespace PokeChess.Client.UI
{
    public sealed partial class PlacementSandboxView
    {
        private readonly UnityEngine.UI.Text[] itemEffectLabels = new UnityEngine.UI.Text[3];
        private RectTransform itemCraftingSection;
        private UnityEngine.UI.Image itemFirstMaterial, itemSecondMaterial;

        private void BuildItemDetailSections()
        {
            itemRecipeDescription.gameObject.SetActive(false);
            var oldBackground = itemRecipePreview.Find("DescriptionBackground");
            if (oldBackground != null) oldBackground.gameObject.SetActive(false);
            for (int i = 0; i < itemEffectLabels.Length; i++)
            {
                var background = HudImage("EffectSection_" + i, itemRecipePreview,
                    new Vector2(264, 40), Vector2.zero, Color.white);
                StyleItemSurface(background, i == 0 ? itemIcons.recipeSection : itemIcons.descriptionInset);
                var label = RecipeListText("Content", background.transform, 12, new Vector2(240, 30), Vector2.zero);
                label.supportRichText = true;
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                label.verticalOverflow = VerticalWrapMode.Overflow;
                itemEffectLabels[i] = label;
            }
            itemCraftingSection = HudImage("Crafting", itemRecipePreview, new Vector2(264, 80), Vector2.zero, Color.white).rectTransform;
            StyleItemSurface(itemCraftingSection.GetComponent<UnityEngine.UI.Image>(), itemIcons.descriptionInset);
            itemFirstMaterial = HudImage("FirstMaterial", itemCraftingSection, new Vector2(25, 25), new Vector2(-32, -23), Color.white);
            itemSecondMaterial = HudImage("SecondMaterial", itemCraftingSection, new Vector2(25, 25), new Vector2(32, -23), Color.white);
            itemFirstMaterial.preserveAspect = itemSecondMaterial.preserveAspect = true;
            var plus = RecipeListText("Plus", itemCraftingSection, 14, new Vector2(20, 25), new Vector2(0, -23));
            plus.text = "+";
            plus.alignment = TextAnchor.MiddleCenter;
        }

        private void LayoutItemDetailSections(ItemDefinition item)
        {

            string[] contents = { item.BasicEffect, item.AdditionalEffect, item.CooldownAbility };
            float top = 54;
            for (int i = 0; i < itemEffectLabels.Length; i++)
            {
                var label = itemEffectLabels[i];
                label.text = contents[i];
                bool visible = !string.IsNullOrWhiteSpace(contents[i]) && contents[i].Trim() != "없음";
                label.transform.parent.gameObject.SetActive(visible);
                if (!visible) continue;
                float height = Mathf.Ceil(label.preferredHeight) + 16;
                var section = (RectTransform)label.transform.parent;
                section.anchorMin = section.anchorMax = new Vector2(.5f, 1);
                section.pivot = new Vector2(.5f, 1);
                section.sizeDelta = new Vector2(264, height);
                section.anchoredPosition = new Vector2(0, -top);
                label.rectTransform.sizeDelta = new Vector2(240, height - 16);
                top += height + 5;
            }
            bool hasRecipe = item.FirstComponent != null && item.SecondComponent != null;
            itemCraftingSection.gameObject.SetActive(hasRecipe);
            float craftingHeight = hasRecipe ? 43 : 0;
            itemCraftingSection.anchorMin = itemCraftingSection.anchorMax = new Vector2(.5f, 1);
            itemCraftingSection.pivot = new Vector2(.5f, 1);
            itemCraftingSection.sizeDelta = new Vector2(264, craftingHeight);
            itemCraftingSection.anchoredPosition = new Vector2(0, -top);
            itemFirstMaterial.gameObject.SetActive(hasRecipe);
            itemSecondMaterial.gameObject.SetActive(hasRecipe);
            itemCraftingSection.Find("Plus").gameObject.SetActive(hasRecipe);
            if (hasRecipe)
            {
                itemFirstMaterial.sprite = itemIcons.Get(item.FirstComponent);
                itemSecondMaterial.sprite = itemIcons.Get(item.SecondComponent);
                itemFirstMaterial.rectTransform.anchoredPosition = new Vector2(-32, 0);
                itemSecondMaterial.rectTransform.anchoredPosition = new Vector2(32, 0);
                ((RectTransform)itemCraftingSection.Find("Plus")).anchoredPosition = new Vector2(0, 0);
            }
            itemRecipePreview.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, top + craftingHeight + 8);
        }
    }
}

