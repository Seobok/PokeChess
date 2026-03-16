using PokeChess.Autobattler;

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