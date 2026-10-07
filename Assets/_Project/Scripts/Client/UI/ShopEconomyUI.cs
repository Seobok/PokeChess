using System;
using System.Linq;
using UnityEngine;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;
using PokeChess.Client.Match;
namespace PokeChess.Client.UI
{
    public sealed partial class PlacementSandboxView
    {
        private sealed class ShopCardView {
            public UnityEngine.UI.Text Name,Price,Types,Owned,Action;
            public UnityEngine.UI.Image Stripe;
            public bool Purchased;public ShopSlot PurchasedSlot;
            public string Flash;public float FlashUntil;
        }
        private readonly ShopCardView[] shopViews=new ShopCardView[ShopRules.SlotCount];
        private UnityEngine.UI.Text economyGold,economyLevel,economyLock,economyStreak,economyDelta;
        private UnityEngine.UI.Image economyXP;
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
        private void BuildShopEconomyUI() {
            resources.gameObject.SetActive(false); // Compatibility diagnostics; the visible resource fields are separate.
            status.rectTransform.anchoredPosition=new Vector2(100,88);status.rectTransform.sizeDelta=new Vector2(980,20);status.fontSize=12;
            economyGold=Label("Gold",shopFrame,"",22,new Vector2(170,30),new Vector2(-505,65));
            economyDelta=Label("EconomyDelta",shopFrame,"",11,new Vector2(190,18),new Vector2(-505,88));
            economyLevel=Label("LevelXP",shopFrame,"",14,new Vector2(235,26),new Vector2(-300,66));
            var track=HudImage("XPTrack",shopFrame,new Vector2(200,4),new Vector2(-300,45),new Color(.1f,.18f,.25f));
            economyXP=HudImage("XPFill",track.transform,new Vector2(200,4),Vector2.zero,new Color(.25f,.7f,1));
            economyXP.rectTransform.anchorMin=economyXP.rectTransform.anchorMax=new Vector2(0,.5f);
            economyXP.rectTransform.pivot=new Vector2(0,.5f);economyXP.rectTransform.anchoredPosition=Vector2.zero;
            economyLock=Label("ShopLockState",shopFrame,"",12,new Vector2(200,28),new Vector2(-60,66));
            economyStreak=Label("ShopStreak",shopFrame,"",12,new Vector2(180,26),new Vector2(165,66));
            foreach(var b in shopCards)b.GetComponent<RectTransform>().sizeDelta=new Vector2(170,116);
            for(int i=0;i<shopCards.Length;i++) {
                var root=shopCards[i].transform;cardText[i].gameObject.SetActive(false);
                var c=new ShopCardView();shopViews[i]=c;
                c.Stripe=HudImage("CostStripe",root,new Vector2(4,108),new Vector2(-81,0),Color.white);
                c.Name=Label("PokemonName",root,"",13,new Vector2(110,22),new Vector2(25,38));
                c.Price=Label("Price",root,"",15,new Vector2(90,22),new Vector2(33,15));c.Price.color=new Color(1,.82f,.35f);
                c.Types=Label("Types",root,"",10,new Vector2(160,16),new Vector2(0,-9));
                c.Owned=Label("RankOwned",root,"",11,new Vector2(160,16),new Vector2(0,-25));
                c.Action=Label("BuyState",root,"",11,new Vector2(160,28),new Vector2(0,-44));
            }
            foreach(var button in new[]{rerollButton,lockButton,xpButton})button.GetComponentInChildren<UnityEngine.UI.Text>().fontSize=12;
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
            economyGold.text="GOLD "+Player.Gold;
            bool max=Player.Level==commands.LevelRules.MaxLevel;
            economyLevel.text="Lv "+Player.Level+" · "+(max?"XP MAX":"XP "+Player.XP+" / "+commands.LevelRules.XPToNextLevel(Player.Level));
            ShopXPProgress=max?1:Mathf.Clamp01(Player.XP/(float)commands.LevelRules.XPToNextLevel(Player.Level));
            economyXP.rectTransform.sizeDelta=new Vector2(200*ShopXPProgress,4);
            economyLock.text=Player.Shop.IsLocked?"[LOCKED]\nKeep next auto refresh":"[UNLOCKED]\nAuto refresh next round";
            economyLock.color=Player.Shop.IsLocked?new Color(1,.82f,.35f):new Color(.75f,.82f,.9f);
            economyStreak.text=Player.WinStreak>0?"WIN STREAK "+Player.WinStreak:Player.LoseStreak>0?"LOSS STREAK "+Player.LoseStreak:"STREAK —";
            economyDelta.text=Time.unscaledTime<economyMessageUntil?economyMessage:"";
            economyDelta.color=economyMessage.StartsWith("-")?new Color(1,.65f,.4f):new Color(.35f,1,.65f);
            for(int i=0;i<shopCards.Length;i++) {
                var c=shopViews[i];var offer=Player.Shop.Slots[i];
                if(offer.IsEmpty) {
                    c.Purchased=ReferenceEquals(c.PurchasedSlot,offer);
                    c.Name.text=c.Purchased?"PURCHASED":"NO OFFER";c.Price.text=c.Types.text=c.Owned.text="";
                    c.Action.text=c.Purchased?"Wait for refresh":"No stock at this cost";c.Action.color=new Color(.65f,.72f,.8f);
                    shopCards[i].targetGraphic.color=new Color(.09f,.14f,.19f);c.Stripe.color=new Color(.3f,.35f,.4f);continue;
                }
                c.Purchased=false;c.PurchasedSlot=null;var d=catalog.Get(offer.DefinitionId);var availability=commands.PreviewBuy(i,displayedShopRevision);
                c.Name.text=d.DisplayName;c.Price.text=offer.Cost+"G · C"+offer.Cost;c.Types.text=string.Join(" / ",d.TypeIds);
                c.Owned.text=string.Join(" · ",new[]{UnitRank.One,UnitRank.Two,UnitRank.Three}.Select(r=>"R"+(int)r+" "+Player.Units.Count(u=>u.DefinitionId==d.Id&&u.EvolutionStage==0&&u.Rank==r)));
                c.Action.text=!availability.Accepted?ShortBuyFailure(availability.Failure):availability.Preview.MergeNextPreparation?
                    (availability.Preview.RankUpCount>0?"R"+(int)availability.Preview.FinalRank+" + NEXT PREP MERGE":"BUY / NEXT PREP MERGE"):
                    availability.Preview.RankUpCount>0?"BUY / R"+(int)availability.Preview.FinalRank:"BUY";
                bool flash=Time.unscaledTime<c.FlashUntil;
                if(flash&&c.Flash!=null)c.Action.text=c.Flash;
                c.Action.color=!availability.Accepted||flash?new Color(1,.65f,.4f):new Color(.5f,1,.7f);
                shopCards[i].targetGraphic.color=shopCards[i].interactable?new Color(.13f,.23f,.31f):new Color(.1f,.15f,.2f);
                var colors=new[]{Color.gray,new Color(.7f,.8f,.85f),new Color(.3f,.8f,.55f),new Color(.25f,.65f,1),new Color(.8f,.45f,1),new Color(1,.72f,.25f)};
                c.Stripe.color=colors[Mathf.Clamp(offer.Cost,1,5)];
            }
        }
    }
}
