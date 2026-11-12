using System;
using System.Linq;
using UnityEngine;
using PokeChess.Core.Match;
using PokeChess.Core.Battle;
using PokeChess.Core.Pokemon;
using PokeChess.Client.Match;
namespace PokeChess.Client.UI
{
    public sealed partial class PlacementSandboxView
    {
        private sealed class ShopCardView {
            public UnityEngine.UI.Text Name,Price,Types;
            public UnityEngine.UI.Image Frame, PriceBadge, TraitBadge;
            public RectTransform Portrait;
            public ShopSlot PurchasedSlot;
            public int PurchasePrice, PurchaseQuantity;
            public UnityEngine.UI.Image PurchaseEffect;
            public Sprite[] PurchaseFrames;
        }
        private ShopPurchaseEffects shopPurchaseEffects;
        private const float ShopPurchaseFramesPerSecond = 12f;
        private readonly ShopCardView[] shopViews=new ShopCardView[ShopRules.SlotCount];
        private UnityEngine.UI.Text economyGold,economyLevel,economyStreak,economyDelta;
        private MatchState economyMatch;
        private int economyPreviousGold,shownIncomeRound,shownXPRound;
        private string economyMessage="";
        private float economyMessageUntil;
        public string EconomyFeedbackText=>economyDelta?.text;
        public float ShopXPProgress {get;private set;}
        public int EconomyFeedbackCount {get;private set;}
        public string ShopCardDisplay(int slot) {
            var c=shopViews[slot];return c.Name.text+" / "+c.Price.text+" / "+c.Types.text;
        }
        private static readonly Color32 ShopInk = new Color32(13, 23, 55, 255);
        private readonly Sprite[] shopCostFrames = new Sprite[5];
        private readonly UnityEngine.UI.Image[] shopXpSegments = new UnityEngine.UI.Image[7];
        private readonly EconomyRules shopEconomyRules = new EconomyRules();
        private UnityEngine.UI.Text economyXPText, economyInterest;
        private UnityEngine.UI.Image shopLockIcon, shopToggleIcon;
        private Sprite shopXpEmpty, shopXpFilled, shopEmptyFrame;

        private static Sprite ShopSprite(string name) => Resources.Load<Sprite>("ShopEconomy/Shop" + name);

        private UnityEngine.UI.Image ShopImage(string name, Transform parent, Vector2 size, Vector2 position, Sprite sprite)
        {
            var image = HudImage(name, parent, size, position, Color.white);
            image.sprite = sprite;
            return image;
        }

        private UnityEngine.UI.Text ShopText(string name, Transform parent, int size, Vector2 bounds, Vector2 position, Color color)
        {
            var label = Label(name, parent, "", size, bounds, position);
            label.fontStyle = FontStyle.Bold;
            label.color = color;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 8;
            label.resizeTextMaxSize = size;
            return label;
        }

