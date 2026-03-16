using PokeChess.Autobattler;
using UnityEngine;

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
