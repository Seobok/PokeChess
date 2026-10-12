using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using PokeChess.Client.UI;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;
public static class VerifyPlacementSale {
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static void Refresh(PlacementSandboxView v)=>typeof(PlacementSandboxView).GetMethod("UpdateTablePresentation",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(v,null);
    public static string Main(){
        EditorApplication.isPaused=true;var v=UnityEngine.Object.FindFirstObjectByType<PlacementSandboxView>();
        v.ResetRosterDemo();v.ShopButton(0).onClick.Invoke();
        var bench=v.Player.Units.First(u=>u.Placement.Kind==PlacementKind.Bench);
        v.BeginDrag(bench.InstanceId,v.ScreenPoint(bench.Placement));Refresh(v);
        var hexes=v.GetComponentsInChildren<PlacementHexGraphic>(true).Where(h=>h.OutlineOnly).ToArray();
        Check(hexes.Length==56&&hexes.Skip(28).All(h=>h.color.r>h.color.g),"Enemy half red");
        Check(hexes.Where(h=>h.name.StartsWith("Board_")).Any(h=>h.color.r>h.color.g),"Full capacity forbidden cells red");
        v.CancelDrag("QA");
        v.AdvanceRoundTime(30);Check(v.Match.Phase==MatchPhase.Combat,"Combat start");
        var battle=v.RoundLoop.GetBattleFor("p1").Battle;int fighters=battle.Units.Count;
        v.SelectUnit(bench.InstanceId);Check(v.SellButton.interactable,"Combat bench sell button");
        int gold=v.Player.Gold;v.SellButton.onClick.Invoke();
        Check(!v.Player.Units.Any(u=>u.InstanceId==bench.InstanceId)&&v.Player.Gold>gold,"Combat sale applied");
        Check(battle.Units.Count==fighters,"Combat roster unchanged");
        var executor=new MatchCommandExecutor(v.Match,PrototypeRoster.CreateCatalog(),"p1");
        var board=v.Player.Units.First(u=>u.Placement.Kind==PlacementKind.Board);
        gold=v.Player.Gold;Check(!executor.Sell(board.InstanceId,v.Player.PlacementRevision).Accepted&&v.Player.Gold==gold,"Combat board sale rejected");
        v.ShopButton(1).onClick.Invoke();bench=v.Player.Units.First(u=>u.Placement.Kind==PlacementKind.Bench);
        v.BeginDrag(bench.InstanceId,v.ScreenPoint(bench.Placement));Refresh(v);
        Check(hexes.All(h=>h.color.r>h.color.g),"Combat board all red");
        v.ToggleShop();var shop=v.ShopRect;Canvas.ForceUpdateCanvases();
        var target=RectTransformUtility.WorldToScreenPoint(null,shop.position+new Vector3(0,50,0));
        v.EndDrag(target);
        Check(!v.Player.Units.Any(u=>u.InstanceId==bench.InstanceId),"Combat bench drag sale");
        v.ResetRosterDemo();
        return "PASS: forbidden enemy/capacity/combat cells red; combat bench button and drag sale; board sale blocked; battle roster preserved.";
    }
}
