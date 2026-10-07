using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using PokeChess.Client.UI;
public static class CaptureRoomUI
{
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
            RenderTexture.active=target;image=new Texture2D(1280,720,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();var stage=panel.Find("Lobby").gameObject.activeSelf?"lobby":"browse";File.WriteAllBytes("TestResults/WBS5.3/"+stage+"-ui.png",image.EncodeToPNG());
        }finally{canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.worldCamera=null;scaler.enabled=true;RenderTexture.active=previous;camera.targetTexture=null;target.Release();UnityEngine.Object.Destroy(target);UnityEngine.Object.Destroy(obj);if(image!=null)UnityEngine.Object.Destroy(image);}
        return "Room panel bounds and scroll hierarchy verified";
    }
}
