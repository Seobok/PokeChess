using System;
using UnityEngine;

namespace PokeChess.Client.UI
{
    // Bundle Korean glyphs with every player; browser builds cannot use OS fonts.
    internal static class UiFont
    {
        private static Font cached;
        public static Font Default => cached != null ? cached : cached =
            Resources.Load<Font>("Fonts/NotoSansKR/NotoSansKR-Regular")
            ?? throw new InvalidOperationException("Missing bundled UI font: NotoSansKR-Regular");

        public static void EnsureLineHeight(UnityEngine.UI.Text label)
        {
            // Legacy font-sized rectangles can truncate Noto's entire first line.
            // Keep the intended font size and centre; reserve its actual line height.
            if (label.font == null || label.resizeTextForBestFit ||
                label.verticalOverflow != VerticalWrapMode.Truncate) return;
            var rect = label.rectTransform;
            if (rect.rect.height <= 0) return; // A parent layout may size this later.
            float minimum = Mathf.Ceil(label.font.lineHeight * (float)label.fontSize /
                Mathf.Max(1, label.font.fontSize)) + 2;
            if (rect.rect.height < minimum)
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, minimum);
        }
    }
}
