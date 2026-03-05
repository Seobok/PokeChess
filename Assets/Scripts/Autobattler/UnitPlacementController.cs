using Fusion;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace PokeChess.Autobattler
{
    /// <summary>
    /// 버튼 클릭으로 유닛 배치 모드(고스트 표시)를 시작하고,
    /// 타일 클릭 시 실제 유닛을 스폰합니다.
    /// </summary>
    public class UnitPlacementController : MonoBehaviour
    {
        [SerializeField] private UnitSpawner unitSpawner;
        [SerializeField] private GameObject unitGhostPrefab;
        [SerializeField] private Camera targetCamera;
        [SerializeField] private float ghostZ = -0.1f;
        [SerializeField] private LayerMask tileMask = ~0;

        private GameObject _activeGhost;
        private bool _isPlacementMode;

        private void Awake()
        {
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

            UpdateGhostPosition();

            if (Input.GetMouseButtonDown(0))
            {
                TryPlaceUnit();
            }
        }

        /// <summary>
        /// UI 버튼 OnClick에 연결하는 진입점입니다.
        /// </summary>
        public void BeginPlacementMode()
        {
            if (_isPlacementMode)
            {
                return;
            }

            if (unitSpawner == null)
            {
                unitSpawner = FindAnyObjectByType<UnitSpawner>();
            }

            if (unitSpawner == null)
            {
                Debug.LogWarning("UnitSpawner is not assigned.");
                return;
            }

            if (unitGhostPrefab == null)
            {
                Debug.LogWarning("UnitGhostPrefab is not assigned.");
                return;
            }

            _activeGhost = Instantiate(unitGhostPrefab);
            _isPlacementMode = true;
        }

        private void UpdateGhostPosition()
        {
            if (_activeGhost == null || targetCamera == null)
            {
                return;
            }

            Ray ray = targetCamera.ScreenPointToRay(Input.mousePosition);
            Plane plane = new(Vector3.forward, new Vector3(0f, 0f, ghostZ));

            if (plane.Raycast(ray, out float enter))
            {
                _activeGhost.transform.position = ray.GetPoint(enter);
            }
        }

        private void TryPlaceUnit()
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                Debug.Log("EventSystem Error");
                return;
            }

            if (TryGetHoveredTile(out HexTile tile) == false)
            {
                Debug.Log("GetHoverdTile Error");
                return;
            }

            unitSpawner.RequestSpawn(tile.BoardIndex, tile.Coord);

            ExitPlacementMode();
        }

        private bool TryGetHoveredTile(out HexTile tile)
        {
            tile = null;
            if (!targetCamera) return false;

            var ray = targetCamera.ScreenPointToRay(Input.mousePosition);
            var hit = Physics2D.GetRayIntersection(ray, Mathf.Infinity, tileMask);

            if (!hit.collider) return false;
            return hit.collider.TryGetComponent(out tile);
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
