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
        private readonly List<Action> scheduledAttacks = new List<Action>();
        private bool stepping;
        private long nextCastId;
        private readonly HashSet<string> reportedDeaths = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> skillInterrupts = new HashSet<string>(StringComparer.Ordinal);
        public MovementReservations Reservations => battle.Movement.Reservations;
        public BattleSimulation(BattleState battle, CombatBehaviorPolicy policy = null, SkillCatalog skillCatalog = null, ISkillEffectExecutor skillExecutor = null, StatusCatalog statusCatalog = null)
        {
            this.battle=battle??throw new ArgumentNullException(nameof(battle));
            if(battle.HasSimulation)throw new InvalidOperationException("Battle already has a simulation owner.");
            if(policy!=null&&skillCatalog!=null)throw new ArgumentException("Provide policy or skill catalog.");
            this.policy=policy??(skillCatalog==null?new BasicAttackCombatBehaviorPolicy():new SkillCombatBehaviorPolicy(skillCatalog,skillExecutor));
            ordered=battle.Units.OrderBy(u=>u.UnitInstanceId,StringComparer.Ordinal).ToArray();
            units=ordered.ToDictionary(u=>u.UnitInstanceId,StringComparer.Ordinal);
            actions=ordered.ToDictionary(u=>u.UnitInstanceId,u=>new UnitActionRuntime(u.UnitInstanceId),StringComparer.Ordinal);
            if(statusCatalog!=null)battle.StatusEffects.UseCatalog(statusCatalog);
            battle.StatusEffects.Changed += OnStatusChanged;
            battle.Movement.Relocating += OnRelocating;
            battle.Movement.TravelInterrupted += unit=>{
                var runtime=actions[unit.UnitInstanceId];
                if(runtime.IsSkillActive){if(runtime.SkillCast!=null)runtime.SkillCast.CancelReason=SkillCancelReason.Interrupted;Finish(unit,runtime,true);}
            };
            battle.HasSimulation=true;
            battle.Synergies.Initialize();
        }
        private bool CanMove(UnitCombatState u)=>!u.HasCrowdControl(CrowdControlKind.Stun)&&
            !u.HasCrowdControl(CrowdControlKind.Root)&&!battle.StatusEffects.BlocksMovement(u)&&policy.CanMove(battle,u);
        private bool CanAttack(UnitCombatState u)=>!u.HasCrowdControl(CrowdControlKind.Stun)&&
            !u.HasCrowdControl(CrowdControlKind.Disarm)&&!battle.StatusEffects.BlocksAttacks(u)&&!battle.StatusEffects.BlocksActions(u)&&policy.CanAttack(battle,u);
        private bool CanUseSkill(UnitCombatState u)=>!u.HasCrowdControl(CrowdControlKind.Stun)&&
            !u.HasCrowdControl(CrowdControlKind.Silence)&&!battle.StatusEffects.BlocksActions(u)&&policy.CanUseSkill(battle,u);
        private void OnRelocating(UnitCombatState unit,bool forced)
        {
            var runtime=actions[unit.UnitInstanceId];
            if(unit.ActionState==CombatActionState.Moving||unit.ActionState==CombatActionState.Attacking||
                forced&&runtime.IsSkillActive&&(runtime.SkillCast==null||runtime.SkillCast.Definition.Interruptible)) {
                if(runtime.SkillCast!=null&&runtime.IsSkillActive)runtime.SkillCast.CancelReason=SkillCancelReason.Interrupted;
                Finish(unit,runtime,true);
            }
            runtime.IsAttackTargetLocked=false;runtime.MovementTargetId=null;runtime.MoveDestination=null;
            Reservations.Release(unit.UnitInstanceId);
        }
        private void OnStatusChanged(UnitCombatState u)
        {
            if(!u.IsAlive||!u.IsOnBoard)return;
            var runtime=actions[u.UnitInstanceId];bool stun=u.HasCrowdControl(CrowdControlKind.Stun)||battle.StatusEffects.BlocksActions(u);
            if(u.ActionState==CombatActionState.Moving&&(stun||u.HasCrowdControl(CrowdControlKind.Root)||battle.StatusEffects.BlocksMovement(u)))
                Finish(u,runtime,true);
            else if(u.ActionState==CombatActionState.Attacking&&(stun||
                (!runtime.EffectApplied&&u.HasCrowdControl(CrowdControlKind.Disarm))))
                Finish(u,runtime,true);
            else if(runtime.IsSkillActive&&(u.HasCrowdControl(CrowdControlKind.Stun)||battle.StatusEffects.HasBehavior(u,StatusBehavior.Sleep)||
                battle.StatusEffects.HasBehavior(u,StatusBehavior.Freeze))&&(runtime.SkillCast==null||runtime.SkillCast.Definition.Interruptible)) {
                if(runtime.SkillCast!=null)runtime.SkillCast.CancelReason=SkillCancelReason.Interrupted;
                Finish(u,runtime,true);
            }
        }
        public void QueueSkillInterrupt(string unitId)
        {
            if(unitId==null||!units.ContainsKey(unitId))throw new ArgumentException(nameof(unitId));
            QueueInput(_=>{if(actions[unitId].IsSkillActive) {
                var runtime=actions[unitId];
                if(runtime.SkillCast==null||runtime.SkillCast.Definition.Interruptible)battle.Movement.Cancel(units[unitId]);
                skillInterrupts.Add(unitId);
            }});
        }
        public UnitActionRuntime GetRuntime(string unitId)=>actions[unitId];
        // Host-validated simulation input hook; not a network command validation API.
        public void QueueInput(Action<BattleState> input)
        {
            if(battle.Result!=BattleResult.InProgress)throw new InvalidOperationException("Battle has ended.");
            inputs.Enqueue(input??throw new ArgumentNullException(nameof(input)));
        }
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
                scheduledAttacks.Clear();
                battle.BeginLifecycleTick();
                battle.BeginDamageTick();
                battle.BeginSkillTick();
                battle.SkillTimeline.BeginTick();
                battle.Synergies.BeginTick();
                battle.StatusEffects.BeginTick();
                battle.Energy.BeginTick();
                battle.Items.BeginTick();
                battle.Projectiles.BeginTick();
                battle.PathProjectiles.BeginTick();
                battle.Movement.BeginTick();
                battle.Shields.BeginTick();
                CleanupUnavailable();
                if(TryEndBattle())return Array.AsReadOnly(signals.ToArray());
                if(!battle.IsOvertime&&battle.CurrentTick>=battle.OvertimeStartTick) {
                    battle.IsOvertime=true;
                    battle.RecordLifecycle(new BattleLifecycleEvent(BattleLifecycleEventKind.OvertimeStarted,battle.CurrentTick));
                }
                int count=inputs.Count;
                for(int i=0;i<count;i++)inputs.Dequeue()(battle);
                CleanupUnavailable();
                if(TryEndBattle())return Array.AsReadOnly(signals.ToArray());
                battle.StatusEffects.Advance();battle.WorldEffects.AdvanceGrounds();
                CleanupUnavailable();if(TryEndBattle())return Array.AsReadOnly(signals.ToArray());
                battle.Movement.Advance();
                foreach(var unit in ordered)
                {
                    if(!unit.IsAlive||!unit.IsOnBoard)continue;
                    if(battle.StatusEffects.BlocksActions(unit)) {
                        var held=actions[unit.UnitInstanceId];
                        bool resting=battle.StatusEffects.HasBehavior(unit,StatusBehavior.RestSleep)&&
                            !battle.StatusEffects.HasBehavior(unit,StatusBehavior.Sleep)&&!battle.StatusEffects.HasBehavior(unit,StatusBehavior.Freeze);
                        if(!resting&&held.IsSkillActive&&held.SkillCast!=null&&!held.SkillCast.Definition.Interruptible) {
                            held.SkillCast.PausedTicks=checked(held.SkillCast.PausedTicks+1);held.SkillCast.EndTick=checked(held.SkillCast.EndTick+1);
                            held.SkillCast.EffectTick=checked(held.SkillCast.EffectTick+1);held.EndTick=checked(held.EndTick+1);held.EffectTick=checked(held.EffectTick+1);
                        }
                        if(!resting||unit.ActionState!=CombatActionState.Casting)continue;
                    }
                    if(battle.Movement.IsTravelling(unit)&&!(unit.ActionState==CombatActionState.Casting&&actions[unit.UnitInstanceId].SkillCast?.Definition.Interruptible==false))continue;
                    var runtime=actions[unit.UnitInstanceId];
                    if(unit.ActionState==CombatActionState.Idle) Decide(unit,runtime);
                    else Advance(unit,runtime);
                }
                battle.SkillTimeline.Advance();
                // All attacks due this tick are selected before any basic attack can kill.
                // Keep participants available until melee hits and arriving projectiles resolve.
                foreach(var unit in ordered)unit.BeginSimultaneousAttacks();
                try
                {
                    foreach(var attack in scheduledAttacks)attack();
                    policy.OnProjectilesTiming(battle);
                    battle.PathProjectiles.Advance();battle.WorldEffects.AdvanceChains();
                }
                finally
                {
                    foreach(var unit in ordered)unit.EndSimultaneousAttacks();
                    scheduledAttacks.Clear();
                }
                CleanupUnavailable(); // Effects may kill units already processed earlier in this tick.
                if(TryEndBattle())return Array.AsReadOnly(signals.ToArray());
                ReevaluateLostAttackTargets();
                ReevaluateLostSkillTargets();
                battle.StatusEffects.Cleanup();
                CleanupUnavailable();
                if(!TryEndBattle()) {
                    battle.CurrentTick=checked(battle.CurrentTick+1);
                    if(battle.CurrentTick>=battle.TimeLimitTick)TryEndBattle();
                }
                return Array.AsReadOnly(signals.ToArray());
            }
            finally {stepping=false;}
        }
        private void CleanupUnavailable()
        {
            foreach(var unit in ordered)
                if(!unit.IsAlive||!unit.IsOnBoard)
                {
                    battle.Movement.Cancel(unit);
                    if(!unit.IsAlive&&reportedDeaths.Add(unit.UnitInstanceId)) {
                        var reason=unit.DeathCause;
                        battle.RecordLifecycle(new BattleLifecycleEvent(BattleLifecycleEventKind.UnitDied,battle.CurrentTick,unit.UnitInstanceId,reason));
                    }
                    if(!unit.IsAlive&&unit.IsOnBoard)battle.TryRemoveUnit(unit.UnitInstanceId);
                    var runtime=actions[unit.UnitInstanceId];
                    if(runtime.SkillCast!=null&&!runtime.SkillCast.Finished)runtime.SkillCast.CancelReason=SkillCancelReason.CasterUnavailable;
                    if(runtime.EndTick>0||runtime.MoveDestination.HasValue)Finish(unit,runtime,true);
                    Reservations.Release(unit.UnitInstanceId);
                    runtime.IsAttackTargetLocked=false;runtime.MovementTargetId=null;
                    unit.CurrentTargetId=null;unit.ActionState=CombatActionState.Dead;
                }
        }
        private bool TryEndBattle()
        {
            bool one=ordered.Any(u=>u.TeamId==1&&u.IsAlive&&u.IsOnBoard);
            bool two=ordered.Any(u=>u.TeamId==2&&u.IsAlive&&u.IsOnBoard);
            if(one&&two&&battle.CurrentTick<battle.TimeLimitTick)return false;
            var result=one&&two?BattleResult.Draw:one?BattleResult.TeamOneWin:two?BattleResult.TeamTwoWin:BattleResult.Draw;
            var reason=one&&two?BattleEndReason.TimeLimit:ordered.Any(u=>!u.IsAlive && u.DeathCause==DeathReason.Surrender) ? BattleEndReason.Surrender : BattleEndReason.Elimination;
            foreach(var u in ordered) {
                var r=actions[u.UnitInstanceId];
                if(r.IsSkillActive||r.EndTick>0||r.MoveDestination.HasValue) {
                    if(r.SkillCast!=null)r.SkillCast.CancelReason=SkillCancelReason.BattleEnded;
                    Finish(u,r,true);
                }
                Reservations.Release(u.UnitInstanceId);u.CurrentTargetId=null;
                r.IsAttackTargetLocked=false;r.MovementTargetId=null;
                u.ActionState=u.IsAlive&&u.IsOnBoard?CombatActionState.Idle:CombatActionState.Dead;
            }
            battle.StatusEffects.Cleanup();battle.Projectiles.EndBattle();battle.SkillTimeline.EndBattle();battle.Movement.EndBattle();battle.PathProjectiles.EndBattle();battle.WorldEffects.EndBattle();inputs.Clear();skillInterrupts.Clear();
            battle.Result=result;battle.EndReason=reason;battle.EndTick=battle.CurrentTick;
            battle.RecordLifecycle(new BattleLifecycleEvent(BattleLifecycleEventKind.BattleEnded,battle.CurrentTick,result:result,endReason:reason));
            return true;
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
                HexCoordinates.IsInRange(unit.Position,target.Position,unit.StatusSystem.AttackRange(unit));
            if(forced!=null && (target==null || forced.UnitInstanceId!=target.UnitInstanceId))retain=false;
            if(!retain)
            {
                runtime.IsAttackTargetLocked=false;
                unit.CurrentTargetId=forced!=null ? forced.UnitInstanceId : policy.SelectTarget(battle,unit);
                target=Target(unit);
            }
            if(TryStartDataSkill(unit,runtime))return;
            if(target==null){unit.CurrentTargetId=null;return;}
            if(battle.Energy.IsSkillReady(unit)&&CanUseSkill(unit)&&policy.TryGetSkillTiming(battle,unit,target,out var timing))
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
            if(HexCoordinates.IsInRange(unit.Position,target.Position,unit.StatusSystem.AttackRange(unit)))
            {
                if(CanAttack(unit) && battle.CurrentTick>=runtime.NextAttackTick)
                {
                    Begin(unit,runtime,CombatActionState.Attacking,policy.GetAttackTiming(battle,unit));
                    Advance(unit,runtime);
                }
                return;
            }
            if(!CanMove(unit))return;
            var path=AttackPositionFinder.FindPosition(Reservations.Overlay(battle.Board),unit.UnitInstanceId,
                target.UnitInstanceId,unit.StatusSystem.AttackRange(unit));
            if(path.Status!=PathSearchStatus.PathFound)return;
            var next=path.Path[1];
            if(!Reservations.TryReserve(unit.UnitInstanceId,next))return;
            runtime.MoveDestination=next;
            runtime.MovementTargetId=target.UnitInstanceId;
            Begin(unit,runtime,CombatActionState.Moving,
                new CombatActionTiming(ToTicks(1.0/unit.EffectiveStats.MoveSpeed,battle.TickRate),0));
        }
        private UnitCombatState SkillTarget(UnitCombatState unit,SkillDefinition skill,string id)
        {
            if(skill.TargetRule==SkillTargetRule.Self)return unit.IsAlive&&unit.IsOnBoard?unit:null;
            if(skill.TargetRule==SkillTargetRule.LowestHPAlly||skill.TargetRule==SkillTargetRule.NearestAlly)
                return id!=null&&units.TryGetValue(id,out var ally)&&ally.TeamId==unit.TeamId&&ally.IsAlive&&ally.IsOnBoard ? ally : null;
            if(id==null||!units.TryGetValue(id,out var target)||target.TeamId==unit.TeamId||!target.IsTargetable||!policy.IsTargetable(target))return null;
            return target;
        }
        private UnitCombatState SelectSkillTarget(UnitCombatState unit,SkillDefinition skill,bool checkRange)
        {
            if(skill.TargetRule==SkillTargetRule.Self)return unit;
            if(skill.TargetRule==SkillTargetRule.LowestHPAlly)
                return ordered.Where(t=>t.TeamId==unit.TeamId&&t.IsAlive&&t.IsOnBoard)
                    .OrderBy(t=>(double)t.CurrentHP/t.Stats.MaxHP)
                    .ThenByDescending(t=>(double)t.Stats.MaxHP-t.CurrentHP)
                    .ThenBy(t=>t.UnitInstanceId,StringComparer.Ordinal).FirstOrDefault();
            if(skill.TargetRule==SkillTargetRule.NearestAlly)
                return ordered.Where(t=>t!=unit&&t.TeamId==unit.TeamId&&t.IsAlive&&t.IsOnBoard&&(!checkRange||HexCoordinates.Distance(unit.Position,t.Position)<=skill.Range))
                    .OrderBy(t=>HexCoordinates.Distance(unit.Position,t.Position)).ThenBy(t=>t.UnitInstanceId,StringComparer.Ordinal).FirstOrDefault();
            if(skill.TargetRule==SkillTargetRule.FarthestEnemy)
                return SkillSpatialSelector.Farthest(battle,unit,checkRange?skill.Range:56,policy.IsTargetable);
            // Restrict candidates before distance/center/RNG selection.
            var id=CombatTargetSelector.Select(battle,unit,
                t=>policy.IsTargetable(t)&&(!checkRange||HexCoordinates.IsInRange(unit.Position,t.Position,skill.Range)),
                policy.GetTauntSource(unit));
            return SkillTarget(unit,skill,id);
        }
        private bool VariantAvailable(UnitCombatState unit,SkillDefinition skill,int depth) {
            if(depth>=8)return false;
            if(skill.VariantIds.Count==0)return SelectSkillTarget(unit,skill,true)!=null&&!FocusRecast(unit,skill);
            var ids=skill.VariantMode==SkillVariantMode.Rank?new[]{skill.VariantIds[(int)unit.Rank-1]}:skill.VariantIds.ToArray();
            return ids.Any(id=>policy.TryGetSkillById(id,out var v)&&VariantAvailable(unit,v,depth+1));
        }
        private bool FocusRecast(UnitCombatState unit,SkillDefinition skill)=>battle.StatusEffects.HasBehavior(unit,StatusBehavior.Focus)&&skill.Effects.Any(e=>e.Type==SkillEffectType.ApplyStatus&&battle.StatusEffects.GetDefinition(e.ResolveStatusId(unit.Rank)).Behavior==StatusBehavior.Focus);
        private bool TryStartDataSkill(UnitCombatState unit,UnitActionRuntime runtime)
        {
            if(!battle.Energy.IsSkillReady(unit)||!CanUseSkill(unit)||
                !policy.TryGetSkillDefinition(battle,unit,out var skill))return false;
            if(FocusRecast(unit,skill))return false;
            for(int depth=0;skill.VariantIds.Count>0;depth++) {
                if(depth>=8)return false;
                var ids=skill.VariantMode==SkillVariantMode.Rank?new[]{skill.VariantIds[(int)unit.Rank-1]}:skill.VariantIds.ToArray();
                var choices=ids.Select(id=>policy.TryGetSkillById(id,out var v)?v:null).Where(v=>v!=null&&VariantAvailable(unit,v,0)).ToArray();
                if(choices.Length==0)return false;skill=choices.Length==1?choices[0]:choices[battle.RngStreams.Skill.NextInt(choices.Length)];
            }
            var target=SelectSkillTarget(unit,skill,true);
            if(target==null)return false;
            if(skill.Activation!=null&&(!battle.Movement.IsSurrounded(unit,skill.Activation.MinimumAdjacentEnemies)||
                skill.Activation.RequireSafeDestination&&!battle.Movement.Safest(unit).HasValue))return false;
            int hit=ToTicks(skill.CastTime,battle.TickRate,false);
            int duration=Math.Max(1,checked(skill.LastCastOffset(battle.TickRate)+ToTicks(skill.PostCastTime,battle.TickRate,false)));
            int lockTicks=ToTicks(skill.EnergyLock??unit.Stats.EnergyLockSeconds,battle.TickRate,false);
            foreach(var step in skill.Steps)checked { var end=battle.CurrentTick+step.StartOffset(skill,battle.TickRate)+
                ToTicks(step.FirstDelay,battle.TickRate,false)+step.RepeatOffset(step.Count-1,battle.TickRate); }
            var cast=new SkillCastRuntime(skill,target.UnitInstanceId,battle.CurrentTick,hit,duration,lockTicks)
                {CastId=checked(++nextCastId),Iterations=new int[skill.Steps.Count]};
            if(!battle.Energy.TryConsumeSkill(unit))return false;
            runtime.SkillCast=cast;runtime.IsSkillActive=true;runtime.IsAttackTargetLocked=false;
            Begin(unit,runtime,CombatActionState.Casting,new CombatActionTiming(duration,hit));
            battle.RecordSkill(new SkillEvent(unit.UnitInstanceId,cast,battle.CurrentTick,SkillEventKind.CastStarted,SkillCancelReason.None));
            policy.OnSkillStarted(battle,unit,target);
            if(unit.IsAlive&&unit.IsOnBoard)Advance(unit,runtime);
            return true;
        }
        private static bool NeedsPendingTarget(SkillCastRuntime cast)
        {
            if(!cast.Definition.UsesAuthoredSteps)return !cast.EffectApplied;
            return cast.Definition.Steps
                .Where((step,i)=>cast.Iterations[i]<(step.Lifetime==SkillStepLifetime.Independent?1:step.Count))
                .Any(step=>step.Targeting!=SkillStepTargeting.Reselect&&step.Effects.Any(e=>e.NeedsCastTarget));
        }
        private void AdvanceDataSkill(UnitCombatState unit,UnitActionRuntime runtime)
        {
            var cast=runtime.SkillCast;var skill=cast.Definition;
            if(SkillTarget(unit,skill,cast.TargetId)==null&&skill.TargetLostPolicy==TargetLostPolicy.Cancel&&NeedsPendingTarget(cast))
            {cast.CancelReason=SkillCancelReason.TargetLost;Finish(unit,runtime,true);return;}
            while(!cast.Finished&&unit.IsAlive&&unit.IsOnBoard)
            {
                var next=skill.Steps.Select((step,index)=>new {Step=step,Index=index,Iteration=cast.Iterations[index]})
                    .Where(x=>x.Iteration<(x.Step.Lifetime==SkillStepLifetime.Independent?1:x.Step.Count))
                    .Select(x=>new {x.Step,x.Index,x.Iteration,Tick=checked(cast.StartTick+cast.PausedTicks+x.Step.StartOffset(skill,battle.TickRate)+
                        (x.Step.Lifetime==SkillStepLifetime.Cast?x.Step.RepeatOffset(x.Iteration,battle.TickRate):0))})
                    .Where(x=>x.Tick<=battle.CurrentTick).OrderBy(x=>x.Tick).ThenBy(x=>x.Index).FirstOrDefault();
                if(next==null)break;
                var target=SkillTarget(unit,skill,cast.TargetId);
                if(next.Step.Targeting==SkillStepTargeting.Reselect||
                    (target==null&&skill.TargetLostPolicy==TargetLostPolicy.RetargetAtEffect))
                {target=SelectSkillTarget(unit,skill,true);cast.TargetId=target?.UnitInstanceId;}
                if(target==null&&skill.TargetLostPolicy!=TargetLostPolicy.ContinueWithoutTarget&&
                    (!skill.UsesAuthoredSteps||next.Step.Effects.Any(e=>e.NeedsCastTarget)))
                {cast.CancelReason=SkillCancelReason.TargetLost;Finish(unit,runtime,true);return;}
                cast.Iterations[next.Index]++;
                bool first=!cast.EffectApplied;
                cast.EffectApplied=true;runtime.EffectApplied=true;
                Emit(unit,CombatActionSignalKind.TimingReached);
                if(next.Step.Lifetime==SkillStepLifetime.Independent)
                    battle.SkillTimeline.Spawn(cast,next.Index,policy.PrepareSkillStep(battle,unit,target,skill,next.Step,cast.CastId,next.Index));
                else policy.ExecuteSkillStep(battle,unit,target,skill,next.Step,cast.CastId,next.Index,next.Iteration);
                if(battle.Movement.IsTravelling(unit)) {
                    cast.EndTick=Math.Max(cast.EndTick,checked(battle.Movement.ArrivalTick(unit)+ToTicks(skill.PostCastTime,battle.TickRate,false)));
                    runtime.EndTick=cast.EndTick;
                }
                if(first){battle.Synergies.OnSkillEffect(unit);battle.Items.OnSkillEffect(unit);}
                battle.RecordSkill(new SkillEvent(unit.UnitInstanceId,cast,battle.CurrentTick,SkillEventKind.EffectApplied,
                    SkillCancelReason.None,next.Index,next.Iteration));
                if(battle.Movement.IsTravelling(unit))break;
            }
            if(!cast.Finished&&unit.IsAlive&&unit.IsOnBoard&&battle.CurrentTick>=cast.EndTick)Finish(unit,runtime,false);
        }
        private void ReevaluateLostSkillTargets()
        {
            foreach(var unit in ordered) {
                var runtime=actions[unit.UnitInstanceId];var cast=runtime.SkillCast;
                if(!unit.IsAlive||!unit.IsOnBoard||cast==null||cast.Finished||!NeedsPendingTarget(cast)||
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
                if(!CanMove(unit)||!runtime.MoveDestination.HasValue||
                    !Reservations.TryGetOwner(runtime.MoveDestination.Value,out var owner)||owner!=unit.UnitInstanceId)
                {Finish(unit,runtime,true);return;}
                if(battle.CurrentTick<runtime.EndTick)return;
                bool success=battle.TryMoveUnit(unit.UnitInstanceId,runtime.MoveDestination.Value)==BoardOperationResult.Success;
                Finish(unit,runtime,!success);return;
            }
            var target=Target(unit);
            if(unit.ActionState==CombatActionState.Attacking&&!runtime.EffectApplied &&
                (target==null||!CanAttack(unit)||
                 !HexCoordinates.IsInRange(unit.Position,target.Position,unit.StatusSystem.AttackRange(unit))))
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
                if(unit.ActionState==CombatActionState.Attacking)scheduledAttacks.Add(()=>{if(runtime.IsAttackTargetLocked)policy.OnAttackTiming(battle,unit,target);});
                else if(unit.ActionState==CombatActionState.Casting)policy.OnSkillTiming(battle,unit,target);
            }
            if(!unit.IsAlive||!unit.IsOnBoard)return;
            if(battle.CurrentTick>=runtime.EndTick)Finish(unit,runtime,false);
        }
        private void Finish(UnitCombatState unit,UnitActionRuntime runtime,bool cancelled)
        {
            Emit(unit,cancelled?CombatActionSignalKind.Cancelled:CombatActionSignalKind.Completed);
            if(!battle.Movement.IsTravelling(unit))Reservations.Release(unit.UnitInstanceId);
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
                if(target==null||!HexCoordinates.IsInRange(unit.Position,target.Position,unit.StatusSystem.AttackRange(unit))||
                    (forced!=null&&forced.UnitInstanceId!=unit.CurrentTargetId))
                    CancelAttackAndReplan(unit,runtime);
            }
        }
        private void Emit(UnitCombatState unit,CombatActionSignalKind kind)=>
            signals.Add(new CombatActionSignal(battle.CurrentTick,unit.UnitInstanceId,unit.ActionState,kind));
    }
}
