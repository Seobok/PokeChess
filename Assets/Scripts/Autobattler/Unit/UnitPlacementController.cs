using TMPro;
using Fusion;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.Events;

namespace PokeChess.Autobattler
{
    /// <summary>
    /// Handles bench spawning and drag-move placement from the local player's camera view.
    /// </summary>
    public class UnitPlacementController : MonoBehaviour
    {
        [SerializeField] private UnitSpawner unitSpawner;
        [SerializeField] private GameObject unitGhostPrefab;
        [SerializeField] private Camera targetCamera;
        [SerializeField] private float ghostZ = -0.1f;
        [SerializeField] private LayerMask tileMask = ~0;
        [SerializeField] private byte defaultUnitTypeId;
        [Header("Random Summon Buttons")]
        [SerializeField] private Button[] summonButtons = new Button[5];

        private GameObject _activeGhost;
        private byte _selectedUnitTypeId;
        private byte[] _randomSummonUnitTypeIds;
        private UnityAction[] _summonButtonActions;
        private GameFlowManager _flowManager;
        private BoardManager _boardManager;
        private UnitController _draggingUnit;
        private HexCoord _dragOriginCell;

        private void Awake()
        {
            _selectedUnitTypeId = defaultUnitTypeId;
            _randomSummonUnitTypeIds = new byte[summonButtons != null ? summonButtons.Length : 0];
            _summonButtonActions = new UnityAction[summonButtons != null ? summonButtons.Length : 0];

            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }
        }

        private void Start()
        {
            RefreshRandomSummonButtons();
        }

        private void OnDestroy()
        {
            UnbindSummonButtons();
        }

        private void Update()
        {
            if (!TryGetPointerScreenPosition(out Vector2 pointerScreenPosition))
            {
                return;
            }

            if (_draggingUnit != null)
            {
                UpdateGhostPosition(pointerScreenPosition);
            }

            if (WasPrimaryPointerPressedThisFrame())
            {
                HandlePrimaryPointerPressed(pointerScreenPosition);
            }
        }

        /// <summary>
        /// UI button OnClick entry point.
        /// </summary>
        public void BeginPlacementMode()
        {
            TryBeginPlacementMode(_selectedUnitTypeId);
        }

        /// <summary>
        /// UI button entry point that selects a unit type before entering placement mode.
        /// </summary>
        public void BeginPlacementMode(int unitTypeId)
        {
            TryBeginPlacementMode((byte)Mathf.Clamp(unitTypeId, 0, byte.MaxValue));
        }

        private bool TryBeginPlacementMode(byte unitTypeId)
        {
            _selectedUnitTypeId = unitTypeId;

            if (unitSpawner == null)
            {
                unitSpawner = FindAnyObjectByType<UnitSpawner>();
            }

            if (unitSpawner == null)
            {
                Debug.LogWarning("UnitSpawner is not assigned.");
                return false;
            }

            if (!unitSpawner.IsValidUnitType(_selectedUnitTypeId))
            {
                Debug.LogWarning($"Unit type '{_selectedUnitTypeId}' is not configured.");
                return false;
            }

            unitSpawner.RequestSpawnToBench(_selectedUnitTypeId);
            return true;
        }

        public void SelectUnitType(int unitTypeId)
        {
            _selectedUnitTypeId = (byte)Mathf.Clamp(unitTypeId, 0, byte.MaxValue);
        }

        [ContextMenu("Refresh Random Summon Buttons")]
        public void RefreshRandomSummonButtons()
        {
            if (unitSpawner == null)
            {
                unitSpawner = FindAnyObjectByType<UnitSpawner>();
            }

            if (unitSpawner == null || summonButtons == null || summonButtons.Length == 0)
            {
                return;
            }

            if (_randomSummonUnitTypeIds == null || _randomSummonUnitTypeIds.Length != summonButtons.Length)
            {
                _randomSummonUnitTypeIds = new byte[summonButtons.Length];
            }

            if (_summonButtonActions == null || _summonButtonActions.Length != summonButtons.Length)
            {
                _summonButtonActions = new UnityAction[summonButtons.Length];
            }

            for (int i = 0; i < summonButtons.Length; i++)
            {
                Button button = summonButtons[i];
                if (button == null)
                {
                    continue;
                }

                if (!unitSpawner.TryGetRandomSpawnableUnitType(out byte randomUnitTypeId))
                {
                    button.interactable = false;
                    continue;
                }

                _randomSummonUnitTypeIds[i] = randomUnitTypeId;
                BindSummonButton(button, i);
                button.interactable = true;
                UpdateSummonButtonVisual(button, randomUnitTypeId);
            }
        }

