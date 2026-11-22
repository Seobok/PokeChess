using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using PokeChess.Client.Services;
using PokeChess.Network.Session;

namespace PokeChess.Client.UI
{
    public sealed partial class RoomFlowView : MonoBehaviour
    {
        public PokeChess.Client.Data.SkillVisualCatalog SkillVisuals;
        public RoomBrowser Browser { get; }=new RoomBrowser();
        private GameObject panel,browse,lobby;
        private RectTransform content;
        private Font font;
        private UnityEngine.UI.Text listStatus,feedback,roomTitle,players,visibilityLabel,capacityLabel,lobbyStatus;
        private UnityEngine.UI.InputField roomName,joinCode;
        private UnityEngine.UI.Button create,join,refresh,next,capacity,visibility,leave,copy,readyButton,startButton;
        private readonly List<UnityEngine.UI.Button> rowButtons=new List<UnityEngine.UI.Button>();
        private int maxPlayers=8;
        private bool isPrivate,initialRefresh,hadRoom;
        private string lastRoom,lastLobbyError;
        private ConnectionState previous;
        private PlacementSandboxView matchClient;
        private GameObject recoveryPanel;
        private UnityEngine.UI.Text recoveryText;
        private UnityEngine.UI.Button recoveryRetry;
        private GameObject roomsToggle;
        private void Awake()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var root = UiPrefab.Instantiate("RoomCanvas", transform);
            recoveryPanel = root.Find("ReconnectPanel").gameObject;
            recoveryText = UiPrefab.Get<UnityEngine.UI.Text>(recoveryPanel.transform, "ReconnectStatus");
            recoveryRetry = UiPrefab.Bind(recoveryPanel.transform, "Reconnect now", async () => await OnlineConnection.Instance.ReconnectAsync());
            UiPrefab.Bind(recoveryPanel.transform, "Leave match", async () => await OnlineConnection.Instance.LeaveAsync());
            roomsToggle = UiPrefab.Bind(root, "Online rooms", () => panel.SetActive(!panel.activeSelf)).gameObject;

            var body = root.Find("RoomPanel");
            panel = body.gameObject;
            UiPrefab.Bind(body, "Local play", StartLocalMatch);
            BindBrowser(body.Find("Browse"));
            var waiting = body.Find("Lobby");
            lobby = waiting.gameObject;
            roomTitle = UiPrefab.Get<UnityEngine.UI.Text>(waiting, "RoomTitle");
            players = UiPrefab.Get<UnityEngine.UI.Text>(waiting, "Players");
            lobbyStatus = UiPrefab.Get<UnityEngine.UI.Text>(waiting, "LobbyStatus");
            readyButton = UiPrefab.Bind(waiting, "Ready", async () =>
            {
                var service = OnlineConnection.Instance;
                await service.SetReadyAsync(!service.LocalReady);
            });
            startButton = UiPrefab.Bind(waiting, "Start", async () => await OnlineConnection.Instance.StartMatchAsync());
            copy = UiPrefab.Bind(waiting, "Copy code", () =>
            {
                GUIUtility.systemCopyBuffer = OnlineConnection.Instance.Code ?? "";
                feedback.text = "Room code copied.";
            });
            leave = UiPrefab.Bind(waiting, "Leave room", Leave);
            BindOnlineCommandUI(body.Find("OnlineMatch"));
            feedback = UiPrefab.Get<UnityEngine.UI.Text>(body, "Feedback");
            panel.SetActive(FindFirstObjectByType<PlacementSandboxView>() == null);
        }

        public void StartLocalMatch()
        {
            panel.SetActive(false);
            var view = FindFirstObjectByType<PlacementSandboxView>();
            if (view == null) view = new GameObject("LocalMatch").AddComponent<PlacementSandboxView>();
            view.SkillVisuals = SkillVisuals ?? view.SkillVisuals;
            view.ShowLocalMatch();
        }

        private void OnEnable() => Browser.Changed += RenderList;
        private void OnDisable() => Browser.Changed -= RenderList;

