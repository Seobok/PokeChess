using System;
using System.Collections.Generic;
using Fusion;
using PokeChess.Autobattler;
using UnityEngine;

/// <summary>
/// Handles authoritative spawning for deployed units and combat clones.
/// </summary>
public class UnitSpawner : NetworkBehaviour
{
    [Serializable]
    private struct SpawnableUnitEntry
    {
        public NetworkPrefabRef prefab;
        public GameObject ghostPrefab;
        public Sprite buttonSprite;
    }

    [SerializeField] private SpawnableUnitEntry[] spawnableUnits;
    [SerializeField] private BoardManager boardManager;
    [SerializeField] private GameFlowManager flowManager;
    [SerializeField] private HexBoardGenerator boardGenerator;
    [SerializeField] private float unitZ = -1f;

    public override void Spawned()
    {
        EnsureDependencies();
    }

    public bool TryFindFirstEmptyBenchSlot(byte boardIndex, out HexCoord cell)
    {
        cell = default;

        if (!EnsureDependencies())
            return false;

        for (int i = 0; i < BoardManager.BenchSlotCount; i++)
        {
            HexCoord candidate = BoardManager.GetBenchCoord(i);
            if (boardManager.IsOccupied(boardIndex, candidate))
                continue;

            cell = candidate;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Requests a unit spawn from the state authority for the local player's board.
    /// </summary>
    public void RequestSpawn(byte boardIndex, HexCoord cell)
    {
        RequestSpawn(boardIndex, cell, 0);
    }

    /// <summary>
    /// Requests a unit spawn for a specific unit type.
    /// </summary>
    public void RequestSpawn(byte boardIndex, HexCoord cell, byte unitTypeId)
    {
        if (Runner == null || !TryGetSpawnableUnit(unitTypeId, out _))
            return;

        if (HasStateAuthority)
        {
            SpawnOnServer(boardIndex, cell, unitTypeId, Runner.LocalPlayer, spawnToBench: false);
            return;
        }

        RPC_RequestSpawn(boardIndex, cell.Q, cell.R, unitTypeId);
    }

    public void RequestSpawnToBench(byte unitTypeId)
    {
        if (Runner == null || !EnsureDependencies() || !TryGetSpawnableUnit(unitTypeId, out _))
            return;

        if (!flowManager.TryGetBoardIndex(Runner.LocalPlayer, out byte boardIndex))
            return;

        if (HasStateAuthority)
        {
            SpawnOnServer(boardIndex, default, unitTypeId, Runner.LocalPlayer, spawnToBench: true);
            return;
        }

        RPC_RequestSpawnToBench(unitTypeId);
    }

    public void RequestMoveUnit(NetworkId unitId, HexCoord targetCell)
    {
        if (Runner == null)
            return;

        if (HasStateAuthority)
        {
            MoveUnitOnServer(unitId, targetCell, Runner.LocalPlayer);
            return;
        }

        RPC_RequestMoveUnit(unitId, targetCell.Q, targetCell.R);
    }

    public bool IsValidUnitType(byte unitTypeId)
    {
        return TryGetSpawnableUnit(unitTypeId, out _);
    }

    public GameObject GetGhostPrefab(byte unitTypeId)
    {
        return TryGetSpawnableUnit(unitTypeId, out SpawnableUnitEntry entry)
            ? entry.ghostPrefab
            : null;
    }

    public Sprite GetButtonSprite(byte unitTypeId)
    {
        return TryGetSpawnableUnit(unitTypeId, out SpawnableUnitEntry entry)
            ? entry.buttonSprite
            : null;
    }

    public bool TryGetRandomSpawnableUnitType(out byte unitTypeId)
    {
        unitTypeId = 0;

        if (spawnableUnits == null || spawnableUnits.Length == 0)
            return false;

        List<byte> validUnitTypes = null;
        for (byte i = 0; i < spawnableUnits.Length; i++)
        {
            if (!spawnableUnits[i].prefab.IsValid)
                continue;

            validUnitTypes ??= new List<byte>();
            validUnitTypes.Add(i);
        }

        if (validUnitTypes == null || validUnitTypes.Count == 0)
            return false;

        int randomIndex = UnityEngine.Random.Range(0, validUnitTypes.Count);
        unitTypeId = validUnitTypes[randomIndex];
        return true;
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

        if (!TryGetSpawnableUnit(sourceUnit.UnitTypeId, out _))
            return false;

        if (!boardManager.IsInside(cell) || boardManager.IsOccupied(boardIndex, cell))
            return false;

        PlayerRef inputAuthority = sourceUnit.Object.InputAuthority;
        return TrySpawnUnit(
            sourceUnit.UnitTypeId,
            boardIndex,
            cell,
            inputAuthority,
            requireDeployZone: false,
            configureBeforeSpawn: unit => unit.SetPendingCombatClone(sourceUnit.Object.Id, sourceUnit.HP, sourceUnit.Mana),
            out spawnedObj);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestSpawn(byte boardIndex, int q, int r, byte unitTypeId, RpcInfo info = default)
    {
        SpawnOnServer(boardIndex, new HexCoord(q, r), unitTypeId, info.Source, spawnToBench: false);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestSpawnToBench(byte unitTypeId, RpcInfo info = default)
    {
        SpawnOnServer(0, default, unitTypeId, info.Source, spawnToBench: true);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestMoveUnit(NetworkId unitId, int targetQ, int targetR, RpcInfo info = default)
    {
        MoveUnitOnServer(unitId, new HexCoord(targetQ, targetR), info.Source);
    }

    /// <summary>
    /// Validates ownership and deploy rules before creating the networked unit.
    /// </summary>
    private void SpawnOnServer(byte boardIndex, HexCoord cell, byte unitTypeId, PlayerRef requester, bool spawnToBench)
    {
        if (!HasStateAuthority)
            return;

        if (!EnsureDependencies())
            return;

        if (!TryGetSpawnableUnit(unitTypeId, out _))
            return;

        if (!flowManager.TryGetBoardIndex(requester, out byte assigned))
            return;

        if (spawnToBench)
        {
            boardIndex = assigned;
            if (!TryFindFirstEmptyBenchSlot(boardIndex, out cell))
                return;
        }
        else
        {
            if (assigned != boardIndex)
                return;

            if (!boardManager.IsDeployZone(cell))
                return;

            if (boardManager.IsOccupied(boardIndex, cell))
                return;
        }

        TrySpawnUnit(unitTypeId, boardIndex, cell, requester, requireDeployZone: !spawnToBench, configureBeforeSpawn: null, out _);
    }

    private void MoveUnitOnServer(NetworkId unitId, HexCoord targetCell, PlayerRef requester)
    {
        if (!HasStateAuthority || unitId == default)
            return;

        if (!EnsureDependencies())
            return;

        if (!flowManager.TryGetBoardIndex(requester, out byte assignedBoard))
            return;

        if (!boardManager.IsPlayerPlacementCell(targetCell))
            return;

        if (!Runner.TryFindObject(unitId, out NetworkObject obj))
            return;

        if (!obj.TryGetComponent(out UnitController movingUnit))
            return;

        if (movingUnit.IsCombatClone || movingUnit.BoardIndex != assignedBoard)
            return;

        if (obj.InputAuthority != requester)
            return;

        HexCoord sourceCell = movingUnit.Cell;
        if (sourceCell == targetCell)
            return;

        if (!boardManager.IsPlayerPlacementCell(sourceCell))
            return;

        if (!boardManager.TryGetOccupant(assignedBoard, targetCell, out NetworkId targetOccupantId))
        {
            movingUnit.ForceRelocateToBoard(assignedBoard, targetCell);
            return;
        }

        if (targetOccupantId == unitId)
            return;

        if (!Runner.TryFindObject(targetOccupantId, out NetworkObject targetObject))
            return;

        if (!targetObject.TryGetComponent(out UnitController targetUnit))
            return;

        if (targetUnit.IsCombatClone || targetUnit.BoardIndex != assignedBoard)
            return;

        movingUnit.SwapPlacementWith(targetUnit);
    }

    /// <summary>
    /// Creates the network object and injects pending board state before spawn completes.
    /// </summary>
    private bool TrySpawnUnit(
        byte unitTypeId,
        byte boardIndex,
        HexCoord cell,
        PlayerRef inputAuthority,
        bool requireDeployZone,
        Action<UnitController> configureBeforeSpawn,
        out NetworkObject spawnedObj)
    {
        spawnedObj = null;

        if (Runner == null || !TryGetSpawnableUnit(unitTypeId, out SpawnableUnitEntry entry))
            return false;

        Vector3 origin = flowManager.GetBoardOrigin(boardIndex);
        Vector3 pos = origin + boardGenerator.AxialToWorld(cell);
        pos.z = unitZ;

        spawnedObj = Runner.Spawn(
            entry.prefab,
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

                unit.SetPendingInit(boardIndex, cell, unitTypeId, requireDeployZone);
                configureBeforeSpawn?.Invoke(unit);
            });

        return spawnedObj != null;
    }

    private bool TryGetSpawnableUnit(byte unitTypeId, out SpawnableUnitEntry entry)
    {
        entry = default;

        if (spawnableUnits == null || unitTypeId >= spawnableUnits.Length)
            return false;

        entry = spawnableUnits[unitTypeId];
        return entry.prefab.IsValid;
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
