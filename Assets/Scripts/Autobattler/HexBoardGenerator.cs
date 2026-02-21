using System.Collections.Generic;
using UnityEngine;

namespace PokeChess.Autobattler
{
    /// <summary>
    /// 지정된 월드 위치(여러 개 가능)를 기준으로 7x8 Hex 보드 타일 오브젝트를 생성합니다.
    /// </summary>
    public class HexBoardGenerator : MonoBehaviour
    {
        [Header("Board Visual")]
        [SerializeField] private GameObject tilePrefab;
        [SerializeField] private Transform boardRoot;
        [SerializeField] private float hexRadius = 0.5f;
        [SerializeField] private bool useXYPlane = true;

        private const int MaxBoardCount = 8;
        private readonly List<GameObject> _spawnedTiles = new();

        /// <summary>
        /// 단일 위치에 보드 1개를 생성합니다.
        /// </summary>
        public void GenerateBoardAt(Vector3 origin)
        {
            GenerateBoardsAt(new List<Vector3> { origin }, 1);
        }

        /// <summary>
        /// 전달받은 위치 배열에서 playerCount 수만큼 보드를 생성합니다. (최대 8개)
        /// </summary>
        public void GenerateBoardsAt(IReadOnlyList<Vector3> origins, int playerCount)
        {
            EnsureBoardRoot();
            ClearBoard();

            if (origins == null || origins.Count == 0 || playerCount <= 0)
            {
                return;
            }

            int boardCount = Mathf.Min(playerCount, MaxBoardCount, origins.Count);
            for (int i = 0; i < boardCount; i++)
            {
                GenerateSingleBoard(origins[i], i + 1);
            }
        }

        /// <summary>
        /// Axial(q, r) 좌표를 월드 좌표로 변환합니다.
        /// 누적 대각 이동이 아닌 odd-r offset 형태(행 단위 반 칸 오프셋)로 배치해
        /// 전형적인 오토배틀러 육각형 보드 형태를 유지합니다.
        /// 2D 프로토타입에서는 XY 평면, 3D에서는 XZ 평면을 사용할 수 있습니다.
        /// </summary>
        public Vector3 AxialToWorld(HexCoord coord)
        {
            float rowOffset = (coord.R & 1) * 0.5f;
            float x = hexRadius * Mathf.Sqrt(3f) * (coord.Q + rowOffset);
            float secondaryAxis = hexRadius * 1.5f * coord.R;

            if (useXYPlane)
            {
                return new Vector3(x, secondaryAxis, 0f);
            }

            return new Vector3(x, 0f, secondaryAxis);
        }

        private void GenerateSingleBoard(Vector3 origin, int boardIndex)
        {
            for (int r = 0; r < BoardManager.BoardHeight; r++)
            {
                for (int q = 0; q < BoardManager.BoardWidth; q++)
                {
                    HexCoord coord = new HexCoord(q, r);
                    Vector3 worldPosition = origin + AxialToWorld(coord);
                    GameObject tile = CreateTile(coord, worldPosition, boardIndex);
                    _spawnedTiles.Add(tile);
                }
            }
        }

        private GameObject CreateTile(HexCoord coord, Vector3 worldPosition, int boardIndex)
        {
            GameObject tile;
            if (tilePrefab != null)
            {
                tile = Instantiate(tilePrefab, worldPosition, Quaternion.identity, boardRoot);
            }
            else
            {
                tile = new GameObject();
                tile.transform.SetParent(boardRoot);
                tile.transform.position = worldPosition;
            }

            tile.name = $"Board{boardIndex}_Hex_{coord.Q}_{coord.R}";

            HexTile hexTile = tile.GetComponent<HexTile>();
            if (hexTile == null)
            {
                hexTile = tile.AddComponent<HexTile>();
            }

            hexTile.Initialize(coord);
            return tile;
        }

        private void EnsureBoardRoot()
        {
            if (boardRoot != null)
            {
                return;
            }

            GameObject root = new GameObject("HexBoardRoot");
            root.transform.SetParent(transform, false);
            boardRoot = root.transform;
        }

        private void ClearBoard()
        {
            for (int i = 0; i < _spawnedTiles.Count; i++)
            {
                if (_spawnedTiles[i] != null)
                {
                    Destroy(_spawnedTiles[i]);
                }
            }

            _spawnedTiles.Clear();
        }
    }
}
