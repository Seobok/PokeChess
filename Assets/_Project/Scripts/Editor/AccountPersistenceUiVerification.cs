using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using PokeChess.Client.Services;
using PokeChess.Client.UI;

namespace PokeChess.Editor
{
    public static class AccountPersistenceUiVerification
    {
        const string Report="TestResults/AccountPersistence/seed-account-id.txt";
        public static string Configure(string url)
        {
            var settings=Resources.Load<AccountApiSettings>("AccountApiSettings");
            settings.BaseUrl=url; settings.AllowLoopbackHttpInEditor=true; EditorUtility.SetDirty(settings); AssetDatabase.SaveAssetIfDirty(settings);
            return "Temporary local API configured for Play Mode restart verification";
        }
        public static string RecordLogin()
        {
            var service=AccountSessionService.Instance;
            if(!service.IsSignedIn || !service.HasSavedSession || service.StorageWarning!=null) throw new Exception("UI login not securely persisted");
            Directory.CreateDirectory(Path.GetDirectoryName(Report)); File.WriteAllText(Report,service.AccountId);
            return "Login persisted in OS vault; only non-secret account ID recorded";
        }
        public static string VerifyRestored()
        {
            var service=AccountSessionService.Instance;
            if(!service.RestoreCompleted || !service.IsSignedIn || service.AccountId!=File.ReadAllText(Report)) throw new Exception("Same account not restored after Play Mode restart");
            var view=UnityEngine.Object.FindFirstObjectByType<AccountAuthenticationView>();
            if(view.GetComponentInChildren<Canvas>(true).transform.Find("Modal").gameObject.activeSelf) throw new Exception("Restored login left account prompt open");
            return "Same account restored after Play Mode restart and server verification; login prompt closed";
        }
        public static string VerifyOffline()
        {
            var service=AccountSessionService.Instance;
            if(!service.RestoreCompleted || service.IsSignedIn || !service.HasSavedSession) throw new Exception("Offline startup state mismatch");
            var body=UnityEngine.Object.FindFirstObjectByType<AccountAuthenticationView>().GetComponentInChildren<Canvas>(true).transform.Find("Modal/Card/Body");
            if(!body.Find("SessionActions").gameObject.activeSelf || !body.Find("Footer/Offline").GetComponent<UnityEngine.UI.Button>().interactable) throw new Exception("Offline restore actions unavailable");
            return "Offline startup kept saved credential without authorizing; retry, forget and local play available";
        }
        public static string ForgetFromUi()
        {
            var view=UnityEngine.Object.FindFirstObjectByType<AccountAuthenticationView>();
            view.GetComponentInChildren<Canvas>(true).transform.Find("Modal/Card/Body/SessionActions/Forget").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            return "Forget saved login button submitted";
        }
        public static string VerifyForgotten()
        {
            var service=AccountSessionService.Instance;
            if(service.IsBusy || service.IsSignedIn || service.HasSavedSession || service.StorageWarning!=null) throw new Exception("Saved login not forgotten");
            return "Forgotten login not restored; no credential retained by service";
        }
        public static string ResetSettings()
        {
            var settings=Resources.Load<AccountApiSettings>("AccountApiSettings"); settings.BaseUrl=""; settings.AllowLoopbackHttpInEditor=false;
            EditorUtility.SetDirty(settings); AssetDatabase.SaveAssetIfDirty(settings); return "Default account API settings restored";
        }
    }
}
