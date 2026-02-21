using System.Collections.Generic;
using Fusion;
using UnityEngine;

namespace PokeChess.Autobattler
{
    /// <summary>
    /// 서버 권한 기준 유닛 상태와 단일 이동(한 Tick 한 칸)을 처리합니다.
    /// </summary>
    public class UnitController : NetworkBehaviour
    {
        [SerializeField] private UnitStats stats;
        [SerializeField] private BoardManager boardManager;
        [SerializeField] private bool autoMoveToDebugTarget;
        [SerializeField] private Vector2Int debugTargetCoord = new(0, 0);

        [Networked] public HexCoord Cell { get; private set; }
        [Networked] public int HP { get; private set; }
        [Networked] public int Mana { get; private set; }
        [Networked] public NetworkId TargetId { get; private set; }
        [Networked] public TickTimer AttackCooldown { get; private set; }

        public override void Spawned()
        {
            if (boardManager == null)
            {
                boardManager = FindObjectOfType<BoardManager>();
            }

            if (HasStateAuthority == false)
            {
                return;
            }

            if (stats != null)
            {
                HP = stats.maxHp;
                Mana = 0;
            }
        }

        public override void FixedUpdateNetwork()
        {
            if (Runner.IsServer == false)
            {
                return;
            }

            if (autoMoveToDebugTarget)
            {
                TryMoveOneStep(new HexCoord(debugTargetCoord.x, debugTargetCoord.y));
            }
        }

        /// <summary>
        /// 유닛을 배치하고 동적 상태를 초기화합니다.
        /// </summary>
        public bool InitializeAt(HexCoord spawnCell)
        {
            if (HasStateAuthority == false || boardManager == null)
            {
                return false;
            }

            if (boardManager.TryDeployUnit(Object.Id, spawnCell) == false)
            {
                return false;
            }

            Cell = spawnCell;
            TargetId = default;
            AttackCooldown = TickTimer.None;
            return true;
        }

        /// <summary>
        /// 목표 좌표를 향해 A* 경로 기준으로 한 칸만 이동합니다.
        /// 경로가 없거나 막혀 있으면 이동하지 않습니다.
        /// </summary>
        public bool TryMoveOneStep(HexCoord targetCell)
        {
            if (HasStateAuthority == false || boardManager == null)
            {
                return false;
            }

            if (HexPathfinder.TryFindPath(boardManager, Cell, targetCell, out List<HexCoord> path) == false)
            {
                return false;
            }

            if (path.Count < 2)
            {
                return false;
            }

            HexCoord next = path[1];
            if (boardManager.TryMoveUnit(Cell, next) == false)
            {
                return false;
            }

            Cell = next;
            return true;
        }
    }
}
