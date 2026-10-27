using System;
using System.Collections.Generic;
using System.Linq;
using PokeChess.Core.Board;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Battle
{
    public sealed class PathProjectileState
    {
        public long Id { get; internal set; }
        public long CastId { get; internal set; }
        public string SourceId { get; internal set; }
        public string SkillId { get; internal set; }
        public int StepIndex { get; internal set; }
        public int Iteration { get; internal set; }
        public long SpawnTick { get; internal set; }
        public long NextTick { get; internal set; }
        public int NextIndex { get; internal set; }
        public int OutgoingCount { get; internal set; }
        public IReadOnlyList<BoardPosition> Path { get; internal set; }
        internal SkillEffectDefinition Effect;
        internal float Value;
        internal DamageSourceSnapshot Snapshot;
        internal Action<UnitCombatState> Impact;
        internal HashSet<string> Hit=new HashSet<string>(StringComparer.Ordinal);
    }
    public enum PathProjectileEventKind { Spawned, Hit, Completed, Cancelled }
    public readonly struct PathProjectileEvent
    {
        public long Id { get; }
        public long CastId { get; }
        public string SourceId { get; }
        public string TargetId { get; }
        public int Leg { get; }
        public PathProjectileEventKind Kind { get; }
        internal PathProjectileEvent(PathProjectileState p,PathProjectileEventKind kind,string target=null)
        {Id=p.Id;CastId=p.CastId;SourceId=p.SourceId;TargetId=target;Leg=p.Effect.Returns&&p.NextIndex>=p.OutgoingCount?1:0;Kind=kind;}
    }
    public sealed class PathProjectileSystem
    {
        private readonly BattleState battle;
        private readonly List<PathProjectileState> active=new List<PathProjectileState>();
        private readonly List<PathProjectileEvent> events=new List<PathProjectileEvent>();
        private readonly IDamageProcessor damage=new DamageProcessor();private long nextId;
        public IReadOnlyList<PathProjectileState> Active=>Array.AsReadOnly(active.Select(Clone).ToArray());
        public IReadOnlyList<PathProjectileEvent> EventsThisTick=>Array.AsReadOnly(events.ToArray());
        private static PathProjectileState Clone(PathProjectileState p)=>new PathProjectileState{Id=p.Id,CastId=p.CastId,SourceId=p.SourceId,
            SkillId=p.SkillId,StepIndex=p.StepIndex,Iteration=p.Iteration,SpawnTick=p.SpawnTick,NextTick=p.NextTick,
            NextIndex=p.NextIndex,OutgoingCount=p.OutgoingCount,Path=p.Path};
        internal PathProjectileSystem(BattleState battle){this.battle=battle;}
        internal void BeginTick()=>events.Clear();
        internal bool Spawn(UnitCombatState caster,UnitCombatState target,string skill,SkillEffectDefinition effect,float value,
            long castId,int stepIndex,int iteration,Action<UnitCombatState> impact=null)
        {
            if(!caster.IsAlive||!caster.IsOnBoard||target==null||!target.IsTargetable)return false;
            var outgoing=SkillGeometry.Ray(caster.Position,target.Position,effect.Spatial.Extent);
            if(outgoing.Count==0)return false;
            var path=effect.Returns?outgoing.Concat(outgoing.Reverse()).Concat(new[]{caster.Position}).ToArray():outgoing.ToArray();
            var p=new PathProjectileState{Id=checked(++nextId),CastId=castId,SourceId=caster.UnitInstanceId,SkillId=skill,StepIndex=stepIndex,
                Iteration=iteration,SpawnTick=battle.CurrentTick,OutgoingCount=outgoing.Count,Path=Array.AsReadOnly(path),Effect=effect,Value=value,
                Impact=impact,Snapshot=SkillNumericMath.Capture(battle,caster)};
            p.NextTick=Due(p,0);Due(p,path.Length-1);active.Add(p);events.Add(new PathProjectileEvent(p,PathProjectileEventKind.Spawned));return true;
        }
        private long Due(PathProjectileState p,int index)=>checked(p.SpawnTick+
            BattleSimulation.ToTicks((index+1)/(double)p.Effect.ProjectileSpeed,battle.TickRate));
        internal void Advance()
        {
            while(true) {
                var p=active.Where(x=>x.NextTick<=battle.CurrentTick).OrderBy(x=>x.NextTick).ThenBy(x=>x.Id).FirstOrDefault();
                if(p==null)return;
                if(p.NextIndex==p.OutgoingCount)p.Hit.Clear();
                var source=battle.Units.Single(u=>u.UnitInstanceId==p.SourceId);var cell=p.Path[p.NextIndex];
                var victims=battle.Units.Where(u=>u.TeamId!=source.TeamId&&u.IsTargetable&&u.Position.Equals(cell)&&!p.Hit.Contains(u.UnitInstanceId))
                    .OrderBy(u=>u.UnitInstanceId,StringComparer.Ordinal).ToArray();
                foreach(var target in victims) {
                    if(p.Effect.Spatial.MaxTargets>0&&p.Hit.Count>=p.Effect.Spatial.MaxTargets)break;
                    p.Hit.Add(target.UnitInstanceId);
                    var result=damage.Apply(battle,SkillNumericMath.Request(battle,source,target,p.Effect,p.Value,snapshot:p.Snapshot));
                    battle.RecordSkillEffect(new SkillEffectEvent(p.SkillId,p.SourceId,target.UnitInstanceId,0,SkillEffectType.PathProjectileDamage,
                        battle.CurrentTick,p.Value,result.AppliedDamage,SkillEffectOutcome.Applied,SkillEffectSkipReason.None,p.CastId,p.StepIndex,p.Iteration));
                    events.Add(new PathProjectileEvent(p,PathProjectileEventKind.Hit,target.UnitInstanceId));
                    if(target.IsAlive&&target.IsOnBoard)p.Impact?.Invoke(target);
                }
                p.NextIndex++;
                if(p.NextIndex==p.Path.Count){active.Remove(p);events.Add(new PathProjectileEvent(p,PathProjectileEventKind.Completed));}
                else p.NextTick=Due(p,p.NextIndex);
            }
        }
        internal void EndBattle(){foreach(var p in active)events.Add(new PathProjectileEvent(p,PathProjectileEventKind.Cancelled));active.Clear();}
    }
}
