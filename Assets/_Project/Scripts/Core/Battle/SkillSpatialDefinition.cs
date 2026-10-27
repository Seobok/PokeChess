using System;
using System.Collections.Generic;
using System.Linq;
using PokeChess.Core.Board;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Battle
{
    public enum SkillAreaCenter { Caster, CastTarget, DensestEnemies }
    public enum SpatialTargetMode { Area, NearestAllies, ChainEnemies }
    public sealed class SkillSpatialDefinition
    {
        public SpatialTargetMode Mode { get; }
        public SkillAreaShape Shape { get; }
        public SkillAreaCenter Center { get; }
        public int Extent { get; }
        public int MaxTargets { get; }
        public int SearchRange { get; }
        public int ChainRange { get; }
        public bool IncludeSelf { get; }
        public bool IncludePrimary { get; }
        public SkillSpatialDefinition(SpatialTargetMode mode=SpatialTargetMode.Area,SkillAreaShape shape=SkillAreaShape.Radius,
            SkillAreaCenter center=SkillAreaCenter.Caster,int extent=1,int maxTargets=0,int searchRange=56,int chainRange=1,bool includeSelf=false,bool includePrimary=true)
        {
            if(!Enum.IsDefined(typeof(SpatialTargetMode),mode)||!Enum.IsDefined(typeof(SkillAreaShape),shape)||
                !Enum.IsDefined(typeof(SkillAreaCenter),center)||extent<0||extent>56||maxTargets<0||maxTargets>56||
                searchRange<0||searchRange>56||chainRange<1||chainRange>56)throw new ArgumentOutOfRangeException();
            Mode=mode;Shape=shape;Center=center;Extent=extent;MaxTargets=maxTargets;SearchRange=searchRange;
            ChainRange=chainRange;IncludeSelf=includeSelf;
            IncludePrimary=includePrimary;
            if((shape==SkillAreaShape.Line||shape==SkillAreaShape.Cone)&&center!=SkillAreaCenter.Caster)
                throw new ArgumentException("Directional areas originate at the caster.");
        }
    }
    public static class SkillSpatialSelector
    {
        public static BoardPosition Densest(BattleState battle,UnitCombatState caster,int radius,int range,BoardPosition? preferred=null,
            SkillAreaShape shape=SkillAreaShape.Radius)
        {
            if(radius<0||range<0)throw new ArgumentOutOfRangeException();
            if(!preferred.HasValue||preferred.Value.Equals(caster.Position))
                preferred=battle.Units.FirstOrDefault(u=>u.UnitInstanceId==caster.CurrentTargetId&&u.IsTargetable)?.Position;
            var enemies=battle.Units.Where(u=>u.TeamId!=caster.TeamId&&u.IsTargetable).ToArray();
            return battle.Board.Cells.Where(p=>HexCoordinates.Distance(caster.Position,p)<=range)
                .Select(p=>new {Position=p,Hits=shape==SkillAreaShape.Radius?enemies.Count(u=>HexCoordinates.Distance(p,u.Position)<=radius):
                    enemies.Count(u=>SkillGeometry.Cells(shape,p,caster.Position,radius).Contains(u.Position))})
                .OrderByDescending(p=>p.Hits).ThenBy(p=>HexCoordinates.Distance(p.Position,preferred??caster.Position))
                .ThenBy(p=>p.Position.Row).ThenBy(p=>p.Position.Column).First().Position;
        }
        public static UnitCombatState Farthest(BattleState battle,UnitCombatState caster,int range,Func<UnitCombatState,bool> predicate=null)
        {
            var candidates=battle.Units.Where(u=>u.TeamId!=caster.TeamId&&u.IsTargetable&&
                HexCoordinates.Distance(caster.Position,u.Position)<=range&&(predicate==null||predicate(u))).ToArray();
            var taunt=CombatTargetSelector.ResolveTaunt(battle,caster,caster.EffectiveTauntSource,u=>candidates.Contains(u));
            if(taunt!=null)return taunt;
            var sorted=candidates.OrderByDescending(u=>HexCoordinates.Distance(caster.Position,u.Position))
                .ThenBy(u=>CombatTargetSelector.CenterDistanceScore(u.Position)).ThenBy(u=>u.UnitInstanceId,StringComparer.Ordinal).ToArray();
            if(sorted.Length==0)return null;
            var ties=sorted.Where(u=>HexCoordinates.Distance(caster.Position,u.Position)==HexCoordinates.Distance(caster.Position,sorted[0].Position)&&
                CombatTargetSelector.CenterDistanceScore(u.Position)==CombatTargetSelector.CenterDistanceScore(sorted[0].Position)).ToArray();
            return ties[ties.Length==1?0:battle.RngStreams.Target.NextInt(ties.Length)];
        }
        public static UnitCombatState[] Select(BattleState battle,UnitCombatState caster,UnitCombatState target,
            SkillSpatialDefinition spec,bool enemies,BoardPosition origin,BoardPosition center,BoardPosition aim)
        {
            var valid=battle.Units.Where(u=>u.IsAlive&&u.IsOnBoard&&(enemies?u.TeamId!=caster.TeamId&&u.IsTargetable:
                u.TeamId==caster.TeamId&&(spec.IncludeSelf||u!=caster))).ToArray();
            if(spec.Mode==SpatialTargetMode.ChainEnemies) {
                var selected=new List<UnitCombatState>();var current=target;
                if(current==null||!valid.Contains(current))return selected.ToArray();
                int count=spec.MaxTargets==0?valid.Length:spec.MaxTargets+(spec.IncludePrimary?0:1);
                while(current!=null&&selected.Count<count) {
                    selected.Add(current);var previous=current;
                    current=valid.Where(u=>!selected.Contains(u)&&HexCoordinates.Distance(previous.Position,u.Position)<=spec.ChainRange)
                        .OrderBy(u=>HexCoordinates.Distance(previous.Position,u.Position))
                        .ThenBy(u=>CombatTargetSelector.CenterDistanceScore(u.Position)).ThenBy(u=>u.UnitInstanceId,StringComparer.Ordinal).FirstOrDefault();
                }
                return (spec.IncludePrimary?selected:selected.Skip(1)).ToArray();
            }
            IEnumerable<UnitCombatState> result;
            if(spec.Mode==SpatialTargetMode.NearestAllies)
                result=valid.Where(u=>HexCoordinates.Distance(origin,u.Position)<=spec.SearchRange)
                    .OrderBy(u=>HexCoordinates.Distance(origin,u.Position)).ThenBy(u=>u.UnitInstanceId,StringComparer.Ordinal);
            else {
                var cells=new HashSet<BoardPosition>(SkillGeometry.Cells(spec.Shape,center,spec.Shape==SkillAreaShape.Cross?origin:aim,spec.Extent));
                result=valid.Where(u=>cells.Contains(u.Position)).OrderBy(u=>HexCoordinates.Distance(center,u.Position))
                    .ThenBy(u=>u.UnitInstanceId,StringComparer.Ordinal);
            }
            return (spec.MaxTargets==0?result:result.Take(spec.MaxTargets)).ToArray();
        }
    }
}
