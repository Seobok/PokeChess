using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Image = UnityEngine.UI.Image;
using Text = UnityEngine.UI.Text;

namespace PokeChess.Client.UI
{
    public sealed class TrackerVisuals : ScriptableObject
    {
        public Sprite[] glyph;
        public Sprite[] stacks;
        public Sprite[] enter;
        public Sprite[] gain;
        public Sprite[] completion;
        public Sprite[] flight;
        public Sprite[] arrive;
        public Sprite[] exit;
    }

    internal sealed class TrackerBattleVisuals
    {
        private sealed class Mark
        {
            public string Target, TransferTarget;
            public int Stacks;
            public Vector2 Position, Departure, ExitPosition, HitPosition;
            public double PulseAt = double.NegativeInfinity, ExitAt = double.NegativeInfinity;
            public long TransferTick = -1, HitTick = -1;
            public Image Owner, TargetOwner, StacksImage, Pulse, Exit, Flight, Hit;
        }

        private const double Fps = 12, TravelSeconds = .32;
        private static readonly Color[] OwnerColors =
        {
            new(.62f, .91f, 1), new(1, .75f, .45f), new(.75f, .99f, .61f),
            new(1, .65f, .82f), new(.83f, .75f, 1), new(1, .91f, .53f)
        };
        private readonly Dictionary<string, Mark> marks = new();
        private TrackerVisuals art;
        private Font font;

        public void Transfer(string owner, string target, long tick, Vector2 departure)
        {
            var mark = Get(owner);
            if (tick <= mark.TransferTick) return;
            mark.TransferTick = tick;
            mark.TransferTarget = target;
            mark.Departure = departure;
        }

        public void Completion(string owner, long tick, Vector2 position)
        {
            var mark = Get(owner);
            if (tick < mark.HitTick) return;
            mark.HitTick = tick;
            mark.HitPosition = position;
        }

