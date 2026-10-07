#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEngine;
using PokeChess.Network.Session;
namespace PokeChess.Client.Services
{
    // Explicit opt-in file commands for repeatable Editor/Windows integration checks.
    public sealed class RoomBuildQA : MonoBehaviour
    {
        [Serializable] public sealed class Command { public int sequence; public string action,name,id,code; public int capacity=2; public bool isPrivate; }
        [Serializable] public sealed class Listing { public string id,name; public int players,capacity; }
        [Serializable] public sealed class Report { public int sequence,players,replies; public string state,error,id,code,listState; public bool busy,isPrivate; public string[] members; public Listing[] rooms; }
        private readonly RoomBrowser browser=new RoomBrowser();
        private string folder;
        private int sequence;
        private bool busy;
        private double nextSave;
        public void Configure(string output){folder=Path.GetFullPath(output);Directory.CreateDirectory(folder);Application.runInBackground=true;}
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot(){var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-pokechess-room-qa");if(i>=0&&i+1<args.Length)new GameObject("RoomBuildQA").AddComponent<RoomBuildQA>().Configure(args[i+1]);}
        private void Update()
        {
            if(folder==null||!OnlineAuthenticationService.Instance.IsReady)return;
            if(!busy&&File.Exists(Path.Combine(folder,"command.json"))) {
                try{var c=JsonUtility.FromJson<Command>(File.ReadAllText(Path.Combine(folder,"command.json")));if(c!=null&&c.sequence>sequence){sequence=c.sequence;Execute(c);}}
                catch(IOException){} // Writer may be replacing the command this frame.
                catch(ArgumentException){} // Ignore an incomplete command until the next frame.
            }
            if(Time.realtimeSinceStartupAsDouble>nextSave){nextSave=Time.realtimeSinceStartupAsDouble+.5;Save();}
        }
        private async void Execute(Command c)
        {
            busy=true;var s=OnlineConnection.Instance;
            try {
                switch(c.action){
                    case "create":await s.CreateRoomAsync(c.name,c.capacity,c.isPrivate);break;
                    case "id":await s.JoinByIdAsync(c.id);break;
                    case "code":await s.JoinAsync(c.code);break;
                    case "refresh":await browser.RefreshAsync();break;
                    case "leave":await s.LeaveAsync();break;
                    case "quit":await s.LeaveAsync();Application.Quit();break;
                    default:throw new ArgumentException("Unknown QA command");
                }
            }finally{busy=false;Save();}
        }
        private void Save(){var s=OnlineConnection.Instance;File.WriteAllText(Path.Combine(folder,"status.json"),JsonUtility.ToJson(new Report{sequence=sequence,busy=busy||s.IsBusy,state=s.State.ToString(),error=s.Error,id=s.SessionId,code=s.Code,players=s.Room?.Members.Count??0,replies=s.RepliesReceived,isPrivate=s.Room?.IsPrivate??false,members=s.Room?.Members.Select(m=>m.Label).ToArray(),listState=browser.State.ToString(),rooms=browser.Rooms.Select(r=>new Listing{id=r.Id,name=r.Name,players=r.Players,capacity=r.Capacity}).ToArray()},true));}
    }
}
#endif
