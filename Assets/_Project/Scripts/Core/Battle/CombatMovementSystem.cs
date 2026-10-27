using System;
using System.Collections.Generic;
using System.Linq;
using PokeChess.Core.Board;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Battle
{
    public enum SkillMovementKind { DashToTarget, JumpToTarget, TeleportSafe, JumpToCluster, Knockback, Throw, Confusion, Retreat, BurrowToTarget, CarryTarget }
    public sealed class SkillMovementDefinition
    {
        public SkillMovementKind Kind { get; }
        public int Distance { get; }
        public float Speed { get; }
        public float? TravelTime { get; }
        public int ClusterRadius { get; }
        public IReadOnlyList<SkillEffectDefinition> ImpactEffects { get; }
        public bool Forced=>Kind==SkillMovementKind.Knockback||Kind==SkillMovementKind.Throw;
        public SkillMovementDefinition(SkillMovementKind kind,int distance=1,float speed=6,int clusterRadius=1,
            IEnumerable<SkillEffectDefinition> impactEffects=null,float? travelTime=null)
        {
            if(!Enum.IsDefined(typeof(SkillMovementKind),kind)||kind==SkillMovementKind.Confusion||distance<1||distance>56||clusterRadius<0||clusterRadius>56)
                throw new ArgumentOutOfRangeException();
            if(travelTime.HasValue)DamageCalculator.Validate(travelTime.Value,true);TravelTime=travelTime;
            DamageCalculator.Validate(speed,true);if(speed==0)throw new ArgumentOutOfRangeException(nameof(speed));
            var effects=(impactEffects??Array.Empty<SkillEffectDefinition>()).ToArray();
            if(effects.Any(e=>e==null||e.Type==SkillEffectType.Move||e.Type==SkillEffectType.PathProjectileDamage))
                throw new ArgumentException("Movement impacts cannot start another movement or path projectile.");
            Kind=kind;Distance=distance;Speed=speed;ClusterRadius=clusterRadius;ImpactEffects=Array.AsReadOnly(effects);
        }
    }
    public enum CombatMovementEventKind { Started, Arrived, Blocked, Cancelled }
    public sealed class CombatMovementSnapshot
    {
        public long Id { get; }
        public long CastId { get; }
        public int StepIndex { get; }
        public int Iteration { get; }
        public string SourceId { get; }
        public string UnitId { get; }
        public SkillMovementKind Kind { get; }
        public long StartTick { get; }
        public long EndTick { get; }
        public IReadOnlyList<BoardPosition> Path { get; }
        internal CombatMovementSnapshot(long id,string source,string unit,SkillMovementKind kind,long start,long end,IEnumerable<BoardPosition> path,long castId=0,int stepIndex=0,int iteration=0)
        {Id=id;CastId=castId;StepIndex=stepIndex;Iteration=iteration;SourceId=source;UnitId=unit;Kind=kind;StartTick=start;EndTick=end;Path=Array.AsReadOnly(path.ToArray());}
    }
    public readonly struct CombatMovementEvent
    {
        public CombatMovementSnapshot Movement { get; }
        public CombatMovementEventKind Kind { get; }
        public long Tick { get; }
        internal CombatMovementEvent(CombatMovementSnapshot movement,CombatMovementEventKind kind,long tick)
        {Movement=movement;Kind=kind;Tick=tick;}
    }
    public sealed class CombatMovementSystem
    {
        private sealed class Pending
        {public CombatMovementSnapshot View;public UnitCombatState Unit;public Action Impact;public UnitCombatState Companion;public CombatMovementSnapshot CompanionView;}
        private readonly BattleState battle;
        private readonly List<Pending> active=new List<Pending>();
        private readonly List<CombatMovementEvent> events=new List<CombatMovementEvent>();
        private long nextId;
        public MovementReservations Reservations { get; }=new MovementReservations();
        internal event Action<UnitCombatState,bool> Relocating;
        internal event Action<UnitCombatState> TravelInterrupted;
        public IReadOnlyList<CombatMovementSnapshot> Active=>Array.AsReadOnly(active.SelectMany(p=>p.CompanionView==null?new[]{p.View}:new[]{p.View,p.CompanionView}).ToArray());
        public IReadOnlyList<CombatMovementEvent> EventsThisTick=>Array.AsReadOnly(events.ToArray());
        internal CombatMovementSystem(BattleState battle){this.battle=battle;}
        public bool IsTravelling(UnitCombatState unit)=>active.Any(p=>p.Unit==unit||p.Companion==unit);
        public long ArrivalTick(UnitCombatState unit)=>active.Where(p=>p.Unit==unit||p.Companion==unit).Select(p=>p.View.EndTick).DefaultIfEmpty(battle.CurrentTick).Max();
        private bool Empty(BoardPosition p,UnitCombatState mover)=>battle.Board.Contains(p)&&
            (!battle.Board.IsOccupied(p)||p.Equals(mover.Position))&&
            (!Reservations.TryGetOwner(p,out var owner)||owner==mover.UnitInstanceId);
        public bool IsSurrounded(UnitCombatState unit,int minimum=2)=>battle.Units.Count(u=>u.TeamId!=unit.TeamId&&
            u.IsTargetable&&HexCoordinates.Distance(unit.Position,u.Position)==1)>=minimum;
        public BoardPosition? Safest(UnitCombatState unit)
        {
            var enemies=battle.Units.Where(u=>u.TeamId!=unit.TeamId&&u.IsTargetable).ToArray();if(enemies.Length==0)return null;
            return battle.Board.Cells.Where(p=>!p.Equals(unit.Position)&&Empty(p,unit))
                .OrderByDescending(p=>enemies.Min(u=>HexCoordinates.Distance(p,u.Position))).ThenBy(p=>p.Row).ThenBy(p=>p.Column)
                .Select(p=>(BoardPosition?)p).FirstOrDefault();
        }
        public bool TryStart(UnitCombatState caster,UnitCombatState target,SkillMovementDefinition definition,Action impact=null,bool generated=false,long castId=0,int stepIndex=0,int iteration=0,
            BoardPosition? sourceOrigin=null)
        {
            if(caster==null||definition==null||!battle.Units.Contains(caster)||
                (!generated||!definition.Forced)&&(!caster.IsAlive||!caster.IsOnBoard))return false;
            var subject=definition.Forced?target:caster;
            if(subject==null||!battle.Units.Contains(subject)||!subject.IsAlive||!subject.IsOnBoard)return false;
            if(definition.Forced&&(subject.TeamId==caster.TeamId||!subject.IsTargetable))return false;
            bool blocked=!definition.Forced&&(subject.HasCrowdControl(CrowdControlKind.Root)||subject.HasCrowdControl(CrowdControlKind.Stun)||subject.StatusSystem.BlocksMovement(subject));
            if(IsTravelling(subject)&&!definition.Forced)blocked=true;
            if(definition.Kind==SkillMovementKind.CarryTarget)return !blocked&&TryCarry(caster,target,definition,impact,castId,stepIndex,iteration);
            var start=subject.Position;IReadOnlyList<BoardPosition> path=null;
            if(!blocked&&definition.Forced) {
                var a=HexCoordinates.ToAxial(sourceOrigin??caster.Position);var b=HexCoordinates.ToAxial(start);
                var ray=SkillGeometry.Ray(start,new AxialPosition(b.Q-a.Q,b.R-a.R),definition.Distance);
                if(definition.Kind==SkillMovementKind.Throw) {
                    var end=ray.Reverse().Where(p=>Empty(p,subject)).Select(p=>(BoardPosition?)p).FirstOrDefault();
                    if(end.HasValue)path=new[]{start,end.Value};
                } else {
                    var cells=new List<BoardPosition>{start};
                    foreach(var p in ray){if(!Empty(p,subject))break;cells.Add(p);}path=cells;
                }
            }
            else if(!blocked&&definition.Kind==SkillMovementKind.Retreat) {
                if(target!=null) {var a=HexCoordinates.ToAxial(start);var t=HexCoordinates.ToAxial(target.Position);
                    var end=SkillGeometry.Ray(start,new AxialPosition(a.Q-t.Q,a.R-t.R),1).FirstOrDefault();
                    if(SkillGeometry.Ray(start,new AxialPosition(a.Q-t.Q,a.R-t.R),1).Count>0&&Empty(end,subject))path=new[]{start,end};}
            }
            else if(!blocked&&definition.Kind==SkillMovementKind.BurrowToTarget) {
                if(target!=null&&target.IsTargetable) {var a=HexCoordinates.ToAxial(start);var t=HexCoordinates.ToAxial(target.Position);
                    var back=SkillGeometry.Ray(target.Position,new AxialPosition(t.Q-a.Q,t.R-a.R),1);
                    var end=back.Where(p=>Empty(p,subject)).Select(p=>(BoardPosition?)p).FirstOrDefault()??battle.Board.Cells.Where(p=>Empty(p,subject)).OrderBy(p=>HexCoordinates.Distance(target.Position,p)).ThenBy(p=>p.Row).ThenBy(p=>p.Column).Select(p=>(BoardPosition?)p).FirstOrDefault();
                    if(end.HasValue)path=new[]{start,end.Value};}
            }
            else if(!blocked&&(definition.Kind==SkillMovementKind.DashToTarget||definition.Kind==SkillMovementKind.JumpToTarget)) {
                if(target!=null&&target.IsTargetable&&target.TeamId!=caster.TeamId) {
                    var candidates=battle.Board.Cells.Where(p=>HexCoordinates.Distance(p,target.Position)==1&&Empty(p,subject));
                    if(definition.Kind==SkillMovementKind.DashToTarget)
                        path=candidates.Select(p=>HexPathfinder.FindPath(Reservations.Overlay(battle.Board),start,p))
                            .Where(p=>p.Succeeded).OrderBy(p=>p.MoveCount).ThenBy(p=>p.Destination.Value.Row)
                            .ThenBy(p=>p.Destination.Value.Column).Select(p=>(IReadOnlyList<BoardPosition>)p.Path).FirstOrDefault();
                    else {var end=candidates.OrderBy(p=>HexCoordinates.Distance(start,p)).ThenBy(p=>p.Row).ThenBy(p=>p.Column)
                            .Select(p=>(BoardPosition?)p).FirstOrDefault();if(end.HasValue)path=new[]{start,end.Value};}
                }
            }
            else if(!blocked) {
                BoardPosition? end;
                if(definition.Kind==SkillMovementKind.TeleportSafe)end=Safest(subject);
                else {
                    var center=SkillSpatialSelector.Densest(battle,caster,definition.ClusterRadius,56,target?.Position);
                    end=battle.Board.Cells.Where(p=>Empty(p,subject)).OrderBy(p=>HexCoordinates.Distance(center,p))
                        .ThenBy(p=>p.Row).ThenBy(p=>p.Column).Select(p=>(BoardPosition?)p).FirstOrDefault();
                }
                if(end.HasValue)path=new[]{start,end.Value};
            }
            if(path==null||path.Count==0||path.Last().Equals(start)&&definition.Forced) {
                Record(new CombatMovementSnapshot(checked(++nextId),caster.UnitInstanceId,subject.UnitInstanceId,definition.Kind,
                    battle.CurrentTick,battle.CurrentTick,new[]{start},castId,stepIndex,iteration),CombatMovementEventKind.Blocked);return false;
            }
            if(IsTravelling(subject))Cancel(subject);
            Relocating?.Invoke(subject,definition.Forced);Reservations.Release(subject.UnitInstanceId);
            var destination=path.Last();int distance=definition.Kind==SkillMovementKind.DashToTarget?path.Count-1:HexCoordinates.Distance(start,destination);
            int ticks=definition.TravelTime.HasValue?BattleSimulation.ToTicks(definition.TravelTime.Value,battle.TickRate,false):definition.Kind==SkillMovementKind.TeleportSafe||definition.Kind==SkillMovementKind.Retreat||definition.Kind==SkillMovementKind.BurrowToTarget||distance==0?0:BattleSimulation.ToTicks(distance/(double)definition.Speed,battle.TickRate);
            var view=new CombatMovementSnapshot(checked(++nextId),caster.UnitInstanceId,subject.UnitInstanceId,definition.Kind,
                battle.CurrentTick,checked(battle.CurrentTick+ticks),path,castId,stepIndex,iteration);
            if(!destination.Equals(start)&&!Reservations.TryReserve(subject.UnitInstanceId,destination)){Record(view,CombatMovementEventKind.Blocked);return false;}
            Record(view,CombatMovementEventKind.Started);
            var pending=new Pending{View=view,Unit=subject,Impact=impact};
            if(ticks==0)Arrive(pending);else active.Add(pending);
            return true;
        }
        private bool TryCarry(UnitCombatState caster,UnitCombatState target,SkillMovementDefinition def,Action impact,long castId,int step,int iteration) {
            if(target==null||target.TeamId==caster.TeamId||!target.IsTargetable)return false;
            var a=HexCoordinates.ToAxial(caster.Position);var t=HexCoordinates.ToAxial(target.Position);
            var ray=SkillGeometry.Ray(target.Position,new AxialPosition(t.Q-a.Q,t.R-a.R),def.Distance);
            var end=ray.Reverse().Where(p=>Empty(p,target)).Select(p=>(BoardPosition?)p).FirstOrDefault();if(!end.HasValue)return false;
            var landing=battle.Board.Cells.Where(p=>HexCoordinates.Distance(p,end.Value)==1&&(Empty(p,caster)||p.Equals(target.Position)))
                .OrderBy(p=>HexCoordinates.Distance(caster.Position,p)).ThenBy(p=>p.Row).ThenBy(p=>p.Column).Select(p=>(BoardPosition?)p).FirstOrDefault();
            if(!landing.HasValue)return false;
            Cancel(caster);Cancel(target);Relocating?.Invoke(caster,false);Relocating?.Invoke(target,true);
            if(!landing.Value.Equals(caster.Position)&&!Reservations.TryReserve(caster.UnitInstanceId,landing.Value))return false;
            if(!Reservations.TryReserve(target.UnitInstanceId,end.Value)){Reservations.Release(caster.UnitInstanceId);return false;}
            long endTick=checked(battle.CurrentTick+BattleSimulation.ToTicks(HexCoordinates.Distance(caster.Position,landing.Value)/def.Speed,battle.TickRate));
            var view=new CombatMovementSnapshot(checked(++nextId),caster.UnitInstanceId,caster.UnitInstanceId,SkillMovementKind.CarryTarget,battle.CurrentTick,endTick,new[]{caster.Position,landing.Value},castId,step,iteration);
            var other=new CombatMovementSnapshot(checked(++nextId),caster.UnitInstanceId,target.UnitInstanceId,SkillMovementKind.Throw,battle.CurrentTick,endTick,new[]{target.Position,end.Value},castId,step,iteration);
            Record(view,CombatMovementEventKind.Started);Record(other,CombatMovementEventKind.Started);
            active.Add(new Pending{View=view,Unit=caster,Companion=target,CompanionView=other,Impact=impact});return true;
        }
        internal void Confuse(UnitCombatState unit,BoardPosition destination,string source)
        {
            if(!unit.IsAlive||!unit.IsOnBoard||IsTravelling(unit)||!Empty(destination,unit))return;
            Relocating?.Invoke(unit,true);Reservations.Release(unit.UnitInstanceId);
            var view=new CombatMovementSnapshot(checked(++nextId),source,unit.UnitInstanceId,SkillMovementKind.Confusion,
                battle.CurrentTick,battle.CurrentTick,new[]{unit.Position,destination});
            Record(view,CombatMovementEventKind.Started);
            Record(view,battle.TryMoveUnit(unit.UnitInstanceId,destination)==BoardOperationResult.Success?CombatMovementEventKind.Arrived:CombatMovementEventKind.Blocked);
        }
        internal void BeginTick(){events.Clear();}
        internal void Advance()
        {
            foreach(var p in active.OrderBy(p=>p.View.Id).ToArray()) {
                if(!p.Unit.IsAlive||!p.Unit.IsOnBoard||p.Companion!=null&&(!p.Companion.IsAlive||!p.Companion.IsOnBoard)){Cancel(p.Unit);continue;}
                if(p.View.Kind!=SkillMovementKind.Throw&&p.View.Kind!=SkillMovementKind.Knockback&&
                    (p.Unit.HasCrowdControl(CrowdControlKind.Root)||p.Unit.HasCrowdControl(CrowdControlKind.Stun)||p.Unit.StatusSystem.BlocksMovement(p.Unit)))
                {Cancel(p.Unit);TravelInterrupted?.Invoke(p.Unit);continue;}
                if(p.View.EndTick<=battle.CurrentTick){active.Remove(p);Arrive(p);}
            }
        }
        private void Arrive(Pending p)
        {
            var end=p.View.Path.Last();
            if(p.Companion!=null) {
                var otherEnd=p.CompanionView.Path.Last();bool valid=Empty(otherEnd,p.Companion)&&(Empty(end,p.Unit)||end.Equals(p.Companion.Position));
                Reservations.Release(p.Companion.UnitInstanceId);Reservations.Release(p.Unit.UnitInstanceId);
                if(!valid){Record(p.View,CombatMovementEventKind.Blocked);Record(p.CompanionView,CombatMovementEventKind.Blocked);return;}
                battle.TryMoveUnit(p.Companion.UnitInstanceId,otherEnd);battle.TryMoveUnit(p.Unit.UnitInstanceId,end);
                Record(p.View,CombatMovementEventKind.Arrived);Record(p.CompanionView,CombatMovementEventKind.Arrived);p.Impact?.Invoke();return;
            }
            bool clear=Empty(end,p.Unit);
            if(p.View.Kind==SkillMovementKind.DashToTarget)
                clear&=p.View.Path.Skip(1).All(c=>Empty(c,p.Unit));
            Reservations.Release(p.Unit.UnitInstanceId);
            if(!clear||battle.TryMoveUnit(p.Unit.UnitInstanceId,end)!=BoardOperationResult.Success){Record(p.View,CombatMovementEventKind.Blocked);return;}
            Record(p.View,CombatMovementEventKind.Arrived);p.Impact?.Invoke();
        }
        internal void Cancel(UnitCombatState unit)
        {
            foreach(var p in active.Where(p=>p.Unit==unit||p.Companion==unit).ToArray()){active.Remove(p);Record(p.View,CombatMovementEventKind.Cancelled);Reservations.Release(p.Unit.UnitInstanceId);if(p.Companion!=null){Reservations.Release(p.Companion.UnitInstanceId);Record(p.CompanionView,CombatMovementEventKind.Cancelled);}}
            Reservations.Release(unit.UnitInstanceId);
        }
        internal void EndBattle(){foreach(var p in active.ToArray())Cancel(p.Unit);}
        private void Record(CombatMovementSnapshot movement,CombatMovementEventKind kind)=>events.Add(new CombatMovementEvent(movement,kind,battle.CurrentTick));
    }
}
