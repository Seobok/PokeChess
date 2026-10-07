using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using PokeChess.Core.Battle;
using PokeChess.Core.Pokemon;
namespace PokeChess.Core.Tests
{
    internal sealed class CombatSandboxResult
    {
        public BattleResult Result;public BattleEndReason Reason;public long EndTick;
        public int Steps,Moves,Hits,Spawns,ProjectileHits,Casts,Heals,Shields,Statuses,Stacks,Ignored,Overtimes,Deaths,LongestQuiet;
        public string FinalState;
        public string InitialDefinitions;
        public long TargetRngDraws;
        public readonly HashSet<CrowdControlKind> CcKinds=new HashSet<CrowdControlKind>();
        public readonly HashSet<StackPolicy> Policies=new HashSet<StackPolicy>();
        public readonly List<string> TickHashes=new List<string>();
    }
    internal static class CombatSandboxRunner
    {
        public static string ReportFolder=>Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../CombatSandbox"));
        private static readonly Dictionary<Type,PropertyInfo[]> properties=new Dictionary<Type,PropertyInfo[]>();
        public static string Canonical(object value)
        {
            var output=new StringBuilder();Append(output,value);return output.ToString();
        }
        private static void Append(StringBuilder output,object value)
        {
            if(value==null){output.Append("null;");return;}
            if(value is string str){output.Append(str.Length).Append(':').Append(str).Append(';');return;}
            var type=value.GetType();
            if(type.IsEnum||type.IsPrimitive||value is decimal) {
                output.Append(value is float f?f.ToString("R",CultureInfo.InvariantCulture):value is double d?d.ToString("R",CultureInfo.InvariantCulture):Convert.ToString(value,CultureInfo.InvariantCulture)).Append(';');return;
            }
            if(value is IEnumerable sequence){output.Append('[');foreach(var item in sequence)Append(output,item);output.Append(']');return;}
            if(!properties.TryGetValue(type,out var getters))properties[type]=getters=type.GetProperties(BindingFlags.Public|BindingFlags.Instance)
                .Where(p=>p.CanRead&&p.GetIndexParameters().Length==0).OrderBy(p=>p.Name,StringComparer.Ordinal).ToArray();
            output.Append('{');foreach(var p in getters){output.Append(p.Name).Append('=');Append(output,p.GetValue(value));}output.Append('}');
        }
        private static string Snapshot(CombatSandboxScenario scenario,object signals=null,Dictionary<string,(EffectiveCombatStats stats,DamageModifiers modifiers)> cached=null)
        {
            var b=scenario.Battle;var sim=scenario.Simulation;var text=new StringBuilder();
            Append(text,new object[]{b.CurrentTick,b.IsOvertime,b.Result,b.EndReason,b.EndTick});
            foreach(var u in b.Units.OrderBy(u=>u.UnitInstanceId,StringComparer.Ordinal)) {
                var e=cached!=null?cached[u.UnitInstanceId].stats:u.EffectiveStats;
                var m=cached!=null?cached[u.UnitInstanceId].modifiers:u.EffectiveDamageModifiers;
                Append(text,new object[]{u.UnitInstanceId,u.Position.Column,u.Position.Row,u.CurrentHP,u.CurrentEnergy,u.CurrentShield,
                    u.EnergyLockUntilTick,u.IsAlive,u.IsOnBoard,u.IsUntargetable,u.ActionState,u.CurrentTargetId,u.TauntSourceId,u.EffectiveTauntSource,
                    e.Attack,e.SpellPower,e.Armor,e.MagicResistance,e.AttackSpeed,e.MoveSpeed,m.BonusAttackDamage,m.Amplification,m.Reduction,
                    u.DamageModifiers.BonusAttackDamage,u.DamageModifiers.Amplification,u.DamageModifiers.Reduction});
                var r=sim.GetRuntime(u.UnitInstanceId);
                Append(text,new object[]{r.StartTick,r.EndTick,r.EffectTick,r.EffectApplied,r.IsSkillActive,r.MoveDestination,
                    r.MovementTargetId,r.IsAttackTargetLocked,r.NextAttackTick});
                var c=r.SkillCast;
                Append(text,c==null?null:new object[]{c.Definition.Id,c.TargetId,c.StartTick,c.EffectTick,c.EndTick,c.EnergyLockTicks,c.EffectApplied,c.Finished,c.CancelReason});
            }
            foreach(var cell in b.Board.Cells) {
                if(b.Board.TryGetOccupant(cell,out var owner))Append(text,new object[]{"occupant",cell.Column,cell.Row,owner});
                if(sim.Reservations.TryGetOwner(cell,out var reserver))Append(text,new object[]{"reservation",cell.Column,cell.Row,reserver});
            }
            Append(text,b.Projectiles.Active);Append(text,b.StatusEffects.Active);
            Append(text,signals);Append(text,b.DamageResultsThisTick);Append(text,b.SkillEventsThisTick);Append(text,b.SkillEffectEventsThisTick);
            Append(text,b.Energy.EventsThisTick);Append(text,b.StatusEffects.EventsThisTick);Append(text,b.Projectiles.EventsThisTick);Append(text,b.LifecycleEventsThisTick);
            Append(text,new[]{b.RngStreams.Target,b.RngStreams.Critical,b.RngStreams.Skill,b.RngStreams.Item});
            return text.ToString();
        }
        private static void Require(bool valid,string message) {if(!valid)throw new InvalidOperationException(message);}
        private static void Finite(float value,string name) {Require(!float.IsNaN(value)&&!float.IsInfinity(value),"Non-finite "+name);}
        private static Dictionary<string,(EffectiveCombatStats stats,DamageModifiers modifiers)> Validate(CombatSandboxScenario s,long processedTick)
        {
            var cache=new Dictionary<string,(EffectiveCombatStats,DamageModifiers)>(StringComparer.Ordinal);
            var b=s.Battle;var sim=s.Simulation;var units=b.Units.ToDictionary(u=>u.UnitInstanceId,StringComparer.Ordinal);
            foreach(var u in b.Units) {
                Finite(u.CurrentHP,"HP "+u.UnitInstanceId);Finite(u.CurrentEnergy,"Energy");Finite(u.CurrentShield,"Shield");
                Require(u.CurrentHP>=0&&u.CurrentHP<=u.Stats.MaxHP&&u.CurrentEnergy>=0&&u.CurrentShield>=0,"Invalid vitals "+u.UnitInstanceId);
                var e=u.EffectiveStats;var modifiers=u.EffectiveDamageModifiers;cache.Add(u.UnitInstanceId,(e,modifiers));foreach(float v in new[]{e.Attack,e.SpellPower,e.Armor,e.MagicResistance,e.AttackSpeed,e.MoveSpeed,
                    modifiers.Amplification,modifiers.Reduction})Finite(v,"Effective stat");
                var r=sim.GetRuntime(u.UnitInstanceId);
                if(u.IsAlive&&u.IsOnBoard) {
                    Require(b.Board.TryGetPosition(u.UnitInstanceId,out var pos)&&pos.Equals(u.Position),"Position lookup mismatch "+u.UnitInstanceId);
                    Require(b.Board.TryGetOccupant(u.Position,out var owner)&&owner==u.UnitInstanceId,"Occupant mismatch "+u.UnitInstanceId);
                    if(u.ActionState!=CombatActionState.Idle)Require(r.EndTick>=processedTick,"Expired action remained active "+u.UnitInstanceId);
                } else {
                    Require(!b.Board.TryGetPosition(u.UnitInstanceId,out _),"Unavailable unit occupies board "+u.UnitInstanceId);
                    Require(u.ActionState==CombatActionState.Dead&&u.CurrentTargetId==null&&r.EndTick==0&&!r.IsSkillActive,"Death cleanup incomplete "+u.UnitInstanceId);
                }
                if(r.MoveDestination.HasValue)Require(u.ActionState==CombatActionState.Moving&&
                    sim.Reservations.TryGetOwner(r.MoveDestination.Value,out var owner)&&owner==u.UnitInstanceId,"Missing movement reservation");
            }
            Require(b.Board.OccupiedCount==b.Units.Count(u=>u.IsAlive&&u.IsOnBoard),"Board occupied count mismatch");
            foreach(var cell in b.Board.Cells)if(sim.Reservations.TryGetOwner(cell,out var owner)) {
                Require(units.TryGetValue(owner,out var u)&&u.IsAlive&&u.IsOnBoard,"Invalid reservation owner");
                var r=sim.GetRuntime(owner);Require(u.ActionState==CombatActionState.Moving&&r.MoveDestination.HasValue&&r.MoveDestination.Value.Equals(cell),"Orphan reservation");
                Require(!b.Board.IsOccupied(cell),"Reserved destination already occupied");
            }
            foreach(var p in b.Projectiles.Active)Require(p.ArrivalTick>=b.CurrentTick&&units.ContainsKey(p.SourceId)&&units.ContainsKey(p.TargetId),"Invalid active projectile");
            foreach(var status in b.StatusEffects.Active)Require(units[status.TargetId].IsAlive&&units[status.TargetId].IsOnBoard,"Status remained on unavailable unit");
            return cache;
        }
        public static CombatSandboxResult Run(CombatSandboxScenario scenario,ulong seed,string variant,int? maxSteps=null,string diagnosticFolder=null)
        {
            var b=scenario.Battle;var sim=scenario.Simulation;var result=new CombatSandboxResult();var recent=new Queue<string>();
            var dead=new HashSet<string>(StringComparer.Ordinal);int endEvents=0,quiet=0;
            Func<string> definitions=()=>Canonical(b.Units.OrderBy(u=>u.UnitInstanceId,StringComparer.Ordinal).Select(u=>new {
                u.UnitInstanceId,u.DefinitionId,u.SkillId,u.OwnerPlayerId,u.TeamId,u.Rank,u.EvolutionStage,u.Stats,u.ItemInstanceIds}));
            result.InitialDefinitions=definitions();
            try {
                using(var sha=SHA256.Create()) {
                    int limit=maxSteps??checked((int)b.TimeLimitTick+2);
                    for(int i=0;i<limit&&b.Result==BattleResult.InProgress;i++) {
                        long tick=b.CurrentTick;var actions=sim.Step();result.Steps++;
                        var cached=Validate(scenario,tick);
                        Require(b.CurrentTick==(b.Result==BattleResult.InProgress?tick+1:tick)||
                            (b.Result!=BattleResult.InProgress&&b.CurrentTick==tick+1),"Tick did not advance correctly");
                        result.Moves+=actions.Count(a=>a.Action==CombatActionState.Moving&&a.Kind==CombatActionSignalKind.Completed);
                        result.Hits+=b.DamageResultsThisTick.Count;
                        result.Spawns+=b.Projectiles.EventsThisTick.Count(e=>e.Kind==ProjectileEventKind.Spawned);
                        result.ProjectileHits+=b.Projectiles.EventsThisTick.Count(e=>e.Kind==ProjectileEventKind.Hit);
                        result.Casts+=b.SkillEventsThisTick.Count(e=>e.Kind==SkillEventKind.CastStarted);
                        result.Heals+=b.SkillEffectEventsThisTick.Count(e=>e.Type==SkillEffectType.Heal&&e.ActualValue>0);
                        result.Shields+=b.SkillEffectEventsThisTick.Count(e=>e.Type==SkillEffectType.Shield&&e.ActualValue>0);
                        result.Statuses+=b.StatusEffects.EventsThisTick.Count(e=>e.Kind==StatusEventKind.Applied);
                        foreach(var e in b.StatusEffects.EventsThisTick) {
                            if(e.Effect.Definition.CrowdControl!=CrowdControlKind.None)result.CcKinds.Add(e.Effect.Definition.CrowdControl);
                            else result.Policies.Add(e.Effect.Definition.StackPolicy);
                        }
                        result.Stacks+=b.StatusEffects.EventsThisTick.Count(e=>e.Kind==StatusEventKind.Stacked);
                        result.Ignored+=b.StatusEffects.EventsThisTick.Count(e=>e.Kind==StatusEventKind.Ignored);
                        foreach(var e in b.LifecycleEventsThisTick) {
                            if(e.Kind==BattleLifecycleEventKind.UnitDied){Require(dead.Add(e.UnitId),"Duplicate UnitDied");result.Deaths++;}
                            if(e.Kind==BattleLifecycleEventKind.OvertimeStarted){result.Overtimes++;Require(result.Overtimes==1&&e.Tick==b.OvertimeStartTick,"Invalid Overtime event");}
                            if(e.Kind==BattleLifecycleEventKind.BattleEnded){endEvents++;Require(endEvents==1,"Duplicate BattleEnded");}
                        }
                        quiet=actions.Count+b.DamageResultsThisTick.Count+b.SkillEffectEventsThisTick.Count==0?quiet+1:0;
                        result.LongestQuiet=Math.Max(result.LongestQuiet,quiet);
                        recent.Enqueue(Canonical(new {Tick=tick,Actions=actions,Damage=b.DamageResultsThisTick,Skills=b.SkillEventsThisTick,
                            Status=b.StatusEffects.EventsThisTick,Projectiles=b.Projectiles.EventsThisTick,Lifecycle=b.LifecycleEventsThisTick}));
                        if(recent.Count>8)recent.Dequeue();
                        result.TickHashes.Add(BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Snapshot(scenario,actions,cached)))).Replace("-",""));
                    }
                }
                Require(b.Result!=BattleResult.InProgress,"Step-call limit exceeded (possible soft lock)");
                Require(endEvents==1&&b.EndTick.HasValue&&b.EndTick<=b.TimeLimitTick,"Invalid final lifecycle");
                Require(sim.Reservations.Count==0&&b.Projectiles.Active.Count==0,"End cleanup incomplete");
                Require(b.Units.All(u=>u.CurrentTargetId==null&&!sim.GetRuntime(u.UnitInstanceId).IsSkillActive&&
                    !sim.GetRuntime(u.UnitInstanceId).IsAttackTargetLocked&&sim.GetRuntime(u.UnitInstanceId).EndTick==0),"Active action/target after end");
                bool one=b.Units.Any(u=>u.TeamId==1&&u.IsAlive&&u.IsOnBoard),two=b.Units.Any(u=>u.TeamId==2&&u.IsAlive&&u.IsOnBoard);
                var expected=b.EndReason==BattleEndReason.TimeLimit?BattleResult.Draw:one?BattleResult.TeamOneWin:two?BattleResult.TeamTwoWin:BattleResult.Draw;
                Require(b.Result==expected,"Result disagrees with survivors");
                Require(b.EndReason!=BattleEndReason.TimeLimit||(one&&two&&b.EndTick==b.TimeLimitTick),"Invalid time-limit result");
                Require(b.EndReason!=BattleEndReason.Elimination||!one||!two,"Elimination with both teams alive");
                Require(definitions()==result.InitialDefinitions,"Original definitions changed during battle");
                result.FinalState=Snapshot(scenario);
                for(int i=0;i<3;i++){Require(sim.Step().Count==0,"Ended Step emitted actions");Require(Snapshot(scenario)==result.FinalState,"Ended Step mutated state");}
                bool rejected=false;try{sim.QueueInput(_=>{});}catch(InvalidOperationException){rejected=true;}Require(rejected,"Ended battle accepted input");
                result.TargetRngDraws=b.RngStreams.Target.DrawCount;result.Result=b.Result;result.Reason=b.EndReason;result.EndTick=b.EndTick.Value;return result;
            } catch(Exception error) {
                var folder=diagnosticFolder??ReportFolder;Directory.CreateDirectory(folder);var path=Path.Combine(folder,scenario.Name+"_"+seed+"_"+variant+"_failure.txt");
                string state;try{state=Snapshot(scenario);}catch(Exception snapshotError){state="Snapshot failed: "+snapshotError;}
                File.WriteAllText(path,"Scenario="+scenario.Name+" Seed="+seed+" Variant="+variant+" Tick="+b.CurrentTick+"\n"+error+
                    "\nRecent steps:\n"+string.Join("\n",recent)+"\nCurrent state:\n"+state);
                throw new InvalidOperationException("Sandbox failed: "+scenario.Name+" seed="+seed+" variant="+variant+" tick="+b.CurrentTick+"; diagnostic: "+path,error);
            }
        }
    }
}
