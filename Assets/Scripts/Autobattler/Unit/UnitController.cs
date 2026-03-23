using System.Collections.Generic;
using Fusion;
using UnityEngine;

namespace PokeChess.Autobattler
{
    public enum UnitAnimationEventType : byte
    {
        None = 0,
        Attack = 1,
        Skill = 2,
        Hit = 3
    }

    public enum UnitAnimationDirection : byte
    {
        Down = 0,
        LeftDown = 1,
        Left = 2,
        LeftUp = 3,
        Up = 4,
        RightUp = 5,
        Right = 6,
        RightDown = 7
    }

    public enum UnitAnimationClipEventType : byte
    {
        None = 0,
        RushFrame = 1,
        HitFrame = 2,
        ReturnFrame = 3
    }

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

        [Header("Animation")]
        [SerializeField] private Animator animator;
        [SerializeField] private string moveBoolParameter = "IsMoving";
        [SerializeField] private string attackTriggerParameter = "Attack";
        [SerializeField] private string skillTriggerParameter = "Skill";
        [SerializeField] private string hitTriggerParameter = "Hit";
        [SerializeField] private string deadBoolParameter = "IsDead";
        [SerializeField] private string directionIntParameter = "Direction";
        [SerializeField] private float deathDespawnDelaySeconds = 0.75f;
        [SerializeField] private string locomotionStatePrefix = "Walk_";
        [SerializeField] private string idleStatePrefix = "Idle_";

        [Networked] public HexCoord Cell { get; private set; }
        [Networked] public float HP { get; private set; }
        [Networked] public int Mana { get; private set; }
        [Networked] public NetworkId TargetId { get; private set; }
        [Networked] public TickTimer AttackCooldown { get; private set; }
        [Networked] public byte BoardIndex { get; private set; }
        [Networked] public byte UnitTypeId { get; private set; }
        [Networked] public NetworkBool IsCombatClone { get; private set; }
        [Networked] public NetworkId SourceUnitId { get; private set; }
        [Networked] private int NextMoveTick { get; set; }
        [Networked] private float BuffAttackPowerAdd { get; set; }
        [Networked] private float BuffArmorAdd { get; set; }
        [Networked] private float BuffMagicResistAdd { get; set; }
        [Networked] private float BuffAttackSpeedMul { get; set; }
        [Networked] private float BuffMoveSpeedMul { get; set; }
        [Networked] private int BuffAttackRangeAdd { get; set; }
        [Networked] private TickTimer BuffExpireTimer { get; set; }
        [Networked] private TickTimer RootExpireTimer { get; set; }
        [Networked] private TickTimer StunExpireTimer { get; set; }

        private bool _pendingInit;
        private byte _pendingBoardIndex;
        private byte _pendingUnitTypeId;
        private HexCoord _pendingCell;
        private bool _pendingRequireDeployZone = true;

        private bool _pendingCombatClone;
        private NetworkId _pendingCloneSourceUnitId;
        private float _pendingCloneHp;
        private int _pendingCloneMana;

        [Networked] private UnitAnimationEventType AnimationEvent { get; set; }
        [Networked] private int AnimationEventSequence { get; set; }
        [Networked] private byte AnimationDirectionValue { get; set; }
        [Networked] private NetworkBool IsDead { get; set; }
        [Networked] private TickTimer DeathDespawnTimer { get; set; }

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
        private int _lastConsumedAnimationEventSequence = -1;

        private int _moveBoolHash;
        private int _attackTriggerHash;
        private int _skillTriggerHash;
        private int _hitTriggerHash;
        private int _deadBoolHash;
        private int _directionIntHash;
        private int _lastLocomotionStateHash;
        private bool _hasMoveBool;
        private bool _hasAttackTrigger;
        private bool _hasSkillTrigger;
        private bool _hasHitTrigger;
        private bool _hasDeadBool;
        private bool _hasDirectionInt;

        private bool _skillMoveLocked;
        private bool _skillAttackLocked;
        private UnitSkillRuntime _activeSkillRuntime;
        private bool _locallyHiddenForPlacementDrag;