        private void BindBrowser(Transform body)
        {
            browse = body.gameObject;
            content = UiPrefab.Get<UnityEngine.UI.ScrollRect>(body, "RoomList").content;
            listStatus = UiPrefab.Get<UnityEngine.UI.Text>(body, "ListStatus");
            roomName = UiPrefab.Get<UnityEngine.UI.InputField>(body, "RoomName");
            joinCode = UiPrefab.Get<UnityEngine.UI.InputField>(body, "JoinCode");
            refresh = UiPrefab.Bind(body, "Refresh", Refresh);
            next = UiPrefab.Bind(body, "Next page", Next);
            capacity = UiPrefab.Bind(body, "Max players: 8", () =>
            {
                maxPlayers = maxPlayers == 8 ? 2 : maxPlayers + 1;
                capacityLabel.text = "Max players: " + maxPlayers;
            });
            capacityLabel = capacity.GetComponentInChildren<UnityEngine.UI.Text>();
            visibility = UiPrefab.Bind(body, "Visibility: Public", () =>
            {
                isPrivate = !isPrivate;
                visibilityLabel.text = "Visibility: " + (isPrivate ? "Private" : "Public");
            });
            visibilityLabel = visibility.GetComponentInChildren<UnityEngine.UI.Text>();
            create = UiPrefab.Bind(body, "Create room", Create);
            join = UiPrefab.Bind(body, "Join room", Join);
        }
        private void Update()
        {
            var s=OnlineConnection.Instance;bool ready=OnlineAuthenticationService.Instance.IsReady;
            bool inRoom=s.Room!=null;bool idle=ready&&!s.IsBusy&&!inRoom;
            bool inMatch=(inRoom||s.IsRecovering)&&s.StartInfo!=null;onlineMatchPanel.SetActive(false);
            recoveryPanel.SetActive(s.IsRecovering);if(s.IsRecovering){recoveryPanel.transform.SetAsLastSibling();recoveryText.text=s.RecoveryFeedback+"\n"+Math.Ceiling(s.ReconnectRemainingSeconds)+"s remaining\nThe match continues. Input resumes after state recovery.";recoveryRetry.interactable=!s.IsBusy&&s.State==ConnectionState.DisconnectedGrace;}
            roomsToggle.SetActive(!inMatch);
            if(inMatch){if(matchClient==null)matchClient=FindFirstObjectByType<PlacementSandboxView>();if(matchClient==null)matchClient=new GameObject("MatchClient").AddComponent<PlacementSandboxView>();matchClient.SkillVisuals=SkillVisuals??matchClient.SkillVisuals;matchClient.EnterOnlineMatch(s);panel.SetActive(false);}
            else if(matchClient!=null&&matchClient.IsOnlineMatch){matchClient.ExitOnlineMatch();panel.SetActive(true);}
            var connectionTest=FindFirstObjectByType<OnlineConnectionView>();if(connectionTest!=null)connectionTest.SetVisible(!inMatch);
            feedback.gameObject.SetActive(!inMatch);
            browse.SetActive(!inRoom);lobby.SetActive(inRoom&&!inMatch);UpdateOnlineCommandUI(s);
            create.interactable=join.interactable=capacity.interactable=visibility.interactable=roomName.interactable=joinCode.interactable=idle;
            refresh.interactable=idle&&Browser.State!=RoomListState.Loading;next.interactable=refresh.interactable&&!string.IsNullOrEmpty(Browser.NextPage);
            foreach(var b in rowButtons)b.interactable=idle&&Browser.State==RoomListState.Ready;
            leave.interactable=inRoom&&!s.LobbyBusy;copy.interactable=inRoom&&!s.LobbyBusy;
            readyButton.gameObject.SetActive(inRoom&&!s.IsHost&&s.LobbyState==LobbyPhase.Waiting);
            startButton.gameObject.SetActive(inRoom&&s.IsHost&&s.LobbyState!=LobbyPhase.Started);
            readyButton.interactable=s.State==ConnectionState.Connected&&s.LobbyConnectionConfirmed&&!s.LobbyBusy;
            readyButton.GetComponentInChildren<UnityEngine.UI.Text>().text=s.LobbyBusy?"Saving…":s.LocalReady?"Cancel ready":"Ready";
            startButton.interactable=s.StartBlockedReason==null;
            if(ready&&!initialRefresh&&panel.activeSelf){initialRefresh=true;Refresh();}
            if(s.State!=previous){previous=s.State;feedback.text=Message(s.Error)??(s.IsBusy?"Connecting…":"");}
            if(inRoom)hadRoom=true;
            else if(hadRoom&&!s.IsBusy){hadRoom=false;if(ready)Refresh();}
            if(inRoom)
            {
                var room=s.Room;var signature=room.Name+room.Code+s.LobbyState+s.StartInfo?.matchId+string.Join("|",room.Members.Select(p=>p.Id+":"+p.IsHost+":"+p.IsReady+":"+p.IsConnected));
                if(signature!=lastRoom){lastRoom=signature;roomTitle.text=room.Name+"\n"+(room.IsPrivate?"Private":"Public")+" / "+room.Members.Count+" / "+room.Capacity+" players / Code: "+room.Code;
                    players.text=s.StartInfo==null?string.Join("\n",room.Members.Select(p=>p.Label+(p.Id==OnlineAuthenticationService.Instance.PlayerId?" (You)":"")+" / "+(!p.IsConnected?"Connecting":p.IsHost?"Connected":p.IsReady?"Ready":"Not ready"))):"MATCH START CONFIRMED\n"+s.StartInfo.matchId+"\n"+s.StartInfo.players.Length+" players";}
                lobbyStatus.text=s.LobbyState==LobbyPhase.Starting?"Confirming match start with all players…":s.LobbyState==LobbyPhase.Started?"All players received the same match start.":s.IsHost?s.StartBlockedReason??"All players are ready. You can start.":s.LobbyBusy?"Saving lobby state…":"Ready when you are prepared to play.";
                if(s.Error!=lastLobbyError){lastLobbyError=s.Error;feedback.text=s.Error??"";}
            }
            else lastRoom=null;
        }
        private void RenderList()
        {
            foreach(Transform child in content)Destroy(child.gameObject);rowButtons.Clear();
            foreach(var room in Browser.Rooms)
            {
                var id=room.Id;var button=Button(room.Name+"   "+room.Players+" / "+room.Capacity+"   Join",content,Vector2.zero,new Vector2(440,44),async()=>{await OnlineConnection.Instance.JoinByIdAsync(id);});
                button.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight=44;rowButtons.Add(button);
            }
            listStatus.text=Browser.State==RoomListState.Loading?"Loading rooms…":Browser.State==RoomListState.Failed?"Could not load rooms. Refresh to retry.":Browser.Rooms.Count==0?"No available public rooms.":Browser.Rooms.Count+" rooms available";
        }
        private static string Message(string error)
        {
            if(error==null)return null;
            if(error=="HostLost")return "Host connection ended. The match is closed.";
            if(error=="ReconnectExpired")return "Reconnect time expired. The previous match cannot be resumed.";
            if(error=="PlayerAlreadyEliminated"||error=="MatchFinished")return "The previous match cannot be resumed ("+error+").";
            if(error=="InvalidRoomName")return "Enter a room name (1–32 characters).";
            if(error=="InvalidRoomCapacity")return "Choose 2–8 players.";
            if(error=="ConnectionLost"||error=="SessionEnded")return "The room connection ended. You can join another room.";
            return "Could not join or create the room ("+error+"). Please retry or refresh the list.";
        }
        private static RectTransform Rect(string name,Transform parent,Vector2 size,Vector2 position){var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(parent,false);r.sizeDelta=size;r.anchoredPosition=position;return r;}
        private UnityEngine.UI.Text Label(string name,Transform parent,string text,Vector2 size,Vector2 position,int fontSize){var t=Rect(name,parent,size,position).gameObject.AddComponent<UnityEngine.UI.Text>();t.font=font;t.fontSize=fontSize;t.supportRichText=false;t.color=Color.white;t.alignment=TextAnchor.MiddleCenter;t.text=text;t.raycastTarget=false;return t;}
        private UnityEngine.UI.Button Button(string name,Transform parent,Vector2 position,Vector2 size,UnityEngine.Events.UnityAction action){var r=Rect(name,parent,size,position);var image=r.gameObject.AddComponent<UnityEngine.UI.Image>();image.color=new Color(.2f,.34f,.48f);var b=r.gameObject.AddComponent<UnityEngine.UI.Button>();b.targetGraphic=image;b.onClick.AddListener(action);Label("Label",r,name,size,Vector2.zero,14);return b;}
        private async void Refresh(){await Browser.RefreshAsync();}
        private async void Next(){await Browser.RefreshAsync(true);}
        private async void Create(){await OnlineConnection.Instance.CreateRoomAsync(roomName.text,maxPlayers,isPrivate);}
        private async void Join(){await OnlineConnection.Instance.JoinAsync(joinCode.text);}
        private async void Leave(){await OnlineConnection.Instance.LeaveAsync();await Browser.RefreshAsync();}
    }
}
