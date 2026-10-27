using System;
using System.Linq;
using PokeChess.Core.Board;

namespace PokeChess.Core.Battle
{
    public enum DamageType { Physical, Magic, True }
    public enum DamageOrigin { Direct, Periodic, Reflection, Debuff, FreezeBreak, Execution }
    public readonly struct DamageSourceSnapshot
    {
        public float Amplification { get; }
        public float OutgoingMultiplier { get; }
        public float? CritChance { get; }
        public float ArmorPenetration { get; }
        public float? Attack { get; }
        public float? SpellPower { get; }
        public float? MaxHP { get; }
        public DamageSourceSnapshot(float amplification,float outgoingMultiplier,float? critChance=null,float armorPenetration=0,float? attack=null,float? spellPower=null,float? maxHP=null)
        {DamageCalculator.Validate(amplification,true);DamageCalculator.Validate(outgoingMultiplier,true);
         if(critChance.HasValue){DamageCalculator.Validate(critChance.Value,true);if(critChance>1)throw new ArgumentOutOfRangeException();}
         DamageCalculator.Validate(armorPenetration,true);if(armorPenetration>1)throw new ArgumentOutOfRangeException();
         Attack=attack;SpellPower=spellPower;MaxHP=maxHP;CritChance=critChance;ArmorPenetration=armorPenetration;Amplification=amplification;OutgoingMultiplier=outgoingMultiplier;}
    }
    public readonly struct DamageRequest
    {
        public string SourceId { get; }
        public string TargetId { get; }
        public float RawDamage { get; }
        public DamageType Type { get; }
        public bool CanCrit { get; }
        public bool IsBasicAttack { get; }
        public DamageOrigin Origin { get; }
        public float BonusDamage { get; }
        public DamageSourceSnapshot? SourceSnapshot { get; }
        public SkillDamagePolicy SkillPolicy { get; }
        public DamageRequest(string sourceId, string targetId, float rawDamage, DamageType type, bool canCrit=false, float bonusDamage=0, bool isBasicAttack=false, DamageOrigin origin=DamageOrigin.Direct,DamageSourceSnapshot? sourceSnapshot=null,SkillDamagePolicy skillPolicy=null)
        {
            if(string.IsNullOrWhiteSpace(sourceId)||string.IsNullOrWhiteSpace(targetId))
                throw new ArgumentException("Damage requires source and target IDs.");
            if(float.IsNaN(rawDamage)||float.IsInfinity(rawDamage)||rawDamage<0)
                throw new ArgumentOutOfRangeException(nameof(rawDamage));
            if(!Enum.IsDefined(typeof(DamageType),type))throw new ArgumentOutOfRangeException(nameof(type));
            if(float.IsNaN(bonusDamage)||float.IsInfinity(bonusDamage)||bonusDamage<0)
                throw new ArgumentOutOfRangeException(nameof(bonusDamage));
            IsBasicAttack=isBasicAttack; CanCrit=canCrit; BonusDamage=bonusDamage;
            if(!Enum.IsDefined(typeof(DamageOrigin),origin))throw new ArgumentOutOfRangeException(nameof(origin));
            Origin=origin;
            SourceSnapshot=sourceSnapshot;SkillPolicy=skillPolicy;
            SourceId=sourceId; TargetId=targetId; RawDamage=rawDamage; Type=type;
        }
    }
    public readonly struct DamageResult
    {
        public long Tick { get; }
        public DamageRequest Request { get; }
        public DamageCalculation Calculation { get; }
        public bool IsCritical => Calculation.IsCritical;
        public bool Missed { get; }
        public float PreMitigationDamage { get; }
        public float PostMitigationDamage { get; }
        public float ShieldAbsorbed { get; }
        public float AppliedDamage { get; }
        public float RemainingHP { get; }
        public float RemainingShield { get; }
        public bool Killed { get; }
        public DamageResult(long tick, DamageRequest request, float appliedDamage, float remainingHP, bool killed)
            : this(tick,request,request.RawDamage,request.RawDamage,0,appliedDamage,remainingHP,0,killed) { }
        public DamageResult(long tick, DamageRequest request, float pre, float post, float shield,
            float hpDamage, float hp, float remainingShield, bool killed, DamageCalculation calculation=default,bool missed=false)
        { Missed=missed;Calculation=calculation; Tick=tick; Request=request; PreMitigationDamage=pre; PostMitigationDamage=post;
          ShieldAbsorbed=shield; AppliedDamage=hpDamage; RemainingHP=hp; RemainingShield=remainingShield; Killed=killed; }
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
            bool killed=target.CurrentHP>0 && applied>=target.CurrentHP;
            target.SetVitals(target.CurrentHP-applied,target.CurrentEnergy);
            if(!target.IsAlive)battle.TryRemoveUnit(target.UnitInstanceId);
            var result=new DamageResult(battle.CurrentTick,request,applied,target.CurrentHP,killed);
            battle.RecordDamage(result);
            return result;
        }
    }
    public sealed class MeleeAttackResolver
    {
        private readonly IDamageProcessor damage;
        public MeleeAttackResolver(IDamageProcessor damage=null) { this.damage=damage??new DamageProcessor(); }
        public bool TryResolve(BattleState battle, UnitCombatState source, UnitCombatState target,
            out DamageResult result)
        {
            if(battle==null)throw new ArgumentNullException(nameof(battle));
            result=default;
            if(source==null||target==null||!battle.Units.Contains(source)||!battle.Units.Contains(target)||
                source.TeamId==target.TeamId||!source.IsAlive||!source.IsOnBoard||!target.IsTargetable||
                source.Stats.AttackDelivery!=PokeChess.Core.Pokemon.AttackDeliveryType.Melee||!HexCoordinates.IsInRange(source.Position,target.Position,source.StatusSystem.AttackRange(source)))return false;
            result=damage.Apply(battle,new DamageRequest(source.UnitInstanceId,target.UnitInstanceId,
                source.EffectiveStats.Attack,DamageType.Physical,true,isBasicAttack:true));
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
