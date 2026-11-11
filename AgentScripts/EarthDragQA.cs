using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using PokeChess.Client.UI;
using PokeChess.Core.Pokemon;
using PokeChess.Core.Match;
using PokeChess.Content;
public static class EarthDragQA
{
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    public static void Prepare(){UnityEngine.Object.FindFirstObjectByType<SynergyLabController>().Select("earth",6);UnityEditor.EditorApplication.Step();}
    public static object Apply()
    {
        var lab=UnityEngine.Object.FindFirstObjectByType<SynergyLabController>();
        var view=lab.View;Canvas.ForceUpdateCanvases();var catalog=GameContent.CreateCatalog();
        var rocks=SynergyPreparation.PreparationRocks(view.Player,catalog);Check(rocks.Length==2,"automatic two rocks");
        var tokens=view.GetComponentsInChildren<EarthRockDragToken>(true).Where(t=>t.GetComponent<UnityEngine.UI.Image>().enabled).ToArray();Check(tokens.Length==2,"two visible rock tokens");
        var token=tokens.Single(t=>t.Slot==0);var source=view.ScreenPoint(UnitPlacement.OnBoard(rocks[0]));
        var e=new PointerEventData(EventSystem.current){position=source,button=PointerEventData.InputButton.Left};var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(e,hits);
        Check(hits.Count>0&&hits[0].gameObject.GetComponent<EarthRockDragToken>()==token,"rock is top pointer target: "+string.Join(",",hits.Select(h=>h.gameObject.name)));
        ExecuteEvents.Execute(token.gameObject,e,ExecuteEvents.beginDragHandler);Check(view.IsEarthRockDragging,"begin drag event");
        e.position=view.ScreenPoint(UnitPlacement.OnBoard(new BoardPosition(6,0)));ExecuteEvents.Execute(token.gameObject,e,ExecuteEvents.dragHandler);ExecuteEvents.Execute(token.gameObject,e,ExecuteEvents.endDragHandler);
        Check(!view.IsEarthRockDragging&&view.Player.Synergy.RockTiles[0].Equals(new BoardPosition(6,0)),"drop persists location");
        view.BeginEarthRockDrag(0,e.position);view.EndEarthRockDrag(view.ScreenPoint(view.Player.Units.First().Placement));Check(view.Player.Synergy.RockTiles[0].Equals(new BoardPosition(6,0)),"occupied drop rejected");
        view.BeginEarthRockDrag(0,e.position);view.EndEarthRockDrag(new Vector2(-100,-100));Check(view.Player.Synergy.RockTiles[0].Equals(new BoardPosition(6,0)),"outside drop cancels");
        view.BeginEarthRockDrag(0,e.position);view.CancelDrag("QA cancel");Check(!view.IsEarthRockDragging,"cancel clears drag");
        lab.Step();Check(view.RoundLoop.Battle.Synergies.RockTiles(1).Contains(new BoardPosition(6,0)),"combat uses dragged position");view.BeginEarthRockDrag(0,e.position);Check(!view.IsEarthRockDragging,"combat input locked");
        lab.Select("earth",2);Check(SynergyPreparation.PreparationRocks(view.Player,catalog).Length==1,"tier two one rock");
        lab.Select("earth",6);Canvas.ForceUpdateCanvases();UnityEditor.EditorWindow.GetWindow(typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();UnityEditor.EditorApplication.isPaused=true;UnityEditor.EditorApplication.Step();
        return "PASS: automatic display, raycast, drag/drop events, occupied/outside rejection, cancel, combat position/lock, tier counts";
    }
}
