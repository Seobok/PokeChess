using System;
using System.Collections.Generic;
using System.Linq;
using PokeChess.Core.Pokemon;
using UnityEngine;
using UnityEngine.UI;

namespace PokeChess.Client.UI
{
    public sealed class VenomVisuals : ScriptableObject
    {
        public Sprite[] markEnter;
        public Sprite[] markLoop;
        public Sprite[] markRefresh;
        public Sprite[] markExit;
        public Sprite[] firstHit;
        public Sprite[] flight;
        public Sprite[] arrive;
        public Sprite[] erosion;
        public Sprite[] erosionTick;
        public Sprite[] reaction;
        public Sprite[] mutation;
        public Sprite[] mutationHit;
        public Sprite[] vial;
        public Sprite[] collect;
        public Sprite[] research10;
        public Sprite[] research40;
        public Sprite[] research120;

        internal static Image Image(Transform parent, string name)
        {
            var image = new GameObject("Venom_" + name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(parent, false);
            image.raycastTarget = false;
            image.enabled = false;
            return image;
        }

        internal static bool Draw(Image image, Sprite[] frames, double age, bool loop = false)
        {
            int frame = (int)Math.Floor(age * 12);
            image.enabled = frames != null && frames.Length > 0 && frame >= 0 && (loop || frame < frames.Length);
            if (image.enabled) image.sprite = frames[frame % frames.Length];
            return image.enabled;
        }

        internal static void Place(Image image, Vector2 point, float size)
        {
            image.rectTransform.anchoredPosition = point;
            image.rectTransform.sizeDelta = Vector2.one * size;
        }

        internal static void Release(Image image)
        {
            if (image == null) return;
            image.enabled = false;
            if (Application.isPlaying) Destroy(image.gameObject);
            else DestroyImmediate(image.gameObject);
        }
    }

    internal sealed class VenomBattleVisuals
    {
        private sealed class Mark
        {
            public Image Ring, Erosion;
            public long Enter = -1, Refresh = -1, Transfer = -1, End = -1;
        }

        private sealed class Burst
        {
            public Image Image;
            public BoardPosition Cell;
            public BoardPosition? Destination;
            public long Tick = -1;
        }

        private readonly Dictionary<string, Mark> marks = new(StringComparer.Ordinal);
        private readonly Dictionary<(string Clip, string Unit), Burst> bursts = new();
        private VenomVisuals art;

        private Mark State(string unit)
        {
            if (!marks.TryGetValue(unit, out var state)) marks.Add(unit, state = new Mark());
            return state;
        }

        public void Signal(Transform root, string clip, string unit, BoardPosition cell, BoardPosition? destination, long tick)
        {
            if (string.IsNullOrEmpty(unit)) return;
            switch (clip)
            {
                case "CONTAMINATE":
                    State(unit).Enter = Math.Max(State(unit).Enter, tick);
                    return;
                case "REFRESH":
                    State(unit).Refresh = Math.Max(State(unit).Refresh, tick);
                    return;
                case "TRANSFER" when destination.HasValue:
                    State(unit).Transfer = Math.Max(State(unit).Transfer, tick);
                    break;
                case "FIRST_HIT":
                case "EROSION":
                case "REACTION":
                case "MUTATION":
                case "MUTATION_HIT":
                    break;
                default: return;
            }

            var key = (clip, unit);
            if (!bursts.TryGetValue(key, out var burst))
                bursts.Add(key, burst = new Burst { Image = VenomVisuals.Image(root, clip) });
            if (tick <= burst.Tick) return;
            burst.Tick = tick;
            burst.Cell = cell;
            burst.Destination = destination;
        }

