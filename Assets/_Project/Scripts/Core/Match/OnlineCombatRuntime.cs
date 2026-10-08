using System;
using System.Collections.Generic;
using System.Linq;
using PokeChess.Core.Battle;

namespace PokeChess.Core.Match
{
    [Serializable] public sealed class CombatUnitView
    {
        public string id,definitionId,owner,target;public int team,rank,column,row,moveColumn,moveRow;
        public float hp,maxHP,energy,shield;public bool onBoard;public CombatActionState action;
        public long startTick,effectTick,endTick;public string[] statuses;
    }
    [Serializable] public sealed class CombatProjectileView
    {
        public long id,spawnTick,arrivalTick;public string source,target;public int column,row,targetColumn,targetRow;
    }
    [Serializable] public sealed class CombatEventView
    {
        public long sequence,tick;public string kind,source,target,detail;public float value,hpAfter,shieldAfter;
    }
    [Serializable] public sealed class CombatFrame
    {
        public string matchId,battleId,one,two;public int round,tickRate=30;public long sequence,tick;
        public bool shadow,complete,baseline;public string result,reason;public long overtimeTick,timeLimitTick;
        public CombatUnitView[] units;public CombatProjectileView[] projectiles;public CombatEventView[] events;
    }
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
        private void Capture(LocalPairBattle pair,bool baseline)
        {
            var battle=pair.Battle;long tick=battle?.CurrentTick??0;
            if(!baseline&&ended.Contains(pair))return;
            if(!baseline&&lastTicks.TryGetValue(pair,out var previous)&&previous==tick&&!pair.IsComplete)return;
            var events=new List<CombatEventView>();
            Action<string,string,string,string,float,float,float> add=(kind,source,target,detail,value,hp,shield)=>{
                eventSequences.TryGetValue(pair,out var sequence);eventSequences[pair]=++sequence;
                events.Add(new CombatEventView{sequence=sequence,tick=tick,kind=kind,source=source,target=target,detail=detail,value=value,hpAfter=hp,shieldAfter=shield});};
            if(baseline)add("BattleStarted",null,null,null,0,0,0);
            else if(battle!=null&&(!lastTicks.TryGetValue(pair,out var old)||tick!=old)){
                foreach(var d in battle.DamageResultsThisTick)add("Damage",d.Request.SourceId,d.Request.TargetId,d.IsCritical?"Critical":d.Request.Type.ToString(),d.AppliedDamage,d.RemainingHP,d.RemainingShield);
                foreach(var e in battle.SkillEventsThisTick)add("Skill"+e.Kind,e.UnitId,e.TargetId,e.SkillId,0,0,0);
                foreach(var e in battle.SkillEffectEventsThisTick)add("Effect"+e.Type,e.CasterId,e.TargetId,e.Outcome.ToString(),e.ActualValue,0,0);
                foreach(var e in battle.StatusEffects.EventsThisTick)add("Status"+e.Kind,e.Effect.SourceId,e.Effect.TargetId,e.Effect.Definition.Id,e.Effect.StackCount,0,0);
                foreach(var e in battle.Projectiles.EventsThisTick)add("Projectile"+e.Kind,e.Projectile.SourceId,e.Projectile.TargetId,e.Projectile.Id.ToString(),0,0,0);
                foreach(var e in battle.LifecycleEventsThisTick)if(e.Kind!=BattleLifecycleEventKind.BattleEnded)add(e.Kind.ToString(),e.UnitId,e.UnitId,e.DeathReason.ToString(),0,0,0);
            }
            sequences.TryGetValue(pair,out var frameSequence);sequences[pair]=++frameSequence;lastTicks[pair]=tick;
            var frame=new CombatFrame{matchId=Match.MatchId,battleId=battle?.BattleId??Match.MatchId+"-empty-"+Match.RoundNumber+"-"+pair.Pairing.PlayerOneId,
                round=Match.RoundNumber,sequence=frameSequence,tick=tick,tickRate=TickRate,baseline=baseline,one=pair.Pairing.PlayerOneId,two=pair.Pairing.PlayerTwoId,shadow=pair.IsShadow,
                complete=pair.IsComplete,result=pair.Result.ToString(),reason=pair.Reason.ToString(),overtimeTick=battle?.OvertimeStartTick??0,timeLimitTick=battle?.TimeLimitTick??0,events=events.ToArray(),
                units=battle==null?Array.Empty<CombatUnitView>():battle.Units.Select(u=>{
                    var a=pair.GetActionRuntime(u.UnitInstanceId);var cast=a.SkillCast;
                    return new CombatUnitView{id=u.UnitInstanceId,definitionId=u.DefinitionId,owner=u.OwnerPlayerId,team=u.TeamId,rank=(int)u.Rank,column=u.Position.Column,row=u.Position.Row,
                        hp=u.CurrentHP,maxHP=u.Stats.MaxHP,energy=u.CurrentEnergy,shield=u.CurrentShield,onBoard=u.IsOnBoard,action=u.ActionState,target=cast?.TargetId??u.CurrentTargetId,
                        startTick=cast?.StartTick??a.StartTick,effectTick=cast?.EffectTick??a.EffectTick,endTick=cast?.EndTick??a.EndTick,
                        moveColumn=a.MoveDestination?.Column??u.Position.Column,moveRow=a.MoveDestination?.Row??u.Position.Row,
                        statuses=battle.StatusEffects.Active.Where(s=>s.TargetId==u.UnitInstanceId).Select(s=>s.Definition.Id).ToArray()};}).ToArray(),
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
