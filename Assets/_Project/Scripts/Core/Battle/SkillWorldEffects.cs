using System;
using System.Collections.Generic;
using System.Linq;
using PokeChess.Core.Board;
using PokeChess.Core.Pokemon;
namespace PokeChess.Core.Battle
{
    public sealed class GroundSkillSnapshot
    {
        public long Id,StartTick,EndTick;public string SourceId,StatusId;public BoardPosition Center;public int Radius;
    }
    public sealed class ChainSkillSnapshot
    {
        public long Id,NextTick;public string SourceId,SkillId;public BoardPosition Center;public string[] Visited;
    }
    public sealed class SkillWorldEffects
    {
        private sealed class Ground
        {public GroundSkillSnapshot View;public StatusEffectDefinition Status;public readonly Dictionary<string,long> Members=new Dictionary<string,long>();}
        private sealed class Chain
        {public long Id,Start,Next;public int Iteration;public UnitCombatState Source;public SkillDefinition Skill;public SkillEffectDefinition Effect;
         public float Value;public float[] Children;public DamageSourceSnapshot Snapshot;public BoardPosition Center;public readonly HashSet<string> Visited=new HashSet<string>();}
        private readonly BattleState battle;private long next;
        private readonly List<Ground> grounds=new List<Ground>();private readonly List<Chain> chains=new List<Chain>();
        private sealed class ProjectileCompletion
        {public int Remaining;public bool Hit;public System.Collections.Generic.IReadOnlyList<SkillEffectDefinition> Effects;public float[] Values;public DamageSourceSnapshot Snapshot;}
        private readonly Dictionary<string,ProjectileCompletion> projectileGroups=new Dictionary<string,ProjectileCompletion>();
        internal Action<bool> ProjectileGroup(UnitCombatState source,UnitCombatState target,SkillDefinition skill,SkillEffectDefinition effect,long castId,DamageSourceSnapshot snapshot,float[] values)
        {
            string key=source.UnitInstanceId+"/"+castId+"/"+skill.Id+"/"+target.UnitInstanceId;
            if(!projectileGroups.TryGetValue(key,out var group))projectileGroups[key]=group=new ProjectileCompletion{Remaining=effect.ProjectileGroupSize};
            if(effect.PathImpactEffects.Count>0){group.Effects=effect.PathImpactEffects;group.Values=values;group.Snapshot=snapshot;}
            return hit=>{
                group.Hit|=hit;if(--group.Remaining>0)return;projectileGroups.Remove(key);
                if(group.Hit&&group.Effects!=null&&target.CurrentHP>0&&target.IsTargetable)
                    new SkillEffectExecutor().ExecuteGenerated(battle,source,target,skill,group.Effects,group.Values,group.Snapshot);
            };
        }
        internal SkillWorldEffects(BattleState battle){this.battle=battle;}
        public IReadOnlyList<GroundSkillSnapshot> Grounds=>grounds.Select(g=>new GroundSkillSnapshot{Id=g.View.Id,SourceId=g.View.SourceId,StatusId=g.View.StatusId,
            StartTick=g.View.StartTick,EndTick=g.View.EndTick,Center=g.View.Center,Radius=g.View.Radius}).ToArray();
        public IReadOnlyList<ChainSkillSnapshot> Chains=>chains.Select(c=>new ChainSkillSnapshot{Id=c.Id,SourceId=c.Source.UnitInstanceId,SkillId=c.Skill.Id,
            NextTick=c.Next,Center=c.Center,Visited=c.Visited.OrderBy(x=>x,StringComparer.Ordinal).ToArray()}).ToArray();
        internal void SpawnGround(UnitCombatState source,SkillEffectDefinition effect,BoardPosition center,float duration)
        {
            var def=battle.StatusEffects.GetDefinition(effect.ResolveStatusId(source.Rank));long id=checked(++next);
            int ticks=BattleSimulation.ToTicks(duration*(battle.IsOvertime&&def.Behavior==StatusBehavior.Blind?.34:1),battle.TickRate);
            // Each zone owns a separate lease; exiting one cannot clear another zone or a standalone blind.
            var lease=new StatusEffectDefinition(def.Id+"@"+id,1000,def.TargetTeam,def.CrowdControl,
                modifiers:def.Modifiers,stackGroup:def.StackGroup+"@"+id,slowFraction:def.SlowFraction,behavior:def.Behavior);
            grounds.Add(new Ground{Status=lease,View=new GroundSkillSnapshot{Id=id,SourceId=source.UnitInstanceId,StatusId=def.Id,Center=center,
                Radius=effect.Spatial.Extent,StartTick=battle.CurrentTick,EndTick=checked(battle.CurrentTick+ticks)}});
        }
        internal void AdvanceGrounds()
        {
            foreach(var g in grounds.ToArray()) {
                var source=battle.Units.Single(u=>u.UnitInstanceId==g.View.SourceId);
                var eligible=battle.CurrentTick>=g.View.EndTick?Array.Empty<UnitCombatState>():battle.Units.Where(u=>u.TeamId!=source.TeamId&&u.IsTargetable&&
                    HexCoordinates.Distance(g.View.Center,u.Position)<=g.View.Radius).ToArray();
                foreach(var id in g.Members.Keys.ToArray())if(!eligible.Any(u=>u.UnitInstanceId==id)) {battle.StatusEffects.Remove(g.Members[id]);g.Members.Remove(id);}
                if(battle.CurrentTick>g.View.StartTick)foreach(var unit in eligible)if(!g.Members.ContainsKey(unit.UnitInstanceId)) {
                    var status=battle.StatusEffects.ApplyGround(g.Status,g.View.SourceId,unit.UnitInstanceId);g.Members[unit.UnitInstanceId]=status.InstanceId;
                }
                if(battle.CurrentTick>=g.View.EndTick)grounds.Remove(g);
            }
        }
        internal void SpawnChain(UnitCombatState source,UnitCombatState primary,SkillDefinition skill,SkillEffectDefinition effect,float value)
        {
            if(primary==null)return;
            var chain=new Chain{Id=checked(++next),Start=battle.CurrentTick,Source=source,Skill=skill,Effect=effect,Value=value,Center=primary.Position,
                Snapshot=SkillNumericMath.Capture(battle,source),Children=effect.PathImpactEffects.Select(e=>SkillNumericMath.SourceValue(source,e)).ToArray()};
            chain.Visited.Add(primary.UnitInstanceId);chain.Next=checked(chain.Start+BattleSimulation.ToTicks(effect.ChainDelay,battle.TickRate));chains.Add(chain);
        }
        internal void AdvanceChains()
        {
            foreach(var c in chains.OrderBy(c=>c.Id).ToArray())if(c.Next<=battle.CurrentTick) {
                int count=c.Effect.ChainSequential?1:c.Effect.Spatial.MaxTargets;
                var targets=battle.Units.Where(u=>u.TeamId!=c.Source.TeamId&&u.IsTargetable&&!c.Visited.Contains(u.UnitInstanceId))
                    .OrderBy(u=>HexCoordinates.Distance(c.Center,u.Position)).ThenBy(u=>u.UnitInstanceId,StringComparer.Ordinal).ToArray();
                if(count>0)targets=targets.Take(count).ToArray();
                if(targets.Length==0){chains.Remove(c);continue;}
                foreach(var target in targets) {
                    c.Visited.Add(target.UnitInstanceId);c.Center=target.Position;
                    var result=new DamageProcessor().Apply(battle,SkillNumericMath.Request(battle,c.Source,target,c.Effect,c.Value,snapshot:c.Snapshot));
                    if(!result.Missed&&target.IsTargetable)new SkillEffectExecutor().ExecuteGenerated(battle,c.Source,target,c.Skill,c.Effect.PathImpactEffects,c.Children,c.Snapshot);
                }
                if(!c.Effect.ChainSequential){chains.Remove(c);continue;}
                c.Iteration++;c.Next=checked(c.Start+BattleSimulation.ToTicks((c.Iteration+1)*c.Effect.ChainDelay,battle.TickRate));
            }
        }
        internal void EndBattle(){foreach(var g in grounds)foreach(var id in g.Members.Values)battle.StatusEffects.Remove(id);grounds.Clear();chains.Clear();projectileGroups.Clear();}
    }
}
