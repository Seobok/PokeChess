using System;
using System.Linq;
using PokeChess.Core.Board;

namespace PokeChess.Core.Battle
{
    public enum DamageType { Physical, Magic, True }
    public readonly struct DamageRequest
    {
        public string SourceId { get; }
        public string TargetId { get; }
        public float RawDamage { get; }
        public DamageType Type { get; }
        public DamageRequest(string sourceId, string targetId, float rawDamage, DamageType type)
        {
            if(string.IsNullOrWhiteSpace(sourceId)||string.IsNullOrWhiteSpace(targetId))
                throw new ArgumentException("Damage requires source and target IDs.");
            if(float.IsNaN(rawDamage)||float.IsInfinity(rawDamage)||rawDamage<0)
                throw new ArgumentOutOfRangeException(nameof(rawDamage));
            SourceId=sourceId; TargetId=targetId; RawDamage=rawDamage; Type=type;
        }
    }
    public readonly struct DamageResult
    {
        public long Tick { get; }
        public DamageRequest Request { get; }
        public float AppliedDamage { get; }
        public float RemainingHP { get; }
        public bool Killed { get; }
        public DamageResult(long tick, DamageRequest request, float appliedDamage, float remainingHP, bool killed)
        { Tick=tick; Request=request; AppliedDamage=appliedDamage; RemainingHP=remainingHP; Killed=killed; }
    }
    public interface IDamageProcessor
    {
        DamageResult Apply(BattleState battle, DamageRequest request);
    }
    // WBS 1.8 provisional raw HP damage. Armor/shield/minimum damage belong to 1.10.
    public sealed class RawDamageProcessor : IDamageProcessor
    {
        public DamageResult Apply(BattleState battle, DamageRequest request)
        {
            if(battle==null)throw new ArgumentNullException(nameof(battle));
            var source=battle.Units.SingleOrDefault(u=>u.UnitInstanceId==request.SourceId);
            var target=battle.Units.SingleOrDefault(u=>u.UnitInstanceId==request.TargetId);
            if(source==null||target==null||source.TeamId==target.TeamId||!target.IsTargetable)throw new InvalidOperationException("Invalid damage participants.");
            float applied=Math.Min(target.CurrentHP,request.RawDamage);
            target.SetVitals(target.CurrentHP-applied,target.CurrentEnergy);
            if(!target.IsAlive)battle.TryRemoveUnit(target.UnitInstanceId);
            var result=new DamageResult(battle.CurrentTick,request,applied,target.CurrentHP,!target.IsAlive);
            battle.RecordDamage(result);
            return result;
        }
    }
    public sealed class MeleeAttackResolver
    {
        private readonly IDamageProcessor damage;
        public MeleeAttackResolver(IDamageProcessor damage=null) { this.damage=damage??new RawDamageProcessor(); }
        public bool TryResolve(BattleState battle, UnitCombatState source, UnitCombatState target,
            out DamageResult result)
        {
            if(battle==null)throw new ArgumentNullException(nameof(battle));
            result=default;
            if(source==null||target==null||!battle.Units.Contains(source)||!battle.Units.Contains(target)||
                source.TeamId==target.TeamId||!source.IsAlive||!source.IsOnBoard||!target.IsTargetable||
                source.Stats.AttackDelivery!=PokeChess.Core.Pokemon.AttackDeliveryType.Melee||!HexCoordinates.IsInRange(source.Position,target.Position,source.Stats.AttackRange))return false;
            result=damage.Apply(battle,new DamageRequest(source.UnitInstanceId,target.UnitInstanceId,
                source.Stats.Attack,DamageType.Physical));
            return true;
        }
    }
    // Explicit melee-only policy retained for callers that need it.
    public class MeleeCombatBehaviorPolicy : CombatBehaviorPolicy
    {
        private readonly MeleeAttackResolver resolver;
        public MeleeCombatBehaviorPolicy(IDamageProcessor damage=null) { resolver=new MeleeAttackResolver(damage); }
        public override bool CanAttack(BattleState battle, UnitCombatState unit) => unit.Stats.AttackDelivery==PokeChess.Core.Pokemon.AttackDeliveryType.Melee;
        public override void OnAttackTiming(BattleState battle, UnitCombatState unit, UnitCombatState target)
        { resolver.TryResolve(battle,unit,target,out _); }
    }
}
