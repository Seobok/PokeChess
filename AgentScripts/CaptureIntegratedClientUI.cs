using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using PokeChess.Client.UI;
public static class CaptureIntegratedClientUI {
    public static string Main() {
        var v=UnityEngine.Object.FindFirstObjectByType<PlacementSandboxView>();if(!v.IsOnlineMatch)throw new Exception("Online client missing");var connection=PokeChess.Network.Session.OnlineConnection.Instance;int width=1280,height=720;string output="TestResults/OnlineClientIntegration/"+(connection.IsHost?"host":"client")+"-"+connection.PublicState.phase+(connection.PublicState.players.Length>2?"-4-player":"")+"-ui.png";
var canvas=v.GetComponentInChildren<Canvas>();var scaler=canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
        var oldMode=canvas.renderMode;var oldCamera=canvas.worldCamera;var oldScale=canvas.scaleFactor;var oldPlane=canvas.planeDistance;
        bool oldEnabled=scaler.enabled;var obj=new GameObject("HudCaptureCamera");
        var camera=obj.AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.035f,.06f,.1f);
        var rt=new RenderTexture(width,height,24);rt.Create();var oldRT=RenderTexture.active;Texture2D image=null;
        try {
            camera.targetTexture=rt;scaler.enabled=false;
            canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            canvas.scaleFactor=Mathf.Min(width/1280f,height/720f);Canvas.ForceUpdateCanvases();camera.Render();
            var corners=new Vector3[4];int profiles=0,cards=0;
            foreach(var rect in canvas.GetComponentsInChildren<RectTransform>()) {
                if(rect.name.StartsWith("ShopCard_")&&!v.ShopExpanded)continue;if(!rect.gameObject.activeInHierarchy||!(rect.name.StartsWith("Profile_")||rect.name.StartsWith("ShopCard_")||rect.name=="UnitDetail"||rect.name=="UnitDetailText"||rect.name=="RoundResult"||rect.name=="EliminationNotice"||rect.name=="MatchFinalResult"||rect.name.StartsWith("FinalRow_")))continue;
                rect.GetWorldCorners(corners);foreach(var corner in corners) {
                    var p=camera.WorldToScreenPoint(corner);
                    if(p.x<0||p.x>width||p.y<0||p.y>height)throw new Exception("Clipped UI "+rect.name);
                }if(rect.name.StartsWith("Profile_"))profiles++;else if(rect.name.StartsWith("ShopCard_"))cards++;
            }
            RenderTexture.active=rt;image=new Texture2D(width,height,TextureFormat.RGBA32,false);
            image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllBytes(output,image.EncodeToPNG());
            return width+"x"+height+" rendered, "+profiles+" profiles / "+cards+" shop cards inside viewport: "+output;
        } finally {
            canvas.renderMode=oldMode;canvas.worldCamera=oldCamera;canvas.planeDistance=oldPlane;
            canvas.scaleFactor=oldScale;scaler.enabled=oldEnabled;Canvas.ForceUpdateCanvases();
            RenderTexture.active=oldRT;camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(obj);
            if(image!=null)UnityEngine.Object.DestroyImmediate(image);rt.Release();UnityEngine.Object.DestroyImmediate(rt);
        }
    }
}

