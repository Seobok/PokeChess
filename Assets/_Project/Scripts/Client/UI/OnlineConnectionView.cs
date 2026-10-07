using UnityEngine;
using PokeChess.Client.Services;
using PokeChess.Network.Session;

namespace PokeChess.Client.UI
{
    public sealed class OnlineConnectionView : MonoBehaviour
    {
        private GameObject panel;
        private UnityEngine.UI.Text status;
        private UnityEngine.UI.InputField code;
        private UnityEngine.UI.Button host,join,leave,probe;
        private Font font;
        private void Awake()
        {
            font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var root=Rect("OnlineCanvas",transform,new Vector2(1280,720),Vector2.zero);
            var canvas=root.gameObject.AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=25;
            var scaler=root.gameObject.AddComponent<UnityEngine.UI.CanvasScaler>();scaler.uiScaleMode=UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1280,720);
            root.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            Button("Online test",root,new Vector2(280,335),()=>panel.SetActive(!panel.activeSelf));
            var body=Rect("ConnectionPanel",root,new Vector2(500,270),Vector2.zero);panel=body.gameObject;
            body.gameObject.AddComponent<UnityEngine.UI.Image>().color=new Color(.06f,.1f,.16f,.98f);
            Label("Title",body,"ONLINE CONNECTION TEST",new Vector2(440,30),new Vector2(0,108),18);
            status=Label("Status",body,"",new Vector2(460,90),new Vector2(0,48),13);
            var input=Rect("JoinCode",body,new Vector2(300,32),new Vector2(0,-25));input.gameObject.AddComponent<UnityEngine.UI.Image>().color=new Color(.15f,.23f,.32f);
            code=input.gameObject.AddComponent<UnityEngine.UI.InputField>();code.characterLimit=32;code.textComponent=Label("Text",input,"",new Vector2(280,30),Vector2.zero,15);
            var placeholder=Label("Placeholder",input,"Enter session code",new Vector2(280,30),Vector2.zero,14);placeholder.color=new Color(.65f,.7f,.75f);code.placeholder=placeholder;
            host=Button("Create Host",body,new Vector2(-175,-78),Create);
            join=Button("Join",body,new Vector2(-58,-78),Join);
            leave=Button("Leave",body,new Vector2(58,-78),Leave);
            probe=Button("Send test",body,new Vector2(175,-78),()=>OnlineConnection.Instance.SendProbe());
            Label("Hint",body,"Connection only / local match remains independent",new Vector2(450,22),new Vector2(0,-115),12);
            panel.SetActive(FindFirstObjectByType<PlacementSandboxView>()==null);
        }
        private static RectTransform Rect(string name,Transform parent,Vector2 size,Vector2 position)
        { var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(parent,false);r.sizeDelta=size;r.anchoredPosition=position;return r; }
        private UnityEngine.UI.Text Label(string name,Transform parent,string value,Vector2 size,Vector2 position,int fontSize)
        { var t=Rect(name,parent,size,position).gameObject.AddComponent<UnityEngine.UI.Text>();t.font=font;t.fontSize=fontSize;t.color=Color.white;t.alignment=TextAnchor.MiddleCenter;t.text=value;t.raycastTarget=false;return t; }
        private UnityEngine.UI.Button Button(string name,Transform parent,Vector2 position,UnityEngine.Events.UnityAction action)
        { var r=Rect(name,parent,new Vector2(108,30),position);var image=r.gameObject.AddComponent<UnityEngine.UI.Image>();image.color=new Color(.2f,.34f,.48f);var b=r.gameObject.AddComponent<UnityEngine.UI.Button>();b.targetGraphic=image;b.onClick.AddListener(action);Label("Label",r,name,r.sizeDelta,Vector2.zero,13);return b; }
        private void Update()
        {
            var s=OnlineConnection.Instance;bool ready=OnlineAuthenticationService.Instance.IsReady;
            bool canConnect=ready&&!s.IsBusy&&(s.State==ConnectionState.Idle||s.State==ConnectionState.Failed);
            host.interactable=join.interactable=canConnect;code.interactable=canConnect;
            leave.interactable=!s.IsBusy&&s.SessionId!=null;probe.interactable=s.State==ConnectionState.Connected&&!s.IsHost;
            status.text=(ready?"":"Waiting for authentication\n")+s.State+" / "+(s.IsHost?"HOST":"CLIENT")+" / peers: "+s.ConnectedPlayers+
                "\nCode: "+(s.Code??"—")+"\nReceived: "+s.RequestsReceived+" / Replies: "+s.RepliesReceived+" / RTT: "+s.LastRoundTripMilliseconds.ToString("F0")+" ms"+(s.Error==null?"":"\n"+s.Error);
        }
        private async void Create() { await OnlineConnection.Instance.CreateAsync(); }
        private async void Join() { await OnlineConnection.Instance.JoinAsync(code.text); }
        private async void Leave() { await OnlineConnection.Instance.LeaveAsync(); }
    }
}
