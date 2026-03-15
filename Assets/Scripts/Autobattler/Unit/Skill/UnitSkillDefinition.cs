using Fusion;
using PokeChess.Autobattler;
using UnityEngine;
using System;
using System.Collections.Generic;

public interface IProjectileVisualSkillDefinition
{
    SkillProjectileView ProjectileViewPrefab { get; }
    Vector3 ProjectileViewOffset { get; }
}

public abstract class UnitSkillDefinition : ScriptableObject
{
    [Header("Targeting")]
    [Min(0)] public int castRange = 1;
    public DamageType damageType = DamageType.Special;

    public abstract bool TryCast(UnitController caster, UnitController preferredTarget);

    protected UnitController ResolveEnemyTarget(UnitController caster, UnitController preferredTarget)
    {
        if (caster == null)
            return null;

        if (caster.IsEnemyInRange(preferredTarget, castRange))
            return preferredTarget;

        return caster.FindClosestEnemyInRange(castRange);
    }

    protected UnitController ResolveFarthestEnemyTarget(UnitController caster)
    {
        if (caster == null)
            return null;

        return caster.FindFarthestEnemyInRange(castRange);
    }

    protected UnitController ResolveAllyTarget(UnitController caster, int range, bool includeSelf)
    {
        if (caster == null)
            return null;

        return caster.FindLowestHealthAllyInRange(range, includeSelf);
    }
}

#region UnitSkillDefinition Example
[CreateAssetMenu(menuName = "PokeChess/Autobattler/Skills/Direct Damage Skill", fileName = "DirectDamageSkill")]
public class DirectDamageSkillDefinition : UnitSkillDefinition
{
    [Header("Effect")]
    [Min(0f)] public float damage = 50f;

    public override bool TryCast(UnitController caster, UnitController preferredTarget)
    {
        UnitController target = ResolveEnemyTarget(caster, preferredTarget);
        if (target == null)
            return false;

        return caster.TryApplyDamage(target, damage, damageType);
    }
}

[CreateAssetMenu(menuName = "PokeChess/Autobattler/Skills/Area Damage Skill", fileName = "AreaDamageSkill")]
public class AreaDamageSkillDefinition : UnitSkillDefinition
{
    [Header("Effect")]
    [Min(0f)] public float damage = 40f;
    [Min(0)] public int effectRadius = 1;

    public override bool TryCast(UnitController caster, UnitController preferredTarget)
    {
        UnitController centerTarget = ResolveEnemyTarget(caster, preferredTarget);
        if (centerTarget == null)
            return false;

        var targets = caster.FindUnitsInRange(centerTarget.Cell, effectRadius, SkillTargetTeam.Enemy, includeSelf: false);
        bool applied = false;

        for (int i = 0; i < targets.Count; i++)
            applied |= caster.TryApplyDamage(targets[i], damage, damageType);

        return applied;
    }
}

[CreateAssetMenu(menuName = "PokeChess/Autobattler/Skills/Heal Skill", fileName = "HealSkill")]
public class HealSkillDefinition : UnitSkillDefinition
{
    [Header("Effect")]
    [Min(0f)] public float healAmount = 40f;
    public bool allowSelf = true;

    public override bool TryCast(UnitController caster, UnitController preferredTarget)
    {
        UnitController target = ResolveAllyTarget(caster, castRange, allowSelf);
        if (target == null)
            return false;

        return target.RestoreHealth(healAmount);
    }
}

[CreateAssetMenu(menuName = "PokeChess/Autobattler/Skills/Buff Skill", fileName = "BuffSkill")]
public class BuffSkillDefinition : UnitSkillDefinition
{
    [Header("Effect")]
    public SkillTargetTeam targetTeam = SkillTargetTeam.AllyOrSelf;
    [Min(0.1f)] public float durationSeconds = 5f;
    public StatBuffEffect[] effects;

    public override bool TryCast(UnitController caster, UnitController preferredTarget)
    {
        if (caster == null || effects == null || effects.Length == 0)
            return false;

        UnitController target = ResolveBuffTarget(caster);
        if (target == null)
            return false;

        return target.ApplyBuff(effects, durationSeconds);
    }

