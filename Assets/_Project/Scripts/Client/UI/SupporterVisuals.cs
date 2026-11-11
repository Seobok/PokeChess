using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Image = UnityEngine.UI.Image;

namespace PokeChess.Client.UI
{
    public sealed class SupporterVisuals : ScriptableObject
    {
        public Sprite[] transfer, arrive, mana;
    }

    internal sealed class SupporterBattleVisuals
    {
        private const double Fps = 12, TravelTime = .25;

        private sealed class Recipient
        {
            public Image Arrival, Hold, End, Mana;
            public long AppliedTick = -1, EndTick = -1, ManaTick = -1;
            public double ArrivalDelay;
            public string EndClip;
        }

        private sealed class Transfer
        {
            public long Tick = -1;
            public Image Image;
        }

        private readonly Dictionary<string, Recipient> recipients = new();
        private readonly Dictionary<(string Source, string Target), Transfer> transfers = new();
        private SupporterVisuals art;

        public void Apply(string source, string target, long tick)
        {
            var recipient = Get(target);
            double delay = source == target ? 0 : TravelTime;
            if (tick > recipient.AppliedTick)
            {
                recipient.AppliedTick = tick;
                recipient.ArrivalDelay = delay;
            }
            else if (tick == recipient.AppliedTick)
                recipient.ArrivalDelay = Math.Min(recipient.ArrivalDelay, delay);
            if (source == target) return;
            var key = (source, target);
            if (!transfers.TryGetValue(key, out var flight)) transfers.Add(key, flight = new Transfer());
            flight.Tick = Math.Max(flight.Tick, tick);
        }

        public void ShieldEnded(string target, string kind, float remaining, long tick)
        {
            if (kind != "Expired" && (kind != "Absorbed" || remaining > 0)) return;
            var recipient = Get(target);
            if (tick <= recipient.EndTick) return;
            recipient.EndTick = tick;
            recipient.EndClip = GuardianBattleVisuals.ShieldClip(kind, remaining);
        }

        public void Mana(string target, long tick)
        {
            var recipient = Get(target);
            recipient.ManaTick = Math.Max(recipient.ManaTick, tick);
        }

        public void Render(Transform root, SkillVisualUnit[] units, double tick, int rate, bool complete)
        {
            art ??= Resources.Load<SupporterVisuals>("SupporterVisuals");
            if (art == null) return;

            foreach (var unit in units)
            {
                bool shielded = unit.Alive && !complete && HasShield(unit, tick);
                if (!recipients.TryGetValue(unit.Id, out var recipient))
                {
                    if (!shielded) continue;
                    recipient = Get(unit.Id);
                }

                bool visible = unit.Alive && !complete;
                Vector2 body = unit.Position + Vector2.up * 19;
                double arrivalAge = (tick - recipient.AppliedTick) / rate - recipient.ArrivalDelay;
                Draw(root, ref recipient.Arrival, "Arrival",
                    shielded && recipient.AppliedTick >= 0 ? Frame(art.arrive, arrivalAge) : null, body, 78);
                DrawBarrier(root, ref recipient.Hold, "Hold", shielded ? "barrier-hold" : null,
                    tick / rate, true, body);
                DrawBarrier(root, ref recipient.End, "End", visible && !shielded ? recipient.EndClip : null,
                    (tick - recipient.EndTick) / rate, false, body);

                var bar = root.Find("Fighter_" + unit.Id)?.Find("EnergyBar") as RectTransform;
                var manaPoint = unit.Position + (bar != null ? bar.anchoredPosition : Vector2.up * 43);
                double manaAge = (tick - recipient.ManaTick) / rate;
                if (recipient.ManaTick == recipient.AppliedTick) manaAge -= recipient.ArrivalDelay;
                Draw(root, ref recipient.Mana, "Mana", visible && recipient.ManaTick >= 0 ? Frame(art.mana, manaAge) : null,
                    manaPoint, 48);
            }

            foreach (var entry in transfers)
            {
                var flight = entry.Value;
                double age = (tick - flight.Tick) / rate;
                var from = Array.Find(units, unit => unit.Id == entry.Key.Source);
                var to = Array.Find(units, unit => unit.Id == entry.Key.Target);
                if (complete || age < 0 || age >= TravelTime || from == null || to == null || !to.Alive || !HasShield(to, tick))
                {
                    if (flight.Image != null) flight.Image.enabled = false;
                    continue;
                }
                float progress = (float)(age / TravelTime);
                var direction = to.Position - from.Position;
                var at = Vector2.Lerp(from.Position, to.Position, progress) +
                    Vector2.up * (19 + Mathf.Sin(progress * Mathf.PI) * 7);
                int frame = Mathf.Min(art.transfer.Length - 1, (int)(progress * art.transfer.Length));
                Draw(root, ref flight.Image, "Transfer", art.transfer[frame], at, 40,
                    Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
            }
        }

        private static bool HasShield(SkillVisualUnit unit, double tick)
            => unit.Shields.Any(shield => shield.group == "supporter" && shield.remaining > 0 && shield.endTick > tick);

        private Recipient Get(string id)
        {
            if (!recipients.TryGetValue(id, out var recipient)) recipients.Add(id, recipient = new Recipient());
            return recipient;
        }

        private static Sprite Frame(Sprite[] frames, double age)
            => age >= 0 && age < frames.Length / Fps ? frames[(int)(age * Fps)] : null;

        private static void Draw(Transform root, ref Image image, string name, Sprite sprite, Vector2 at, float size, float angle = 0)
        {
            if (sprite != null)
            {
                image ??= Create(root, name, size);
                image.sprite = sprite;
                image.rectTransform.anchoredPosition = at;
                image.rectTransform.localRotation = Quaternion.Euler(0, 0, angle);
            }
            if (image != null) image.enabled = sprite != null;
        }

        private static void DrawBarrier(Transform root, ref Image image, string name, string clip, double age, bool loop, Vector2 at)
        {
            if (clip != null)
            {
                image ??= Create(root, name, 72);
                if (GuardianVisuals.Draw(image, clip, age, loop)) image.rectTransform.anchoredPosition = at;
            }
            else if (image != null) image.enabled = false;
        }

        private static Image Create(Transform root, string name, float size)
        {
            var image = new GameObject("Supporter_" + name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(root, false);
            image.rectTransform.sizeDelta = Vector2.one * size;
            image.raycastTarget = false;
            return image;
        }

        public void Clear()
        {
            foreach (var recipient in recipients.Values)
            {
                Release(recipient.Arrival);
                Release(recipient.Hold);
                Release(recipient.End);
                Release(recipient.Mana);
            }
            foreach (var flight in transfers.Values) Release(flight.Image);
            recipients.Clear();
            transfers.Clear();
        }

        private static void Release(Image image)
        {
            if (image == null) return;
            image.enabled = false;
            UnityEngine.Object.Destroy(image.gameObject);
        }
    }
}
