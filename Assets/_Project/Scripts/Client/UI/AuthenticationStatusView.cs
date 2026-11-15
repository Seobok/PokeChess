using UnityEngine;
using PokeChess.Client.Services;

namespace PokeChess.Client.UI
{
    public sealed class AuthenticationStatusView : MonoBehaviour
    {
        private UnityEngine.UI.Text status;
        private UnityEngine.UI.Button retry;
        private void Awake()
        {
            if (FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var events = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem));
                events.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>().AssignDefaultActions();
            }
            var canvas = UiPrefab.Instantiate("AuthenticationCanvas", transform);
            status = UiPrefab.Get<UnityEngine.UI.Text>(canvas, "AuthenticationStatus/Status");
            retry = UiPrefab.Get<UnityEngine.UI.Button>(canvas, "AuthenticationStatus/Retry");
            retry.onClick.AddListener(Retry);
        }
        private void OnEnable() { OnlineAuthenticationService.Instance.Changed += Render; Render(); }
        private void OnDisable() { OnlineAuthenticationService.Instance.Changed -= Render; }
        private void Render()
        {
            var auth = OnlineAuthenticationService.Instance;
            status.text = auth.IsReady ? "ONLINE READY / PlayerId: " + auth.PlayerId : "ONLINE: " + auth.State + (auth.Error == null ? "" : " / " + auth.Error) + " / Local play available";
            retry.gameObject.SetActive(auth.State == AuthenticationState.Failed || auth.State == AuthenticationState.Expired || auth.State == AuthenticationState.SignedOut);
        }
        private async void Retry() { await OnlineAuthenticationService.Instance.EnsureSignedInAsync(); }
    }
}
