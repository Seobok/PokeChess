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

        [Header("최대 8개 보드 시작 위치")]
        [SerializeField] private List<Vector3> boardOrigins = new();

        [Networked] public GameFlowState FlowState { get; private set; }

        private int _lastAppliedBoardIndex = -1;

        public override void Spawned()
        {
            if (HasStateAuthority && generateBoardOnSpawn)
            {
                StartFlowForConnectedPlayers();
            }

            TryFocusLocalCameraToAssignedBoard();
        }

        public override void FixedUpdateNetwork()
        {
            TryFocusLocalCameraToAssignedBoard();
        }

        /// <summary>
        /// 현재 접속한 플레이어 수를 기준으로 보드를 생성하고 준비 단계로 전환합니다.
        /// </summary>
        public void StartFlowForConnectedPlayers()
        {
            if (HasStateAuthority == false)
            {
                Debug.LogWarning("HasStateAuthority is false");
                return;
            }

            EnsureBoardGenerator();
            int connectedPlayerCount = GetConnectedPlayerCount();
            boardGenerator.GenerateBoardsAt(boardOrigins, connectedPlayerCount);
            FlowState = GameFlowState.Preparation;
        }

        public void StartCombat()
        {
            if (HasStateAuthority == false)
            {
                return;
            }

            FlowState = GameFlowState.Combat;
        }

        public void FinishRound()
        {
            if (HasStateAuthority == false)
            {
                return;
            }

            FlowState = GameFlowState.Result;
        }

        private int GetConnectedPlayerCount()
        {
            if (Runner == null)
            {
                return 0;
            }

            int count = 0;
            foreach (PlayerRef _ in Runner.ActivePlayers)
            {
                count++;
            }

            return Mathf.Min(count, MaxPlayerCount);
        }

        private void EnsureBoardGenerator()
        {
            if (boardGenerator != null)
            {
                return;
            }

            boardGenerator = GetComponent<HexBoardGenerator>();
            if (boardGenerator == null)
            {
                boardGenerator = gameObject.AddComponent<HexBoardGenerator>();
            }
        }

        private void TryFocusLocalCameraToAssignedBoard()
        {
            if (Runner == null)
            {
                return;
            }

            int localBoardIndex = GetLocalPlayerBoardIndex();
            if (localBoardIndex < 0 || localBoardIndex >= boardOrigins.Count || localBoardIndex == _lastAppliedBoardIndex)
            {
                return;
            }

            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }

            if (targetCamera == null)
            {
                return;
            }

            EnsureBoardGenerator();
            Vector3 boardCenter = boardGenerator.GetBoardCenter(boardOrigins[localBoardIndex]);
            targetCamera.transform.position = boardCenter + cameraOffset;
            _lastAppliedBoardIndex = localBoardIndex;
        }

        private int GetLocalPlayerBoardIndex()
        {
            if (Runner == null)
            {
                return -1;
            }

            PlayerRef localPlayer = Runner.LocalPlayer;
            if (localPlayer == PlayerRef.None)
            {
                return -1;
            }

            List<PlayerRef> orderedPlayers = Runner.ActivePlayers.OrderBy(player => player.AsIndex).ToList();
            for (int i = 0; i < orderedPlayers.Count; i++)
            {
                if (orderedPlayers[i] == localPlayer)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
