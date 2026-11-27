using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;
using PokeChess.Client.UI;
using PokeChess.Core.Pokemon;
using PokeChess.Core.Battle;
public static class TextureSidesQA
{
    public static string Run()
    {
        var go=new GameObject("TextureSidesQA");go.SetActive(false);
        try
        {
            var view=go.AddComponent<PlacementSandboxView>();var type=typeof(PlacementSandboxView);
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var definitions=new[]{"porygon","other"}.Select(id=>new PokemonDefinition(id,id,1,
                new PokemonStats(1000,100,100,.5f,0,0,1,2,100,0,.2f),"test","test",traitIds:new[]{SynergyTraits.Guardian})).ToArray();
            var catalog=new PokemonCatalog(definitions);type.GetField("catalog",flags).SetValue(view,catalog);
            var slots=(IList)type.GetField("slots",flags).GetValue(view);var slotType=type.GetNestedType("Slot",BindingFlags.NonPublic);
            for(int row=0;row<4;row++)for(int col=0;col<7;col++)
            {
                var rect=new GameObject("Slot",typeof(RectTransform)).GetComponent<RectTransform>();rect.SetParent(go.transform,false);rect.sizeDelta=new Vector2(60,70);
                var slot=Activator.CreateInstance(slotType,true);slotType.GetField("Placement").SetValue(slot,UnitPlacement.OnBoard(new BoardPosition(col,row)));slotType.GetField("Rect").SetValue(slot,rect);slots.Add(slot);
            }
            var roster=definitions.Select((d,i)=>catalog.CreateUnit(d.Id,d.Id,"p1",UnitRank.One,0,
                UnitPlacement.OnBoard(i==0?new BoardPosition(3,2):new BoardPosition(0,0)))).ToArray();
            var refresh=type.GetMethod("RefreshGuardianTiles",flags);refresh.Invoke(view,new object[]{roster});type.GetMethod("AnimateGuardianTiles",flags).Invoke(view,null);
            var markers=go.GetComponentsInChildren<PlacementHexGraphic>(true).Where(g=>g.name=="TextureSideTile").ToArray();
            if(markers.Length!=2||markers.Any(g=>!g.gameObject.activeSelf||g.raycastTarget||g.color.a<=0))throw new Exception("Expected two visible, non-interactive floor tiles including empty positions and clipped board edge.");
            if(markers.Any(g=>g.transform.Find("Outline")==null))throw new Exception("Missing floor outline.");
            refresh.Invoke(view,new object[]{Array.Empty<UnitInstance>()});
            if(markers.Any(g=>g.gameObject.activeSelf))throw new Exception("Preparation highlights were not cleared.");
            refresh.Invoke(view,new object[]{roster});
            if(go.GetComponentsInChildren<PlacementHexGraphic>(true).Count(g=>g.name=="TextureSideTile")!=2)throw new Exception("Duplicate floor highlights.");
            return "Passed: both empty texture side tiles, visible floor fill and outline, no raycast interception, clearing and reusing markers.";
        }
        finally{UnityEngine.Object.DestroyImmediate(go);}
    }
}
