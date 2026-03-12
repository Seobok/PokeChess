using Fusion;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

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

        private GameObject _activeGhost;
        private bool _isPlacementMode;
        private byte _selectedUnitTypeId;

        private void Awake()
        {
            _selectedUnitTypeId = defaultUnitTypeId;

            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }
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
