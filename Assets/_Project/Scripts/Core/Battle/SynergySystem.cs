using System;
using System.Collections.Generic;
using System.Linq;
using PokeChess.Core.Board;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;
namespace PokeChess.Core.Battle
{
    public sealed class SynergyRules
    {
        public static SynergyRules Disabled { get; } = new SynergyRules(false);
        public bool Enabled { get; }
        // Unspecified origin values in v0.9 are isolated prototype tuning.
        public SynergyOriginTuning Origins { get; }
        public float FlameDamage => Origins.FlameDamage;
        public float StormDamage => Origins.StormDamage;
        public float TorrentShield => Origins.TorrentShield;
        public SynergyRules(bool enabled = true,SynergyOriginTuning origins=null) { Enabled = enabled; Origins=origins??new SynergyOriginTuning(); }
    }
    public readonly struct SynergyCount
    {
        public string TraitId { get; }
        public int Count { get; }
        public int Tier { get { int count=Count; return SynergyTraits.Thresholds(TraitId).Where(t => count >= t).DefaultIfEmpty(0).Max(); } }
        public int NextThreshold { get { int count=Count; return SynergyTraits.Thresholds(TraitId).FirstOrDefault(t => t > count); } }
        public int Needed => NextThreshold == 0 ? 0 : NextThreshold - Count;
        public SynergyCount(string traitId, int count) { TraitId = traitId; Count = count; }
    }
    public readonly struct SynergyEvent
    {
        public string TraitId { get; }
        public string SourceId { get; }
        public string TargetId { get; }
        public string Detail { get; }
        public float Value { get; }
        public long Tick { get; }
        public BoardPosition? Cell { get; }
        public BoardPosition? Destination { get; }
        public SynergyEvent(string trait, string source, string target, string detail, float value, long tick, BoardPosition? cell = null, BoardPosition? destination = null)
        { TraitId=trait; SourceId=source; TargetId=target; Detail=detail; Value=value; Tick=tick; Cell=cell; Destination=destination; }
    }
    public sealed partial class SynergySystem
    {
        private sealed class Runtime
        {
            public int Attacks, Casts, Marks;
            public string MarkTarget;
            public long LastHit=-1, LastCast=-1, SupportReady, ManaReady, ChaseReady, ChaseEnd;
            public bool FirstCast, FirstKill, ShieldUsed, SurvivalUsed, FirstHit, Suppressed, AssassinAttack;
            public float GuardReduction, GuardFirst;
            public bool Guarded;
            public long ChaseAttackEnd;
            public readonly HashSet<string> MarkedCasts=new HashSet<string>();
            public readonly Dictionary<string,long> MarkProcs = new Dictionary<string,long>();
            public readonly HashSet<string> Contributors = new HashSet<string>();
        }
        private readonly BattleState battle;
        private readonly SynergyRules rules;
        private readonly Dictionary<string,Runtime> runtime;
        private readonly Dictionary<int,IReadOnlyList<SynergyCount>> teams;
        private readonly List<SynergyEvent> events=new List<SynergyEvent>();
        private bool initialized;
        private readonly Dictionary<long,bool> skillCriticals=new Dictionary<long,bool>();
        internal bool ResolveCritical(DamageRequest request,bool rolled) { long cast=request.SkillPolicy?.CastId??0;if(cast==0||!SkillCanCrit(battle.Units.First(u=>u.UnitInstanceId==request.SourceId),request))return rolled;if(skillCriticals.TryGetValue(cast,out bool cached))return cached;skillCriticals[cast]=rolled;return rolled; }
        internal bool? CachedCritical(DamageRequest request) {long cast=request.SkillPolicy?.CastId??0;return cast!=0&&skillCriticals.TryGetValue(cast,out bool cached)?cached:(bool?)null;}
        public IReadOnlyList<SynergyEvent> EventsThisTick => events.AsReadOnly();
        public static IReadOnlyList<SynergyCount> CountAssignments(IEnumerable<KeyValuePair<string,IReadOnlyList<string>>> assignments) { var rows=assignments.ToArray();return Array.AsReadOnly(SynergyTraits.All.Select(t=>new SynergyCount(t,rows.Where(r=>r.Value.Contains(t)).Select(r=>SynergyTraits.Family(r.Key)).Distinct(StringComparer.Ordinal).Count())).ToArray()); }
        public static IReadOnlyList<SynergyCount> Count(IEnumerable<PokeChess.Core.Pokemon.PokemonDefinition> definitions)
        {
            var units=definitions.ToArray();
            return Array.AsReadOnly(SynergyTraits.All.Select(t => new SynergyCount(t,units.Where(d=>d.TraitIds.Contains(t)).Select(d=>SynergyTraits.Family(d.Id)).Distinct(StringComparer.Ordinal).Count())).ToArray());
        }
        internal SynergySystem(BattleState battle,SynergyRules rules)
        {
            this.battle=battle;this.rules=rules??new SynergyRules();
            runtime=battle.Units.ToDictionary(u=>u.UnitInstanceId,_=>new Runtime(),StringComparer.Ordinal);
            teams=new Dictionary<int,IReadOnlyList<SynergyCount>>();
            foreach(int team in new[]{1,2}) teams[team]=Array.AsReadOnly(SynergyTraits.All.Select(t=>new SynergyCount(t,battle.Units.Where(u=>u.TeamId==team&&u.IsOnBoard&&u.TraitIds.Contains(t)).Select(u=>SynergyTraits.Family(u.DefinitionId)).Distinct(StringComparer.Ordinal).Count())).ToArray());
        }
        public IReadOnlyList<SynergyCount> TeamCounts(int team)=>teams[team];
        public int TeamTier(int team,string trait)=>rules.Enabled?teams[team].First(c=>c.TraitId==trait).Tier:0;
        public int Tier(UnitCombatState unit,string trait)=>unit.TraitIds.Contains(trait)?TeamTier(unit.TeamId,trait):0;
        private Runtime R(UnitCombatState u)=>runtime[u.UnitInstanceId];
        private IEnumerable<UnitCombatState> Allies(UnitCombatState u)=>battle.Units.Where(x=>x.TeamId==u.TeamId&&x.CurrentHP>0&&x.IsOnBoard);
        private IEnumerable<UnitCombatState> Enemies(UnitCombatState u)=>battle.Units.Where(x=>x.TeamId!=u.TeamId&&x.IsTargetable);
        private long T(float seconds)=>BattleSimulation.ToTicks(seconds,battle.TickRate);
        private void Record(string trait,UnitCombatState source,UnitCombatState target,string detail,float value)=>events.Add(new SynergyEvent(trait,source.UnitInstanceId,target.UnitInstanceId,detail,value,battle.CurrentTick));
        private void Buff(string id,UnitCombatState source,UnitCombatState target,CombatStat stat,float value,float duration=60,StatModifierType type=StatModifierType.Flat)
        {
            // Replace the shared category so updated stack values take effect immediately.
            foreach(var old in battle.StatusEffects.Active.Where(e=>e.TargetId==target.UnitInstanceId&&e.Definition.Id==id).ToArray())battle.StatusEffects.Remove(old.InstanceId);
            battle.StatusEffects.Apply(new StatusEffectDefinition(id,duration,source.TeamId==target.TeamId?StatusTargetTeam.Ally:StatusTargetTeam.Enemy,sourceRule:SourceStackRule.Shared,modifiers:new[]{new StatModifierDefinition(stat,type,value)}),source.UnitInstanceId,target.UnitInstanceId);
        }
        private void Shield(string trait,UnitCombatState source,UnitCombatState target,float value,float duration)
        {
            value*=battle.IsOvertime?.34f:1;
            // One shared trait shield per target; weaker applications never replace stronger remaining shields.
            var old=battle.Shields.Active.Where(s=>s.TargetId==target.UnitInstanceId&&s.Group==trait).Select(s=>s.Remaining).DefaultIfEmpty(0).Max();
            battle.Shields.Add(target,"synergy:"+trait,trait,Math.Max(old,value),duration);
            Record(trait,source,target,"SHIELD",value);
        }
        private static IEnumerable<UnitCombatState> GuardianTargets(UnitCombatState guardian,IEnumerable<UnitCombatState> allies,int tier)
            =>allies.Where(a=>!a.TraitIds.Contains(SynergyTraits.Guardian)&&HexCoordinates.Distance(guardian.Position,a.Position)<=1)
                .OrderByDescending(a=>a.ItemInstanceIds.Count).ThenByDescending(a=>a.Stats.Attack+a.Stats.SpellPower)
                .ThenBy(a=>SynergyTraits.Dex(a.DefinitionId)).ThenBy(a=>a.UnitInstanceId,StringComparer.Ordinal).Take(tier>=4?2:1);

