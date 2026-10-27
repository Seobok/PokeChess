using System;
using System.Collections.Generic;
using System.Linq;
using PokeChess.Core.Board;

namespace PokeChess.Core.Battle
{
    // Per-battle hit/participation bookkeeping. Persistent bounty remainder belongs to the owned unit.
    public sealed class SkillProcSystem
    {
        private readonly BattleState battle;
        private readonly Dictionary<string,HashSet<string>> participants=new Dictionary<string,HashSet<string>>();
        private readonly Dictionary<string,HashSet<string>> bounties=new Dictionary<string,HashSet<string>>();
        private readonly HashSet<string> settled=new HashSet<string>();
        public event Action<UnitCombatState,int> GoldAwarded;
        internal SkillProcSystem(BattleState battle){this.battle=battle;}
        private static void Mark(Dictionary<string,HashSet<string>> map,string victim,string source)
        {if(!map.TryGetValue(victim,out var ids))map[victim]=ids=new HashSet<string>();ids.Add(source);}
        internal void OnDamage(DamageResult result)
        {
            if(result.Missed)return;
            var source=battle.Units.Single(u=>u.UnitInstanceId==result.Request.SourceId);
            var target=battle.Units.Single(u=>u.UnitInstanceId==result.Request.TargetId);
            if(result.AppliedDamage+result.ShieldAbsorbed>0) {
                if(battle.StatusEffects.HasBehavior(source,StatusBehavior.SwordDance))Mark(participants,target.UnitInstanceId,source.UnitInstanceId);
                if(result.Request.SkillPolicy?.Numeric?.Bounty==true)Mark(bounties,target.UnitInstanceId,source.UnitInstanceId);
            }
            if(result.Killed&&settled.Add(target.UnitInstanceId)) {
                if(participants.TryGetValue(target.UnitInstanceId,out var ids))
                    foreach(var id in ids.OrderBy(x=>x,StringComparer.Ordinal))battle.StatusEffects.ExtendSwordDance(battle.Units.Single(u=>u.UnitInstanceId==id));
                if(bounties.TryGetValue(target.UnitInstanceId,out ids))foreach(var id in ids.OrderBy(x=>x,StringComparer.Ordinal)) {
                    var unit=battle.Units.Single(u=>u.UnitInstanceId==id);
                    int gold=unit.PersistentUnit.AddBountyParticipation();if(gold>0)GoldAwarded?.Invoke(unit,gold);
                }
            }
            if(!result.Request.IsBasicAttack||result.PostMitigationDamage<=0||!source.IsAlive||!source.IsOnBoard)return;
            var damage=new DamageProcessor();
            var splash=battle.StatusEffects.AttackSplash(source);
            if(splash>0)foreach(var victim in battle.Units.Where(u=>u!=target&&u.TeamId!=source.TeamId&&u.IsTargetable&&
                HexCoordinates.Distance(target.Position,u.Position)<=1).OrderBy(u=>u.UnitInstanceId,StringComparer.Ordinal).ToArray())
                damage.Apply(battle,new DamageRequest(source.UnitInstanceId,victim.UnitInstanceId,
                    result.Calculation.AfterAmplification*splash,DamageType.Physical,sourceSnapshot:new DamageSourceSnapshot(0,1,source.EffectiveStats.CritChance,source.EffectiveStats.ArmorPenetration)));
            float coefficient=battle.StatusEffects.AdvanceSwordDance(source);
            if(coefficient>0)foreach(var victim in battle.Units.Where(u=>u.TeamId!=source.TeamId&&u.IsTargetable&&
                HexCoordinates.Distance(source.Position,u.Position)<=1).OrderBy(u=>u.UnitInstanceId,StringComparer.Ordinal).ToArray())
                damage.Apply(battle,new DamageRequest(source.UnitInstanceId,victim.UnitInstanceId,source.EffectiveStats.Attack*coefficient,DamageType.Physical));
        }
    }
}
