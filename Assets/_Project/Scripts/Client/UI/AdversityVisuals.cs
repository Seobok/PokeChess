using System;
using System.Collections.Generic;
using PokeChess.Core.Match;
using UnityEngine;
using Image = UnityEngine.UI.Image;
using Text = UnityEngine.UI.Text;
using Button = UnityEngine.UI.Button;

namespace PokeChess.Client.UI
{
    public sealed class AdversityVisuals : ScriptableObject
    {
        public Sprite[] resolveIcon;
        public Sprite[] lossSpark;
        public Sprite[] resolveCollect;
        public Sprite[] cashout;
        public Sprite[] gold;
        public Sprite[] equipment;
        public Sprite[] emblem;
        public Sprite[] choiceReady;
        public Sprite[] choiceConfirm;
        public Sprite[] rewardCollect;
    }

    internal sealed class AdversityHud
    {
        private sealed class Playback
        {
            public Image Image;
            public Sprite[] Frames;
            public Vector2 From, To;
            public double Start, Duration;
        }

        private const double Fps = 12;
        private const double TravelTime = .34;
        private readonly AdversityVisuals art;
        private readonly RectTransform canvas, goldTarget, inventoryTarget, resultTitle;
        private readonly Button settings;
        private readonly Button[] buttons;
        private readonly Image[] counterIcons = new Image[3], choiceIcons = new Image[3];
        private readonly Text[] counters = new Text[3];
        private readonly Sprite berry;
        private readonly Sprite[][] rewards;
        private readonly List<Playback> playing = new();
        private object match;
        private string player;
        private int settlementRound, choiceSequence, resolve;
        private int pendingResolve;
        private double resolveArrival;

        public AdversityHud(RectTransform canvas, RectTransform hud, Font font, RectTransform resultTitle,
            RectTransform goldTarget, RectTransform inventoryTarget, Button settings, Button[] buttons, Sprite berry)
        {
            this.canvas = canvas;
            this.resultTitle = resultTitle;
            this.goldTarget = goldTarget;
            this.inventoryTarget = inventoryTarget;
            this.settings = settings;
            this.buttons = buttons;
            this.berry = berry;
            art = Resources.Load<AdversityVisuals>("AdversityVisuals");
            if (art == null) return;
            rewards = new[] { art.gold, art.equipment, art.emblem };
            Sprite[][] icons = { art.resolveIcon, art.equipment, art.emblem };
            for (int i = 0; i < 3; i++)
            {
                var at = new Vector2(-53 + 43 * i, -9);
                counterIcons[i] = MakeImage(hud, "CounterIcon" + i, at, 16);
                counterIcons[i].sprite = icons[i][0];
                var node = new GameObject("Adversity_Count" + i, typeof(RectTransform), typeof(Text));
                node.transform.SetParent(hud, false);
                var label = counters[i] = node.GetComponent<Text>();
                label.font = font;
                label.fontSize = 11;
                label.alignment = TextAnchor.MiddleLeft;
                label.raycastTarget = false;
                label.rectTransform.anchoredPosition = at + new Vector2(25, 0);
                label.rectTransform.sizeDelta = new Vector2(32, 18);
                choiceIcons[i] = MakeImage(buttons[i].transform, "ChoiceIcon" + i, new Vector2(-47, 0), 16);
                choiceIcons[i].sprite = rewards[i][0];
            }
        }

        public bool HasArt => art != null;

        public void Show(object matchKey, string playerId, int round, MatchPhase phase, bool visible, bool canChoose,
            int tier, int currentResolve, int equipment, int emblem, int choices,
            AdversitySettlement settlement, AdversityChoice choice)
        {
            if (art == null) return;
            double now = Time.unscaledTimeAsDouble;
            bool newScope = !Equals(match, matchKey) || player != playerId;
            if (newScope || !visible)
            {
                foreach (var clip in playing) UnityEngine.Object.Destroy(clip.Image.gameObject);
                playing.Clear();
                resolveArrival = 0;
            }
            if (!newScope && visible)
            {
                if (settlement.round > settlementRound && settlement.round == round && phase == MatchPhase.Result)
                    PlaySettlement(settlement, now);
                if (choice.sequence > choiceSequence && phase == MatchPhase.Preparation)
                    PlayChoice(choice.slot, now);
            }
            match = matchKey;
            player = playerId;
            if (newScope || !visible || settlement.round < round || (settlement.round == round && phase == MatchPhase.Result))
                settlementRound = settlement.round;
            if (newScope || !visible || phase == MatchPhase.Preparation)
                choiceSequence = choice.sequence;
            resolve = currentResolve;
            bool showCounters = visible && tier > 0;
            counters[1].text = equipment.ToString();
            counters[2].text = emblem.ToString();
            for (int i = 0; i < 3; i++)
            {
                counterIcons[i].enabled = counters[i].enabled = showCounters;
                buttons[i].gameObject.SetActive(visible && choices > 0 && phase == MatchPhase.Preparation);
                buttons[i].interactable = canChoose;
            }
            settings.GetComponentInChildren<Text>().text = visible && choices > 0 ? $"시너지 설정 · 역경 {choices}" : "시너지 설정";
            Tick(now);
        }