        public void OnClickRandomSummonButton(int buttonIndex)
        {
            if (summonButtons == null || buttonIndex < 0 || buttonIndex >= summonButtons.Length)
            {
                return;
            }

            if (_randomSummonUnitTypeIds == null || buttonIndex >= _randomSummonUnitTypeIds.Length)
            {
                return;
            }

            if (!TryBeginPlacementMode(_randomSummonUnitTypeIds[buttonIndex]))
            {
                return;
            }

            ClearSummonButton(buttonIndex);
        }

        private void BindSummonButton(Button button, int buttonIndex)
        {
            if (button == null)
            {
                return;
            }

            // These summon buttons may still have old inspector-persistent listeners.
            // Replace the click event so only the runtime-assigned slot action remains.
            button.onClick = new Button.ButtonClickedEvent();

            UnityAction action = () => OnClickRandomSummonButton(buttonIndex);
            _summonButtonActions[buttonIndex] = action;
            button.onClick.AddListener(action);
        }

        private void UnbindSummonButtons()
        {
            if (summonButtons == null || _summonButtonActions == null)
            {
                return;
            }

            int count = Mathf.Min(summonButtons.Length, _summonButtonActions.Length);
            for (int i = 0; i < count; i++)
            {
                if (summonButtons[i] == null || _summonButtonActions[i] == null)
                {
                    continue;
                }

                summonButtons[i].onClick.RemoveListener(_summonButtonActions[i]);
            }
        }

        private void ClearSummonButton(int buttonIndex)
        {
            if (summonButtons == null || buttonIndex < 0 || buttonIndex >= summonButtons.Length)
            {
                return;
            }

            Button button = summonButtons[buttonIndex];
            if (button == null)
            {
                return;
            }

            button.onClick = new Button.ButtonClickedEvent();

            if (_summonButtonActions != null && buttonIndex < _summonButtonActions.Length)
            {
                _summonButtonActions[buttonIndex] = null;
            }

            if (_randomSummonUnitTypeIds != null && buttonIndex < _randomSummonUnitTypeIds.Length)
            {
                _randomSummonUnitTypeIds[buttonIndex] = 0;
            }

            button.interactable = false;
            UpdateSummonButtonVisual(button, 0, clearVisual: true);
        }

        private void UpdateSummonButtonVisual(Button button, byte unitTypeId, bool clearVisual = false)
        {
            if (button == null)
            {
                return;
            }

            Sprite buttonSprite = null;
            if (!clearVisual && unitSpawner != null)
            {
                buttonSprite = unitSpawner.GetButtonSprite(unitTypeId);
            }

            if (button.image != null)
            {
                button.image.sprite = buttonSprite;
                button.image.enabled = buttonSprite != null;
            }

            TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
            {
                bool showLabel = !clearVisual && buttonSprite == null;
                label.gameObject.SetActive(showLabel);
                if (showLabel)
                {
                    label.text = unitTypeId.ToString();
                }
            }
        }

        private void UpdateGhostPosition(Vector2 pointerScreenPosition)
        {
            if (_activeGhost == null || targetCamera == null)
            {
                return;
            }

            Ray ray = targetCamera.ScreenPointToRay(pointerScreenPosition);
            Plane plane = new(Vector3.forward, new Vector3(0f, 0f, ghostZ));

            if (plane.Raycast(ray, out float enter))
            {
                _activeGhost.transform.position = ray.GetPoint(enter);
            }
        }

