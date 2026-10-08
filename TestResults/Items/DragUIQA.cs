using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using PokeChess.Client.UI;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;

public static class DragUIQA
{
    public static string Run()
    {
        const string output="TestResults/Items/DragUIQA";
        Directory.CreateDirectory(output);
        var library=Resources.Load<ItemIconLibrary>("ItemIcons");
        if(library==null||library.icons.Length!=8)throw new Exception("Missing icon library.");
        foreach(var icon in library.icons)if(icon==null)throw new Exception("Missing sprite reference.");
        var root=new GameObject("ItemDragQA");
        try{
            var view=root.AddComponent<PlacementSandboxView>();
            if(view.Match==null)typeof(PlacementSandboxView).GetMethod("Awake",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,null);
            var qa=root.AddComponent<ContentAlphaBuildQA>();
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            typeof(ContentAlphaBuildQA).GetField("view",flags).SetValue(qa,view);
            typeof(ContentAlphaBuildQA).GetField("output",flags).SetValue(qa,output);
            typeof(ContentAlphaBuildQA).GetMethod("CheckItems",flags).Invoke(qa,null);
            view.enabled=false;
            var player=view.Player;
            SharedPoolSystem.RegisterUnit(view.Match,PrototypeRoster.CreateCatalog().CreateUnit("item-bench-qa","charmander","p1",UnitRank.One,0,UnitPlacement.OnBench(0)),"charmander");
            view.RefreshFromState();
            string helmet="item:"+ItemCatalog.RockyHelmet+":99:p1";
            view.BeginItemDrag(1,new Vector2(50,450));view.EndItemDrag(view.ScreenPoint(UnitPlacement.OnBench(0)));
            if(!player.GetUnit("item-bench-qa").ItemInstanceIds.Contains(helmet))throw new Exception("Bench drop failed.");
            view.BeginItemDrag(0,new Vector2(50,450));view.EndItemDrag(view.ScreenPoint(UnitPlacement.OnBench(0)));
            if(!player.ItemInventory.Contains(helmet)||player.GetUnit("item-bench-qa").ItemInstanceIds.Count!=1)throw new Exception("Swap did not return old tool.");
            view.BeginItemDrag(0,new Vector2(50,450));new ItemSystem().Unequip(view.Match,"p1","item-bench-qa",player.ItemRevision);view.EndItemDrag(view.ScreenPoint(UnitPlacement.OnBench(0)));
            if(!player.ItemInventory.Contains(helmet)||view.IsItemDragging)throw new Exception("Stale inventory drop was accepted.");
            foreach(var definition in ItemCatalog.All)player.AddInventoryItem("item:"+definition.Id+":102:p1");
            view.RefreshFromState();
            typeof(ContentAlphaBuildQA).GetMethod("Capture",flags).Invoke(qa,new object[]{"item-icons.png"});
            view.GetComponentsInChildren<UnityEngine.UI.Button>(true).Single(b=>b.name==">").onClick.Invoke();
            if(!view.InventoryItemButton(0).gameObject.activeSelf||view.InventoryItemButton(0).transform.Find("Icon").GetComponent<UnityEngine.UI.Image>().sprite==null)throw new Exception("Icon pagination failed.");
            view.Match.TransitionTo(MatchPhase.Combat);view.RefreshFromState();view.BeginItemDrag(0,new Vector2(50,450));
            if(view.IsItemDragging)throw new Exception("Combat item drag allowed.");
            File.WriteAllText(output+"/result.json","{\"status\":\"Passed\",\"icons\":8,\"reward\":true,\"invalidDrop\":true,\"boardDrop\":true,\"benchDrop\":true,\"swap\":true,\"staleDrop\":true,\"combatLock\":true,\"pagination\":true,\"return\":true}");
            return "Passed: eight sprites, reward, invalid drop, drag equip and return.";
        }
        finally{UnityEngine.Object.DestroyImmediate(root);}
    }
}

