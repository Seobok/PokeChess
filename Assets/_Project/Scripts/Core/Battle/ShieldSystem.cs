using System;
using System.Collections.Generic;
using System.Linq;

namespace PokeChess.Core.Battle
{
    public readonly struct ShieldSnapshot
    {
        public long Id { get; }
        public string SourceId { get; }
        public string TargetId { get; }
        public string Group { get; }
        public float Remaining { get; }
        public long ExpireTick { get; }
        internal ShieldSnapshot(long id,string source,string target,string group,float remaining,long expire)
        {Id=id;SourceId=source;TargetId=target;Group=group;Remaining=remaining;ExpireTick=expire;}
    }
    public enum ShieldEventKind { Applied, Replaced, Absorbed, Expired, Removed }
    public readonly struct ShieldEvent
    {
        public ShieldSnapshot Shield { get; }
        public ShieldEventKind Kind { get; }
        public long Tick { get; }
        internal ShieldEvent(ShieldSnapshot shield,ShieldEventKind kind,long tick){Shield=shield;Kind=kind;Tick=tick;}
    }
    public sealed class ShieldSystem
    {
        private sealed class Entry
        {public long Id,End;public string Source,Target,Group;public float Remaining;
         public ShieldSnapshot Snapshot()=>new ShieldSnapshot(Id,Source,Target,Group,Remaining,End);}
        private readonly BattleState battle;private long nextId;
        private readonly List<Entry> entries=new List<Entry>();
        private readonly List<ShieldEvent> events=new List<ShieldEvent>();
        internal ShieldSystem(BattleState battle){this.battle=battle;}
        public IReadOnlyList<ShieldSnapshot> Active=>Array.AsReadOnly(entries.Where(e=>e.End>battle.CurrentTick&&e.Remaining>0).OrderBy(e=>e.Id).Select(e=>e.Snapshot()).ToArray());
        public IReadOnlyList<ShieldEvent> EventsThisTick=>Array.AsReadOnly(events.ToArray());
        internal float Total(UnitCombatState unit)=>(float)entries.Where(e=>e.Target==unit.UnitInstanceId&&e.End>battle.CurrentTick).Sum(e=>(double)e.Remaining);
        internal void SetTotal(UnitCombatState unit,float value)
        {
            DamageCalculator.Validate(value,true);float before=unit.CurrentShield;
            if(value<before)Absorb(unit,before-value);else unit.LegacyShield+=value-before;
        }
        public void Add(UnitCombatState target,string source,string group,float value,float duration)
        {
            DamageCalculator.Validate(value,true);DamageCalculator.Validate(duration,true);
            if(!battle.Units.Contains(target)||!target.IsAlive||!target.IsOnBoard||duration<=0||string.IsNullOrWhiteSpace(source)||string.IsNullOrWhiteSpace(group))throw new ArgumentException();
            long end=checked(battle.CurrentTick+BattleSimulation.ToTicks(duration,battle.TickRate));
            var e=entries.FirstOrDefault(x=>x.Source==source&&x.Target==target.UnitInstanceId&&x.Group==group&&x.End>battle.CurrentTick);
            double total=(double)target.CurrentShield-(e?.Remaining??0)+value;if(total>float.MaxValue)throw new OverflowException();
            bool replace=e!=null;
            if(e==null){e=new Entry{Id=checked(++nextId),Source=source,Target=target.UnitInstanceId,Group=group};entries.Add(e);}
            e.Remaining=value;e.End=end;Record(e,replace?ShieldEventKind.Replaced:ShieldEventKind.Applied);
        }
        internal float Absorb(UnitCombatState unit,float amount)
        {
            float remaining=Math.Min(amount,unit.CurrentShield),absorbed=remaining;
            foreach(var e in entries.Where(e=>e.Target==unit.UnitInstanceId&&e.End>battle.CurrentTick&&e.Remaining>0).OrderBy(e=>e.End).ThenBy(e=>e.Id).ToArray()) {
                float take=Math.Min(remaining,e.Remaining);if(take<=0)break;e.Remaining-=take;remaining-=take;Record(e,ShieldEventKind.Absorbed);
                if(e.Remaining==0)entries.Remove(e);
            }
            unit.LegacyShield=Math.Max(0,unit.LegacyShield-remaining);return absorbed;
        }
        internal void BeginTick()
        {
            events.Clear();foreach(var e in entries.ToArray()) {
                var unit=battle.Units.Single(u=>u.UnitInstanceId==e.Target);
                if(e.End<=battle.CurrentTick||!unit.IsAlive||!unit.IsOnBoard){Record(e,e.End<=battle.CurrentTick?ShieldEventKind.Expired:ShieldEventKind.Removed);entries.Remove(e);}
            }
        }
        private void Record(Entry e,ShieldEventKind kind)=>events.Add(new ShieldEvent(e.Snapshot(),kind,battle.CurrentTick));
    }
    public sealed class HealingSystem
    {
        private readonly BattleState battle;
        internal HealingSystem(BattleState battle){this.battle=battle;}
        public float Apply(UnitCombatState target,float amount,bool overtime=true)
        {
            DamageCalculator.Validate(amount,true);
            if(target==null||!battle.Units.Contains(target))throw new ArgumentException();
            if(target.CurrentHP<=0||!target.IsOnBoard)return 0;
            float before=target.CurrentHP;
            double value=amount*target.StatusSystem.HealingMultiplier(target)*(overtime&&battle.IsOvertime?.34:1);
            target.SetVitals((float)Math.Min(target.Stats.MaxHP,before+value),target.CurrentEnergy);return target.CurrentHP-before;
        }
    }
}
