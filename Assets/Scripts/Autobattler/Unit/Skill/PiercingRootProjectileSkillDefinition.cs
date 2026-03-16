using Fusion;
using PokeChess.Autobattler;
using System.Collections.Generic;
using UnityEngine;

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