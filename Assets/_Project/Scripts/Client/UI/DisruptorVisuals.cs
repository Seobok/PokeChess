using System;
using System.Collections.Generic;
using PokeChess.Core.Match;
using UnityEngine;
using Image = UnityEngine.UI.Image;

namespace PokeChess.Client.UI
{
    public sealed class DisruptorVisuals : ScriptableObject
    {
        public Sprite[] weakenEnter;
        public Sprite[] weakenIcon;
        public Sprite[] exposure;
        public Sprite[] manaEnter;
        public Sprite[] manaIcon;
        public Sprite[] refresh;
        public Sprite[] exit;
    }

    internal sealed class DisruptorBattleVisuals
    {
        private const double Fps = 12;
        private const string Weaken = "synergy-disruptor-at";
        private const string Exposure = "synergy-disruptor-expose";
        private const string ManaLock = "synergy-disruptor-mana";

        private sealed class StatusVisual
        {
            public long Start = -1, End = -1;
            public double Exit = -1;
            public bool Active, Refreshed;
            public Image Icon, Pulse;

            public void Update(CombatStatusView status, double tick)
            {
                if (status != null && tick >= status.startTick && tick < status.endTick)
                {
                    if (Start != status.startTick || End != status.endTick)
                    {
                        Refreshed = Active && status.startTick < End;
                        Start = status.startTick;
                        End = status.endTick;
                    }
                    Active = true;
                }
                else if (Active)
                {
                    Exit = Math.Min(tick, End);
                    Active = false;
                }
            }
        }

        private sealed class Actor
        {
            public RectTransform Root;
            public Image Exposure;
            public readonly StatusVisual Weaken = new(), Mana = new();
        }

        private readonly Dictionary<string, Actor> actors = new();
        private DisruptorVisuals art;

        public void Render(Transform board, SkillVisualUnit[] units, double tick, int rate, bool complete)
        {
            art ??= Resources.Load<DisruptorVisuals>("DisruptorVisuals");
            if (art == null) return;

            foreach (var unit in units)
            {
                bool visible = unit.Alive && !complete;
                actors.TryGetValue(unit.Id, out var actor);
                if (!visible)
                {
                    if (actor != null) actor.Root.gameObject.SetActive(false);
                    continue;
                }

                var weaken = Array.Find(unit.Statuses, status => status.definition == Weaken);
                var exposure = Array.Find(unit.Statuses, status => status.definition == Exposure);
                var mana = Array.Find(unit.Statuses, status => status.definition == ManaLock);
                if (actor == null)
                {
                    if (weaken == null && exposure == null && mana == null) continue;
                    actor = Build(board);
                    actors.Add(unit.Id, actor);
                }

                actor.Root.gameObject.SetActive(true);
                actor.Root.anchoredPosition = unit.Position;
                actor.Root.SetAsLastSibling();
                actor.Weaken.Update(weaken, tick);
                actor.Mana.Update(mana, tick);

                var fighter = board.Find("Fighter_" + unit.Id);
                var health = fighter?.Find("HealthBar") as RectTransform;
                var energy = fighter?.Find("EnergyBar") as RectTransform;
                Place(actor.Weaken, new Vector2(31, health != null ? health.anchoredPosition.y : 48));
                Place(actor.Mana, new Vector2(-31, energy != null ? energy.anchoredPosition.y : 43));

                Draw(actor.Exposure, exposure != null && tick >= exposure.startTick && tick < exposure.endTick
                    ? Frame(art.exposure, tick / rate, true) : null);
                RenderStatus(actor.Weaken, art.weakenIcon[0], art.weakenEnter, art.refresh, tick, rate, true);
                RenderStatus(actor.Mana, art.manaIcon[0], art.manaEnter, null, tick, rate, !unit.VenomErosion);
            }
        }

        private void RenderStatus(StatusVisual visual, Sprite icon, Sprite[] enter, Sprite[] refresh, double tick, int rate, bool visible)
        {
            Draw(visual.Icon, visible && visual.Active ? icon : null);
            var clip = visual.Active ? (visual.Refreshed ? refresh : enter) : art.exit;
            double start = visual.Active ? visual.Start : visual.Exit;
            Draw(visual.Pulse, visible && start >= 0 ? Frame(clip, (tick - start) / rate) : null);
        }

        private static Actor Build(Transform board)
        {
            var root = new GameObject("Disruptor_Effects", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(board, false);
            var actor = new Actor { Root = root, Exposure = ImageAt(root, "Exposure", 80) };
            actor.Exposure.rectTransform.anchoredPosition = Vector2.up * 15;
            actor.Weaken.Icon = ImageAt(root, "Weaken", 15);
            actor.Weaken.Pulse = ImageAt(root, "WeakenPulse", 36);
            actor.Mana.Icon = ImageAt(root, "Mana", 15);
            actor.Mana.Pulse = ImageAt(root, "ManaPulse", 30);
            return actor;
        }

        private static void Place(StatusVisual visual, Vector2 at)
        {
            visual.Icon.rectTransform.anchoredPosition = at;
            visual.Pulse.rectTransform.anchoredPosition = at;
        }

        private static Image ImageAt(Transform root, string name, float size)
        {
            var image = new GameObject("Disruptor_" + name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(root, false);
            image.rectTransform.sizeDelta = Vector2.one * size;
            image.raycastTarget = false;
            return image;
        }

        private static Sprite Frame(Sprite[] frames, double age, bool loop = false)
            => frames != null && age >= 0 && (loop || age < frames.Length / Fps)
                ? frames[(int)(age * Fps) % frames.Length] : null;

        private static void Draw(Image image, Sprite sprite)
        {
            image.sprite = sprite;
            image.enabled = sprite != null;
        }

        public void Clear()
        {
            foreach (var actor in actors.Values)
            {
                actor.Root.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(actor.Root.gameObject);
            }
            actors.Clear();
        }
    }
}
