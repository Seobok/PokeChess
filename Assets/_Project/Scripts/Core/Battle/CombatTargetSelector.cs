using System;
using System.Linq;
using PokeChess.Core.Board;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Battle
{
    public static class CombatTargetSelector
    {
        // Pointy-top Odd-R cell center: (sqrt(3)*(col + parity/2), 1.5*row).
        // 7x8 center = (sqrt(3)*3.25, 1.5*3.5). Score = 16 * squared physical distance.
        public static int CenterDistanceScore(BoardPosition position)
        {
            if(!HexBoardBounds.Combat.Contains(position))throw new ArgumentOutOfRangeException(nameof(position));
            int dx=4*position.Column+2*(position.Row&1)-13;
            int dy=2*position.Row-7;
            return 3*dx*dx+9*dy*dy;
        }

        public static UnitCombatState ResolveTaunt(BattleState battle, UnitCombatState unit,
            string sourceId, Func<UnitCombatState,bool> targetable)
        {
            if(sourceId==null)return null;
            return battle.Units.FirstOrDefault(t=>t.UnitInstanceId==sourceId&&
                t.TeamId!=unit.TeamId&&t.IsTargetable&&targetable(t));
        }

        public static string Select(BattleState battle, UnitCombatState unit,
            Func<UnitCombatState,bool> targetable=null, string tauntSourceId=null)
        {
            if(battle==null)throw new ArgumentNullException(nameof(battle));
            if(unit==null||!battle.Units.Contains(unit))throw new ArgumentException("Unit must belong to battle.",nameof(unit));
            if(!unit.IsAlive||!unit.IsOnBoard)return null;
            targetable=targetable??(t=>t.IsTargetable);
            tauntSourceId=tauntSourceId??unit.TauntSourceId;
            var taunt=ResolveTaunt(battle,unit,tauntSourceId,targetable);
            if(taunt!=null)return taunt.UnitInstanceId;
            var candidates=battle.Units.Where(t=>t.TeamId!=unit.TeamId&&t.IsTargetable&&targetable(t))
                .Select(t=>new {Unit=t, Distance=HexCoordinates.Distance(unit.Position,t.Position), Center=CenterDistanceScore(t.Position)})
                .OrderBy(t=>t.Distance).ThenBy(t=>t.Center).ThenBy(t=>t.Unit.UnitInstanceId,StringComparer.Ordinal).ToArray();
            if(candidates.Length==0)return null;
            var tied=candidates.Where(t=>t.Distance==candidates[0].Distance&&t.Center==candidates[0].Center).ToArray();
            return tied[tied.Length==1?0:battle.RngStreams.Target.NextInt(tied.Length)].Unit.UnitInstanceId;
        }
    }
}
