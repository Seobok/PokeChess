using Fusion;
using UnityEngine;
using UnityEngine.EventSystems;

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
                unitSpawner = FindObjectOfType<UnitSpawner>();
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
                return;
            }

            if (TryGetHoveredTile(out HexTile tile) == false)
            {
                return;
            }

            UnitController unit = unitSpawner.SpawnUnit(tile.Coord, tile.transform.position);
            if (unit == null)
            {
                return;
            }

            ExitPlacementMode();
        }

        private bool TryGetHoveredTile(out HexTile tile)
        {
            tile = null;
            if (targetCamera == null)
            {
                return false;
            }

            Vector2 mousePosition = Input.mousePosition;
            Ray ray = targetCamera.ScreenPointToRay(mousePosition);

            RaycastHit2D[] hits2D = Physics2D.GetRayIntersectionAll(ray);
            for (int i = 0; i < hits2D.Length; i++)
            {
                tile = hits2D[i].collider.GetComponentInParent<HexTile>();
                if (tile != null)
                {
                    return true;
                }
            }

            if (Physics.Raycast(ray, out RaycastHit hit3D))
            {
                tile = hit3D.collider.GetComponentInParent<HexTile>();
                return tile != null;
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
