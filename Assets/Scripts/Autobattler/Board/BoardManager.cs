using Fusion;
using PokeChess.Autobattler;
using System.Collections.Generic;

public class BoardManager : NetworkBehaviour
{
    public const int BoardWidth = 7;
    public const int BoardHeight = 8;
    public const int DeployStartRow = 4;

    private readonly Dictionary<BoardCellKey, NetworkId> _occupants = new();

    public bool IsInside(HexCoord coord)
        => coord.Q >= 0 && coord.Q < BoardWidth && coord.R >= 0 && coord.R < BoardHeight;

    public bool IsDeployZone(HexCoord coord)
        => IsInside(coord) && coord.R < DeployStartRow;

    public bool IsOccupied(byte boardIndex, HexCoord coord)
        => _occupants.ContainsKey(new BoardCellKey(boardIndex, coord));

    public bool TryDeployUnit(NetworkId unitId, byte boardIndex, HexCoord coord)
    {
        if (!HasStateAuthority) return false;
        if (unitId == default) return false;
        if (!IsDeployZone(coord)) return false;

        var key = new BoardCellKey(boardIndex, coord);
        if (_occupants.ContainsKey(key)) return false;

        _occupants[key] = unitId;
        return true;
    }

    public bool RemoveUnit(byte boardIndex, HexCoord coord)
    {
        if (!HasStateAuthority) return false;
        return _occupants.Remove(new BoardCellKey(boardIndex, coord));
    }

    /// <summary>
    /// 보드 내부에 있는 인접 6방향 좌표만 반환합니다.
    /// </summary>
    public IEnumerable<HexCoord> GetNeighbors(HexCoord origin)
    {
        for (int i = 0; i < HexCoord.NeighborCount; i++)
        {
            HexCoord next = origin.Neighbor(i);
            if (IsInside(next))
                yield return next;
        }
    }

    /// <summary>
    /// 보드 밖도 포함해서 6방향 전부 반환.
    /// </summary>
    public IEnumerable<HexCoord> GetNeighborsUnsafe(HexCoord origin)
    {
        for (int i = 0; i < HexCoord.NeighborCount; i++)
            yield return origin.Neighbor(i);
    }

    /// <summary>
    /// 유닛을 from -> to로 이동시킵니다. (State Authority에서만 성공)
    /// 멀티 보드 지원: boardIndex + coord로 점유를 관리합니다.
    /// </summary>
    public bool TryMoveUnit(byte boardIndex, HexCoord from, HexCoord to)
    {
        if (HasStateAuthority == false)
            return false;

        // 목적지는 보드 내부여야 함
        if (IsInside(to) == false)
            return false;

        var toKey = new BoardCellKey(boardIndex, to);
        if (_occupants.ContainsKey(toKey))
            return false; // 목적지 점유 중

        var fromKey = new BoardCellKey(boardIndex, from);
        if (_occupants.TryGetValue(fromKey, out NetworkId unitId) == false)
            return false; // 출발지에 유닛 없음

        _occupants.Remove(fromKey);
        _occupants[toKey] = unitId;
        return true;
    }

    public bool TryTransferUnit(NetworkId unitId, byte fromBoard, HexCoord fromCell, byte toBoard, HexCoord toCell)
    {
        if (!HasStateAuthority) return false;
        if (unitId == default) return false;
        if (!IsInside(fromCell) || !IsInside(toCell)) return false;

        var fromKey = new BoardCellKey(fromBoard, fromCell);
        var toKey = new BoardCellKey(toBoard, toCell);

        if (_occupants.TryGetValue(fromKey, out var foundId) == false)
            return false;

        if (foundId != unitId)
            return false;

        if (_occupants.ContainsKey(toKey))
            return false;

        _occupants.Remove(fromKey);
        _occupants[toKey] = unitId;
        return true;
    }
}