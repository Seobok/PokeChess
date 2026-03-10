using System;
using Fusion;
using PokeChess.Autobattler;
using UnityEngine;

/// <summary>
/// Handles authoritative spawning for deployed units and combat clones.
/// </summary>
public class UnitSpawner : NetworkBehaviour
{
    [SerializeField] private NetworkPrefabRef unitPrefab;
    [SerializeField] private BoardManager boardManager;
    [SerializeField] private GameFlowManager flowManager;
    [SerializeField] private HexBoardGenerator boardGenerator;
    [SerializeField] private float unitZ = -1f;

    public override void Spawned()
    {
        EnsureDependencies();
    }

    /// <summary>
    /// Requests a unit spawn from the state authority for the local player's board.
    /// </summary>
    public void RequestSpawn(byte boardIndex, HexCoord cell)
    {
        if (Runner == null || !unitPrefab.IsValid)
            return;

        if (HasStateAuthority)
        {
            SpawnOnServer(boardIndex, cell, Runner.LocalPlayer);
            return;
        }

        RPC_RequestSpawn(boardIndex, cell.Q, cell.R);
    }

    /// <summary>
    /// Spawns a transient combat clone that mirrors an existing unit's combat state.
    /// </summary>
    public bool TrySpawnCombatClone(UnitController sourceUnit, byte boardIndex, HexCoord cell, out NetworkObject spawnedObj)
    {
        spawnedObj = null;

        if (!HasStateAuthority)
            return false;

        if (sourceUnit == null || sourceUnit.Object == null || sourceUnit.IsCombatClone)
            return false;

        if (!EnsureDependencies())
            return false;

        if (!boardManager.IsInside(cell) || boardManager.IsOccupied(boardIndex, cell))
            return false;

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
        SpawnOnServer(boardIndex, new HexCoord(q, r), info.Source);
    }

    /// <summary>
    /// Validates ownership and deploy rules before creating the networked unit.
    /// </summary>
    private void SpawnOnServer(byte boardIndex, HexCoord cell, PlayerRef requester)
    {
        if (!HasStateAuthority)
            return;

        if (!EnsureDependencies())
            return;

        if (!flowManager.TryGetBoardIndex(requester, out byte assigned))
            return;

        if (assigned != boardIndex)
            return;

        if (!boardManager.IsDeployZone(cell))
            return;

        if (boardManager.IsOccupied(boardIndex, cell))
            return;

        TrySpawnUnit(boardIndex, cell, requester, requireDeployZone: true, configureBeforeSpawn: null, out _);
    }

    /// <summary>
    /// Creates the network object and injects pending board state before spawn completes.
    /// </summary>
    private bool TrySpawnUnit(
        byte boardIndex,
        HexCoord cell,
        PlayerRef inputAuthority,
        bool requireDeployZone,
        Action<UnitController> configureBeforeSpawn,
        out NetworkObject spawnedObj)
    {
        spawnedObj = null;

        if (Runner == null || !unitPrefab.IsValid)
            return false;

        Vector3 origin = flowManager.GetBoardOrigin(boardIndex);
        Vector3 pos = origin + boardGenerator.AxialToWorld(cell);
        pos.z = unitZ;

        spawnedObj = Runner.Spawn(
            unitPrefab,
            pos,
            Quaternion.identity,
            inputAuthority: inputAuthority,
            onBeforeSpawned: (runner, obj) =>
            {
                if (obj == null)
                    return;

                var unit = obj.GetComponent<UnitController>();
                if (unit == null)
                    return;

                unit.SetPendingInit(boardIndex, cell, requireDeployZone);
                configureBeforeSpawn?.Invoke(unit);
            });

        return spawnedObj != null;
    }

    /// <summary>
    /// Lazily resolves scene references so the spawner can work after dynamic scene loads.
    /// </summary>
    private bool EnsureDependencies()
    {
        boardManager ??= FindAnyObjectByType<BoardManager>();
        flowManager ??= FindAnyObjectByType<GameFlowManager>();
        boardGenerator ??= FindAnyObjectByType<HexBoardGenerator>();

        return boardManager != null && flowManager != null && boardGenerator != null;
    }
}
