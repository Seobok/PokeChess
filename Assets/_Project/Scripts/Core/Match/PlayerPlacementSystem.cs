using System;
using System.Collections.Generic;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Match
{
    public enum PlacementOutcome { Rejected, Unchanged, Moved, Swapped }
    public enum PlacementRejectReason { None, UnknownPlayer, InvalidPhase, Eliminated, StaleRevision,
        UnitNotOwned, InvalidSource, InvalidDestination, BoardLimit, RevisionOverflow }
    public sealed class PlacementCommandResult
    {
        public PlacementOutcome Outcome { get; }
        public PlacementRejectReason Reason { get; }
        public long Revision { get; }
        public string TargetUnitId { get; }
        public bool Accepted => Outcome != PlacementOutcome.Rejected;
        internal PlacementCommandResult(PlacementOutcome outcome, PlacementRejectReason reason, long revision, string target=null)
        { Outcome=outcome; Reason=reason; Revision=revision; TargetUnitId=target; }
    }
    // Host executes commands serially; preview is read-only and commit revalidates.
    public sealed class PlayerPlacementSystem
    {
        private static PlacementCommandResult Reject(PlacementRejectReason reason, long revision=0) =>
            new PlacementCommandResult(PlacementOutcome.Rejected,reason,revision);
        public PlacementCommandResult Preview(MatchState match,string playerId,string unitId,UnitPlacement destination,long expectedRevision)
        {
            if(match==null) throw new ArgumentNullException(nameof(match));
            PlayerState player;
            try { player=match.GetPlayer(playerId); }
            catch(ArgumentException) { return Reject(PlacementRejectReason.UnknownPlayer); }
            catch(KeyNotFoundException) { return Reject(PlacementRejectReason.UnknownPlayer); }
            long revision=player.PlacementRevision;
            if(match.Phase!=MatchPhase.Preparation && match.Phase!=MatchPhase.Combat) return Reject(PlacementRejectReason.InvalidPhase,revision);
            if(player.IsEliminated || player.HP==0) return Reject(PlacementRejectReason.Eliminated,revision);
            if(expectedRevision!=revision) return Reject(PlacementRejectReason.StaleRevision,revision);
            UnitInstance source;
            try { source=player.GetUnit(unitId); }
            catch(ArgumentException) { return Reject(PlacementRejectReason.UnitNotOwned,revision); }
            catch(KeyNotFoundException) { return Reject(PlacementRejectReason.UnitNotOwned,revision); }
            if(match.Phase==MatchPhase.Combat && (source.Placement.Kind!=PlacementKind.Bench || destination.Kind!=PlacementKind.Bench))
                return Reject(PlacementRejectReason.InvalidPhase,revision);
            if(source.Placement.Kind!=PlacementKind.Board && source.Placement.Kind!=PlacementKind.Bench)
                return Reject(PlacementRejectReason.InvalidSource,revision);
            UnitInstance target;
            try
            {
                if(destination.Kind==PlacementKind.Board) target=player.GetBoardUnit(destination.Position.Value);
                else if(destination.Kind==PlacementKind.Bench) target=player.GetBenchUnit(destination.BenchSlot.Value);
                else return Reject(PlacementRejectReason.InvalidDestination,revision);
            }
            catch(ArgumentOutOfRangeException) { return Reject(PlacementRejectReason.InvalidDestination,revision); }
            if(PlayerState.SamePlacement(source.Placement,destination))
                return new PlacementCommandResult(PlacementOutcome.Unchanged,PlacementRejectReason.None,revision);
            if(target==null && destination.Kind==PlacementKind.Board && source.Placement.Kind==PlacementKind.Bench
                && player.RemainingDeploymentCapacity==0) return Reject(PlacementRejectReason.BoardLimit,revision);
            if(revision==long.MaxValue) return Reject(PlacementRejectReason.RevisionOverflow,revision);
            return new PlacementCommandResult(target==null ? PlacementOutcome.Moved : PlacementOutcome.Swapped,
                PlacementRejectReason.None,revision,target?.InstanceId);
        }
        public PlacementCommandResult Move(MatchState match,string playerId,string unitId,UnitPlacement destination,long expectedRevision)
        {
            var result=Preview(match,playerId,unitId,destination,expectedRevision);
            if(!result.Accepted || result.Outcome==PlacementOutcome.Unchanged) return result;
            var player=match.GetPlayer(playerId);
            var source=player.GetUnit(unitId);
            var changes=new Dictionary<string,UnitPlacement>(StringComparer.Ordinal) { [unitId]=destination };
            if(result.TargetUnitId!=null) changes.Add(result.TargetUnitId,source.Placement);
            player.ApplyPlacements(changes);
            return new PlacementCommandResult(result.Outcome,PlacementRejectReason.None,player.PlacementRevision,result.TargetUnitId);
        }
    }
}
