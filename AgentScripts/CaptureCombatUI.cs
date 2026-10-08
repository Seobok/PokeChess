using System.Linq;
using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using PokeChess.Client.UI;
public static class CaptureCombatUI
{
    public static string Main()
    {
        var view=UnityEngine.Object.FindFirstObjectByType<RoomFlowView>();
                var state=PokeChess.Network.Session.OnlineConnection.Instance.LocalMatchState;if(state==null)throw new Exception("Missing online owner state");
        typeof(RoomFlowView).GetMethod("Update",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(view,null);
        var canvas=view.GetComponentInChildren<Canvas>();var panel=canvas.transform.Find("RoomPanel");panel.gameObject.SetActive(true);
        var scroll=panel.Find("Browse/RoomList").GetComponent<ScrollRect>();
        if(scroll.content==null||scroll.viewport==null||scroll.horizontal||scroll.viewport.GetComponent<Mask>()==null)throw new Exception("Invalid room list hierarchy");
                var matchPanel=panel.Find("OnlineMatch");if(!matchPanel.gameObject.activeSelf||!matchPanel.Find("Economy").GetComponent<Text>().text.Contains(state.gold+"G"))throw new Exception("Wrong owner economy label");
        if(matchPanel.Find("Shop 0")==null||matchPanel.Find("0,0")==null||matchPanel.Find("B8")==null)throw new Exception("Missing command input cells");
        var scaler=canvas.GetComponent<CanvasScaler>();var obj=new GameObject("RoomQACamera");var camera=obj.AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.035f,.06f,.1f);
        var target=new RenderTexture(1280,720,24);target.Create();var previous=RenderTexture.active;Texture2D image=null;
        try{
            scaler.enabled=false;canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;canvas.scaleFactor=1;camera.targetTexture=target;Canvas.ForceUpdateCanvases();camera.Render();
            var corners=new Vector3[4];panel.GetComponent<RectTransform>().GetWorldCorners(corners);foreach(var p in corners){var s=camera.WorldToScreenPoint(p);if(s.x<0||s.x>1280||s.y<0||s.y>720)throw new Exception("Panel outside screen");}
            var syncCorners=new Vector3[4];var benchCorners=new Vector3[4];matchPanel.Find("Sync state").GetComponent<RectTransform>().GetWorldCorners(syncCorners);matchPanel.Find("B8").GetComponent<RectTransform>().GetWorldCorners(benchCorners);var syncRect=UnityEngine.Rect.MinMaxRect(syncCorners.Min(p=>p.x),syncCorners.Min(p=>p.y),syncCorners.Max(p=>p.x),syncCorners.Max(p=>p.y));var benchRect=UnityEngine.Rect.MinMaxRect(benchCorners.Min(p=>p.x),benchCorners.Min(p=>p.y),benchCorners.Max(p=>p.x),benchCorners.Max(p=>p.y));if(syncRect.Overlaps(benchRect))throw new Exception("Sync button overlaps bench");
            RenderTexture.active=target;image=new Texture2D(1280,720,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();var stage=PokeChess.Network.Session.OnlineConnection.Instance.IsHost?"host":"client";File.WriteAllBytes("TestResults/WBS5.7/"+stage+"-"+state.phase+"-ui.png",image.EncodeToPNG());
        }finally{canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.worldCamera=null;scaler.enabled=true;RenderTexture.active=previous;camera.targetTexture=null;target.Release();UnityEngine.Object.Destroy(target);UnityEngine.Object.Destroy(obj);if(image!=null)UnityEngine.Object.Destroy(image);}
        return "Room panel bounds and scroll hierarchy verified";
    }
}



