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
    [Header("Room")]
    [SerializeField] private int maxPlayers = 8;
    [SerializeField] private string sessionCode = "TEST123";

    [Header("Scene")]
    [Tooltip("Build Settings에 들어있는 씬 경로를 정확히 넣어주세요. (예: Assets/Scenes/AutoBattler.unity)")]
    [SerializeField] private string autoBattlerScenePath = "Assets/Scenes/AutoBattler.unity";

    private NetworkRunner _runner;
    private List<SessionInfo> _sessionList = new();

    private bool _startingGame;

    private void Awake()
    {
        // Single 로드 시에도 Runner가 유지되도록
        DontDestroyOnLoad(gameObject);
    }

    async Task StartRunner(GameMode mode, string code)
    {
        if (_runner != null) return;

        _runner = gameObject.AddComponent<NetworkRunner>();
        _runner.AddCallbacks(this);

        // 로비에서는 입력 필요 없으면 false로 둬도 됨(필요 시 true)
        _runner.ProvideInput = false;

        var scene = SceneRef.FromIndex(SceneManager.GetActiveScene().buildIndex);

        var result = await _runner.StartGame(new StartGameArgs
        {
            GameMode = mode,              // Host / Client
            SessionName = code,           // "방 코드"로 사용
            PlayerCount = maxPlayers,
            Scene = scene,
            SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>(),
        });

        if (!result.Ok)
        {
            Debug.LogError($"StartGame failed: {result.ShutdownReason}");
            Destroy(_runner);
            _runner = null;
        }
    }

    async Task JoinLobbyForSessionList()
    {
        // 세션 목록만 받고 싶을 때: 로비 조인
        if (_runner == null)
        {
            _runner = gameObject.AddComponent<NetworkRunner>();
            _runner.AddCallbacks(this);
        }

        var res = await _runner.JoinSessionLobby(SessionLobby.Shared);
        if (!res.Ok)
            Debug.LogError($"JoinSessionLobby failed: {res.ShutdownReason}");
    }

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 380, 520), GUI.skin.box);

        GUILayout.Label("Session Code (방 코드)");
        sessionCode = GUILayout.TextField(sessionCode);

        GUILayout.Space(8);
        GUILayout.BeginHorizontal();

        if (GUILayout.Button("Host (방 만들기)"))
            _ = StartRunner(GameMode.Host, sessionCode);

        if (GUILayout.Button("Join (코드로 입장)"))
            _ = StartRunner(GameMode.Client, sessionCode);

        GUILayout.EndHorizontal();

        GUILayout.Space(8);
        if (GUILayout.Button("Get Room List (방 리스트 받기)"))
            _ = JoinLobbyForSessionList();

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

        // 방 인원 표시 (PlayerRef로 표시)
        // ActivePlayers: 현재 세션에 접속한 플레이어 목록 :contentReference[oaicite:1]{index=1}
        var players = _runner.ActivePlayers.OrderBy(p => p.AsIndex).ToList(); // AsIndex는 정렬용으로 깔끔함 :contentReference[oaicite:2]{index=2}

        GUILayout.Label($"Players ({players.Count}/{maxPlayers})");
        foreach (var p in players)
        {
            var me = (p == _runner.LocalPlayer) ? " (ME)" : "";
            GUILayout.Label($"- PlayerRef: {p.AsIndex} / Raw:{p.RawEncoded}{me}"); // RawEncoded/AsIndex 참고 :contentReference[oaicite:3]{index=3}
        }

        GUILayout.Space(10);

        // 시작 버튼: 호스트(씬 권한자)만 보이게
        GUI.enabled = _runner.IsSceneAuthority && !_startingGame;

        if (_runner.IsSceneAuthority)
        {
            if (GUILayout.Button(_startingGame ? "Starting..." : "START (모두 AutoBattler로 이동)"))
            {
                _ = StartGameForAll();
            }
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

        // AutoBattler 씬 인덱스 찾기 (Build Settings 기준) :contentReference[oaicite:4]{index=4}
        int buildIndex = SceneUtility.GetBuildIndexByScenePath(autoBattlerScenePath);
        if (buildIndex < 0)
        {
            Debug.LogError(
                $"AutoBattler scene not found in Build Settings. Path='{autoBattlerScenePath}'\n" +
                $"1) File > Build Settings... 에 AutoBattler 씬을 추가\n" +
                $"2) 스크립트의 autoBattlerScenePath를 실제 경로로 수정"
            );
            _startingGame = false;
            return;
        }

        // Host/Master만 LoadScene 가능. 기본 SceneManager가 전원 동기 로드 처리 :contentReference[oaicite:5]{index=5}
        _ = _runner.LoadScene(SceneRef.FromIndex(buildIndex), LoadSceneMode.Single);

        // 씬 로딩은 비동기라 여기서 await할 건 없고, 로딩 완료 이벤트는 OnSceneLoadDone에서 받게 됨
        await Task.Yield();
    }

    // ===== INetworkRunnerCallbacks =====

    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player)
    {
        Debug.Log($"OnObjectExitAOI {obj} - {player}");
    }

    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player)
    {
        Debug.Log($"OnObjectEnterAOI {obj} - {player}");
    }

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        // 로비에서는 스폰하지 않음 (요구사항 1)
        // UI는 ActivePlayers에서 자동으로 보임
        Debug.Log($"OnPlayerJoined {player}");
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        // 로비에서는 스폰/디스폰 처리 없음
        Debug.Log($"OnPlayerLeft {player}");
    }

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        Debug.Log($"OnShutdown {shutdownReason}");
    }

    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        Debug.Log($"OnDisconnectedFromServer {reason}");
    }

    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token)
    {
        Debug.Log($"OnConnectRequest {request}");
    }

    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
    {
        Debug.Log($"OnConnectFailed {reason}");
    }

    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message)
    {
        Debug.Log($"OnUserSimulationMessage {message}");
    }

    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data)
    {
        Debug.Log($"OnReliableDataReceived {player} - {key}");
    }

    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress)
    {
        Debug.Log($"OnReliableDataProgress {player} - {key}");
    }

    public void OnInput(NetworkRunner runner, NetworkInput input)
    {
        Debug.Log($"OnInput {input}");
    }

    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input)
    {
        Debug.Log($"OnInputMissing {player} - {input}");
    }

    public void OnConnectedToServer(NetworkRunner runner)
    {
        Debug.Log($"OnConnectedToServer {runner}");
    }


    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
    {
        _sessionList = sessionList;
    }

    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data)
    {
        Debug.Log($"OnCustomAuthenticationResponse {data}");
    }

    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken)
    {
        Debug.Log($"OnHostMigration {hostMigrationToken}");
    }

    public void OnSceneLoadDone(NetworkRunner runner)
    {
        // 씬 전환 완료 후 처리하고 싶으면 여기서
        // 예) AutoBattler 씬에서 게임 매니저 스폰/초기화 등
        _startingGame = false;
        
        Debug.Log("OnSceneLoadDone");
    }

    public void OnSceneLoadStart(NetworkRunner runner)
    {
        Debug.Log($"OnSceneLoadStart {runner}");
    }
}