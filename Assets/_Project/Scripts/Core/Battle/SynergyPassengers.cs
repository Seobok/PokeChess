using System;
using System.Collections.Generic;
using System.Linq;
using PokeChess.Core.Board;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Battle
{
    public sealed partial class SynergySystem
    {
        private readonly Dictionary<string, string> embarkedPassengers = new Dictionary<string, string>();
        private readonly HashSet<string> pendingDisembark = new HashSet<string>();

        private void InitializeBoarding()
        {
            if (!rules.Enabled) return;
            foreach (int team in new[] { 1, 2 })
            {
                var lapras = Representative(team, "lapras");
                if (lapras == null || !lapras.IsOnBoard) continue;
                // Team two's preparation board is rotated by 180 degrees for combat.
                int column = lapras.Position.Column + (team == 1 ? 1 : -1);
                var passenger = battle.Units.FirstOrDefault(u => u != lapras && u.TeamId == team &&
                    u.IsAlive && u.IsOnBoard && u.Position.Row == lapras.Position.Row && u.Position.Column == column);
                if (passenger == null) continue;
                float hp = passenger.Stats.MaxHP * .15f;
                float attack = passenger.EffectiveStats.Attack * .20f;
                if (battle.TryRemoveUnit(passenger.UnitInstanceId) != BoardOperationResult.Success) continue;
                passenger.IsEmbarked = true;
                embarkedPassengers.Add(lapras.UnitInstanceId, passenger.UnitInstanceId);
                lapras.AddRoundStats(hp, attack);
                events.Add(new SynergyEvent("passenger", lapras.UnitInstanceId, passenger.UnitInstanceId,
                    "BOARD", 0, battle.CurrentTick, passenger.Position, lapras.Position));
            }
            AdvancePassengers();
        }

        private static bool IsPassengerControl(StatusEffectDefinition definition)
            => definition.CrowdControl != CrowdControlKind.None || new[]{StatusBehavior.Sleep, StatusBehavior.Freeze,
                StatusBehavior.Paralysis, StatusBehavior.Confusion, StatusBehavior.Blind}.Contains(definition.Behavior);

        internal void AdvancePassengers()
        {
            foreach (var sourceId in embarkedPassengers.Keys.OrderBy(id => id, StringComparer.Ordinal).ToArray())
            {
                var source = battle.Units.First(u => u.UnitInstanceId == sourceId);
                if (!source.IsAlive || !source.IsOnBoard || source.CurrentHP < source.Stats.MaxHP * .75f ||
                    pendingDisembark.Contains(sourceId) || battle.StatusEffects.Active.Any(e =>
                        e.TargetId == sourceId && IsPassengerControl(e.Definition))) Disembark(source);
            }
        }

        internal void Disembark(UnitCombatState lapras)
        {
            if (!embarkedPassengers.TryGetValue(lapras.UnitInstanceId, out var passengerId)) return;
            var passenger = battle.Units.First(u => u.UnitInstanceId == passengerId);
            if (!passenger.IsAlive)
            {
                passenger.IsEmbarked = false;
                embarkedPassengers.Remove(lapras.UnitInstanceId);
                pendingDisembark.Remove(lapras.UnitInstanceId);
                return;
            }
            pendingDisembark.Add(lapras.UnitInstanceId);
            bool Empty(BoardPosition tile) => !battle.Board.IsOccupied(tile) && !battle.Movement.Reservations.IsReserved(tile);
            var empty = battle.Board.Cells.Where(Empty).OrderBy(p => p.Row).ThenBy(p => p.Column).ToArray();
            // If the entire board is full, retry when a cell becomes available.
            if (empty.Length == 0) return;
            int behindRow = lapras.Position.Row + (lapras.TeamId == 1 ? -1 : 1);
            BoardPosition destination;
            if (behindRow >= 0 && empty.Contains(new BoardPosition(lapras.Position.Column, behindRow)))
                destination = new BoardPosition(lapras.Position.Column, behindRow);
            else
            {
                int distance = empty.Min(p => HexCoordinates.Distance(lapras.Position, p));
                var nearest = empty.Where(p => HexCoordinates.Distance(lapras.Position, p) == distance).ToArray();
                destination = nearest[battle.RngStreams.Target.NextInt(nearest.Length)];
            }
            if (!battle.TryRestoreUnit(passenger, destination)) return;
            embarkedPassengers.Remove(lapras.UnitInstanceId);
            pendingDisembark.Remove(lapras.UnitInstanceId);
            // The bonus belongs to this battle state and intentionally survives disembarking.
            battle.Shields.Add(passenger, "synergy:passenger", "passenger", passenger.Stats.MaxHP * .15f, 4);
            events.Add(new SynergyEvent("passenger", lapras.UnitInstanceId, passenger.UnitInstanceId,
                "DISEMBARK", 0, battle.CurrentTick, destination));
        }
    }
}
