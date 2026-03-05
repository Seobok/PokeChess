using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;

public class FusionLobbyBootstrap : MonoBehaviour, INetworkRunnerCallbacks
{
    private static FusionLobbyBootstrap _instance;

    [Header("Room")]
    [SerializeField] private int maxPlayers = 8;
    [SerializeField] private string sessionCode = "TEST123";

    [Header("Scene")]
    [Tooltip("Build Settings에 들어있는 씬 경로를 정확히 넣어주세요. (예: Assets/Scenes/AutoBattler.unity)")]
    [SerializeField] private string autoBattlerScenePath = "Assets/Scenes/AutoBattler.unity";

    [Header("Input Policy")]
    [Tooltip("로비 씬에서는 Fusion 입력(ProvideInput)을 쓸지 여부")]
    [SerializeField] private bool provideInputInLobby = false;

    [Tooltip("게임 씬에서는 Fusion 입력(ProvideInput)을 쓸지 여부")]
    [SerializeField] private bool provideInputInGame = true;

    private NetworkRunner _runner;
    private NetworkSceneManagerDefault _sceneManager;

    // “룸 리스트용으로만” 러너가 떠있는 상태인지 표시
    private bool _runnerIsLobbyOnly;

    private List<SessionInfo> _sessionList = new();
    private bool _startingGame;
    private bool _startingRunner;

    private int _autoBattlerBuildIndex = -1;

    private void Awake()
    {
        // ✅ DontDestroy 중복 방지
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);

        // ✅ SceneManager 컴포넌트 1개만 재사용
        _sceneManager = GetComponent<NetworkSceneManagerDefault>();
        if (_sceneManager == null)
            _sceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>();