        private void BuildShopEconomyUI()
        {
            resources.gameObject.SetActive(false);
            shopPurchaseEffects = Resources.Load<ShopPurchaseEffects>("ShopEconomy/ShopPurchaseEffects");
            for (int i = 0; i < shopCostFrames.Length; i++)
                shopCostFrames[i] = ShopSprite($"CardCost{i + 1}");

            var toolbar = ShopImage("ShopToolbar", shopFrame, new Vector2(994, 60), new Vector2(0, 136), ShopSprite("EconomyFrame"));
            toolbar.transform.SetAsFirstSibling();
            var level = ShopImage("LevelPanel", toolbar.transform, new Vector2(302, 43), new Vector2(-331, 0), ShopSprite("LevelPanel"));
            economyLevel = ShopText("Level", level.transform, 17, new Vector2(70, 31), new Vector2(-114, 0), Color.white);
            economyXPText = ShopText("Experience", level.transform, 13, new Vector2(93, 30), new Vector2(101, 0), Color.white);
            shopXpEmpty = ShopSprite("XpSegmentEmpty");
            shopXpFilled = ShopSprite("XpSegmentFilled");
            for (int i = 0; i < shopXpSegments.Length; i++)
                shopXpSegments[i] = ShopImage($"XPSegment_{i}", level.transform, new Vector2(15, 17),
                    new Vector2(-58 + i * 17, 0), shopXpEmpty);

            var gold = ShopImage("GoldPanel", toolbar.transform, new Vector2(273, 43), new Vector2(-39, 0), ShopSprite("GoldPanel"));
            var coin = ShopSprite("Coin");
            ShopImage("Coin", gold.transform, new Vector2(24, 24), new Vector2(-111, 0), coin);
            var goldText = new Color32(255, 224, 71, 255);
            economyGold = ShopText("Gold", gold.transform, 19, new Vector2(66, 28), new Vector2(-67, 0), goldText);
            economyInterest = ShopText("Interest", gold.transform, 12, new Vector2(65, 22), new Vector2(-1, 0), new Color32(142, 233, 255, 255));
            economyStreak = ShopText("Streak", gold.transform, 12, new Vector2(61, 22), new Vector2(62, 0), goldText);
            economyDelta = ShopText("EconomyDelta", gold.transform, 10, new Vector2(57, 19), new Vector2(102, 11), Color.white);

            var action = shopEmptyFrame = ShopSprite("ActionButton");
            StyleShopButton(xpButton, action, new Vector2(114, 42), new Vector2(164, 136));
            StyleShopButton(rerollButton, action, new Vector2(114, 42), new Vector2(283, 136));
            StyleShopButton(lockButton, action, new Vector2(67, 42), new Vector2(378, 136));
            StyleShopButton(shopToggle, action, new Vector2(52, 42), new Vector2(441, 136));
            ShopImage("RerollIcon", rerollButton.transform, new Vector2(14, 14), new Vector2(-42, 0), ShopSprite("RerollIcon"));
            shopLockIcon = ShopImage("LockIcon", lockButton.transform, new Vector2(15, 17), new Vector2(-19, 0), ShopSprite("LockIcon"));
            shopToggleIcon = ShopImage("CollapseIcon", shopToggle.transform, new Vector2(19, 15), Vector2.zero, ShopSprite("CollapseIcon"));
            shopToggle.GetComponentInChildren<UnityEngine.UI.Text>().enabled = false;

            var price = ShopSprite("PriceBadge");
            var badge = ShopSprite("SynergyBadge");
            for (int i = 0; i < shopCards.Length; i++)
            {
                var root = (RectTransform)shopCards[i].transform;
                Place(root, new Vector2(190, 102), new Vector2((i - (shopCards.Length - 1) * .5f) * 198, 56));
                var card = new ShopCardView();
                shopViews[i] = card;
                card.Frame = shopCards[i].GetComponent<UnityEngine.UI.Image>();
                card.Frame.pixelsPerUnitMultiplier = 1.4f;
                card.Portrait = Rect("OfferPortrait", root, new Vector2(67, 60), new Vector2(-53, 8));
                shopCards[i].transition = UnityEngine.UI.Selectable.Transition.None;
                card.Name = ShopText("PokemonName", root, 17, new Vector2(104, 24), new Vector2(34, 22), ShopInk);
                card.PriceBadge = ShopImage("PriceBadge", root, new Vector2(57, 21), new Vector2(50, -5), price);
                ShopImage("Coin", card.PriceBadge.transform, new Vector2(13, 13), new Vector2(-17, 0), coin);
                card.Price = ShopText("Price", card.PriceBadge.transform, 13, new Vector2(35, 21), new Vector2(8, 0), ShopInk);
                card.TraitBadge = ShopImage("SynergyBadge", root, new Vector2(168, 17), new Vector2(0, -32), badge);
                card.Types = ShopText("Types", card.TraitBadge.transform, 11, new Vector2(161, 16), Vector2.zero, ShopInk);
                card.PurchaseEffect = ShopImage("PurchaseEffect", root, root.sizeDelta + new Vector2(7, 12), Vector2.zero, null);
                card.PurchaseEffect.raycastTarget = false;
                card.PurchaseEffect.enabled = false;
            }
        }

        private void StyleShopButton(UnityEngine.UI.Button button, Sprite sprite, Vector2 size, Vector2 position)
        {
            Place((RectTransform)button.transform, size, position);
            var image = button.GetComponent<UnityEngine.UI.Image>();
            image.sprite = sprite;
            image.type = UnityEngine.UI.Image.Type.Simple;
            image.color = Color.white;
            var label = button.GetComponentInChildren<UnityEngine.UI.Text>();
            label.rectTransform.sizeDelta = size - new Vector2(14, 4);
            label.fontSize = 12;
            label.fontStyle = FontStyle.Bold;
            label.color = ShopInk;
            if (button == rerollButton || button == lockButton)
                label.rectTransform.anchoredPosition = new Vector2(8, 0);
        }

