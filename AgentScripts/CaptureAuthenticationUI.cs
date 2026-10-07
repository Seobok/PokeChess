using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using PokeChess.Client.UI;
using PokeChess.Client.Services;
public static class CaptureAuthenticationUI
{
    public static string Main()
    {
        var view=UnityEngine.Object.FindFirstObjectByType<AuthenticationStatusView>();
        if(view==null||!OnlineAuthenticationService.Instance.IsReady)throw new Exception("Authentication not ready");
        var canvas=view.GetComponentInChildren<Canvas>();var scaler=canvas.GetComponent<CanvasScaler>();
        var text=view.GetComponentInChildren<Text>();var retry=view.GetComponentInChildren<Button>(true);
        if(!text.text.Contains(OnlineAuthenticationService.Instance.PlayerId)||retry.gameObject.activeSelf)throw new Exception("Ready UI mismatch");
        var cameraObject=new GameObject("AuthenticationQACamera");var camera=cameraObject.AddComponent<Camera>();camera.backgroundColor=new Color(.035f,.06f,.1f);camera.clearFlags=CameraClearFlags.SolidColor;
        var target=new RenderTexture(1280,720,24);target.Create();var previous=RenderTexture.active;Texture2D image=null;
        try {
            scaler.enabled=false;canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;canvas.scaleFactor=1;
            camera.targetTexture=target;Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=target;
            image=new Texture2D(1280,720,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();
            File.WriteAllBytes("TestResults/WBS5.1/auth-ready.png",image.EncodeToPNG());
        } finally { canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.worldCamera=null;scaler.enabled=true;RenderTexture.active=previous;camera.targetTexture=null;target.Release();UnityEngine.Object.Destroy(target);UnityEngine.Object.Destroy(cameraObject);if(image!=null)UnityEngine.Object.Destroy(image); }
        Unity.Services.Authentication.AuthenticationService.Instance.SignOut();
        if(!retry.gameObject.activeSelf||retry.GetComponent<RectTransform>().rect.width<=0)throw new Exception("Retry UI missing");
        var local=UnityEngine.Object.FindFirstObjectByType<PlacementSandboxView>();
        if(local==null)local=new GameObject("AuthenticationLocalQA").AddComponent<PlacementSandboxView>();
        local.StartLocalSimulation(1,true);local.Simulation.Advance(1,60);
        if(local.Simulation.Status!=PokeChess.Core.Match.SimulationStatus.Running||OnlineAuthenticationService.Instance.IsReady)throw new Exception("Local play depends on authentication");
        retry.onClick.Invoke();
        File.WriteAllText("TestResults/WBS5.1/ui-checks.json","{\"readyLabel\":true,\"retryAfterSignOut\":true,\"retryClicked\":true,\"localPlayWithoutAuth\":true}");
        return "Ready screen captured; signout and retry button verified.";
    }
}
