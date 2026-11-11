using System;
using System.Collections.Generic;
using UnityEngine;
using Image = UnityEngine.UI.Image;

namespace PokeChess.Client.UI
{
    public sealed class InvokerVisuals : ScriptableObject
    {
        public Sprite[] teamPulse;
        public Sprite[] receive;
        public Sprite[] bonus;
        public Sprite[] refund;
    }

    internal sealed class InvokerBattleVisuals
    {
        private const double Fps = 12;

        private sealed class Recipient
        {
            public long PeriodicTick = -1, RefundTick = -1;
            public bool Bonus;
            public Image GroundPulse, Receive, BonusFlash, Refund;
        }

        private readonly Dictionary<string, Recipient> recipients = new();
        private InvokerVisuals art;

        public void Signal(string detail, string target, long tick)
        {
            if (!recipients.TryGetValue(target, out var recipient))
                recipients.Add(target, recipient = new Recipient());

            if (detail == "REFUND")
            {
                recipient.RefundTick = Math.Max(tick, recipient.RefundTick);
                return;
            }
            if (tick <= recipient.PeriodicTick) return;
            recipient.PeriodicTick = tick;
            recipient.Bonus = detail == "PERIODIC_BONUS";
        }

        public void Render(Transform root, SkillVisualUnit[] units, double tick, int rate)
        {
            art ??= Resources.Load<InvokerVisuals>("InvokerVisuals");
            if (art == null) return;

            foreach (var entry in recipients)
            {
                var unit = Array.Find(units, candidate => candidate.Id == entry.Key);
                var recipient = entry.Value;
                bool visible = unit != null && unit.Alive;
                var bar = root.Find("Fighter_" + entry.Key)?.Find("EnergyBar") as RectTransform;
                var at = (unit?.Position ?? Vector2.zero) + (bar != null ? bar.anchoredPosition : Vector2.up * 43);
                double periodicAge = recipient.PeriodicTick < 0 ? -1 : (tick - recipient.PeriodicTick) / rate;
                double refundAge = recipient.RefundTick < 0 ? -1 : (tick - recipient.RefundTick) / rate;
                Draw(root, ref recipient.GroundPulse, "GroundPulse", visible ? Frame(art.teamPulse, periodicAge) : null,
                    unit?.Position ?? Vector2.zero, 54);
                Draw(root, ref recipient.Receive, "Receive", visible ? Frame(art.receive, periodicAge) : null, at, 48);
                Draw(root, ref recipient.BonusFlash, "Bonus", visible && recipient.Bonus ? Frame(art.bonus, periodicAge - 4 / Fps) : null, at, 32);
                Draw(root, ref recipient.Refund, "Refund", visible ? Frame(art.refund, refundAge) : null, at, 58);
            }
        }

        private static Sprite Frame(Sprite[] frames, double age)
            => age >= 0 && age < frames.Length / Fps ? frames[(int)(age * Fps)] : null;

        private static void Draw(Transform root, ref Image image, string name, Sprite sprite, Vector2 at, float size)
        {
            if (image == null && sprite != null)
            {
                image = new GameObject("Invoker_" + name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                image.transform.SetParent(root, false);
                image.rectTransform.sizeDelta = Vector2.one * size;
                image.raycastTarget = false;
                if (name == "GroundPulse")
                {
                    for (int i = 0; i < root.childCount - 1; i++)
                    {
                        if (!root.GetChild(i).name.StartsWith("Fighter", StringComparison.Ordinal)) continue;
                        image.transform.SetSiblingIndex(i);
                        break;
                    }
                }
            }
            if (image == null) return;
            image.sprite = sprite;
            image.enabled = sprite != null;
            image.rectTransform.anchoredPosition = at;
        }

        public void Clear()
        {
            foreach (var recipient in recipients.Values)
            {
                Release(recipient.GroundPulse);
                Release(recipient.Receive);
                Release(recipient.BonusFlash);
                Release(recipient.Refund);
            }
            recipients.Clear();
        }

        private static void Release(Image image)
        {
            if (image == null) return;
            image.enabled = false;
            UnityEngine.Object.Destroy(image.gameObject);
        }
    }
}
