using System.Collections.Generic;
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

        [Header("최대 8개 보드 시작 위치")]
        [SerializeField] private List<Vector3> boardOrigins = new();

        [Networked] public GameFlowState FlowState { get; private set; }

        public override void Spawned()
        {
            if (HasStateAuthority == false)
            {
                Debug.LogWarning("HasStateAuthority is false");
                return;
            }

            if (generateBoardOnSpawn)
            {
                StartFlowForConnectedPlayers();
            }
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
    }
}
