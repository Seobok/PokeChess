using System.Collections.Generic;
using System.Linq;
using Fusion;
using UnityEngine;

namespace PokeChess.Autobattler
{
    public enum GameFlowState : byte
    {
        None = 0,
        Preparation = 1,
        Combat = 2,
        Result = 3
    }

    public class GameFlowManager : NetworkBehaviour
    {
        private const int MaxPlayerCount = 8;
        private const byte InvalidBoardIndex = byte.MaxValue;

        [Header("Board")]
        [SerializeField] private HexBoardGenerator boardGenerator;
        [SerializeField] private UnitSpawner unitSpawner;
        [SerializeField] private bool generateBoardOnSpawn = true;

        [Header("Camera")]
        [SerializeField] private Camera targetCamera;
        [SerializeField] private Vector3 cameraOffset = new(0f, 0f, -10f);
        [SerializeField] private bool applyCameraEveryFrame = true;

        [Header("Board Origins")]
        [SerializeField] private List<Vector3> boardOrigins = new();

        [Networked] public GameFlowState FlowState { get; private set; }
        [Networked] public byte BoardCount { get; private set; }
        [Networked, Capacity(MaxPlayerCount)] private NetworkArray<byte> CombatViewBoardByPlayerBoard => default;
        [Networked, Capacity(MaxPlayerCount)] private NetworkArray<NetworkBool> CombatViewRotatedByPlayerBoard => default;

        private int _lastAppliedBoardIndex = -1;
        private int _lastGeneratedBoardCount = -1;
        private int _lastConnectedPlayerCount = -1;
        private float _lastAppliedCameraZRotation = float.NaN;

        private const float CameraSnapEpsilonSqr = 0.0001f;

        private class CombatPairRuntime
        {
            public PlayerRef HostPlayer;
            public PlayerRef GuestPlayer;
            public byte HostBoardIndex;
            public byte GuestBoardIndex;
            public readonly List<NetworkId> SpawnedCloneIds = new();
        }

        private readonly List<CombatPairRuntime> _activeCombatPairs = new();

        public override void Spawned()
        {
            EnsureBoardGenerator();
            EnsureUnitSpawner();

            if (HasStateAuthority && generateBoardOnSpawn)
            {
                StartFlowForConnectedPlayers();
            }

            TryGenerateBoardsIfNeeded();
            TryFocusLocalCameraToAssignedBoard(force: true);
        }

        public override void FixedUpdateNetwork()
        {
            if (HasStateAuthority && FlowState == GameFlowState.Preparation)
            {
                UpdateBoardCountIfNeeded();
            }

            TryGenerateBoardsIfNeeded();

            if (!applyCameraEveryFrame)
            {
                TryFocusLocalCameraToAssignedBoard();
            }
        }

        private void LateUpdate()
        {
            if (!applyCameraEveryFrame) return;
            if (Runner == null) return;
            if (Runner.State != NetworkRunner.States.Running) return;

            TryFocusLocalCameraToAssignedBoard();
        }

        public void StartFlowForConnectedPlayers()
        {
            if (!HasStateAuthority)
            {
                Debug.LogWarning("[GameFlowManager] HasStateAuthority is false");
                return;
            }

            UpdateBoardCountIfNeeded(force: true);
            ClearActiveCombatPairs();
            ResetCombatViewMappings();
            FlowState = GameFlowState.Preparation;
        }

        public void StartCombat()
        {
            if (!HasStateAuthority) return;
            ResetCombatViewMappings();
            FlowState = GameFlowState.Combat;
        }

        public void FinishRound()
        {
            if (!HasStateAuthority) return;
            ClearActiveCombatPairs();
            ResetCombatViewMappings();
            FlowState = GameFlowState.Result;
        }

        public bool ShouldUnitSimulate(UnitController unit)
        {
            if (unit == null)
                return false;

            if (FlowState != GameFlowState.Combat)
                return !unit.IsCombatClone;

            if (unit.IsCombatClone)
                return true;

            for (int i = 0; i < _activeCombatPairs.Count; i++)
            {
                CombatPairRuntime pair = _activeCombatPairs[i];
                if (unit.BoardIndex == pair.HostBoardIndex)
                    return true;

                if (unit.BoardIndex == pair.GuestBoardIndex)
                    return false;
            }

            return false;
        }