        private void HandlePrimaryPointerPressed(Vector2 pointerScreenPosition)
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                if (_draggingUnit != null)
                {
                    CancelDrag();
                }

                return;
            }

            if (_draggingUnit != null)
            {
                TryDropDraggedUnit(pointerScreenPosition);
                return;
            }

            TryBeginUnitDrag(pointerScreenPosition);
        }

        private void TryBeginUnitDrag(Vector2 pointerScreenPosition)
        {
            if (!TryResolveControllableUnit(pointerScreenPosition, out UnitController unit))
            {
                return;
            }

            if (!CanControlUnit(unit))
            {
                return;
            }

            _draggingUnit = unit;
            _dragOriginCell = unit.Cell;
            _draggingUnit.SetLocallyHiddenForPlacementDrag(true);
            SpawnGhost(unit.UnitTypeId);
            UpdateGhostPosition(pointerScreenPosition);
        }

        private bool TryResolveControllableUnit(Vector2 pointerScreenPosition, out UnitController unit)
        {
            unit = null;

            if (TryGetHoveredUnit(pointerScreenPosition, out unit) && unit != null)
            {
                return true;
            }

            if (TryGetHoveredBenchSlot(pointerScreenPosition, out BenchSlot benchSlot)
                && TryGetOccupyingUnit(benchSlot.BoardIndex, benchSlot.Coord, out unit))
            {
                return true;
            }

            if (TryGetHoveredTile(pointerScreenPosition, out HexTile tile)
                && TryGetOccupyingUnit(tile.BoardIndex, tile.Coord, out unit))
            {
                return true;
            }

            return false;
        }

        private void TryDropDraggedUnit(Vector2 pointerScreenPosition)
        {
            if (_draggingUnit == null)
            {
                return;
            }

            if (!TryGetDropTarget(pointerScreenPosition, out HexCoord targetCell))
            {
                CancelDrag();
                return;
            }

            if (targetCell == _dragOriginCell)
            {
                CancelDrag();
                return;
            }

            unitSpawner.RequestMoveUnit(_draggingUnit.Object.Id, targetCell);
            ExitDragMode();
        }

        private bool TryGetDropTarget(Vector2 pointerScreenPosition, out HexCoord targetCell)
        {
            targetCell = default;

            if (TryGetHoveredUnit(pointerScreenPosition, out UnitController hoveredUnit)
                && hoveredUnit != null
                && hoveredUnit != _draggingUnit
                && CanControlUnit(hoveredUnit))
            {
                targetCell = hoveredUnit.Cell;
                return true;
            }

            if (TryGetHoveredBenchSlot(pointerScreenPosition, out BenchSlot benchSlot) && IsValidPlacementTarget(benchSlot.BoardIndex, benchSlot.Coord))
            {
                targetCell = benchSlot.Coord;
                return true;
            }

            if (TryGetHoveredTile(pointerScreenPosition, out HexTile tile) && IsValidPlacementTarget(tile.BoardIndex, tile.Coord))
            {
                targetCell = tile.Coord;
                return true;
            }

            return false;
        }

        private bool TryGetHoveredTile(Vector2 pointerScreenPosition, out HexTile tile)
        {
            tile = GetHoveredComponent<HexTile>(pointerScreenPosition);
            return tile != null;
        }

        private bool TryGetHoveredBenchSlot(Vector2 pointerScreenPosition, out BenchSlot benchSlot)
        {
            benchSlot = GetHoveredComponent<BenchSlot>(pointerScreenPosition);
            return benchSlot != null;
        }

        private bool TryGetHoveredUnit(Vector2 pointerScreenPosition, out UnitController unit)
        {
            unit = GetHoveredComponent<UnitController>(pointerScreenPosition);
            return unit != null;
        }

        private bool TryGetOccupyingUnit(byte boardIndex, HexCoord coord, out UnitController unit)
        {
            unit = null;
            EnsureDependencies();

            if (_boardManager == null || unitSpawner == null || unitSpawner.Runner == null)
            {
                return false;
            }

            if (!_boardManager.TryGetOccupant(boardIndex, coord, out NetworkId occupantId) || occupantId == default)
            {
                return false;
            }

            if (!unitSpawner.Runner.TryFindObject(occupantId, out NetworkObject occupantObject))
            {
                return false;
            }

            return occupantObject.TryGetComponent(out unit);
        }

        private T GetHoveredComponent<T>(Vector2 pointerScreenPosition) where T : Component
        {
            if (targetCamera == null)
            {
                return null;
            }

            Ray ray = targetCamera.ScreenPointToRay(pointerScreenPosition);
            RaycastHit2D[] hits = Physics2D.GetRayIntersectionAll(ray, Mathf.Infinity, tileMask);
            for (int i = 0; i < hits.Length; i++)
            {
                Collider2D collider = hits[i].collider;
                if (collider == null)
                {
                    continue;
                }

                if (collider.TryGetComponent(out T component))
                {
                    return component;
                }

                component = collider.GetComponentInParent<T>();
                if (component != null)
                {
                    return component;
                }
            }

            return null;
        }

        private bool CanControlUnit(UnitController unit)
        {
            if (unit == null || unit.Object == null || unit.IsCombatClone)
            {
                return false;
            }

            EnsureDependencies();
            if (_flowManager == null || unitSpawner == null || unitSpawner.Runner == null)
            {
                return false;
            }

            if (!_flowManager.TryGetBoardIndex(unitSpawner.Runner.LocalPlayer, out byte localBoardIndex))
            {
                return false;
            }

            return unit.BoardIndex == localBoardIndex;
        }

        private bool IsValidPlacementTarget(byte boardIndex, HexCoord coord)
        {
            EnsureDependencies();
            if (_flowManager == null || _boardManager == null || unitSpawner == null || unitSpawner.Runner == null)
            {
                return false;
            }

            if (!_flowManager.TryGetBoardIndex(unitSpawner.Runner.LocalPlayer, out byte localBoardIndex))
            {
                return false;
            }

            return boardIndex == localBoardIndex && _boardManager.IsPlayerPlacementCell(coord);
        }

        private void SpawnGhost(byte unitTypeId)
        {
            GameObject ghostPrefab = unitSpawner.GetGhostPrefab(unitTypeId);
            if (ghostPrefab == null)
            {
                ghostPrefab = unitGhostPrefab;
            }

            if (ghostPrefab == null)
            {
                Debug.LogWarning("Unit ghost prefab is not assigned.");
                return;
            }

            if (_activeGhost != null)
            {
                Destroy(_activeGhost);
            }

            _activeGhost = Instantiate(ghostPrefab);
        }

        private void CancelDrag()
        {
            ExitDragMode();
        }

        private void ExitDragMode()
        {
            if (_draggingUnit != null)
            {
                _draggingUnit.SetLocallyHiddenForPlacementDrag(false);
            }

            _draggingUnit = null;
            _dragOriginCell = default;
            ExitPlacementMode();
        }

        private void EnsureDependencies()
        {
            unitSpawner ??= FindAnyObjectByType<UnitSpawner>();
            _flowManager ??= FindAnyObjectByType<GameFlowManager>();
            _boardManager ??= FindAnyObjectByType<BoardManager>();
        }

        private bool TryGetPointerScreenPosition(out Vector2 pointerScreenPosition)
        {
            if (Mouse.current != null)
            {
                pointerScreenPosition = Mouse.current.position.ReadValue();
                return true;
            }

            if (Touchscreen.current != null)
            {
                pointerScreenPosition = Touchscreen.current.primaryTouch.position.ReadValue();
                return true;
            }

            pointerScreenPosition = default;
            return false;
        }

        private bool WasPrimaryPointerPressedThisFrame()
        {
            if (Mouse.current?.leftButton.wasPressedThisFrame == true)
            {
                return true;
            }

            if (Touchscreen.current?.primaryTouch.press.wasPressedThisFrame == true)
            {
                return true;
            }

            return false;
        }

        private void ExitPlacementMode()
        {
            if (_activeGhost != null)
            {
                Destroy(_activeGhost);
            }

            _activeGhost = null;
        }
    }
}
