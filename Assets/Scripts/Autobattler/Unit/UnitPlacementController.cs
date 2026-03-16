using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.Events;

namespace PokeChess.Autobattler
{
    /// <summary>
    /// Handles click-to-place unit spawning from the local player's camera view.
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
        private bool _isPlacementMode;
        private byte _selectedUnitTypeId;
        private byte[] _randomSummonUnitTypeIds;
        private UnityAction[] _summonButtonActions;

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
            if (_isPlacementMode == false)
            {
                return;
            }

            if (!TryGetPointerScreenPosition(out Vector2 pointerScreenPosition))
            {
                return;
            }

            UpdateGhostPosition(pointerScreenPosition);

            if (WasPrimaryPointerPressedThisFrame())
            {
                TryPlaceUnit(pointerScreenPosition);
            }
        }

        /// <summary>
        /// UI button OnClick entry point.
        /// </summary>
        public void BeginPlacementMode()
        {
            BeginPlacementMode(_selectedUnitTypeId);
        }

        /// <summary>
        /// UI button entry point that selects a unit type before entering placement mode.
        /// </summary>
        public void BeginPlacementMode(int unitTypeId)
        {
            if (_isPlacementMode)
            {
                return;
            }

            _selectedUnitTypeId = (byte)Mathf.Clamp(unitTypeId, 0, byte.MaxValue);

            if (unitSpawner == null)
            {
                unitSpawner = FindAnyObjectByType<UnitSpawner>();
            }

            if (unitSpawner == null)
            {
                Debug.LogWarning("UnitSpawner is not assigned.");
                return;
            }

            if (!unitSpawner.IsValidUnitType(_selectedUnitTypeId))
            {
                Debug.LogWarning($"Unit type '{_selectedUnitTypeId}' is not configured.");
                return;
            }

            GameObject ghostPrefab = unitSpawner.GetGhostPrefab(_selectedUnitTypeId);
            if (ghostPrefab == null)
            {
                ghostPrefab = unitGhostPrefab;
            }

            if (ghostPrefab == null)
            {
                Debug.LogWarning("Unit ghost prefab is not assigned.");
                return;
            }

            _activeGhost = Instantiate(ghostPrefab);
            _isPlacementMode = true;
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

            BeginPlacementMode(_randomSummonUnitTypeIds[buttonIndex]);
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

        private void UpdateSummonButtonVisual(Button button, byte unitTypeId)
        {
            if (button == null || unitSpawner == null)
            {
                return;
            }

            Sprite buttonSprite = unitSpawner.GetButtonSprite(unitTypeId);
            if (button.image != null)
            {
                button.image.sprite = buttonSprite;
                button.image.enabled = buttonSprite != null;
            }

            TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
            {
                label.gameObject.SetActive(buttonSprite == null);
                if (buttonSprite == null)
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

        private void TryPlaceUnit(Vector2 pointerScreenPosition)
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                Debug.Log("EventSystem Error");
                return;
            }

            if (TryGetHoveredTile(pointerScreenPosition, out HexTile tile) == false)
            {
                Debug.Log("GetHoverdTile Error");
                return;
            }

            unitSpawner.RequestSpawn(tile.BoardIndex, tile.Coord, _selectedUnitTypeId);
            ExitPlacementMode();
        }

        private bool TryGetHoveredTile(Vector2 pointerScreenPosition, out HexTile tile)
        {
            tile = null;
            if (!targetCamera)
            {
                return false;
            }

            var ray = targetCamera.ScreenPointToRay(pointerScreenPosition);
            var hit = Physics2D.GetRayIntersection(ray, Mathf.Infinity, tileMask);

            if (!hit.collider)
            {
                return false;
            }

            return hit.collider.TryGetComponent(out tile);
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
            _isPlacementMode = false;

            if (_activeGhost != null)
            {
                Destroy(_activeGhost);
            }

            _activeGhost = null;
        }
    }
}
