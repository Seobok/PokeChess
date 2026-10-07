#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEngine;
using PokeChess.Network.Session;
using PokeChess.Core.Match;
namespace PokeChess.Client.Services
{
    // Explicit opt-in file commands for repeatable Editor/Windows integration checks.
    public sealed class RoomBuildQA : MonoBehaviour
    {
        [Serializable] public sealed class Command { public int sequence; public string action,name,id,code,kind,unitId,targetPlayer; public int capacity=2,slot,column,row,bench; public bool isPrivate,ready,locked,toBench,stale,wrongRound,wrongPhase,wrongMatch,badVersion; }
        [Serializable] public sealed class Listing { public string id,name; public int players,capacity; }
        [Serializable] public sealed class Report { public int sequence,players,replies; public string state,error,id,code,listState,lobbyPhase,startBlocked,qaError,commandFeedback; public bool busy,isPrivate,localReady,connectionConfirmed,identityBound,pendingCommand; public string[] members; public bool[] ready; public Listing[] rooms; public MatchStartInfo start; public OwnerMatchState match;public CommandAck ack; }
        private readonly RoomBrowser browser=new RoomBrowser();
        private string folder;
        private int sequence;
        private bool busy;
        private double nextSave;
        private string qaError;
        private MatchCommand lastSent;
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
            busy=true;qaError=null;var s=OnlineConnection.Instance;
            try {
                switch(c.action){
                    case "create":await s.CreateRoomAsync(c.name,c.capacity,c.isPrivate);break;
                    case "id":await s.JoinByIdAsync(c.id);break;
                    case "code":await s.JoinAsync(c.code);break;
                    case "refresh":await browser.RefreshAsync();break;
                    case "leave":await s.LeaveAsync();break;
                    case "ready":await s.SetReadyAsync(c.ready);break;
                    case "start":await s.StartMatchAsync();break;
                    case "startTwice":await System.Threading.Tasks.Task.WhenAll(s.StartMatchAsync(),s.StartMatchAsync());break;
                    case "holdAck":s.QAHoldStartAcknowledgement=c.ready;break;
                    case "dropAck":s.QADropNextAck=true;break;
                    case "game":
                        var command=s.CreateCommand((MatchCommandKind)Enum.Parse(typeof(MatchCommandKind),c.kind));command.slot=c.slot;command.unitId=c.unitId;command.destination=c.toBench?PokeChess.Core.Pokemon.PlacementKind.Bench:PokeChess.Core.Pokemon.PlacementKind.Board;command.column=c.column;command.row=c.row;command.bench=c.bench;command.locked=c.locked;
                        if(c.targetPlayer!=null)command.playerId=c.targetPlayer;if(c.stale)command.playerRevision=-1;if(c.wrongRound)command.round++;if(c.wrongPhase)command.phase=MatchPhase.Result;if(c.wrongMatch)command.matchId="other-match";if(c.badVersion)command.protocol=999;
                        lastSent=command.Copy();await s.SubmitCommandAsync(command);break;
                    case "repeat":await s.SubmitCommandAsync(lastSent.Copy());break;
                    case "repeatChanged":var changed=lastSent.Copy();changed.slot++;await s.SubmitCommandAsync(changed);break;
                    case "retryCommand":await s.RetryPendingCommandAsync();break;
                    case "gameUI":await ClickMatchButton(c.name);break;
                    case "readyUI":await ClickLobbyButton("Ready");break;
                    case "startUI":await ClickLobbyButton("Start");break;
                    case "quit":await s.LeaveAsync();Application.Quit();break;
                    default:throw new ArgumentException("Unknown QA command");
                }
            }catch(Exception e){qaError=e.GetType().Name+": "+e.Message;}finally{busy=false;Save();}
        }
        private static async System.Threading.Tasks.Task ClickLobbyButton(string name)
        {
            var view=FindFirstObjectByType<PokeChess.Client.UI.RoomFlowView>();
            var canvas=view.GetComponentInChildren<Canvas>();canvas.transform.Find("RoomPanel").gameObject.SetActive(true);
            var button=canvas.transform.Find("RoomPanel/Lobby/"+name).GetComponent<UnityEngine.UI.Button>();
            if(!button.interactable||!button.gameObject.activeSelf)throw new InvalidOperationException("Lobby button unavailable: "+name);
            button.onClick.Invoke();while(OnlineConnection.Instance.LobbyBusy)await System.Threading.Tasks.Task.Yield();
        }
        private static async System.Threading.Tasks.Task ClickMatchButton(string name)
        {
            var view=FindFirstObjectByType<PokeChess.Client.UI.RoomFlowView>();var canvas=view.GetComponentInChildren<Canvas>();canvas.transform.Find("RoomPanel").gameObject.SetActive(true);
            var button=canvas.transform.Find("RoomPanel/OnlineMatch/"+name).GetComponent<UnityEngine.UI.Button>();if(!button.interactable||!button.gameObject.activeInHierarchy)throw new InvalidOperationException("Match button unavailable: "+name);
            button.onClick.Invoke();double end=Time.realtimeSinceStartupAsDouble+8;while(OnlineConnection.Instance.PendingCommand!=null&&Time.realtimeSinceStartupAsDouble<end)await System.Threading.Tasks.Task.Yield();
        }
        private void Save(){var s=OnlineConnection.Instance;File.WriteAllText(Path.Combine(folder,"status.json"),JsonUtility.ToJson(new Report{sequence=sequence,busy=busy||s.LobbyBusy,state=s.State.ToString(),error=s.Error,id=s.SessionId,code=s.Code,players=s.Room?.Members.Count??0,replies=s.RepliesReceived,isPrivate=s.Room?.IsPrivate??false,members=s.Room?.Members.Select(m=>m.Label).ToArray(),ready=s.Room?.Members.Select(m=>m.IsReady).ToArray(),localReady=s.LocalReady,connectionConfirmed=s.LobbyConnectionConfirmed,identityBound=s.IdentityBound,lobbyPhase=s.LobbyState.ToString(),startBlocked=s.StartBlockedReason,start=s.StartInfo,qaError=qaError,match=s.LocalMatchState,ack=s.LastCommandAck,pendingCommand=s.PendingCommand!=null,commandFeedback=s.CommandFeedback,listState=browser.State.ToString(),rooms=browser.Rooms.Select(r=>new Listing{id=r.Id,name=r.Name,players=r.Players,capacity=r.Capacity}).ToArray()},true));}
    }
}
#endif
