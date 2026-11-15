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
        private void Awake()
        {
            var root = UiPrefab.Instantiate("OnlineCanvas", transform);
            panel = root.Find("ConnectionPanel").gameObject;
            status = UiPrefab.Get<UnityEngine.UI.Text>(panel.transform, "Status");
            code = UiPrefab.Get<UnityEngine.UI.InputField>(panel.transform, "JoinCode");
            UiPrefab.Bind(root, "Online test", () => panel.SetActive(!panel.activeSelf));
            host = UiPrefab.Bind(panel.transform, "Create Host", Create);
            join = UiPrefab.Bind(panel.transform, "Join", Join);
            leave = UiPrefab.Bind(panel.transform, "Leave", Leave);
            probe = UiPrefab.Bind(panel.transform, "Send test", () => OnlineConnection.Instance.SendProbe());
        }

        internal void SetVisible(bool visible) => panel.transform.parent.gameObject.SetActive(visible);
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