        public bool IsNetworkStateReady { get; private set; }
        public UnitStats Stats => stats;
        public UnitSkillDefinition Skill => stats != null ? stats.skill : null;
        public float MaxHp => stats != null ? Mathf.Max(1f, stats.maxHp) : 1f;
        public int MaxMana => stats != null ? Mathf.Max(0, stats.maxMana) : 0;
        public bool IsAlive => IsNetworkStateReady && HP > 0f;
        public bool IsDeadOrDying => IsDead || HP <= 0f;
        public float HealthNormalized => !IsNetworkStateReady || MaxHp <= 0f ? 0f : Mathf.Clamp01(HP / MaxHp);
        public float ManaNormalized => !IsNetworkStateReady || MaxMana <= 0 ? 0f : Mathf.Clamp01((float)Mana / MaxMana);
        public UnitAnimationDirection AnimationDirection => (UnitAnimationDirection)AnimationDirectionValue;
        public bool IsLocallyHiddenForPlacementDrag => _locallyHiddenForPlacementDrag;
        public event System.Action<UnitAnimationClipEventType> AnimationClipEventFired;

        public void SetPendingInit(byte boardIndex, HexCoord cell, byte unitTypeId, bool requireDeployZone = true)
        {
            _pendingInit = true;
            _pendingBoardIndex = boardIndex;
            _pendingUnitTypeId = unitTypeId;
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
            CacheAnimationComponents();

            if (HasStateAuthority)
            {
                if (_pendingInit)
                {
                    bool ok = InitializeAt(_pendingBoardIndex, _pendingCell, _pendingUnitTypeId, _pendingRequireDeployZone);
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
            ApplyAnimationState();
        }

        private void OnDisable()
        {
            IsNetworkStateReady = false;
            ClearSkillActionLock();
        }

        private void OnDestroy()
        {
            IsNetworkStateReady = false;
            ClearSkillActionLock();
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority)
                return;

            if (IsDead)
            {
                if (DeathDespawnTimer.ExpiredOrNotRunning(Runner) && Object != null && Runner != null)
                {
                    Runner.Despawn(Object);
                }

                return;
            }

            _flow ??= FindAnyObjectByType<GameFlowManager>();
            if (_flow != null && !_flow.ShouldUnitSimulate(this))
                return;

            UpdateBuffState();

            if (HP <= 0f)
                return;

            if (TickActiveSkillRuntime())
                return;

            if (_flow != null && _flow.FlowState == GameFlowState.Combat)
            {
                TickCombatBehaviour();
                return;
            }

            if (Runner.Tick < NextMoveTick || IsMovementLocked())
                return;

            if (autoMoveToDebugTarget)
            {
                bool moved = TryMoveOneStep(new HexCoord(debugTargetCoord.x, debugTargetCoord.y));
                if (moved)
                    NextMoveTick = Runner.Tick + GetTicksForSeconds(secondsPerStep);
            }
        }

        public bool CanContinueSkillRuntime()
        {
            return HasStateAuthority && Runner != null && Object != null && IsAlive;
        }

        public bool TryStartSkillRuntime(UnitSkillRuntime runtime)
        {
            if (!HasStateAuthority || runtime == null || _activeSkillRuntime != null || HP <= 0f)
                return false;

            if (!runtime.Start())
                return false;

            _activeSkillRuntime = runtime;
            return true;
        }

        public void SetSkillActionLock(bool lockMovement, bool lockAttack)
        {
            _skillMoveLocked = lockMovement;
            _skillAttackLocked = lockAttack;
        }

        public void ClearSkillActionLock()
        {
            _skillMoveLocked = false;
            _skillAttackLocked = false;
        }

        public bool ApplyRoot(float durationSeconds)
        {
            if (!HasStateAuthority || HP <= 0f || durationSeconds <= 0f)
                return false;

            RootExpireTimer = TickTimer.CreateFromSeconds(Runner, durationSeconds);
            return true;
        }

        public bool ApplyStun(float durationSeconds)
        {
            if (!HasStateAuthority || HP <= 0f || durationSeconds <= 0f)
                return false;

            StunExpireTimer = TickTimer.CreateFromSeconds(Runner, durationSeconds);
            return true;
        }

        private bool TickActiveSkillRuntime()
        {
            if (_activeSkillRuntime == null)
                return false;

            bool stillRunning = _activeSkillRuntime.Tick();
            if (stillRunning)
                return true;

            _activeSkillRuntime.Stop();
            _activeSkillRuntime = null;
            return false;
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
                    if (!IsAttackLocked())
                        TryAttack(currentTarget);
                    return;
                }

                if (Runner.Tick < NextMoveTick || IsMovementLocked())
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
            if (attackableEnemy != null && !IsAttackLocked())
            {
                TargetId = attackableEnemy.Object.Id;
                TryAttack(attackableEnemy);
                return;
            }

            UnitController nearestEnemy = FindClosestEnemy();
            if (nearestEnemy == null)
                return;

            TargetId = nearestEnemy.Object.Id;

            if (Runner.Tick < NextMoveTick || IsMovementLocked())
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
                TargetId = attackableEnemy.Object.Id;
        }