        public void Render(Transform root, SkillVisualUnit[] units, Func<int, int, Vector2> point, double tick, int rate, bool complete)
        {
            if (complete) units = Array.Empty<SkillVisualUnit>();
            if (point == null || (bursts.Count == 0 && marks.Count == 0 && !units.Any(unit => unit.ContaminationEnd > tick))) return;
            art ??= Resources.Load<VenomVisuals>("VenomVisuals");
            if (art == null) return;
            float width = Vector2.Distance(point(0, 0), point(1, 0));
            rate = Math.Max(1, rate);
            foreach (var state in marks.Values)
            {
                if (state.Ring != null) state.Ring.enabled = false;
                if (state.Erosion != null) state.Erosion.enabled = false;
            }
            foreach (var unit in units)
            {
                if (!unit.Alive) continue;
                bool active = unit.ContaminationEnd > tick;
                if (!active && !marks.ContainsKey(unit.Id)) continue;
                var state = State(unit.Id);
                if (active)
                {
                    state.End = unit.ContaminationEnd;
                    state.Ring ??= VenomVisuals.Image(root, "Mark");
                    double enter = state.Enter < 0 ? -1 : (tick - state.Enter) / rate;
                    double refresh = state.Refresh < 0 ? -1 : (tick - state.Refresh) / rate;
                    double transfer = state.Transfer < 0 ? -1 : (tick - state.Transfer) / rate;
                    double flight = art.flight.Length / 12d;
                    if (state.Transfer >= state.Refresh && state.Transfer >= state.Enter && transfer >= 0 && transfer < flight + art.arrive.Length / 12d)
                    {
                        if (transfer >= flight) VenomVisuals.Draw(state.Ring, art.arrive, transfer - flight);
                    }
                    else if (state.Refresh >= state.Enter && refresh >= 0 && refresh < art.markRefresh.Length / 12d)
                        VenomVisuals.Draw(state.Ring, art.markRefresh, refresh);
                    else if (enter >= 0 && enter < art.markEnter.Length / 12d)
                        VenomVisuals.Draw(state.Ring, art.markEnter, enter);
                    else VenomVisuals.Draw(state.Ring, art.markLoop, tick / rate, true);

                    if (unit.VenomErosion)
                    {
                        state.Erosion ??= VenomVisuals.Image(root, "ErosionIcon");
                        VenomVisuals.Draw(state.Erosion, art.erosion, 0);
                        VenomVisuals.Place(state.Erosion, ManaPoint(unit.Position, width), width * .36f);
                        state.Erosion.transform.SetAsLastSibling();
                    }
                }
                else if (state.Ring != null && state.End >= 0)
                {
                    if (tick < state.End) state.End = (long)tick;
                    VenomVisuals.Draw(state.Ring, art.markExit, (tick - state.End) / rate);
                }
                if (state.Ring == null) continue;
                VenomVisuals.Place(state.Ring, unit.Position + Vector2.down * width * .24f, width);
                state.Ring.transform.SetAsFirstSibling();
            }
            foreach (var entry in bursts)
            {
                var burst = entry.Value;
                var clip = entry.Key.Clip;
                var frames = clip switch
                {
                    "TRANSFER" => art.flight,
                    "FIRST_HIT" => art.firstHit,
                    "EROSION" => art.erosionTick,
                    "REACTION" => art.reaction,
                    "MUTATION" => art.mutation,
                    "MUTATION_HIT" => art.mutationHit,
                    _ => null
                };
                double age = (tick - burst.Tick) / rate;
                if (!VenomVisuals.Draw(burst.Image, frames, age)) continue;
                var position = point(burst.Cell.Column, burst.Cell.Row);
                float scale = clip == "MUTATION" ? 3f : clip == "REACTION" ? 1.45f : 1;
                if (clip == "TRANSFER")
                {
                    var destination = burst.Destination.Value;
                    var end = point(destination.Column, destination.Row);
                    var delta = end - position;
                    burst.Image.rectTransform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
                    position = Vector2.Lerp(position, end, (float)(age * 12 / frames.Length)) + Vector2.down * width * .16f;
                }
                else if (clip == "EROSION")
                {
                    var unit = Array.Find(units, u => u.Id == entry.Key.Unit && u.Alive);
                    if (unit == null) { burst.Image.enabled = false; continue; }
                    position = ManaPoint(unit.Position, width);
                    scale = .36f;
                }
                else if (clip != "MUTATION") position += Vector2.up * width * .18f;
                VenomVisuals.Place(burst.Image, position, width * scale);
                if (clip == "MUTATION") burst.Image.transform.SetAsFirstSibling();
                else burst.Image.transform.SetAsLastSibling();
            }
        }

        private static Vector2 ManaPoint(Vector2 unit, float width) => unit + new Vector2(width * .45f, 43);

        public void Clear()
        {
            foreach (var state in marks.Values)
            {
                VenomVisuals.Release(state.Ring);
                VenomVisuals.Release(state.Erosion);
            }
            foreach (var burst in bursts.Values) VenomVisuals.Release(burst.Image);
            marks.Clear();
            bursts.Clear();
        }
    }

