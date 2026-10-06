using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;
using PokeChess.Client.Match;

namespace PokeChess.Client.UI
{
    public sealed partial class PlacementSandboxView
    {
        private LocalMatchCommandGateway commands;
        private readonly UnityEngine.UI.Button[] shopCards=new UnityEngine.UI.Button[ShopRules.SlotCount];
        private readonly UnityEngine.UI.Text[] cardText=new UnityEngine.UI.Text[ShopRules.SlotCount];
        private UnityEngine.UI.Button rerollButton,lockButton,xpButton,sellButton,shopToggle;
        private UnityEngine.UI.Text resources,playersLabel,inventoryLabel,detailLabel,debugLabel;
        private RectTransform shopFrame;
        private const float ShopHeight=200, ShopHandleHeight=64;
        public RectTransform ShopRect => shopFrame;
        private void AnimateShop()
        {
            float target=ShopExpanded ? 20 : ShopHandleHeight-ShopHeight;
            shopFrame.anchoredPosition=new Vector2(0,Mathf.MoveTowards(shopFrame.anchoredPosition.y,target,900*Time.unscaledDeltaTime));
        }
        private bool HitShop(Vector2 point) => RectTransformUtility.RectangleContainsScreenPoint(canvasRect,point,null) && RectTransformUtility.RectangleContainsScreenPoint(shopFrame,point,null);
        private void PreviewShopSale()
        {
            bool allowed=commands.CanTrade && Player.PlacementRevision==dragRevision;
            shopFrame.GetComponent<UnityEngine.UI.Image>().color=allowed ? new Color(.08f,.36f,.29f) : new Color(.48f,.12f,.17f);
            Feedback(allowed ? "Release to sell / +"+commands.SalePrice(dragged)+"G; equipped items return to inventory." : "Sale unavailable: preparation only, or placement changed.");
        }
        private string selectedUnitId;
        private long displayedShopRevision,displayedPlacementRevision;
        private bool shopCollapsed,busy,displayedLocked;
        private MatchPhase? shopPhase;
        private readonly Dictionary<string,float> highlights=new Dictionary<string,float>();
        public string SelectedUnitId => selectedUnitId;
        public bool ShopExpanded => (Match.Phase==MatchPhase.Preparation || Match.Phase==MatchPhase.Combat) && !shopCollapsed;
        public UnityEngine.UI.Button ShopButton(int slot) => shopCards[slot];
        public UnityEngine.UI.Button SellButton => sellButton;
        public UnityEngine.UI.Button XPButton => xpButton;
        public UnityEngine.UI.Button RerollButton => rerollButton;
        public UnityEngine.UI.Button LockButton => lockButton;
        public string ResourceText => resources.text;
        public string DetailText => detailLabel.text;
        private UnityEngine.UI.Button ActionButton(string name,Vector2 size,Vector2 position,Action action)
        {
            var rect=Rect(name,canvasRect,size,position);var image=rect.gameObject.AddComponent<UnityEngine.UI.Image>();image.color=new Color(.16f,.26f,.35f);
            var button=rect.gameObject.AddComponent<UnityEngine.UI.Button>();button.targetGraphic=image;button.onClick.AddListener(()=>action());
            Label("Label",rect,name,14,size-Vector2.one*4,Vector2.zero);return button;
        }
        private RectTransform Panel(string name,Vector2 size,Vector2 position)
        {
            var rect=Rect(name,canvasRect,size,position);rect.gameObject.AddComponent<UnityEngine.UI.Image>().color=new Color(.065f,.105f,.16f);return rect;
        }
        private static void ButtonText(UnityEngine.UI.Button button,string text) => button.GetComponentInChildren<UnityEngine.UI.Text>(true).text=text;
        private void BuildMinimumUI()
        {
            Panel("PlayerSidebar",new Vector2(205,160),new Vector2(-510,202));
            playersLabel=Label("Players",canvasRect,"",13,new Vector2(190,150),new Vector2(-510,202));
            Panel("Inventory",new Vector2(205,140),new Vector2(-510,30));
            inventoryLabel=Label("InventoryText",canvasRect,"",14,new Vector2(190,125),new Vector2(-510,30));
            Label("Controls",canvasRect,"Click: select unit\nDrag: move / swap\nEsc: cancel drag",13,new Vector2(200,65),new Vector2(-510,-98));
            Panel("UnitDetail",new Vector2(225,215),new Vector2(490,160));
            detailLabel=Label("UnitDetailText",canvasRect,"Select your unit\nto inspect or sell",14,new Vector2(210,145),new Vector2(490,185));
            sellButton=ActionButton("Sell",new Vector2(200,32),new Vector2(490,72),SellSelected);
            Label("DebugTitle",canvasRect,"DEBUG / TEST CONTROLS",12,new Vector2(220,22),new Vector2(490,13));
            ActionButton("Reset match",new Vector2(110,25),new Vector2(432,-18),ResetMatchUI);
            ActionButton("Rank demo",new Vector2(110,25),new Vector2(548,-18),ResetRankDemo);
            ActionButton("Full bench",new Vector2(110,25),new Vector2(432,-50),ResetFullBenchDemo);
            ActionButton("Fixed foe",new Vector2(110,25),new Vector2(548,-50),ResetOpponent);
            ActionButton("4 players",new Vector2(72,25),new Vector2(414,-82),()=>ResetPairingDemo(4));
            ActionButton("6 players",new Vector2(72,25),new Vector2(490,-82),()=>ResetPairingDemo(6));
            ActionButton("8 players",new Vector2(72,25),new Vector2(566,-82),()=>ResetPairingDemo(8));
            ActionButton("3 players",new Vector2(72,25),new Vector2(414,-112),()=>ResetPairingDemo(3));
            ActionButton("5 players",new Vector2(72,25),new Vector2(490,-112),()=>ResetPairingDemo(5));
            ActionButton("7 players",new Vector2(72,25),new Vector2(566,-112),()=>ResetPairingDemo(7));
            debugLabel=Label("DebugState",canvasRect,"",8,new Vector2(225,12),new Vector2(490,-132));
            shopFrame=Panel("ShopFrame",new Vector2(1220,200),new Vector2(0,-240));
            status=Label("Feedback",canvasRect,"",14,new Vector2(1180,24),new Vector2(0,-152));
            resources=Label("Resources",canvasRect,"",15,new Vector2(955,30),new Vector2(-125,-183));
            shopToggle=ActionButton("Shop",new Vector2(170,27),new Vector2(490,-182),ToggleShop);
            for(int i=0;i<ShopRules.SlotCount;i++)
            {
                int slot=i;shopCards[i]=ActionButton("ShopCard_"+i,new Vector2(170,100),new Vector2(-500+i*190,-260),()=>BuySlot(slot));
                cardText[i]=shopCards[i].GetComponentInChildren<UnityEngine.UI.Text>();cardText[i].fontSize=13;
            }
            rerollButton=ActionButton("Reroll",new Vector2(170,32),new Vector2(490,-224),RequestReroll);
            lockButton=ActionButton("Lock",new Vector2(170,32),new Vector2(490,-263),RequestLock);
            xpButton=ActionButton("Buy XP",new Vector2(170,32),new Vector2(490,-302),()=>Perform(()=>commands.BuyXP()));
            ConfigureShopPanel();BuildRoundUI();
        }
        private void ConfigureShopPanel()
        {
            foreach(var rect in new[]{status.rectTransform,resources.rectTransform,(RectTransform)shopToggle.transform,(RectTransform)rerollButton.transform,(RectTransform)lockButton.transform,(RectTransform)xpButton.transform}.Concat(shopCards.Select(b=>(RectTransform)b.transform)))
                rect.SetParent(shopFrame,true);
            shopFrame.anchorMin=shopFrame.anchorMax=new Vector2(.5f,0);
            shopFrame.pivot=new Vector2(.5f,0);shopFrame.sizeDelta=new Vector2(1220,ShopHeight);shopFrame.anchoredPosition=new Vector2(0,20);
        }
        private static PokemonDefinition[] CreateSandboxDefinitions()
        {
            var stats=new PokemonStats(100,10,0,1,0,0,1,2,0,0,.25f);
            return new[]{"bulbasaur","charmander","squirtle","pikachu"}.Select(id=>new PokemonDefinition(id,id,1,stats,"role","skill"))
                .Concat(Enumerable.Range(2,4).Select(c=>new PokemonDefinition("sample-c"+c,"Cost "+c+" sample",c,stats,"role","skill"))).ToArray();
        }
        private void AttachMinimumMatch()
        {
            foreach(var player in Match.Players)if(!player.Shop.IsInitialized)new ShopSystem().RefreshForRound(Match,player.PlayerId);
            commands=new LocalMatchCommandGateway(Match,catalog,"p1");selectedUnitId=null;LastPurchase=null;shopCollapsed=false;shopPhase=null;highlights.Clear();AttachRoundLoop();
        }
        public void ResetMatchUI()
        {
            CancelDrag("Reset.");catalog=new PokemonCatalog(CreateSandboxDefinitions());
            Match=MatchStateFactory.CreateWithPool("minimum-ui",new[]{"p1","p2"},catalog,CreateSandboxDefinitions().Select(d=>d.Id),matchSeed:123);
            Match.TransitionTo(MatchPhase.Starting);Match.TransitionTo(MatchPhase.Preparation);AttachMinimumMatch();LastResult=null;
            ResetOpponent();Feedback("Buy and arrange units within 30 seconds. Empty board slots fill from the bench before battle.");Render();
        }
        public void ResetPairingDemo(int playerCount)
        {
            if(playerCount<3 || playerCount>8)throw new ArgumentOutOfRangeException(nameof(playerCount));
            CancelDrag("Reset pairing demo.");catalog=new PokemonCatalog(CreateSandboxDefinitions());
            Match=MatchStateFactory.CreateWithPool("pairing-demo",Enumerable.Range(1,playerCount).Select(i=>"p"+i),catalog,CreateSandboxDefinitions().Select(d=>d.Id),matchSeed:123);
            foreach(var player in Match.Players)
                SharedPoolSystem.RegisterUnit(Match,catalog.CreateUnit("demo-"+player.PlayerId,"bulbasaur",player.PlayerId,UnitRank.One,0,UnitPlacement.OnBench(0)),"bulbasaur");
            Match.TransitionTo(MatchPhase.Starting);Match.TransitionTo(MatchPhase.Preparation);AttachMinimumMatch();LastResult=null;
            Feedback(playerCount+" players: automatic deployment, pairing and parallel battles. Opponents are revealed at combat start.");Render();
        }
        public void ResetFullBenchDemo()
        {
            CancelDrag("Reset full bench.");Match=MatchStateFactory.CreateWithPool("full-bench",new[]{"p1","p2"},catalog,new[]{"bulbasaur"},
                poolRules:new PoolRules(new[]{100,18,14,10,9}),matchSeed:123);Player.SetProgress(20,0,1);
            for(int i=0;i<9;i++) SharedPoolSystem.RegisterUnit(Match,catalog.CreateUnit(((char)('A'+i)).ToString(),"bulbasaur","p1",i<2 ? UnitRank.One : UnitRank.Three,0,UnitPlacement.OnBench(i)),"bulbasaur");
            Match.TransitionTo(MatchPhase.Starting);Match.TransitionTo(MatchPhase.Preparation);AttachMinimumMatch();
            Feedback("Full bench demo: buying a card merges A + B and creates space. Debug pool has 100 copies.");Render();
        }
        private void Perform(Func<MatchCommandResult> action)
        {
            if(busy) return;
            if(RoundLoop!=null && RoundLoop.NextPreparationPending) { Feedback("Finish shop refresh before sending commands.");return; }
            busy=true;CancelDrag("Action requested.");
            try
            {
                var result=action();LastPurchase=result.Purchase;
                if(!result.Accepted) Feedback(result.Message);
                else if(result.Purchase!=null)
                {
                    var purchase=result.Purchase;highlights[purchase.Unit.InstanceId]=Time.unscaledTime+1.2f;
                    string rank=purchase.RankUps.Count==0 ? "" : " / rank "+string.Join(" -> ",purchase.RankUps.Select(e=>(int)e.Rank));
                    int returned=purchase.RankUps.Sum(e=>e.ReturnedItemIds.Count);
                    Feedback("Bought "+catalog.Get(purchase.Unit.DefinitionId).DisplayName+rank+(returned==0 ? "." : "; "+returned+" item(s) returned.")+(purchase.MergeNextPreparation ? " Merge pending: next preparation (board copies required)." : ""));
                }
                else Feedback(result.Message);
            }
            finally { busy=false;Render();Match.Pool.AssertConservation(Match); }
        }
        public void RefreshFromState()
        {
            if(IsDragging && Player.PlacementRevision!=dragRevision) CancelDrag("Placement changed.");
            Render();
        }
        public void BuySlot(int slot) { long revision=displayedShopRevision;Perform(()=>commands.Buy(slot,revision)); }
        private void RequestReroll() { long revision=displayedShopRevision;Perform(()=>commands.Reroll(revision)); }
        private void RequestLock() { long revision=displayedShopRevision;bool locked=!displayedLocked;Perform(()=>commands.Lock(locked,revision)); }
        public void SelectUnit(string id)
        {
            if(IsDragging) return;
            selectedUnitId=Player.Units.Any(u=>u.InstanceId==id) ? id : null;Render();
        }
        public void SellSelected()
        {
            if(selectedUnitId==null) { Feedback("Select your unit first.");return; }
            string id=selectedUnitId;long revision=displayedPlacementRevision;Perform(()=>commands.Sell(id,revision));
        }
        public void ToggleShop() { if(Match.Phase==MatchPhase.Preparation || Match.Phase==MatchPhase.Combat) { shopCollapsed=!shopCollapsed;Render(); } }
        private Color BaseSlotColor(Slot slot) => slot.Placement.Kind==PlacementKind.Board && Match.Phase!=MatchPhase.Preparation ? new Color(.12f,.12f,.18f) : slot.Color;
        private void UpdateTokenFeedback()
        {
            foreach(var pair in tokens)
            {
                bool highlighted=highlights.TryGetValue(pair.Key,out float end) && end>Time.unscaledTime;
                pair.Value.GetComponent<UnityEngine.UI.Image>().color=highlighted ? new Color(.22f,.85f,.53f) : pair.Key==selectedUnitId ? new Color(.3f,.73f,.94f) : new Color(.88f,.63f,.22f);
            }
        }
        private void RenderMinimumUI()
        {
            if(shopPhase!=Match.Phase) { shopCollapsed=Match.Phase!=MatchPhase.Preparation;shopPhase=Match.Phase; }
            if(commands==null) return;
            surrenderButton.interactable=!Player.IsEliminated && Player.HP>0 && (Match.Phase==MatchPhase.Preparation || Match.Phase==MatchPhase.Combat || Match.Phase==MatchPhase.Result);
            displayedShopRevision=Player.Shop.Revision;displayedPlacementRevision=Player.PlacementRevision;displayedLocked=Player.Shop.IsLocked;
            bool live=Player.HP>0 && !Player.IsEliminated;bool trade=commands.CanTrade && !busy && !RoundLoop.NextPreparationPending;
            header.text="ROUND "+Match.RoundNumber+"    |    BOARD "+Player.DeployedUnitCount+" / "+Player.BoardCapacity+"    |    "+(trade ? "BOARD OPEN" : "BOARD LOCKED");
            playersLabel.text="PLAYERS\n"+string.Join("\n",Match.Players.Select(p=>(p.PlayerId=="p1" ? "YOU " : "")+p.PlayerId+" HP "+p.HP+(p.Elimination==null ? "" : " #"+p.FinalPlacement+" "+(p.Elimination.Reason==EliminationReason.Surrender ? "SURRENDER" : "OUT"))));
            inventoryLabel.text="ITEMS ("+Player.ItemInventory.Count+")\n"+(Player.ItemInventory.Count==0 ? "No items" : string.Join("\n",Player.ItemInventory.Take(4)))+(Player.ItemInventory.Count>4 ? "\n+"+(Player.ItemInventory.Count-4)+" more" : "");
            string xp=Player.Level==commands.LevelRules.MaxLevel ? "MAX" : Player.XP+" / "+commands.LevelRules.XPToNextLevel(Player.Level);
            resources.text="GOLD "+Player.Gold+"   |   LV "+Player.Level+"   XP "+xp+"   |   STREAK W"+Player.WinStreak+" / L"+Player.LoseStreak;
            debugLabel.text=Match.Phase+" / Shop "+Player.Shop.Revision+" / Board "+Player.PlacementRevision;
            shopFrame.GetComponent<UnityEngine.UI.Image>().color=new Color(.065f,.105f,.16f);
            shopToggle.interactable=live && (Match.Phase==MatchPhase.Preparation || Match.Phase==MatchPhase.Combat);ButtonText(shopToggle,ShopExpanded ? "Collapse shop" : "Open shop ("+(Player.Shop.IsLocked ? "LOCKED" : "unlocked")+")");
            for(int i=0;i<shopCards.Length;i++)
            {
                shopCards[i].gameObject.SetActive(ShopExpanded);var offer=Player.Shop.Slots[i];
                if(offer.IsEmpty) { cardText[i].text="EMPTY SLOT";shopCards[i].interactable=false;continue; }
                var availability=commands.PreviewBuy(i,displayedShopRevision);var definition=catalog.Get(offer.DefinitionId);
                int owned=Player.Units.Count(u=>u.DefinitionId==offer.DefinitionId && u.EvolutionStage==0);
                cardText[i].text=definition.DisplayName+"\nCost "+offer.Cost+" / "+offer.Cost+"G\nOwned units: "+owned+"\n"+(availability.Accepted ? availability.Preview.MergeNextPreparation ? (availability.Preview.RankUpCount>0 ? "BENCH RANK UP + NEXT PREP MERGE" : "BUY / MERGE NEXT PREP") : availability.Preview.RankUpCount>0 ? "BUY + RANK UP" : "BUY" : availability.Message);
                shopCards[i].interactable=commands.CanBuy && !busy && !RoundLoop.NextPreparationPending && availability.Accepted;
            }
            rerollButton.gameObject.SetActive(ShopExpanded);lockButton.gameObject.SetActive(ShopExpanded);xpButton.gameObject.SetActive(ShopExpanded);
            rerollButton.interactable=trade && Player.Gold>=commands.ShopRules.RerollGoldCost;ButtonText(rerollButton,"Reroll / "+commands.ShopRules.RerollGoldCost+"G");
            lockButton.interactable=trade;ButtonText(lockButton,Player.Shop.IsLocked ? "LOCKED / Unlock" : "Lock next refresh");
            xpButton.interactable=trade && Player.Level<commands.LevelRules.MaxLevel && Player.Gold>=commands.LevelRules.PurchaseGoldCost;
            ButtonText(xpButton,Player.Level==commands.LevelRules.MaxLevel ? "XP MAX" : "Buy "+commands.LevelRules.PurchaseXP+" XP / "+commands.LevelRules.PurchaseGoldCost+"G");
            var selected=Player.Units.FirstOrDefault(u=>u.InstanceId==selectedUnitId);
            if(selected==null) { selectedUnitId=null;detailLabel.text="SELECT YOUR UNIT\nClick board or bench\nto inspect and sell.";sellButton.interactable=false;ButtonText(sellButton,"Sell"); }
            else
            {
                int price=commands.SalePrice(selectedUnitId);
                detailLabel.text=catalog.Get(selected.DefinitionId).DisplayName+" / R"+(int)selected.Rank+"\n"+selected.InstanceId+" / stage "+selected.EvolutionStage+"\n"+selected.Placement.Kind+"\nItems: "+selected.ItemInstanceIds.Count+"\n"+string.Join(", ",selected.ItemInstanceIds.Take(2));
                sellButton.interactable=trade && selected.Placement.Kind!=PlacementKind.Unplaced;ButtonText(sellButton,"Sell / +"+price+"G");
            }

            UpdateTokenFeedback();RenderRoundUI();
        }
    }
}






