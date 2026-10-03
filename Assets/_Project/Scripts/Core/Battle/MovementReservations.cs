using System;
using System.Collections.Generic;
using System.Linq;
using PokeChess.Core.Board;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Battle
{
    public sealed class MovementReservations
    {
        private readonly Dictionary<BoardPosition,string> cells = new Dictionary<BoardPosition,string>();
        public int Count => cells.Count;
        public bool IsReserved(BoardPosition position) => cells.ContainsKey(position);
        public bool TryGetOwner(BoardPosition position, out string unitId) => cells.TryGetValue(position,out unitId);
        internal bool TryReserve(string id, BoardPosition position)
        {
            if(cells.ContainsKey(position)) return false;
            cells.Add(position,id); return true;
        }
        internal void Release(string id)
        {
            foreach(var cell in cells.Where(p=>p.Value==id).Select(p=>p.Key).ToArray()) cells.Remove(cell);
        }
        internal IReadOnlyCombatBoard Overlay(IReadOnlyCombatBoard board) => new ReservationView(board,this);
        private sealed class ReservationView : IReadOnlyCombatBoard
        {
            private readonly IReadOnlyCombatBoard board;
            private readonly MovementReservations reservations;
            public ReservationView(IReadOnlyCombatBoard board, MovementReservations reservations)
            {this.board=board;this.reservations=reservations;}
            public int CellCount=>board.CellCount;
            public int OccupiedCount=>board.Cells.Count(IsOccupied);
            public IEnumerable<BoardPosition> Cells=>board.Cells;
            public bool Contains(BoardPosition p)=>board.Contains(p);
            public bool IsOccupied(BoardPosition p)=>board.IsOccupied(p)||reservations.IsReserved(p);
            public bool TryGetOccupant(BoardPosition p,out string id)
            {
                if(board.TryGetOccupant(p,out id))return true;
                if(reservations.TryGetOwner(p,out var owner)){id="reservation:"+owner;return true;}
                return false;
            }
            public bool TryGetPosition(string id,out BoardPosition p)=>board.TryGetPosition(id,out p);
            public IReadOnlyList<BoardPosition> EmptyNeighbors(BoardPosition p)=>
                Array.AsReadOnly(HexBoardBounds.Combat.Neighbors(p).Where(n=>!IsOccupied(n)).ToArray());
        }
    }
}
