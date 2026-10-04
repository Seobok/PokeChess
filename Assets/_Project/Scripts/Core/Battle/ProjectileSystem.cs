using System;
using System.Collections.Generic;
using System.Linq;
using PokeChess.Core.Board;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Battle
{
    public enum ProjectileEventKind { Spawned, Hit, Expired }
    public enum ProjectileExpireReason { None, TargetDead, TargetRemoved, TargetUntargetable }
    public sealed class ProjectileState
    {
        public long Id { get; }
        public string SourceId { get; }
        public string TargetId { get; }
        public BoardPosition LaunchPosition { get; }
        public BoardPosition TargetPositionAtLaunch { get; }
        public long SpawnTick { get; }
        public long ArrivalTick { get; }
        internal ProjectileState(long id,UnitCombatState source,UnitCombatState target,long tick,long arrival)
        {
            Id=id;SourceId=source.UnitInstanceId;TargetId=target.UnitInstanceId;
            LaunchPosition=source.Position;TargetPositionAtLaunch=target.Position;
            SpawnTick=tick;ArrivalTick=arrival;
        }
    }
    public readonly struct ProjectileEvent
    {
        public ProjectileState Projectile { get; }
        public long Tick { get; }
        public ProjectileEventKind Kind { get; }
        public ProjectileExpireReason Reason { get; }
        public ProjectileEvent(ProjectileState projectile,long tick,ProjectileEventKind kind,
            ProjectileExpireReason reason=ProjectileExpireReason.None)
        { Projectile=projectile;Tick=tick;Kind=kind;Reason=reason; }
    }
    public sealed class ProjectileSystem
    {
        private readonly BattleState battle;
        private readonly List<ProjectileState> active=new List<ProjectileState>();
        private readonly List<ProjectileEvent> events=new List<ProjectileEvent>();
        private long nextId;
        public IReadOnlyList<ProjectileState> Active => Array.AsReadOnly(active.ToArray());
        public IReadOnlyList<ProjectileEvent> EventsThisTick => Array.AsReadOnly(events.ToArray());
        internal ProjectileSystem(BattleState battle) { this.battle=battle; }
        internal void BeginTick() => events.Clear();
        internal bool TrySpawn(UnitCombatState source,UnitCombatState target)
        {
            if(source==null||target==null||!battle.Units.Contains(source)||!battle.Units.Contains(target)||
                !source.IsAlive||!source.IsOnBoard||!target.IsTargetable||source.TeamId==target.TeamId||
                source.Stats.AttackDelivery!=AttackDeliveryType.Projectile||
                !HexCoordinates.IsInRange(source.Position,target.Position,source.Stats.AttackRange))return false;
            int travel=BattleSimulation.ToTicks((double)HexCoordinates.Distance(source.Position,target.Position)/
                source.Stats.ProjectileSpeed,battle.TickRate);
            var projectile=new ProjectileState(checked(++nextId),source,target,battle.CurrentTick,
                checked(battle.CurrentTick+travel));
            active.Add(projectile);
            events.Add(new ProjectileEvent(projectile,battle.CurrentTick,ProjectileEventKind.Spawned));
            return true;
        }
        internal void Advance(IDamageProcessor damage)
        {
            foreach(var p in active.Where(p=>p.ArrivalTick<=battle.CurrentTick).OrderBy(p=>p.Id).ToArray())
            {
                var target=battle.Units.Single(u=>u.UnitInstanceId==p.TargetId);
                var reason=!target.IsAlive?ProjectileExpireReason.TargetDead:
                    !target.IsOnBoard?ProjectileExpireReason.TargetRemoved:
                    target.IsUntargetable?ProjectileExpireReason.TargetUntargetable:ProjectileExpireReason.None;
                // Consume before applying; each projectile can resolve at most once.
                active.Remove(p);
                if(reason!=ProjectileExpireReason.None)
                { events.Add(new ProjectileEvent(p,battle.CurrentTick,ProjectileEventKind.Expired,reason));continue; }
                var source=battle.Units.Single(u=>u.UnitInstanceId==p.SourceId);
                damage.Apply(battle,new DamageRequest(p.SourceId,p.TargetId,source.Stats.Attack,DamageType.Physical));
                events.Add(new ProjectileEvent(p,battle.CurrentTick,ProjectileEventKind.Hit));
            }
        }
    }
    public class BasicAttackCombatBehaviorPolicy : CombatBehaviorPolicy
    {
        private readonly IDamageProcessor damage;
        private readonly MeleeAttackResolver melee;
        public BasicAttackCombatBehaviorPolicy(IDamageProcessor damage=null)
        { this.damage=damage??new RawDamageProcessor();melee=new MeleeAttackResolver(this.damage); }
        public override void OnAttackTiming(BattleState battle,UnitCombatState unit,UnitCombatState target)
        {
            if(unit.Stats.AttackDelivery==AttackDeliveryType.Projectile)battle.Projectiles.TrySpawn(unit,target);
            else melee.TryResolve(battle,unit,target,out _);
        }
        public override void OnProjectilesTiming(BattleState battle) => battle.Projectiles.Advance(damage);
    }
}