    internal sealed class VenomHud
    {
        private sealed class Pickup
        {
            public Image Image;
            public Vector2 Start;
            public int Count;
            public double At;
        }

        private readonly List<Pickup> pickups = new();
        private readonly HashSet<(object Battle, string Target)> seen = new();
        private RectTransform canvas;
        private Image vial, badge, flash;
        private Text gain;
        private VenomVisuals art;
        private (object Match, string Player, int Round) scope;
        private bool minimumPlayed;
        private double flashAt = double.NegativeInfinity;

        public VenomHud(RectTransform canvasRect, RectTransform parent, Font font)
        {
            canvas = canvasRect;
            vial = VenomVisuals.Image(parent, "SampleIcon");
            badge = VenomVisuals.Image(parent, "ResearchBadge");
            flash = VenomVisuals.Image(parent, "SampleCollect");
            VenomVisuals.Place(vial, new Vector2(-52, -9), 18);
            VenomVisuals.Place(badge, new Vector2(52, -9), 16);
            VenomVisuals.Place(flash, vial.rectTransform.anchoredPosition, 32);
            var text = new GameObject("Venom_SampleGain", typeof(RectTransform), typeof(Text));
            text.transform.SetParent(parent, false);
            gain = text.GetComponent<Text>();
            gain.font = font;
            gain.fontSize = 13;
            gain.alignment = TextAnchor.MiddleCenter;
            gain.color = new Color(1, .66f, .95f);
            gain.raycastTarget = false;
            gain.rectTransform.anchoredPosition = new Vector2(-33, -14);
            gain.rectTransform.sizeDelta = new Vector2(44, 18);
            gain.enabled = false;
        }

        public void Show(object match, string player, int round, int research, bool minimum)
        {
            art ??= Resources.Load<VenomVisuals>("VenomVisuals");
            if (art == null) return;
            var next = (match, player, round);
            if (!scope.Equals(next))
            {
                foreach (var pickup in pickups) VenomVisuals.Release(pickup.Image);
                pickups.Clear();
                seen.Clear();
                flashAt = double.NegativeInfinity;
                flash.enabled = gain.enabled = false;
                minimumPlayed = false;
                scope = next;
            }
            VenomVisuals.Draw(vial, art.vial, 0);
            var sprite = research switch { 10 => art.research10, 40 => art.research40, 120 => art.research120, _ => null };
            VenomVisuals.Draw(badge, sprite, 0);
            if (minimum && !minimumPlayed)
            {
                minimumPlayed = true;
                Collect(1, Time.unscaledTimeAsDouble);
            }
        }

        public void Sample(object battle, string target, int count, Vector3 world)
        {
            art ??= Resources.Load<VenomVisuals>("VenomVisuals");
            if (art == null || canvas == null || count <= 0 || !seen.Add((battle, target))) return;
            var image = VenomVisuals.Image(canvas, "SampleFlight");
            var start = (Vector2)canvas.InverseTransformPoint(world);
            VenomVisuals.Draw(image, art.vial, 0);
            VenomVisuals.Place(image, start, 20);
            image.transform.SetAsLastSibling();
            pickups.Add(new Pickup { Image = image, Start = start, Count = count, At = Time.unscaledTimeAsDouble });
        }

        private void Collect(int count, double now)
        {
            flashAt = now;
            gain.text = "+" + count;
            gain.enabled = true;
            gain.transform.SetAsLastSibling();
        }

        public void Tick(double now)
        {
            if (art == null || canvas == null) return;
            var target = (Vector2)canvas.InverseTransformPoint(vial.transform.position);
            int arriving = 0;
            for (int i = pickups.Count - 1; i >= 0; i--)
            {
                var pickup = pickups[i];
                float progress = Mathf.Clamp01((float)((now - pickup.At) / .5));
                if (progress >= 1)
                {
                    arriving += pickup.Count;
                    VenomVisuals.Release(pickup.Image);
                    pickups.RemoveAt(i);
                    continue;
                }
                Vector2 position = Vector2.Lerp(pickup.Start, target, progress);
                position.y += Mathf.Sin(progress * Mathf.PI) * 36;
                VenomVisuals.Place(pickup.Image, position, 20);
            }
            if (arriving > 0) Collect(arriving, now);
            VenomVisuals.Draw(flash, art.collect, double.IsNegativeInfinity(flashAt) ? -1 : now - flashAt);
            gain.enabled = now - flashAt < 1;
        }


    }
}
