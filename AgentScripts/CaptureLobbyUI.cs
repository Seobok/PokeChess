using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using PokeChess.Client.UI;
public static class CaptureLobbyUI
{
    public static string WatchSnapshots(){Watch();return "Lobby UI watcher started";}
    private static async void Watch(){var seen=new System.Collections.Generic.HashSet<string>();double end=Time.realtimeSinceStartupAsDouble+150;
        try{while(Time.realtimeSinceStartupAsDouble<end){var service=PokeChess.Network.Session.OnlineConnection.Instance;
            if(service.Room!=null&&service.State==PokeChess.Network.Session.ConnectionState.Connected){var key=(service.IsHost?"host":"client")+service.LobbyState+(service.StartBlockedReason==null);if(!seen.Contains(key)){await System.Threading.Tasks.Task.Delay(250);var current=PokeChess.Network.Session.OnlineConnection.Instance;var currentKey=(current.IsHost?"host":"client")+current.LobbyState+(current.StartBlockedReason==null);if(current.Room!=null&&key==currentKey){Main();seen.Add(key);}}}
            await System.Threading.Tasks.Task.Delay(250);
        }}catch(Exception e){File.WriteAllText("TestResults/WBS5.4/ui-error.txt",e.ToString());}}
    public static string Main()
    {
        var view=UnityEngine.Object.FindFirstObjectByType<RoomFlowView>();
        var canvas=view.GetComponentInChildren<Canvas>();var panel=canvas.transform.Find("RoomPanel");panel.gameObject.SetActive(true);
        var scroll=panel.Find("Browse/RoomList").GetComponent<ScrollRect>();
        if(scroll.content==null||scroll.viewport==null||scroll.horizontal||scroll.viewport.GetComponent<Mask>()==null)throw new Exception("Invalid room list hierarchy");
        var scaler=canvas.GetComponent<CanvasScaler>();var obj=new GameObject("RoomQACamera");var camera=obj.AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.035f,.06f,.1f);
        var target=new RenderTexture(1280,720,24);target.Create();var previous=RenderTexture.active;Texture2D image=null;
        try{
            scaler.enabled=false;canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;canvas.scaleFactor=1;camera.targetTexture=target;Canvas.ForceUpdateCanvases();camera.Render();
            var corners=new Vector3[4];panel.GetComponent<RectTransform>().GetWorldCorners(corners);foreach(var p in corners){var s=camera.WorldToScreenPoint(p);if(s.x<0||s.x>1280||s.y<0||s.y>720)throw new Exception("Panel outside screen");}
            RenderTexture.active=target;image=new Texture2D(1280,720,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();var service=PokeChess.Network.Session.OnlineConnection.Instance;var stage=(service.IsHost?"host-":"client-")+service.LobbyState.ToString().ToLowerInvariant();File.WriteAllBytes("TestResults/WBS5.4/"+stage+"-ui.png",image.EncodeToPNG());
        }finally{canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.worldCamera=null;scaler.enabled=true;RenderTexture.active=previous;camera.targetTexture=null;target.Release();UnityEngine.Object.Destroy(target);UnityEngine.Object.Destroy(obj);if(image!=null)UnityEngine.Object.Destroy(image);}
        return "Room panel bounds and scroll hierarchy verified";
    }
}