        private int GetTicksForSeconds(float seconds)
        {
            float dt = Runner.DeltaTime;
            if (dt <= 0f)
                return 1;

            return Mathf.Max(1, Mathf.RoundToInt(seconds / dt));
        }

        public bool InitializeAt(byte boardIndex, HexCoord spawnCell, byte unitTypeId, bool requireDeployZone = true)
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
            UnitTypeId = unitTypeId;
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

        public bool SwapPlacementWith(UnitController other)
        {
            if (!HasStateAuthority || boardManager == null || other == null || other == this)
                return false;

            if (other.boardManager != boardManager || other.BoardIndex != BoardIndex)
                return false;

            HexCoord sourceCell = Cell;
            HexCoord targetCell = other.Cell;

            if (!boardManager.TrySwapUnits(BoardIndex, sourceCell, targetCell))
                return false;

            Cell = targetCell;
            other.Cell = sourceCell;
            TargetId = default;
            other.TargetId = default;
            AttackCooldown = TickTimer.None;
            other.AttackCooldown = TickTimer.None;
            UpdateAnimationDirectionFromStep(sourceCell, targetCell);
            other.UpdateAnimationDirectionFromStep(targetCell, sourceCell);
            return true;
        }

        public void SetLocallyHiddenForPlacementDrag(bool hidden)
        {
            _locallyHiddenForPlacementDrag = hidden;
            ApplyVisibility(force: true);
        }

        public bool TryMoveOneStep(HexCoord targetCell)
        {
            if (!HasStateAuthority || boardManager == null || IsMovementLocked())
                return false;

            if (!HexPathfinder.TryFindPath(boardManager, BoardIndex, Cell, targetCell, out List<HexCoord> path))
                return false;

            if (path.Count < 2)
                return false;

            HexCoord next = path[1];
            if (!boardManager.TryMoveUnit(BoardIndex, Cell, next))
                return false;

            UpdateAnimationDirectionFromStep(Cell, next);
            Cell = next;
            return true;
        }

