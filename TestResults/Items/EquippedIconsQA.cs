using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using PokeChess.Client.UI;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;
public static class EquippedIconsQA
{
    public static string Run()
    {
        const string output="TestResults/Items/EquippedIconsQA";Directory.CreateDirectory(output);
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var root=new GameObject("EquippedIconsQA");
        try{
            var view=root.AddComponent<PlacementSandboxView>();
            if(view.Match==null)typeof(PlacementSandboxView).GetMethod("Awake",flags).Invoke(view,null);
            view.enabled=false;view.Player.SetProgress(100,0,3);
            var catalog=PrototypeRoster.CreateCatalog();
            string[] species={"geodude","squirtle","charmander"};string[] tools={ItemCatalog.RockyHelmet,ItemCatalog.AssaultVest,ItemCatalog.ChoiceScarf};
            for(int i=0;i<3;i++)SharedPoolSystem.RegisterUnit(view.Match,catalog.CreateUnit("icon-qa-"+i,species[i],"p1",UnitRank.One,0,UnitPlacement.OnBoard(new BoardPosition(1+i*2,3)),new[]{"item:"+tools[i]+":1:p1"}),species[i]);
            view.RefreshFromState();
            if(view.GetComponentsInChildren<UnityEngine.UI.Image>().Count(i=>i.name=="EquippedItemIcon"&&i.sprite!=null)!=3)throw new Exception("Preparation item icons missing.");
            var qa=root.AddComponent<ContentAlphaBuildQA>();typeof(ContentAlphaBuildQA).GetField("view",flags).SetValue(qa,view);typeof(ContentAlphaBuildQA).GetField("output",flags).SetValue(qa,output);
            typeof(ContentAlphaBuildQA).GetMethod("Capture",flags).Invoke(qa,new object[]{"preparation.png"});
            new ItemSystem().Unequip(view.Match,"p1","icon-qa-0",view.Player.ItemRevision);view.RefreshFromState();
            if(view.Token("icon-qa-0").transform.Find("EquippedItemIcon").gameObject.activeSelf)throw new Exception("Unequipped icon remained visible.");
            new ItemSystem().Equip(view.Match,"p1","icon-qa-0",view.Player.ItemInventory[0],view.Player.ItemRevision);
            if(!view.RoundLoop.StartCombat(1))throw new Exception("Combat rejected.");view.RoundLoop.Step();view.RefreshFromState();
            var icons=view.GetComponentsInChildren<UnityEngine.UI.Image>().Where(i=>i.name=="EquippedItemIcon"&&i.transform.parent.name.StartsWith("Fighter_")).ToArray();
            if(icons.Length!=3||icons.Any(i=>i.sprite==null))throw new Exception("Combat icons missing.");
            foreach(var icon in icons){var bar=icon.transform.parent.Find("HealthBar") as RectTransform;if(bar==null||icon.rectTransform.anchoredPosition.y-8<=bar.anchoredPosition.y+3||icon.raycastTarget)throw new Exception("Icon overlaps health bar / intercepts pointer.");}
            typeof(ContentAlphaBuildQA).GetMethod("Capture",flags).Invoke(qa,new object[]{"combat.png"});
            typeof(PlacementSandboxView).GetMethod("RenderEquippedItemIcon",flags).Invoke(view,new object[]{icons[0].transform.parent,new[]{"item:rocky-helmet:1:p1"},false,33f});
            if(icons[0].gameObject.activeSelf)throw new Exception("Dead unit icon visible.");
            File.WriteAllText(output+"/result.json","{\"status\":\"Passed\",\"preparation\":true,\"combat\":true,\"unequip\":true,\"death\":true,\"aboveHealthBar\":true,\"raycastDisabled\":true}");
            return "Passed: preparation/combat icons, above health bars, unequip/death hidden.";
        }finally{UnityEngine.Object.DestroyImmediate(root);}
    }
}
