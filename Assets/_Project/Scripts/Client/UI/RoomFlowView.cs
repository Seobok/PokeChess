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
        private void Awake()
        {
            font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var root=Rect("RoomCanvas",transform,new Vector2(1280,720),Vector2.zero);
            var canvas=root.gameObject.AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=30;
            var scaler=root.gameObject.AddComponent<UnityEngine.UI.CanvasScaler>();scaler.uiScaleMode=UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1280,720);
            root.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            Button("Online rooms",root,new Vector2(420,-335),new Vector2(135,30),()=>panel.SetActive(!panel.activeSelf));
            var body=Rect("RoomPanel",root,new Vector2(960,570),Vector2.zero);panel=body.gameObject;
            body.gameObject.AddComponent<UnityEngine.UI.Image>().color=new Color(.055f,.09f,.145f,.99f);
            Label("Title",body,"POKECHESS / ONLINE ROOMS",new Vector2(640,35),new Vector2(-90,245),22);
            Button("Local play",body,new Vector2(400,245),new Vector2(125,30),()=>{
                panel.SetActive(false);if(FindFirstObjectByType<PlacementSandboxView>()==null)new GameObject("LocalMatch").AddComponent<PlacementSandboxView>();
            });
            var browser=Rect("Browse",body,body.sizeDelta,Vector2.zero);browse=browser.gameObject;
            BuildBrowser(browser);
            var waiting=Rect("Lobby",body,body.sizeDelta,Vector2.zero);lobby=waiting.gameObject;
            roomTitle=Label("RoomTitle",waiting,"",new Vector2(820,90),new Vector2(0,135),20);
            players=Label("Players",waiting,"",new Vector2(820,210),new Vector2(0,-15),18);
            lobbyStatus=Label("LobbyStatus",waiting,"",new Vector2(840,40),new Vector2(0,-150),14);
            readyButton=Button("Ready",waiting,new Vector2(-200,-205),new Vector2(150,34),async()=>{var s=OnlineConnection.Instance;await s.SetReadyAsync(!s.LocalReady);});
            startButton=Button("Start",waiting,new Vector2(-200,-205),new Vector2(150,34),async()=>{await OnlineConnection.Instance.StartMatchAsync();});
            copy=Button("Copy code",waiting,new Vector2(0,-205),new Vector2(150,34),()=>{GUIUtility.systemCopyBuffer=OnlineConnection.Instance.Code??"";feedback.text="Room code copied.";});
            leave=Button("Leave room",waiting,new Vector2(200,-205),new Vector2(150,34),Leave);
            BuildOnlineCommandUI(body);
            feedback=Label("Feedback",body,"",new Vector2(880,38),new Vector2(0,-248),13);
            lobby.SetActive(false);panel.SetActive(FindFirstObjectByType<PlacementSandboxView>()==null);
        }
        private void OnEnable(){Browser.Changed+=RenderList;}
        private void OnDisable(){Browser.Changed-=RenderList;}
        private void BuildBrowser(RectTransform body)
        {
            Label("ListTitle",body,"PUBLIC ROOMS",new Vector2(440,30),new Vector2(-225,195),17);
            refresh=Button("Refresh",body,new Vector2(-300,158),new Vector2(140,30),Refresh);
            next=Button("Next page",body,new Vector2(-140,158),new Vector2(140,30),Next);
            var scroll=Rect("RoomList",body,new Vector2(460,305),new Vector2(-225,-17));
            var scrollRect=scroll.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();scrollRect.horizontal=false;
            var viewport=Rect("Viewport",scroll,scroll.sizeDelta,Vector2.zero);viewport.gameObject.AddComponent<UnityEngine.UI.Image>().color=new Color(.08f,.14f,.21f);viewport.gameObject.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic=true;
            content=Rect("Content",viewport,new Vector2(460,0),Vector2.zero);content.anchorMin=new Vector2(0,1);content.anchorMax=new Vector2(1,1);content.pivot=new Vector2(.5f,1);content.sizeDelta=Vector2.zero;
            var layout=content.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();layout.spacing=6;layout.padding=new RectOffset(8,8,8,8);layout.childControlHeight=true;layout.childControlWidth=true;layout.childForceExpandHeight=false;
            var fitter=content.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();fitter.verticalFit=UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            scrollRect.viewport=viewport;scrollRect.content=content;
            listStatus=Label("ListStatus",body,"Waiting for authentication",new Vector2(450,44),new Vector2(-225,-204),13);
            Label("CreateTitle",body,"CREATE ROOM",new Vector2(370,30),new Vector2(255,195),17);
            roomName=Input("RoomName",body,"Room name",new Vector2(255,150));roomName.text="PokeChess Room";roomName.characterLimit=32;
            capacity=Button("Max players: 8",body,new Vector2(255,100),new Vector2(320,32),()=>{maxPlayers=maxPlayers==8?2:maxPlayers+1;capacityLabel.text="Max players: "+maxPlayers;});capacityLabel=capacity.GetComponentInChildren<UnityEngine.UI.Text>();
            visibility=Button("Visibility: Public",body,new Vector2(255,55),new Vector2(320,32),()=>{isPrivate=!isPrivate;visibilityLabel.text="Visibility: "+(isPrivate?"Private":"Public");});visibilityLabel=visibility.GetComponentInChildren<UnityEngine.UI.Text>();
            create=Button("Create room",body,new Vector2(255,10),new Vector2(320,34),Create);
            Label("JoinTitle",body,"JOIN BY CODE",new Vector2(370,30),new Vector2(255,-65),17);
            joinCode=Input("JoinCode",body,"Enter room code",new Vector2(255,-110));joinCode.characterLimit=32;
            join=Button("Join room",body,new Vector2(255,-155),new Vector2(320,34),Join);
        }
        private void Update()
        {
            var s=OnlineConnection.Instance;bool ready=OnlineAuthenticationService.Instance.IsReady;
            bool inRoom=s.Room!=null;bool idle=ready&&!s.IsBusy&&!inRoom;
            bool inMatch=inRoom&&s.StartInfo!=null;onlineMatchPanel.SetActive(inMatch);
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
            if(error=="InvalidRoomName")return "Enter a room name (1–32 characters).";
            if(error=="InvalidRoomCapacity")return "Choose 2–8 players.";
            if(error=="ConnectionLost"||error=="SessionEnded")return "The room connection ended. You can join another room.";
            return "Could not join or create the room ("+error+"). Please retry or refresh the list.";
        }
        private static RectTransform Rect(string name,Transform parent,Vector2 size,Vector2 position){var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(parent,false);r.sizeDelta=size;r.anchoredPosition=position;return r;}
        private UnityEngine.UI.Text Label(string name,Transform parent,string text,Vector2 size,Vector2 position,int fontSize){var t=Rect(name,parent,size,position).gameObject.AddComponent<UnityEngine.UI.Text>();t.font=font;t.fontSize=fontSize;t.supportRichText=false;t.color=Color.white;t.alignment=TextAnchor.MiddleCenter;t.text=text;t.raycastTarget=false;return t;}
        private UnityEngine.UI.Button Button(string name,Transform parent,Vector2 position,Vector2 size,UnityEngine.Events.UnityAction action){var r=Rect(name,parent,size,position);var image=r.gameObject.AddComponent<UnityEngine.UI.Image>();image.color=new Color(.2f,.34f,.48f);var b=r.gameObject.AddComponent<UnityEngine.UI.Button>();b.targetGraphic=image;b.onClick.AddListener(action);Label("Label",r,name,size,Vector2.zero,14);return b;}
        private UnityEngine.UI.InputField Input(string name,Transform parent,string hint,Vector2 position){var r=Rect(name,parent,new Vector2(320,32),position);r.gameObject.AddComponent<UnityEngine.UI.Image>().color=new Color(.14f,.22f,.3f);var f=r.gameObject.AddComponent<UnityEngine.UI.InputField>();f.textComponent=Label("Text",r,"",new Vector2(300,30),Vector2.zero,15);var p=Label("Placeholder",r,hint,new Vector2(300,30),Vector2.zero,14);p.color=new Color(.65f,.7f,.75f);f.placeholder=p;return f;}
        private async void Refresh(){await Browser.RefreshAsync();}
        private async void Next(){await Browser.RefreshAsync(true);}
        private async void Create(){await OnlineConnection.Instance.CreateRoomAsync(roomName.text,maxPlayers,isPrivate);}
        private async void Join(){await OnlineConnection.Instance.JoinAsync(joinCode.text);}
        private async void Leave(){await OnlineConnection.Instance.LeaveAsync();await Browser.RefreshAsync();}
    }
}
