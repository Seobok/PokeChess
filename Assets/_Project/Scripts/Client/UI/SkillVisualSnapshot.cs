using System;
using System.Linq;
using UnityEngine;
using PokeChess.Core.Battle;
using PokeChess.Core.Match;
namespace PokeChess.Client.UI
{
    // Both presentation paths produce the same visual input; no simulation runs here.
    internal sealed class SkillVisualUnit
    {
        public string Id,Skill,Target; public int Column,Row;
        public bool Alive,Targetable,Guarded;
        public Vector2 Position;
        public CombatActionState Action;
        public long Cast,Start,Effect,End;
        public CombatStatusView[] Statuses;
        public CombatShieldView[] Shields;
    }
    internal static class SkillVisualSnapshot
    {
        public static SkillVisualUnit[] Playback(CombatFrame frame,double tick,Func<int,int,Vector2> point)
            =>frame.units.Select(u=>new SkillVisualUnit {
                Column=u.column,Row=u.row,Guarded=u.guarded,Id=u.id,Skill=u.skillId,Target=u.target,Alive=u.hp>0&&u.onBoard,Targetable=u.hp>0&&u.onBoard&&!u.untargetable,
                Action=u.action,Cast=u.castId,Start=u.startTick,Effect=u.effectTick,End=u.endTick,Statuses=u.statusDetails??Array.Empty<CombatStatusView>(),Shields=u.shields??Array.Empty<CombatShieldView>(),
                Position=ThrowPresentation.Playback(frame,u.id,tick,u.action==CombatActionState.Moving?Vector2.Lerp(point(u.column,u.row),point(u.moveColumn,u.moveRow),
                    (float)((tick-u.startTick)/Math.Max(1,u.endTick-u.startTick))):point(u.column,u.row),point)
            }).ToArray();
        public static SkillVisualUnit[] Local(LocalPairBattle pair,double tick,Func<int,int,Vector2> point)
            =>Local(pair.Battle,pair.GetActionRuntime,tick,point);
        public static SkillVisualUnit[] Local(BattleState battle,Func<string,UnitActionRuntime> runtime,double tick,Func<int,int,Vector2> point)
            =>battle.Units.Select(u=>{
                var action=runtime(u.UnitInstanceId);var cast=action.SkillCast;
                var position=point(u.Position.Column,u.Position.Row);
                if(action.MoveDestination.HasValue)position=Vector2.Lerp(position,point(action.MoveDestination.Value.Column,action.MoveDestination.Value.Row),
                    (float)((tick-action.StartTick)/Math.Max(1,action.EndTick-action.StartTick)));
                position=ThrowPresentation.Local(battle,u.UnitInstanceId,position,point);
                return new SkillVisualUnit {Column=u.Position.Column,Row=u.Position.Row,Guarded=battle.Synergies.IsGuarded(u),Id=u.UnitInstanceId,Skill=cast?.Definition.Id,Target=cast?.TargetId??u.CurrentTargetId,
                    Alive=u.IsAlive&&u.IsOnBoard,Targetable=u.IsTargetable,Position=position,Action=u.ActionState,
                    Cast=cast?.CastId??0,Start=cast?.StartTick??action.StartTick,Effect=cast?.EffectTick??action.EffectTick,End=cast?.EndTick??action.EndTick,
                    Shields=battle.Shields.Active.Where(s=>s.TargetId==u.UnitInstanceId).Select(s=>new CombatShieldView{id=s.Id,source=s.SourceId,group=s.Group,remaining=s.Remaining,endTick=s.ExpireTick}).ToArray(),
                    Statuses=battle.StatusEffects.Active.Where(s=>s.TargetId==u.UnitInstanceId).Select(s=>new CombatStatusView {
                        id=s.InstanceId,definition=s.Definition.Id,source=s.SourceId,startTick=s.StartTick,endTick=s.ExpireTick
                    }).ToArray()};
            }).ToArray();
    }
}