        public void Render(Transform root, SkillVisualUnit[] units, double tick, int rate, bool complete)
        {
            art ??= Resources.Load<TrackerVisuals>("TrackerVisuals");
            if (art == null) return;

            var owners = units.Where(unit => unit.TrackerTier > 0).OrderBy(unit => unit.Id, StringComparer.Ordinal);
            var rows = new Dictionary<string, int>();
            int number = 0;
            double now = tick / rate;
            foreach (var owner in owners)
            {
                var mark = Get(owner.Id);
                var color = OwnerColors[number % OwnerColors.Length];
                string label = (++number).ToString();
                bool show = owner.Alive && !complete;
                var target = show && owner.TrackerExpiry > tick
                    ? Array.Find(units, unit => unit.Alive && unit.Id == owner.TrackerTarget) : null;
                int stacks = target == null ? 0 : owner.TrackerStacks;
                string targetId = stacks > 0 ? target.Id : null;
                double transferAge = (tick - mark.TransferTick) / rate;
                bool transferred = targetId != null && targetId == mark.TransferTarget &&
                    transferAge >= 0 && transferAge < TravelSeconds + art.arrive.Length / Fps;
                bool targetChanged = mark.Target != targetId;
                if (show && targetChanged && mark.Target != null && !transferred)
                {
                    mark.ExitAt = now;
                    mark.ExitPosition = mark.Position;
                }
                if (targetChanged || stacks > mark.Stacks) mark.PulseAt = now;
                mark.Target = targetId;
                mark.Stacks = stacks;

                if (targetId != null)
                {
                    rows.TryGetValue(targetId, out int row);
                    rows[targetId] = row + 1;
                    mark.Position = target.Position + new Vector2(0, 72 + row * 11);
                }
                bool flying = transferred && transferAge < TravelSeconds;
                var rowPosition = mark.Position;
                DrawGlyph(root, ref mark.Owner, "Owner", show, owner.Position + new Vector2(-28, 55), color, label);
                DrawGlyph(root, ref mark.TargetOwner, "TargetOwner", targetId != null && !flying,
                    rowPosition + Vector2.left * 22, color, label);
                Draw(root, ref mark.StacksImage, "Stacks", targetId != null && !flying ? art.stacks[stacks - 1] : null,
                    rowPosition, new Vector2(38, 19));

                Sprite pulse = null;
                Vector2 pulsePosition = rowPosition;
                if (targetId != null && !flying)
                {
                    pulse = transferred ? Frame(art.arrive, transferAge - TravelSeconds) :
                        Frame(stacks == 1 ? art.enter : art.gain, now - mark.PulseAt);
                    if (!transferred && stacks > 1) pulsePosition.x += (stacks - 2) * 7;
                }
                Draw(root, ref mark.Pulse, "MarkPulse", pulse, pulsePosition, Vector2.one * 26);
                Draw(root, ref mark.Exit, "Exit", show ? Frame(art.exit, now - mark.ExitAt) : null,
                    mark.ExitPosition, new Vector2(38, 19));
                var direction = rowPosition - mark.Departure;
                var flightPosition = Vector2.Lerp(mark.Departure, rowPosition, (float)(transferAge / TravelSeconds));
                Draw(root, ref mark.Flight, "Transfer", flying ? Frame(art.flight, transferAge, true) : null,
                    flightPosition, Vector2.one * 34, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
                Draw(root, ref mark.Hit, "Completion", mark.HitTick >= 0 ? Frame(art.completion, (tick - mark.HitTick) / rate) : null,
                    mark.HitPosition + Vector2.up * 14, Vector2.one * 55);
            }
        }

        private void DrawGlyph(Transform root, ref Image image, string name, bool visible, Vector2 at, Color color, string label)
        {
            Draw(root, ref image, name, visible ? art.glyph[0] : null, at, Vector2.one * 12);
            if (image == null) return;
            var text = image.GetComponentInChildren<Text>(true);
            if (text == null)
            {
                font ??= Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                text = new GameObject("TrackerNumber", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
                text.transform.SetParent(image.transform, false);
                text.rectTransform.anchoredPosition = Vector2.left * 9;
                text.rectTransform.sizeDelta = new Vector2(14, 13);
                text.font = font;
                text.fontSize = 9;
                text.alignment = TextAnchor.MiddleCenter;
                text.raycastTarget = false;
            }
            text.enabled = visible;
            text.text = label;
            text.color = image.color = color;
        }

        public void Clear()
        {
            foreach (var mark in marks.Values)
            {
                Release(mark.Owner);
                Release(mark.TargetOwner);
                Release(mark.StacksImage);
                Release(mark.Pulse);
                Release(mark.Exit);
                Release(mark.Flight);
                Release(mark.Hit);
            }
            marks.Clear();
        }

        private Mark Get(string id)
        {
            if (!marks.TryGetValue(id, out var mark)) marks.Add(id, mark = new Mark());
            return mark;
        }

        private static Sprite Frame(Sprite[] frames, double age, bool loop = false)
        {
            if (age < 0 || !loop && age >= frames.Length / Fps) return null;
            return frames[(int)(age * Fps) % frames.Length];
        }

        private static void Draw(Transform root, ref Image image, string name, Sprite sprite, Vector2 at, Vector2 size, float angle = 0)
        {
            if (image == null && sprite != null)
            {
                image = new GameObject("Tracker_" + name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                image.transform.SetParent(root, false);
                image.raycastTarget = false;
            }
            if (image == null) return;
            image.enabled = sprite != null;
            if (!image.enabled) return;
            image.sprite = sprite;
            image.rectTransform.anchoredPosition = at;
            image.rectTransform.sizeDelta = size;
            image.rectTransform.localRotation = Quaternion.Euler(0, 0, angle);
        }

        private static void Release(Image image)
        {
            if (image == null) return;
            image.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(image.gameObject);
        }
    }
}