        private void Update()
        {
            ApplyVisibility();

            if (_lastVisualBoard != BoardIndex || _lastVisualCell != Cell)
            {
                if (_flow != null && _flow.FlowState != GameFlowState.Combat)
                {
                    SnapToCellImmediate();
                    _lerping = false;
                }
                else
                {
                    StartVisualLerpToCell();
                }

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

            ApplyAnimationState();
        }

        private void SnapToCellImmediate()
        {
            if (_flow == null)
                _flow = FindAnyObjectByType<GameFlowManager>();
            if (_flow == null)
                return;

            Vector3 position = _flow.GetCellWorldPosition(BoardIndex, Cell);
            position.z = unitZ;
            transform.position = position;
        }

        private void StartVisualLerpToCell()
        {
            if (_flow == null)
                _flow = FindAnyObjectByType<GameFlowManager>();
            if (_flow == null)
                return;

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

            HexCoord previousCell = Cell;
            byte previousBoardIndex = BoardIndex;

            if (!boardManager.TryTransferUnit(Object.Id, BoardIndex, Cell, targetBoardIndex, targetCell))
                return false;

            BoardIndex = targetBoardIndex;
            Cell = targetCell;
            TargetId = default;
            AttackCooldown = TickTimer.None;
            if (previousBoardIndex != targetBoardIndex)
            {
                ResetAnimationDirectionToDefault();
            }
            else
            {
                UpdateAnimationDirectionFromStep(previousCell, targetCell);
            }

            return true;
        }

        public bool TryTeleportBehindTarget(UnitController target)
        {
            if (!HasStateAuthority || target == null || !IsEnemyCombatUnit(target) || boardManager == null)
                return false;

            if (!TryFindCellBehindTarget(target, out HexCoord teleportCell))
                return false;

            return ForceRelocateToBoard(BoardIndex, teleportCell);
        }

        private bool TryFindCellBehindTarget(UnitController target, out HexCoord teleportCell)
        {
            teleportCell = default;
            int bestDistance = int.MinValue;
            bool found = false;

            foreach (HexCoord neighbor in boardManager.GetNeighbors(target.Cell))
            {
                if (boardManager.IsOccupied(BoardIndex, neighbor))
                    continue;

                int distanceFromCaster = HexCoord.Distance(neighbor, Cell);
                if (!found || distanceFromCaster > bestDistance)
                {
                    bestDistance = distanceFromCaster;
                    teleportCell = neighbor;
                    found = true;
                }
            }

            return found;
        }

        public void SetCurrentTarget(UnitController target)
        {
            if (target == null || !IsEnemyCombatUnit(target))
            {
                TargetId = default;
                return;
            }

            TargetId = target.Object.Id;
        }

        public void PlaySkillProjectileVisual(HexCoord from, HexCoord to, float durationSeconds)
        {
            if (!HasStateAuthority)
                return;

            RpcPlaySkillProjectileVisual(from, to, durationSeconds);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RpcPlaySkillProjectileVisual(HexCoord from, HexCoord to, float durationSeconds)
        {
            if (Skill is not IProjectileVisualSkillDefinition visualSkill || visualSkill.ProjectileViewPrefab == null)
                return;

            _flow ??= FindAnyObjectByType<GameFlowManager>();
            if (_flow == null)
                return;

            Vector3 start = _flow.GetCellWorldPosition(BoardIndex, from) + visualSkill.ProjectileViewOffset;
            Vector3 end = _flow.GetCellWorldPosition(BoardIndex, to) + visualSkill.ProjectileViewOffset;
            SkillProjectileView view = Instantiate(visualSkill.ProjectileViewPrefab, start, Quaternion.identity);
            view.Initialize(start);
            view.SetSegment(start, end, durationSeconds);
            Destroy(view.gameObject, Mathf.Max(0.5f, durationSeconds + 0.5f));
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
            else
            {
                RaiseAnimationEvent(UnitAnimationEventType.Hit);
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

        public bool ApplyBuff(IReadOnlyList<StatBuffEffect> effects, float durationSeconds)
        {
            if (!HasStateAuthority || HP <= 0f || effects == null || effects.Count == 0 || durationSeconds <= 0f)
                return false;

            ClearBuffState();

            for (int i = 0; i < effects.Count; i++)
            {
                StatBuffEffect effect = effects[i];
                float multiplier = effect.multiplier == 0f ? 1f : Mathf.Max(0f, effect.multiplier);

                switch (effect.statType)
                {
                    case BuffStatType.AttackPower:
                        BuffAttackPowerAdd += effect.additiveAmount;
                        break;
                    case BuffStatType.Armor:
                        BuffArmorAdd += effect.additiveAmount;
                        break;
                    case BuffStatType.MagicResist:
                        BuffMagicResistAdd += effect.additiveAmount;
                        break;
                    case BuffStatType.AttackSpeed:
                        BuffAttackSpeedMul *= multiplier;
                        if (BuffAttackSpeedMul <= 0f)
                            BuffAttackSpeedMul = multiplier;
                        break;
                    case BuffStatType.MoveSpeed:
                        BuffMoveSpeedMul *= multiplier;
                        if (BuffMoveSpeedMul <= 0f)
                            BuffMoveSpeedMul = multiplier;
                        break;
                    case BuffStatType.AttackRange:
                        BuffAttackRangeAdd += Mathf.RoundToInt(effect.additiveAmount);
                        break;
                }
            }

            BuffExpireTimer = TickTimer.CreateFromSeconds(Runner, durationSeconds);
            return true;
        }

        private void ResolveManaThreshold()
        {
            if (Mana < MaxMana)
                return;

            UnitController preferredTarget = GetCurrentValidTarget();
            Mana = 0;
            TryCastSkill(preferredTarget);
        }

        private bool TryCastSkill(UnitController preferredTarget)
        {
            UnitSkillDefinition skill = Skill;
            if (skill == null)
                return false;

            bool casted = skill.TryCast(this, preferredTarget);
            if (casted)
            {
                RaiseAnimationEvent(UnitAnimationEventType.Skill);
            }

            return casted;
        }

        private void ApplySpawnState()
        {
            TargetId = default;
            AttackCooldown = TickTimer.None;
            NextMoveTick = default;
            IsDead = false;
            DeathDespawnTimer = TickTimer.None;
            AnimationEvent = UnitAnimationEventType.None;
            AnimationEventSequence = 0;
            AnimationDirectionValue = (byte)GetDefaultAnimationDirection();
            ClearBuffState();
            RootExpireTimer = TickTimer.None;
            StunExpireTimer = TickTimer.None;
            ClearSkillActionLock();
            _activeSkillRuntime = null;

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
            UnitController[] allUnits = FindObjectsByType<UnitController>(FindObjectsSortMode.None);
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

        public UnitController FindFarthestEnemyInRange(int range)
        {
            if (range < 0)
                return null;

            UnitController[] allUnits = FindObjectsByType<UnitController>(FindObjectsSortMode.None);
            UnitController best = null;
            int bestDistance = int.MinValue;

            for (int i = 0; i < allUnits.Length; i++)
            {
                UnitController candidate = allUnits[i];
                if (!IsEnemyCombatUnit(candidate))
                    continue;

                int distance = HexCoord.Distance(Cell, candidate.Cell);
                if (distance > range)
                    continue;

                if (distance > bestDistance)
                {
                    best = candidate;
                    bestDistance = distance;
                }
            }

            return best;
        }

        public UnitController FindLowestHealthAllyInRange(int range, bool includeSelf)
        {
            if (range < 0)
                return null;

            UnitController[] allUnits = FindObjectsByType<UnitController>(FindObjectsSortMode.None);
            UnitController best = null;
            float bestRatio = float.MaxValue;
            int bestDistance = int.MaxValue;

            for (int i = 0; i < allUnits.Length; i++)
            {
                UnitController candidate = allUnits[i];
                if (!IsFriendlyCombatUnit(candidate, includeSelf))
                    continue;

                int distance = HexCoord.Distance(Cell, candidate.Cell);
                if (distance > range)
                    continue;

                float ratio = candidate.MaxHp <= 0f ? 1f : candidate.HP / candidate.MaxHp;
                if (ratio < bestRatio || (Mathf.Approximately(ratio, bestRatio) && distance < bestDistance))
                {
                    best = candidate;
                    bestRatio = ratio;
                    bestDistance = distance;
                }
            }

            return best;
        }

        public List<UnitController> FindUnitsInRange(HexCoord center, int range, SkillTargetTeam targetTeam, bool includeSelf)
        {
            var results = new List<UnitController>();
            if (range < 0)
                return results;

            UnitController[] allUnits = FindObjectsByType<UnitController>(FindObjectsSortMode.None);
            for (int i = 0; i < allUnits.Length; i++)
            {
                UnitController candidate = allUnits[i];
                if (!MatchesTargetTeam(candidate, targetTeam, includeSelf))
                    continue;

                if (HexCoord.Distance(center, candidate.Cell) <= range)
                    results.Add(candidate);
            }

            return results;
        }

        public bool IsEnemyInRange(UnitController target, int range)
        {
            if (!IsEnemyCombatUnit(target))
                return false;

            return IsWithinRange(target, range);
        }

        public bool IsAllyInRange(UnitController target, int range, bool includeSelf)
        {
            if (!IsFriendlyCombatUnit(target, includeSelf))
                return false;

            return IsWithinRange(target, range);
        }

        private bool MatchesTargetTeam(UnitController candidate, SkillTargetTeam targetTeam, bool includeSelf)
        {
            switch (targetTeam)
            {
                case SkillTargetTeam.Enemy:
                    return IsEnemyCombatUnit(candidate);
                case SkillTargetTeam.Ally:
                    return IsFriendlyCombatUnit(candidate, includeSelf: false);
                case SkillTargetTeam.AllyOrSelf:
                    return IsFriendlyCombatUnit(candidate, includeSelf);
                case SkillTargetTeam.Self:
                    return candidate == this && IsFriendlyCombatUnit(candidate, includeSelf: true);
                default:
                    return false;
            }
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

        private bool IsFriendlyCombatUnit(UnitController candidate, bool includeSelf)
        {
            if (candidate == null)
                return false;

            if (!includeSelf && candidate == this)
                return false;

            if (!candidate.IsNetworkStateReady || candidate.Object == null)
                return false;

            if (!candidate.IsCombatClone || candidate.BoardIndex != BoardIndex || candidate.HP <= 0f)
                return false;

            return GetCombatTeamBoardIndex(candidate) == GetCombatTeamBoardIndex(this);
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
            if (target == null || !IsEnemyCombatUnit(target) || IsAttackLocked())
                return false;

            // Do not attack until the current movement step has fully elapsed.
            if (Runner != null && Runner.Tick < NextMoveTick)
                return false;

            if (!IsWithinAttackRange(target))
                return false;

            if (!AttackCooldown.ExpiredOrNotRunning(Runner))
                return false;

            float damage = GetAttackDamageAgainst(target);
            if (!target.ReceiveDamage(damage, this))
                return false;

            RaiseAnimationEvent(UnitAnimationEventType.Attack);
            GainMana(GetManaGainOnAttack(), target);
            AttackCooldown = TickTimer.CreateFromSeconds(Runner, GetAttackIntervalSeconds());
            return true;
        }

        private float GetAttackDamageAgainst(UnitController target)
        {
            float rawDamage = GetAttackPower();
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
            if (target != null)
            {
                resistance = damageType == DamageType.Physical
                    ? target.GetArmor()
                    : target.GetMagicResist();
            }

            return ApplyResistance(rawDamage, resistance);
        }

        private float ApplyResistance(float rawDamage, float resistance)
        {
            float clampedRawDamage = Mathf.Max(0f, rawDamage);
            if (clampedRawDamage <= 0f)
                return 0f;

            if (resistance >= 0f)
                return clampedRawDamage / (1f + resistance * 0.01f);

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
            float attackSpeed = GetAttackSpeed();
            if (attackSpeed <= 0f)
                return 1f;

            return 1f / attackSpeed;
        }

        private int GetAttackRange()
        {
            float baseRange = stats != null ? stats.attackRange : 1;
            return Mathf.Max(1, Mathf.RoundToInt(baseRange + BuffAttackRangeAdd));
        }

        private int GetMoveTickInterval()
        {
            float moveSpeed = GetMoveSpeed();
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

        private float GetAttackPower()
        {
            float baseValue = stats != null ? stats.attackPower : 1f;
            return Mathf.Max(0f, baseValue + BuffAttackPowerAdd);
        }

        private float GetArmor()
        {
            float baseValue = stats != null ? stats.armor : 0f;
            return baseValue + BuffArmorAdd;
        }

        private float GetMagicResist()
        {
            float baseValue = stats != null ? stats.magicResist : 0f;
            return baseValue + BuffMagicResistAdd;
        }

        private float GetAttackSpeed()
        {
            float baseValue = stats != null ? stats.attackSpeed : 1f;
            float multiplier = BuffAttackSpeedMul <= 0f ? 1f : BuffAttackSpeedMul;
            return Mathf.Max(0.1f, baseValue * multiplier);
        }

        private float GetMoveSpeed()
        {
            float baseValue = stats != null ? stats.moveSpeed : 1f;
            float multiplier = BuffMoveSpeedMul <= 0f ? 1f : BuffMoveSpeedMul;
            return Mathf.Max(0f, baseValue * multiplier);
        }

        private bool TryMoveOneStepToward(UnitController target)
        {
            if (target == null || boardManager == null || IsMovementLocked())
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

                    if (path.Count < bestPathLength || (path.Count == bestPathLength && targetDistance < bestTargetDistance))
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

        private void UpdateBuffState()
        {
            if (!HasStateAuthority)
                return;

            if (BuffExpireTimer.IsRunning && BuffExpireTimer.Expired(Runner))
                ClearBuffState();
        }

        private bool IsMovementLocked()
        {
            return _skillMoveLocked || IsTimerActive(RootExpireTimer) || IsTimerActive(StunExpireTimer);
        }

        private bool IsAttackLocked()
        {
            return _skillAttackLocked || IsTimerActive(StunExpireTimer);
        }

        private bool IsTimerActive(TickTimer timer)
        {
            return timer.IsRunning && !timer.ExpiredOrNotRunning(Runner);
        }

        private void ClearBuffState()
        {
            BuffAttackPowerAdd = 0f;
            BuffArmorAdd = 0f;
            BuffMagicResistAdd = 0f;
            BuffAttackSpeedMul = 1f;
            BuffMoveSpeedMul = 1f;
            BuffAttackRangeAdd = 0;
            BuffExpireTimer = TickTimer.None;
        }

        private void HandleDeath()
        {
            if (IsDead)
                return;

            IsDead = true;
            TargetId = default;
            AttackCooldown = TickTimer.None;
            NextMoveTick = default;
            ClearBuffState();
            RootExpireTimer = TickTimer.None;
            StunExpireTimer = TickTimer.None;
            _activeSkillRuntime?.Stop();
            _activeSkillRuntime = null;
            ClearSkillActionLock();
            DeathDespawnTimer = deathDespawnDelaySeconds > 0f
                ? TickTimer.CreateFromSeconds(Runner, deathDespawnDelaySeconds)
                : TickTimer.None;

            if (boardManager != null)
                boardManager.RemoveUnit(BoardIndex, Cell);
        }

        private void CacheVisualComponents()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
            _colliders3D = GetComponentsInChildren<Collider>(true);
            _colliders2D = GetComponentsInChildren<Collider2D>(true);
        }

        private void CacheAnimationComponents()
        {
            animator ??= GetComponentInChildren<Animator>(true);
            if (animator == null)
                return;

            AnimatorControllerParameter[] parameters = animator.parameters;
            _moveBoolHash = Animator.StringToHash(moveBoolParameter);
            _attackTriggerHash = Animator.StringToHash(attackTriggerParameter);
            _skillTriggerHash = Animator.StringToHash(skillTriggerParameter);
            _hitTriggerHash = Animator.StringToHash(hitTriggerParameter);
            _deadBoolHash = Animator.StringToHash(deadBoolParameter);
            _directionIntHash = Animator.StringToHash(directionIntParameter);

            _hasMoveBool = HasAnimatorParameter(parameters, _moveBoolHash, AnimatorControllerParameterType.Bool);
            _hasAttackTrigger = HasAnimatorParameter(parameters, _attackTriggerHash, AnimatorControllerParameterType.Trigger);
            _hasSkillTrigger = HasAnimatorParameter(parameters, _skillTriggerHash, AnimatorControllerParameterType.Trigger);
            _hasHitTrigger = HasAnimatorParameter(parameters, _hitTriggerHash, AnimatorControllerParameterType.Trigger);
            _hasDeadBool = HasAnimatorParameter(parameters, _deadBoolHash, AnimatorControllerParameterType.Bool);
            _hasDirectionInt = HasAnimatorParameter(parameters, _directionIntHash, AnimatorControllerParameterType.Int);
            _lastLocomotionStateHash = 0;
        }

        private static bool HasAnimatorParameter(AnimatorControllerParameter[] parameters, int hash, AnimatorControllerParameterType type)
        {
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].nameHash == hash && parameters[i].type == type)
                    return true;
            }

            return false;
        }

        private void ApplyAnimationState()
        {
            if (animator == null)
                return;

            bool isMoving = _lerping && !IsDeadOrDying;

            if (_hasMoveBool)
            {
                animator.SetBool(_moveBoolHash, isMoving);
            }

            if (_hasDeadBool)
            {
                animator.SetBool(_deadBoolHash, IsDeadOrDying);
            }

            if (_hasDirectionInt)
            {
                animator.SetInteger(_directionIntHash, (int)AnimationDirection);
            }
            else
            {
                ApplyLocomotionAnimation(isMoving);
            }

            if (_lastConsumedAnimationEventSequence == AnimationEventSequence)
                return;

            _lastConsumedAnimationEventSequence = AnimationEventSequence;
            switch (AnimationEvent)
            {
                case UnitAnimationEventType.Attack:
                    if (_hasAttackTrigger)
                        animator.SetTrigger(_attackTriggerHash);
                    break;
                case UnitAnimationEventType.Skill:
                    if (_hasSkillTrigger)
                        animator.SetTrigger(_skillTriggerHash);
                    break;
                case UnitAnimationEventType.Hit:
                    if (_hasHitTrigger)
                        animator.SetTrigger(_hitTriggerHash);
                    break;
            }
        }

        private void RaiseAnimationEvent(UnitAnimationEventType animationEvent)
        {
            if (!HasStateAuthority || animationEvent == UnitAnimationEventType.None)
                return;

            AnimationEvent = animationEvent;
            AnimationEventSequence++;
        }

        private void ApplyLocomotionAnimation(bool isMoving)
        {
            int stateHash = isMoving
                ? GetLocomotionStateHash(AnimationDirection)
                : GetIdleStateHash(AnimationDirection);

            if (stateHash == 0 && !isMoving)
                stateHash = GetLocomotionStateHash(AnimationDirection);

            if (stateHash == 0 || !animator.HasState(0, stateHash))
                return;

            if (isMoving)
            {
                if (_lastLocomotionStateHash != stateHash)
                {
                    animator.CrossFadeInFixedTime(stateHash, 0.05f);
                    _lastLocomotionStateHash = stateHash;
                }

                return;
            }

            animator.Play(stateHash, 0, 0f);
            animator.Update(0f);
            _lastLocomotionStateHash = stateHash;
        }

        private int GetIdleStateHash(UnitAnimationDirection direction)
        {
            if (string.IsNullOrWhiteSpace(idleStatePrefix))
                return 0;

            string suffix = direction switch
            {
                UnitAnimationDirection.Down => "D",
                UnitAnimationDirection.LeftDown => "LD",
                UnitAnimationDirection.Left => "L",
                UnitAnimationDirection.LeftUp => "LU",
                UnitAnimationDirection.Up => "U",
                UnitAnimationDirection.RightUp => "RU",
                UnitAnimationDirection.Right => "R",
                UnitAnimationDirection.RightDown => "RD",
                _ => "D"
            };

            return Animator.StringToHash($"{idleStatePrefix}{suffix}");
        }

        public void NotifyAnimationClipEvent(UnitAnimationClipEventType eventType)
        {
            if (eventType == UnitAnimationClipEventType.None)
                return;

            AnimationClipEventFired?.Invoke(eventType);
        }

        private int GetLocomotionStateHash(UnitAnimationDirection direction)
        {
            string suffix = direction switch
            {
                UnitAnimationDirection.Down => "D",
                UnitAnimationDirection.LeftDown => "LD",
                UnitAnimationDirection.Left => "L",
                UnitAnimationDirection.LeftUp => "LU",
                UnitAnimationDirection.Up => "U",
                UnitAnimationDirection.RightUp => "RU",
                UnitAnimationDirection.Right => "R",
                UnitAnimationDirection.RightDown => "RD",
                _ => "D"
            };

            return Animator.StringToHash($"{locomotionStatePrefix}{suffix}");
        }

        private void UpdateAnimationDirectionFromStep(HexCoord from, HexCoord to)
        {
            if (!HasStateAuthority)
                return;

            if (!TryResolveAnimationDirection(from, to, out UnitAnimationDirection direction))
                return;

            AnimationDirectionValue = (byte)direction;
        }

        private void ResetAnimationDirectionToDefault()
        {
            if (!HasStateAuthority)
                return;

            AnimationDirectionValue = (byte)GetDefaultAnimationDirection();
        }

        private UnitAnimationDirection GetDefaultAnimationDirection()
        {
            byte teamBoardIndex = GetCombatTeamBoardIndex(this);
            if (IsCombatClone && teamBoardIndex != byte.MaxValue && teamBoardIndex != BoardIndex)
                return UnitAnimationDirection.Up;

            return UnitAnimationDirection.Down;
        }

        private bool TryResolveAnimationDirection(HexCoord from, HexCoord to, out UnitAnimationDirection direction)
        {
            direction = AnimationDirection;

            if (from == to)
                return false;

            int dq = to.Q - from.Q;
            int dr = to.R - from.R;
            bool isOddRow = (from.R & 1) != 0;

            if (dr == 0)
            {
                if (dq == -1)
                {
                    direction = UnitAnimationDirection.Left;
                    return true;
                }

                if (dq == 1)
                {
                    direction = UnitAnimationDirection.Right;
                    return true;
                }
            }

            if (isOddRow)
            {
                if (dq == 0 && dr == -1)
                {
                    direction = UnitAnimationDirection.LeftUp;
                    return true;
                }

                if (dq == 1 && dr == -1)
                {
                    direction = UnitAnimationDirection.RightUp;
                    return true;
                }

                if (dq == 0 && dr == 1)
                {
                    direction = UnitAnimationDirection.LeftDown;
                    return true;
                }

                if (dq == 1 && dr == 1)
                {
                    direction = UnitAnimationDirection.RightDown;
                    return true;
                }
            }
            else
            {
                if (dq == -1 && dr == -1)
                {
                    direction = UnitAnimationDirection.LeftUp;
                    return true;
                }

                if (dq == 0 && dr == -1)
                {
                    direction = UnitAnimationDirection.RightUp;
                    return true;
                }

                if (dq == -1 && dr == 1)
                {
                    direction = UnitAnimationDirection.LeftDown;
                    return true;
                }

                if (dq == 0 && dr == 1)
                {
                    direction = UnitAnimationDirection.RightDown;
                    return true;
                }
            }

            return false;
        }

        private void ApplyVisibility(bool force = false)
        {
            _flow ??= FindAnyObjectByType<GameFlowManager>();
            bool hidden = (_flow != null && !_flow.ShouldUnitBeVisible(this)) || _locallyHiddenForPlacementDrag;
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



