using Fusion;
using PokeChess.Autobattler;
using UnityEngine;

public class UnitSpawner : NetworkBehaviour
{
    [SerializeField] private NetworkPrefabRef unitPrefab;
    [SerializeField] private BoardManager boardManager;
    [SerializeField] private GameFlowManager flowManager;
    [SerializeField] private HexBoardGenerator boardGenerator;
    [SerializeField] private float unitZ = -1f;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private const string TAG = "<color=#66CCFF>[UnitSpawner]</color> ";
    private void Log(string msg) => Debug.Log(TAG + msg, this);
    private void LogW(string msg) => Debug.LogWarning(TAG + msg, this);
    private void LogE(string msg) => Debug.LogError(TAG + msg, this);
#else
    private void Log(string msg) { }
    private void LogW(string msg) { }
    private void LogE(string msg) { }
#endif

    public override void Spawned()
    {
        boardManager ??= FindAnyObjectByType<BoardManager>();
        flowManager ??= FindAnyObjectByType<GameFlowManager>();
        boardGenerator ??= FindAnyObjectByType<HexBoardGenerator>();

        Log($"Spawned | HasStateAuthority={HasStateAuthority} HasInputAuthority={HasInputAuthority} " +
            $"Runner={(Runner ? "OK" : "NULL")} Mode={Runner?.GameMode} IsServer={Runner?.IsServer} " +
            $"Object.Id={Object?.Id} StateAuth={Object?.StateAuthority} InputAuth={Object?.InputAuthority}");

        Log($"Refs | boardManager={(boardManager ? boardManager.name : "NULL")} " +
            $"flowManager={(flowManager ? flowManager.name : "NULL")} " +
            $"boardGenerator={(boardGenerator ? boardGenerator.name : "NULL")} " +
            $"unitPrefabValid={unitPrefab.IsValid}");
    }

    public void RequestSpawn(byte boardIndex, HexCoord cell)
    {
        Log($"RequestSpawn CALLED | local={Runner?.LocalPlayer} boardIndex={boardIndex} cell={cell} " +
            $"HasStateAuthority={HasStateAuthority} IsServer={Runner?.IsServer} StateAuth={Object?.StateAuthority}");

        if (Runner == null)
        {
            LogE("RequestSpawn FAIL: Runner is NULL");
            return;
        }

        if (!unitPrefab.IsValid)
        {
            LogE("RequestSpawn FAIL: unitPrefab is not valid (NetworkPrefabRef not set/registered?)");
            return;
        }

        // 서버(권한자)면 바로 스폰
        if (HasStateAuthority)
        {
            Log("RequestSpawn -> SpawnOnServer DIRECT (I am StateAuthority)");
            SpawnOnServer(boardIndex, cell, Runner.LocalPlayer);
            return;
        }

        // 클라면 RPC
        Log($"RequestSpawn -> RPC_RequestSpawn SEND to StateAuthority | from={Runner.LocalPlayer}");
        RPC_RequestSpawn(boardIndex, cell.Q, cell.R);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestSpawn(byte boardIndex, int q, int r, RpcInfo info = default)
    {
        Log($"RPC_RequestSpawn RECEIVED | src={info.Source} boardIndex={boardIndex} cell=({q},{r}) " +
            $"HasStateAuthority={HasStateAuthority} IsServer={Runner?.IsServer}");

        SpawnOnServer(boardIndex, new HexCoord(q, r), info.Source);
    }

    private void SpawnOnServer(byte boardIndex, HexCoord cell, PlayerRef requester)
    {
        Log($"SpawnOnServer ENTER | requester={requester} boardIndex={boardIndex} cell={cell} " +
            $"HasStateAuthority={HasStateAuthority} IsServer={Runner?.IsServer}");

        if (!HasStateAuthority)
        {
            LogW("SpawnOnServer ABORT: HasStateAuthority is FALSE (should only run on StateAuthority)");
            return;
        }

        if (boardManager == null || flowManager == null || boardGenerator == null)
        {
            LogE($"SpawnOnServer ABORT: refs missing | boardManager={(boardManager ? "OK" : "NULL")}, " +
                 $"flowManager={(flowManager ? "OK" : "NULL")}, boardGenerator={(boardGenerator ? "OK" : "NULL")}");
            return;
        }

        // 요청자 보드 할당 검증
        if (!flowManager.TryGetBoardIndex(requester, out byte assigned))
        {
            LogW($"SpawnOnServer ABORT: flowManager.TryGetBoardIndex FAILED for requester={requester}");
            return;
        }

        if (assigned != boardIndex)
        {
            LogW($"SpawnOnServer ABORT: board mismatch | requesterAssigned={assigned} requested={boardIndex}");
            return;
        }

        // 배치 구역 검증
        if (!boardManager.IsDeployZone(cell))
        {
            LogW($"SpawnOnServer ABORT: IsDeployZone FALSE | cell={cell} DeployStartRow={BoardManager.DeployStartRow}");
            return;
        }

        if (boardManager.IsOccupied(boardIndex, cell))
        {
            LogW($"SpawnOnServer ABORT: IsOccupied TRUE | boardIndex={boardIndex} cell={cell}");
            return;
        }

        // 서버에서 월드 좌표 계산
        Vector3 origin = flowManager.GetBoardOrigin(boardIndex);
        Vector3 pos = origin + boardGenerator.AxialToWorld(cell);
        pos.z = unitZ;

        Log($"SpawnOnServer PASS -> Runner.Spawn | origin={origin} pos={pos} unitZ={unitZ} inputAuthority={requester}");

        NetworkObject spawnedObj = null;

        spawnedObj = Runner.Spawn(unitPrefab, pos, Quaternion.identity,
            inputAuthority: requester,
            onBeforeSpawned: (runner, obj) =>
            {
                if (obj == null)
                {
                    LogE("onBeforeSpawned FAIL: obj is NULL");
                    return;
                }

                var unit = obj.GetComponent<UnitController>();
                if (unit == null)
                {
                    LogE($"onBeforeSpawned FAIL: UnitController missing on spawned obj={obj.name} -> Despawn");
                    runner.Despawn(obj);
                    return;
                }

                unit.SetPendingInit(boardIndex, cell);
            });

        Log($"Runner.Spawn RETURN | spawnedObj={(spawnedObj ? spawnedObj.name : "NULL")} id={spawnedObj?.Id}");
    }
}