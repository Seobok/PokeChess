using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using PokeChess.Client.UI;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;
using PokeChess.Core.Battle;
public static class VerifyTableLayout {
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static object Call(PlacementSandboxView v,string method,params object[] args)=>typeof(PlacementSandboxView).GetMethod(method,Private).Invoke(v,args);
    static T Field<T>(PlacementSandboxView v,string name)=>(T)typeof(PlacementSandboxView).GetField(name,Private).GetValue(v);
    public static string Main(){
        EditorApplication.isPaused=true;
        var v=UnityEngine.Object.FindFirstObjectByType<PlacementSandboxView>();
        v.ResetSynergyDemo();Call(v,"UpdateTablePresentation");
        Check(v.GetComponentsInChildren<PlacementHexGraphic>(true).Count(h=>h.OutlineOnly)==56,"56 board outlines");
        Check(!v.BoardOutlineVisible,"Idle grid hidden");
        Check(!v.UnitPopupVisible,"Idle popup hidden");
        var sidebar=Field<RectTransform>(v,"sidebar");Check(sidebar.anchoredPosition.x>450,"Right player list");
        Check(Field<RectTransform>(v,"developerPanel").gameObject.activeSelf==false,"Developer panel collapsed");
        Check(v.GetComponentsInChildren<RectTransform>(true).Count(t=>t.name.StartsWith("Bench_")&&t.gameObject.activeSelf)==9,"Persistent bench slots");
        v.HoverUnit("p1-synergy-0");Check(v.UnitPopupVisible&&v.BoardOutlineVisible,"Selected popup and grid");
        Field<RectTransform>(v,"canvasRect").Find("Background").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
        Check(!v.UnitPopupVisible&&!v.BoardOutlineVisible&&v.SelectedUnitId==null,"Background deselection");
        for(int i=0;i<10;i++)Check(v.InventoryItemButton(i).gameObject.activeSelf,"Ten fixed inventory slots");
        string unit="p1-synergy-0";
        typeof(PlayerState).GetMethod("GrantEvolutionPoints",Private).Invoke(v.Player,new object[]{v.Match.RoundNumber,1});
        Call(v,"RenderItemWildUI");v.BeginItemDrag(0,Vector2.zero);Check(v.IsItemDragging,"Evolution point drag");
        v.EndItemDrag(v.ScreenPoint(v.Player.GetUnit(unit).Placement));
        Check(v.Player.GetUnit(unit).DefinitionId=="slowbro"&&v.Player.EvolutionPoints==0,"Evolution point application");
        for(int i=0;i<9;i++)v.Player.AddInventoryItem("item:leftovers:qa:"+i);
        Call(v,"RenderItemWildUI");
        Check(Field<string[]>(v,"visibleItemSlots").Count(x=>x!=null)==8&&v.Player.ItemInventory.Count==9,"Overflow preserved");
        var slots=Field<string[]>(v,"visibleItemSlots");var before=slots.ToArray();string waiting=v.Player.ItemInventory.Last();
        v.BeginItemDrag(2,Vector2.zero);Check(v.IsItemDragging,"Item drag starts");
        v.EndItemDrag(v.ScreenPoint(v.Player.GetUnit(unit).Placement));
        Check(v.Player.GetUnit(unit).ItemInstanceIds.Contains(before[0]),"Item equip drag");
        Check(slots[0]==waiting&&slots.Skip(1).SequenceEqual(before.Skip(1)),"Overflow fills exact empty slot");
        v.BeginItemDrag(1,Vector2.zero);Check(v.IsItemDragging,"Return drag starts");
        v.EndItemDrag(v.ScreenPoint(v.Player.GetUnit(unit).Placement));
        Check(v.Player.GetUnit(unit).ItemInstanceIds.Count==0&&v.Player.ItemInventory.Count==9,"Return succeeds while full");
        Check(slots.SequenceEqual(new[]{waiting}.Concat(before.Skip(1))),"Full visible slots stay stable");
        v.RequestPlayerView("p2");v.BeginItemDrag(1,Vector2.zero);Check(!v.IsItemDragging,"Observer item input blocked");
        Check(v.ViewedPlayerId=="p2","Sidebar scouting preserved");
        v.RequestPlayerView("p1");
        var counts=Enumerable.Range(0,10).Select(i=>new SynergyCount("qa-trait-"+i,1)).ToArray();
        Call(v,"RenderSynergies",counts,true);
        var colors=Field<Dictionary<string,UnityEngine.UI.Image>>(v,"synergyColors");
        Check(colors.Values.Count(x=>x.gameObject.activeSelf)==8,"Eight traits per page");
        Field<UnityEngine.UI.Button>(v,"synergyNext").onClick.Invoke();
        Check(colors.Values.Count(x=>x.gameObject.activeSelf)==2&&colors["qa-trait-8"].gameObject.activeSelf,"Next synergy page");
        v.ToggleShop();Check(!v.ShopExpanded,"Shop collapse");
        var shop=Field<RectTransform>(v,"shopFrame");shop.anchoredPosition=new Vector2(0,-116);
        Canvas.ForceUpdateCanvases();var handle=Field<UnityEngine.UI.Button>(v,"shopToggle");var handlePoint=RectTransformUtility.WorldToScreenPoint(null,handle.transform.position);
        Check(handlePoint.y>0&&handlePoint.y<Screen.height,"Collapsed shop handle visible");handle.onClick.Invoke();Check(v.ShopExpanded,"Shop reopen");
        shop.anchoredPosition=new Vector2(0,10);v.ResetRosterDemo();
        Check(v.SynergyDisplay.Length>0,"Real traits restored");
        var offer=v.Player.Shop.Slots.Select((o,i)=>new{o,i}).First(x=>!x.o.IsEmpty);
        int count=v.Player.Units.Count;v.ShopButton(offer.i).onClick.Invoke();
        Check(v.Player.Units.Count>count,"Shop purchase");
        var bought=v.Player.Units.Last();v.BeginDrag(bought.InstanceId,v.ScreenPoint(bought.Placement));
        Check(v.IsDragging,"Placement drag");
        var destination=v.Player.Units.First(u=>u.Placement.Kind==PlacementKind.Board).Placement;v.EndDrag(v.ScreenPoint(destination));
        Check(v.LastResult?.Accepted==true,"New board coordinates placement");
        v.AdvanceRoundTime(30);Check(v.Match.Phase==MatchPhase.Combat,"Combat starts");
        Check(!v.BoardOutlineVisible,"Combat idle grid hidden");
        Check(Field<RectTransform>(v,"combatRoot").gameObject.activeSelf,"Combat renderer");
        v.ResetSynergyDemo();
        return "PASS: evolution drag, 56 hexes, hidden idle grid, fixed bench, right sidebar, popup/deselect, 10 inventory slots, overflow FIFO/stable slots, equip/return drags, scouting, 8+2 trait pages, shop purchase, board placement, combat.";
    }
}





