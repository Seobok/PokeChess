using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEditor;
using PokeChess.Client.UI;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;
public static class VerifyUnitPanelImages {
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static Transform Child(PlacementSandboxView v,string name)=>v.GetComponentsInChildren<Transform>(true).First(t=>t.name==name);
    public static string Main(){
        EditorApplication.isPaused=true;var v=UnityEngine.Object.FindFirstObjectByType<PlacementSandboxView>();v.ResetRosterDemo();
        var unit=v.Player.Units.First();var catalog=PrototypeRoster.CreateCatalog();v.SelectUnit(unit.InstanceId);
        Check(Child(v,"UnitPortrait").GetComponent<UnityEngine.UI.Image>().sprite!=null,"Portrait sprite");
        Check(Child(v,"PositionLabel").GetComponent<UnityEngine.UI.Text>().text=="전방","Range <=2 front");
        var data=new PointerEventData(EventSystem.current);
        ExecuteEvents.Execute<IPointerEnterHandler>(Child(v,"SkillSlot").gameObject,data,(h,e)=>h.OnPointerEnter((PointerEventData)e));
        Check(Child(v,"UnitInfoTooltip").gameObject.activeSelf,"Skill hover tooltip");
        Check(Child(v,"UnitInfoTooltip").Find("Hint").GetComponent<UnityEngine.UI.Text>().text.Length>0,"Skill description");
        string item="item:leftovers:qa:p1";v.Player.AddInventoryItem(item);new ItemSystem().Equip(v.Match,"p1",unit.InstanceId,item,v.Player.ItemRevision);v.SelectUnit(unit.InstanceId);
        var image=Child(v,"EquippedIcon_0").GetComponent<UnityEngine.UI.Image>();
        Check(image.gameObject.activeSelf&&image.sprite!=null,"Equipped item sprite");
        ExecuteEvents.Execute<IPointerEnterHandler>(image.gameObject,data,(h,e)=>h.OnPointerEnter((PointerEventData)e));
        Check(Child(v,"UnitInfoTooltip").Find("Hint").GetComponent<UnityEngine.UI.Text>().text.Contains("먹다남은"),"Equipped item hover");
        new ItemSystem().Unequip(v.Match,"p1",unit.InstanceId,v.Player.ItemRevision);v.SelectUnit(unit.InstanceId);
        Check(!image.gameObject.activeSelf,"Unequip removes stale icon");
        v.ResetHighCostRosterDemo();unit=v.Player.Units.First(u=>catalog.Get(u.DefinitionId).BaseStats.AttackRange>=3);v.SelectUnit(unit.InstanceId);
        Check(Child(v,"PositionLabel").GetComponent<UnityEngine.UI.Text>().text=="후방","Range >=3 rear");
        v.DeselectUnit();v.HoverShopUnit(0);Check(Child(v,"UnitPortrait").GetComponent<UnityEngine.UI.Image>().sprite!=null,"Shop portrait");
        v.DeselectUnit();v.ResetRosterDemo();v.AdvanceRoundTime(30);
        unit=v.Player.Units.First();var enemy=v.RoundLoop.GetBattleFor("p1").Battle.Units.First(u=>u.OwnerPlayerId!="p1");v.SelectUnit(enemy.UnitInstanceId);
        Check(v.UnitPopupVisible&&Child(v,"UnitPortrait").GetComponent<UnityEngine.UI.Image>().sprite!=null,"Enemy portrait");
        Check(!v.SellButton.interactable,"Enemy remains read only");
        v.ResetRosterDemo();return "PASS: portrait, skill hover, equipped icon and hover/removal, front/rear range rule, shop/enemy portraits.";
    }
}

