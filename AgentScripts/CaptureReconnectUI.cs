using System;
using System.IO;
using UnityEngine;
using PokeChess.Client.UI;
public static class CaptureReconnectUI
{
    public static string Main()
    {
        var canvas=UnityEngine.Object.FindFirstObjectByType<RoomFlowView>().GetComponentInChildren<Canvas>();
        var panel=(RectTransform)canvas.transform.Find("ReconnectPanel");if(!panel.gameObject.activeInHierarchy)throw new Exception("Recovery panel inactive");
        var scaler=canvas.GetComponent<UnityEngine.UI.CanvasScaler>();var oldMode=canvas.renderMode;var oldCamera=canvas.worldCamera;var oldScale=canvas.scaleFactor;var oldPlane=canvas.planeDistance;bool oldEnabled=scaler.enabled;
        var obj=new GameObject("RecoveryCaptureCamera");var camera=obj.AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.035f,.06f,.1f);
        var rt=new RenderTexture(1280,720,24);rt.Create();var oldRT=RenderTexture.active;Texture2D image=null;
        try{
            camera.targetTexture=rt;scaler.enabled=false;canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;canvas.scaleFactor=1;Canvas.ForceUpdateCanvases();camera.Render();
            foreach(var rect in panel.GetComponentsInChildren<RectTransform>()){
                var corners=new Vector3[4];rect.GetWorldCorners(corners);foreach(var corner in corners){var p=camera.WorldToScreenPoint(corner);if(p.x<0||p.x>1280||p.y<0||p.y>720)throw new Exception("Clipped "+rect.name);}
            }
            var label=panel.Find("ReconnectStatus").GetComponent<UnityEngine.UI.Text>();if(label.preferredHeight>label.rectTransform.rect.height)throw new Exception("Recovery text overflows");
            RenderTexture.active=rt;image=new Texture2D(1280,720,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes("TestResults/WBS6.1/recovery-ui.png",image.EncodeToPNG());return "Recovery panel bounds and text verified / 1280x720";
        }finally{canvas.renderMode=oldMode;canvas.worldCamera=oldCamera;canvas.planeDistance=oldPlane;canvas.scaleFactor=oldScale;scaler.enabled=oldEnabled;Canvas.ForceUpdateCanvases();RenderTexture.active=oldRT;camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(obj);if(image!=null)UnityEngine.Object.DestroyImmediate(image);rt.Release();UnityEngine.Object.DestroyImmediate(rt);}
    }
}
