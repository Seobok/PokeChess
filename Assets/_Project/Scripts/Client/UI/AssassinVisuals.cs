using System;
using System.Collections.Generic;
using UnityEngine;
using Image = UnityEngine.UI.Image;

namespace PokeChess.Client.UI
{
    public sealed class AssassinVisuals : ScriptableObject
    {
        public Sprite[] critical, smoke, enter, loop, exit, ready, empowered;
    }

    internal sealed class AssassinBattleVisuals
    {
        internal const string Stealth = "synergy-stealth";
        internal const float StealthAlpha = .42f;
        private const double Fps = 12;

        private sealed class Actor
        {
            public Image Smoke, Mist, Ready;
            public long Start = -1, End = -1;
            public double Exit = -1;
            public bool Hidden;
        }

        private sealed class Impact
        {
            public Image Image;
            public long Tick = -1;
            public Vector2 Position;
        }

        private readonly Dictionary<string, Actor> actors = new();
        private readonly Dictionary<(string Source, string Target, bool Empowered), Impact> impacts = new();
        private AssassinVisuals art;

        public void Hit(string source, string target, long tick, bool empowered, Vector2 position)
        {
            var key = (source, target, empowered);
            if (!impacts.TryGetValue(key, out var impact)) impacts.Add(key, impact = new Impact());
            if (tick <= impact.Tick) return;
            impact.Tick = tick;
            impact.Position = position;
        }

        public void Render(Transform root, SkillVisualUnit[] units, double tick, int rate, bool complete)
        {
            art ??= Resources.Load<AssassinVisuals>("AssassinVisuals");
            if (art == null) return;
            foreach (var unit in units)
            {
                var stealth = Array.Find(unit.Statuses, status => status.definition == Stealth);
                if (!actors.TryGetValue(unit.Id, out var actor))
                {
                    if (stealth == null && !unit.AssassinReady) continue;
                    actors.Add(unit.Id, actor = new Actor());
                }

                bool visible = unit.Alive && !complete;
                bool hidden = visible && stealth != null && stealth.startTick <= tick && tick < stealth.endTick;
                if (hidden)
                {
                    actor.Start = stealth.startTick;
                    actor.End = stealth.endTick;
                }
                else if (actor.Hidden)
                    actor.Exit = Math.Min(tick, actor.End);
                actor.Hidden = hidden;

                double age = actor.Start < 0 ? -1 : (tick - actor.Start) / rate;
                Draw(root, ref actor.Smoke, "Smoke", visible ? Frame(art.smoke, age) : null, unit.Position, 76);

                Sprite mist = null;
                if (hidden)
                {
                    double enterLength = art.enter.Length / Fps;
                    mist = age < enterLength ? Frame(art.enter, age) : Frame(art.loop, age - enterLength, true);
                }
                else if (visible && actor.Exit >= 0)
                    mist = Frame(art.exit, (tick - actor.Exit) / rate);
                Draw(root, ref actor.Mist, "Stealth", mist, unit.Position + Vector2.up * 17, 52);

                var bar = root.Find("Fighter_" + unit.Id)?.Find("HealthBar") as RectTransform;
                var at = unit.Position + new Vector2(-31, bar != null ? bar.anchoredPosition.y : 48);
                Draw(root, ref actor.Ready, "Ready", visible && unit.AssassinReady ? art.ready[0] : null, at, 16);
            }

            foreach (var entry in impacts)
            {
                var impact = entry.Value;
                bool empowered = entry.Key.Empowered;
                Draw(root, ref impact.Image, empowered ? "EmpoweredHit" : "Critical",
                    Frame(empowered ? art.empowered : art.critical, (tick - impact.Tick) / rate),
                    impact.Position + new Vector2(empowered ? 0 : 4, empowered ? 13 : 19), empowered ? 53 : 59);
            }
        }

        private static Sprite Frame(Sprite[] frames, double age, bool loop = false)
            => age >= 0 && (loop || age < frames.Length / Fps) ? frames[(int)(age * Fps) % frames.Length] : null;

        private static void Draw(Transform root, ref Image image, string name, Sprite sprite, Vector2 at, float size)
        {
            if (image == null && sprite != null)
            {
                image = new GameObject("Assassin_" + name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                image.transform.SetParent(root, false);
                image.rectTransform.sizeDelta = Vector2.one * size;
                image.raycastTarget = false;
            }
            if (image == null) return;
            image.sprite = sprite;
            image.enabled = sprite != null;
            if (!image.enabled) return;
            image.rectTransform.anchoredPosition = at;
            image.transform.SetAsLastSibling();
        }

        public void Clear()
        {
            foreach (var actor in actors.Values)
            {
                Release(actor.Smoke);
                Release(actor.Mist);
                Release(actor.Ready);
            }
            foreach (var impact in impacts.Values) Release(impact.Image);
            actors.Clear();
            impacts.Clear();
        }

        private static void Release(Image image)
        {
            if (image == null) return;
            image.enabled = false;
            UnityEngine.Object.Destroy(image.gameObject);
        }
    }
}