    private UnitController ResolveBuffTarget(UnitController caster)
    {
        switch (targetTeam)
        {
            case SkillTargetTeam.Self:
                return caster;
            case SkillTargetTeam.Ally:
                return caster.FindLowestHealthAllyInRange(castRange, includeSelf: false);
            case SkillTargetTeam.AllyOrSelf:
                return caster.FindLowestHealthAllyInRange(castRange, includeSelf: true);
            default:
                return null;
        }
    }
}
#endregion

public abstract class RuntimeUnitSkillDefinition : UnitSkillDefinition
{
    public sealed override bool TryCast(UnitController caster, UnitController preferredTarget)
    {
        if (caster == null)
            return false;

        UnitSkillRuntime runtime = CreateRuntime(caster, preferredTarget);
        if (runtime == null)
            return false;

        return caster.TryStartSkillRuntime(runtime);
    }

    protected abstract UnitSkillRuntime CreateRuntime(UnitController caster, UnitController preferredTarget);
}

#region RuntimeUnitSkillDefinition Example
[CreateAssetMenu(menuName = "PokeChess/Autobattler/Skills/Blink Behind Target Skill", fileName = "BlinkBehindTargetSkill")]
public class BlinkBehindTargetSkillDefinition : UnitSkillDefinition
{
    [Header("Effect")]
    [Min(0f)] public float damage = 60f;

    public override bool TryCast(UnitController caster, UnitController preferredTarget)
    {
        UnitController target = ResolveFarthestEnemyTarget(caster);
        if (target == null)
            return false;

        if (!caster.TryTeleportBehindTarget(target))
            return false;

        caster.SetCurrentTarget(target);
        return caster.TryApplyDamage(target, damage, damageType);
    }
}

[CreateAssetMenu(menuName = "PokeChess/Autobattler/Skills/Piercing Root Projectile Skill", fileName = "PiercingRootProjectileSkill")]
public class PiercingRootProjectileSkillDefinition : RuntimeUnitSkillDefinition, IProjectileVisualSkillDefinition
{
    [Header("Projectile")]
    [Min(0f)] public float damage = 50f;
    [Min(0.1f)] public float projectileCellsPerSecond = 6f;
    public UnitStatusEffectDefinition onHitStatus;
    [SerializeField] private SkillProjectileView projectileViewPrefab;
    [SerializeField] private Vector3 projectileViewOffset;

    public SkillProjectileView ProjectileViewPrefab => projectileViewPrefab;
    public Vector3 ProjectileViewOffset => projectileViewOffset;

    protected override UnitSkillRuntime CreateRuntime(UnitController caster, UnitController preferredTarget)
    {
        UnitController target = ResolveFarthestEnemyTarget(caster);
        if (target == null)
            return null;

        return new PiercingRootProjectileSkillRuntime(caster, this, target);
    }

    private sealed class PiercingRootProjectileSkillRuntime : UnitSkillRuntime
    {
        private readonly PiercingRootProjectileSkillDefinition _skill;
        private readonly UnitController _target;
        private readonly List<HexCoord> _pathCells = new();
        private readonly HashSet<NetworkId> _hitTargetIds = new();
        private TickTimer _stepTimer;
        private int _currentPathIndex;

        public PiercingRootProjectileSkillRuntime(UnitController caster, PiercingRootProjectileSkillDefinition skill, UnitController target)
            : base(caster, skill, target)
        {
            _skill = skill;
            _target = target;
        }

        protected override bool OnStart()
        {
            if (_target == null || !_target.IsAlive || Runner == null)
                return false;

            BuildLine(Caster.Cell, _target.Cell, _pathCells);
            if (_pathCells.Count <= 1)
                return false;

            _currentPathIndex = 1;
            PlayCurrentSegmentVisual();
            ApplyHitsAtCurrentCell();
            _stepTimer = TickTimer.CreateFromSeconds(Runner, GetStepSeconds());
            return true;
        }

