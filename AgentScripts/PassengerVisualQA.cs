using System;
using System.IO;
using System.Linq;
using UnityEngine;
using PokeChess.Client.UI;
using PokeChess.Core.Match;
public static class PassengerVisualQA
{
    public static string Run()
    {
        var root=new GameObject("PassengerVisualQA",typeof(RectTransform),typeof(Canvas));root.layer=30;
        var canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;root.GetComponent<RectTransform>().sizeDelta=new Vector2(640,320);
        var cameraObject=new GameObject("PassengerQACamera",typeof(Camera));var camera=cameraObject.GetComponent<Camera>();
        camera.orthographic=true;camera.orthographicSize=160;camera.transform.position=new Vector3(0,0,-100);camera.cullingMask=1<<30;
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.04f,.06f,.12f);
        var target=new RenderTexture(640,320,24);camera.targetTexture=target;
        try
        {
            var source=new GameObject("Lapras",typeof(RectTransform));source.transform.SetParent(root.transform,false);source.layer=30;
            var boat=source.AddComponent<PokemonSpriteView>();boat.Initialize("lapras");boat.ShowIdle(0);
            var view=SkillVisualBattleView.Get(root.transform,null);
            var frame=new CombatFrame{battleId="passenger-qa",tickRate=30,units=new[]{
                new CombatUnitView{id="boat",definitionId="lapras",hp=1300,maxHP=1300,onBoard=true,column=3,row=2,statusDetails=Array.Empty<CombatStatusView>(),shields=Array.Empty<CombatShieldView>()},
                new CombatUnitView{id="rider",definitionId="pikachu",hp=2000,maxHP=2000,onBoard=false,column=4,row=2,statusDetails=Array.Empty<CombatStatusView>(),shields=Array.Empty<CombatShieldView>()}},
                events=new[]{new CombatEventView{kind="SynergyTile",detail="passenger:BOARD",source="boat",target="rider",tick=0,synergyCell=new CombatCellView{column=4,row=2},synergyDestination=new CombatCellView{column=3,row=2}}}};
            Func<int,int,Vector2> point=(c,r)=>new Vector2((c-3)*80,(2-r)*70);
            view.RenderPlayback(frame,0,point);var transition=view.transform.Find("Passenger_BOARD");
            if(transition==null||!transition.gameObject.activeSelf)throw new Exception("Missing boarding effect.");
            var ghost=transition.GetComponentInChildren<PokemonSpriteView>();if(ghost==null||!ghost.Visible)throw new Exception("Missing visible boarding passenger.");
            view.RenderPlayback(frame,9,point);var fade=ghost.GetComponent<CanvasGroup>();
            if(fade.alpha<=0||fade.alpha>=1||transition.localPosition.x>=80||transition.localPosition.x<=0)throw new Exception("Passenger did not move and fade toward Lapras.");
            foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=30;
            Canvas.ForceUpdateCanvases();camera.Render();var previous=RenderTexture.active;RenderTexture.active=target;
            var texture=new Texture2D(640,320,TextureFormat.RGBA32,false);texture.ReadPixels(new Rect(0,0,640,320),0,0);texture.Apply();
            Directory.CreateDirectory("TestResults/Passenger");File.WriteAllBytes("TestResults/Passenger/boarding.png",texture.EncodeToPNG());RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(texture);
            view.RenderPlayback(frame,18,point);if(transition.gameObject.activeSelf)throw new Exception("Boarded passenger remained visible.");
            frame.events=new[]{new CombatEventView{kind="SynergyTile",detail="passenger:DISEMBARK",source="boat",target="rider",tick=30,synergyCell=new CombatCellView{column=3,row=1}}};
            frame.units[1].onBoard=true;frame.units[1].row=1;frame.units[1].column=3;
            view.RenderPlayback(frame,30,point);if(view.transform.Find("Passenger_DISEMBARK")?.gameObject.activeSelf!=true)throw new Exception("Missing disembark effect.");
            view.RenderPlayback(frame,31,point);if(view.transform.Cast<Transform>().Count(t=>t.name=="Passenger_DISEMBARK")!=1)throw new Exception("Replay duplicated effect.");
            return "Passed: boarding effect, passenger sprite, movement and fade, hidden at 0.6 seconds, disembark effect and event replay deduplication.";
        }
        finally{camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(cameraObject);UnityEngine.Object.DestroyImmediate(target);}
    }
}
