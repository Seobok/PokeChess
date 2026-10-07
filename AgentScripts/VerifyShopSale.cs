using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using PokeChess.Client.UI;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;
public static class VerifyShopSale {
    static void Check(bool v,string message){if(!v)throw new Exception(message);}
    static Vector2 ShopPoint(PlacementSandboxView v)=>RectTransformUtility.WorldToScreenPoint(null,v.ShopRect.TransformPoint(new Vector3(0,160,0)));
    public static string Main() {
        var v=UnityEngine.Object.FindFirstObjectByType<PlacementSandboxView>();
        EditorApplication.isPaused=true;v.ResetRankDemo();v.ShopRect.anchoredPosition=new Vector2(0,20);
        int gold=v.Player.Gold;v.BeginDrag("B",ShopPoint(v));Check(v.IsDragging,"Begin bench sale drag");v.EndDrag(ShopPoint(v));
        Check(!v.Player.Units.Any(u=>u.InstanceId=="B")&&v.Player.Gold>gold&&v.Player.ItemInventory.Contains("demo-item"),"Drop sale / items");
        v.Match.Pool.AssertConservation(v.Match);
        v.ResetRankDemo();v.ShopRect.anchoredPosition=new Vector2(0,20);gold=v.Player.Gold;
        v.BeginDrag("B",ShopPoint(v));v.Player.SetPlacement("A",UnitPlacement.OnBoard(new BoardPosition(3,0)));v.EndDrag(ShopPoint(v));
        Check(v.Player.Gold==gold&&v.Player.Units.Any(u=>u.InstanceId=="B")&&v.LastFeedback.Contains("changed"),"Stale sale rejected");
        v.ResetRankDemo();v.RoundLoop.StartCombat(v.Match.RoundNumber);v.RoundFlow.RefreshState();v.RefreshFromState();v.ToggleShop();
        v.ShopRect.anchoredPosition=new Vector2(0,20);gold=v.Player.Gold;v.BeginDrag("B",ShopPoint(v));v.EndDrag(ShopPoint(v));
        Check(v.Player.Gold==gold&&v.Player.Units.Any(u=>u.InstanceId=="B"),"Combat sale rejected");
        v.ResetRankDemo();v.ToggleShop();v.ShopRect.anchoredPosition=new Vector2(0,-136);gold=v.Player.Gold;
        v.BeginDrag("B",ShopPoint(v));v.EndDrag(ShopPoint(v));Check(v.Player.Gold>gold,"Collapsed header sale");
        v.Match.Pool.AssertConservation(v.Match);
        v.ResetHighCostRosterDemo();v.Player.SetProgress(25,2,4);v.RefreshFromState();v.ShopRect.anchoredPosition=new Vector2(0,20);
        return "Sale passed: expanded / collapsed shop drop, item return / pool conservation, stale drag / combat sale rejection.";
    }
}
