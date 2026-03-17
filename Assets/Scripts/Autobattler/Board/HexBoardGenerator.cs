using System.Collections.Generic;
using UnityEngine;

namespace PokeChess.Autobattler
{
    /// <summary>
    /// Builds one or more 7x8 hex boards at the supplied origin points.
    /// </summary>
    public class HexBoardGenerator : MonoBehaviour
    {
        [Header("Board Visual")]
        [SerializeField] private GameObject tilePrefab;
        [SerializeField] private GameObject benchSlotPrefab;
        [SerializeField] private Transform boardRoot;
        [SerializeField] private float hexRadius = 0.5f;
        [SerializeField] private bool useXYPlane = true;
        [SerializeField] private float benchVerticalGap = 1.25f;
        [SerializeField] private float benchHorizontalSpacingMultiplier = 1f;

        private const int MaxBoardCount = 8;
        private readonly List<GameObject> _spawnedTiles = new();

        /// <summary>
        /// ?⑥씪 ?꾩튂??蹂대뱶 1媛쒕? ?앹꽦?⑸땲??
        /// </summary>
        public void GenerateBoardAt(Vector3 origin)
        {
            GenerateBoardsAt(new List<Vector3> { origin }, 1);
        }

        /// <summary>
        /// ?꾨떖諛쏆? ?꾩튂 諛곗뿴?먯꽌 playerCount ?섎쭔??蹂대뱶瑜??앹꽦?⑸땲?? (理쒕? 8媛?
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
        /// 吏?뺥븳 origin 蹂대뱶???쒓컖??以묒떖?먯쓣 怨꾩궛?⑸땲??
        /// </summary>
        public Vector3 GetBoardCenter(Vector3 origin)
        {
            Vector3 min = origin + AxialToWorld(new HexCoord(0, 0));
            Vector3 max = min;

            for (int r = 0; r < BoardManager.BoardHeight; r++)
            {
                for (int q = 0; q < BoardManager.BoardWidth; q++)
                {
                    Vector3 worldPosition = origin + AxialToWorld(new HexCoord(q, r));
                    min = Vector3.Min(min, worldPosition);
                    max = Vector3.Max(max, worldPosition);
                }
            }

            return (min + max) * 0.5f;
        }

        /// <summary>
        /// Axial(q, r) 醫뚰몴瑜??붾뱶 醫뚰몴濡?蹂?섑빀?덈떎.
        /// ?꾩쟻 ?媛??대룞???꾨땶 odd-r offset ?뺥깭(???⑥쐞 諛?移??ㅽ봽??濡?諛곗튂??
        /// ?꾪삎?곸씤 ?ㅽ넗諛고????↔컖??蹂대뱶 ?뺥깭瑜??좎??⑸땲??
        /// 2D ?꾨줈?좏??낆뿉?쒕뒗 XY ?됰㈃, 3D?먯꽌??XZ ?됰㈃???ъ슜?????덉뒿?덈떎.
        /// </summary>
        public Vector3 AxialToWorld(HexCoord coord)
        {
            if (coord.R == BoardManager.BenchRow)
            {
                return BenchToWorld(coord.Q);
            }

            float rowOffset = (coord.R & 1) * 0.5f;
            float x = hexRadius * Mathf.Sqrt(3f) * (coord.Q + rowOffset);
            float secondaryAxis = hexRadius * 1.5f * coord.R;

            if (useXYPlane)
            {
                return new Vector3(x, secondaryAxis, 0f);
            }

            return new Vector3(x, 0f, secondaryAxis);
        }

        public Vector3 GetPlacementWorldPosition(Vector3 origin, HexCoord coord)
        {
            return origin + AxialToWorld(coord);
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

            GenerateBenchSlots(origin, boardIndex);
        }

        private GameObject CreateTile(HexCoord coord, Vector3 worldPosition, int boardIndex)
        {
            GameObject tile = tilePrefab != null
                ? Instantiate(tilePrefab, worldPosition, Quaternion.identity, boardRoot)
                : new GameObject();

            tile.name = $"Board{boardIndex}_Hex_{coord.Q}_{coord.R}";

            var hexTile = tile.GetComponent<HexTile>() ?? tile.AddComponent<HexTile>();
            hexTile.Initialize((byte)(boardIndex - 1), coord); // ??0-based boardIndex 沅뚯옣
            return tile;
        }

        private void GenerateBenchSlots(Vector3 origin, int boardIndex)
        {
            GameObject prefab = benchSlotPrefab != null ? benchSlotPrefab : tilePrefab;
            if (prefab == null)
            {
                return;
            }

            for (int i = 0; i < BoardManager.BenchSlotCount; i++)
            {
                HexCoord coord = BoardManager.GetBenchCoord(i);
                Vector3 worldPosition = origin + AxialToWorld(coord);
                GameObject slot = Instantiate(prefab, worldPosition, Quaternion.identity, boardRoot);
                slot.name = $"Board{boardIndex}_Bench_{i}";

                var benchSlot = slot.GetComponent<BenchSlot>() ?? slot.AddComponent<BenchSlot>();
                benchSlot.Initialize((byte)(boardIndex - 1), i);

                if (slot.GetComponent<Collider2D>() == null)
                {
                    slot.AddComponent<BoxCollider2D>();
                }

                _spawnedTiles.Add(slot);
            }
        }

        private Vector3 BenchToWorld(int slotIndex)
        {
            float xStep = hexRadius * Mathf.Sqrt(3f) * Mathf.Max(0.1f, benchHorizontalSpacingMultiplier);
            float totalWidth = xStep * (BoardManager.BenchSlotCount - 1);
            float x = -totalWidth * 0.5f + (slotIndex * xStep);
            float secondaryAxis = -benchVerticalGap;

            if (useXYPlane)
            {
                return new Vector3(x, secondaryAxis, 0f);
            }

            return new Vector3(x, 0f, secondaryAxis);
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


