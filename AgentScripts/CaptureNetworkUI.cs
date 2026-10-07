using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using PokeChess.Client.UI;
using PokeChess.Network.Session;
public static class CaptureNetworkUI
{
    public static string Main()
    {
        var view=UnityEngine.Object.FindFirstObjectByType<OnlineConnectionView>();if(view==null)throw new Exception("Missing online UI");
        var canvas=view.GetComponentInChildren<Canvas>();var panel=canvas.transform.Find("ConnectionPanel");panel.gameObject.SetActive(true);
        var status=panel.Find("Status").GetComponent<Text>();if(!status.text.Contains(OnlineConnection.Instance.State.ToString()))throw new Exception("Wrong state label");
        var scaler=canvas.GetComponent<CanvasScaler>();var obj=new GameObject("NetworkQACamera");var camera=obj.AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.035f,.06f,.1f);
        var target=new RenderTexture(1280,720,24);target.Create();var previous=RenderTexture.active;Texture2D image=null;
        try {
            scaler.enabled=false;canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;canvas.scaleFactor=1;camera.targetTexture=target;Canvas.ForceUpdateCanvases();camera.Render();
            var corners=new Vector3[4];panel.GetComponent<RectTransform>().GetWorldCorners(corners);foreach(var p in corners){var s=camera.WorldToScreenPoint(p);if(s.x<0||s.x>1280||s.y<0||s.y>720)throw new Exception("Panel outside screen");}
            RenderTexture.active=target;image=new Texture2D(1280,720,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes("TestResults/WBS5.2/network-ui.png",image.EncodeToPNG());
        } finally {canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.worldCamera=null;scaler.enabled=true;RenderTexture.active=previous;camera.targetTexture=null;target.Release();UnityEngine.Object.Destroy(target);UnityEngine.Object.Destroy(obj);if(image!=null)UnityEngine.Object.Destroy(image);}
        return "Network UI state and screen bounds verified";
    }
}