        protected override bool OnTick()
        {
            if (_currentPathIndex >= _pathCells.Count)
                return false;

            if (!_stepTimer.ExpiredOrNotRunning(Runner))
                return true;

            _currentPathIndex++;
            if (_currentPathIndex >= _pathCells.Count)
                return false;

            PlayCurrentSegmentVisual();
            ApplyHitsAtCurrentCell();
            _stepTimer = TickTimer.CreateFromSeconds(Runner, GetStepSeconds());
            return true;
        }

        private float GetStepSeconds()
        {
            return 1f / Mathf.Max(0.1f, _skill.projectileCellsPerSecond);
        }

        private void PlayCurrentSegmentVisual()
        {
            if (_currentPathIndex <= 0 || _currentPathIndex >= _pathCells.Count)
                return;

            Caster.PlaySkillProjectileVisual(_pathCells[_currentPathIndex - 1], _pathCells[_currentPathIndex], GetStepSeconds());
        }

        private void ApplyHitsAtCurrentCell()
        {
            HexCoord cell = _pathCells[_currentPathIndex];
            List<UnitController> hitUnits = Caster.FindUnitsInRange(cell, 0, SkillTargetTeam.Enemy, includeSelf: false);
            for (int i = 0; i < hitUnits.Count; i++)
            {
                UnitController victim = hitUnits[i];
                if (victim == null || victim.Object == null || !_hitTargetIds.Add(victim.Object.Id))
                    continue;

                Caster.TryApplyDamage(victim, _skill.damage, _skill.damageType);
                if (_skill.onHitStatus != null)
                    _skill.onHitStatus.Apply(Caster, victim);
            }
        }

        private static void BuildLine(HexCoord from, HexCoord to, List<HexCoord> results)
        {
            results.Clear();
            int distance = HexCoord.Distance(from, to);
            if (distance <= 0)
            {
                results.Add(from);
                return;
            }

            CubeCoord start = ToCube(from);
            CubeCoord end = ToCube(to);
            for (int i = 0; i <= distance; i++)
            {
                float t = distance == 0 ? 0f : (float)i / distance;
                CubeCoord interpolated = CubeLerp(start, end, t);
                HexCoord cell = FromCube(CubeRound(interpolated));
                if (results.Count == 0 || results[results.Count - 1] != cell)
                    results.Add(cell);
            }
        }

        private static CubeCoord ToCube(HexCoord coord)
        {
            int x = coord.Q - ((coord.R - (coord.R & 1)) / 2);
            int z = coord.R;
            int y = -x - z;
            return new CubeCoord(x, y, z);
        }

        private static HexCoord FromCube(CubeCoord cube)
        {
            int cubeZ = Mathf.RoundToInt(cube.Z);
            int q = Mathf.RoundToInt(cube.X) + ((cubeZ - (cubeZ & 1)) / 2);
            return new HexCoord(q, cubeZ);
        }

        private static CubeCoord CubeLerp(CubeCoord a, CubeCoord b, float t)
        {
            return new CubeCoord(
                Mathf.Lerp(a.X, b.X, t),
                Mathf.Lerp(a.Y, b.Y, t),
                Mathf.Lerp(a.Z, b.Z, t));
        }

        private static CubeCoord CubeRound(CubeCoord cube)
        {
            int rx = Mathf.RoundToInt(cube.X);
            int ry = Mathf.RoundToInt(cube.Y);
            int rz = Mathf.RoundToInt(cube.Z);

            float dx = Mathf.Abs(rx - cube.X);
            float dy = Mathf.Abs(ry - cube.Y);
            float dz = Mathf.Abs(rz - cube.Z);

            if (dx > dy && dx > dz)
                rx = -ry - rz;
            else if (dy > dz)
                ry = -rx - rz;
            else
                rz = -rx - ry;

            return new CubeCoord(rx, ry, rz);
        }

        private readonly struct CubeCoord
        {
            public readonly float X;
            public readonly float Y;
            public readonly float Z;

            public CubeCoord(float x, float y, float z)
            {
                X = x;
                Y = y;
                Z = z;
            }
        }
    }
}
#endregion
