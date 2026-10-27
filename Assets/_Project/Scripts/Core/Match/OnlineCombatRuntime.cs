using System;
using System.Collections.Generic;
using System.Linq;
using PokeChess.Core.Battle;

namespace PokeChess.Core.Match
{
    [Serializable] public sealed class CombatUnitView
    {
        public string id,definitionId,owner,target;public int team,rank,column,row,moveColumn,moveRow,assaultCharges;
        public float hp,maxHP,energy,maxEnergy,shield;public bool onBoard;public CombatActionState action;
        public string[] items;
        public bool untargetable,isBoss;
        public float accuracy,evasion,critChance,attackSpeed;public int attackRange,bountyStacks;
        public CombatStatusView[] statusDetails;public CombatShieldView[] shields;
        public long startTick,effectTick,endTick;public string[] statuses;
    }
    [Serializable] public sealed class CombatStatusView
    {public long id,startTick,endTick,nextTick;public string definition,source;public int behavior,stacks,pulses;}
    [Serializable] public sealed class CombatShieldView
    {public long id,endTick;public string source,group;public float remaining;}
    [Serializable] public sealed class CombatProjectileView
    {
        public long id,spawnTick,arrivalTick;public string source,target;public int column,row,targetColumn,targetRow;
    }
    [Serializable] public sealed class CombatEventView
    {
        public DamageOrigin origin;
        public long sequence,tick;public string kind,source,target,detail;public float value,hpAfter,shieldAfter;
        public long castId,effectId;public int stepIndex,iteration;
        public CombatAreaView area;public CombatMovementView movement;
    }
    [Serializable] public sealed class CombatIndependentSkillView
    {
        public long id,castId,nextTick,lastTick;public string skillId,casterId;
        public int stepIndex,nextIteration,column,row,centerColumn,centerRow;public bool hasCenter;
        public CombatAreaView[] areas;
    }
    [Serializable] public sealed class CombatCellView {public int column,row;}
    [Serializable] public sealed class CombatAreaView
    {public int effectIndex,shape;public CombatCellView origin,center,aim;public CombatCellView[] cells;}
    [Serializable] public sealed class CombatMovementView
    {public long id,castId,startTick,endTick;public string source,unit;public int kind,stepIndex,iteration;public CombatCellView[] path;}
    [Serializable] public sealed class CombatPathProjectileView
    {public long id,castId,spawnTick,nextTick;public string source,skill;public int stepIndex,iteration,nextIndex,outgoingCount;public CombatCellView[] path;}
    [Serializable] public sealed class CombatFrame
    {
        public string matchId,battleId,one,two;public int round,tickRate=30;public long sequence,tick;
        public bool shadow,wild,complete,baseline;public string result,reason;public long overtimeTick,timeLimitTick;
        public CombatUnitView[] units;public CombatProjectileView[] projectiles;public CombatEventView[] events;
        public CombatIndependentSkillView[] independentSkills;
        public CombatMovementView[] movements;public CombatPathProjectileView[] pathProjectiles;
        public CombatGroundView[] grounds;public CombatChainView[] chains;
    }
    [Serializable] public sealed class CombatGroundView {public long id,startTick,endTick;public string source,status;public int column,row,radius;}
    [Serializable] public sealed class CombatChainView {public long id,nextTick;public string source,skill;public int column,row;public string[] visited;}
    [Serializable] public sealed class CombatBatch {public int protocol=1;public CombatFrame[] frames;}

