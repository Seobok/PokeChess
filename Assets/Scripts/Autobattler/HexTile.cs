using UnityEngine;

namespace PokeChess.Autobattler
{
    /// <summary>
    /// Hex 타일 오브젝트에 보드 좌표를 연결합니다.
    /// </summary>
    public class HexTile : MonoBehaviour
    {
        [field: SerializeField] public int Q { get; private set; }
        [field: SerializeField] public int R { get; private set; }

        public HexCoord Coord => new(Q, R);

        public void Initialize(HexCoord coord)
        {
            Q = coord.Q;
            R = coord.R;
        }
    }
}