        private void UpdateBoardCountIfNeeded(bool force = false)
        {
            int connected = GetConnectedPlayerCount();
            if (!force && connected == _lastConnectedPlayerCount)
                return;

            _lastConnectedPlayerCount = connected;

            int originCap = boardOrigins != null ? boardOrigins.Count : 0;
            int clamped = Mathf.Min(connected, MaxPlayerCount, originCap);
            byte newCount = (byte)Mathf.Max(0, clamped);

            if (BoardCount != newCount)
            {
                BoardCount = newCount;
                ClearActiveCombatPairs();
                ResetCombatViewMappings();
                _lastGeneratedBoardCount = -1;
                _lastAppliedBoardIndex = -1;
                _lastAppliedCameraZRotation = float.NaN;
            }
        }

        private int GetConnectedPlayerCount()
        {
            if (Runner == null) return 0;

            int count = 0;
            foreach (PlayerRef _ in Runner.ActivePlayers)
            {
                count++;
            }

            return Mathf.Min(count, MaxPlayerCount);
        }

        private void EnsureBoardGenerator()
        {
            if (boardGenerator != null) return;

            boardGenerator = FindAnyObjectByType<HexBoardGenerator>();
            if (boardGenerator == null)
            {
                Debug.LogError("[GameFlowManager] HexBoardGenerator is missing in scene.", this);
            }
        }

        private void EnsureUnitSpawner()
        {
            if (unitSpawner != null) return;

            unitSpawner = FindAnyObjectByType<UnitSpawner>();
            if (unitSpawner == null)
            {
                Debug.LogError("[GameFlowManager] UnitSpawner is missing in scene.", this);
            }
        }

        private void TryGenerateBoardsIfNeeded()
        {
            if (BoardCount <= 0) return;

            EnsureBoardGenerator();
            if (boardGenerator == null) return;

            if (boardOrigins == null || boardOrigins.Count == 0)
            {
                Debug.LogError("[GameFlowManager] boardOrigins is empty.", this);
                return;
            }

            if (_lastGeneratedBoardCount == BoardCount)
                return;

            boardGenerator.GenerateBoardsAt(boardOrigins, BoardCount);
            _lastGeneratedBoardCount = BoardCount;
            _lastAppliedBoardIndex = -1;
            _lastAppliedCameraZRotation = float.NaN;
        }

        private void TryFocusLocalCameraToAssignedBoard(bool force = false)
        {
            if (Runner == null) return;
            if (BoardCount <= 0) return;

            int localBoardIndex = GetLocalPlayerViewBoardIndex();
            if (localBoardIndex < 0) return;
            if (boardOrigins == null || localBoardIndex >= boardOrigins.Count) return;

            if (targetCamera == null || !targetCamera.isActiveAndEnabled)
            {
                targetCamera = FindBestCamera();
            }
            if (targetCamera == null) return;

            EnsureBoardGenerator();
            if (boardGenerator == null) return;

            Vector3 boardCenter = boardGenerator.GetBoardCenter(boardOrigins[localBoardIndex]);
            Vector3 desired = boardCenter + cameraOffset;
            float zRot = GetLocalPlayerCombatCameraZRotation();

            if (!force && localBoardIndex == _lastAppliedBoardIndex)
            {
                bool samePosition = (targetCamera.transform.position - desired).sqrMagnitude < CameraSnapEpsilonSqr;
                bool sameRotation = !float.IsNaN(_lastAppliedCameraZRotation)
                    && Mathf.Abs(Mathf.DeltaAngle(_lastAppliedCameraZRotation, zRot)) < 0.01f;
                if (samePosition && sameRotation)
                    return;
            }

            targetCamera.transform.position = desired;
            _lastAppliedBoardIndex = localBoardIndex;
            targetCamera.transform.rotation = Quaternion.Euler(0f, 0f, zRot);
            _lastAppliedCameraZRotation = zRot;
        }

