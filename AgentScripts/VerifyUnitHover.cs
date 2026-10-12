using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEngine.EventSystems;
using PokeChess.Client.UI;
using PokeChess.Core.Match;
public static class VerifyUnitHover {
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    public static string Main(){
        EditorApplication.isPaused=true;var v=UnityEngine.Object.FindFirstObjectByType<PlacementSandboxView>();v.ResetRosterDemo();
        var data=new PointerEventData(EventSystem.current);var unit=v.Player.Units.First();
        v.SelectUnit(unit.InstanceId);Check(!v.UnitPopupVisible,"Click must not open info");
        v.Token(unit.InstanceId).OnPointerEnter(data);Check(v.UnitPopupVisible,"Board hover");
        v.Token(unit.InstanceId).OnPointerExit(data);Check(!v.UnitPopupVisible&&v.SelectedUnitId==null,"Immediate exit hides info");
        int slot=Enumerable.Range(0,5).First(i=>!v.Player.Shop.Slots[i].IsEmpty);
        var target=v.ShopButton(slot).GetComponent<UnitHoverTarget>();
        target.OnPointerEnter(data);Check(v.UnitPopupVisible&&v.HoveredShopSlot==slot,"Shop hover without click");
        Check(!v.SellButton.gameObject.activeSelf,"Shop info has no sale action");
        var title=v.GetComponentsInChildren<UnityEngine.UI.Text>(true).First(t=>t.transform.parent.name=="UnitInfoIcons"&&t.name=="Name").text;
        Check(title.Length>0&&title.Contains("★"),"Rank one shop info");
        target.OnPointerExit(data);Check(!v.UnitPopupVisible,"Shop immediate exit");
        target.OnPointerEnter(data);v.RerollButton.onClick.Invoke();Check(!v.UnitPopupVisible,"Reroll clears stale shop info");
        slot=Enumerable.Range(0,5).First(i=>!v.Player.Shop.Slots[i].IsEmpty);target=v.ShopButton(slot).GetComponent<UnitHoverTarget>();
        target.OnPointerEnter(data);v.ShopButton(slot).onClick.Invoke();Check(!v.UnitPopupVisible,"Purchase clears empty offer info");
        var bench=v.Player.Units.First(u=>u.Placement.Kind==PokeChess.Core.Pokemon.PlacementKind.Bench);
        v.Token(bench.InstanceId).OnPointerEnter(data);Check(v.UnitPopupVisible&&v.SellButton.gameObject.activeSelf,"Bench hover restores actions");
        v.Token(bench.InstanceId).OnPointerExit(data);Check(!v.UnitPopupVisible,"Bench immediate exit");
        v.AdvanceRoundTime(30);var fighter=v.GetComponentsInChildren<UnitHoverTarget>().First(t=>t.ShopSlot<0);
        fighter.OnPointerEnter(data);Check(v.UnitPopupVisible,"Combat hover");
        fighter.OnPointerExit(data);Check(!v.UnitPopupVisible,"Combat immediate exit");
        v.ResetRosterDemo();return "PASS: unit/bench/combat immediate hover exit; shop info and rank; sale hidden; reroll/purchase invalidation.";
    }
}