    // Owns the authoritative clock, not a client replay simulation.
    public sealed class OnlineCombatRuntime : IRoundFlowRuntime
    {
        private readonly LocalRoundCoordinator rounds;
        private readonly HostCommandProcessor commands;
        private readonly Dictionary<LocalPairBattle,long> sequences=new Dictionary<LocalPairBattle,long>();
        private readonly Dictionary<LocalPairBattle,long> eventSequences=new Dictionary<LocalPairBattle,long>();
        private readonly Dictionary<LocalPairBattle,long> lastTicks=new Dictionary<LocalPairBattle,long>();
        private readonly HashSet<LocalPairBattle> ended=new HashSet<LocalPairBattle>();
        private readonly Dictionary<LocalPairBattle,CombatFrame> previousFrames=new Dictionary<LocalPairBattle,CombatFrame>();
        public event Action<CombatFrame> FrameProduced;
        public RoundFlowController Clock {get;}
        public MatchState Match=>rounds.Match;
        public bool PreparationReady=>rounds.PreparationReady;
        public bool NextPreparationPending=>rounds.NextPreparationPending;
        public bool IsSettled=>rounds.IsSettled;
        public int TickRate=>rounds.TickRate;
        public OnlineCombatRuntime(LocalRoundCoordinator rounds,HostCommandProcessor commands,RoundFlowRules rules=null)
        {this.rounds=rounds;this.commands=commands;Clock=new RoundFlowController(this,rules);}
        public void Advance(double seconds){Clock.AdvanceTime(seconds);CaptureCommandEffects();Clock.RefreshState();}
        public bool StartCombat(int round)
        {
            if(!rounds.StartCombat(round))return false;
            sequences.Clear();eventSequences.Clear();lastTicks.Clear();ended.Clear();previousFrames.Clear();
            foreach(var pair in rounds.Battles)Capture(pair,true);
            commands.NotifyHostStateChanged();return true;
        }
        public void Step()
        {
            var phase=Match.Phase;rounds.Step();foreach(var pair in rounds.Battles)Capture(pair,false);
            if(phase!=Match.Phase)commands.NotifyHostStateChanged();
        }
        public void SettleResult(){rounds.SettleResult();commands.NotifyHostStateChanged();}
        public bool NextRound(int round){if(!rounds.NextRound(round))return false;commands.NotifyHostStateChanged();return true;}
        public void CaptureCommandEffects(){foreach(var pair in rounds.Battles)if(sequences.ContainsKey(pair)&&pair.IsComplete&&!ended.Contains(pair))Capture(pair,false);}
        private static CombatCellView Cell(PokeChess.Core.Pokemon.BoardPosition p)=>new CombatCellView{column=p.Column,row=p.Row};
        private static CombatAreaView Area(SkillAreaGeometry a)=>new CombatAreaView{effectIndex=a.EffectIndex,shape=(int)a.Shape,
            origin=Cell(a.Origin),center=Cell(a.Center),aim=Cell(a.Aim),cells=a.Cells.Select(Cell).ToArray()};
        private static CombatMovementView Movement(CombatMovementSnapshot m)=>new CombatMovementView{id=m.Id,castId=m.CastId,stepIndex=m.StepIndex,iteration=m.Iteration,source=m.SourceId,unit=m.UnitId,
            kind=(int)m.Kind,startTick=m.StartTick,endTick=m.EndTick,path=m.Path.Select(Cell).ToArray()};
        private void Capture(LocalPairBattle pair,bool baseline)
        {
            var battle=pair.Battle;long tick=battle?.CurrentTick??0;
            if(!baseline&&ended.Contains(pair))return;
            if(!baseline&&lastTicks.TryGetValue(pair,out var previous)&&previous==tick&&!pair.IsComplete)return;
            var events=new List<CombatEventView>();
            Action<string,string,string,string,float,float,float> add=(kind,source,target,detail,value,hp,shield)=>{
                eventSequences.TryGetValue(pair,out var sequence);eventSequences[pair]=++sequence;
                events.Add(new CombatEventView{sequence=sequence,tick=tick,kind=kind,source=source,target=target,detail=detail,value=value,hpAfter=hp,shieldAfter=shield});};
            if(baseline) {
                add("BattleStarted",null,null,null,0,0,0);
                if(battle!=null)foreach(var e in battle.Synergies.EventsThisTick)add("Synergy",e.SourceId,e.TargetId,e.TraitId+":"+e.Detail,e.Value,0,0);
            }
            else if(battle!=null&&(!lastTicks.TryGetValue(pair,out var old)||tick!=old)){
                foreach(var e in battle.Synergies.EventsThisTick)add("Synergy",e.SourceId,e.TargetId,e.TraitId+":"+e.Detail,e.Value,0,0);
                foreach(var e in battle.Items.EventsThisTick)add("Item",e.SourceId,e.TargetId,e.ItemId,e.Value,0,0);
                foreach(var d in battle.DamageResultsThisTick){add("Damage",d.Request.SourceId,d.Request.TargetId,d.Missed?"Miss":d.IsCritical?"Critical":d.Request.Type.ToString(),d.AppliedDamage,d.RemainingHP,d.RemainingShield);events[events.Count-1].origin=d.Request.Origin;}
                foreach(var e in battle.Shields.EventsThisTick){add("Shield"+e.Kind,e.Shield.SourceId,e.Shield.TargetId,e.Shield.Group,e.Shield.Remaining,0,0);events[events.Count-1].effectId=e.Shield.Id;}
                foreach(var e in battle.SkillEventsThisTick){add("Skill"+e.Kind,e.UnitId,e.TargetId,e.SkillId,0,0,0);
                    var v=events[events.Count-1];v.castId=e.CastId;v.stepIndex=e.StepIndex;v.iteration=e.Iteration;}
                foreach(var e in battle.SkillEffectEventsThisTick){add("Effect"+e.Type,e.CasterId,e.TargetId,e.Outcome.ToString(),e.ActualValue,0,0);
                    var v=events[events.Count-1];v.castId=e.CastId;v.stepIndex=e.StepIndex;v.iteration=e.Iteration;}
                foreach(var e in battle.SkillTimeline.EventsThisTick){add("IndependentSkill"+e.Kind,e.Effect.CasterId,null,e.Effect.SkillId,0,0,0);
                    var v=events[events.Count-1];v.effectId=e.Effect.Id;v.castId=e.Effect.CastId;v.stepIndex=e.Effect.StepIndex;v.iteration=e.Effect.NextIteration;}
                foreach(var e in battle.SkillAreasThisTick){add("SkillArea",e.CasterId,null,null,0,0,0);
                    var v=events[events.Count-1];v.castId=e.CastId;v.stepIndex=e.StepIndex;v.iteration=e.Iteration;v.area=Area(e.Area);}
                foreach(var e in battle.Movement.EventsThisTick){add("Movement"+e.Kind,e.Movement.SourceId,e.Movement.UnitId,e.Movement.Kind.ToString(),0,0,0);
                    var v=events[events.Count-1];v.movement=Movement(e.Movement);v.castId=e.Movement.CastId;v.stepIndex=e.Movement.StepIndex;v.iteration=e.Movement.Iteration;}
                foreach(var e in battle.PathProjectiles.EventsThisTick){add("PathProjectile"+e.Kind,e.SourceId,e.TargetId,e.Leg.ToString(),0,0,0);
                    var v=events[events.Count-1];v.effectId=e.Id;v.castId=e.CastId;}
                foreach(var e in battle.StatusEffects.EventsThisTick)add("Status"+e.Kind,e.Effect.SourceId,e.Effect.TargetId,e.Effect.Definition.Id,e.Effect.StackCount,0,0);
                foreach(var e in battle.Projectiles.EventsThisTick)add("Projectile"+e.Kind,e.Projectile.SourceId,e.Projectile.TargetId,e.Projectile.Id.ToString(),0,0,0);
                foreach(var e in battle.LifecycleEventsThisTick)if(e.Kind!=BattleLifecycleEventKind.BattleEnded)add(e.Kind.ToString(),e.UnitId,e.UnitId,e.DeathReason.ToString(),0,0,0);
            }
            sequences.TryGetValue(pair,out var frameSequence);sequences[pair]=++frameSequence;lastTicks[pair]=tick;
            var frame=new CombatFrame{matchId=Match.MatchId,battleId=battle?.BattleId??Match.MatchId+"-empty-"+Match.RoundNumber+"-"+pair.Pairing.PlayerOneId,
                round=Match.RoundNumber,sequence=frameSequence,tick=tick,tickRate=TickRate,baseline=baseline,one=pair.Pairing.PlayerOneId,two=pair.Pairing.PlayerTwoId,shadow=pair.IsShadow,wild=pair.IsWild,
                complete=pair.IsComplete,result=pair.Result.ToString(),reason=pair.Reason.ToString(),overtimeTick=battle?.OvertimeStartTick??0,timeLimitTick=battle?.TimeLimitTick??0,events=events.ToArray(),
                grounds=battle?.WorldEffects.Grounds.Select(g=>new CombatGroundView{id=g.Id,source=g.SourceId,status=g.StatusId,startTick=g.StartTick,endTick=g.EndTick,column=g.Center.Column,row=g.Center.Row,radius=g.Radius}).ToArray()??Array.Empty<CombatGroundView>(),
                chains=battle?.WorldEffects.Chains.Select(c=>new CombatChainView{id=c.Id,source=c.SourceId,skill=c.SkillId,nextTick=c.NextTick,column=c.Center.Column,row=c.Center.Row,visited=c.Visited}).ToArray()??Array.Empty<CombatChainView>(),
                independentSkills=battle?.SkillTimeline.Active.Select(e=>new CombatIndependentSkillView{id=e.Id,castId=e.CastId,
                    skillId=e.SkillId,casterId=e.CasterId,stepIndex=e.StepIndex,nextIteration=e.NextIteration,nextTick=e.NextTick,lastTick=e.LastTick,
                    column=e.Origin.Column,row=e.Origin.Row,hasCenter=e.Center.HasValue,centerColumn=e.Center?.Column??0,centerRow=e.Center?.Row??0,areas=e.Areas.Select(Area).ToArray()}).ToArray()
                    ??Array.Empty<CombatIndependentSkillView>(),
                movements=battle?.Movement.Active.Select(Movement).ToArray()??Array.Empty<CombatMovementView>(),
                pathProjectiles=battle?.PathProjectiles.Active.Select(p=>new CombatPathProjectileView{id=p.Id,castId=p.CastId,source=p.SourceId,
                    skill=p.SkillId,stepIndex=p.StepIndex,iteration=p.Iteration,spawnTick=p.SpawnTick,nextTick=p.NextTick,nextIndex=p.NextIndex,
                    outgoingCount=p.OutgoingCount,path=p.Path.Select(Cell).ToArray()}).ToArray()??Array.Empty<CombatPathProjectileView>(),
                units=battle==null?Array.Empty<CombatUnitView>():battle.Units.Select(u=>{
                    var a=pair.GetActionRuntime(u.UnitInstanceId);var cast=a.SkillCast;var effective=u.EffectiveStats;
                    return new CombatUnitView{id=u.UnitInstanceId,definitionId=u.DefinitionId,owner=u.OwnerPlayerId,team=u.TeamId,rank=(int)u.Rank,column=u.Position.Column,row=u.Position.Row,
                        hp=u.CurrentHP,maxHP=u.Stats.MaxHP,energy=u.CurrentEnergy,maxEnergy=u.Stats.MaxEnergy,shield=u.CurrentShield,onBoard=u.IsOnBoard,action=u.ActionState,target=cast?.TargetId??u.CurrentTargetId,
                        assaultCharges=battle.Synergies.AssaultCharges(u),
                        startTick=cast?.StartTick??a.StartTick,effectTick=cast?.EffectTick??a.EffectTick,endTick=cast?.EndTick??a.EndTick,
                        moveColumn=a.MoveDestination?.Column??u.Position.Column,moveRow=a.MoveDestination?.Row??u.Position.Row,
                        untargetable=u.IsUntargetable,isBoss=u.IsBoss,accuracy=effective.Accuracy,evasion=effective.Evasion,
                        critChance=effective.CritChance,attackSpeed=effective.AttackSpeed,attackRange=battle.StatusEffects.AttackRange(u),bountyStacks=u.BountyStacks,
                        statusDetails=battle.StatusEffects.Active.Where(s=>s.TargetId==u.UnitInstanceId).Select(s=>new CombatStatusView{id=s.InstanceId,
                            definition=s.Definition.Id,source=s.SourceId,behavior=(int)s.Definition.Behavior,stacks=s.StackCount,
                            startTick=s.StartTick,endTick=s.ExpireTick,nextTick=s.NextTick,pulses=s.Pulses}).ToArray(),
                        shields=battle.Shields.Active.Where(s=>s.TargetId==u.UnitInstanceId).Select(s=>new CombatShieldView{id=s.Id,source=s.SourceId,
                            group=s.Group,remaining=s.Remaining,endTick=s.ExpireTick}).ToArray(),
                        items=u.ItemInstanceIds.ToArray(),statuses=battle.StatusEffects.Active.Where(s=>s.TargetId==u.UnitInstanceId).Select(s=>s.Definition.Id).ToArray()};}).ToArray(),
                projectiles=battle?.Projectiles.Active.Select(p=>new CombatProjectileView{id=p.Id,source=p.SourceId,target=p.TargetId,spawnTick=p.SpawnTick,arrivalTick=p.ArrivalTick,
                    column=p.LaunchPosition.Column,row=p.LaunchPosition.Row,targetColumn=p.TargetPositionAtLaunch.Column,targetRow=p.TargetPositionAtLaunch.Row}).ToArray()??Array.Empty<CombatProjectileView>()};
            previousFrames.TryGetValue(pair,out var prior);
            foreach(var unit in frame.units){var old=prior?.units.FirstOrDefault(u=>u.id==unit.id);
                if(old!=null&&(old.action!=unit.action||old.startTick!=unit.startTick))add("ActionChanged",unit.id,unit.target,unit.action.ToString(),0,unit.hp,unit.shield);
                if(old!=null&&(old.column!=unit.column||old.row!=unit.row))add("Moved",unit.id,null,null,0,unit.hp,unit.shield);
                if(old?.hp>0&&unit.hp<=0&&!events.Any(e=>e.kind=="UnitDied"&&e.target==unit.id))add("UnitDied",unit.id,unit.id,"StateChange",0,0,unit.shield);
                if(old!=null&&old.energy!=unit.energy)add("EnergyChanged",unit.id,unit.id,null,unit.energy-old.energy,unit.hp,unit.shield);
            }
            if(pair.IsComplete){ended.Add(pair);add("BattleEnded",null,null,pair.Result.ToString(),0,0,0);}
            frame.events=events.ToArray();previousFrames[pair]=frame;FrameProduced?.Invoke(frame);
        }
    }
    // A bounded presentation timeline. Every frame is also a complete public recovery baseline.
    public sealed class CombatPlayback
    {
        private readonly Queue<CombatFrame> pending=new Queue<CombatFrame>();
        public CombatFrame Current {get;private set;}
        public CombatFrame Latest {get;private set;}
        public double Tick {get;private set;}
        public int ReceivedEvents {get;private set;}
        public int RecoveredGaps {get;private set;}
        public double EndHold {get;private set;}
        public event Action<CombatEventView> EventPlayed;
        public bool IsPlaying=>Latest!=null&&(!Latest.complete||Current?.sequence!=Latest.sequence||EndHold<.35);
        public bool Accept(CombatFrame frame,string matchId,string playerId,int currentRound)
        {
            if(frame==null||frame.matchId!=matchId||frame.round!=currentRound||frame.sequence<1||frame.tickRate!=30||frame.tick<0||frame.units==null||frame.units.Length>56||frame.events==null||frame.projectiles==null||
                frame.one!=playerId&&(frame.shadow||frame.two!=playerId)||frame.units.Any(u=>u==null||string.IsNullOrEmpty(u.definitionId)||u.statuses==null))return false;
            if(Latest!=null&&Latest.round>frame.round)return false;
            if(Latest!=null&&Latest.round==frame.round&&Latest.battleId!=frame.battleId)return false;
            bool recover=false;
            if(Latest!=null&&Latest.battleId==frame.battleId){if(frame.sequence<=Latest.sequence||frame.tick<Latest.tick)return false;if(frame.sequence>Latest.sequence+1){RecoveredGaps++;recover=true;}}
            else {Clear();Tick=Math.Max(0,frame.tick-6);recover=frame.tick>0;}
            if(recover){pending.Clear();Latest=Current=frame;Tick=frame.tick;foreach(var e in frame.events){ReceivedEvents++;EventPlayed?.Invoke(e);}return true;}
            Latest=frame;pending.Enqueue(frame);while(pending.Count>120){Current=pending.Dequeue();Tick=Current.tick;RecoveredGaps++;}
            return true;
        }
        public void Advance(double seconds)
        {
            if(Latest==null)return;
            double target=Math.Max(Tick,Latest.complete?Latest.tick:Math.Max(0,Latest.tick-6));
            if(target-Tick>30)Tick=target-6;
            Tick=Math.Min(target,Tick+Math.Max(0,seconds)*30);
            while(pending.Count>0&&pending.Peek().tick<=Tick){Current=pending.Dequeue();foreach(var e in Current.events){ReceivedEvents++;EventPlayed?.Invoke(e);}}
            if(Latest.complete&&Current?.sequence==Latest.sequence)EndHold+=Math.Max(0,seconds);
        }
        public void Clear(){pending.Clear();Current=Latest=null;Tick=EndHold=0;ReceivedEvents=RecoveredGaps=0;}
    }
}
