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
        private readonly Image tick, token, reroll, expiry, glyph, selection, reveal, badge, badgeGlyph;
        private MysticVisuals art;
        private object match;
        private string player, selected;
        private int battles, consumed;
        private bool ready;
        private double tickAt, rerollAt, expireAt, selectionAt, revealAt;
        private Sprite[] rerollClip;

        public MysticShopVisuals(Transform hud, Transform settings, RectTransform button, RectTransform card)
        {
            slots = new[]
            {
                Image(hud, "Progress1", new Vector2(-10, -291), new Vector2(21, 21)),
                Image(hud, "Progress2", new Vector2(13, -291), new Vector2(21, 21))
            };
            tick = Image(hud, "ProgressTick", new Vector2(-10, -291), new Vector2(36, 36));
            expiry = Image(hud, "Expire", new Vector2(42, -291), new Vector2(34, 34));
            token = Image(button, "FreeToken", new Vector2(-73, 0), new Vector2(18, 18));
            reroll = Image(button, "Reroll", Vector2.zero, new Vector2(170, 91));
            glyph = Image(settings, "SelectedOrigin", new Vector2(-125, -4), new Vector2(24, 24));
            selection = Image(settings, "SelectionFlash", new Vector2(-125, -4), new Vector2(42, 42));
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
            bool hasFree = phase == MatchPhase.Preparation && freeRound == round;
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
                else if (!hasFree && ready)
                {
                    expireAt = now;
                    rerollClip = null;
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
            Static(token, hasFree ? art.freeToken[0] : null);
            Static(glyph, phase == MatchPhase.Preparation && (tier >= 6 || lastTier >= 6) ? Glyph(prophecy) : null);
            Static(badge, favoredOrigin != null ? art.cardBadge[0] : null);
            Static(badgeGlyph, Glyph(favoredOrigin));
            Draw(tick, total > 0 ? art.progressTick : null, now - tickAt);
            Draw(reroll, phase == MatchPhase.Preparation ? rerollClip : null, now - rerollAt);
            Draw(expiry, art.expire, now - expireAt);
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
            tickAt = rerollAt = expireAt = selectionAt = revealAt = double.NegativeInfinity;
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
