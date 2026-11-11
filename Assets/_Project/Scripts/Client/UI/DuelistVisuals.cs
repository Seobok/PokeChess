using System;
using System.Collections.Generic;
using PokeChess.Core.Battle;
using UnityEngine;
using Image = UnityEngine.UI.Image;

namespace PokeChess.Client.UI
{
    public sealed class DuelistVisuals : ScriptableObject
    {
        public Sprite[] stackMeter;
        public Sprite[] stackGain;
        public Sprite[] stackMax;
        public Sprite[] chaseStart;
        public Sprite[] chaseTrail;
        public Sprite[] empoweredReady;
        public Sprite[] empoweredHit;
        public Sprite[] empoweredExpire;
    }

    internal sealed class DuelistBattleVisuals
    {
        private sealed class UnitVisuals
        {
            public Image Meter, StackPulse, Start, Trail, Ready;
            public int GainedStack;
            public long GainTick = -1;
        }

        private sealed class HitVisual
        {
            public Image Image;
            public Vector2 Position;
            public long Tick = -1;
        }

        private const double Fps = 12;
        private const float MeterScale = .64f;
        private static readonly float[] Pips = { -23, -14, -5, 4, 13, 22 };
        private readonly Dictionary<string, UnitVisuals> actors = new();
        private readonly Dictionary<(string, string), HitVisual> hits = new();
        private DuelistVisuals art;

        public void Stack(string id, long tick, int count)
        {
            var actor = Actor(id);
            if (tick < actor.GainTick || tick == actor.GainTick && count <= actor.GainedStack) return;
            actor.GainTick = tick;
            actor.GainedStack = count;
        }

        public void Hit(string source, string target, long tick, Vector2 position)
        {
            var key = (source, target);
            if (!hits.TryGetValue(key, out var hit)) hits.Add(key, hit = new HitVisual());
            if (tick <= hit.Tick) return;
            hit.Tick = tick;
            hit.Position = position;
        }

        public void Render(Transform root, SkillVisualUnit[] units, double tick, int rate, bool complete)
        {
            art ??= Resources.Load<DuelistVisuals>("DuelistVisuals");
            if (art == null) return;
            foreach (var unit in units)
            {
                if (unit.DuelistTier == 0) continue;
                var actor = Actor(unit.Id);
                bool show = unit.Alive && !complete;
                var meter = unit.Position + Vector2.up * 55;
                var ready = unit.Position + new Vector2(32, 48);
                Draw(root, ref actor.Meter, "Meter", show ? art.stackMeter[unit.DuelistStacks] : null, meter,
                    new Vector2(64, 32) * MeterScale);

                double pulseAge = (tick - actor.GainTick) / rate;
                bool max = actor.GainedStack == 6;
                if (actor.GainedStack > 0)
                    Draw(root, ref actor.StackPulse, "StackPulse", show ? Frame(max ? art.stackMax : art.stackGain, pulseAge) : null,
                        max ? meter : meter + Vector2.right * (Pips[actor.GainedStack - 1] * MeterScale),
                        new Vector2(max ? 64 : 32, 32) * MeterScale);

                double chaseAge = (tick - unit.DuelistChaseEnd) / rate + 2;
                double attackRemaining = (unit.DuelistAttackEnd - tick) / rate;
                var target = Array.Find(units, enemy => enemy.Id == unit.Target);
                Vector2 direction = unit.MoveDirection != Vector2.zero ? unit.MoveDirection :
                    target == null ? Vector2.right : target.Position - unit.Position;
                float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
                bool chasing = show && unit.DuelistChaseEnd > tick;
                Draw(root, ref actor.Start, "ChaseStart", chasing ? Frame(art.chaseStart, chaseAge) : null,
                    unit.Position, Vector2.one * 56, angle);
                Draw(root, ref actor.Trail, "ChaseTrail",
                    chasing && unit.MoveDirection != Vector2.zero ? Frame(art.chaseTrail, chaseAge, true) : null,
                    unit.Position, Vector2.one * 56, angle);

                Sprite charge = null;
                if (show && unit.DuelistAttackEnd > 0)
                    charge = attackRemaining > 0 ? Frame(art.empoweredReady, 2 - attackRemaining, true)
                        : Frame(art.empoweredExpire, -attackRemaining);
                Draw(root, ref actor.Ready, "Empowered", charge, ready, Vector2.one * 19);
            }
            foreach (var hit in hits.Values)
                Draw(root, ref hit.Image, "Hit", Frame(art.empoweredHit, (tick - hit.Tick) / rate),
                    hit.Position + Vector2.up * 13, Vector2.one * 55);
        }

        public void Clear()
        {
            foreach (var actor in actors.Values)
            {
                Release(actor.Meter);
                Release(actor.StackPulse);
                Release(actor.Start);
                Release(actor.Trail);
                Release(actor.Ready);
            }
            foreach (var hit in hits.Values) Release(hit.Image);
            actors.Clear();
            hits.Clear();
        }

        private UnitVisuals Actor(string id)
        {
            if (!actors.TryGetValue(id, out var actor)) actors.Add(id, actor = new UnitVisuals());
            return actor;
        }

        private static Sprite Frame(Sprite[] frames, double age, bool loop = false)
        {
            if (age < 0 || !loop && age >= frames.Length / Fps) return null;
            return frames[(int)(age * Fps) % frames.Length];
        }

        private static void Draw(Transform parent, ref Image image, string name, Sprite sprite,
            Vector2 at, Vector2 size, float rotation = 0)
        {
            if (image == null && sprite != null)
            {
                image = new GameObject("Duelist_" + name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                image.transform.SetParent(parent, false);
                image.raycastTarget = false;
            }
            if (image == null) return;
            image.enabled = sprite != null;
            if (!image.enabled) return;
            image.sprite = sprite;
            image.rectTransform.anchoredPosition = at;
            image.rectTransform.sizeDelta = size;
            image.rectTransform.localRotation = Quaternion.Euler(0, 0, rotation);
        }

        private static void Release(Image image)
        {
            if (image == null) return;
            image.enabled = false;
            UnityEngine.Object.Destroy(image.gameObject);
        }
    }
}
