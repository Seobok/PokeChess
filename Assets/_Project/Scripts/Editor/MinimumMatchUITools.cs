using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using PokeChess.Client.UI;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;

namespace PokeChess.Editor
{
    public static class MinimumMatchUITools
    {
        private static void Check(bool condition,string message) { if(!condition) throw new Exception(message); }
        private static void Click(GameObject target,bool dragging=false)
        {
            ExecuteEvents.Execute(target,new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left,eligibleForClick=true,dragging=dragging},ExecuteEvents.pointerClickHandler);
        }
        public static string Verify()
        {
            if(!EditorApplication.isPlaying) throw new Exception("Enter Play mode.");
            var view=UnityEngine.Object.FindFirstObjectByType<PlacementSandboxView>();view.ResetMatchUI();
            Check(view.Player.Gold==5 && view.Player.Level==1 && view.Player.Units.Count==0,"Real starting rules");
            Check(Enumerable.Range(0,5).All(i=>view.ShopButton(i).interactable),"Five cards buyable");
            view.Match.GetPlayer("p2").SetProgress(33333,0,1);view.RefreshFromState();Check(!view.ResourceText.Contains("33333"),"Owner-only resources");
            Click(view.ShopButton(0).gameObject);var owned=view.LastPurchase.Unit;
            Check(view.Player.Gold==4 && view.Player.Shop.Slots[0].IsEmpty,"Purchase resources and slot");
            Click(view.Token(owned.InstanceId).gameObject,true);Check(view.SelectedUnitId==null,"Drag does not select");
            owned.AddItem("ui-sale-item");Click(view.Token(owned.InstanceId).gameObject);Check(view.SelectedUnitId==owned.InstanceId && view.SellButton.interactable,"Unit selection");
            Check(view.DetailText.Contains(owned.DefinitionId),"Unit detail");Click(view.SellButton.gameObject);
            Check(view.Player.Gold==5 && view.Player.Units.Count==0 && view.Player.ItemInventory.Contains("ui-sale-item"),"Sale and item return");
            Check(view.SelectedUnitId==null && !view.Token(owned.InstanceId).gameObject.activeSelf,"Sold token and selection removed");
            Click(view.ShopButton(0).gameObject);Check(view.Player.Gold==5 && view.Player.Units.Count==0,"Empty slot cannot charge again");
            Click(view.XPButton.gameObject);Check(view.Player.Gold==1 && view.Player.Level==3 && view.Player.BoardCapacity==3,"XP and board cap");
            view.Player.SetProgress(100,0,10);view.RefreshFromState();Check(!view.XPButton.interactable && view.ResourceText.Contains("MAX"),"Maximum XP state");Click(view.XPButton.gameObject);Check(view.Player.Gold==100,"Maximum XP cannot charge");
            view.ResetMatchUI();Click(view.LockButton.gameObject);Check(view.Player.Shop.IsLocked,"Lock enabled");
            Click(view.ShopButton(0).gameObject);Check(view.Player.Gold==4,"Locked shop allows buy");Click(view.RerollButton.gameObject);
            Check(view.Player.Gold==2 && view.Player.Shop.IsLocked,"Manual reroll while locked");
            var offers=view.Player.Shop.Slots.Select(s=>s.DefinitionId).ToArray();var rng=view.Player.Shop.RandomState;long revision=view.Player.Shop.Revision;
            view.TogglePhase();Check(!view.ShopExpanded && !view.ShopButton(0).gameObject.activeSelf && !view.XPButton.gameObject.activeSelf,"Combat collapses shop");
            view.BuySlot(1);Check(view.Player.Gold==2 && view.Player.Units.Count==1,"Combat buy rejected");
            view.TogglePhase();Check(view.Match.RoundNumber==2 && view.ShopExpanded,"Next preparation");
            Check(view.Player.Shop.Revision==revision && view.Player.Shop.RandomState==rng && view.Player.Shop.Slots.Select(s=>s.DefinitionId).SequenceEqual(offers),"Locked round refresh preserved");
            view.ResetMatchUI();new ShopSystem().Reroll(view.Match,"p1");revision=view.Player.Shop.Revision;rng=view.Player.Shop.RandomState;
            Click(view.ShopButton(0).gameObject);Check(view.LastPurchase==null && view.Player.Gold==3 && view.Player.Units.Count==0 && view.Player.Shop.Revision==revision && view.Player.Shop.RandomState==rng,"Stale visible card rejected");
            Check(view.ResourceText.Contains("GOLD 3"),"Rejection refreshes authoritative view");
            Click(view.ShopButton(0).gameObject);owned=view.LastPurchase.Unit;Click(view.Token(owned.InstanceId).gameObject);
            view.Player.SetPlacement(owned.InstanceId,UnitPlacement.OnBoard(new BoardPosition(0,0)));int gold=view.Player.Gold;
            Click(view.SellButton.gameObject);Check(view.Player.Units.Count==1 && view.Player.Gold==gold,"Stale selection rejected");
            view.ResetRankDemo();Click(view.Token("B").gameObject);Click(view.ShopButton(0).gameObject);
            Check(view.LastPurchase.RankUps.Count==2 && view.Player.GetUnit("A").Rank==UnitRank.Three && view.SelectedUnitId==null,"Rank feedback and consumed selection");
            Check(view.Player.ItemInventory.Contains("demo-item"),"Rank item return visible");
            view.ResetFullBenchDemo();Check(view.Player.FindFirstEmptyBenchSlot()==null && view.ShopButton(0).interactable,"Full bench merge purchase enabled");
            Click(view.ShopButton(0).gameObject);Check(view.Player.Units.Count==8 && view.Player.GetUnit("A").Rank==UnitRank.Two && view.Player.Gold==19,"Full bench purchase commit");
            view.ResetMatchUI();view.Player.SetProgress(0,0,1);view.RefreshFromState();Check(!view.ShopButton(0).interactable && !view.XPButton.interactable && !view.RerollButton.interactable,"Insufficient gold states");
            rng=view.Player.Shop.RandomState;view.BuySlot(0);Check(view.Player.Units.Count==0 && view.Player.Gold==0 && view.Player.Shop.RandomState==rng,"Rejected buy preserves state");
            view.Player.SetHP(0);view.RefreshFromState();Check(!view.LockButton.interactable && !view.SellButton.interactable,"Elimination input disabled");
            view.Match.Pool.AssertConservation(view.Match);view.ResetMatchUI();
            return "Passed: 5 cards, starting rules, owner-only HUD, buy/sale/item return, click vs drag, XP/cap/MAX, Lock/manual reroll/round refresh, combat collapse, stale card/selection, chain rank-up, full bench merge, insufficient gold, elimination.";
        }
        // Test-only offscreen capture: temporarily route the existing canvas through its camera,
        // then restore overlay rendering. No scene, camera or model state is saved.
        public static string Capture(string path,int width=1280,int height=720)
        {
            if(!EditorApplication.isPlaying) throw new Exception("Enter Play mode.");
            var view=UnityEngine.Object.FindFirstObjectByType<PlacementSandboxView>();
            var canvas=view.GetComponentInChildren<Canvas>();
            var camera=UnityEngine.SceneManagement.SceneManager.GetSceneByPath(PlacementSandboxTools.ScenePath).GetRootGameObjects()
                .SelectMany(o=>o.GetComponentsInChildren<Camera>()).Single(c=>c.name=="PlacementCamera");
            var mode=canvas.renderMode;var worldCamera=canvas.worldCamera;float distance=canvas.planeDistance;var target=camera.targetTexture;
            var active=RenderTexture.active;var render=new RenderTexture(width,height,24);Texture2D texture=null;
            try
            {
                camera.targetTexture=render;canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=10;
                Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=render;
                texture=new Texture2D(width,height,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,width,height),0,0);texture.Apply();
                string full=System.IO.Path.GetFullPath(path);System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full));System.IO.File.WriteAllBytes(full,texture.EncodeToPNG());
                return full;
            }
            finally
            {
                camera.targetTexture=target;canvas.renderMode=mode;canvas.worldCamera=worldCamera;canvas.planeDistance=distance;
                RenderTexture.active=active;Canvas.ForceUpdateCanvases();if(texture!=null) UnityEngine.Object.DestroyImmediate(texture);render.Release();UnityEngine.Object.DestroyImmediate(render);
            }
        }
    }
}
