using System;
using PokeChess.Core.Battle;
using UnityEngine;
using Image = UnityEngine.UI.Image;

namespace PokeChess.Client.UI
{
    public sealed class BruiserVisuals : ScriptableObject
    {
        public Sprite[] teamHealth;
        public Sprite[] memberHealth;
        public Sprite[] survivalEnter;
        public Sprite[] survivalLoop;
        public Sprite[] survivalExit;
        public Sprite[] survivalIcon;

        private const double Fps = 12;
        private static BruiserVisuals catalog;

        internal static void Render(Transform fighter, RectTransform healthBar, bool alive, BruiserHealthBonus healthBonus,
            long survivalEnd, double tick, int rate)
        {
            if (healthBonus == BruiserHealthBonus.None && survivalEnd == 0) return;
            if (catalog == null) catalog = Resources.Load<BruiserVisuals>("BruiserVisuals");
            if (catalog == null) return;

            var barAnchor = healthBar != null ? healthBar : fighter;
            var barOffset = healthBar != null ? Vector2.zero : Vector2.up * 33;
            double elapsed = alive ? tick / rate : double.NegativeInfinity;
            if (healthBonus != BruiserHealthBonus.None)
                Draw(barAnchor, "HealthUp", Frame(catalog.teamHealth, elapsed), barOffset, 64, behind: healthBar != null);
            if (healthBonus == BruiserHealthBonus.Member)
                Draw(barAnchor, "MemberHealthUp", Frame(catalog.memberHealth, elapsed - catalog.teamHealth.Length / Fps),
                    barOffset, 64, behind: healthBar != null);

            if (survivalEnd <= 0) return;
            double sinceEnd = (tick - survivalEnd) / rate;
            bool active = alive && sinceEnd < 0;
            Sprite guard = null;
            if (active)
            {
                double sinceStart = sinceEnd + 4;
                double enterDuration = catalog.survivalEnter.Length / Fps;
                guard = sinceStart < enterDuration ? Frame(catalog.survivalEnter, sinceStart)
                    : Frame(catalog.survivalLoop, sinceStart - enterDuration, loop: true);
            }
            else if (alive)
                guard = Frame(catalog.survivalExit, sinceEnd);
            Draw(fighter, "Survival", guard, Vector2.up * 18, 80);
            Draw(barAnchor, "SurvivalIcon", active ? catalog.survivalIcon[0] : null, barOffset + Vector2.right * 32, 16);
        }

        private static Sprite Frame(Sprite[] frames, double elapsed, bool loop = false)
        {
            if (elapsed < 0 || (!loop && elapsed >= frames.Length / Fps)) return null;
            return frames[(int)Math.Floor(elapsed * Fps) % frames.Length];
        }

        private static void Draw(Transform parent, string name, Sprite sprite, Vector2 position, float size, bool behind = false)
        {
            string objectName = "Bruiser_" + name;
            var image = parent.Find(objectName)?.GetComponent<Image>();
            if (image == null)
            {
                if (sprite == null) return;
                image = new GameObject(objectName, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                image.transform.SetParent(parent, false);
                image.rectTransform.anchoredPosition = position;
                image.rectTransform.sizeDelta = Vector2.one * size;
                image.raycastTarget = false;
                if (behind) image.transform.SetAsFirstSibling();
            }
            image.sprite = sprite;
            image.enabled = sprite != null;
        }
    }
}
