using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using PokeChess.Client.UI;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;

namespace PokeChess.Editor
{
    public static class PlacementSandboxTools
    {
        public const string ScenePath="Assets/_Project/Scenes/PlacementSandbox.unity";
        [MenuItem("PokeChess/Sandbox/Open Placement Sandbox")]
        public static void CreateScene()
        {
            if(EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode before opening the sandbox.");
            var scene=SceneManager.GetSceneByPath(ScenePath);
            if(!scene.isLoaded)
            {
                if(System.IO.File.Exists(ScenePath)) scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
                else
                {
                    scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
                    SceneManager.SetActiveScene(scene);
                    new GameObject("PlacementSandbox").AddComponent<PlacementSandboxView>();
                    var camera=new GameObject("PlacementCamera",typeof(Camera)).GetComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;
                    camera.backgroundColor=new Color(.035f,.06f,.10f);camera.orthographic=true;
                    EditorSceneManager.SaveScene(scene,ScenePath);
                }
            }
            SceneManager.SetActiveScene(scene);
            Selection.activeGameObject=scene.GetRootGameObjects().First(o=>o.GetComponent<PlacementSandboxView>()!=null);
        }
        private static void Check(bool condition,string message) { if(!condition) throw new Exception(message); }
        private static void Drag(PlacementSandboxView view,string id,Vector2 destination)
        {
            var data=new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left,position=view.ScreenPoint(view.Player.GetUnit(id).Placement)};
            ExecuteEvents.Execute(view.Token(id).gameObject,data,ExecuteEvents.beginDragHandler);
            data.position=destination;ExecuteEvents.Execute(view.Token(id).gameObject,data,ExecuteEvents.dragHandler);
            ExecuteEvents.Execute(view.Token(id).gameObject,data,ExecuteEvents.endDragHandler);
            foreach(var unit in view.Player.Units) Check(view.TokenMatchesState(unit.InstanceId),"Token view diverged: "+unit.InstanceId);
            view.Match.Pool.AssertConservation(view.Match);
        }
        public static string VerifyRankInteractions()
        {
            if(!EditorApplication.isPlaying) throw new Exception("Enter Play mode.");
            var view=UnityEngine.Object.FindFirstObjectByType<PlacementSandboxView>();view.ResetRankDemo();
            var buy=UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Button>(FindObjectsSortMode.None).Single(b=>b.name=="Buy copy");
            var data=new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left};
            ExecuteEvents.Execute(buy.gameObject,data,ExecuteEvents.pointerClickHandler);
            Check(view.LastPurchase.RankUps.Count==2,"Chain events");Check(view.Player.Units.Count==1 && view.Player.GetUnit("A").Rank==PokeChess.Core.Pokemon.UnitRank.Three,"R3 survivor");
            Check(view.Player.ItemInventory.Contains("demo-item"),"Returned item");Check(view.Player.Gold==19,"Purchase cost");
            Check(view.Player.DeployedUnitCount==1 && view.TokenMatchesState("A"),"Board placement");Check(!view.Token("B").gameObject.activeSelf,"Consumed token hidden");
            ExecuteEvents.Execute(buy.gameObject,data,ExecuteEvents.pointerClickHandler);Check(view.Player.Units.Count==2,"Dynamic purchased token");
            foreach(var unit in view.Player.Units) Check(view.TokenMatchesState(unit.InstanceId),"Fresh rank view");
            for(int i=0;i<3;i++) ExecuteEvents.Execute(buy.gameObject,data,ExecuteEvents.pointerClickHandler);
            int gold=view.Player.Gold;long revision=view.Player.PlacementRevision;var rng=view.Player.Shop.RandomState;
            ExecuteEvents.Execute(buy.gameObject,data,ExecuteEvents.pointerClickHandler);
            Check(view.LastPurchase==null && view.LastFeedback.Contains("No offers left"),"Empty offers reject");
            Check(view.Player.Gold==gold && view.Player.PlacementRevision==revision && view.Player.Shop.RandomState==rng,"No implicit reroll or charge");
            view.Match.Pool.AssertConservation(view.Match);view.ResetSandbox();
            return "Passed: Buy button -> two rank-up events -> R3 board survivor; item return; gold; consumed token hiding; dynamic token; pool conservation.";
        }
        public static string VerifyInteractions()
        {
            if(!EditorApplication.isPlaying) throw new Exception("Enter Play mode.");
            var view=UnityEngine.Object.FindFirstObjectByType<PlacementSandboxView>();view.ResetSandbox();
            Drag(view,"A",view.ScreenPoint(UnitPlacement.OnBoard(new BoardPosition(1,0))));Check(view.LastResult.Outcome==PlacementOutcome.Moved,"Empty board move");
            Drag(view,"A",view.ScreenPoint(view.Player.GetUnit("B").Placement));Check(view.LastResult.Outcome==PlacementOutcome.Swapped,"Board swap");
            Drag(view,"C",view.ScreenPoint(view.Player.GetUnit("A").Placement));Check(view.LastResult.Outcome==PlacementOutcome.Swapped,"Bench/board swap at cap");
            Drag(view,"A",view.ScreenPoint(view.Player.GetUnit("D").Placement));Check(view.LastResult.Outcome==PlacementOutcome.Swapped,"Bench swap");
            var before=view.Player.PlacementRevision;
            Drag(view,"A",view.ScreenPoint(UnitPlacement.OnBoard(new BoardPosition(6,3))));Check(view.LastResult.Reason==PlacementRejectReason.BoardLimit,"Cap reject");Check(view.Player.PlacementRevision==before,"Reject revision");
            Drag(view,"A",new Vector2(-100,-100));Check(!view.IsDragging && view.Player.PlacementRevision==before,"Outside drop");
            var data=new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left,position=view.ScreenPoint(view.Player.GetUnit("A").Placement)};
            ExecuteEvents.Execute(view.Token("A").gameObject,data,ExecuteEvents.beginDragHandler);view.CancelDrag("Cancelled test.");Check(!view.IsDragging && view.TokenMatchesState("A"),"Cancel");
            ExecuteEvents.Execute(view.Token("A").gameObject,data,ExecuteEvents.beginDragHandler);view.TogglePhase();Check(!view.IsDragging,"Phase cancels drag");
            Drag(view,"A",view.ScreenPoint(UnitPlacement.OnBench(3)));Check(view.LastResult.Outcome==PlacementOutcome.Moved,"Combat bench move");
            Drag(view,"A",view.ScreenPoint(view.Player.GetUnit("D").Placement));Check(view.LastResult.Outcome==PlacementOutcome.Swapped,"Combat bench swap");
            before=view.Player.PlacementRevision;
            Drag(view,"A",view.ScreenPoint(view.Player.GetUnit("B").Placement));Check(view.LastResult.Reason==PlacementRejectReason.InvalidPhase,"Combat bench to board locked");
            Drag(view,"B",view.ScreenPoint(UnitPlacement.OnBench(3)));Check(view.LastResult.Reason==PlacementRejectReason.InvalidPhase,"Combat board to bench locked");
            Drag(view,"B",view.ScreenPoint(UnitPlacement.OnBoard(new BoardPosition(6,3))));Check(view.LastResult.Reason==PlacementRejectReason.InvalidPhase,"Combat board move locked");
            Check(view.Player.PlacementRevision==before,"Combat board rejects preserve revision");
            view.TogglePhase();ExecuteEvents.Execute(view.Token("A").gameObject,data,ExecuteEvents.beginDragHandler);
            view.Player.SetPlacement("B",UnitPlacement.OnBoard(new BoardPosition(0,3)));data.position=view.ScreenPoint(UnitPlacement.OnBench(3));
            ExecuteEvents.Execute(view.Token("A").gameObject,data,ExecuteEvents.endDragHandler);Check(view.LastResult.Reason==PlacementRejectReason.StaleRevision,"Stale drag");
            foreach(var unit in view.Player.Units) Check(view.TokenMatchesState(unit.InstanceId),"Fresh authoritative view");
            view.ResetSandbox();return "Passed: empty move, board swap, bench/board swap, bench swap, cap reject, outside drop, cancel, phase cancellation, combat bench move/swap, combat board reject, stale drag and authoritative return.";
        }
    }
}
