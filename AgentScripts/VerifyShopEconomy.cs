using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using PokeChess.Client.UI;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;
public static class VerifyShopEconomy {
    static void Check(bool v,string message){if(!v)throw new Exception(message);}
    static int Slot(PlacementSandboxView v)=>Enumerable.Range(0,5).First(i=>!v.Player.Shop.Slots[i].IsEmpty);
    static void Tick(PlacementSandboxView v,int n){for(int i=0;i<n;i++)v.AdvanceRoundTime(1d/30);}
    static void Settle(PlacementSandboxView v) {
        v.RoundLoop.StartCombat(v.Match.RoundNumber);v.RoundFlow.RefreshState();
        if(v.RoundLoop.Battle!=null)foreach(var u in v.RoundLoop.Battle.Units.Where(u=>u.TeamId==2))u.SetVitals(0,0);
        Tick(v,2);v.RefreshFromState();
    }
    public static string Main() {
        Check(Application.isPlaying,"Enter Play mode");EditorApplication.isPaused=true;
        var v=UnityEngine.Object.FindFirstObjectByType<PlacementSandboxView>();
        v.ResetMatchUI();Check(v.ShopExpanded,"Initial expanded");
        for(int i=0;i<8;i++)v.ToggleShop();Check(v.ShopExpanded,"Repeated toggle");
        int slot=Slot(v);var before=v.Player.Shop.Slots.Select(s=>s.DefinitionId).ToArray();int price=v.Player.Shop.Slots[slot].Cost;
        v.ShopButton(slot).onClick.Invoke();Check(v.LastPurchase!=null,"Card click purchase");
        Check(v.Player.Gold==5-price&&v.ShopCardDisplay(slot).Contains("PURCHASED"),"Price / purchased slot");
        for(int i=0;i<5;i++)if(i!=slot)Check(v.Player.Shop.Slots[i].DefinitionId==before[i],"Cards shifted");
        Check(v.EconomyFeedbackText.Contains("-"+price+"G"),"Purchase delta");
        v.XPButton.onClick.Invoke();Check(v.EconomyFeedbackText.Contains("+4XP"),"XP purchase feedback");
        Check(!v.RerollButton.interactable&&!v.XPButton.interactable,"Insufficient gold buttons");
        v.ResetMatchUI();v.Player.SetProgress(20,0,10);v.RefreshFromState();
        Check(v.ResourceText.Contains("MAX")&&v.ShopXPProgress==1&&!v.XPButton.interactable,"Max level");
        v.ResetFullBenchDemo();slot=Slot(v);Check(v.ShopCardDisplay(slot).Contains("R2"),"Full bench preview");
        v.ShopButton(slot).onClick.Invoke();Check(v.LastPurchase!=null&&v.Player.Units.Count==8,"Full bench merge purchase");v.Match.Pool.AssertConservation(v.Match);
        v.ResetRankDemo();slot=Slot(v);Check(v.ShopCardDisplay(slot).Contains("R3"),"Rank 3 preview");
        v.ShopButton(slot).onClick.Invoke();Check(v.LastPurchase!=null&&v.LastPurchase.RankUps.Count==2,"Chained merge");
        v.ResetRankDemo();v.ResetOpponent();slot=Slot(v);
        v.RoundLoop.StartCombat(v.Match.RoundNumber);v.RoundFlow.RefreshState();v.RefreshFromState();
        Check(!v.ShopExpanded,"Combat auto collapse");v.ToggleShop();Check(v.ShopExpanded,"Combat manual expand");
        Check(!v.RerollButton.interactable&&!v.LockButton.interactable&&!v.XPButton.interactable&&!v.SellButton.interactable,"Combat inputs");
        var battle=v.RoundLoop.Battle;var snapshot=battle.Units.Select(u=>u.UnitInstanceId+":"+u.Rank).ToArray();
        Check(v.ShopCardDisplay(slot).Contains("NEXT PREP"),"Deferred merge preview");
        v.ShopButton(slot).onClick.Invoke();Check(v.LastPurchase!=null&&v.LastPurchase.MergeNextPreparation,"Combat deferred purchase");
        Check(battle.Units.Select(u=>u.UnitInstanceId+":"+u.Rank).SequenceEqual(snapshot),"Current battle mutated");
        Check(v.LastFeedback.Contains("Current battle unchanged"),"Combat purchase guidance");
        foreach(var u in battle.Units.Where(u=>u.TeamId==2))u.SetVitals(0,0);Tick(v,2);
        Check(v.Match.Phase==MatchPhase.Result&&!v.ShopExpanded,"Result collapse");
        int feedback=v.EconomyFeedbackCount;v.RefreshFromState();v.RefreshFromState();
        Check(v.EconomyFeedbackCount==feedback,"Settlement feedback repeated");
        v.AdvanceRoundTime(4);v.RefreshFromState();Check(v.Match.Phase==MatchPhase.Preparation&&v.Player.Units.Any(u=>u.Rank==UnitRank.Three),"Next prep merge");
        v.ResetMatchUI();slot=Slot(v);v.ShopButton(slot).onClick.Invoke();v.LockButton.onClick.Invoke();
        var locked=v.Player.Shop.Slots.Select(s=>s.DefinitionId).ToArray();Settle(v);v.AdvanceRoundTime(4);v.RefreshFromState();
        Check(v.Player.Shop.IsLocked&&v.Player.Shop.Slots.Select(s=>s.DefinitionId).SequenceEqual(locked),"Locked round refresh");
        Check(v.ShopCardDisplay(slot).Contains("PURCHASED"),"Locked empty slot retained");
        v.RerollButton.onClick.Invoke();Check(v.Player.Shop.Slots.Any(s=>!s.IsEmpty),"Manual reroll while locked");
        v.ResetMatchUI();slot=Slot(v);v.Player.SetProgress(0,0,1);v.RefreshFromState();
        Check(!v.ShopButton(slot).interactable&&v.ShopCardDisplay(slot).Contains("NOT ENOUGH GOLD"),"Card gold reason");
        v.ResetMatchUI();slot=Slot(v);var shops=new ShopSystem();shops.Reroll(v.Match,"p1");
        int gold=v.Player.Gold;v.BuySlot(slot);Check(v.LastPurchase==null&&v.Player.Gold==gold&&v.ShopCardDisplay(slot).Contains("STATE CHANGED"),"Stale rejection feedback");
        v.ResetPairingDemo(3);v.SurrenderLocalPlayer();v.RefreshFromState();
        Check(!v.ShopButton(0).interactable&&!v.XPButton.interactable&&!v.RerollButton.interactable&&!v.LockButton.interactable,"Eliminated inputs");
        v.ResetHighCostRosterDemo();v.Player.SetProgress(25,2,4);v.RefreshFromState();
        v.ShopRect.anchoredPosition=new Vector2(0,20);
        Canvas.ForceUpdateCanvases();EditorApplication.Step();
        return "Shop passed: 5 card click / price / fixed slots, collapse toggle, gold reasons, XP / MAX, full bench / chained merges, combat buy / snapshot / deferred merge, result / settlement feedback once, lock refresh / manual reroll, stale card / elimination.";
    }
}
