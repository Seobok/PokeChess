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

        public Sprite Find(string trait)
        {
            foreach (var entry in entries)
                if (entry.trait == trait) return entry.icon;
            return null;
        }
    }
}
