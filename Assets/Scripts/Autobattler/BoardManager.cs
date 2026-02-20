using System.Collections.Generic;
using Fusion;

namespace PokeChess.Autobattler
{
    /// <summary>
    /// 오토배틀러 Hex 보드의 범위/배치/점유 상태를 관리합니다.
    /// </summary>
    public class BoardManager : NetworkBehaviour
    {
        // 가로 7칸
        public const int BoardWidth = 7;

        // 세로 8칸
        public const int BoardHeight = 8;

        // 하단 4줄 배치 제한 시작 행 (r >= 4)
        public const int DeployStartRow = 4;

        // 타일 점유 정보: 한 칸에는 하나의 유닛만 배치 가능
        private readonly Dictionary<HexCoord, NetworkId> _occupants = new();

        /// <summary>
        /// 보드 내부 좌표인지 확인합니다.
        /// </summary>
        public bool IsInside(HexCoord coord)
        {
            return coord.Q >= 0 && coord.Q < BoardWidth && coord.R >= 0 && coord.R < BoardHeight;
        }

        /// <summary>
        /// 플레이어 배치 가능 구역(하단 4줄)인지 확인합니다.
        /// </summary>
        public bool IsDeployZone(HexCoord coord)
        {
            return IsInside(coord) && coord.R >= DeployStartRow;
        }

        /// <summary>
        /// 해당 좌표가 이미 점유되었는지 확인합니다.
        /// </summary>
        public bool IsOccupied(HexCoord coord)
        {
            return _occupants.ContainsKey(coord);
        }

        /// <summary>
        /// 좌표에 배치된 유닛의 NetworkId를 조회합니다.
        /// </summary>
        public bool TryGetOccupant(HexCoord coord, out NetworkId occupantId)
        {
            return _occupants.TryGetValue(coord, out occupantId);
        }

        /// <summary>
        /// 유닛을 배치합니다. (State Authority에서만 성공)
        /// </summary>
        public bool TryDeployUnit(NetworkId unitId, HexCoord coord)
        {
            // 서버(권한 객체)만 보드 상태를 변경하도록 제한
            if (HasStateAuthority == false)
            {
                return false;
            }

            // 유효하지 않은 NetworkId는 배치 불가
            if (unitId == default)
            {
                return false;
            }

            // 배치 가능 구역 + 미점유 조건을 동시에 만족해야 함
            if (IsDeployZone(coord) == false || IsOccupied(coord))
            {
                return false;
            }

            _occupants[coord] = unitId;
            return true;
        }

        /// <summary>
        /// 유닛을 from -> to로 이동시킵니다. (State Authority에서만 성공)
        /// </summary>
        public bool TryMoveUnit(HexCoord from, HexCoord to)
        {
            if (HasStateAuthority == false)
            {
                return false;
            }

            // 목적지는 보드 내부이면서 비어있어야 함
            if (IsInside(to) == false || IsOccupied(to))
            {
                return false;
            }

            // 출발지에 실제 유닛이 있어야 이동 가능
            if (_occupants.TryGetValue(from, out NetworkId unitId) == false)
            {
                return false;
            }

            _occupants.Remove(from);
            _occupants[to] = unitId;
            return true;
        }

        /// <summary>
        /// 해당 좌표의 유닛을 제거합니다. (State Authority에서만 성공)
        /// </summary>
        public bool RemoveUnit(HexCoord coord)
        {
            if (HasStateAuthority == false)
            {
                return false;
            }

            return _occupants.Remove(coord);
        }

        /// <summary>
        /// 보드 내부에 있는 인접 6방향 좌표만 순회합니다.
        /// </summary>
        public IEnumerable<HexCoord> GetNeighbors(HexCoord origin)
        {
            for (int i = 0; i < HexCoord.NeighborDirections.Length; i++)
            {
                HexCoord next = origin.Neighbor(i);
                if (IsInside(next))
                {
                    yield return next;
                }
            }
        }
    }
}