        private Camera FindBestCamera()
        {
            var cam = Camera.main;
            if (cam != null && cam.isActiveAndEnabled)
                return cam;

            var cams = FindObjectsByType<Camera>(FindObjectsSortMode.None);
            for (int i = 0; i < cams.Length; i++)
            {
                if (cams[i] != null && cams[i].isActiveAndEnabled)
                    return cams[i];
            }

            return null;
        }

        private int GetLocalPlayerBoardIndex()
        {
            if (Runner == null) return -1;

            PlayerRef localPlayer = Runner.LocalPlayer;
            if (localPlayer == PlayerRef.None) return -1;

            List<PlayerRef> orderedPlayers = Runner.ActivePlayers.OrderBy(player => player.AsIndex).ToList();
            for (int i = 0; i < orderedPlayers.Count; i++)
            {
                if (orderedPlayers[i] == localPlayer)
                    return i;
            }

            return -1;
        }

        public bool TryGetBoardIndex(PlayerRef player, out byte index)
        {
            index = 0;
            if (Runner == null || player == PlayerRef.None) return false;

            var ordered = Runner.ActivePlayers.OrderBy(p => p.AsIndex).ToList();
            for (int i = 0; i < ordered.Count; i++)
            {
                if (ordered[i] == player)
                {
                    index = (byte)i;
                    return true;
                }
            }

            return false;
        }

        public Vector3 GetBoardOrigin(byte boardIndex)
        {
            if (boardOrigins == null || boardIndex >= boardOrigins.Count) return Vector3.zero;
            return boardOrigins[boardIndex];
        }

        public Vector3 GetCellWorldPosition(byte boardIndex, HexCoord cell)
        {
            EnsureBoardGenerator();
            if (boardGenerator == null) return Vector3.zero;

            if (boardOrigins == null || boardIndex >= boardOrigins.Count) return Vector3.zero;
            return boardOrigins[boardIndex] + boardGenerator.AxialToWorld(cell);
        }

        private bool PrepareCombatPair(PlayerRef hostPlayer, PlayerRef guestPlayer)
        {
            if (!HasStateAuthority || Runner == null)
                return false;

            EnsureUnitSpawner();
            if (unitSpawner == null)
                return false;

            if (!TryGetBoardIndex(hostPlayer, out byte hostBoard))
                return false;

            if (!TryGetBoardIndex(guestPlayer, out byte guestBoard))
                return false;

            CombatPairRuntime runtime = new CombatPairRuntime
            {
                HostPlayer = hostPlayer,
                GuestPlayer = guestPlayer,
                HostBoardIndex = hostBoard,
                GuestBoardIndex = guestBoard
            };

            SetCombatViewForBoard(hostBoard, hostBoard, false);
            SetCombatViewForBoard(guestBoard, hostBoard, true);

            var allUnits = FindObjectsByType<UnitController>(FindObjectsSortMode.None);
            foreach (var unit in allUnits)
            {
                if (unit == null || unit.Object == null)
                    continue;

                if (unit.IsCombatClone)
                    continue;

                if (unit.BoardIndex != guestBoard)
                    continue;

                HexCoord targetCell = Rotate180(unit.Cell);
                if (!unitSpawner.TrySpawnCombatClone(unit, hostBoard, targetCell, out NetworkObject cloneObject))
                {
                    Debug.LogWarning($"[GameFlowManager] PrepareCombatPair failed while spawning clone for unit={unit.Object.Id}");
                    CleanupCombatPair(runtime);
                    return false;
                }

                runtime.SpawnedCloneIds.Add(cloneObject.Id);
            }

            _activeCombatPairs.Add(runtime);
            return true;
        }

        public bool StartCombatRound(List<MatchPair> pairs)
        {
            if (!HasStateAuthority || Runner == null)
                return false;

            ClearActiveCombatPairs();
            ResetCombatViewMappings();

            foreach (var pair in pairs)
            {
                if (pair.A == PlayerRef.None || pair.B == PlayerRef.None)
                    continue;

                if (!PrepareCombatPair(pair.A, pair.B))
                {
                    Debug.LogWarning($"[GameFlowManager] StartCombatRound failed while preparing pair {pair.A} vs {pair.B}");
                    ClearActiveCombatPairs();
                    ResetCombatViewMappings();
                    return false;
                }
            }

            FlowState = GameFlowState.Combat;
            return true;
        }