        public static IEnumerable<(string guardian,string target)> PreviewGuardianLinks(IEnumerable<UnitInstance> roster,PokemonCatalog catalog)
        {
            var board=roster.Where(u=>u.Placement.Kind==PlacementKind.Board).ToArray();
            int tier=SynergyPreparation.CountUnits(board,catalog).First(c=>c.TraitId==SynergyTraits.Guardian).Tier;
            if(tier==0)yield break;
            var units=board.Select(u=>{
                var definition=catalog.Get(u.DefinitionId);
                var stats=ItemCombatSystem.ApplyStats(u,RankRules.Default.Apply(definition,u.Rank));
                return new UnitCombatState(u,stats,1,u.Placement.Position.Value,definition.SkillId,SynergyPreparation.Traits(u,catalog));
            }).ToArray();
            foreach(var guardian in units.Where(u=>u.TraitIds.Contains(SynergyTraits.Guardian)))
                foreach(var target in GuardianTargets(guardian,units,tier))
                    yield return (guardian.UnitInstanceId,target.UnitInstanceId);
        }

        internal void Initialize()
        {
            if(initialized)return;initialized=true;
            foreach(var u in battle.Units.OrderBy(u=>u.UnitInstanceId,StringComparer.Ordinal))
            {
                int bruiser=TeamTier(u.TeamId,SynergyTraits.Bruiser);
                if(bruiser>0){float bonus=bruiser==6?.08f:bruiser==4?.06f:.04f;if(Tier(u,SynergyTraits.Bruiser)>0)bonus+=bruiser==6?.38f:bruiser==4?.22f:.10f;u.ScaleMaxHP(bonus);}
                int guardian=Tier(u,SynergyTraits.Guardian);
                if(guardian>0){float armor=guardian==5?28:guardian==4?20:10;Buff("synergy-guardian-armor",u,u,CombatStat.Armor,armor);Buff("synergy-guardian-mr",u,u,CombatStat.MagicResistance,armor);Record(SynergyTraits.Guardian,u,u,"GUARD_START",armor);}
                int sorcerer=Tier(u,SynergyTraits.Sorcerer);
                if(sorcerer>0)Buff("synergy-sorcerer-base",u,u,CombatStat.SpellPower,sorcerer==6?36:sorcerer==4?24:12);
                int assassin=Tier(u,SynergyTraits.Assassin);
                if(assassin>0)Buff("synergy-assassin-crit",u,u,CombatStat.CritChance,assassin==3?.20f:.12f);
            }
            foreach(var u in battle.Units.Where(u=>Tier(u,SynergyTraits.Guardian)>0).OrderBy(u=>u.UnitInstanceId,StringComparer.Ordinal))
            {
                int tier=Tier(u,SynergyTraits.Guardian);
                foreach(var ally in GuardianTargets(u,Allies(u),tier))
                {var r=R(ally);r.Guarded=true;r.GuardReduction=tier==5?.10f:tier==4?.07f:.04f;r.GuardFirst=tier==5?.20f:tier==4?.15f:0;Record(SynergyTraits.Guardian,u,ally,"GUARD",tier);}
            }
            InitializeOrigins();
        }
        internal void BeginTick()=>events.Clear();
        internal void Advance()
        {
            if(!rules.Enabled)return;
            if(battle.CurrentTick>0&&battle.CurrentTick%T(3)==0)
            foreach(var u in battle.Units.Where(u=>u.CurrentHP>0&&u.IsOnBoard))
            {int tier=TeamTier(u.TeamId,SynergyTraits.Invoker);if(tier>0)Mana(u,(tier==4?6:3)+(Tier(u,SynergyTraits.Invoker)>0?(tier==4?4:3):0),SynergyTraits.Invoker);}
            AdvanceOrigins();
        }
        private void Mana(UnitCombatState u,float amount,string trait)
        {float before=u.CurrentEnergy;u.SetEnergy(Math.Min(u.Stats.MaxEnergy,before+amount*u.EffectiveStats.EnergyGain));Record(trait,u,u,"MANA",u.CurrentEnergy-before);}
        public int DuelistStacks(UnitCombatState u)=>R(u).Attacks;
        public int TrackerStacks(UnitCombatState u)=>R(u).LastHit>=0&&battle.CurrentTick-R(u).LastHit<T(3)?R(u).Marks:0;
        public string TrackerTarget(UnitCombatState u)=>TrackerStacks(u)>0?R(u).MarkTarget:null;
        public bool IsGuarded(UnitCombatState u)=>R(u).Guarded;
        internal float AttackBonus(UnitCombatState u)=>u.EffectiveStats.Attack*((R(u).ChaseAttackEnd>battle.CurrentTick?.25f:0)+(R(u).AssassinAttack?.20f:0));
        internal bool SkillCanCrit(UnitCombatState u,DamageRequest req)=>Tier(u,SynergyTraits.Assassin)>0&&!req.IsBasicAttack&&req.Origin==DamageOrigin.Direct&&req.SkillPolicy!=null;
        internal float CriticalBonus(UnitCombatState u)=>Tier(u,SynergyTraits.Assassin)==3?.25f:Tier(u,SynergyTraits.Assassin)>0?.15f:0;
        internal float IncomingMultiplier(UnitCombatState u,DamageRequest req)
        {
            var r=R(u);float multiplier=1;
            if(r.Guarded)multiplier*=(1-r.GuardReduction)*(r.FirstHit?1:1-r.GuardFirst);
            if(r.ChaseEnd>battle.CurrentTick)multiplier*=.85f;
            return multiplier*OriginIncoming(u,req);
        }
        internal float OutgoingMultiplier(UnitCombatState u,UnitCombatState target)
        {int tier=Tier(u,SynergyTraits.Tracker);var r=R(u);float value=tier>0&&r.MarkTarget==target.UnitInstanceId&&TrackerStacks(u)>=2?(tier==4?.25f:tier==3?.18f:.10f):0;return (1+value)*DisruptorVulnerability(target);}
        private float DisruptorVulnerability(UnitCombatState u)=>battle.StatusEffects.Active.Any(e=>e.TargetId==u.UnitInstanceId&&e.Definition.Id=="synergy-disruptor-expose")?1+(TeamTier(u.TeamId==1?2:1,SynergyTraits.Disruptor)==6?.12f:.08f):1;
        internal float PoisonMultiplier(UnitCombatState source)=>Tier(source,SynergyTraits.Venom)>=6&&battle.VenomSamples.TryGetValue(source.TeamId,out var samples)&&samples>=25?rules.Origins.VenomDamageMultiplier:1;
        internal void OnSkillEffect(UnitCombatState u) {
            OriginSkill(u);
            foreach(var poison in battle.StatusEffects.Active.Where(e=>e.TargetId==u.UnitInstanceId&&e.Definition.Behavior==StatusBehavior.Poison).Take(1)) {
                var source=battle.Units.First(x=>x.UnitInstanceId==poison.SourceId);
                if(Tier(source,SynergyTraits.Venom)>=6&&battle.VenomSamples.TryGetValue(source.TeamId,out var samples)&&samples>=45)Deal(source,u,rules.Origins.VenomReactionDamage);
            }
        }
        internal void OnSkillCompleted(UnitCombatState u)
        {
            if(u.CurrentHP<=0||!u.IsOnBoard)return;
            var r=R(u);int tier=Tier(u,SynergyTraits.Sorcerer);
            if(tier>0){r.Casts=Math.Min(r.Casts+1,tier==6?6:tier==4?5:4);Buff("synergy-sorcerer-stack",u,u,CombatStat.SpellPower,r.Casts*(tier==6?8:tier==4?5:3));Record(SynergyTraits.Sorcerer,u,u,"AP",r.Casts);}
            if(!r.FirstCast&&Tier(u,SynergyTraits.Invoker)==4)Mana(u,10,SynergyTraits.Invoker);r.FirstCast=true;
            tier=Tier(u,SynergyTraits.Supporter);
            if(tier>0&&battle.CurrentTick>=r.SupportReady)
            {
                var ally=Allies(u).OrderBy(a=>a.CurrentHP/a.Stats.MaxHP).ThenBy(a=>a.CurrentHP).ThenBy(a=>SynergyTraits.Dex(a.DefinitionId)).ThenBy(a=>a.UnitInstanceId,StringComparer.Ordinal).FirstOrDefault();
                if(ally!=null){r.SupportReady=battle.CurrentTick+T(4);Shield(SynergyTraits.Supporter,u,ally,tier==4?140:80,4);if(tier==4&&battle.CurrentTick>=R(ally).ManaReady){R(ally).ManaReady=battle.CurrentTick+T(6);Mana(ally,8,SynergyTraits.Supporter);}}
            }
            UniqueSkillCompleted(u);
        }
        internal void OnDebuffApplied(StatusEffectDefinition d,UnitCombatState source,UnitCombatState target)
        {
            if(d.Behavior==StatusBehavior.Poison&&PoisonMultiplier(source)>1)Buff("synergy-venom-healing",source,target,CombatStat.HealingReceived,-rules.Origins.VenomHealingReduction,d.Duration,StatModifierType.Percentage);
            if(d.Id.StartsWith("synergy-",StringComparison.Ordinal)||source.TeamId==target.TeamId||Tier(source,SynergyTraits.Disruptor)==0)return;
            bool valid=d.CrowdControl!=CrowdControlKind.None||new[]{StatusBehavior.Sleep,StatusBehavior.Freeze,StatusBehavior.Paralysis,StatusBehavior.Confusion,StatusBehavior.Blind,StatusBehavior.Bleed}.Contains(d.Behavior)||d.Modifiers.Any(m=>m.Value<0);
            if(!valid)return;int tier=Tier(source,SynergyTraits.Disruptor);float penalty=tier==6?-.18f:tier==4?-.12f:-.08f;
            Buff("synergy-disruptor-at",source,target,CombatStat.Attack,penalty,3,StatModifierType.Percentage);Buff("synergy-disruptor-ap",source,target,CombatStat.SpellPower,penalty,3,StatModifierType.Percentage);
            if(tier>=4)Buff("synergy-disruptor-expose",source,target,CombatStat.Armor,0,3);
            if(tier==6&&!R(target).Suppressed){R(target).Suppressed=true;Buff("synergy-disruptor-mana",source,target,CombatStat.EnergyGain,-.25f,3,StatModifierType.Percentage);}
            Record(SynergyTraits.Disruptor,source,target,"WEAKEN",-penalty);
        }
        internal void OnDamageResolved(DamageResult result)
        {
            var source=battle.Units.Single(u=>u.UnitInstanceId==result.Request.SourceId);var target=battle.Units.Single(u=>u.UnitInstanceId==result.Request.TargetId);
            if(result.Missed||result.AppliedDamage+result.ShieldAbsorbed<=0)return;
            var r=R(source);var tr=R(target);
            if(!tr.FirstHit && tr.GuardFirst>0)Record(SynergyTraits.Guardian,source,target,"FIRST_HIT",tr.GuardFirst);
            tr.FirstHit=true;tr.Contributors.Add(source.UnitInstanceId);
            if(result.Request.IsBasicAttack)
            {
                r.ChaseAttackEnd=0;r.AssassinAttack=false;int tier=Tier(source,SynergyTraits.Duelist);
                if(tier>0&&source.CurrentHP>0&&source.IsOnBoard){r.Attacks=Math.Min(6,r.Attacks+1);Buff("synergy-duelist",source,source,CombatStat.AttackSpeed,r.Attacks*(tier==6?.08f:tier==4?.06f:.04f),type:StatModifierType.Percentage);Record(SynergyTraits.Duelist,source,source,"HASTE",r.Attacks);}
            }
            bool direct=result.Request.Origin==DamageOrigin.Direct;
            if(Tier(source,SynergyTraits.Tracker)>0&&direct&&(result.Request.IsBasicAttack||result.Request.SkillPolicy!=null))
            {
                if(r.MarkTarget!=target.UnitInstanceId||battle.CurrentTick-r.LastHit>=T(3)){r.Marks=0;r.LastCast=-1;}
                r.MarkTarget=target.UnitInstanceId;r.LastHit=battle.CurrentTick;
                long cast=result.Request.SkillPolicy?.CastId??-1;
                if(result.Request.IsBasicAttack||r.MarkedCasts.Add(cast+"/"+target.UnitInstanceId)){r.LastCast=cast;int before=r.Marks;r.Marks=Math.Min(3,r.Marks+1);Record(SynergyTraits.Tracker,source,target,"TRACK",r.Marks);
                    int tier=Tier(source,SynergyTraits.Tracker);
                    if(before<3&&r.Marks==3&&tier>=3&&(!r.MarkProcs.TryGetValue(target.UnitInstanceId,out long ready)||battle.CurrentTick>=ready))
                    {r.MarkProcs[target.UnitInstanceId]=battle.CurrentTick+T(5);if(target.IsTargetable)Deal(source,target,tier==4?110:70,DamageOrigin.Synergy);}}
            }
            OriginDamage(source,target,result);
            if(target.CurrentHP>0&&target.IsOnBoard)
            {
                if(tr.Guarded&&TeamTier(target.TeamId,SynergyTraits.Guardian)==5&&!tr.ShieldUsed&&target.CurrentHP<=target.Stats.MaxHP*.5f){tr.ShieldUsed=true;Shield(SynergyTraits.Guardian,target,target,target.Stats.MaxHP*.12f,5);}
                if(Tier(target,SynergyTraits.Bruiser)==6&&!tr.SurvivalUsed&&target.CurrentHP<=target.Stats.MaxHP*.4f){tr.SurvivalUsed=true;tr.ChaseEnd=battle.CurrentTick+T(4);Record(SynergyTraits.Bruiser,target,target,"SURVIVE",.15f);}
            }
            if(result.Killed){if(R(target).Contributors.Any(id=>battle.Units.Any(u=>u.UnitInstanceId==id&&u.IsSynergySummon))){int team=target.TeamId==1?2:1;battle.NatureEarned.TryGetValue(team,out int xp);battle.NatureEarned[team]=xp+1;}Takedown(target);}
        }
        private void Takedown(UnitCombatState target)
        {
            foreach(var u in battle.Units.Where(u=>R(target).Contributors.Contains(u.UnitInstanceId)&&u.CurrentHP>0&&u.IsOnBoard).OrderBy(u=>u.UnitInstanceId,StringComparer.Ordinal))
            {
                var r=R(u);var next=Enemies(u).OrderBy(e=>HexCoordinates.Distance(u.Position,e.Position)).ThenBy(e=>e.UnitInstanceId,StringComparer.Ordinal).FirstOrDefault();
                if(Tier(u,SynergyTraits.Duelist)==6&&battle.CurrentTick>=r.ChaseReady){r.ChaseReady=battle.CurrentTick+T(5);r.ChaseAttackEnd=battle.CurrentTick+T(2);u.CurrentTargetId=next?.UnitInstanceId;Buff("synergy-chase",u,u,CombatStat.MoveSpeed,.4f,2,StatModifierType.Percentage);}
                if(Tier(u,SynergyTraits.Assassin)==3&&!r.FirstKill){r.FirstKill=true;r.AssassinAttack=true;battle.StatusEffects.Apply(new StatusEffectDefinition("synergy-stealth",1.2f,StatusTargetTeam.Ally,behavior:StatusBehavior.Untargetable),u.UnitInstanceId,u.UnitInstanceId);Record(SynergyTraits.Assassin,u,u,"STEALTH",1.2f);}
                if(Tier(u,SynergyTraits.Tracker)==4&&r.MarkTarget==target.UnitInstanceId){r.MarkTarget=next?.UnitInstanceId;r.Marks=next==null?0:1;r.LastHit=battle.CurrentTick;r.LastCast=-1;}
            }
        }
        private void Deal(UnitCombatState source,UnitCombatState target,float amount,DamageOrigin origin=DamageOrigin.Synergy)
        {if(target.CurrentHP>0&&target.IsOnBoard)new DamageProcessor().Apply(battle,new DamageRequest(source.UnitInstanceId,target.UnitInstanceId,amount,DamageType.Magic,origin:origin,isAreaDamage:true));}
    }
}
