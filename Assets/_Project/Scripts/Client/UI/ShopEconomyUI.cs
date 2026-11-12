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
            public UnityEngine.UI.Text Name,Price,Types,Owned,Action;
            public UnityEngine.UI.Image Frame, PriceBadge, TraitBadge;
            public RectTransform Portrait;
            public bool Purchased;public ShopSlot PurchasedSlot;
            public string Flash;public float FlashUntil;
        }
        private readonly ShopCardView[] shopViews=new ShopCardView[ShopRules.SlotCount];
        private UnityEngine.UI.Text economyGold,economyLevel,economyStreak,economyDelta;
        private MatchState economyMatch;
        private int economyPreviousGold,economyPreviousRound,shownIncomeRound,shownXPRound;
        private string economyMessage="";
        private float economyMessageUntil;
        public string EconomyFeedbackText=>economyDelta?.text;
        public float ShopXPProgress {get;private set;}
        public int EconomyFeedbackCount {get;private set;}
        public string ShopCardDisplay(int slot) {
            var c=shopViews[slot];return c.Name.text+" / "+c.Price.text+" / "+c.Types.text+" / "+c.Owned.text+" / "+c.Action.text;
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
            for (int i = 0; i < shopCostFrames.Length; i++)
                shopCostFrames[i] = ShopSprite($"CardCost{i + 1}");

            var toolbar = ShopImage("ShopToolbar", shopFrame, new Vector2(994, 60), new Vector2(-130, 136), ShopSprite("EconomyFrame"));
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
            StyleShopButton(xpButton, action, new Vector2(114, 42), new Vector2(34, 136));
            StyleShopButton(rerollButton, action, new Vector2(114, 42), new Vector2(153, 136));
            StyleShopButton(lockButton, action, new Vector2(67, 42), new Vector2(248, 136));
            StyleShopButton(shopToggle, action, new Vector2(52, 42), new Vector2(311, 136));
            ShopImage("RerollIcon", rerollButton.transform, new Vector2(14, 14), new Vector2(-42, 0), ShopSprite("RerollIcon"));
            shopLockIcon = ShopImage("LockIcon", lockButton.transform, new Vector2(15, 17), new Vector2(-19, 0), ShopSprite("LockIcon"));
            shopToggleIcon = ShopImage("CollapseIcon", shopToggle.transform, new Vector2(19, 15), Vector2.zero, ShopSprite("CollapseIcon"));
            shopToggle.GetComponentInChildren<UnityEngine.UI.Text>().enabled = false;

            var price = ShopSprite("PriceBadge");
            var badge = ShopSprite("SynergyBadge");
            for (int i = 0; i < shopCards.Length; i++)
            {
                var root = (RectTransform)shopCards[i].transform;
                Place(root, new Vector2(190, 102), new Vector2(-526 + i * 198, 56));
                cardText[i].gameObject.SetActive(false);
                var card = new ShopCardView();
                shopViews[i] = card;
                card.Frame = shopCards[i].GetComponent<UnityEngine.UI.Image>();
                card.Frame.type = UnityEngine.UI.Image.Type.Simple;
                card.Portrait = Rect("OfferPortrait", root, new Vector2(67, 60), new Vector2(-53, 8));
                shopCards[i].transition = UnityEngine.UI.Selectable.Transition.None;
                card.Name = ShopText("PokemonName", root, 17, new Vector2(104, 24), new Vector2(34, 28), ShopInk);
                card.Owned = ShopText("RankOwned", root, 11, new Vector2(103, 16), new Vector2(34, 9), ShopInk);
                card.PriceBadge = ShopImage("PriceBadge", root, new Vector2(57, 21), new Vector2(50, -9), price);
                ShopImage("Coin", card.PriceBadge.transform, new Vector2(13, 13), new Vector2(-17, 0), coin);
                card.Price = ShopText("Price", card.PriceBadge.transform, 13, new Vector2(35, 21), new Vector2(8, 0), ShopInk);
                card.TraitBadge = ShopImage("SynergyBadge", root, new Vector2(168, 17), new Vector2(0, -26), badge);
                card.Types = ShopText("Types", card.TraitBadge.transform, 11, new Vector2(161, 16), Vector2.zero, ShopInk);
                card.Action = ShopText("BuyState", root, 9, new Vector2(164, 13), new Vector2(0, -40), ShopInk);
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
            card.Frame.color = !hasOffer || shopCards[slot].interactable ? Color.white : new Color(.81f, .85f, .9f);
            card.PriceBadge.gameObject.SetActive(hasOffer);
            card.TraitBadge.gameObject.SetActive(hasOffer);
        }

        private void ShowEconomyChange(string message) {
            economyMessage=message;economyMessageUntil=Time.unscaledTime+2;EconomyFeedbackCount++;
        }
        private static string ShortBuyFailure(MatchCommandFailure failure) {
            switch(failure) {
                case MatchCommandFailure.NotEnoughGold:return "NOT ENOUGH GOLD";
                case MatchCommandFailure.FullBench:return "BENCH FULL";
                case MatchCommandFailure.Changed:return "STATE CHANGED / RETRY";
                case MatchCommandFailure.PoolEmpty:return "POOL EMPTY";
                case MatchCommandFailure.Eliminated:return "PLAYER ELIMINATED";
                case MatchCommandFailure.WrongPhase:return "UNAVAILABLE NOW";
                default:return "UNAVAILABLE";
            }
        }
        private void RecordShopPurchase(int slot,MatchCommandResult result) {
            var c=shopViews[slot];if(result.Accepted)c.PurchasedSlot=Player.Shop.Slots[slot];c.Purchased=result.Accepted;c.FlashUntil=Time.unscaledTime+1.5f;
            c.Flash=result.Accepted?"PURCHASED":ShortBuyFailure(result.Failure);
        }
        private void RenderShopEconomyUI() {
            if(economyGold==null)return;
            if(!ReferenceEquals(economyMatch,Match)) {
                economyMatch=Match;economyPreviousGold=Player.Gold;economyPreviousRound=Match.RoundNumber;
                shownIncomeRound=Player.LastEconomyRound;shownXPRound=Player.LastAutomaticXPRound;
                economyMessage="";economyMessageUntil=0;EconomyFeedbackCount=0;
                foreach(var c in shopViews){c.Purchased=false;c.PurchasedSlot=null;c.Flash=null;c.FlashUntil=0;}
            }
            if(economyPreviousRound!=Match.RoundNumber) {
                if(!Player.Shop.IsLocked)foreach(var c in shopViews)c.Purchased=false;
                economyPreviousRound=Match.RoundNumber;
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
                    c.Purchased=ReferenceEquals(c.PurchasedSlot,offer);
                    c.Name.text=c.Purchased?"구매 완료":"빈 슬롯";c.Price.text=c.Types.text=c.Owned.text="";
                    c.Action.text=c.Purchased?"":"다음 새로고침 대기";c.Action.color=ShopInk;
                    ApplyShopCardArt(i, 0);continue;
                }
                c.Purchased=false;c.PurchasedSlot=null;var d=catalog.Get(offer.DefinitionId);var availability=commands.PreviewBuy(i,displayedShopRevision);
                c.Name.text=d.DisplayName;c.Price.text=offer.Cost+"G";RenderShopCardTraits(c, d);
                c.Owned.text=string.Join(" · ",new[]{UnitRank.One,UnitRank.Two,UnitRank.Three}.Select(r=>"R"+(int)r+" "+Player.Units.Count(u=>u.DefinitionId==d.Id&&u.Rank==r)));
                c.Action.text=!availability.Accepted?ShortBuyFailure(availability.Failure):availability.Preview.MergeNextPreparation?
                    (availability.Preview.RankUpCount>0?"R"+(int)availability.Preview.FinalRank+" + NEXT PREP MERGE":"BUY / NEXT PREP MERGE"):
                    availability.Preview.RankUpCount>0?"BUY / R"+(int)availability.Preview.FinalRank:"BUY";
                bool flash=Time.unscaledTime<c.FlashUntil;
                if(flash&&c.Flash!=null)c.Action.text=c.Flash;
                c.Action.color = !availability.Accepted || flash ? new Color32(139, 55, 23, 255) : ShopInk;
                ApplyShopCardArt(i, offer.Cost);
            }
        }
    }
}