        public void RestoreCombatRound()
        {
            if (!HasStateAuthority || Runner == null)
                return;

            ClearActiveCombatPairs();
            ResetCombatViewMappings();
            FlowState = GameFlowState.Result;
        }

        private void ClearActiveCombatPairs()
        {
            for (int i = 0; i < _activeCombatPairs.Count; i++)
            {
                CleanupCombatPair(_activeCombatPairs[i]);
            }

            _activeCombatPairs.Clear();
        }

        private void CleanupCombatPair(CombatPairRuntime runtime)
        {
            for (int i = 0; i < runtime.SpawnedCloneIds.Count; i++)
            {
                DespawnCombatClone(runtime.SpawnedCloneIds[i]);
            }

            runtime.SpawnedCloneIds.Clear();
        }

        private void DespawnCombatClone(NetworkId cloneId)
        {
            if (Runner == null)
                return;

            if (!Runner.TryFindObject(cloneId, out NetworkObject obj))
                return;

            if (obj.TryGetComponent(out UnitController unit))
            {
                unit.RemoveFromBoard();
            }

            Runner.Despawn(obj);
        }

        private HexCoord Rotate180(HexCoord source)
        {
            return new HexCoord(
                BoardManager.BoardWidth - 1 - source.Q,
                BoardManager.BoardHeight - 1 - source.R
            );
        }

        private int GetLocalPlayerViewBoardIndex()
        {
            if (Runner == null)
                return -1;

            if (!TryGetLocalPlayerBoardIndex(out byte playerBoardIndex))
                return -1;

            if (FlowState != GameFlowState.Combat)
                return playerBoardIndex;

            byte mappedBoardIndex = CombatViewBoardByPlayerBoard[playerBoardIndex];
            if (mappedBoardIndex == InvalidBoardIndex)
                return playerBoardIndex;

            return mappedBoardIndex;
        }

        private float GetLocalPlayerCombatCameraZRotation()
        {
            if (!TryGetLocalPlayerBoardIndex(out byte playerBoardIndex))
                return 0f;

            if (FlowState != GameFlowState.Combat)
                return 0f;

            return CombatViewRotatedByPlayerBoard[playerBoardIndex] ? 180f : 0f;
        }

        private bool TryGetLocalPlayerBoardIndex(out byte boardIndex)
        {
            boardIndex = 0;
            int localBoardIndex = GetLocalPlayerBoardIndex();
            if (localBoardIndex < 0 || localBoardIndex > byte.MaxValue)
                return false;

            boardIndex = (byte)localBoardIndex;
            return true;
        }

        private void ResetCombatViewMappings()
        {
            if (!HasStateAuthority)
                return;

            for (byte i = 0; i < MaxPlayerCount; i++)
            {
                CombatViewBoardByPlayerBoard.Set(i, i < BoardCount ? i : InvalidBoardIndex);
                CombatViewRotatedByPlayerBoard.Set(i, false);
            }

            _lastAppliedBoardIndex = -1;
            _lastAppliedCameraZRotation = float.NaN;
        }

        private void SetCombatViewForBoard(byte playerBoardIndex, byte viewBoardIndex, bool rotated)
        {
            if (!HasStateAuthority)
                return;

            CombatViewBoardByPlayerBoard.Set(playerBoardIndex, viewBoardIndex);
            CombatViewRotatedByPlayerBoard.Set(playerBoardIndex, rotated);
        }

        [ContextMenu("Debug Prepare Combat A vs B")]
        public void DebugPrepareCombat()
        {
            if (!HasStateAuthority || Runner == null) return;

            var players = Runner.ActivePlayers.ToList();
            if (players.Count < 2) return;

            ClearActiveCombatPairs();
            ResetCombatViewMappings();

            if (PrepareCombatPair(players[0], players[1]))
            {
                FlowState = GameFlowState.Combat;
            }
        }

        [ContextMenu("Debug Restore Combat Round")]
        public void DebugRestoreCombat()
        {
            if (!HasStateAuthority) return;
            RestoreCombatRound();
        }
    }
}
