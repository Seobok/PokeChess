using UnityEngine;
using PokeChess.Client.Services;
using PokeChess.Client.UI;
using PokeChess.Network.Session;

namespace PokeChess.Client.Bootstrap
{
    [DisallowMultipleComponent]
    public sealed class GameBootstrap : MonoBehaviour
    {
        public PokeChess.Client.Data.SkillVisualCatalog SkillVisuals;
        [Tooltip("Temporarily disabled for tester builds. Enable to restore account login and server admission.")]
        public bool UseAccountLogin;
        public bool IsInitialized { get; private set; }
        private string activeAccount;
        private int accountGeneration;
        private void Update() => OnlineAuthenticationService.Instance.Tick();
        private void Awake()
        {
            // Room/local UI needs pointer input even when the account UI is disabled.
            var events = FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>();
            if (events == null)
                events = new GameObject("EventSystem").AddComponent<UnityEngine.EventSystems.EventSystem>();
            if (events.GetComponent<UnityEngine.EventSystems.BaseInputModule>() == null)
                events.gameObject.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>().AssignDefaultActions();
            IsInitialized = true;
            Debug.Log("PokeChess bootstrap initialized.", this);
        }
        private async void Start()
        {
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-pokechess-content-qa") >= 0) return;
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-pokechess-items-qa") >= 0) return;
            OnlineAuthenticationService.ConfigureBootMode(UseAccountLogin);
            OnlineConnection.RequireAccountAdmission=UseAccountLogin;
            OnlineConnection.AccountAccessAllowed = () => OnlineAuthenticationService.Instance.IsReady;
            OnlineConnection.RequestAccountAdmission = OnlineAccountAccess.Admission;
            OnlineConnection.VerifyAccountAdmission = OnlineAccountAccess.Verify;
            OnlineConnection.ValidateAccountHost = OnlineAccountAccess.Host;
            RoomBrowser.AccountAccessAllowed = () => OnlineAuthenticationService.Instance.IsReady;
            if(UseAccountLogin) {
                AccountSessionService.Instance.Changed += AccountChanged;
                if (FindFirstObjectByType<AccountAuthenticationView>() == null)
                    new GameObject("AccountAuthentication").AddComponent<AccountAuthenticationView>();
            }
            if (FindFirstObjectByType<OnlineConnectionView>() == null)
                new GameObject("OnlineConnectionView").AddComponent<OnlineConnectionView>();
            if (FindFirstObjectByType<RoomFlowView>() == null)
                new GameObject("RoomFlowView").AddComponent<RoomFlowView>();
            FindFirstObjectByType<RoomFlowView>().SkillVisuals = SkillVisuals;
            if(UseAccountLogin) {
                AccountChanged();
                await AccountSessionService.Instance.RestoreAsync();
            } else await OnlineAuthenticationService.Instance.EnsureSignedInAsync();
        }
        private void OnDestroy() { if(UseAccountLogin) AccountSessionService.Instance.Changed -= AccountChanged; }
        private async void AccountChanged()
        {
            var own=AccountSessionService.Instance; var id=own.IsSignedIn?own.AccountId:null;
            if(id==activeAccount) return;
            var previous=activeAccount; activeAccount=id; int version=++accountGeneration;
            var online=OnlineAuthenticationService.Instance; online.InvalidateForAccountChange();
            var connection=OnlineConnection.Instance;
            if(previous!=null || connection.SessionId!=null || connection.IsBusy || connection.IsRecovering) await connection.LeaveAsync();
            if(connection.SessionId!=null) await connection.LeaveAsync();
            if(this==null || version!=accountGeneration) return;
            online.SignOut();
            if(id!=null && own.IsSignedIn && own.AccountId==id) await online.EnsureSignedInAsync();
        }
    }
}
