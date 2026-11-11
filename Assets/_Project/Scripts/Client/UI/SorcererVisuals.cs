using System.Collections.Generic;
using UnityEngine;
using Image = UnityEngine.UI.Image;

namespace PokeChess.Client.UI
{
    public sealed class SorcererVisuals : ScriptableObject
    {
        public Sprite[] basePower;
        public Sprite[] castComplete;
        public Sprite[] stackSlot;
        public Sprite[] stackGain;
        public Sprite[] stackMax;
    }

    internal sealed class SorcererBattleVisuals
    {
        private const double Fps = 12;
        private const float SlotSpacing = 9;

        private sealed class Actor
        {
            public RectTransform Root;
            public Image Base, Complete;
            public Image[] Slots, Pulses;
            public long GainTick = -1;
            public int GainedStack;
        }

        private readonly Dictionary<string, Actor> actors = new();
        private SorcererVisuals art;

        public void Stack(string id, long tick, int count)
        {
            var actor = Get(id);
            if (tick < actor.GainTick || tick == actor.GainTick && count <= actor.GainedStack) return;
            actor.GainTick = tick;
            actor.GainedStack = count;
        }

        public void Render(Transform board, SkillVisualUnit[] units, double tick, int rate, bool complete)
        {
            art ??= Resources.Load<SorcererVisuals>("SorcererVisuals");
            if (art == null) return;
            foreach (var unit in units)
            {
                if (unit.SorcererTier == 0) continue;
                var actor = Get(unit.Id);
                bool visible = unit.Alive && !complete;
                if (actor.Root != null) actor.Root.gameObject.SetActive(visible);
                if (!visible) continue;

                int capacity = unit.SorcererTier == 6 ? 6 : unit.SorcererTier == 4 ? 5 : 4;
                if (actor.Root == null) Build(actor, board, capacity);
                actor.Root.anchoredPosition = unit.Position;
                actor.Root.SetAsLastSibling();

                Draw(actor.Base, Frame(art.basePower, tick / rate));
                double age = actor.GainTick < 0 ? -1 : (tick - actor.GainTick) / rate;
                Draw(actor.Complete, Frame(art.castComplete, age));
                bool max = unit.SorcererStacks == capacity;
                bool reachedMax = actor.GainedStack == capacity;
                for (int i = 0; i < capacity; i++)
                {
                    Draw(actor.Slots[i], art.stackSlot[max ? 2 : i < unit.SorcererStacks ? 1 : 0]);
                    var pulse = reachedMax ? Frame(art.stackMax, age - i / Fps)
                        : actor.GainedStack == i + 1 ? Frame(art.stackGain, age) : null;
                    Draw(actor.Pulses[i], pulse);
                }
            }
        }

        private static void Build(Actor actor, Transform board, int capacity)
        {
            actor.Root = new GameObject("Sorcerer_Effects", typeof(RectTransform)).GetComponent<RectTransform>();
            actor.Root.SetParent(board, false);
            actor.Base = ImageAt(actor.Root, "Base", Vector2.zero, 80);
            actor.Complete = ImageAt(actor.Root, "CastComplete", Vector2.up * 26, 48);
            actor.Slots = new Image[capacity];
            actor.Pulses = new Image[capacity];
            for (int i = 0; i < capacity; i++)
            {
                var position = new Vector2((i - (capacity - 1) * .5f) * SlotSpacing, 37);
                actor.Slots[i] = ImageAt(actor.Root, "Slot", position, 12);
                actor.Pulses[i] = ImageAt(actor.Root, "Pulse", position, 12);
            }
        }

        private Actor Get(string id)
        {
            if (!actors.TryGetValue(id, out var actor)) actors.Add(id, actor = new Actor());
            return actor;
        }

        private static Sprite Frame(Sprite[] frames, double age)
            => age >= 0 && age < frames.Length / Fps ? frames[(int)(age * Fps)] : null;

        private static void Draw(Image image, Sprite sprite)
        {
            image.sprite = sprite;
            image.enabled = sprite != null;
        }

        private static Image ImageAt(Transform root, string name, Vector2 at, float size)
        {
            var image = new GameObject("Sorcerer_" + name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(root, false);
            image.rectTransform.anchoredPosition = at;
            image.rectTransform.sizeDelta = Vector2.one * size;
            image.raycastTarget = false;
            return image;
        }

        public void Clear()
        {
            foreach (var actor in actors.Values)
            {
                if (actor.Root == null) continue;
                actor.Root.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(actor.Root.gameObject);
            }
            actors.Clear();
        }
    }
}
