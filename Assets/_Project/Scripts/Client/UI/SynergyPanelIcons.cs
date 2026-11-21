using System;
using UnityEngine;

namespace PokeChess.Client.UI
{
    [DisallowMultipleComponent]
    public sealed class SynergyPanelIcons : MonoBehaviour
    {
        [Serializable]
        private struct Entry
        {
            public string trait;
            public Sprite icon;
        }

        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        [SerializeField] private Sprite[] portraitCostFrames = Array.Empty<Sprite>();

        [SerializeField] private Sprite[] costBallIcons = Array.Empty<Sprite>();

        public Sprite FindCostBallIcon(int cost) =>
            cost > 0 && cost <= costBallIcons.Length ? costBallIcons[cost - 1] : null;

        public Sprite FindPortraitCostFrame(int cost) =>
            cost > 0 && cost <= portraitCostFrames.Length ? portraitCostFrames[cost - 1] : null;

        public Sprite Find(string trait)
        {
            foreach (var entry in entries)
                if (entry.trait == trait) return entry.icon;
            return null;
        }
    }
}