        private void RenderShopToolbar(int gold, int level, int xp, int wins, int losses, bool locked, bool freeReroll,
            LevelRules levels, int rerollCost)
        {
            bool max = level >= levels.MaxLevel;
            int required = max ? 0 : levels.XPToNextLevel(level);
            ShopXPProgress = max ? 1 : Mathf.Clamp01(xp / (float)required);
            economyLevel.text = $"Lv. {level}";
            economyXPText.text = max ? "XP MAX" : $"{xp} / {required} XP";
            int filled = Mathf.FloorToInt(ShopXPProgress * shopXpSegments.Length);
            for (int i = 0; i < shopXpSegments.Length; i++)
                shopXpSegments[i].sprite = i < filled ? shopXpFilled : shopXpEmpty;
            economyGold.text = $"{gold} G";
            int interest = Math.Min(gold / shopEconomyRules.GoldPerInterest, shopEconomyRules.MaxInterest);
            economyInterest.text = $"이자 +{interest}";
            economyStreak.text = wins > 0 ? $"{wins}연승" : losses > 0 ? $"{losses}연패" : "–";
            economyDelta.text = Time.unscaledTime < economyMessageUntil ? economyMessage : "";
            economyDelta.color = economyMessage.StartsWith("-") ? new Color32(255, 168, 117, 255) : new Color32(108, 255, 187, 255);
            ButtonText(xpButton, max ? "XP 최대" : $"XP 구매\n{levels.PurchaseGoldCost}G");
            ButtonText(rerollButton, freeReroll ? "새로고침\n무료" : $"새로고침\n{rerollCost}G");
            ButtonText(lockButton, locked ? "잠김" : "열림");
            shopLockIcon.color = locked ? new Color32(156, 92, 0, 255) : ShopInk;
            shopToggleIcon.rectTransform.localRotation = Quaternion.Euler(0, 0, ShopExpanded ? 0 : 180);
            foreach (var button in new[] { xpButton, rerollButton, lockButton })
                button.gameObject.SetActive(ShopExpanded);
        }

        private void RenderShopPortrait(int slot, string species)
        {
            if (string.IsNullOrEmpty(species))
            {
                if (shopSprites.TryGetValue(slot, out var empty)) empty.gameObject.SetActive(false);
                return;
            }
            if (!shopSprites.TryGetValue(slot, out var sprite) || sprite == null)
                shopSprites[slot] = sprite = AddSprite(shopViews[slot].Portrait, species);
            sprite.gameObject.SetActive(true);
            if (sprite.PokemonId != species) sprite.Initialize(species);
            sprite.ShowIdle(Time.unscaledTimeAsDouble);
        }

        private static void RenderShopCardTraits(ShopCardView card, PokemonDefinition definition)
        {
            card.Types.text = TraitList(definition);
            // Keep origin colors inside the badge. The frame is driven by the offer cost.
            switch (definition.TraitIds.FirstOrDefault())
            {
                case SynergyTraits.Nature: card.TraitBadge.color = new Color32(186, 238, 176, 255); break;
                case SynergyTraits.Flame: card.TraitBadge.color = new Color32(255, 194, 187, 255); break;
                case SynergyTraits.Torrent: card.TraitBadge.color = new Color32(180, 225, 255, 255); break;
                case SynergyTraits.Storm: card.TraitBadge.color = new Color32(255, 237, 163, 255); break;
                case SynergyTraits.Mystic: card.TraitBadge.color = new Color32(230, 197, 252, 255); break;
                case SynergyTraits.Earth: card.TraitBadge.color = new Color32(238, 218, 181, 255); break;
                case SynergyTraits.Venom: card.TraitBadge.color = new Color32(221, 204, 248, 255); break;
                case SynergyTraits.Adversity: card.TraitBadge.color = new Color32(255, 212, 171, 255); break;
                default: card.TraitBadge.color = new Color32(219, 229, 244, 255); break;
            }
        }

