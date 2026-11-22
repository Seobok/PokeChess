using System;
using System.IO;
using System.Linq;
using UnityEngine;
using PokeChess.Client.UI;
using PokeChess.Client.Services;

namespace PokeChess.Editor
{
    public static class AccountUiVerification
    {
        public static string Capture(int width, int height, string filename)
        {
            var view = UnityEngine.Object.FindFirstObjectByType<AccountAuthenticationView>();
            if (view == null) throw new Exception("Account view not present");
            var canvas = view.GetComponentInChildren<Canvas>(true);
            var card = canvas.transform.Find("Modal/Card").GetComponent<RectTransform>();
            var issued = canvas.transform.Find("Modal/Card/Body/RecoveryCode/Issued/Input").GetComponent<UnityEngine.UI.InputField>();
            var actualCode = issued.text;
            // Screenshots must never contain the actual code, even for a temporary test account.
            if (actualCode.Length > 0) issued.text = string.Join("-", Enumerable.Repeat("00000000",8));
            var cameraObject = new GameObject("AccountCaptureCamera"); var camera = cameraObject.AddComponent<Camera>();
            camera.backgroundColor = new Color(.025f,.04f,.07f); camera.clearFlags = CameraClearFlags.SolidColor;
            var target = new RenderTexture(width,height,24); target.Create(); var previous = RenderTexture.active;
            var oldMode = canvas.renderMode; var oldCamera = canvas.worldCamera; var oldSize = card.sizeDelta;
            Texture2D image = null;
            try {
                camera.targetTexture = target; canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                Canvas.ForceUpdateCanvases(); card.sizeDelta = new Vector2(Mathf.Min(620,canvas.GetComponent<RectTransform>().rect.width-24),720);
                Canvas.ForceUpdateCanvases();
                var bounds = new Vector3[4]; card.GetWorldCorners(bounds);
                foreach(var field in canvas.GetComponentsInChildren<UnityEngine.UI.InputField>()) {
                    if (field.GetComponent<RectTransform>().rect.width <= 0 || field.GetComponent<RectTransform>().rect.height <= 0) throw new Exception("Zero-sized input");
                }
                camera.Render(); RenderTexture.active = target;
                image = new Texture2D(width,height,TextureFormat.RGBA32,false); image.ReadPixels(new Rect(0,0,width,height),0,0); image.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(filename)); File.WriteAllBytes(filename,image.EncodeToPNG());
                return filename;
            } finally {
                issued.text = actualCode; canvas.renderMode = oldMode; canvas.worldCamera = oldCamera; card.sizeDelta = oldSize;
                RenderTexture.active = previous; camera.targetTexture = null; target.Release();
                UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(cameraObject); if(image!=null) UnityEngine.Object.DestroyImmediate(image);
            }
        }
        public static string BeginSignup(string url)
        {
            var settings = Resources.Load<AccountApiSettings>("AccountApiSettings"); settings.BaseUrl = url; settings.AllowLoopbackHttpInEditor = true;
            var view = UnityEngine.Object.FindFirstObjectByType<AccountAuthenticationView>(); view.SelectScreen(AccountScreen.Register);
            var body = view.GetComponentInChildren<Canvas>().transform.Find("Modal/Card/Body");
            body.Find("Form/Username/Input").GetComponent<UnityEngine.UI.InputField>().text = "ui_" + Guid.NewGuid().ToString("N").Substring(0,16);
            body.Find("Form/Password/Input").GetComponent<UnityEngine.UI.InputField>().text = "ui-test-password-123456";
            body.Find("Form/Confirm/Input").GetComponent<UnityEngine.UI.InputField>().text = "different-password-123";
            body.Find("Submit").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            if (!body.Find("Status").GetComponent<UnityEngine.UI.Text>().text.Contains("do not match")) throw new Exception("Password confirmation missing");
            body.Find("Form/Confirm/Input").GetComponent<UnityEngine.UI.InputField>().text = "ui-test-password-123456";
            body.Find("Submit").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            return "Signup button submitted";
        }
        public static string VerifyCodeScreen()
        {
            var view = UnityEngine.Object.FindFirstObjectByType<AccountAuthenticationView>();
            var body = view.GetComponentInChildren<Canvas>().transform.Find("Modal/Card/Body");
            if (AccountSessionService.Instance.PendingRecoveryCode == null || !body.Find("RecoveryCode").gameObject.activeSelf) throw new Exception("Recovery code not displayed");
            if (body.Find("RecoveryCode/Acknowledge").GetComponent<UnityEngine.UI.Button>().interactable || body.Find("Footer/Close").GetComponent<UnityEngine.UI.Button>().interactable) throw new Exception("Code acknowledgement bypass");
            if (body.Find("Form/Password/Input").GetComponent<UnityEngine.UI.InputField>().text != "") throw new Exception("Password retained after request");
            return "Code shown; confirmation enforced; password cleared";
        }
        public static string AcknowledgeAndLogin()
        {
            var view = UnityEngine.Object.FindFirstObjectByType<AccountAuthenticationView>();
            var body = view.GetComponentInChildren<Canvas>().transform.Find("Modal/Card/Body");
            body.Find("RecoveryCode/Saved").GetComponent<UnityEngine.UI.Toggle>().isOn = true;
            body.Find("RecoveryCode/Acknowledge").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            if (AccountSessionService.Instance.PendingRecoveryCode != null || body.Find("RecoveryCode/Issued/Input").GetComponent<UnityEngine.UI.InputField>().text != "") throw new Exception("Code not cleared");
            body.Find("Form/Password/Input").GetComponent<UnityEngine.UI.InputField>().text = "ui-test-password-123456";
            body.Find("Submit").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            return "Code acknowledged and login button submitted";
        }
        public static string OpenManage()
        {
            if (!AccountSessionService.Instance.IsSignedIn) throw new Exception("UI login failed");
            var view = UnityEngine.Object.FindFirstObjectByType<AccountAuthenticationView>();
            var canvas = view.GetComponentInChildren<Canvas>(true);
            canvas.transform.Find("AccountButton").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            var body = canvas.transform.Find("Modal/Card/Body");
            if (!body.Find("ManageActions").gameObject.activeSelf || body.Find("Form/Password/Input").GetComponent<UnityEngine.UI.InputField>().text != "") throw new Exception("Management screen mismatch");
            return "UI login succeeded; management shown; password cleared";
        }
        public static string LogoutFromManage()
        {
            var view = UnityEngine.Object.FindFirstObjectByType<AccountAuthenticationView>();
            view.GetComponentInChildren<Canvas>().transform.Find("Modal/Card/Body/ManageActions/LogoutRow/Logout").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            return "Logout button submitted";
        }        public static string FinishAndCheckOffline()
        {
            var view = UnityEngine.Object.FindFirstObjectByType<AccountAuthenticationView>();
            var body = view.GetComponentInChildren<Canvas>().transform.Find("Modal/Card/Body");
            body.Find("RecoveryCode/Saved").GetComponent<UnityEngine.UI.Toggle>().isOn = true;
            body.Find("RecoveryCode/Acknowledge").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            if (AccountSessionService.Instance.PendingRecoveryCode != null || body.Find("RecoveryCode/Issued/Input").GetComponent<UnityEngine.UI.InputField>().text != "") throw new Exception("Code not cleared after acknowledgement");
            body.Find("Footer/Offline").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            if (AccountSessionService.Instance.IsSignedIn || OnlineAuthenticationService.Instance.IsReady) throw new Exception("Unexpected authentication");
            if (UnityEngine.Object.FindFirstObjectByType<PlacementSandboxView>() == null) throw new Exception("Local play missing");
            var settings=Resources.Load<AccountApiSettings>("AccountApiSettings"); settings.BaseUrl=""; settings.AllowLoopbackHttpInEditor=false;
            return "Password confirmation, code acknowledgement/clear, and local play without login passed";
        }
    }
}
