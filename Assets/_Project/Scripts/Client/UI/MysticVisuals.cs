using System;
using PokeChess.Core.Battle;
using PokeChess.Core.Match;
using UnityEngine;
using UnityEngine.UI;

namespace PokeChess.Client.UI
{
    public sealed class MysticVisuals : ScriptableObject
    {
        public Sprite[] progressSlot;
        public Sprite[] progressTick;
        public Sprite[] freeToken;
        public Sprite[] ready;
        public Sprite[] spent;
        public Sprite[] expire;
        public Sprite[] originGlyphs;
        public Sprite[] select;
        public Sprite[] cardReveal;
        public Sprite[] cardBadge;
    }

    internal sealed class MysticShopVisuals
    {
        private readonly Image[] slots;
        private readonly Image tick, token, reroll, glyph, selection, reveal, badge, badgeGlyph;
        private MysticVisuals art;
        private object match;
        private string player, selected;
        private int battles, consumed;
        private bool ready;
        private double tickAt, rerollAt, selectionAt, revealAt;
        private Sprite[] rerollClip;

        public MysticShopVisuals(Transform hud, Transform originVisuals, RectTransform button, RectTransform card)
        {
            slots = new[]
            {
                Image(hud, "Progress1", new Vector2(-13, -8), new Vector2(21, 21)),
                Image(hud, "Progress2", new Vector2(13, -8), new Vector2(21, 21))
            };
            tick = Image(hud, "ProgressTick", new Vector2(-13, -8), new Vector2(36, 36));
            token = Image(button, "FreeToken", new Vector2(-20, 0), new Vector2(18, 18));
            reroll = Image(button, "Reroll", Vector2.zero, new Vector2(112, 64));
            glyph = Image(originVisuals, "SelectedOrigin", Vector2.zero, new Vector2(24, 24));
            selection = Image(originVisuals, "SelectionFlash", Vector2.zero, new Vector2(42, 42));
            reveal = Image(card, "FavoredReveal", Vector2.zero, new Vector2(230, 113));
            badge = Image(card, "FavoredBadge", new Vector2(72, 31), new Vector2(27, 27));
            badgeGlyph = Image(badge.transform, "FavoredOrigin", Vector2.zero, new Vector2(18, 18));
            ResetTimes();
        }

        public void Show(object matchKey, string playerId, int round, MatchPhase phase, int tier, int lastTier,
            int completedBattles, int freeRound, int consumedRound, string prophecy, string favoredOrigin)
        {
            art ??= Resources.Load<MysticVisuals>("MysticVisuals");
            if (art == null) return;
            bool hasFree = SynergyProgress.IsFreeRerollAvailable(freeRound, round);
            bool canReroll = phase == MatchPhase.Preparation || phase == MatchPhase.Combat;
            bool newScope = !Equals(match, matchKey) || player != playerId;
            double now = Time.unscaledTimeAsDouble;
            if (newScope)
            {
                match = matchKey;
                player = playerId;
                ResetTimes();
            }
            else
            {
                if (completedBattles > battles)
                {
                    tickAt = now;
                    int index = lastTier == 2 && completedBattles % 2 == 0 ? 1 : 0;
                    tick.rectTransform.anchoredPosition = slots[index].rectTransform.anchoredPosition;
                }
                if (consumedRound == round && consumedRound != consumed)
                {
                    rerollClip = art.spent;
                    rerollAt = now;
                    if (favoredOrigin != null) revealAt = now;
                }
                else if (hasFree && !ready)
                {
                    rerollClip = art.ready;
                    rerollAt = now;
                }
                if (prophecy != null && selected != prophecy && (tier >= 6 || lastTier >= 6))
                    selectionAt = now;
            }
            battles = completedBattles;
            ready = hasFree;
            consumed = consumedRound;
            selected = prophecy;

            int shownTier = phase == MatchPhase.Result ? lastTier : tier;
            if (shownTier == 0 && hasFree) shownTier = lastTier;
            int total = shownTier == 2 ? 2 : shownTier >= 4 ? 1 : 0;
            bool awardReady = hasFree || phase == MatchPhase.Result && freeRound == round + 1;
            int filled = awardReady ? total : shownTier == 2 ? completedBattles % 2 : 0;
            for (int i = 0; i < slots.Length; i++)
                Static(slots[i], i < total ? art.progressSlot[i < filled ? 1 : 0] : null);
            Static(token, hasFree && canReroll ? art.freeToken[0] : null);
            Static(glyph, phase == MatchPhase.Preparation && (tier >= 6 || lastTier >= 6) ? Glyph(prophecy) : null);
            Static(badge, favoredOrigin != null ? art.cardBadge[0] : null);
            Static(badgeGlyph, Glyph(favoredOrigin));
            Draw(tick, total > 0 ? art.progressTick : null, now - tickAt);
            Draw(reroll, canReroll ? rerollClip : null, now - rerollAt);
            Draw(selection, glyph.enabled ? art.select : null, now - selectionAt);
            Draw(reveal, badge.enabled ? art.cardReveal : null, now - revealAt);
        }

        private Sprite Glyph(string origin)
        {
            for (int i = 0; i < SynergyTraits.Origins.Count; i++)
                if (SynergyTraits.Origins[i] == origin) return art.originGlyphs[i];
            return null;
        }

        private void ResetTimes()
        {
            tickAt = rerollAt = selectionAt = revealAt = double.NegativeInfinity;
            rerollClip = null;
        }

        private static Image Image(Transform root, string name, Vector2 position, Vector2 size)
        {
            var image = new GameObject("Mystic_" + name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(root, false);
            image.rectTransform.anchoredPosition = position;
            image.rectTransform.sizeDelta = size;
            image.raycastTarget = false;
            image.enabled = false;
            return image;
        }

        private static void Static(Image image, Sprite sprite)
        {
            image.sprite = sprite;
            image.enabled = sprite != null;
        }

        private static void Draw(Image image, Sprite[] frames, double age)
        {
            if (frames == null || age < 0 || age >= frames.Length / 12d)
            {
                image.enabled = false;
                return;
            }
            Static(image, frames[(int)(age * 12)]);
            image.transform.SetAsLastSibling();
        }
    }
}
