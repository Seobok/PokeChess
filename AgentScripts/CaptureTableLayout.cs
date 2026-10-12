using System;
using System.IO;
using System.Reflection;
using System.Linq;
using UnityEngine;
using UnityEditor;
using PokeChess.Client.UI;
public static class CaptureTableLayout {
    static void Check(bool v,string m){if(!v)throw new Exception(m);}
    public static string Main(){
        EditorApplication.isPaused=true;
        var v=UnityEngine.Object.FindFirstObjectByType<PlacementSandboxView>();v.ResetSynergyDemo();v.SelectUnit("p1-synergy-0");
        var canvas=v.GetComponentInChildren<Canvas>();var mode=canvas.renderMode;var oldCamera=canvas.worldCamera;float plane=canvas.planeDistance;
        var cameraObject=new GameObject("TableLayoutQACamera");var camera=cameraObject.AddComponent<Camera>();
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.035f,.06f,.1f);
        var previous=RenderTexture.active;RenderTexture rt=null;Texture2D tex=null;
        string path="TestResults/TableLayout";Directory.CreateDirectory(path);
        try {
            canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            foreach(var size in new[]{new Vector2Int(1280,720),new Vector2Int(1024,768)}){
                rt=new RenderTexture(size.x,size.y,24);rt.Create();camera.targetTexture=rt;
                canvas.GetComponent<UnityEngine.UI.CanvasScaler>().SendMessage("Update",SendMessageOptions.DontRequireReceiver);
                Canvas.ForceUpdateCanvases();
                foreach(var name in new[]{"PlayerSidebar","ItemIcons","UnitPopup","ShopFrame","Bench_0","Bench_8"}){
                    var rect=v.GetComponentsInChildren<RectTransform>(true).First(t=>t.name==name);
                    var corners=new Vector3[4];rect.GetWorldCorners(corners);
                    foreach(var corner in corners){var p=camera.WorldToScreenPoint(corner);Check(p.x>=-1&&p.x<=size.x+1&&p.y>=-1&&p.y<=size.y+1,name+" outside "+size);}
                }
                camera.Render();RenderTexture.active=rt;tex=new Texture2D(size.x,size.y,TextureFormat.RGBA32,false);
                tex.ReadPixels(new Rect(0,0,size.x,size.y),0,0);tex.Apply();
                File.WriteAllBytes(path+"/selected-"+size.x+"x"+size.y+".png",tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);tex=null;camera.targetTexture=null;rt.Release();UnityEngine.Object.DestroyImmediate(rt);rt=null;
            }
            return "PASS: 1280x720 / 1024x768 canvas bounds and rendered captures.";
        }finally{
            canvas.renderMode=mode;canvas.worldCamera=oldCamera;canvas.planeDistance=plane;RenderTexture.active=previous;
            if(tex!=null)UnityEngine.Object.DestroyImmediate(tex);
            if(rt!=null){camera.targetTexture=null;rt.Release();UnityEngine.Object.DestroyImmediate(rt);}
            UnityEngine.Object.DestroyImmediate(cameraObject);Canvas.ForceUpdateCanvases();
        }
    }
}

