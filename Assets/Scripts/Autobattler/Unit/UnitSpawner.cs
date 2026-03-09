using System;
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
        EnsureDependencies();

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

        if (HasStateAuthority)
        {
            Log("RequestSpawn -> SpawnOnServer DIRECT (I am StateAuthority)");
            SpawnOnServer(boardIndex, cell, Runner.LocalPlayer);
            return;
        }

        Log($"RequestSpawn -> RPC_RequestSpawn SEND to StateAuthority | from={Runner.LocalPlayer}");
        RPC_RequestSpawn(boardIndex, cell.Q, cell.R);
    }

    public bool TrySpawnCombatClone(UnitController sourceUnit, byte boardIndex, HexCoord cell, out NetworkObject spawnedObj)
    {
        spawnedObj = null;

        if (!HasStateAuthority)
        {
            LogW("TrySpawnCombatClone ABORT: HasStateAuthority is FALSE");
            return false;
        }

        if (sourceUnit == null || sourceUnit.Object == null || sourceUnit.IsCombatClone)
        {
            LogW("TrySpawnCombatClone ABORT: invalid source unit");
            return false;
        }

        if (!EnsureDependencies())
        {
            LogE("TrySpawnCombatClone ABORT: refs missing");
            return false;
        }

        if (!boardManager.IsInside(cell) || boardManager.IsOccupied(boardIndex, cell))
        {
            LogW($"TrySpawnCombatClone ABORT: invalid target cell | boardIndex={boardIndex} cell={cell}");
            return false;
        }

        PlayerRef inputAuthority = sourceUnit.Object.InputAuthority;
        return TrySpawnUnit(
            boardIndex,
            cell,
            inputAuthority,
            requireDeployZone: false,
            configureBeforeSpawn: unit => unit.SetPendingCombatClone(sourceUnit.Object.Id, sourceUnit.HP, sourceUnit.Mana),
            out spawnedObj);
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

        if (!EnsureDependencies())
        {
            LogE("SpawnOnServer ABORT: refs missing");
            return;
        }

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

        if (!TrySpawnUnit(boardIndex, cell, requester, requireDeployZone: true, configureBeforeSpawn: null, out _))
        {
            LogE($"SpawnOnServer FAIL: Runner.Spawn failed | boardIndex={boardIndex} cell={cell}");
        }
    }

    private bool TrySpawnUnit(
        byte boardIndex,
        HexCoord cell,
        PlayerRef inputAuthority,
        bool requireDeployZone,
        Action<UnitController> configureBeforeSpawn,
        out NetworkObject spawnedObj)
    {
        spawnedObj = null;

        if (Runner == null)
        {
            LogE("TrySpawnUnit FAIL: Runner is NULL");
            return false;
        }

        if (!unitPrefab.IsValid)
        {
            LogE("TrySpawnUnit FAIL: unitPrefab is not valid");
            return false;
        }

        Vector3 origin = flowManager.GetBoardOrigin(boardIndex);
        Vector3 pos = origin + boardGenerator.AxialToWorld(cell);
        pos.z = unitZ;

        Log($"TrySpawnUnit PASS -> Runner.Spawn | origin={origin} pos={pos} unitZ={unitZ} inputAuthority={inputAuthority}");

        spawnedObj = Runner.Spawn(
            unitPrefab,
            pos,
            Quaternion.identity,
            inputAuthority: inputAuthority,
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

                unit.SetPendingInit(boardIndex, cell, requireDeployZone);
                configureBeforeSpawn?.Invoke(unit);
            });

        Log($"Runner.Spawn RETURN | spawnedObj={(spawnedObj ? spawnedObj.name : "NULL")} id={spawnedObj?.Id}");
        return spawnedObj != null;
    }

    private bool EnsureDependencies()
    {
        boardManager ??= FindAnyObjectByType<BoardManager>();
        flowManager ??= FindAnyObjectByType<GameFlowManager>();
        boardGenerator ??= FindAnyObjectByType<HexBoardGenerator>();

        return boardManager != null && flowManager != null && boardGenerator != null;
    }
}
