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

        // ✅ Spawn 직전(서버) 전달받는 초기화 데이터 (Networked 아님)
        private bool _pendingInit;
        private byte _pendingBoardIndex;
        private HexCoord _pendingCell;

        // ✅ 1초/칸 게이트 (서버만 의미 있음)
        [Networked] private int NextMoveTick { get; set; }

        private GameFlowManager _flow;
        private HexCoord _lastVisualCell;
        private byte _lastVisualBoard;

        private bool _lerping;
        private float _lerpStartTime;
        private Vector3 _lerpFrom;
        private Vector3 _lerpTo;

        public void SetPendingInit(byte boardIndex, HexCoord cell)
        {
            _pendingInit = true;
            _pendingBoardIndex = boardIndex;
            _pendingCell = cell;
        }

        public override void Spawned()
        {
            boardManager ??= FindAnyObjectByType<BoardManager>();
            _flow ??= FindAnyObjectByType<GameFlowManager>();

            if (HasStateAuthority)
            {
                if (_pendingInit)
                {
                    bool ok = InitializeAt(_pendingBoardIndex, _pendingCell);
                    _pendingInit = false;

                    if (!ok)
                    {
                        Debug.LogWarning($"[UnitController] InitializeAt FAILED in Spawned -> Despawn. board={_pendingBoardIndex}, cell={_pendingCell}", this);
                        Runner.Despawn(Object);
                        return;
                    }
                }

                if (stats != null)
                {
                    HP = stats.maxHp;
                    Mana = 0;
                }
            }

            // ✅ 처음 위치 스냅
            SnapToCellImmediate();

            _lastVisualCell = Cell;
            _lastVisualBoard = BoardIndex;
        }

        public override void FixedUpdateNetwork()
        {
            if (HasStateAuthority == false) return;

            // ✅ 1초/칸 제한
            if (Runner.Tick < NextMoveTick) return;

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
            // Runner.DeltaTime = 한 Tick의 시간(초)
            float dt = Runner.DeltaTime;
            if (dt <= 0f) return 1;
            return Mathf.Max(1, Mathf.RoundToInt(seconds / dt));
        }

        public bool InitializeAt(byte boardIndex, HexCoord spawnCell)
        {
            if (!HasStateAuthority || boardManager == null)
                return false;

            if (!boardManager.TryDeployUnit(Object.Id, boardIndex, spawnCell))
                return false;

            BoardIndex = boardIndex;
            Cell = spawnCell;
            TargetId = default;
            AttackCooldown = TickTimer.None;

            // 서버에서도 바로 스냅(호스트 화면)
            SnapToCellImmediate();

            return true;
        }

        public bool TryMoveOneStep(HexCoord targetCell)
        {
            if (HasStateAuthority == false || boardManager == null)
                return false;

            if (HexPathfinder.TryFindPath(boardManager, BoardIndex, Cell, targetCell, out List<HexCoord> path) == false)
                return false;

            if (path.Count < 2)
                return false;

            HexCoord next = path[1];

            if (boardManager.TryMoveUnit(BoardIndex, Cell, next) == false)
                return false;

            Cell = next; // ✅ 로직 이동(서버)
            return true;
        }

        // ✅ 시각적 이동(모든 클라이언트)
        private void Update()
        {
            // Cell/BoardIndex 변경 감지 -> 1초 동안 lerp
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

            var p = _flow.GetCellWorldPosition(BoardIndex, Cell);
            p.z = unitZ;
            transform.position = p;
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
    }
}