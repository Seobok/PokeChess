using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEngine.EventSystems;
using PokeChess.Client.UI;
using PokeChess.Core.Match;
public static class VerifyUnitHover {
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    public static string Main(){
        EditorApplication.isPaused=true;var v=UnityEngine.Object.FindFirstObjectByType<PlacementSandboxView>();v.ResetRosterDemo();
        var data=new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left,eligibleForClick=true};var unit=v.Player.Units.First();
        v.Token(unit.InstanceId).OnPointerEnter(data);Check(v.UnitPopupVisible&&!v.UnitInfoPinned,"Hover transient");
        v.Token(unit.InstanceId).OnPointerExit(data);Check(!v.UnitPopupVisible,"Hover exit immediate");
        v.Token(unit.InstanceId).OnPointerEnter(data);v.Token(unit.InstanceId).OnPointerClick(data);
        Check(v.UnitInfoPinned&&v.UnitPopupVisible,"Click pins");
        v.Token(unit.InstanceId).OnPointerExit(data);Check(v.UnitPopupVisible,"Pinned exit stays");
        var other=v.Player.Units.Skip(1).First();v.Token(other.InstanceId).OnPointerEnter(data);
        Check(v.SelectedUnitId==unit.InstanceId,"Hover preserves pinned target");
        v.SelectUnit(other.InstanceId);Check(v.UnitPopupVisible&&v.SelectedUnitId==other.InstanceId,"Click replaces pinned target");
        typeof(PlacementSandboxView).GetMethod("ShowUnitInfoHint",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(v,new object[]{-1});
        Check(v.GetComponentsInChildren<RectTransform>(true).First(t=>t.name=="UnitInfoTooltip").gameObject.activeSelf,"Skill description readable");
        v.GetComponentsInChildren<RectTransform>(true).First(t=>t.name=="Background").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
        Check(!v.UnitPopupVisible&&!v.UnitInfoPinned,"Background releases pin");
        int slot=Enumerable.Range(0,5).First(i=>!v.Player.Shop.Slots[i].IsEmpty);
        var shop=v.ShopButton(slot).GetComponent<UnitHoverTarget>();shop.OnPointerEnter(data);Check(v.UnitPopupVisible&&!v.SellButton.gameObject.activeSelf,"Shop hover preserved");
        shop.OnPointerExit(data);Check(!v.UnitPopupVisible,"Shop exit immediate");
        v.AdvanceRoundTime(30);
        var battle=v.RoundLoop.GetBattleFor("p1").Battle;
        var enemy=battle.Units.First(u=>u.OwnerPlayerId!="p1");
        var target=v.GetComponentsInChildren<UnitHoverTarget>().First(t=>t.UnitId==enemy.UnitInstanceId);
        target.OnPointerEnter(data);Check(v.UnitPopupVisible&&!v.UnitInfoPinned&&!v.SellButton.interactable,"Enemy hover read only");
        target.OnPointerExit(data);Check(!v.UnitPopupVisible,"Enemy hover exit");
        target.OnPointerEnter(data);target.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();target.OnPointerExit(data);
        Check(v.UnitPopupVisible&&v.UnitInfoPinned&&v.SelectedUnitId==enemy.UnitInstanceId,"Enemy click pins");
        typeof(PlacementSandboxView).GetMethod("ShowUnitInfoHint",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(v,new object[]{-1});
        Check(v.GetComponentsInChildren<RectTransform>(true).First(t=>t.name=="UnitInfoTooltip").gameObject.activeSelf,"Enemy skill description");
        v.DeselectUnit();Check(!v.UnitPopupVisible,"Enemy unpin");v.ResetRosterDemo();
        return "PASS: transient hover/immediate exit; click pin; stable target and replacement; skill tooltip; background unpin; shop hover; enemy hover/pin/skill and read-only.";
    }
}
