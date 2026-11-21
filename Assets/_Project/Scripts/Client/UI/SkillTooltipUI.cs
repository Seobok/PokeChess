using UnityEngine;

namespace PokeChess.Client.UI
{
    public sealed partial class PlacementSandboxView
    {
        private RectTransform skillTooltip;
        private UnityEngine.UI.Text skillTooltipTitle, skillTooltipDescription;
        private UnityEngine.UI.Image skillTooltipIcon;

        private void BuildSkillTooltip()
        {
            skillTooltip = UiPrefab.Instantiate("SkillTooltip", canvasRect, itemFont);
            skillTooltipTitle = UiPrefab.Get<UnityEngine.UI.Text>(skillTooltip, "Header/Title");
            skillTooltipIcon = UiPrefab.Get<UnityEngine.UI.Image>(skillTooltip, "Header/Icon");
            skillTooltipDescription = UiPrefab.Get<UnityEngine.UI.Text>(skillTooltip, "Description");
        }

        private void ShowSkillTooltip()
        {
            skillTooltipTitle.text = unitDetailSkill.text;
            skillTooltipIcon.sprite = unitSkillImage.sprite;
            skillTooltipIcon.enabled = skillTooltipIcon.sprite != null;
            skillTooltipTitle.rectTransform.offsetMin = new Vector2(skillTooltipIcon.enabled ? 52 : 16, 4);
            string description = unitSkillHint ?? "";
            // Some summon and fallback descriptions already include their skill name.
            int lineBreak = description.IndexOf('\n');
            if (lineBreak >= 0 && description.Substring(0, lineBreak).Trim() == unitDetailSkill.text.Trim())
                description = description.Substring(lineBreak + 1).TrimStart();
            skillTooltipDescription.text = description;
            float height = Mathf.Max(24, Mathf.Ceil(skillTooltipDescription.preferredHeight));
            skillTooltipDescription.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            skillTooltip.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height + 78);

            var bounds = canvasRect.rect;
            var right = canvasRect.InverseTransformPoint(unitInfoRoot.TransformPoint(
                new Vector2(unitInfoRoot.rect.xMax, unitInfoRoot.rect.yMax)));
            var left = canvasRect.InverseTransformPoint(unitInfoRoot.TransformPoint(
                new Vector2(unitInfoRoot.rect.xMin, unitInfoRoot.rect.yMax)));
            var half = skillTooltip.rect.size * .5f;
            float x = right.x + half.x;
            if (x + half.x > bounds.xMax) x = left.x - half.x;
            float y = canvasRect.InverseTransformPoint(unitSkillImage.transform.position).y;
            skillTooltip.anchoredPosition = new Vector2(
                Mathf.Clamp(x, bounds.xMin + half.x, bounds.xMax - half.x),
                Mathf.Clamp(y, bounds.yMin + half.y, bounds.yMax - half.y));
            skillTooltip.gameObject.SetActive(true);
            skillTooltip.SetAsLastSibling();
        }
    }
}
