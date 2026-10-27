using System;
using System.Collections.Generic;
using System.Linq;
using PokeChess.Core.Board;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Battle
{
    public sealed class PreparedSkillStep
    {
        internal UnitCombatState Caster { get; }
        public BoardPosition Origin { get; }
        public BoardPosition? Center { get; }
        public IReadOnlyList<SkillAreaGeometry> Areas { get; }
        private readonly Action<int> execute;
        public PreparedSkillStep(UnitCombatState caster,BoardPosition origin,BoardPosition? center,Action<int> execute,IEnumerable<SkillAreaGeometry> areas=null)
        { Caster=caster??throw new ArgumentNullException(nameof(caster));Origin=origin;Center=center;
          this.execute=execute??throw new ArgumentNullException(nameof(execute));Areas=Array.AsReadOnly((areas??Array.Empty<SkillAreaGeometry>()).ToArray()); }
        internal void Execute(int iteration)=>execute(iteration);
    }
    public enum IndependentSkillEventKind { Created, Pulse, Completed, Cancelled }
    public readonly struct IndependentSkillSnapshot
    {
        public long Id { get; }
        public long CastId { get; }
        public string SkillId { get; }
        public string CasterId { get; }
        public int StepIndex { get; }
        public int NextIteration { get; }
        public long NextTick { get; }
        public long LastTick { get; }
        public BoardPosition Origin { get; }
        public BoardPosition? Center { get; }
        public IReadOnlyList<SkillAreaGeometry> Areas { get; }
        internal IndependentSkillSnapshot(long id,long castId,string skill,string caster,int step,int iteration,
            long next,long last,BoardPosition origin,BoardPosition? center,IReadOnlyList<SkillAreaGeometry> areas=null)
        {Id=id;CastId=castId;SkillId=skill;CasterId=caster;StepIndex=step;NextIteration=iteration;
         NextTick=next;LastTick=last;Origin=origin;Center=center;Areas=areas??Array.Empty<SkillAreaGeometry>();}
    }
    public readonly struct IndependentSkillEvent
    {
        public IndependentSkillSnapshot Effect { get; }
        public IndependentSkillEventKind Kind { get; }
        public long Tick { get; }
        internal IndependentSkillEvent(IndependentSkillSnapshot effect,IndependentSkillEventKind kind,long tick)
        {Effect=effect;Kind=kind;Tick=tick;}
    }
    // Owns generated effects independently of the caster's current action/cast.
    public sealed class SkillTimelineSystem
    {
        private sealed class Entry
        {
            public long Id,CastId,Start;public string Skill;public int Index,Iteration;
            public SkillStepDefinition Step;public PreparedSkillStep Prepared;
        }
        private readonly BattleState battle;
        private readonly List<Entry> entries=new List<Entry>();
        private readonly List<IndependentSkillEvent> events=new List<IndependentSkillEvent>();
        private long nextId;
        internal SkillTimelineSystem(BattleState battle){this.battle=battle;}
        private long Due(Entry e,int iteration)=>checked(e.Start+
            BattleSimulation.ToTicks(e.Step.FirstDelay,battle.TickRate,false)+e.Step.RepeatOffset(iteration,battle.TickRate));
        private IndependentSkillSnapshot Snapshot(Entry e)=>new IndependentSkillSnapshot(e.Id,e.CastId,e.Skill,
            e.Prepared.Caster.UnitInstanceId,e.Index,e.Iteration,Due(e,Math.Min(e.Iteration,e.Step.Count-1)),
            Due(e,e.Step.Count-1),e.Prepared.Origin,e.Prepared.Center,e.Prepared.Areas);
        public IReadOnlyList<IndependentSkillSnapshot> Active=>Array.AsReadOnly(entries.OrderBy(e=>e.Id).Select(Snapshot).ToArray());
        public IReadOnlyList<IndependentSkillEvent> EventsThisTick=>Array.AsReadOnly(events.ToArray());
        internal void BeginTick()=>events.Clear();
        internal void Spawn(SkillCastRuntime cast,int index,PreparedSkillStep prepared)
        {
            var e=new Entry{Id=checked(++nextId),CastId=cast.CastId,Skill=cast.Definition.Id,Index=index,
                Start=battle.CurrentTick,Step=cast.Definition.Steps[index],Prepared=prepared};
            // Validate all end-time arithmetic before registering the effect.
            Due(e,e.Step.Count-1);entries.Add(e);Record(e,IndependentSkillEventKind.Created);
        }
        internal void Advance()
        {
            foreach(var e in entries.OrderBy(e=>e.Id).ToArray())
                if(!e.Step.SurvivesCaster&&(!e.Prepared.Caster.IsAlive||!e.Prepared.Caster.IsOnBoard))
                {Record(e,IndependentSkillEventKind.Cancelled);entries.Remove(e);}
            // Chronological ordering also handles intervals rounding to the same Tick.
            while(true)
            {
                var e=entries.Where(x=>Due(x,x.Iteration)<=battle.CurrentTick)
                    .OrderBy(x=>Due(x,x.Iteration)).ThenBy(x=>x.CastId).ThenBy(x=>x.Index).ThenBy(x=>x.Id).FirstOrDefault();
                if(e==null)return;
                if(!e.Step.SurvivesCaster&&(!e.Prepared.Caster.IsAlive||!e.Prepared.Caster.IsOnBoard))
                {Record(e,IndependentSkillEventKind.Cancelled);entries.Remove(e);continue;}
                int iteration=e.Iteration++;
                e.Prepared.Execute(iteration);
                events.Add(new IndependentSkillEvent(new IndependentSkillSnapshot(e.Id,e.CastId,e.Skill,
                    e.Prepared.Caster.UnitInstanceId,e.Index,iteration,battle.CurrentTick,Due(e,e.Step.Count-1),
                    e.Prepared.Origin,e.Prepared.Center,e.Prepared.Areas),IndependentSkillEventKind.Pulse,battle.CurrentTick));
                if(e.Iteration==e.Step.Count){Record(e,IndependentSkillEventKind.Completed);entries.Remove(e);}
            }
        }
        internal void EndBattle()
        {
            foreach(var e in entries.OrderBy(x=>x.Id))Record(e,IndependentSkillEventKind.Cancelled);
            entries.Clear();
        }
        private void Record(Entry e,IndependentSkillEventKind kind)=>events.Add(new IndependentSkillEvent(Snapshot(e),kind,battle.CurrentTick));
    }
}
