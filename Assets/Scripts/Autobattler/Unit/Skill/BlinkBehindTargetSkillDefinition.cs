using PokeChess.Autobattler;
using UnityEngine;

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