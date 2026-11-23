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
        private UnityEngine.UI.Button create,join,refresh,capacity,visibility,leave,copy,readyButton,startButton;
        private readonly List<UnityEngine.UI.Button> rowButtons=new List<UnityEngine.UI.Button>();
        private int maxPlayers=8;
        private bool isPrivate,initialRefresh,hadRoom;
        private string lastLobbyError;
        private ConnectionState previous;
        private PlacementSandboxView matchClient;
        private GameObject recoveryPanel;
        private UnityEngine.UI.Text recoveryText;
        private UnityEngine.UI.Button recoveryRetry;
        private GameObject roomsToggle;
        private void Awake()
        {
            font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Arial Unicode MS", "Arial" }, 18);
            var root = UiPrefab.Instantiate("RoomCanvas", transform);
            foreach (var label in root.GetComponentsInChildren<UnityEngine.UI.Text>(true)) label.font = font;
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
                GUIUtility.systemCopyBuffer = roomCodeLabel.text ?? "";
                feedback.text = "방 코드를 복사했습니다.";
            });
            leave = UiPrefab.Bind(waiting, "Leave room", Leave);
            BindRoomLobbyArt(waiting);
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
            capacity = UiPrefab.Bind(body, "Max players: 8", () => { });
            capacityLabel = capacity.GetComponentInChildren<UnityEngine.UI.Text>();
            visibility = UiPrefab.Bind(body, "Visibility: Public", () => isPrivate = false);
            visibilityLabel = visibility.GetComponentInChildren<UnityEngine.UI.Text>();
            create = UiPrefab.Bind(body, "Create room", Create);
            join = UiPrefab.Bind(body, "Join room", Join);
            BindBrowserArt(body);
        }
        private void Update()
        {
            var s=OnlineConnection.Instance;bool ready=OnlineAuthenticationService.Instance.IsReady;
            bool inRoom=s.Room!=null;bool idle=ready&&!s.IsBusy&&!inRoom;
            bool inMatch=(inRoom||s.IsRecovering)&&s.StartInfo!=null;onlineMatchPanel.SetActive(false);
            recoveryPanel.SetActive(s.IsRecovering);if(s.IsRecovering){recoveryPanel.transform.SetAsLastSibling();recoveryText.text=s.RecoveryFeedback+"\n"+Math.Ceiling(s.ReconnectRemainingSeconds)+"s remaining\nThe match continues. Input resumes after state recovery.";recoveryRetry.interactable=!s.IsBusy&&s.State==ConnectionState.DisconnectedGrace;}
            roomsToggle.SetActive(!inMatch&&!panel.activeSelf);
            if(inMatch){if(matchClient==null)matchClient=FindFirstObjectByType<PlacementSandboxView>();if(matchClient==null)matchClient=new GameObject("MatchClient").AddComponent<PlacementSandboxView>();matchClient.SkillVisuals=SkillVisuals??matchClient.SkillVisuals;matchClient.EnterOnlineMatch(s);panel.SetActive(false);}
            else if(matchClient!=null&&matchClient.IsOnlineMatch){matchClient.ExitOnlineMatch();panel.SetActive(true);}
            var connectionTest=FindFirstObjectByType<OnlineConnectionView>();if(connectionTest!=null)connectionTest.SetVisible(!inMatch&&!panel.activeSelf);
            feedback.gameObject.SetActive(!inMatch);
            browse.SetActive(!inRoom);lobby.SetActive(inRoom&&!inMatch);SetRoomLobbyChrome(inRoom);UpdateOnlineCommandUI(s);
            create.interactable=join.interactable=capacity.interactable=visibility.interactable=roomName.interactable=joinCode.interactable=idle;
            refresh.interactable=idle&&Browser.State!=RoomListState.Loading;
            foreach(var b in rowButtons)b.interactable=idle&&Browser.State==RoomListState.Ready;
            UpdateBrowserArt(idle, ready);
            if(ready&&!initialRefresh&&panel.activeSelf){initialRefresh=true;Refresh();}
            if(s.State!=previous){previous=s.State;feedback.text=Message(s.Error)??(s.IsBusy?"Connecting…":"");}
            if(inRoom)hadRoom=true;
            else if(hadRoom&&!s.IsBusy){hadRoom=false;if(ready)Refresh();}
            if(inRoom)
            {
                RenderRoomLobby(s.Room, OnlineAuthenticationService.Instance.PlayerId, s.IsHost, s.LocalReady,
                    s.State == ConnectionState.Connected, s.LobbyConnectionConfirmed, s.LobbyBusy,
                    s.LobbyState, s.StartBlockedReason);
                if(s.Error!=lastLobbyError){lastLobbyError=s.Error;feedback.text=s.Error??"";}
            }
        }
        private void RenderList()
        {
            if (!ReferenceEquals(renderedRooms, Browser.Rooms))
            {
                var position = content.anchoredPosition;
                foreach(Transform child in content) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
                rowButtons.Clear();
                foreach(var room in Browser.Rooms) rowButtons.Add(CreateBrowserRow(room));
                content.anchoredPosition = resetRoomScroll ? Vector2.zero : position;
                renderedRooms = Browser.Rooms;
                resetRoomScroll = false;
            }
            listStatus.text = Browser.State == RoomListState.Loading ? Browser.IsLoadingMore ? "방 목록 추가 로딩 중…" : "방 목록 갱신 중…"
                : Browser.State == RoomListState.Failed ? Browser.Rooms.Count > 0 ? "추가 목록 로딩 실패 · 새로고침" : "공개 방 목록" : "공개 방 " + Browser.Rooms.Count + "개";
            RenderBrowserEmptyState();
        }
        private static string Message(string error)
        {
            if(error==null)return null;
            if(error=="HostLost")return "Host connection ended. The match is closed.";
            if(error=="ReconnectExpired")return "Reconnect time expired. The previous match cannot be resumed.";
            if(error=="PlayerAlreadyEliminated"||error=="MatchFinished")return "The previous match cannot be resumed ("+error+").";
            if(error=="InvalidRoomName")return "방 이름을 1~32자로 입력해 주세요.";
            if(error=="InvalidRoomCapacity")return "최대 인원을 2~8명으로 선택해 주세요.";
            if(error=="ConnectionLost"||error=="SessionEnded")return "The room connection ended. You can join another room.";
            return "Could not join or create the room ("+error+"). Please retry or refresh the list.";
        }
        private static RectTransform Rect(string name,Transform parent,Vector2 size,Vector2 position){var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(parent,false);r.sizeDelta=size;r.anchoredPosition=position;return r;}
        private UnityEngine.UI.Text Label(string name,Transform parent,string text,Vector2 size,Vector2 position,int fontSize){var t=Rect(name,parent,size,position).gameObject.AddComponent<UnityEngine.UI.Text>();t.font=font;t.fontSize=fontSize;t.supportRichText=false;t.color=Color.white;t.alignment=TextAnchor.MiddleCenter;t.text=text;t.raycastTarget=false;return t;}
        private UnityEngine.UI.Button Button(string name,Transform parent,Vector2 position,Vector2 size,UnityEngine.Events.UnityAction action){var r=Rect(name,parent,size,position);var image=r.gameObject.AddComponent<UnityEngine.UI.Image>();image.color=new Color(.2f,.34f,.48f);var b=r.gameObject.AddComponent<UnityEngine.UI.Button>();b.targetGraphic=image;b.onClick.AddListener(action);Label("Label",r,name,size,Vector2.zero,14);return b;}
        private async void Refresh(){resetRoomScroll=true;roomListScrollRequested=false;await Browser.RefreshAsync();}
        private async void Create(){await OnlineConnection.Instance.CreateRoomAsync(roomName.text,maxPlayers,isPrivate);}
        private async void Join(){await OnlineConnection.Instance.JoinAsync(joinCode.text);}
        private async void Leave(){await OnlineConnection.Instance.LeaveAsync();resetRoomScroll=true;await Browser.RefreshAsync();}
    }
}
