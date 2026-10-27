using System;
using System.Collections.Generic;
using System.Linq;
using PokeChess.Core.Board;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Battle
{
    public enum SkillEffectType { Damage, Heal, Shield, ProjectileDamage, ApplyStatus, Move, PathProjectileDamage, HealthCost, EnergyRefund, GroundStatus, ChainDamage, RandomStatus }
    public enum SkillPowerScaling { LegacyBonus, TotalSpellPower }
    public enum SkillValueSource { Flat, Attack, CasterMaxHP, TargetMaxHP, CasterMaxEnergy }
    public enum EffectTargetSelector { CastTarget, Self, EnemiesAroundCastTarget, AlliesAroundCaster, EnemiesAroundCaster, Spatial, BehindCastTarget }
    public enum SkillEffectOutcome { Applied, Spawned, Skipped }
    public enum SkillEffectSkipReason { None, NoValidTarget, CasterUnavailable, StackPolicyIgnored, ConditionFailed, ChanceFailed }
    public sealed class SkillEffectDefinition
    {
        public SkillEffectType Type { get; }
        public EffectTargetSelector TargetSelector { get; }
        public float BaseValue { get; }
        public bool SpellPowerScalable { get; }
        public SkillPowerScaling PowerScaling { get; }
        public DamageType DamageType { get; }
        public int Radius { get; }
        public float ProjectileSpeed { get; }
        public int? ImpactRadius { get; }
        public string StatusId { get; }
        public StatusTargetTeam StatusTargetTeam { get; }
        public SkillSpatialDefinition Spatial { get; }
        public SkillMovementDefinition Movement { get; }
        public bool Returns { get; }
        public IReadOnlyList<SkillEffectDefinition> PathImpactEffects { get; }
        public SkillNumericDefinition Numeric { get; }
        public IReadOnlyList<string> StatusChoices { get; }
        public float ChainDelay { get; }
        public bool ChainSequential { get; }
        public int ProjectileGroupSize { get; }
        public SkillValueSource ValueSource { get; }
        public IReadOnlyList<float> RankValues { get; }
        public IReadOnlyList<string> RankStatusIds { get; }
        public bool RequiresEnemy => Type==SkillEffectType.GroundStatus||Type==SkillEffectType.RandomStatus||IsDamage || (Type==SkillEffectType.Move&&Movement.Kind!=SkillMovementKind.TeleportSafe&&Movement.Kind!=SkillMovementKind.JumpToCluster) || (Type==SkillEffectType.ApplyStatus&&StatusTargetTeam==StatusTargetTeam.Enemy);
        public bool NeedsCastTarget => TargetSelector==EffectTargetSelector.BehindCastTarget||TargetSelector==EffectTargetSelector.CastTarget ||
            TargetSelector==EffectTargetSelector.EnemiesAroundCastTarget || Type==SkillEffectType.PathProjectileDamage ||
            (Spatial!=null&&(Spatial.Mode==SpatialTargetMode.ChainEnemies||Spatial.Center==SkillAreaCenter.CastTarget||
                Spatial.Shape==SkillAreaShape.Line||Spatial.Shape==SkillAreaShape.Cone));
        public bool IsDamage => Type==SkillEffectType.ChainDamage||Type==SkillEffectType.Damage||Type==SkillEffectType.ProjectileDamage||Type==SkillEffectType.PathProjectileDamage;
        public SkillEffectDefinition(SkillEffectType type,EffectTargetSelector targetSelector,float baseValue,
            bool spellPowerScalable=false,DamageType damageType=DamageType.Magic,int radius=1,float projectileSpeed=6,string statusId=null,StatusTargetTeam statusTargetTeam=StatusTargetTeam.Enemy,
            IEnumerable<float> rankValues=null,SkillValueSource valueSource=SkillValueSource.Flat,IEnumerable<string> rankStatusIds=null,int? impactRadius=null,SkillSpatialDefinition spatial=null,SkillMovementDefinition movement=null,bool returns=false,IEnumerable<SkillEffectDefinition> pathImpactEffects=null,SkillNumericDefinition numeric=null,IEnumerable<string> statusChoices=null,float chainDelay=.15f,bool chainSequential=false,int projectileGroupSize=0,SkillPowerScaling powerScaling=SkillPowerScaling.LegacyBonus)
        {
            if(!Enum.IsDefined(typeof(SkillPowerScaling),powerScaling))throw new ArgumentOutOfRangeException(nameof(powerScaling));
            PowerScaling=powerScaling;Numeric=numeric;StatusChoices=Array.AsReadOnly((statusChoices??Array.Empty<string>()).ToArray());
            DamageCalculator.Validate(chainDelay,true);if(chainDelay<=0||StatusChoices.Any(string.IsNullOrWhiteSpace)||type==SkillEffectType.RandomStatus&&StatusChoices.Count==0)throw new ArgumentException();
            ChainDelay=chainDelay;ChainSequential=chainSequential;ProjectileGroupSize=projectileGroupSize;
            if(projectileGroupSize<0||projectileGroupSize>0&&type!=SkillEffectType.ProjectileDamage)throw new ArgumentException("Invalid projectile group.");
            if(type==SkillEffectType.GroundStatus&&(spatial==null||string.IsNullOrWhiteSpace(statusId)))throw new ArgumentException("Ground status needs an area and status.");
            if((type==SkillEffectType.HealthCost||type==SkillEffectType.EnergyRefund)&&targetSelector!=EffectTargetSelector.Self)
                throw new ArgumentException("Resource effects must target self.");
            if(valueSource!=SkillValueSource.Flat&&valueSource!=SkillValueSource.Attack&&spellPowerScalable)
                throw new ArgumentException("HP/energy ratios do not scale with spell power.");
            bool damageEffect=IsDamageType(type);
            if(valueSource==SkillValueSource.TargetMaxHP&&damageEffect&&numeric?.DamageCap==null)
                throw new ArgumentException("Target HP damage requires an explicit damage cap.");
            if(numeric!=null&&(!damageEffect&&(numeric.Lifesteal>0||numeric.CanCrit||numeric.ArmorPenetration>0||numeric.ExecuteThreshold>0)||
                numeric.ShieldDuration.HasValue&&type!=SkillEffectType.Shield))throw new ArgumentException("Numeric options do not match effect type.");
            var pathImpacts=(pathImpactEffects??Array.Empty<SkillEffectDefinition>()).ToArray();
            if(pathImpacts.Any(e=>e==null||e.Type==SkillEffectType.PathProjectileDamage)||pathImpacts.Length>0&&type!=SkillEffectType.PathProjectileDamage&&type!=SkillEffectType.ProjectileDamage&&type!=SkillEffectType.ChainDamage)
                throw new ArgumentException("Invalid path impact effects.");
            PathImpactEffects=Array.AsReadOnly(pathImpacts);
            if((targetSelector==EffectTargetSelector.Spatial)!=(spatial!=null))throw new ArgumentException("Spatial selector requires spatial data.");
            if((type==SkillEffectType.Move)!=(movement!=null))throw new ArgumentException("Move effect requires movement data.");
            if(type==SkillEffectType.Move&&(baseValue!=0||spellPowerScalable||rankValues!=null&&rankValues.Any(v=>v!=0)))
                throw new ArgumentException("Movement does not use numeric values.");
            if(returns&&type!=SkillEffectType.PathProjectileDamage)throw new ArgumentException("Return path belongs to path projectiles.");
            if(type==SkillEffectType.PathProjectileDamage&&(spatial==null||spatial.Shape!=SkillAreaShape.Line||spatial.Mode!=SpatialTargetMode.Area||spatial.Center!=SkillAreaCenter.Caster))
                throw new ArgumentException("Path projectiles require a caster-centered line.");
            if(spatial!=null&&((spatial.Mode==SpatialTargetMode.NearestAllies&&(IsDamageType(type)||type==SkillEffectType.ApplyStatus&&statusTargetTeam==StatusTargetTeam.Enemy))||
                (spatial.Mode==SpatialTargetMode.ChainEnemies&&!(IsDamageType(type)||type==SkillEffectType.ApplyStatus&&statusTargetTeam==StatusTargetTeam.Enemy))))
                throw new ArgumentException("Spatial selection team mismatch.");
            Spatial=spatial;Movement=movement;Returns=returns;
            if(type==SkillEffectType.Move&&spatial!=null&&(!movement.Forced||spatial.Mode==SpatialTargetMode.NearestAllies))
                throw new ArgumentException("Only hostile displacement can select multiple spatial targets.");
            if(impactRadius.HasValue&&(impactRadius.Value<0||type!=SkillEffectType.ProjectileDamage))throw new ArgumentException("Invalid projectile impact radius.");
            ImpactRadius=impactRadius;
            DamageCalculator.Validate(baseValue,true);DamageCalculator.Validate(projectileSpeed,true);
            if(radius<0||projectileSpeed<=0||!Enum.IsDefined(typeof(SkillEffectType),type)||
                !Enum.IsDefined(typeof(EffectTargetSelector),targetSelector)||!Enum.IsDefined(typeof(DamageType),damageType))
                throw new ArgumentOutOfRangeException();
            if(type==SkillEffectType.ApplyStatus&&(string.IsNullOrWhiteSpace(statusId)||baseValue!=0||spellPowerScalable||
                !Enum.IsDefined(typeof(StatusTargetTeam),statusTargetTeam)))throw new ArgumentException("Invalid status effect request.");
            bool enemy=type==SkillEffectType.GroundStatus||type==SkillEffectType.RandomStatus||IsDamageType(type)||(type==SkillEffectType.Move&&movement.Kind!=SkillMovementKind.TeleportSafe&&movement.Kind!=SkillMovementKind.JumpToCluster)||(type==SkillEffectType.ApplyStatus&&statusTargetTeam==StatusTargetTeam.Enemy);
            if(enemy&&(targetSelector==EffectTargetSelector.Self||targetSelector==EffectTargetSelector.AlliesAroundCaster))
                throw new ArgumentException("Damage effects require enemy selectors.");
            if(!enemy&&(targetSelector==EffectTargetSelector.EnemiesAroundCastTarget||targetSelector==EffectTargetSelector.EnemiesAroundCaster))
                throw new ArgumentException("Support effects require ally selectors.");
            if(!Enum.IsDefined(typeof(SkillValueSource),valueSource)||
                (valueSource==SkillValueSource.Attack&&!IsDamageType(type)))throw new ArgumentException("Invalid value source.");
            var values=(rankValues??new[]{baseValue,baseValue,baseValue}).ToArray();
            if(values.Length!=3)throw new ArgumentException("Exactly three rank values are required.");
            foreach(var v in values)DamageCalculator.Validate(v,true);
            if(type==SkillEffectType.ApplyStatus&&values.Any(v=>v!=0))throw new ArgumentException("Status effects do not use numeric values.");
            var ids=(rankStatusIds??new[]{statusId,statusId,statusId}).ToArray();
            if(ids.Length!=3||(type==SkillEffectType.ApplyStatus&&ids.Any(string.IsNullOrWhiteSpace))||
                (type!=SkillEffectType.ApplyStatus&&rankStatusIds!=null))throw new ArgumentException("Invalid ranked status IDs.");
            RankValues=Array.AsReadOnly(values);RankStatusIds=Array.AsReadOnly(ids);ValueSource=valueSource;
            StatusId=statusId;StatusTargetTeam=statusTargetTeam;Type=type;TargetSelector=targetSelector;BaseValue=baseValue;SpellPowerScalable=spellPowerScalable;
            DamageType=damageType;Radius=radius;ProjectileSpeed=projectileSpeed;
        }
        private static bool IsDamageType(SkillEffectType type)=>type==SkillEffectType.ChainDamage||type==SkillEffectType.Damage||type==SkillEffectType.ProjectileDamage||type==SkillEffectType.PathProjectileDamage;
        public float ResolveValue(float spellPower)
            => ResolveValue(UnitRank.One,spellPower,0);
        public string ResolveStatusId(UnitRank rank) => RankStatusIds[RankIndex(rank)];
        private static int RankIndex(UnitRank rank)
        { if(!Enum.IsDefined(typeof(UnitRank),rank))throw new ArgumentOutOfRangeException(nameof(rank));return (int)rank-1; }
        public float ResolveValue(UnitRank rank,float spellPower,float attack)
        {
            DamageCalculator.Validate(spellPower,true);
            DamageCalculator.Validate(attack,true);
            double value=SkillNumericMath.Magnitude(RankValues[RankIndex(rank)],ValueSource,attack,spellPower,SpellPowerScalable,PowerScaling);
            if(value>float.MaxValue)throw new OverflowException("Skill effect exceeds model capacity.");
            return (float)value;
        }
    }
    public readonly struct SkillEffectEvent
    {
        public long CastId { get; }
        public int StepIndex { get; }
        public int Iteration { get; }
        public string SkillId { get; }
        public string CasterId { get; }
        public string TargetId { get; }
        public int EffectIndex { get; }
        public SkillEffectType Type { get; }
        public long Tick { get; }
        public float CalculatedValue { get; }
        public float ActualValue { get; }
        public SkillEffectOutcome Outcome { get; }
        public SkillEffectSkipReason SkipReason { get; }
        public SkillEffectEvent(string skillId,string caster,string target,int index,SkillEffectType type,long tick,
            float calculated,float actual,SkillEffectOutcome outcome,SkillEffectSkipReason skip,long castId=0,int stepIndex=0,int iteration=0)
        { CastId=castId;StepIndex=stepIndex;Iteration=iteration;SkillId=skillId;CasterId=caster;TargetId=target;EffectIndex=index;Type=type;Tick=tick;
          CalculatedValue=calculated;ActualValue=actual;Outcome=outcome;SkipReason=skip; }
    }
    public readonly struct SkillEffectApplication
    {
        public float ActualValue { get; }
        public SkillEffectOutcome Outcome { get; }
        public SkillEffectSkipReason Reason { get; }
        public SkillEffectApplication(float actual,SkillEffectOutcome outcome=SkillEffectOutcome.Applied,SkillEffectSkipReason reason=SkillEffectSkipReason.None)
        {ActualValue=actual;Outcome=outcome;Reason=reason;}
    }
    public interface ISkillEffectHandler
    {
        SkillEffectType Type { get; }
        SkillEffectApplication Apply(BattleState battle,UnitCombatState caster,UnitCombatState target,
            SkillEffectDefinition effect,float value);
    }
    public sealed class DamageSkillEffectHandler : ISkillEffectHandler
    {
        private readonly IDamageProcessor damage;
        public SkillEffectType Type => SkillEffectType.Damage;
        public DamageSkillEffectHandler(IDamageProcessor damage) {this.damage=damage??throw new ArgumentNullException(nameof(damage));}
        public SkillEffectApplication Apply(BattleState b,UnitCombatState caster,UnitCombatState target,SkillEffectDefinition e,float value) =>
            new SkillEffectApplication(damage.Apply(b,SkillNumericMath.Request(b,caster,target,e,value)).AppliedDamage);
        internal SkillEffectApplication ApplySnapshot(BattleState b,UnitCombatState caster,UnitCombatState target,
            SkillEffectDefinition e,float value,DamageSourceSnapshot snapshot,DamageOrigin origin=DamageOrigin.Periodic) =>
            new SkillEffectApplication(damage.Apply(b,SkillNumericMath.Request(b,caster,target,e,value,origin,snapshot)).AppliedDamage);
    }
    public sealed class HealSkillEffectHandler : ISkillEffectHandler
    {
        public SkillEffectType Type => SkillEffectType.Heal;
        public SkillEffectApplication Apply(BattleState b,UnitCombatState caster,UnitCombatState target,SkillEffectDefinition e,float value)
        {
            return new SkillEffectApplication(b.Healing.Apply(target,value));
        }
    }
    public sealed class ShieldSkillEffectHandler : ISkillEffectHandler
    {
        public SkillEffectType Type => SkillEffectType.Shield;
        public SkillEffectApplication Apply(BattleState b,UnitCombatState caster,UnitCombatState target,SkillEffectDefinition e,float value)
        {
            double total=(double)target.CurrentShield+value*(b.IsOvertime?.34:1);
            if(total>float.MaxValue)throw new OverflowException();
            float before=target.CurrentShield;target.SetShield((float)total);
            return new SkillEffectApplication(target.CurrentShield-before);
        }
    }
    public sealed class ProjectileSkillEffectHandler : ISkillEffectHandler
    {
        public SkillEffectType Type => SkillEffectType.ProjectileDamage;
        public SkillEffectApplication Apply(BattleState b,UnitCombatState caster,UnitCombatState target,SkillEffectDefinition e,float value)
        {
            bool spawned=b.Projectiles.TrySpawnSkill(caster,target,1,e.ProjectileSpeed,
                SkillNumericMath.Request(b,caster,target,e,value,snapshot:SkillNumericMath.Capture(b,caster)),e.ImpactRadius);
            return new SkillEffectApplication(0,spawned?SkillEffectOutcome.Spawned:SkillEffectOutcome.Skipped);
        }
    }
    public sealed class StatusSkillEffectHandler : ISkillEffectHandler
    {
        public SkillEffectType Type=>SkillEffectType.ApplyStatus;
        public SkillEffectApplication Apply(BattleState b,UnitCombatState caster,UnitCombatState target,SkillEffectDefinition e,float value)
        {
            var applied=b.StatusEffects.Apply(e.ResolveStatusId(caster.Rank),caster.UnitInstanceId,target.UnitInstanceId);
            return applied==null?new SkillEffectApplication(0,SkillEffectOutcome.Skipped,SkillEffectSkipReason.StackPolicyIgnored):
                new SkillEffectApplication(applied.StackCount);
        }
    }
    public sealed class SkillEffectExecutor : ISkillStepExecutor
    {
        private readonly Dictionary<SkillEffectType,ISkillEffectHandler> handlers;
        public SkillEffectExecutor(IDamageProcessor damage=null,IEnumerable<ISkillEffectHandler> overrides=null)
        {
            handlers=new ISkillEffectHandler[]{new DamageSkillEffectHandler(damage??new DamageProcessor()),
                new HealSkillEffectHandler(),new ShieldSkillEffectHandler(),new ProjectileSkillEffectHandler(),new StatusSkillEffectHandler()}
                .ToDictionary(h=>h.Type);
            var replacements=(overrides??Array.Empty<ISkillEffectHandler>()).ToArray();
            if(replacements.Any(h=>h==null)||replacements.Select(h=>h.Type).Distinct().Count()!=replacements.Length)
                throw new ArgumentException("Invalid effect handlers.");
            foreach(var h in replacements) {
                if(!Enum.IsDefined(typeof(SkillEffectType),h.Type))throw new ArgumentException("Unknown handler type.");
                handlers[h.Type]=h;
            }
        }
        private static bool Valid(UnitCombatState caster,UnitCombatState target,SkillEffectDefinition effect) =>
            target!=null&&target.IsAlive&&target.IsOnBoard&&
            (effect.RequiresEnemy?target.TeamId!=caster.TeamId&&target.IsTargetable:target.TeamId==caster.TeamId);
        private static UnitCombatState[] Select(BattleState b,UnitCombatState caster,UnitCombatState castTarget,SkillEffectDefinition e, BoardPosition? casterAnchor=null, BoardPosition? targetAnchor=null,BoardPosition? spatialCenter=null,BoardPosition? spatialAim=null)
        {
            if(e.TargetSelector==EffectTargetSelector.BehindCastTarget) {
                if(castTarget==null)return Array.Empty<UnitCombatState>();var a=HexCoordinates.ToAxial(casterAnchor??caster.Position);var t=HexCoordinates.ToAxial(targetAnchor??castTarget.Position);
                var back=SkillGeometry.Ray(targetAnchor??castTarget.Position,new AxialPosition(t.Q-a.Q,t.R-a.R),1);
                return b.Units.Where(u=>u!=castTarget&&Valid(caster,u,e)&&back.Contains(u.Position)).OrderBy(u=>u.UnitInstanceId,StringComparer.Ordinal).ToArray();
            }
            if(e.Spatial!=null) {
                var origin=casterAnchor??caster.Position;
                var aim=spatialAim??targetAnchor??castTarget?.Position??origin;
                var center=spatialCenter??(e.Spatial.Center==SkillAreaCenter.Caster?origin:
                    e.Spatial.Center==SkillAreaCenter.CastTarget?targetAnchor??castTarget?.Position??origin:
                    SkillSpatialSelector.Densest(b,caster,e.Spatial.Extent,e.Spatial.SearchRange,castTarget?.Position,e.Spatial.Shape));
                return SkillSpatialSelector.Select(b,caster,castTarget,e.Spatial,e.RequiresEnemy,origin,center,aim);
            }
            IEnumerable<UnitCombatState> targets;
            switch(e.TargetSelector) {
                case EffectTargetSelector.Self: targets=new[]{caster};break;
                case EffectTargetSelector.CastTarget: targets=castTarget==null?Array.Empty<UnitCombatState>():new[]{castTarget};break;
                case EffectTargetSelector.EnemiesAroundCastTarget:
                    if(!targetAnchor.HasValue&&(castTarget==null||!castTarget.IsTargetable))return Array.Empty<UnitCombatState>();
                    targets=b.Units.Where(t=>HexCoordinates.Distance(targetAnchor??castTarget.Position,t.Position)<=e.Radius);break;
                default: targets=b.Units.Where(t=>HexCoordinates.Distance(casterAnchor??caster.Position,t.Position)<=e.Radius);break;
            }
            return targets.Where(t=>Valid(caster,t,e)).OrderBy(t=>t.UnitInstanceId,StringComparer.Ordinal).ToArray();
        }
        public void Execute(BattleState battle,UnitCombatState caster,UnitCombatState target,SkillDefinition skill)
            => ExecuteEffects(battle,caster,target,skill,skill.Effects);
        public void ExecuteStep(BattleState battle,UnitCombatState caster,UnitCombatState target,SkillDefinition skill,
            SkillStepDefinition step,long castId,int stepIndex,int iteration)
            => ExecuteEffects(battle,caster,target,skill,step.Effects,castId:castId,stepIndex:stepIndex,iteration:iteration);
        public PreparedSkillStep PrepareStep(BattleState battle,UnitCombatState caster,UnitCombatState target,SkillDefinition skill,
            SkillStepDefinition step,long castId,int stepIndex)
        {
            var values=step.Effects.Select(e=>SkillNumericMath.SourceValue(caster,e)).ToArray();
            var origin=caster.Position;BoardPosition? center=target?.Position;
            var centers=step.Effects.Select(e=>e.Spatial==null?(BoardPosition?)null:e.Spatial.Center==SkillAreaCenter.Caster?origin:
                e.Spatial.Center==SkillAreaCenter.CastTarget?center??origin:
                SkillSpatialSelector.Densest(battle,caster,e.Spatial.Extent,e.Spatial.SearchRange,target?.Position,e.Spatial.Shape)).ToArray();
            var aims=step.Effects.Select(e=>(BoardPosition?)(center??origin)).ToArray();
            var sourceSnapshot=SkillNumericMath.Capture(battle,caster,DamageOrigin.Periodic);
            return new PreparedSkillStep(caster,origin,center,iteration=>
                ExecuteEffects(battle,caster,target,skill,step.Effects,values,true,origin,center,castId,stepIndex,iteration,sourceSnapshot,centers,aims),
                step.Effects.Select((e,i)=>new {e,i}).Where(x=>x.e.Spatial?.Mode==SpatialTargetMode.Area)
                    .Select(x=>new SkillAreaGeometry(x.i,x.e.Spatial.Shape,origin,centers[x.i].Value,aims[x.i].Value,x.e.Spatial.Extent)));
        }
        private void ExecuteEffects(BattleState battle,UnitCombatState caster,UnitCombatState target,SkillDefinition skill,
            IReadOnlyList<SkillEffectDefinition> effects,float[] frozenValues=null,bool independent=false,
            BoardPosition? casterAnchor=null,BoardPosition? targetAnchor=null,long castId=0,int stepIndex=0,int iteration=0,
            DamageSourceSnapshot? sourceSnapshot=null,BoardPosition?[] centers=null,BoardPosition?[] aims=null,DamageOrigin snapshotOrigin=DamageOrigin.Periodic,
            IReadOnlyDictionary<SkillEffectDefinition,float> frozenTree=null)
        {
            if(battle==null||skill==null)throw new ArgumentNullException();
            if(caster==null||!battle.Units.Contains(caster)||(target!=null&&!battle.Units.Contains(target)))
                throw new ArgumentException("Effect participants must belong to battle.");
            foreach(var e in effects.Where(e=>e.Type==SkillEffectType.ApplyStatus))
                if(battle.StatusEffects.GetDefinition(e.ResolveStatusId(caster.Rank)).TargetTeam!=e.StatusTargetTeam)
                    throw new ArgumentException("Status target team does not match skill effect.");
            // Resolve values before applying any effect; invalid scaling cannot leave partial numeric changes.
            var values=frozenValues??effects.Select(e=>frozenTree!=null?frozenTree[e]:SkillNumericMath.SourceValue(caster,e)).ToArray();
            // Freeze target lists before effects: a killed AoE center must not erase later CC targets.
            var selections=effects.Select(e=>e.TargetSelector==EffectTargetSelector.EnemiesAroundCastTarget||e.Spatial?.Mode==SpatialTargetMode.ChainEnemies?
                Select(battle,caster,target,e,casterAnchor,targetAnchor):null).ToArray();
            for(int i=0;i<effects.Count;i++) {
                var e=effects[i];
                if(!independent&&(!caster.IsAlive||!caster.IsOnBoard)) {
                    Record(battle,skill,castId,stepIndex,iteration,caster,null,i,e,values[i],0,SkillEffectOutcome.Skipped,SkillEffectSkipReason.CasterUnavailable);break;
                }
                if(e.Spatial?.Mode==SpatialTargetMode.Area) {
                    var areaOrigin=casterAnchor??caster.Position;
                    var areaCenter=centers?[i]??(e.Spatial.Center==SkillAreaCenter.Caster?areaOrigin:
                        e.Spatial.Center==SkillAreaCenter.CastTarget?targetAnchor??target?.Position??areaOrigin:
                        SkillSpatialSelector.Densest(battle,caster,e.Spatial.Extent,e.Spatial.SearchRange,target?.Position,e.Spatial.Shape));
                    var aim=aims?[i]??targetAnchor??target?.Position??areaOrigin;
                    battle.RecordSkillArea(new SkillAreaEvent(caster.UnitInstanceId,castId,stepIndex,iteration,
                        new SkillAreaGeometry(i,e.Spatial.Shape,areaOrigin,areaCenter,aim,e.Spatial.Extent)));
                }
                if(e.Type==SkillEffectType.GroundStatus) {
                    var c=e.Spatial.Center==SkillAreaCenter.Caster?caster.Position:target?.Position??caster.Position;
                    battle.WorldEffects.SpawnGround(caster,e,c,values[i]);continue;
                }
                if(e.Type==SkillEffectType.ChainDamage) {battle.WorldEffects.SpawnChain(caster,target,skill,e,values[i]);continue;}
                if(e.Type==SkillEffectType.Move) {
                    var victims=e.Spatial==null?new[]{target}:Select(battle,caster,target,e,casterAnchor,targetAnchor,centers?[i],aims?[i]);
                    foreach(var victim in victims) {
                        if(!independent&&(!caster.IsAlive||!caster.IsOnBoard))break;
                        ExecuteMovement(battle,caster,victim,skill,e,castId,stepIndex,iteration,independent,sourceSnapshot,frozenTree,i,casterAnchor);
                    }
                    continue;
                }
                if(e.Type==SkillEffectType.ProjectileDamage&&(e.PathImpactEffects.Count>0||e.ProjectileGroupSize>0)) {
                    var impactTree=CaptureValues(caster,e.PathImpactEffects);var impactValues=e.PathImpactEffects.Select(v=>impactTree[v]).ToArray();
                    var snapshot=SkillNumericMath.Capture(battle,caster);var launchOrigin=caster.Position;
                    foreach(var victim in Select(battle,caster,target,e,casterAnchor,targetAnchor,centers?[i],aims?[i])) {
                        Action<bool> completion=e.ProjectileGroupSize>0?battle.WorldEffects.ProjectileGroup(caster,victim,skill,e,castId,snapshot,impactValues):null;
                        bool spawned=battle.Projectiles.TrySpawnSkill(caster,victim,skill.Range,e.ProjectileSpeed,
                            SkillNumericMath.Request(battle,caster,victim,e,values[i],snapshot:snapshot),e.ImpactRadius,
                            e.ProjectileGroupSize>0?null:(Action<UnitCombatState>)(v=>ExecuteEffects(battle,caster,v,skill,e.PathImpactEffects,impactValues,true,
                                casterAnchor:launchOrigin,targetAnchor:v.Position,castId:castId,stepIndex:stepIndex,iteration:iteration,
                                sourceSnapshot:snapshot,snapshotOrigin:DamageOrigin.Direct,frozenTree:impactTree)),completion);
                        if(!spawned)completion?.Invoke(false);
                        Record(battle,skill,castId,stepIndex,iteration,caster,victim,i,e,values[i],0,
                            spawned?SkillEffectOutcome.Spawned:SkillEffectOutcome.Skipped,spawned?SkillEffectSkipReason.None:SkillEffectSkipReason.NoValidTarget);
                    }
                    continue;
                }
                if(e.Type==SkillEffectType.PathProjectileDamage) {
                    var impactTree=CaptureValues(caster,e.PathImpactEffects);
                    var impactValues=e.PathImpactEffects.Select(v=>impactTree[v]).ToArray();
                    var snapshot=SkillNumericMath.Capture(battle,caster);
                    var launchOrigin=caster.Position;
                    bool spawned=battle.PathProjectiles.Spawn(caster,target,skill.Id,e,values[i],castId,stepIndex,iteration,victim=>
                        ExecuteEffects(battle,caster,victim,skill,e.PathImpactEffects,impactValues,true,
                            casterAnchor:launchOrigin,targetAnchor:victim.Position,castId:castId,stepIndex:stepIndex,iteration:iteration,sourceSnapshot:snapshot,snapshotOrigin:DamageOrigin.Direct,frozenTree:impactTree));
                    Record(battle,skill,castId,stepIndex,iteration,caster,target,i,e,values[i],0,
                        spawned?SkillEffectOutcome.Spawned:SkillEffectOutcome.Skipped,spawned?SkillEffectSkipReason.None:SkillEffectSkipReason.NoValidTarget);continue;
                }
                var targets=selections[i]??Select(battle,caster,target,e,casterAnchor,targetAnchor,centers?[i],aims?[i]);
                if(targets.Length==0) {
                    Record(battle,skill,castId,stepIndex,iteration,caster,null,i,e,values[i],0,SkillEffectOutcome.Skipped,SkillEffectSkipReason.NoValidTarget);continue;
                }
                foreach(var t in targets) {
                    if(!independent&&(!caster.IsAlive||!caster.IsOnBoard)) {
                        Record(battle,skill,castId,stepIndex,iteration,caster,t,i,e,values[i],0,SkillEffectOutcome.Skipped,SkillEffectSkipReason.CasterUnavailable);return;
                    }
                    if(!Valid(caster,t,e)) {
                        Record(battle,skill,castId,stepIndex,iteration,caster,t,i,e,values[i],0,SkillEffectOutcome.Skipped,SkillEffectSkipReason.NoValidTarget);continue;
                    }
                    if(!e.IsDamage&&!SkillNumericMath.Matches(e.Numeric,caster,t)) {
                        Record(battle,skill,castId,stepIndex,iteration,caster,t,i,e,values[i],0,SkillEffectOutcome.Skipped,SkillEffectSkipReason.ConditionFailed);continue;
                    }
                    if(!e.IsDamage&&e.Numeric!=null&&e.Numeric.Chance<1&&
                        (e.Numeric.Chance==0||(battle.RngStreams.Skill.NextUInt64()>>11)*(1.0/9007199254740992.0)>=e.Numeric.Chance)) {
                        Record(battle,skill,castId,stepIndex,iteration,caster,t,i,e,values[i],0,SkillEffectOutcome.Skipped,SkillEffectSkipReason.ChanceFailed);continue;
                    }
                    float actualValue=values[i]*(e.ValueSource==SkillValueSource.TargetMaxHP&&!e.IsDamage?t.Stats.MaxHP:1);
                    SkillEffectApplication applied;
                    if(e.Type==SkillEffectType.RandomStatus) {
                        var id=e.StatusChoices[battle.RngStreams.Skill.NextInt(e.StatusChoices.Count)];
                        var status=independent?battle.StatusEffects.ApplyPersistent(id,caster.UnitInstanceId,t.UnitInstanceId):battle.StatusEffects.Apply(id,caster.UnitInstanceId,t.UnitInstanceId);
                        applied=new SkillEffectApplication(status?.StackCount??0);
                    } else if(e.Type==SkillEffectType.HealthCost) {
                        float before=t.CurrentHP;t.SetVitals(Math.Max(1,before-actualValue),t.CurrentEnergy);applied=new SkillEffectApplication(before-t.CurrentHP);
                    } else if(e.Type==SkillEffectType.EnergyRefund) {
                        float before=t.CurrentEnergy;battle.Energy.Refund(t,actualValue);applied=new SkillEffectApplication(t.CurrentEnergy-before);
                    } else if(e.Type==SkillEffectType.Shield&&e.Numeric?.ShieldDuration!=null) {
                        float before=t.CurrentShield;battle.Shields.Add(t,caster.UnitInstanceId,skill.Id+":"+stepIndex+":"+i,
                            actualValue*(battle.IsOvertime?.34f:1),e.Numeric.ShieldDuration.Value);applied=new SkillEffectApplication(t.CurrentShield-before);
                    } else applied=independent&&e.Type==SkillEffectType.ApplyStatus
                        ? ApplyPersistentStatus(battle,caster,t,e)
                        : sourceSnapshot.HasValue&&handlers[e.Type] is DamageSkillEffectHandler damageHandler
                        ? damageHandler.ApplySnapshot(battle,caster,t,e,values[i],sourceSnapshot.Value,snapshotOrigin)
                        : handlers[e.Type].Apply(battle,caster,t,e,actualValue);
                    Record(battle,skill,castId,stepIndex,iteration,caster,t,i,e,values[i],applied.ActualValue,applied.Outcome,
                        applied.Outcome==SkillEffectOutcome.Skipped?
                            (applied.Reason==SkillEffectSkipReason.None?SkillEffectSkipReason.NoValidTarget:applied.Reason):SkillEffectSkipReason.None);
                }
            }
        }
        internal void ExecuteGenerated(BattleState b,UnitCombatState caster,UnitCombatState target,SkillDefinition skill,IReadOnlyList<SkillEffectDefinition> effects,float[] values,DamageSourceSnapshot snapshot)=>
            ExecuteEffects(b,caster,target,skill,effects,values,true,sourceSnapshot:snapshot,snapshotOrigin:DamageOrigin.Direct);
        private static IReadOnlyDictionary<SkillEffectDefinition,float> CaptureValues(UnitCombatState caster,IEnumerable<SkillEffectDefinition> effects)
        {
            var values=new Dictionary<SkillEffectDefinition,float>();
            Action<SkillEffectDefinition> visit=null;
            visit=e=>{
                if(values.ContainsKey(e))return;
                values[e]=SkillNumericMath.SourceValue(caster,e);
                foreach(var child in e.PathImpactEffects)visit(child);
                if(e.Movement!=null)foreach(var child in e.Movement.ImpactEffects)visit(child);
            };
            foreach(var effect in effects)visit(effect);
            return values;
        }
        private void ExecuteMovement(BattleState battle,UnitCombatState caster,UnitCombatState target,SkillDefinition skill,
            SkillEffectDefinition effect,long castId,int stepIndex,int iteration,bool generated,DamageSourceSnapshot? inheritedSnapshot,
            IReadOnlyDictionary<SkillEffectDefinition,float> frozenTree,int effectIndex,BoardPosition? sourceOrigin)
        {
            var move=effect.Movement;
            var impactTree=frozenTree??CaptureValues(caster,move.ImpactEffects);
            var values=move.ImpactEffects.Select(e=>impactTree[e]).ToArray();
            var snapshot=inheritedSnapshot??SkillNumericMath.Capture(battle,caster);
            Action impact=()=>{
                if((move.Kind==SkillMovementKind.DashToTarget||move.Kind==SkillMovementKind.JumpToTarget)&&
                    (target==null||!target.IsTargetable||HexCoordinates.Distance(caster.Position,target.Position)>1))return;
                ExecuteEffects(battle,caster,target,skill,move.ImpactEffects,values,true,
                    targetAnchor:target?.Position,castId:castId,stepIndex:stepIndex,iteration:iteration,sourceSnapshot:snapshot,snapshotOrigin:DamageOrigin.Direct,frozenTree:impactTree);
            };
            // A blocked knockback still deals its hit; throws and charges only hit after arrival.
            if(move.Kind==SkillMovementKind.Knockback)impact();
            bool started=battle.Movement.TryStart(caster,target,move,move.Kind==SkillMovementKind.Knockback?null:impact,generated,castId,stepIndex,iteration,sourceOrigin);
            Record(battle,skill,castId,stepIndex,iteration,caster,target,effectIndex,effect,0,0,
                started?SkillEffectOutcome.Spawned:SkillEffectOutcome.Skipped,started?SkillEffectSkipReason.None:SkillEffectSkipReason.NoValidTarget);
        }
        private static SkillEffectApplication ApplyPersistentStatus(BattleState battle,UnitCombatState caster,UnitCombatState target,SkillEffectDefinition effect)
        {
            var applied=battle.StatusEffects.ApplyPersistent(effect.ResolveStatusId(caster.Rank),caster.UnitInstanceId,target.UnitInstanceId);
            return applied==null?new SkillEffectApplication(0,SkillEffectOutcome.Skipped,SkillEffectSkipReason.StackPolicyIgnored):new SkillEffectApplication(applied.StackCount);
        }
        private static void Record(BattleState b,SkillDefinition skill,long castId,int stepIndex,int iteration,UnitCombatState caster,UnitCombatState target,
            int i,SkillEffectDefinition e,float value,float actual,SkillEffectOutcome outcome,SkillEffectSkipReason reason) =>
            b.RecordSkillEffect(new SkillEffectEvent(skill.Id,caster.UnitInstanceId,target?.UnitInstanceId,i,e.Type,b.CurrentTick,value,actual,outcome,reason,castId,stepIndex,iteration));
    }
}
