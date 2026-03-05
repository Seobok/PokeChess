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

    /// <summary>
    /// 게임 라운드 플로우를 관리하고, 접속 인원 수에 맞춰 지정된 위치들에 Hex 보드를 생성합니다.
    /// 타일은 네트워크 스폰이 아니라 로컬 생성이므로, 서버가 BoardCount를 Networked로 전파하고
    /// 모든 클라이언트가 동일하게 로컬 생성합니다.
    /// </summary>
    public class GameFlowManager : NetworkBehaviour
    {
        private const int MaxPlayerCount = 8;

        [Header("Board")]
        [SerializeField] private HexBoardGenerator boardGenerator;
        [SerializeField] private bool generateBoardOnSpawn = true;

        [Header("Camera")]
        [SerializeField] private Camera targetCamera;
        [SerializeField] private Vector3 cameraOffset = new(0f, 0f, -10f);

        [Tooltip("다른 스크립트(또는 Cinemachine)가 카메라를 덮어쓰는 경우를 대비해 LateUpdate에서 매 프레임 적용합니다.")]
        [SerializeField] private bool applyCameraEveryFrame = true;

        [Header("최대 8개 보드 시작 위치")]
        [SerializeField] private List<Vector3> boardOrigins = new();

        [Networked] public GameFlowState FlowState { get; private set; }

        // ✅ 서버가 결정해서 모든 클라이언트에게 공유하는 보드 개수
        [Networked] public byte BoardCount { get; private set; }

        private int _lastAppliedBoardIndex = -1;
        private int _lastGeneratedBoardCount = -1;
        private int _lastConnectedPlayerCount = -1;

        // 카메라가 이미 목적지에 붙어있으면 불필요한 set 방지
        private const float CameraSnapEpsilonSqr = 0.0001f;

        public override void Spawned()
        {
            EnsureBoardGenerator();

            // 서버는 시작 시 BoardCount 설정 + Preparation 전환
            if (HasStateAuthority && generateBoardOnSpawn)
            {
                StartFlowForConnectedPlayers();
            }

            // ✅ Host/Client 모두: BoardCount가 유효하면 로컬로 보드 생성
            TryGenerateBoardsIfNeeded();

            // ✅ 최초 1회 강제 포커스
            TryFocusLocalCameraToAssignedBoard(force: true);
        }

        public override void FixedUpdateNetwork()
        {
            // 서버: Preparation 단계에서는 접속 인원 변화에 따라 BoardCount 갱신
            if (HasStateAuthority && FlowState == GameFlowState.Preparation)
            {
                UpdateBoardCountIfNeeded();
            }

            // Host/Client 모두: BoardCount 변화가 오면 로컬 보드 재생성
            TryGenerateBoardsIfNeeded();

            // 네트워크 틱에서도 한 번 시도 (하지만 실제 카메라 덮어쓰기 문제는 LateUpdate가 더 강함)
            if (!applyCameraEveryFrame)
                TryFocusLocalCameraToAssignedBoard();
        }

        // ✅ 중요: 다른 스크립트가 Update에서 카메라를 바꾸더라도, LateUpdate에서 마지막으로 덮어쓴다.
        private void LateUpdate()
        {
            if (!applyCameraEveryFrame) return;
            if (Runner == null) return;
            if (Runner.State != NetworkRunner.States.Running) return;

            TryFocusLocalCameraToAssignedBoard();
        }

        /// <summary>
        /// 현재 접속한 플레이어 수를 기준으로 "보드 개수"를 결정하고 준비 단계로 전환합니다.
        /// (보드 생성 자체는 모든 클라이언트가 BoardCount를 보고 로컬로 수행)
        /// </summary>
        public void StartFlowForConnectedPlayers()
        {
            if (!HasStateAuthority)
            {
                Debug.LogWarning("[GameFlowManager] HasStateAuthority is false");
                return;
            }

            UpdateBoardCountIfNeeded(force: true);
            FlowState = GameFlowState.Preparation;
        }

        public void StartCombat()
        {
            if (!HasStateAuthority) return;
            FlowState = GameFlowState.Combat;
        }

        public void FinishRound()
        {
            if (!HasStateAuthority) return;
            FlowState = GameFlowState.Result;
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

                // 보드 개수 바뀌면 보드 재생성/카메라 재포커스 유도
                _lastGeneratedBoardCount = -1;
                _lastAppliedBoardIndex = -1;
            }
        }

        private int GetConnectedPlayerCount()
        {
            if (Runner == null) return 0;

            int count = 0;
            foreach (PlayerRef _ in Runner.ActivePlayers) count++;
            return Mathf.Min(count, MaxPlayerCount);
        }

        private void EnsureBoardGenerator()
        {
            if (boardGenerator != null) return;

            boardGenerator = FindAnyObjectByType<HexBoardGenerator>();
            if (boardGenerator == null)
            {
                Debug.LogError("[GameFlowManager] HexBoardGenerator is missing in scene. Please place it in the scene and assign tilePrefab.", this);
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

            // 생성/재생성 되었으면 카메라도 다시 맞추게 리셋
            _lastAppliedBoardIndex = -1;
        }

        private void TryFocusLocalCameraToAssignedBoard(bool force = false)
        {
            if (Runner == null) return;
            if (BoardCount <= 0) return;

            int localBoardIndex = GetLocalPlayerBoardIndex();
            if (localBoardIndex < 0) return;
            if (boardOrigins == null || localBoardIndex >= boardOrigins.Count) return;

            // 카메라 확실히 찾기
            if (targetCamera == null || !targetCamera.isActiveAndEnabled)
            {
                targetCamera = FindBestCamera();
            }
            if (targetCamera == null) return;

            EnsureBoardGenerator();
            if (boardGenerator == null) return;

            Vector3 boardCenter = boardGenerator.GetBoardCenter(boardOrigins[localBoardIndex]);
            Vector3 desired = boardCenter + cameraOffset;

            // 같은 보드로 이미 적용했고, 현재 위치도 거의 같으면 스킵
            if (!force && localBoardIndex == _lastAppliedBoardIndex)
            {
                if ((targetCamera.transform.position - desired).sqrMagnitude < CameraSnapEpsilonSqr)
                    return;
            }

            targetCamera.transform.position = desired;
            _lastAppliedBoardIndex = localBoardIndex;
        }

        private Camera FindBestCamera()
        {
            // 1) MainCamera 태그가 있으면 최우선
            var cam = Camera.main;
            if (cam != null && cam.isActiveAndEnabled)
                return cam;

            // 2) 씬의 활성 카메라 중 하나를 선택
            var cams = FindObjectsByType<Camera>(FindObjectsSortMode.None);
            for (int i = 0; i < cams.Length; i++)
            {
                if (cams[i] != null && cams[i].isActiveAndEnabled)
                    return cams[i];
            }

            return null;
        }

        // ✅ 지금 단계에서는 기존 방식(ActivePlayers 정렬)이 가장 단순/실용적
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
    }
}