        private void ApplyShopCardArt(int slot, int cost)
        {
            var card = shopViews[slot];
            bool hasOffer = cost > 0;
            card.Frame.sprite = hasOffer ? shopCostFrames[Mathf.Clamp(cost, 1, 5) - 1] : shopEmptyFrame;
            card.Frame.type = hasOffer ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
            card.Frame.color = !hasOffer || shopCards[slot].interactable ? Color.white : new Color(.81f, .85f, .9f);
            card.PriceBadge.gameObject.SetActive(hasOffer);
            card.TraitBadge.gameObject.SetActive(hasOffer);
        }

        private void ShowEconomyChange(string message) {
            economyMessage=message;economyMessageUntil=Time.unscaledTime+2;EconomyFeedbackCount++;
        }
        internal static string ShopPurchasePrice(int price, int quantity) =>
            quantity > 1 ? $"{quantity}장 · {price}G" : $"{price}G";

        private void SetShopPurchaseEffect(ShopCardView card, bool owned, UnitRank projectedRank)
        {
            card.PurchaseFrames = projectedRank == UnitRank.Three ? shopPurchaseEffects?.UpgradeThree
                : projectedRank == UnitRank.Two ? shopPurchaseEffects?.UpgradeTwo
                : owned ? shopPurchaseEffects?.Owned : null;
            card.PurchaseEffect.enabled = card.PurchaseFrames != null && card.PurchaseFrames.Length > 0;
            if (card.PurchaseEffect.enabled) AnimateShopPurchaseEffect(card, Time.unscaledTime);
        }

        private static void AnimateShopPurchaseEffect(ShopCardView card, float now)
        {
            int frame = Mathf.FloorToInt(now * ShopPurchaseFramesPerSecond) % card.PurchaseFrames.Length;
            card.PurchaseEffect.sprite = card.PurchaseFrames[frame];
        }
        private void RenderShopEconomyUI() {
            if(economyGold==null)return;
            if(!ReferenceEquals(economyMatch,Match)) {
                economyMatch=Match;economyPreviousGold=Player.Gold;
                shownIncomeRound=Player.LastEconomyRound;shownXPRound=Player.LastAutomaticXPRound;
                economyMessage="";economyMessageUntil=0;EconomyFeedbackCount=0;
                foreach(var c in shopViews)c.PurchasedSlot=null;
            }
            var result=RoundLoop.LastResult;
            bool income=result!=null&&result.Income.TryGetValue("p1",out _)&&result.Round>shownIncomeRound;
            bool xp=result!=null&&result.XP.TryGetValue("p1",out _)&&result.Round>shownXPRound;
            if(income||xp) {
                string text="";
                if(income){text="+"+result.Income["p1"].TotalIncome+"G";shownIncomeRound=result.Round;}
                if(xp){text+=(text.Length>0?" · ":"")+"+"+result.XP["p1"].XPGranted+"XP";shownXPRound=result.Round;}
                ShowEconomyChange(text);
            } else if(Player.Gold!=economyPreviousGold) {
                int delta=Player.Gold-economyPreviousGold;ShowEconomyChange((delta>0?"+":"")+delta+"G");
            }
            economyPreviousGold=Player.Gold;
            RenderShopToolbar(Player.Gold, Player.Level, Player.XP, Player.WinStreak, Player.LoseStreak,
                Player.Shop.IsLocked, Player.Synergy.FreeRerollRound == Match.RoundNumber, commands.LevelRules, commands.ShopRules.RerollGoldCost);
            for(int i=0;i<shopCards.Length;i++) {
                var c=shopViews[i];var offer=Player.Shop.Slots[i];
                if(offer.IsEmpty) {
                    c.Name.text=ReferenceEquals(c.PurchasedSlot,offer)?"구매 완료":"빈 슬롯";c.Price.text=c.Types.text="";
                    ApplyShopCardArt(i, 0);continue;
                }
                c.PurchasedSlot=null;var d=catalog.Get(offer.DefinitionId);
                c.Name.text=d.DisplayName;c.Price.text=ShopPurchasePrice(c.PurchasePrice, c.PurchaseQuantity);RenderShopCardTraits(c, d);
                ApplyShopCardArt(i, offer.Cost);
            }
        }
    }
}
