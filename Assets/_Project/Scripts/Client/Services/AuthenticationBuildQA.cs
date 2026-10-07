#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;

namespace PokeChess.Client.Services
{
    // Optional development-player verification, including restart identity checks.
    public sealed class AuthenticationBuildQA : MonoBehaviour
    {
        [Serializable] private sealed class Report { public string state,playerId,error; public bool ready; }
        private string path;
        private bool done;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            int flag = Array.IndexOf(args,"-pokechess-auth-qa-output");
            if (flag < 0 || flag + 1 >= args.Length) return;
            var qa = new GameObject("AuthenticationBuildQA").AddComponent<AuthenticationBuildQA>();
            qa.path = Path.GetFullPath(args[flag+1]);
            Application.runInBackground = true;
        }
        private void Update()
        {
            if (done) return;
            var service = OnlineAuthenticationService.Instance;
            if (service.State != AuthenticationState.Ready && service.State != AuthenticationState.Failed && Time.realtimeSinceStartup < 120) return;
            done = true;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path,JsonUtility.ToJson(new Report { state=service.State.ToString(),playerId=service.PlayerId,error=service.Error,ready=service.IsReady },true));
            Application.Quit(service.IsReady ? 0 : 1);
        }
    }
}
#endif
