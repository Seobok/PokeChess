using System.Collections.Generic;
using Fusion;
using UnityEngine;

namespace PokeChess.Autobattler
{
    /// <summary>
    /// Owns replicated unit state, board movement, combat logic, and visual syncing.
    /// Skills are delegated to UnitSkillDefinition assets referenced by UnitStats.
    /// </summary>
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
        [Networked] public float HP { get; private set; }
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
        private float _pendingCloneHp;
        private int _pendingCloneMana;

        [Networked] private int NextMoveTick { get; set; }

        private GameFlowManager _flow;
        private HexCoord _lastVisualCell;
        private byte _lastVisualBoard;
        private bool _lastHiddenState;

        private bool _lerping;
        private float _lerpStartTime;
        private Vector3 _lerpFrom;
        private Vector3 _lerpTo;

        private Renderer[] _renderers;
        private Collider[] _colliders3D;
        private Collider2D[] _colliders2D;

        public bool IsNetworkStateReady { get; private set; }
        public UnitStats Stats => stats;
        public UnitSkillDefinition Skill => stats != null ? stats.skill : null;
        public float MaxHp => stats != null ? Mathf.Max(1f, stats.maxHp) : 1f;
        public int MaxMana => stats != null ? Mathf.Max(0, stats.maxMana) : 0;
        public float HealthNormalized => !IsNetworkStateReady || MaxHp <= 0f ? 0f : Mathf.Clamp01(HP / MaxHp);
        public float ManaNormalized => !IsNetworkStateReady || MaxMana <= 0 ? 0f : Mathf.Clamp01((float)Mana / MaxMana);

        public void SetPendingInit(byte boardIndex, HexCoord cell, bool requireDeployZone = true)
        {
            _pendingInit = true;
            _pendingBoardIndex = boardIndex;
            _pendingCell = cell;
            _pendingRequireDeployZone = requireDeployZone;
        }

        public void SetPendingCombatClone(NetworkId sourceUnitId, float hp, int mana)
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
            CacheVisualComponents();

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

