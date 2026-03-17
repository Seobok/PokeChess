using UnityEngine;

namespace PokeChess.Autobattler
{
    /// <summary>
    /// Hex 타일 오브젝트에 보드 좌표를 연결합니다.
    /// </summary>
    public class HexTile : MonoBehaviour
    {
        public HexCoord Coord { get; private set; }
        public byte BoardIndex { get; private set; }

        public void Initialize(byte boardIndex, HexCoord coord)
        {
            BoardIndex = boardIndex;
            Coord = coord;
        }
    }

    /// <summary>
    /// Clickable waiting-area slot that maps to a board-local placement coordinate.
    /// </summary>
    public class BenchSlot : MonoBehaviour
    {
        public byte BoardIndex { get; private set; }
        public int SlotIndex { get; private set; }
        public HexCoord Coord { get; private set; }

        public void Initialize(byte boardIndex, int slotIndex)
        {
            BoardIndex = boardIndex;
            SlotIndex = slotIndex;
            Coord = BoardManager.GetBenchCoord(slotIndex);
        }
    }
}