        // ✅ AutoBattler build index 캐시
        _autoBattlerBuildIndex = SceneUtility.GetBuildIndexByScenePath(autoBattlerScenePath);
        if (_autoBattlerBuildIndex < 0)
        {
            Debug.LogWarning(
                $"[FusionLobbyBootstrap] AutoBattler scene not found in Build Settings. Path='{autoBattlerScenePath}'\n" +
                $"Build Settings에 씬 추가/경로 확인 필요."
            );
        }
    }

    private void OnDestroy()
    {
        if (_instance == this)
            _instance = null;

        // 필요하면 여기서 Shutdown (에디터 종료 등)
        // ShutdownRunnerImmediate();
    }

    // -------------------------
    // Public Flow
    // -------------------------

    async Task StartRunner(GameMode mode, string code)
    {
        if (_startingRunner) return;
        _startingRunner = true;

        try
        {
            // ✅ 이미 “룸 리스트용 러너”가 떠있으면 Host/Join 전에 정리
            if (_runner != null && _runnerIsLobbyOnly)
            {
                ShutdownRunnerImmediate();
            }

            // ✅ 러너가 이미 Running 상태면 추가 StartGame 시도 방지
            if (_runner != null && _runner.State == NetworkRunner.States.Running)
            {
                Debug.LogWarning("[FusionLobbyBootstrap] Runner already running.");
                return;
            }

            if (_runner == null)
            {
                _runner = gameObject.AddComponent<NetworkRunner>();
                _runner.AddCallbacks(this);
            }

            _runnerIsLobbyOnly = false;

            // 로비에서는 입력 필요 없으면 false로 둬도 됨(필요 시 true)
            _runner.ProvideInput = provideInputInLobby;

            var scene = SceneRef.FromIndex(SceneManager.GetActiveScene().buildIndex);

            var result = await _runner.StartGame(new StartGameArgs
            {
                GameMode = mode,               // Host / Client
                SessionName = code,            // "방 코드"로 사용
                PlayerCount = maxPlayers,
                Scene = scene,                 // 현재 로비 씬에서 시작
                SceneManager = _sceneManager,  // ✅ 재사용
            });

            if (!result.Ok)
            {
                Debug.LogError($"StartGame failed: {result.ShutdownReason}");
                ShutdownRunnerImmediate();
            }
        }
        finally
        {
            _startingRunner = false;
        }
    }

    async Task JoinLobbyForSessionList()
    {
        // ✅ 이미 게임 러너가 Running이면 (간단하게) 룸리스트 기능 비활성 처리
        if (_runner != null && _runner.State == NetworkRunner.States.Running && !_runnerIsLobbyOnly)
        {
            Debug.LogWarning("[FusionLobbyBootstrap] Runner is running a game. Not joining lobby list.");
            return;
        }

        // 러너가 없으면 생성
        if (_runner == null)
        {
            _runner = gameObject.AddComponent<NetworkRunner>();
            _runner.AddCallbacks(this);
        }

        _runnerIsLobbyOnly = true;
        _runner.ProvideInput = provideInputInLobby;

        var res = await _runner.JoinSessionLobby(SessionLobby.Shared);
        if (!res.Ok)
        {
            Debug.LogError($"JoinSessionLobby failed: {res.ShutdownReason}");
            // 룸 리스트용 러너 생성 실패면 정리
            ShutdownRunnerImmediate();
        }
    }

    private void ShutdownRunnerImmediate()
    {
        if (_runner == null) return;

        try
        {
            _runner.Shutdown();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Runner.Shutdown exception: {e}");
        }

        Destroy(_runner);
        _runner = null;
        _runnerIsLobbyOnly = false;
        _startingGame = false;
        _startingRunner = false;
    }

    // -------------------------
    // GUI
    // -------------------------

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 380, 520), GUI.skin.box);

        GUILayout.Label("Session Code (방 코드)");
        sessionCode = GUILayout.TextField(sessionCode);

        GUILayout.Space(8);
        GUILayout.BeginHorizontal();

        GUI.enabled = !_startingRunner && (_runner == null || _runner.State != NetworkRunner.States.Running || _runnerIsLobbyOnly);
        if (GUILayout.Button("Host (방 만들기)"))
            _ = StartRunner(GameMode.Host, sessionCode);

        if (GUILayout.Button("Join (코드로 입장)"))
            _ = StartRunner(GameMode.Client, sessionCode);
        GUI.enabled = true;

        GUILayout.EndHorizontal();

        GUILayout.Space(8);

        GUI.enabled = !_startingRunner;
        if (GUILayout.Button("Get Room List (방 리스트 받기)"))
            _ = JoinLobbyForSessionList();
        GUI.enabled = true;

        GUILayout.Space(8);
        GUILayout.Label($"Rooms: {_sessionList.Count}");
        foreach (var s in _sessionList)
            GUILayout.Label($"{s.Name} ({s.PlayerCount}/{s.MaxPlayers})");

        GUILayout.Space(12);
        DrawInRoomUI();

        GUILayout.EndArea();
    }

    private void DrawInRoomUI()
    {
        if (_runner == null) return;
        if (_runner.State != NetworkRunner.States.Running) return;

        GUILayout.Label("=== In Room ===");
        GUILayout.Label($"Mode: {_runner.GameMode}");
        GUILayout.Label($"IsServer(Host): {_runner.IsServer}");
        GUILayout.Label($"IsSceneAuthority: {_runner.IsSceneAuthority}");
        GUILayout.Label($"LobbyOnlyRunner: {_runnerIsLobbyOnly}");
        GUILayout.Label($"ProvideInput: {_runner.ProvideInput}");

        var players = _runner.ActivePlayers.OrderBy(p => p.AsIndex).ToList();
        GUILayout.Label($"Players ({players.Count}/{maxPlayers})");
        foreach (var p in players)
        {
            var me = (p == _runner.LocalPlayer) ? " (ME)" : "";
            GUILayout.Label($"- PlayerRef: {p.AsIndex} / Raw:{p.RawEncoded}{me}");
        }

        GUILayout.Space(10);

        // 시작 버튼: 호스트(씬 권한자)만 보이게
        GUI.enabled = _runner.IsSceneAuthority && !_startingGame && !_runnerIsLobbyOnly;

        if (_runner.IsSceneAuthority && !_runnerIsLobbyOnly)
        {
            if (GUILayout.Button(_startingGame ? "Starting..." : "START (모두 AutoBattler로 이동)"))
            {
                _ = StartGameForAll();
            }
        }
        else if (_runnerIsLobbyOnly)
        {
            GUILayout.Label("현재는 '룸 리스트용 러너' 상태입니다. Host/Join으로 게임 러너를 시작하세요.");
        }
        else
        {
            GUILayout.Label("호스트가 시작을 누르면 AutoBattler 씬으로 이동합니다.");
        }

        GUI.enabled = true;
    }

    private async Task StartGameForAll()
    {
        if (_runner == null || !_runner.IsSceneAuthority) return;
        if (_startingGame) return;

        _startingGame = true;

        if (_autoBattlerBuildIndex < 0)
        {
            // 혹시 Awake에서 못 찾았으면 재시도
            _autoBattlerBuildIndex = SceneUtility.GetBuildIndexByScenePath(autoBattlerScenePath);
        }

        if (_autoBattlerBuildIndex < 0)
        {
            Debug.LogError(
                $"AutoBattler scene not found in Build Settings. Path='{autoBattlerScenePath}'\n" +
                $"1) File > Build Settings... 에 AutoBattler 씬을 추가\n" +
                $"2) 스크립트의 autoBattlerScenePath를 실제 경로로 수정"
            );
            _startingGame = false;
            return;
        }

        _ = _runner.LoadScene(SceneRef.FromIndex(_autoBattlerBuildIndex), LoadSceneMode.Single);
        await Task.Yield();
    }

    // -------------------------
    // INetworkRunnerCallbacks
    // -------------------------

    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
    {
        _sessionList = sessionList;
    }

    public void OnSceneLoadDone(NetworkRunner runner)
    {
        _startingGame = false;

        // ✅ 씬 전환이 끝난 뒤, 로비/게임 씬에 따라 ProvideInput 정책 적용
        int activeBuildIndex = SceneManager.GetActiveScene().buildIndex;
        bool isGameScene = (_autoBattlerBuildIndex >= 0 && activeBuildIndex == _autoBattlerBuildIndex);

        runner.ProvideInput = isGameScene ? provideInputInGame : provideInputInLobby;

        Debug.Log($"OnSceneLoadDone | activeBuildIndex={activeBuildIndex} | isGameScene={isGameScene} | ProvideInput={runner.ProvideInput}");
    }

    public void OnSceneLoadStart(NetworkRunner runner)
    {
        Debug.Log($"OnSceneLoadStart {runner}");
    }

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player) => Debug.Log($"OnPlayerJoined {player}");
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) => Debug.Log($"OnPlayerLeft {player}");

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        Debug.Log($"OnShutdown {shutdownReason}");

        // ✅ Runner가 꺼졌으면 로컬 참조 정리
        if (runner == _runner)
        {
            Destroy(_runner);
            _runner = null;
            _runnerIsLobbyOnly = false;
            _startingGame = false;
            _startingRunner = false;
        }
    }

    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) => Debug.Log($"OnDisconnectedFromServer {reason}");
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) => Debug.Log($"OnConnectRequest {request}");
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) => Debug.Log($"OnConnectFailed {reason}");
    public void OnConnectedToServer(NetworkRunner runner) => Debug.Log($"OnConnectedToServer {runner}");

    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) => Debug.Log($"OnObjectExitAOI {obj} - {player}");
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) => Debug.Log($"OnObjectEnterAOI {obj} - {player}");

    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) => Debug.Log($"OnUserSimulationMessage {message}");
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) => Debug.Log($"OnReliableDataReceived {player} - {key}");
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) => Debug.Log($"OnReliableDataProgress {player} - {key}");

    public void OnInput(NetworkRunner runner, NetworkInput input) { /* ProvideInput=true일 때만 의미 있음 */ }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }

    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) => Debug.Log($"OnCustomAuthenticationResponse {data}");
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) => Debug.Log($"OnHostMigration {hostMigrationToken}");
}