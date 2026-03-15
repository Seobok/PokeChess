using PokeChess.Autobattler;
using UnityEngine;

public abstract class UnitStatusEffectDefinition : ScriptableObject
{
    [Min(0.1f)] public float durationSeconds = 1f;

    public abstract bool Apply(UnitController source, UnitController target);
}

[CreateAssetMenu(menuName = "PokeChess/Autobattler/Status Effects/Root", fileName = "RootStatus")]
public class RootStatusEffectDefinition : UnitStatusEffectDefinition
{
    public override bool Apply(UnitController source, UnitController target)
    {
        return target != null && target.ApplyRoot(durationSeconds);
    }
}

[CreateAssetMenu(menuName = "PokeChess/Autobattler/Status Effects/Stun", fileName = "StunStatus")]
public class StunStatusEffectDefinition : UnitStatusEffectDefinition
{
    public override bool Apply(UnitController source, UnitController target)
    {
        return target != null && target.ApplyStun(durationSeconds);
    }
}