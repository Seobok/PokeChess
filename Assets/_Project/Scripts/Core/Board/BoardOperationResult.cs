namespace PokeChess.Core.Board
{
    public enum BoardOperationResult
    {
        Success, InvalidUnitId, OutOfBounds, CellOccupied, UnitAlreadyPlaced, UnitNotFound
    }
}
