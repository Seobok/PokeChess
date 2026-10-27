using System;
using System.Collections.Generic;
using System.Linq;
namespace PokeChess.Core.Battle
{
    public sealed class StatusEffectSystem
    {
        private sealed class Entry
        {
            public long Id,Start,End,Next;public DamageSourceSnapshot? Offense; public int Pulses;public float RestAmount;public int Extensions; public StatusEffectDefinition Def; public string Source,Target; public int Count=1;
            public Entry Copy() => (Entry)MemberwiseClone();
            public StatusEffectSnapshot Snapshot()=>new StatusEffectSnapshot(Id,Def,Source,Target,Start,End,Count,Next,Pulses);
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
        internal void BeginTick() {events.Clear();Cleanup(false);}
        public bool HasBehavior(UnitCombatState unit,StatusBehavior behavior)=>Live(unit).Any(e=>e.Def.Behavior==behavior);
        public bool IsSleeping(UnitCombatState unit)=>HasBehavior(unit,StatusBehavior.Sleep)||HasBehavior(unit,StatusBehavior.RestSleep);
        public bool BlocksActions(UnitCombatState unit)=>HasBehavior(unit,StatusBehavior.ActionRecovery)||IsSleeping(unit)||HasBehavior(unit,StatusBehavior.Freeze);
        public bool BlocksAttacks(UnitCombatState unit)=>HasBehavior(unit,StatusBehavior.AttackRecovery);
        public int AttackRange(UnitCombatState unit)=>(int)Math.Max(1,Calculate(unit,CombatStat.AttackRange,unit.Stats.AttackRange));
        internal float SkillVulnerability(UnitCombatState unit)=>Live(unit).Where(e=>e.Def.Behavior==StatusBehavior.Confusion).Select(e=>e.Def.EffectValue).DefaultIfEmpty(0).Max();
        internal float Reflection(UnitCombatState unit)=>Live(unit).Where(e=>e.Def.Behavior==StatusBehavior.RestSleep).Select(e=>e.Def.ReflectionMultiplier).DefaultIfEmpty(0).Max();
        internal bool ConsumeCritical(UnitCombatState unit) {var e=Live(unit).Where(x=>x.Def.Behavior==StatusBehavior.GuaranteedCritical).ToArray();foreach(var x in e)Remove(x.Id);return e.Length>0;}
        internal float AttackSplash(UnitCombatState unit)=>Live(unit).Where(e=>e.Def.Behavior==StatusBehavior.AttackSplash).Select(e=>e.Def.EffectValue).DefaultIfEmpty(0).Max();
        internal float AdvanceSwordDance(UnitCombatState unit)
        {
            var e=Live(unit).FirstOrDefault(x=>x.Def.Behavior==StatusBehavior.SwordDance);if(e==null)return 0;
            e.Pulses=(e.Pulses+1)%3;unit.SwordDanceHits=e.Pulses;events.Add(new StatusEvent(e.Snapshot(),battle.CurrentTick,StatusEventKind.Pulse));
            return e.Pulses==0?e.Def.EffectValue:0;
        }
        internal void ExtendSwordDance(UnitCombatState unit)
        {
            var e=Live(unit).FirstOrDefault(x=>x.Def.Behavior==StatusBehavior.SwordDance);if(e==null||e.Extensions>=2)return;
            e.Extensions++;e.End=Math.Min(e.Start+BattleSimulation.ToTicks(10,battle.TickRate),e.End+BattleSimulation.ToTicks(2,battle.TickRate));
            events.Add(new StatusEvent(e.Snapshot(),battle.CurrentTick,StatusEventKind.Refreshed));
        }
        public bool BlocksMovement(UnitCombatState unit)=>BlocksActions(unit)||HasBehavior(unit,StatusBehavior.Paralysis);
        public float HealingMultiplier(UnitCombatState unit)=>(float)Math.Max(0,Calculate(unit,CombatStat.HealingReceived,1))*
            (HasBehavior(unit,StatusBehavior.Bleed)?.5f:HasBehavior(unit,StatusBehavior.Burn)?.75f:1);
        internal void NotifyTargetability(UnitCombatState unit)
        {
            if(unit.IsUntargetable)foreach(var other in battle.Units.Where(u=>u.CurrentTargetId==unit.UnitInstanceId))other.CurrentTargetId=null;
            Changed?.Invoke(unit);
        }
        internal void Cleanup(bool expire=true)
        {
            foreach(var e in entries.OrderBy(e=>e.Id).ToArray()) {
                var target=Unit(e.Target);
                if(expire&&e.End<=battle.CurrentTick||!target.IsAlive||!target.IsOnBoard) {
                    entries.Remove(e);events.Add(new StatusEvent(e.Snapshot(),battle.CurrentTick,
                        e.End<=battle.CurrentTick?StatusEventKind.Expired:StatusEventKind.Removed));NotifyTargetability(target);
                }
            }
        }
        public bool Remove(long instanceId)
        {
            var e=entries.SingleOrDefault(x=>x.Id==instanceId);if(e==null)return false;
            entries.Remove(e);events.Add(new StatusEvent(e.Snapshot(),battle.CurrentTick,StatusEventKind.Removed));
            NotifyTargetability(Unit(e.Target));return true;
        }
        public StatusEffectSnapshot Apply(string id,string sourceId,string targetId)=>Apply(catalog.Get(id),sourceId,targetId);
        public StatusEffectSnapshot Apply(StatusEffectDefinition d,string sourceId,string targetId) => ApplyCore(d,sourceId,targetId,false);
        internal StatusEffectSnapshot ApplyPersistent(string id,string sourceId,string targetId) => ApplyCore(catalog.Get(id),sourceId,targetId,true);
        internal StatusEffectSnapshot ApplyGround(StatusEffectDefinition definition,string sourceId,string targetId)=>ApplyCore(definition,sourceId,targetId,true);
        private StatusEffectSnapshot ApplyCore(StatusEffectDefinition d,string sourceId,string targetId,bool persistent)
        {
            if(d==null)throw new ArgumentNullException(nameof(d));
            var source=Unit(sourceId);var target=Unit(targetId);
            if(source==null||target==null||(!persistent&&(!source.IsAlive||!source.IsOnBoard))||!target.IsAlive||!target.IsOnBoard||
                (d.TargetTeam==StatusTargetTeam.Enemy?(source.TeamId==target.TeamId||!target.IsTargetable):source.TeamId!=target.TeamId))
                throw new InvalidOperationException("Invalid status participants.");
            bool timedControl=d.Behavior==StatusBehavior.AttackRecovery||d.Behavior==StatusBehavior.ActionRecovery||d.Behavior==StatusBehavior.RestSleep||d.Behavior==StatusBehavior.Blind||d.CrowdControl!=CrowdControlKind.None||d.Behavior==StatusBehavior.Sleep||d.Behavior==StatusBehavior.Freeze||d.Behavior==StatusBehavior.Paralysis||d.Behavior==StatusBehavior.Confusion;
            int ticks=BattleSimulation.ToTicks(d.Duration*(timedControl&&battle.IsOvertime ? .34 : 1),battle.TickRate);
            long end=checked(battle.CurrentTick+ticks);
            if(d.Behavior!=StatusBehavior.None)return ApplyBehavior(d,sourceId,targetId,end);
            var previous=entries;entries=entries.Select(e=>e.Copy()).ToList();
            Entry applied=null;StatusEventKind kind=StatusEventKind.Applied;
            try {
                var cc=d.CrowdControl!=CrowdControlKind.None;
                var existing=entries.FirstOrDefault(e=>e.Target==targetId&&e.End>battle.CurrentTick&&
                    (cc?e.Def.CrowdControl==d.CrowdControl&&e.Source==sourceId&&(d.CrowdControl!=CrowdControlKind.Slow||e.Def.StackGroup==d.StackGroup):
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
            if(d.CrowdControl==CrowdControlKind.Root&&d.EffectValue>0){applied.Start=battle.CurrentTick;applied.Pulses=0;applied.Next=NextPulse(applied);applied.RestAmount=SkillNumericMath.Magnitude(d.EffectValue,d.FixedSpellDamage?SkillValueSource.Flat:SkillValueSource.Attack,source.EffectiveStats.Attack,source.EffectiveStats.SpellPower,d.FixedSpellDamage,SkillPowerScaling.TotalSpellPower);applied.Offense=SkillNumericMath.Capture(battle,source,DamageOrigin.Periodic);}
            if(kind==StatusEventKind.Applied)nextId=applied.Id;
            var snapshot=applied.Snapshot();events.Add(new StatusEvent(snapshot,battle.CurrentTick,kind));
            if(kind!=StatusEventKind.Ignored)Changed?.Invoke(target);
            return kind==StatusEventKind.Ignored?null:snapshot;
        }
        private StatusEffectSnapshot ApplyBehavior(StatusEffectDefinition d,string source,string target,long end)
        {
            if((d.Behavior==StatusBehavior.RestSleep||d.Behavior==StatusBehavior.Focus)&&source!=target)throw new ArgumentException("Rest sleep and focus are self-only.");
            var previous=entries;entries=entries.Select(e=>e.Copy()).ToList();
            var e=entries.FirstOrDefault(x=>x.Target==target&&x.Def.Behavior==d.Behavior&&x.End>battle.CurrentTick&&
                (d.Behavior!=StatusBehavior.Untargetable&&d.Behavior!=StatusBehavior.Blind||x.Source==source&&x.Def.StackGroup==d.StackGroup));
            bool created=e==null;
            if(!created&&d.Behavior==StatusBehavior.SwordDance){e.Start=battle.CurrentTick;e.End=end;e.Extensions=0;}
            if(e==null){e=new Entry{Id=checked(nextId+1),Def=d,Source=source,Target=target,Start=battle.CurrentTick};entries.Add(e);}
            e.Def=d;e.Source=source;e.End=Math.Max(e.End,end);
            if(created&&d.Behavior==StatusBehavior.SwordDance)e.Pulses=Unit(target).SwordDanceHits;
            if(!created&&d.Behavior==StatusBehavior.Poison)e.Count=Math.Min(3,e.Count+1);
            if(created||d.Behavior==StatusBehavior.RestSleep) {
                e.Start=battle.CurrentTick;e.Pulses=d.Behavior==StatusBehavior.SwordDance?Unit(target).SwordDanceHits:0;e.Next=NextPulse(e);
                if(d.Behavior==StatusBehavior.RestSleep)e.RestAmount=Unit(target).Stats.MaxHP*d.RecoveryFraction/4;
            }
            try{Resolve(Unit(target));ResolveDamageModifiers(Unit(target));}catch{entries=previous;throw;}
            if(created)nextId=e.Id;
            var snapshot=e.Snapshot();events.Add(new StatusEvent(snapshot,battle.CurrentTick,created?StatusEventKind.Applied:
                d.Behavior==StatusBehavior.Poison?StatusEventKind.Stacked:StatusEventKind.Refreshed));
            NotifyTargetability(Unit(target));return snapshot;
        }
        private long NextPulse(Entry e)
        {
            if(e.Def.Behavior==StatusBehavior.RestSleep)return checked(e.Start+(long)Math.Ceiling((e.End-e.Start)*(e.Pulses+1)/4.0));
            double interval=e.Def.Behavior==StatusBehavior.Confusion?.5:1;
            return checked(e.Start+BattleSimulation.ToTicks((e.Pulses+1)*interval,battle.TickRate));
        }
        internal void Advance()
        {
            foreach(var e in entries.OrderBy(e=>e.Id).ToArray()) {
                if(!(e.Def.CrowdControl==CrowdControlKind.Root&&e.Def.EffectValue>0)&&e.Def.Behavior!=StatusBehavior.Poison&&e.Def.Behavior!=StatusBehavior.Burn&&
                    e.Def.Behavior!=StatusBehavior.RestSleep&&e.Def.Behavior!=StatusBehavior.Confusion)continue;
                while(entries.Contains(e)&&e.Next<=battle.CurrentTick&&e.Next<=e.End) {
                    var target=Unit(e.Target);if(target.CurrentHP<=0||!target.IsOnBoard)break;
                    e.Pulses++;e.Next=NextPulse(e);
                    if(e.Def.CrowdControl==CrowdControlKind.Root&&e.Def.EffectValue>0)
                        new DamageProcessor().Apply(battle,new DamageRequest(e.Source,e.Target,e.RestAmount,DamageType.Physical,origin:DamageOrigin.Periodic,sourceSnapshot:e.Offense));
                    else if(e.Def.Behavior==StatusBehavior.Poison||e.Def.Behavior==StatusBehavior.Burn)
                        new DamageProcessor().Apply(battle,new DamageRequest(e.Source,e.Target,target.Stats.MaxHP*.025f*e.Count,
                            DamageType.True,origin:DamageOrigin.Debuff));
                    else if(e.Def.Behavior==StatusBehavior.RestSleep) {
                        if(e.Pulses<=4)battle.Healing.Apply(target,e.RestAmount);
                    } else if(!BlocksActions(target)&&!Has(target,CrowdControlKind.Stun)&&!battle.Movement.IsTravelling(target)) {
                        var empty=battle.Board.EmptyNeighbors(target.Position).Where(p=>!battle.Movement.Reservations.TryGetOwner(p,out var owner)||owner==target.UnitInstanceId).ToArray();
                        if(empty.Length>0)battle.Movement.Confuse(target,empty[battle.RngStreams.Status.NextInt(empty.Length)],e.Source);
                    }
                    events.Add(new StatusEvent(e.Snapshot(),battle.CurrentTick,StatusEventKind.Pulse));
                }
            }
            Cleanup();
        }
        internal void OnDamage(DamageResult result)
        {
            if(result.Missed||result.Request.Origin!=DamageOrigin.Direct)return;
            var target=Unit(result.Request.TargetId);
            foreach(var e in Live(target).Where(e=>e.Def.Behavior==StatusBehavior.Sleep).ToArray()) {
                if(!result.Request.IsBasicAttack)continue;
                e.End=Math.Max(battle.CurrentTick,e.End-BattleSimulation.ToTicks(.5,battle.TickRate));
                events.Add(new StatusEvent(e.Snapshot(),battle.CurrentTick,StatusEventKind.Shortened));
                if(e.End<=battle.CurrentTick)Remove(e.Id);
            }
            var frozen=Live(target).Where(e=>e.Def.Behavior==StatusBehavior.Freeze).ToArray();
            foreach(var e in frozen)Remove(e.Id);
            if(frozen.Length>0&&target.CurrentHP>0&&target.IsOnBoard&&result.PostMitigationDamage>0)
                new DamageProcessor().Apply(battle,new DamageRequest(result.Request.SourceId,target.UnitInstanceId,
                    result.PostMitigationDamage*.25f,DamageType.True,origin:DamageOrigin.FreezeBreak));
            // Only a surviving direct hit that actually removed HP can trigger focus.
            // In particular, shields, poison/burn, reflection and HP costs do not qualify.
            var focus=Live(target).FirstOrDefault(e=>e.Def.Behavior==StatusBehavior.Focus);
            if(focus!=null&&result.AppliedDamage>0&&target.CurrentHP>0&&target.IsOnBoard) {
                battle.Movement.TryStart(target,null,new SkillMovementDefinition(SkillMovementKind.TeleportSafe),()=>{
                    Remove(focus.Id);
                    battle.Energy.Refund(target,target.Stats.MaxEnergy*focus.Def.EnergyRefundFraction);
                });
            }
        }
        private IEnumerable<Entry> StatEntries(UnitCombatState unit)
        {
            var all=Live(unit).Where(e=>e.Def.CrowdControl==CrowdControlKind.None).OrderBy(e=>e.Def.Id,StringComparer.Ordinal)
                .ThenBy(e=>e.Source,StringComparer.Ordinal).ThenBy(e=>e.Id).ToArray();
            foreach(var e in all.Where(e=>e.Def.StackPolicy!=StackPolicy.Strongest||e.Def.Behavior!=StatusBehavior.None))yield return e;
            foreach(var group in all.Where(e=>e.Def.StackPolicy==StackPolicy.Strongest&&e.Def.Behavior==StatusBehavior.None).GroupBy(e=>
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
            bool extra=Live(u).Any(e=>e.Def.Modifiers.Any(m=>(int)m.Stat>=(int)CombatStat.Accuracy));
            return new EffectiveCombatStats(Store(Calculate(u,CombatStat.Attack,s.Attack)*power),
                Store(Calculate(u,CombatStat.SpellPower,s.SpellPower)*power),Store(Calculate(u,CombatStat.Armor,s.Armor),-float.MaxValue),
                Store(Calculate(u,CombatStat.MagicResistance,s.MagicResistance),-float.MaxValue),
                Store(Calculate(u,CombatStat.AttackSpeed,s.AttackSpeed)*speed*(HasBehavior(u,StatusBehavior.Paralysis)?.5:1),.1,5),
                Store(Calculate(u,CombatStat.MoveSpeed,s.MoveSpeed)*speed*(1-slow),.1),
                extra?Store(Calculate(u,CombatStat.Accuracy,1),0,1):1,extra?Store(Calculate(u,CombatStat.Evasion,0),0,1):0,
                extra?Store(Calculate(u,CombatStat.CritChance,s.CritChance),0,1):s.CritChance,extra?Store(Calculate(u,CombatStat.EnergyGain,1)):1,
                extra?Store(Calculate(u,CombatStat.ArmorPenetration,0),0,1):0);
        }
        public DamageModifiers ResolveDamageModifiers(UnitCombatState u)=>new DamageModifiers(u.DamageModifiers.BonusAttackDamage,
            Store(Calculate(u,CombatStat.DamageAmplification,u.DamageModifiers.Amplification)),
            Store(Calculate(u,CombatStat.DamageReduction,u.DamageModifiers.Reduction)+Live(u).Where(e=>e.Def.Behavior==StatusBehavior.RestSleep).Select(e=>(double)e.Def.RestReduction).DefaultIfEmpty(0).Max(),0,1));
    }
}
