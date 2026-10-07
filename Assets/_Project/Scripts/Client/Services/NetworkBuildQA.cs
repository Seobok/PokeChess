#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;
using PokeChess.Network.Session;
namespace PokeChess.Client.Services
{
    public sealed class NetworkBuildQA : MonoBehaviour
    {
        [Serializable] private sealed class Report { public string status,role,code,error; public int cycles,requests,replies,players; public double rtt; public bool hostLoss; }
        private string role,folder,code;
        private int cycles;
        private bool started,busy,done;
        private double savedAt;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"-pokechess-network-qa");
            if(index<0||index+1>=args.Length)return;
            var qa=new GameObject("NetworkBuildQA").AddComponent<NetworkBuildQA>();qa.role=args[index+1];
            index=Array.IndexOf(args,"-qa-output");qa.folder=index>=0?Path.GetFullPath(args[index+1]):Path.Combine(Application.persistentDataPath,"NetworkQA");
            index=Array.IndexOf(args,"-qa-code");qa.code=index>=0?args[index+1]:null;
            Directory.CreateDirectory(qa.folder);Application.runInBackground=true;
        }
        private void Update()
        {
            if(done)return;
            var auth=OnlineAuthenticationService.Instance;
            if(!started) { if(auth.IsReady){started=true;StartConnection();}else if(auth.State==AuthenticationState.Failed)Finish("Failed",false);return; }
            var service=OnlineConnection.Instance;
            if(Time.realtimeSinceStartup>240){Finish("Timeout",false);return;}
            if(busy)return;
            if(service.State==ConnectionState.Failed){Finish("Failed",false);return;}
            if(role=="client")
            {
                if(service.State==ConnectionState.Connected&&service.RepliesReceived>0)
                {
                    if(cycles==0){cycles=1;Rejoin();return;}
                    if(cycles==1){cycles=2;Save("ConnectedTwice",false);}
                }
                if(cycles==2&&service.State==ConnectionState.Idle){Finish("Completed",true);return;}
            }
            else if(File.Exists(Path.Combine(folder,"stop.txt"))){StopHost();return;}
            if(Time.realtimeSinceStartupAsDouble-savedAt>1){savedAt=Time.realtimeSinceStartupAsDouble;Save("Running",false);}
        }
        private async void StartConnection()
        {
            busy=true;
            try { if(role=="host")await OnlineConnection.Instance.CreateAsync();else await OnlineConnection.Instance.JoinAsync(code); }
            catch(Exception e){Debug.LogError(e.GetType().Name);Finish("Failed",false);}
            finally {busy=false;}
        }
        private async void Rejoin() { busy=true;await OnlineConnection.Instance.LeaveAsync();await OnlineConnection.Instance.JoinAsync(code);busy=false; }
        private async void StopHost() { busy=true;await OnlineConnection.Instance.LeaveAsync();Finish("Completed",false); }
        private void Save(string status,bool hostLoss)
        {
            var s=OnlineConnection.Instance;File.WriteAllText(Path.Combine(folder,"status.json"),JsonUtility.ToJson(new Report{status=status,role=role,code=s.Code,error=s.Error,cycles=cycles,requests=s.RequestsReceived,replies=s.RepliesReceived,players=s.ConnectedPlayers,rtt=s.LastRoundTripMilliseconds,hostLoss=hostLoss},true));
        }
        private void Finish(string status,bool hostLoss){done=true;Save(status,hostLoss);Application.Quit(status=="Completed"?0:1);}
    }
}
#endif
