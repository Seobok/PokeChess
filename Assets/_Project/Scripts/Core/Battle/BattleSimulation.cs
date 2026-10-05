using System;
using System.Collections.Generic;
using System.Linq;
using PokeChess.Core.Board;

namespace PokeChess.Core.Battle
{
    public sealed class BattleSimulation
    {
        private readonly BattleState battle;
        private readonly CombatBehaviorPolicy policy;
        private readonly UnitCombatState[] ordered;
        private readonly Dictionary<string,UnitCombatState> units;
        private readonly Dictionary<string,UnitActionRuntime> actions;
        private readonly Queue<Action<BattleState>> inputs = new Queue<Action<BattleState>>();
        private readonly List<CombatActionSignal> signals = new List<CombatActionSignal>();
        private bool stepping;
        private readonly HashSet<string> skillInterrupts = new HashSet<string>(StringComparer.Ordinal);
        public MovementReservations Reservations { get; } = new MovementReservations();
        public BattleSimulation(BattleState battle, CombatBehaviorPolicy policy = null, SkillCatalog skillCatalog = null, ISkillEffectExecutor skillExecutor = null)
        {
            this.battle=battle??throw new ArgumentNullException(nameof(battle));
            if(battle.HasSimulation)throw new InvalidOperationException("Battle already has a simulation owner.");
            if(policy!=null&&skillCatalog!=null)throw new ArgumentException("Provide policy or skill catalog.");
            this.policy=policy??(skillCatalog==null?new BasicAttackCombatBehaviorPolicy():new SkillCombatBehaviorPolicy(skillCatalog,skillExecutor));
            ordered=battle.Units.OrderBy(u=>u.UnitInstanceId,StringComparer.Ordinal).ToArray();
            units=ordered.ToDictionary(u=>u.UnitInstanceId,StringComparer.Ordinal);
            actions=ordered.ToDictionary(u=>u.UnitInstanceId,u=>new UnitActionRuntime(u.UnitInstanceId),StringComparer.Ordinal);
            battle.HasSimulation=true;
        }
        public void QueueSkillInterrupt(string unitId)
        {
            if(unitId==null||!units.ContainsKey(unitId))throw new ArgumentException(nameof(unitId));
            QueueInput(_=>{if(actions[unitId].IsSkillActive)skillInterrupts.Add(unitId);});
        }
        public UnitActionRuntime GetRuntime(string unitId)=>actions[unitId];
        // Host-validated simulation input hook; not a network command validation API.
        public void QueueInput(Action<BattleState> input)=>inputs.Enqueue(input??throw new ArgumentNullException(nameof(input)));
        public static int ToTicks(double seconds,int tickRate,bool minimumOne=true)
        {
            if(double.IsNaN(seconds)||double.IsInfinity(seconds)||seconds<0||tickRate<1)throw new ArgumentOutOfRangeException();
            return Math.Max(minimumOne?1:0,checked((int)Math.Ceiling(seconds*tickRate)));
        }
        public IReadOnlyList<CombatActionSignal> Step()
        {
            if(stepping)throw new InvalidOperationException("Reentrant Step.");
            if(battle.Result!=BattleResult.InProgress)return Array.Empty<CombatActionSignal>();
            stepping=true;
            try
            {
                signals.Clear();
                battle.BeginDamageTick();
                battle.BeginSkillTick();
                battle.Energy.BeginTick();
                battle.Projectiles.BeginTick();
                int count=inputs.Count;
                for(int i=0;i<count;i++)inputs.Dequeue()(battle);
                CleanupUnavailable();
                foreach(var unit in ordered)
                {
                    if(!unit.IsAlive||!unit.IsOnBoard)continue;
                    var runtime=actions[unit.UnitInstanceId];
                    if(unit.ActionState==CombatActionState.Idle) Decide(unit,runtime);
                    else Advance(unit,runtime);
                }
                policy.OnProjectilesTiming(battle);
                CleanupUnavailable(); // Effects may kill units already processed earlier in this tick.
                ReevaluateLostAttackTargets();
                ReevaluateLostSkillTargets();
                CleanupUnavailable();
                battle.CurrentTick=checked(battle.CurrentTick+1);
                return Array.AsReadOnly(signals.ToArray());
            }
            finally {stepping=false;}
        }
        private void CleanupUnavailable()
        {
            foreach(var unit in ordered)
                if(!unit.IsAlive||!unit.IsOnBoard)
                {
                    if(!unit.IsAlive&&unit.IsOnBoard)battle.TryRemoveUnit(unit.UnitInstanceId);
                    var runtime=actions[unit.UnitInstanceId];
                    if(runtime.SkillCast!=null&&!runtime.SkillCast.Finished)runtime.SkillCast.CancelReason=SkillCancelReason.CasterUnavailable;
                    if(runtime.EndTick>0||runtime.MoveDestination.HasValue)Finish(unit,runtime,true);
                    Reservations.Release(unit.UnitInstanceId);
                    runtime.IsAttackTargetLocked=false;runtime.MovementTargetId=null;
                    unit.CurrentTargetId=null;unit.ActionState=CombatActionState.Dead;
                }
        }
        private UnitCombatState Target(UnitCombatState unit)
        {
            if(unit.CurrentTargetId==null||!units.TryGetValue(unit.CurrentTargetId,out var target)||
                target.TeamId==unit.TeamId||!target.IsAlive||!target.IsOnBoard||!policy.IsTargetable(target))return null;
            return target;
        }
        private void Decide(UnitCombatState unit,UnitActionRuntime runtime)
        {
            var target=Target(unit);
            var forced=ForcedTarget(unit);
            bool retain=runtime.IsAttackTargetLocked && target!=null &&
                HexCoordinates.IsInRange(unit.Position,target.Position,unit.Stats.AttackRange);
            if(forced!=null && (target==null || forced.UnitInstanceId!=target.UnitInstanceId))retain=false;
            if(!retain)
            {
                runtime.IsAttackTargetLocked=false;
                unit.CurrentTargetId=forced!=null ? forced.UnitInstanceId : policy.SelectTarget(battle,unit);
                target=Target(unit);
            }
            if(TryStartDataSkill(unit,runtime))return;
            if(target==null){unit.CurrentTargetId=null;return;}
            if(battle.Energy.IsSkillReady(unit)&&policy.CanUseSkill(battle,unit)&&policy.TryGetSkillTiming(battle,unit,target,out var timing))
            {
                if(timing.DurationTicks<1)throw new InvalidOperationException("Invalid skill timing.");
                // Validate all scheduled ticks before consuming energy.
                checked { var end=battle.CurrentTick+timing.DurationTicks; var effect=battle.CurrentTick+timing.EffectOffsetTicks; }
                if(!battle.Energy.TryConsumeSkill(unit))return;
                runtime.SkillCast=null;
                runtime.IsSkillActive=true;
                Begin(unit,runtime,CombatActionState.Casting,timing);
                policy.OnSkillStarted(battle,unit,target);
                if(unit.IsAlive&&unit.IsOnBoard)Advance(unit,runtime);
                return;
            }
            if(HexCoordinates.IsInRange(unit.Position,target.Position,unit.Stats.AttackRange))
            {
                if(policy.CanAttack(battle,unit) && battle.CurrentTick>=runtime.NextAttackTick)
                {
                    Begin(unit,runtime,CombatActionState.Attacking,policy.GetAttackTiming(battle,unit));
                    Advance(unit,runtime);
                }
                return;
            }
            if(!policy.CanMove(battle,unit))return;
            var path=AttackPositionFinder.FindPosition(Reservations.Overlay(battle.Board),unit.UnitInstanceId,
                target.UnitInstanceId,unit.Stats.AttackRange);
            if(path.Status!=PathSearchStatus.PathFound)return;
            var next=path.Path[1];
            if(!Reservations.TryReserve(unit.UnitInstanceId,next))return;
            runtime.MoveDestination=next;
            runtime.MovementTargetId=target.UnitInstanceId;
            Begin(unit,runtime,CombatActionState.Moving,
                new CombatActionTiming(ToTicks(1.0/unit.Stats.MoveSpeed,battle.TickRate),0));
        }
        private UnitCombatState SkillTarget(UnitCombatState unit,SkillDefinition skill,string id)
        {
            if(skill.TargetRule==SkillTargetRule.Self)return unit.IsAlive&&unit.IsOnBoard?unit:null;
            if(id==null||!units.TryGetValue(id,out var target)||target.TeamId==unit.TeamId||!target.IsTargetable||!policy.IsTargetable(target))return null;
            return target;
        }
        private UnitCombatState SelectSkillTarget(UnitCombatState unit,SkillDefinition skill,bool checkRange)
        {
            if(skill.TargetRule==SkillTargetRule.Self)return unit;
            // Restrict candidates before distance/center/RNG selection.
            var id=CombatTargetSelector.Select(battle,unit,
                t=>policy.IsTargetable(t)&&(!checkRange||HexCoordinates.IsInRange(unit.Position,t.Position,skill.Range)),
                policy.GetTauntSource(unit));
            return SkillTarget(unit,skill,id);
        }
        private bool TryStartDataSkill(UnitCombatState unit,UnitActionRuntime runtime)
        {
            if(!battle.Energy.IsSkillReady(unit)||!policy.CanUseSkill(battle,unit)||
                !policy.TryGetSkillDefinition(battle,unit,out var skill))return false;
            var target=SelectSkillTarget(unit,skill,true);
            if(target==null)return false;
            int hit=ToTicks(skill.CastTime,battle.TickRate,false);
            int duration=Math.Max(1,checked(hit+ToTicks(skill.PostCastTime,battle.TickRate,false)));
            int lockTicks=ToTicks(skill.EnergyLock??unit.Stats.EnergyLockSeconds,battle.TickRate,false);
            var cast=new SkillCastRuntime(skill,target.UnitInstanceId,battle.CurrentTick,hit,duration,lockTicks);
            if(!battle.Energy.TryConsumeSkill(unit))return false;
            runtime.SkillCast=cast;runtime.IsSkillActive=true;runtime.IsAttackTargetLocked=false;
            Begin(unit,runtime,CombatActionState.Casting,new CombatActionTiming(duration,hit));
            battle.RecordSkill(new SkillEvent(unit.UnitInstanceId,cast,battle.CurrentTick,SkillEventKind.CastStarted,SkillCancelReason.None));
            policy.OnSkillStarted(battle,unit,target);
            if(unit.IsAlive&&unit.IsOnBoard)Advance(unit,runtime);
            return true;
        }
        private void AdvanceDataSkill(UnitCombatState unit,UnitActionRuntime runtime)
        {
            var cast=runtime.SkillCast;var skill=cast.Definition;
            var target=SkillTarget(unit,skill,cast.TargetId);
            if(!cast.EffectApplied&&target==null&&skill.TargetLostPolicy==TargetLostPolicy.Cancel)
            {cast.CancelReason=SkillCancelReason.TargetLost;Finish(unit,runtime,true);return;}
            if(!cast.EffectApplied&&battle.CurrentTick>=cast.EffectTick)
            {
                if(target==null&&skill.TargetLostPolicy==TargetLostPolicy.RetargetAtEffect)
                {target=SelectSkillTarget(unit,skill,true);cast.TargetId=target?.UnitInstanceId;}
                if(target==null&&skill.TargetLostPolicy!=TargetLostPolicy.ContinueWithoutTarget)
                {cast.CancelReason=SkillCancelReason.TargetLost;Finish(unit,runtime,true);return;}
                cast.EffectApplied=true;runtime.EffectApplied=true;
                Emit(unit,CombatActionSignalKind.TimingReached);
                policy.ExecuteSkillEffect(battle,unit,target,skill);
                battle.RecordSkill(new SkillEvent(unit.UnitInstanceId,cast,battle.CurrentTick,SkillEventKind.EffectApplied,SkillCancelReason.None));
            }
            if(unit.IsAlive&&unit.IsOnBoard&&battle.CurrentTick>=cast.EndTick)Finish(unit,runtime,false);
        }
        private void ReevaluateLostSkillTargets()
        {
            foreach(var unit in ordered) {
                var runtime=actions[unit.UnitInstanceId];var cast=runtime.SkillCast;
                if(!unit.IsAlive||!unit.IsOnBoard||cast==null||cast.Finished||cast.EffectApplied||
                    cast.Definition.TargetLostPolicy!=TargetLostPolicy.Cancel)continue;
                if(SkillTarget(unit,cast.Definition,cast.TargetId)==null) {
                    cast.CancelReason=SkillCancelReason.TargetLost;Finish(unit,runtime,true);
                }
            }
        }
        private void Begin(UnitCombatState unit,UnitActionRuntime runtime,CombatActionState state,CombatActionTiming timing)
        {
            if(timing.DurationTicks<1)throw new InvalidOperationException("Invalid action timing.");
            unit.ActionState=state;
            if(state==CombatActionState.Attacking)runtime.IsAttackTargetLocked=true;
            runtime.StartTick=battle.CurrentTick;
            runtime.EndTick=checked(battle.CurrentTick+timing.DurationTicks);
            if(state==CombatActionState.Attacking)runtime.NextAttackTick=runtime.EndTick;
            runtime.EffectTick=checked(battle.CurrentTick+timing.EffectOffsetTicks);
            runtime.EffectApplied=false;
            Emit(unit,CombatActionSignalKind.Started);
        }
        private void Advance(UnitCombatState unit,UnitActionRuntime runtime)
        {
            if(runtime.IsSkillActive&&skillInterrupts.Remove(unit.UnitInstanceId)&&
                (runtime.SkillCast==null||runtime.SkillCast.Definition.Interruptible))
            {
                if(runtime.SkillCast!=null)runtime.SkillCast.CancelReason=SkillCancelReason.Interrupted;
                Finish(unit,runtime,true);return;
            }
            if(unit.ActionState==CombatActionState.Casting&&runtime.SkillCast!=null&&!runtime.SkillCast.Finished)
            { AdvanceDataSkill(unit,runtime);return; }
            if(unit.ActionState==CombatActionState.Moving)
            {
                if(!policy.CanMove(battle,unit)||!runtime.MoveDestination.HasValue||
                    !Reservations.TryGetOwner(runtime.MoveDestination.Value,out var owner)||owner!=unit.UnitInstanceId)
                {Finish(unit,runtime,true);return;}
                if(battle.CurrentTick<runtime.EndTick)return;
                bool success=battle.TryMoveUnit(unit.UnitInstanceId,runtime.MoveDestination.Value)==BoardOperationResult.Success;
                Finish(unit,runtime,!success);return;
            }
            var target=Target(unit);
            if(unit.ActionState==CombatActionState.Attacking&&!runtime.EffectApplied &&
                (target==null||!policy.CanAttack(battle,unit)||
                 !HexCoordinates.IsInRange(unit.Position,target.Position,unit.Stats.AttackRange)))
            {CancelAttackAndReplan(unit,runtime);return;}
            var forced=ForcedTarget(unit);
            if(unit.ActionState==CombatActionState.Attacking && forced!=null && forced.UnitInstanceId!=unit.CurrentTargetId)
            {CancelAttackAndReplan(unit,runtime);return;}
            if(unit.ActionState==CombatActionState.Casting&&!policy.CanContinueSkill(battle,unit,target))
            {unit.CurrentTargetId=null;Finish(unit,runtime,true);return;}
            if(!runtime.EffectApplied&&battle.CurrentTick>=runtime.EffectTick)
            {
                runtime.EffectApplied=true;
                Emit(unit,CombatActionSignalKind.TimingReached);
                if(unit.ActionState==CombatActionState.Attacking)policy.OnAttackTiming(battle,unit,target);
                else if(unit.ActionState==CombatActionState.Casting)policy.OnSkillTiming(battle,unit,target);
            }
            if(!unit.IsAlive||!unit.IsOnBoard)return;
            if(battle.CurrentTick>=runtime.EndTick)Finish(unit,runtime,false);
        }
        private void Finish(UnitCombatState unit,UnitActionRuntime runtime,bool cancelled)
        {
            Emit(unit,cancelled?CombatActionSignalKind.Cancelled:CombatActionSignalKind.Completed);
            Reservations.Release(unit.UnitInstanceId);
            if(runtime.IsSkillActive) {
                var cast=runtime.SkillCast;
                if(cast!=null&&!cast.Finished) {
                    cast.Finished=true;
                    battle.RecordSkill(new SkillEvent(unit.UnitInstanceId,cast,battle.CurrentTick,
                        cancelled?SkillEventKind.CastCancelled:SkillEventKind.CastCompleted,cast.CancelReason));
                }
                battle.Energy.EndCast(unit,cast==null?(int?)null:cast.EnergyLockTicks);
                runtime.IsSkillActive=false;
            }
            skillInterrupts.Remove(unit.UnitInstanceId);
            if(unit.ActionState==CombatActionState.Moving)
            {runtime.MovementTargetId=null;unit.CurrentTargetId=null;}
            if(cancelled)runtime.IsAttackTargetLocked=false;
            runtime.MoveDestination=null;runtime.EndTick=0;
            unit.ActionState=CombatActionState.Idle;
        }
        private UnitCombatState ForcedTarget(UnitCombatState unit) =>
            CombatTargetSelector.ResolveTaunt(battle,unit,policy.GetTauntSource(unit),policy.IsTargetable);
        private void CancelAttackAndReplan(UnitCombatState unit,UnitActionRuntime runtime)
        {
            Finish(unit,runtime,true);
            unit.CurrentTargetId=null;
            runtime.IsAttackTargetLocked=false;
            if(unit.IsAlive&&unit.IsOnBoard)Decide(unit,runtime);
        }
        private void ReevaluateLostAttackTargets()
        {
            foreach(var unit in ordered)
            {
                var runtime=actions[unit.UnitInstanceId];
                if(!unit.IsAlive||!unit.IsOnBoard||!runtime.IsAttackTargetLocked||
                    (unit.ActionState!=CombatActionState.Attacking&&unit.ActionState!=CombatActionState.Idle))continue;
                var target=Target(unit);
                var forced=ForcedTarget(unit);
                if(target==null||!HexCoordinates.IsInRange(unit.Position,target.Position,unit.Stats.AttackRange)||
                    (forced!=null&&forced.UnitInstanceId!=unit.CurrentTargetId))
                    CancelAttackAndReplan(unit,runtime);
            }
        }
        private void Emit(UnitCombatState unit,CombatActionSignalKind kind)=>
            signals.Add(new CombatActionSignal(battle.CurrentTick,unit.UnitInstanceId,unit.ActionState,kind));
    }
}
