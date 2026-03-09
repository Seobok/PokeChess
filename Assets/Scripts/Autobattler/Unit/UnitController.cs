using System.Collections.Generic;
using Fusion;
using UnityEngine;

namespace PokeChess.Autobattler
{
    public class UnitController : NetworkBehaviour
    {
        [SerializeField] private UnitStats stats;
        [SerializeField] private BoardManager boardManager;
        [SerializeField] private bool autoMoveToDebugTarget;
        [SerializeField] private Vector2Int debugTargetCoord = new(0, 0);

        [Header("Visual Move")]
        [SerializeField] private float secondsPerStep = 1f;
        [SerializeField] private float unitZ = -1f;

        [Networked] public HexCoord Cell { get; private set; }
        [Networked] public int HP { get; private set; }
        [Networked] public int Mana { get; private set; }
        [Networked] public NetworkId TargetId { get; private set; }
        [Networked] public TickTimer AttackCooldown { get; private set; }
        [Networked] public byte BoardIndex { get; private set; }
        [Networked] public NetworkBool IsCombatClone { get; private set; }
        [Networked] public NetworkId SourceUnitId { get; private set; }

        private bool _pendingInit;
        private byte _pendingBoardIndex;
        private HexCoord _pendingCell;
        private bool _pendingRequireDeployZone = true;

        private bool _pendingCombatClone;
        private NetworkId _pendingCloneSourceUnitId;
        private int _pendingCloneHp;
        private int _pendingCloneMana;

        [Networked] private int NextMoveTick { get; set; }

        private GameFlowManager _flow;
        private HexCoord _lastVisualCell;
        private byte _lastVisualBoard;

        private bool _lerping;
        private float _lerpStartTime;
        private Vector3 _lerpFrom;
        private Vector3 _lerpTo;

        public void SetPendingInit(byte boardIndex, HexCoord cell, bool requireDeployZone = true)
        {
            _pendingInit = true;
            _pendingBoardIndex = boardIndex;
            _pendingCell = cell;
            _pendingRequireDeployZone = requireDeployZone;
        }

        public void SetPendingCombatClone(NetworkId sourceUnitId, int hp, int mana)
        {
            _pendingCombatClone = true;
            _pendingCloneSourceUnitId = sourceUnitId;
            _pendingCloneHp = hp;
            _pendingCloneMana = mana;
        }

        public override void Spawned()
        {
            boardManager ??= FindAnyObjectByType<BoardManager>();
            _flow ??= FindAnyObjectByType<GameFlowManager>();

            if (HasStateAuthority)
            {
                if (_pendingInit)
                {
                    bool ok = InitializeAt(_pendingBoardIndex, _pendingCell, _pendingRequireDeployZone);
                    _pendingInit = false;

                    if (!ok)
                    {
                        Debug.LogWarning($"[UnitController] InitializeAt failed in Spawned. board={_pendingBoardIndex}, cell={_pendingCell}", this);
                        Runner.Despawn(Object);
                        return;
                    }
                }

                ApplySpawnState();
            }

            SnapToCellImmediate();
            _lastVisualCell = Cell;
            _lastVisualBoard = BoardIndex;
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority)
                return;

            _flow ??= FindAnyObjectByType<GameFlowManager>();
            if (_flow != null && !_flow.ShouldUnitSimulate(this))
                return;

            if (Runner.Tick < NextMoveTick)
                return;

            if (autoMoveToDebugTarget)
            {
                bool moved = TryMoveOneStep(new HexCoord(debugTargetCoord.x, debugTargetCoord.y));
                if (moved)
                {
                    NextMoveTick = Runner.Tick + GetTicksForSeconds(secondsPerStep);
                }
            }
        }

        private int GetTicksForSeconds(float seconds)
        {
            float dt = Runner.DeltaTime;
            if (dt <= 0f) return 1;
            return Mathf.Max(1, Mathf.RoundToInt(seconds / dt));
        }

        public bool InitializeAt(byte boardIndex, HexCoord spawnCell, bool requireDeployZone = true)
        {
            if (!HasStateAuthority || boardManager == null)
                return false;

            bool occupied = requireDeployZone
                ? boardManager.TryDeployUnit(Object.Id, boardIndex, spawnCell)
                : boardManager.TryOccupyUnit(Object.Id, boardIndex, spawnCell);

            if (!occupied)
                return false;

            BoardIndex = boardIndex;
            Cell = spawnCell;
            TargetId = default;
            AttackCooldown = TickTimer.None;
            SnapToCellImmediate();
            return true;
        }

        public bool TryMoveOneStep(HexCoord targetCell)
        {
            if (!HasStateAuthority || boardManager == null)
                return false;

            if (!HexPathfinder.TryFindPath(boardManager, BoardIndex, Cell, targetCell, out List<HexCoord> path))
                return false;

            if (path.Count < 2)
                return false;

            HexCoord next = path[1];
            if (!boardManager.TryMoveUnit(BoardIndex, Cell, next))
                return false;

            Cell = next;
            return true;
        }

        private void Update()
        {
            if (_lastVisualBoard != BoardIndex || _lastVisualCell != Cell)
            {
                StartVisualLerpToCell();
                _lastVisualBoard = BoardIndex;
                _lastVisualCell = Cell;
            }

            if (_lerping)
            {
                float t = (Time.time - _lerpStartTime) / Mathf.Max(0.0001f, secondsPerStep);
                if (t >= 1f)
                {
                    transform.position = _lerpTo;
                    _lerping = false;
                }
                else
                {
                    transform.position = Vector3.Lerp(_lerpFrom, _lerpTo, t);
                }
            }
        }

        private void SnapToCellImmediate()
        {
            if (_flow == null) _flow = FindAnyObjectByType<GameFlowManager>();
            if (_flow == null) return;

            var position = _flow.GetCellWorldPosition(BoardIndex, Cell);
            position.z = unitZ;
            transform.position = position;
        }

        private void StartVisualLerpToCell()
        {
            if (_flow == null) _flow = FindAnyObjectByType<GameFlowManager>();
            if (_flow == null) return;

            _lerping = true;
            _lerpStartTime = Time.time;
            _lerpFrom = transform.position;
            _lerpTo = _flow.GetCellWorldPosition(BoardIndex, Cell);
            _lerpTo.z = unitZ;
        }

        public bool ForceRelocateToBoard(byte targetBoardIndex, HexCoord targetCell)
        {
            if (!HasStateAuthority || boardManager == null)
                return false;

            if (!boardManager.TryTransferUnit(Object.Id, BoardIndex, Cell, targetBoardIndex, targetCell))
                return false;

            BoardIndex = targetBoardIndex;
            Cell = targetCell;
            TargetId = default;
            AttackCooldown = TickTimer.None;
            return true;
        }

        public bool RemoveFromBoard()
        {
            if (!HasStateAuthority || boardManager == null)
                return false;

            return boardManager.RemoveUnit(BoardIndex, Cell);
        }

        private void ApplySpawnState()
        {
            TargetId = default;
            AttackCooldown = TickTimer.None;

            if (_pendingCombatClone)
            {
                IsCombatClone = true;
                SourceUnitId = _pendingCloneSourceUnitId;
                HP = Mathf.Max(0, _pendingCloneHp);
                Mana = Mathf.Max(0, _pendingCloneMana);
                _pendingCombatClone = false;
                return;
            }

            IsCombatClone = false;
            SourceUnitId = default;
            HP = stats != null ? stats.maxHp : 0;
            Mana = 0;
        }
    }
}
