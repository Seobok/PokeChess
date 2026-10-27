using System;
using System.Collections.Generic;
using System.Linq;
using PokeChess.Core.Board;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Battle
{
    public readonly struct ItemCombatEvent
    {
        public long Tick { get; }
        public string ItemId { get; }
        public string SourceId { get; }
        public string TargetId { get; }
        public float Value { get; }
        internal ItemCombatEvent(long tick,string item,string source,string target,float value)
        { Tick=tick;ItemId=item;SourceId=source;TargetId=target;Value=value; }
    }
    public sealed class ItemCombatSystem
    {
        private readonly BattleState battle;
        private readonly HashSet<string> consumed=new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string,long> protection=new Dictionary<string,long>(StringComparer.Ordinal);
        private readonly Dictionary<string,long> shareReady=new Dictionary<string,long>(StringComparer.Ordinal);
        private readonly List<ItemCombatEvent> events=new List<ItemCombatEvent>();
        public IReadOnlyList<ItemCombatEvent> EventsThisTick=>events.AsReadOnly();
        internal ItemCombatSystem(BattleState battle){this.battle=battle;}
        public static bool Has(UnitCombatState u,string id)=>ItemCatalog.Has(u.ItemInstanceIds,id);
        internal static PokemonStats ApplyStats(UnitInstance unit,PokemonStats s)
        {
            if(!ItemCatalog.Has(unit.ItemInstanceIds,ItemCatalog.ChoiceScarf))return s;
            return new PokemonStats(s.MaxHP,s.Attack,s.SpellPower,Math.Min(5,s.AttackSpeed*1.25f),s.Armor,
                s.MagicResistance,s.AttackRange,s.MoveSpeed,s.MaxEnergy,s.StartingEnergy,s.AttackHitTime,
                s.AttackDelivery,s.ProjectileSpeed,s.CritChance,s.CritMultiplier,s.EnergyLockSeconds);
        }
        internal void BeginTick()
        {
            events.Clear();
            if(battle.CurrentTick==0||battle.CurrentTick%(3L*battle.TickRate)!=0)return;
            foreach(var u in battle.Units.Where(u=>u.CurrentHP>0&&u.IsOnBoard&&Has(u,ItemCatalog.Leftovers)))
                Heal(u,u.Stats.MaxHP*.04f,ItemCatalog.Leftovers);
        }
        internal float Outgoing(UnitCombatState u,DamageRequest request)
        {
            if(request.IsBasicAttack||request.Origin!=DamageOrigin.Direct)return 1;
            return Has(u,ItemCatalog.WiseGlasses)?1.2f:Has(u,ItemCatalog.ChoiceScarf)?.85f:1;
        }
        internal float IncomingReduction(UnitCombatState u,DamageRequest r,float reduction)
            =>r.Type==DamageType.Magic&&Has(u,ItemCatalog.AssaultVest)?1-(1-reduction)*.75f:reduction;
        internal float AdjustHPDamage(UnitCombatState target,float hpDamage)
        {
            if(protection.TryGetValue(target.UnitInstanceId,out var until)&&battle.CurrentTick<until)return 0;
            if(hpDamage>=target.CurrentHP&&target.CurrentHP>0&&Has(target,ItemCatalog.FocusSash)&&consumed.Add(target.UnitInstanceId)){
                protection[target.UnitInstanceId]=battle.CurrentTick+battle.TickRate;
                Emit(ItemCatalog.FocusSash,target,target,Math.Max(0,hpDamage-target.CurrentHP+1));
                return Math.Max(0,target.CurrentHP-1);
            }
            return hpDamage;
        }
        internal void AfterDamage(UnitCombatState source,UnitCombatState target,DamageResult result,DamageProcessor processor)
        {
            if(target.CurrentHP>0&&target.IsOnBoard&&Has(target,ItemCatalog.SitrusBerry)&&
                target.CurrentHP<=target.Stats.MaxHP*.4f&&consumed.Add(target.UnitInstanceId))
                Heal(target,target.Stats.MaxHP*.25f,ItemCatalog.SitrusBerry);
            if(result.Request.Origin!=DamageOrigin.Direct||result.AppliedDamage<=0||!Has(target,ItemCatalog.RockyHelmet)||
                source.CurrentHP<=0||!source.IsOnBoard)return;
            var reflected=processor.Apply(battle,new DamageRequest(target.UnitInstanceId,source.UnitInstanceId,
                result.AppliedDamage*.2f,DamageType.True,origin:DamageOrigin.Reflection));
            Emit(ItemCatalog.RockyHelmet,target,source,reflected.AppliedDamage);
        }
        internal void OnSkillEffect(UnitCombatState caster)
        {
            foreach(var u in battle.Units.Where(u=>u.TeamId==caster.TeamId&&u!=caster&&u.CurrentHP>0&&u.IsOnBoard&&
                Has(u,ItemCatalog.ExpShare)&&HexCoordinates.IsInRange(u.Position,caster.Position,1))){
                if(shareReady.TryGetValue(u.UnitInstanceId,out var until)&&battle.CurrentTick<until)continue;
                if(battle.Energy.Gain(u,10,EnergyChangeReason.Item)){
                    shareReady[u.UnitInstanceId]=battle.CurrentTick+2L*battle.TickRate;
                    Emit(ItemCatalog.ExpShare,caster,u,10);
                }
            }
        }
        private void Heal(UnitCombatState u,float amount,string item)
        {
            float before=u.CurrentHP;battle.Healing.Apply(u,amount,false);
            if(u.CurrentHP>before)Emit(item,u,u,u.CurrentHP-before);
        }
        private void Emit(string item,UnitCombatState source,UnitCombatState target,float value)
            =>events.Add(new ItemCombatEvent(battle.CurrentTick,item,source.UnitInstanceId,target.UnitInstanceId,value));
    }
}