            IsNetworkStateReady = true;
            SnapToCellImmediate();
            _lastVisualCell = Cell;
            _lastVisualBoard = BoardIndex;
            ApplyVisibility(force: true);
        }

        private void OnDisable()
        {
            IsNetworkStateReady = false;
        }

        private void OnDestroy()
        {
            IsNetworkStateReady = false;
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority)
                return;

            _flow ??= FindAnyObjectByType<GameFlowManager>();
            if (_flow != null && !_flow.ShouldUnitSimulate(this))
                return;

            if (HP <= 0f)
                return;

            if (_flow != null && _flow.FlowState == GameFlowState.Combat)
            {
                TickCombatBehaviour();
                return;
            }

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

        private void TickCombatBehaviour()
        {
            if (!IsCombatClone || boardManager == null)
                return;

            UnitController currentTarget = GetCurrentValidTarget();
            int attackRange = GetAttackRange();

            if (currentTarget != null)
            {
                if (IsWithinAttackRange(currentTarget, attackRange))
                {
                    TryAttack(currentTarget);
                    return;
                }

                if (Runner.Tick < NextMoveTick)
                    return;

                if (TryMoveOneStepToward(currentTarget))
                {
                    NextMoveTick = Runner.Tick + GetMoveTickInterval();
                    AssignAttackableTargetAfterMovement();
                    return;
                }

                TargetId = default;
                currentTarget = null;
            }

            UnitController attackableEnemy = FindClosestAttackableEnemy();
            if (attackableEnemy != null)
            {
                TargetId = attackableEnemy.Object.Id;
                TryAttack(attackableEnemy);
                return;
            }

            UnitController nearestEnemy = FindClosestEnemy();
            if (nearestEnemy == null)
                return;

            TargetId = nearestEnemy.Object.Id;

            if (Runner.Tick < NextMoveTick)
                return;

            if (TryMoveOneStepToward(nearestEnemy))
            {
                NextMoveTick = Runner.Tick + GetMoveTickInterval();
                AssignAttackableTargetAfterMovement();
                return;
            }

            TargetId = default;
        }

        private void AssignAttackableTargetAfterMovement()
        {
            UnitController attackableEnemy = FindClosestAttackableEnemy();
            if (attackableEnemy != null)
            {
                TargetId = attackableEnemy.Object.Id;
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

        public bool RegisterOnBoard(bool requireDeployZone = false)
        {
            if (!HasStateAuthority || boardManager == null)
                return false;

            return requireDeployZone
                ? boardManager.TryDeployUnit(Object.Id, BoardIndex, Cell)
                : boardManager.TryOccupyUnit(Object.Id, BoardIndex, Cell);
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
            ApplyVisibility();

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

        public bool ReceiveDamage(float amount, UnitController source = null)
        {
            if (!HasStateAuthority)
                return false;

            float applied = Mathf.Max(0f, amount);
            if (applied <= 0f || HP <= 0f)
                return false;

            GainMana(GetManaGainOnHit(), source);

            HP = Mathf.Max(0f, HP - applied);
            if (HP <= 0f)
            {
                HP = 0f;
                HandleDeath();
            }

            return true;
        }

        public bool RestoreHealth(float amount)
        {
            if (!HasStateAuthority)
                return false;

            float applied = Mathf.Max(0f, amount);
            if (applied <= 0f || HP <= 0f)
                return false;

            float nextHp = Mathf.Min(MaxHp, HP + applied);
            bool changed = nextHp > HP;
            HP = nextHp;
            return changed;
        }

        public bool GainMana(int amount, UnitController skillTarget = null)
        {
            if (!HasStateAuthority)
                return false;

            int applied = Mathf.Max(0, amount);
            if (applied <= 0 || MaxMana <= 0 || HP <= 0f)
                return false;

            Mana = Mathf.Max(0, Mana + applied);
            if (Mana < MaxMana)
                return true;

            ResolveManaThreshold();
            return true;
        }

        private void ResolveManaThreshold()
        {
            if (Mana < MaxMana)
                return;

            UnitController preferredTarget = GetCurrentValidTarget();

            // Reaching max mana immediately attempts skill usage, and failure still consumes mana.
            Mana = 0;
            TryCastSkill(preferredTarget);
        }

        private bool TryCastSkill(UnitController preferredTarget)
        {
            UnitSkillDefinition skill = Skill;
            if (skill == null)
                return false;

            return skill.TryCast(this, preferredTarget);
        }

        private void ApplySpawnState()
        {
            TargetId = default;
            AttackCooldown = TickTimer.None;
            NextMoveTick = default;

            if (_pendingCombatClone)
            {
                IsCombatClone = true;
                SourceUnitId = _pendingCloneSourceUnitId;
                HP = Mathf.Clamp(_pendingCloneHp, 0f, MaxHp);
                Mana = Mathf.Clamp(_pendingCloneMana, 0, MaxMana);
                _pendingCombatClone = false;
                return;
            }

            IsCombatClone = false;
            SourceUnitId = default;
            HP = MaxHp;
            Mana = 0;
        }

        public UnitController GetCurrentValidTarget()
        {
            if (Runner == null || TargetId == default)
                return null;

            if (!Runner.TryFindObject(TargetId, out NetworkObject currentTargetObject))
                return null;

            if (!currentTargetObject.TryGetComponent(out UnitController currentTarget))
                return null;

            return IsEnemyCombatUnit(currentTarget) ? currentTarget : null;
        }

        private UnitController FindClosestAttackableEnemy()
        {
            return FindClosestEnemy(maxDistance: GetAttackRange(), requireAttackRange: true);
        }

        private UnitController FindClosestEnemy(int maxDistance = int.MaxValue, bool requireAttackRange = false)
        {
            var allUnits = FindObjectsByType<UnitController>(FindObjectsSortMode.None);
            UnitController best = null;
            int bestDistance = int.MaxValue;

            for (int i = 0; i < allUnits.Length; i++)
            {
                UnitController candidate = allUnits[i];
                if (!IsEnemyCombatUnit(candidate))
                    continue;

                int distance = HexCoord.Distance(Cell, candidate.Cell);
                if (distance > maxDistance)
                    continue;

                if (requireAttackRange && !IsWithinAttackRange(candidate, maxDistance))
                    continue;

                if (distance < bestDistance)
                {
                    best = candidate;
                    bestDistance = distance;
                }
            }

            return best;
        }

        public UnitController FindClosestEnemyInRange(int range)
        {
            if (range < 0)
                return null;

            return FindClosestEnemy(maxDistance: range);
        }

        public bool IsEnemyInRange(UnitController target, int range)
        {
            if (!IsEnemyCombatUnit(target))
                return false;

            return IsWithinRange(target, range);
        }

        private bool IsEnemyCombatUnit(UnitController candidate)
        {
            if (candidate == null || candidate == this)
                return false;

            if (!candidate.IsNetworkStateReady || candidate.Object == null)
                return false;

            if (!candidate.IsCombatClone || candidate.BoardIndex != BoardIndex || candidate.HP <= 0f)
                return false;

            return GetCombatTeamBoardIndex(candidate) != GetCombatTeamBoardIndex(this);
        }

        private byte GetCombatTeamBoardIndex(UnitController unit)
        {
            if (unit == null || !unit.IsNetworkStateReady || unit.Object == null)
                return byte.MaxValue;

            if (!unit.IsCombatClone)
                return unit.BoardIndex;

            if (unit.Runner != null
                && unit.SourceUnitId != default
                && unit.Runner.TryFindObject(unit.SourceUnitId, out NetworkObject sourceObject)
                && sourceObject.TryGetComponent(out UnitController sourceUnit)
                && sourceUnit.IsNetworkStateReady)
            {
                return sourceUnit.BoardIndex;
            }

            return unit.BoardIndex;
        }

        private bool TryAttack(UnitController target)
        {
            if (target == null || !IsEnemyCombatUnit(target))
                return false;

            if (!IsWithinAttackRange(target))
                return false;

            if (!AttackCooldown.ExpiredOrNotRunning(Runner))
                return false;

            float damage = GetAttackDamageAgainst(target);
            if (!target.ReceiveDamage(damage, this))
                return false;

            GainMana(GetManaGainOnAttack(), target);
            AttackCooldown = TickTimer.CreateFromSeconds(Runner, GetAttackIntervalSeconds());
            return true;
        }

        private float GetAttackDamageAgainst(UnitController target)
        {
            float rawDamage = stats != null ? stats.attackPower : 1f;
            return CalculateDamageAfterResistance(target, rawDamage, DamageType.Physical);
        }

        public bool TryApplyDamage(UnitController target, float rawDamage, DamageType damageType)
        {
            if (!HasStateAuthority || !IsEnemyCombatUnit(target))
                return false;

            float finalDamage = CalculateDamageAfterResistance(target, rawDamage, damageType);
            return target.ReceiveDamage(finalDamage, this);
        }

        private float CalculateDamageAfterResistance(UnitController target, float rawDamage, DamageType damageType)
        {
            float resistance = 0f;
            if (target != null && target.stats != null)
            {
                resistance = damageType == DamageType.Physical
                    ? target.stats.armor
                    : target.stats.magicResist;
            }

            return ApplyResistance(rawDamage, resistance);
        }

        private float ApplyResistance(float rawDamage, float resistance)
        {
            float clampedRawDamage = Mathf.Max(0f, rawDamage);
            if (clampedRawDamage <= 0f)
                return 0f;

            if (resistance >= 0f)
            {
                return clampedRawDamage / (1f + resistance * 0.01f);
            }

            return clampedRawDamage * (2f - 1f / (1f - resistance * 0.01f));
        }

        public bool IsWithinRange(UnitController target, int range)
        {
            if (range < 0)
                return false;

            return IsWithinAttackRange(target, range);
        }

        private bool IsWithinAttackRange(UnitController target, int? overrideRange = null)
        {
            if (target == null || !target.IsNetworkStateReady || !IsNetworkStateReady)
                return false;

            int range = overrideRange ?? GetAttackRange();
            if (range < 0)
                return false;

            return HexCoord.Distance(Cell, target.Cell) <= range;
        }

        private float GetAttackIntervalSeconds()
        {
            float attackSpeed = stats != null ? stats.attackSpeed : 1f;
            if (attackSpeed <= 0f)
                return 1f;

            return 1f / attackSpeed;
        }

        private int GetAttackRange()
        {
            return Mathf.Max(1, stats != null ? stats.attackRange : 1);
        }

        private int GetMoveTickInterval()
        {
            float moveSpeed = stats != null ? stats.moveSpeed : 1f;
            if (moveSpeed <= 0f)
                return int.MaxValue / 4;

            return GetTicksForSeconds(1f / moveSpeed);
        }

        private int GetManaGainOnAttack()
        {
            return stats != null ? Mathf.Max(0, stats.manaGainOnAttack) : 20;
        }

        private int GetManaGainOnHit()
        {
            return stats != null ? Mathf.Max(0, stats.manaGainOnHit) : 10;
        }

        private bool TryMoveOneStepToward(UnitController target)
        {
            if (target == null || boardManager == null)
                return false;

            if (!TryGetBestPathStepToward(target, out HexCoord nextStep))
                return false;

            return TryMoveOneStep(nextStep);
        }

        private bool TryGetBestPathStepToward(UnitController target, out HexCoord nextStep)
        {
            nextStep = default;

            int attackRange = GetAttackRange();
            int bestPathLength = int.MaxValue;
            int bestTargetDistance = int.MaxValue;
            List<HexCoord> bestPath = null;

            for (int r = 0; r < BoardManager.BoardHeight; r++)
            {
                for (int q = 0; q < BoardManager.BoardWidth; q++)
                {
                    HexCoord candidateCell = new HexCoord(q, r);
                    int targetDistance = HexCoord.Distance(candidateCell, target.Cell);
                    if (targetDistance > attackRange)
                        continue;

                    if (candidateCell != Cell && boardManager.IsOccupied(BoardIndex, candidateCell))
                        continue;

                    if (!HexPathfinder.TryFindPath(boardManager, BoardIndex, Cell, candidateCell, out List<HexCoord> path))
                        continue;

                    if (path.Count < 2)
                        continue;

                    if (path.Count < bestPathLength
                        || (path.Count == bestPathLength && targetDistance < bestTargetDistance))
                    {
                        bestPath = path;
                        bestPathLength = path.Count;
                        bestTargetDistance = targetDistance;
                    }
                }
            }

            if (bestPath == null || bestPath.Count < 2)
                return false;

            nextStep = bestPath[1];
            return true;
        }

        private void HandleDeath()
        {
            TargetId = default;
            AttackCooldown = TickTimer.None;
            NextMoveTick = default;

            if (boardManager != null)
            {
                boardManager.RemoveUnit(BoardIndex, Cell);
            }

            if (Object != null && Runner != null)
            {
                Runner.Despawn(Object);
            }
        }

        private void CacheVisualComponents()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
            _colliders3D = GetComponentsInChildren<Collider>(true);
            _colliders2D = GetComponentsInChildren<Collider2D>(true);
        }

        private void ApplyVisibility(bool force = false)
        {
            _flow ??= FindAnyObjectByType<GameFlowManager>();
            bool hidden = _flow != null && !_flow.ShouldUnitBeVisible(this);
            if (!force && hidden == _lastHiddenState)
                return;

            _lastHiddenState = hidden;

            if (_renderers != null)
            {
                for (int i = 0; i < _renderers.Length; i++)
                {
                    if (_renderers[i] != null)
                        _renderers[i].enabled = !hidden;
                }
            }

            if (_colliders3D != null)
            {
                for (int i = 0; i < _colliders3D.Length; i++)
                {
                    if (_colliders3D[i] != null)
                        _colliders3D[i].enabled = !hidden;
                }
            }

            if (_colliders2D != null)
            {
                for (int i = 0; i < _colliders2D.Length; i++)
                {
                    if (_colliders2D[i] != null)
                        _colliders2D[i].enabled = !hidden;
                }
            }
        }
    }
}
