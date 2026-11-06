using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace PokeChess.Client.UI
{
    public sealed class GuardianVisuals : ScriptableObject
    {
        [Serializable] private sealed class Clip { public string name; public Sprite[] frames; }
        [SerializeField] private Clip[] clips;
        private static GuardianVisuals catalog;

        internal static bool Draw(Image image, string clip, double seconds, bool loop)
        {
            if (catalog == null) catalog = Resources.Load<GuardianVisuals>("GuardianVisuals");
            var frames = catalog == null ? null : Array.Find(catalog.clips, c => c.name == clip)?.frames;
            int frame = (int)Math.Floor(seconds * 12);
            image.enabled = frames != null && frames.Length > 0 && frame >= 0 && (loop || frame < frames.Length);
            if (image.enabled) image.sprite = frames[frame % frames.Length];
            return image.enabled;
        }

        internal static Image Create(Transform parent, string name)
        {
            var image = new GameObject("Guardian_" + name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(parent, false);
            image.raycastTarget = false;
            image.enabled = false;
            return image;
        }
    }

    internal sealed class GuardianBattleVisuals
    {
        private readonly Dictionary<(string unit, string clip), (Image image, long tick)> effects =
            new Dictionary<(string, string), (Image, long)>();

        public void Trigger(Transform root, string unit, string clip, long tick)
        {
            var key = (unit, clip);
            if (effects.TryGetValue(key, out var previous))
            {
                if (previous.tick < tick) effects[key] = (previous.image, tick);
                return;
            }
            effects.Add(key, (GuardianVisuals.Create(root, clip), tick));
        }

        public void Render(Transform root, SkillVisualUnit[] units, double tick, int rate, float scale)
        {
            foreach (var unit in units)
            {
                if (unit.Alive && unit.Guarded) Trigger(root, unit.Id, "target-mark", 0);
                if (unit.Alive && unit.Shields.Any(s => s.group == "guardian"))
                    Trigger(root, unit.Id, "barrier-hold", 0);
            }
            foreach (var entry in effects)
            {
                var unit = Array.Find(units, u => u.Id == entry.Key.unit);
                var image = entry.Value.image;
                string clip = entry.Key.clip;
                bool mark = clip == "target-mark", hold = clip == "barrier-hold";
                if (unit == null || !unit.Alive || (hold && !unit.Shields.Any(s => s.group == "guardian")))
                {
                    image.enabled = false;
                    continue;
                }
                if (!GuardianVisuals.Draw(image, clip, (tick - entry.Value.tick) / rate, mark || hold)) continue;
                image.rectTransform.sizeDelta = Vector2.one * (mark ? 36 : 64) * scale;
                image.rectTransform.anchoredPosition = unit.Position + Vector2.up * (mark ? 52 : 20) * scale;
                image.transform.SetAsLastSibling();
            }
        }

        public void Clear()
        {
            foreach (var effect in effects.Values)
            {
                effect.image.enabled = false;
                UnityEngine.Object.Destroy(effect.image.gameObject);
            }
            effects.Clear();
        }

        public static string ShieldClip(string kind, float remaining)
        {
            switch (kind)
            {
                case "Applied": return "barrier-create";
                case "Absorbed": return remaining <= 0 ? "barrier-break" : null;
                case "Expired": return "barrier-expire";
                default: return null;
            }
        }
    }
}