        private void PlaySettlement(AdversitySettlement settlement, double now)
        {
            Vector2 counter = Point(counterIcons[0].rectTransform);
            if (settlement.resolveDelta > 0)
            {
                Vector2 origin = Point(resultTitle) + new Vector2(-155, 0);
                Flash("Loss", art.lossSpark, origin, 64, now);
                const double release = .25;
                Fly("Resolve", art.resolveIcon[0], origin, counter, now + release);
                resolveArrival = now + release + TravelTime;
                pendingResolve = settlement.resolveDelta;
                Flash("ResolveCollect", art.resolveCollect, counter, 46, resolveArrival);
                return;
            }

            if (settlement.resolveDelta == 0) return;
            resolveArrival = 0;
            Flash("Cashout", art.cashout, counter, 92, now);
            double rewardAt = now + .5;
            if (settlement.choice)
                Flash("ChoiceReady", art.choiceReady, Point((RectTransform)settings.transform), 128, rewardAt);
            else
            {
                if (settlement.gold > 0)
                    Collect(art.gold[0], counter, Point(goldTarget) + new Vector2(-50, 0), rewardAt);
                if (settlement.berry && berry != null)
                    Collect(berry, counter, Point(inventoryTarget), rewardAt + .12);
            }
        }

        private void PlayChoice(int slot, double now)
        {
            Vector2 start = Point((RectTransform)buttons[slot].transform);
            Vector2 end = slot == 0 ? Point(goldTarget) + new Vector2(-50, 0) : Point(counterIcons[slot].rectTransform);
            Flash("ChoiceConfirm", art.choiceConfirm, start, 128, now);
            Collect(rewards[slot][0], start, end, now + .26);
        }

        private void Collect(Sprite icon, Vector2 from, Vector2 to, double at)
        {
            Fly("Reward", icon, from, to, at);
            Flash("RewardCollect", art.rewardCollect, to, 54, at + TravelTime);
        }

        private void Fly(string name, Sprite sprite, Vector2 from, Vector2 to, double at)
        {
            var image = MakeImage(canvas, name, from, 26);
            image.sprite = sprite;
            image.enabled = false;
            playing.Add(new Playback { Image = image, From = from, To = to, Start = at, Duration = TravelTime });
        }

        private void Flash(string name, Sprite[] frames, Vector2 at, float size, double start)
        {
            var image = MakeImage(canvas, name, at, size);
            image.enabled = false;
            playing.Add(new Playback { Image = image, Frames = frames, Start = start, Duration = frames.Length / Fps });
        }

        public void Tick(double now)
        {
            if (art == null) return;
            counters[0].text = Math.Max(0, resolve - (now < resolveArrival ? pendingResolve : 0)).ToString();
            for (int i = playing.Count - 1; i >= 0; i--)
            {
                var clip = playing[i];
                double age = now - clip.Start;
                if (age < 0) continue;
                if (age >= clip.Duration)
                {
                    UnityEngine.Object.Destroy(clip.Image.gameObject);
                    playing.RemoveAt(i);
                    continue;
                }
                clip.Image.enabled = true;
                clip.Image.transform.SetAsLastSibling();
                if (clip.Frames != null)
                    clip.Image.sprite = clip.Frames[(int)(age * Fps)];
                else
                {
                    float progress = (float)(age / clip.Duration);
                    clip.Image.rectTransform.anchoredPosition = Vector2.Lerp(clip.From, clip.To, Mathf.SmoothStep(0, 1, progress))
                        + Vector2.up * (Mathf.Sin(progress * Mathf.PI) * 24);
                }
            }
        }

        private Vector2 Point(RectTransform target) => canvas.InverseTransformPoint(target.position);

        private static Image MakeImage(Transform parent, string name, Vector2 at, float size)
        {
            var image = new GameObject("Adversity_" + name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(parent, false);
            image.rectTransform.anchoredPosition = at;
            image.rectTransform.sizeDelta = new Vector2(size, size);
            image.raycastTarget = false;
            image.preserveAspect = true;
            return image;
        }
    }
}
