using UnityEngine;
using PokeChess.Client.Services;
using PokeChess.Client.UI;

namespace PokeChess.Client.Bootstrap
{
    [DisallowMultipleComponent]
    public sealed class GameBootstrap : MonoBehaviour
    {
        public bool IsInitialized { get; private set; }

        private void Awake()
        {
            IsInitialized = true;
            Debug.Log("PokeChess bootstrap initialized.", this);
        }

        private async void Start()
        {
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-pokechess-content-qa") >= 0) return;
            if (FindFirstObjectByType<AuthenticationStatusView>() == null)
                new GameObject("AuthenticationStatus").AddComponent<AuthenticationStatusView>();
            if (FindFirstObjectByType<OnlineConnectionView>() == null)
                new GameObject("OnlineConnectionView").AddComponent<OnlineConnectionView>();
            await OnlineAuthenticationService.Instance.EnsureSignedInAsync();
        }
    }
}
