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
            var canvasObject = new GameObject("AuthenticationCanvas", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 20;
            canvasObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            var scaler = canvasObject.GetComponent<UnityEngine.UI.CanvasScaler>(); scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1280,720);
            var panel = Rect("AuthenticationStatus", canvasObject.transform, new Vector2(650,30));
            panel.anchorMin = panel.anchorMax = new Vector2(.5f,0); panel.pivot = new Vector2(.5f,0); panel.anchoredPosition = new Vector2(0,4);
            panel.gameObject.AddComponent<UnityEngine.UI.Image>().color = new Color(.06f,.1f,.15f,.96f);
            status = Rect("Status", panel, new Vector2(530,28)).gameObject.AddComponent<UnityEngine.UI.Text>();
            status.rectTransform.anchoredPosition = new Vector2(-55,0); status.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); status.fontSize = 12; status.color = Color.white; status.alignment = TextAnchor.MiddleLeft; status.raycastTarget = false;
            var button = Rect("Retry", panel, new Vector2(90,26)); button.anchoredPosition = new Vector2(275,0);
            button.gameObject.AddComponent<UnityEngine.UI.Image>().color = new Color(.2f,.35f,.5f);
            retry = button.gameObject.AddComponent<UnityEngine.UI.Button>(); retry.onClick.AddListener(Retry);
            var label = Rect("Label",button,new Vector2(90,26)).gameObject.AddComponent<UnityEngine.UI.Text>(); label.font = status.font; label.fontSize = 12; label.color = Color.white; label.alignment = TextAnchor.MiddleCenter; label.text = "Retry"; label.raycastTarget = false;
        }
        private static RectTransform Rect(string name, Transform parent, Vector2 size)
        {
            var rect = new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent,false); rect.sizeDelta = size; return rect;
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
