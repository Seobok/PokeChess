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

            int boardCount = BoardGenerationPlan.CalculateBoardCount(origins.Count, playerCount);
            for (int i = 0; i < boardCount; i++)
            {
                GenerateSingleBoard(origins[i], i + 1);
            }
        }

        /// <summary>
        /// Axial(q, r) 좌표를 XZ 월드 좌표로 변환합니다.
        /// </summary>
        public Vector3 AxialToWorld(HexCoord coord)
        {
            float x = hexRadius * Mathf.Sqrt(3f) * (coord.Q + coord.R * 0.5f);
            float z = hexRadius * 1.5f * coord.R;
            return new Vector3(x, 0f, z);
        }

        private void GenerateSingleBoard(Vector3 origin, int boardIndex)
        {
            foreach (HexCoord coord in BoardGenerationPlan.EnumerateBoardCoords())
            {
                Vector3 worldPosition = origin + AxialToWorld(coord);
                GameObject tile = CreateTile(coord, worldPosition, boardIndex);
                _spawnedTiles.Add(tile);
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
