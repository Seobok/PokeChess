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
    }
}
