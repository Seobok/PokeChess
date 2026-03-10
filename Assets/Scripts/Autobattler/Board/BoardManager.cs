using Fusion;
using PokeChess.Autobattler;
using System.Collections.Generic;

/// <summary>
/// Stores per-board occupancy state and validates movement or placement requests.
/// </summary>
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
        => TryPlaceUnit(unitId, boardIndex, coord, requireDeployZone: true);

    public bool TryOccupyUnit(NetworkId unitId, byte boardIndex, HexCoord coord)
        => TryPlaceUnit(unitId, boardIndex, coord, requireDeployZone: false);

    public bool RemoveUnit(byte boardIndex, HexCoord coord)
    {
        if (!HasStateAuthority) return false;
        return _occupants.Remove(new BoardCellKey(boardIndex, coord));
    }

    public IEnumerable<HexCoord> GetNeighbors(HexCoord origin)
    {
        for (int i = 0; i < HexCoord.NeighborCount; i++)
        {
            HexCoord next = origin.Neighbor(i);
            if (IsInside(next))
                yield return next;
        }
    }

    public IEnumerable<HexCoord> GetNeighborsUnsafe(HexCoord origin)
    {
        for (int i = 0; i < HexCoord.NeighborCount; i++)
            yield return origin.Neighbor(i);
    }

    public bool TryMoveUnit(byte boardIndex, HexCoord from, HexCoord to)
    {
        if (!HasStateAuthority)
            return false;

        if (!IsInside(to))
            return false;

        var toKey = new BoardCellKey(boardIndex, to);
        if (_occupants.ContainsKey(toKey))
            return false;

        var fromKey = new BoardCellKey(boardIndex, from);
        if (!_occupants.TryGetValue(fromKey, out NetworkId unitId))
            return false;

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

        if (!_occupants.TryGetValue(fromKey, out var foundId))
            return false;

        if (foundId != unitId)
            return false;

        if (_occupants.ContainsKey(toKey))
            return false;

        _occupants.Remove(fromKey);
        _occupants[toKey] = unitId;
        return true;
    }

    private bool TryPlaceUnit(NetworkId unitId, byte boardIndex, HexCoord coord, bool requireDeployZone)
    {
        if (!HasStateAuthority) return false;
        if (unitId == default) return false;
        if (!IsInside(coord)) return false;
        if (requireDeployZone && !IsDeployZone(coord)) return false;

        var key = new BoardCellKey(boardIndex, coord);
        if (_occupants.ContainsKey(key)) return false;

        _occupants[key] = unitId;
        return true;
    }
}

