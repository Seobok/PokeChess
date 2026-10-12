using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using PokeChess.Client.UI;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;

public static class VerifyEvolutionUI
{
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static Rect Bounds(RectTransform rect,Camera camera)
    {
        var corners=new Vector3[4];rect.GetWorldCorners(corners);
        var lower=camera.WorldToScreenPoint(corners[0]);var upper=camera.WorldToScreenPoint(corners[2]);
        return new Rect(lower.x,lower.y,upper.x-lower.x,upper.y-lower.y);
    }
    public static string Main()
    {
        var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var root=new GameObject("EvolutionUIVerification");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,scene);
        RenderTexture target=null;Texture2D image=null;var previous=RenderTexture.active;
        try
        {
            var view=root.AddComponent<PlacementSandboxView>();
            if(view.Match==null)typeof(PlacementSandboxView).GetMethod("Awake",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,null);
            view.enabled=false;view.ResetHighCostRosterDemo();
            var catalog=PrototypeRoster.CreateCatalog();var trades=new ShopTransactionSystem(catalog);
            foreach(var p in view.Match.Players)foreach(var unit in p.Units.ToArray())trades.Sell(view.Match,p.PlayerId,unit.InstanceId);
            while(view.Match.RoundNumber<5){Check(view.RoundLoop.StartCombat(view.Match.RoundNumber),"Preparation unavailable");Check(view.RoundLoop.NextRound(view.Match.RoundNumber),"Round did not advance");}
            var source=SharedPoolSystem.RegisterUnit(view.Match,catalog.CreateUnit("evolution-ui-unit","bulbasaur","p1",UnitRank.Two,0,UnitPlacement.OnBench(0)),"bulbasaur");
            view.SelectUnit(source.InstanceId);view.RefreshFromState();
            Check(view.EvolutionPointText.Contains("1 EP"),"EP grant not shown");Check(view.EvolveButton.interactable,"Evolution button unavailable");
            Check(view.EvolutionHintText.Contains("이상해풀")&&view.EvolutionHintText.Contains("즉시 진화"),"Preview guidance missing");
            var pointButton=view.InventoryItemButton(0);
            Check(pointButton.gameObject.activeSelf&&pointButton.interactable,"Evolution point is not first inventory item");
            Check(pointButton.transform.Find("Icon").GetComponent<UnityEngine.UI.Image>().sprite.name=="MoonStone","Wrong evolution icon");
            Canvas.ForceUpdateCanvases();
            var start=RectTransformUtility.WorldToScreenPoint(null,pointButton.transform.position);
            view.BeginItemDrag(0,start);Check(view.IsItemDragging,"Evolution drag did not begin");
            view.EndItemDrag(new Vector2(-10000,-10000));Check(view.Player.EvolutionPoints==1&&source.DefinitionId=="bulbasaur","Invalid drop consumed EP");
            var slot=typeof(PlacementSandboxView).GetMethod("FindSlot",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,new object[]{source.Placement});
            var slotRect=(RectTransform)slot.GetType().GetField("Rect").GetValue(slot);
            var destination=RectTransformUtility.WorldToScreenPoint(null,slotRect.position);
            view.BeginItemDrag(0,start);view.DragItem(destination);view.EndItemDrag(destination);
            Check(source.DefinitionId=="ivysaur"&&source.Rank==UnitRank.Two&&source.Placement.BenchSlot==0,"Button did not preserve rank or placement");
            Check(!view.InventoryItemButton(0).interactable&&view.EvolutionPointText.Contains("0 EP"),"Spent EP/icon state incorrect");
            Check(view.InventoryItemButton(0).gameObject.activeSelf,"Zero EP icon disappeared");
            for(int i=0;i<7;i++)view.Player.AddInventoryItem("item:choice-scarf:"+(i+100)+":p1");
            view.RefreshFromState();
            var next=(UnityEngine.UI.Button)typeof(PlacementSandboxView).GetField("itemNext",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(view);
            next.onClick.Invoke();
            Check(view.InventoryItemButton(0).transform.Find("Icon").GetComponent<UnityEngine.UI.Image>().sprite.name=="MoonStone","EP moved on next page");
            view.BeginItemDrag(1,start);Check(view.IsItemDragging,"Ordinary item on next page cannot drag");
            view.EndItemDrag(destination);
            Check(source.ItemInstanceIds.Contains("item:choice-scarf:105:p1"),"Paged ordinary item selected wrong index");
            Check(view.EvolutionHintText.Contains("부족"),"Insufficient EP guidance missing");
            view.Player.SetProgress(100,0,10);view.RerollButton.onClick.Invoke();
            Check(view.Player.Shop.Slots.Where(s=>s.PoolOriginDefinitionId=="bulbasaur").All(s=>s.DefinitionId=="ivysaur"),"Shop family replacement missing");
            // The disabled edit-mode fixture does not receive the normal per-frame animation update.
            typeof(PlacementSandboxView).GetMethod("AnimateRosterSprites",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,null);
            var sprite= root.GetComponentsInChildren<PokemonSpriteView>(true).FirstOrDefault(s=>s.PokemonId=="ivysaur");
            Check(sprite!=null&&sprite.ShowIdle(0)&&sprite.Visible,"Evolution sprite invisible");
            Check(sprite.GetComponent<UnityEngine.UI.Image>().sprite.name.StartsWith("ivysaur_"),"Evolution still uses base-form art");
            var canvas=root.GetComponentInChildren<Canvas>();var scaler=canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
            var camObject=new GameObject("EvolutionQACamera");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camObject,scene);
            camObject.transform.SetParent(root.transform);
            var camera=camObject.AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.035f,.06f,.1f);
            target=new RenderTexture(1280,720,24);target.Create();camera.targetTexture=target;
            scaler.enabled=false;canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;canvas.scaleFactor=1;
            view.ShopRect.anchoredPosition=new Vector2(0,20);
            canvas.transform.Find("EvolutionPreview").gameObject.SetActive(true);
            Canvas.ForceUpdateCanvases();camera.Render();
            var corners=new Vector3[4];((RectTransform)view.EvolveButton.transform).GetWorldCorners(corners);
            foreach(var corner in corners){var point=camera.WorldToScreenPoint(corner);Check(point.x>=0&&point.x<=1280&&point.y>=0&&point.y<=720,"Button outside canvas");}
            var evolutionBounds=Bounds((RectTransform)view.EvolveButton.transform,camera);
            Check(!evolutionBounds.Overlaps(Bounds((RectTransform)view.RoundButton.transform,camera)),"Evolution overlaps round control");
            Check(!evolutionBounds.Overlaps(Bounds((RectTransform)view.SellButton.transform,camera)),"Evolution overlaps sale control");
            RenderTexture.active=target;image=new Texture2D(1280,720,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();
            var cornerPixel=image.GetPixel(0,0);
            Check(image.GetPixels().Count(p=>Mathf.Abs(p.r-cornerPixel.r)+Mathf.Abs(p.g-cornerPixel.g)+Mathf.Abs(p.b-cornerPixel.b)>.05f)>1000,"Canvas did not render into capture");
            Directory.CreateDirectory("TestResults/Evolution");File.WriteAllBytes("TestResults/Evolution/evolution-ui.png",image.EncodeToPNG());
            view.Match.Pool.AssertConservation(view.Match);
            return "Passed: MoonStone first inventory slot, invalid-drop cancellation, drag evolution, zero-point visibility/disabled state, rank/placement preservation, shop replacement, actual Ivysaur sprite, canvas bounds and pool conservation.";
        }
        finally
        {
            RenderTexture.active=previous;if(target!=null){target.Release();UnityEngine.Object.DestroyImmediate(target);}if(image!=null)UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }
}
