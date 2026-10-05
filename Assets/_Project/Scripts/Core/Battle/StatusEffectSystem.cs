using System;
using System.Collections.Generic;
using System.Linq;
namespace PokeChess.Core.Battle
{
    public sealed class StatusEffectSystem
    {
        private sealed class Entry
        {
            public long Id,Start,End; public StatusEffectDefinition Def; public string Source,Target; public int Count=1;
            public Entry Copy() => (Entry)MemberwiseClone();
            public StatusEffectSnapshot Snapshot()=>new StatusEffectSnapshot(Id,Def,Source,Target,Start,End,Count);
        }
        private readonly BattleState battle;
        private List<Entry> entries=new List<Entry>();
        private readonly List<StatusEvent> events=new List<StatusEvent>();
        private long nextId;
        private StatusCatalog catalog=new StatusCatalog(Array.Empty<StatusEffectDefinition>());
        internal event Action<UnitCombatState> Changed;
        internal StatusEffectSystem(BattleState battle) {this.battle=battle;}
        public IReadOnlyList<StatusEffectSnapshot> Active => Array.AsReadOnly(entries.Where(e=>e.End>battle.CurrentTick).OrderBy(e=>e.Id).Select(e=>e.Snapshot()).ToArray());
        public IReadOnlyList<StatusEvent> EventsThisTick => Array.AsReadOnly(events.ToArray());
        public void UseCatalog(StatusCatalog value) {catalog=value??throw new ArgumentNullException(nameof(value));}
        public StatusEffectDefinition GetDefinition(string id)=>catalog.Get(id);
        private UnitCombatState Unit(string id)=>battle.Units.SingleOrDefault(u=>u.UnitInstanceId==id);
        private IEnumerable<Entry> Live(UnitCombatState unit)=>entries.Where(e=>e.Target==unit.UnitInstanceId&&e.End>battle.CurrentTick);
        public bool Has(UnitCombatState unit,CrowdControlKind kind)=>Live(unit).Any(e=>e.Def.CrowdControl==kind);
        public string TauntSource(UnitCombatState unit)=>Live(unit).Where(e=>e.Def.CrowdControl==CrowdControlKind.Taunt)
            .Where(e=>{var s=Unit(e.Source);return s!=null&&s.IsTargetable&&s.TeamId!=unit.TeamId;})
            .OrderByDescending(e=>e.End).ThenBy(e=>e.Source,StringComparer.Ordinal).Select(e=>e.Source).FirstOrDefault();
        internal void BeginTick() {events.Clear();Cleanup();}
        internal void Cleanup()
        {
            foreach(var e in entries.OrderBy(e=>e.Id).ToArray()) {
                var target=Unit(e.Target);
                if(e.End<=battle.CurrentTick||!target.IsAlive||!target.IsOnBoard) {
                    entries.Remove(e);events.Add(new StatusEvent(e.Snapshot(),battle.CurrentTick,
                        e.End<=battle.CurrentTick?StatusEventKind.Expired:StatusEventKind.Removed));Changed?.Invoke(target);
                }
            }
        }
        public bool Remove(long instanceId)
        {
            var e=entries.SingleOrDefault(x=>x.Id==instanceId);if(e==null)return false;
            entries.Remove(e);events.Add(new StatusEvent(e.Snapshot(),battle.CurrentTick,StatusEventKind.Removed));
            Changed?.Invoke(Unit(e.Target));return true;
        }
        public StatusEffectSnapshot Apply(string id,string sourceId,string targetId)=>Apply(catalog.Get(id),sourceId,targetId);
        public StatusEffectSnapshot Apply(StatusEffectDefinition d,string sourceId,string targetId)
        {
            if(d==null)throw new ArgumentNullException(nameof(d));
            var source=Unit(sourceId);var target=Unit(targetId);
            if(source==null||target==null||!source.IsAlive||!source.IsOnBoard||!target.IsAlive||!target.IsOnBoard||
                (d.TargetTeam==StatusTargetTeam.Enemy?(source.TeamId==target.TeamId||!target.IsTargetable):source.TeamId!=target.TeamId))
                throw new InvalidOperationException("Invalid status participants.");
            int ticks=BattleSimulation.ToTicks(d.Duration*(d.CrowdControl!=CrowdControlKind.None&&battle.IsOvertime ? .34 : 1),battle.TickRate);
            long end=checked(battle.CurrentTick+ticks);
            var previous=entries;entries=entries.Select(e=>e.Copy()).ToList();
            Entry applied=null;StatusEventKind kind=StatusEventKind.Applied;
            try {
                var cc=d.CrowdControl!=CrowdControlKind.None;
                var existing=entries.FirstOrDefault(e=>e.Target==targetId&&e.End>battle.CurrentTick&&
                    (cc?e.Def.CrowdControl==d.CrowdControl&&e.Source==sourceId:
                        e.Def.CrowdControl==CrowdControlKind.None&&e.Def.StackGroup==d.StackGroup&&
                        (d.SourceRule==SourceStackRule.Shared||e.Source==sourceId)));
                if(existing!=null&&!cc) {
                    if(existing.Def.StackPolicy!=d.StackPolicy||existing.Def.SourceRule!=d.SourceRule||
                        existing.Def.Modifiers.Count!=d.Modifiers.Count||
                        existing.Def.Modifiers.Where((m,i)=>m.Stat!=d.Modifiers[i].Stat||m.Type!=d.Modifiers[i].Type).Any())
                        throw new ArgumentException("Stack group definitions must use matching policies and modifier shapes.");
                }
                if(!cc&&d.StackPolicy==StackPolicy.Strongest)
                    existing=entries.FirstOrDefault(e=>e.Target==targetId&&e.End>battle.CurrentTick&&e.Def.Id==d.Id&&e.Source==sourceId);
                if(existing!=null&&cc) {
                    existing.End=Math.Max(existing.End,end);
                    if(d.CrowdControl==CrowdControlKind.Slow&&d.SlowFraction>existing.Def.SlowFraction)existing.Def=d;
                    applied=existing;kind=StatusEventKind.Refreshed;
                } else if(existing!=null&&d.StackPolicy==StackPolicy.None) {applied=existing;kind=StatusEventKind.Ignored;}
                else if(existing!=null&&d.StackPolicy!=StackPolicy.Strongest) {
                    existing.End=end;
                    if(d.StackPolicy==StackPolicy.Stack)existing.Count=Math.Min(existing.Count+1,existing.Def.MaxStack);
                    applied=existing;kind=d.StackPolicy==StackPolicy.Stack?StatusEventKind.Stacked:StatusEventKind.Refreshed;
                } else {
                    if(existing!=null&&existing.Def.Id==d.Id&&existing.Source==sourceId&&d.StackPolicy==StackPolicy.Strongest) {
                        existing.End=end;applied=existing;kind=StatusEventKind.Refreshed;
                    } else {
                        applied=new Entry{Id=checked(nextId+1),Def=d,Source=sourceId,Target=targetId,Start=battle.CurrentTick,End=end};
                        entries.Add(applied);
                    }
                }
                Resolve(target);ResolveDamageModifiers(target);
            } catch {entries=previous;throw;}
            if(kind==StatusEventKind.Applied)nextId=applied.Id;
            var snapshot=applied.Snapshot();events.Add(new StatusEvent(snapshot,battle.CurrentTick,kind));
            if(kind!=StatusEventKind.Ignored)Changed?.Invoke(target);
            return kind==StatusEventKind.Ignored?null:snapshot;
        }
        private IEnumerable<Entry> StatEntries(UnitCombatState unit)
        {
            var all=Live(unit).Where(e=>e.Def.CrowdControl==CrowdControlKind.None).OrderBy(e=>e.Def.Id,StringComparer.Ordinal)
                .ThenBy(e=>e.Source,StringComparer.Ordinal).ThenBy(e=>e.Id).ToArray();
            foreach(var e in all.Where(e=>e.Def.StackPolicy!=StackPolicy.Strongest))yield return e;
            foreach(var group in all.Where(e=>e.Def.StackPolicy==StackPolicy.Strongest).GroupBy(e=>
                (e.Def.StackGroup,e.Def.Modifiers[0].Stat,e.Def.Modifiers[0].Type,
                e.Def.Modifiers[0].Value<(e.Def.Modifiers[0].Type==StatModifierType.Multiplicative?1:0),
                e.Def.SourceRule==SourceStackRule.PerSource?e.Source:"shared"))) {
                yield return group.OrderByDescending(e=>Math.Abs(e.Def.Modifiers[0].Value-
                    (e.Def.Modifiers[0].Type==StatModifierType.Multiplicative?1:0))).ThenBy(e=>e.Def.Id,StringComparer.Ordinal)
                    .ThenBy(e=>e.Source,StringComparer.Ordinal).ThenBy(e=>e.Id).First();
            }
        }
        private double Calculate(UnitCombatState u,CombatStat stat,double original)
        {
            double flat=0,percent=0,multi=1;
            foreach(var e in StatEntries(u))foreach(var m in e.Def.Modifiers.Where(m=>m.Stat==stat)) {
                if(m.Type==StatModifierType.Flat)flat+=m.Value*e.Count;
                else if(m.Type==StatModifierType.Percentage)percent+=m.Value*e.Count;
                else multi*=Math.Pow(m.Value,e.Count);
            }
            return (original+flat)*(1+percent)*multi;
        }
        private static float Store(double value,double minimum=0,double maximum=float.MaxValue)
        {
            if(double.IsNaN(value)||double.IsInfinity(value)||Math.Abs(value)>float.MaxValue)throw new OverflowException("Status stat exceeds model capacity.");
            return (float)Math.Max(minimum,Math.Min(maximum,value));
        }
        public EffectiveCombatStats Resolve(UnitCombatState u)
        {
            var s=u.Stats;double power=battle.IsOvertime?2:1;double speed=battle.IsOvertime?3:1;double slow=Live(u).Where(e=>e.Def.CrowdControl==CrowdControlKind.Slow)
                .Select(e=>(double)e.Def.SlowFraction).DefaultIfEmpty(0).Max();
            return new EffectiveCombatStats(Store(Calculate(u,CombatStat.Attack,s.Attack)*power),
                Store(Calculate(u,CombatStat.SpellPower,s.SpellPower)*power),Store(Calculate(u,CombatStat.Armor,s.Armor),-float.MaxValue),
                Store(Calculate(u,CombatStat.MagicResistance,s.MagicResistance),-float.MaxValue),
                Store(Calculate(u,CombatStat.AttackSpeed,s.AttackSpeed)*speed,.1,5),
                Store(Calculate(u,CombatStat.MoveSpeed,s.MoveSpeed)*speed*(1-slow),.1));
        }
        public DamageModifiers ResolveDamageModifiers(UnitCombatState u)=>new DamageModifiers(u.DamageModifiers.BonusAttackDamage,
            Store(Calculate(u,CombatStat.DamageAmplification,u.DamageModifiers.Amplification)),
            Store(Calculate(u,CombatStat.DamageReduction,u.DamageModifiers.Reduction),0,1));
    }
}
