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
        [SerializeField] private Transform boardRoot;
        [SerializeField] private float hexRadius = 0.5f;
        [SerializeField] private bool useXYPlane = true;

        private const int MaxBoardCount = 8;
        private readonly List<GameObject> _spawnedTiles = new();

        /// <summary>
        /// ?¨ì¼ ?„ì¹˜??ë³´ë“œ 1ê°œë? ?ì„±?©ë‹ˆ??
        /// </summary>
        public void GenerateBoardAt(Vector3 origin)
        {
            GenerateBoardsAt(new List<Vector3> { origin }, 1);
        }

        /// <summary>
        /// ?„ë‹¬ë°›ì? ?„ì¹˜ ë°°ì—´?ì„œ playerCount ?˜ë§Œ??ë³´ë“œë¥??ì„±?©ë‹ˆ?? (ìµœë? 8ê°?
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
        /// ì§€?•í•œ origin ë³´ë“œ???œê°??ì¤‘ì‹¬?ì„ ê³„ì‚°?©ë‹ˆ??
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
        /// Axial(q, r) ì¢Œí‘œë¥??”ë“œ ì¢Œí‘œë¡?ë³€?˜í•©?ˆë‹¤.
        /// ?„ì  ?€ê°??´ë™???„ë‹Œ odd-r offset ?•íƒœ(???¨ìœ„ ë°?ì¹??¤í”„??ë¡?ë°°ì¹˜??
        /// ?„í˜•?ì¸ ?¤í† ë°°í????¡ê°??ë³´ë“œ ?•íƒœë¥?? ì??©ë‹ˆ??
        /// 2D ?„ë¡œ? í??…ì—?œëŠ” XY ?‰ë©´, 3D?ì„œ??XZ ?‰ë©´???¬ìš©?????ˆìŠµ?ˆë‹¤.
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
            GameObject tile = tilePrefab != null
                ? Instantiate(tilePrefab, worldPosition, Quaternion.identity, boardRoot)
                : new GameObject();

            tile.name = $"Board{boardIndex}_Hex_{coord.Q}_{coord.R}";

            var hexTile = tile.GetComponent<HexTile>() ?? tile.AddComponent<HexTile>();
            hexTile.Initialize((byte)(boardIndex - 1), coord); // ??0-based boardIndex ê¶Œì¥
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

