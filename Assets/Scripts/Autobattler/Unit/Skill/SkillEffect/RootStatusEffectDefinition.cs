using PokeChess.Autobattler;
using UnityEngine;

[CreateAssetMenu(menuName = "PokeChess/Autobattler/Status Effects/Root", fileName = "RootStatus")]
public class RootStatusEffectDefinition : UnitStatusEffectDefinition
{
    public override bool Apply(UnitController source, UnitController target)
    {
        return target != null && target.ApplyRoot(durationSeconds);
    }
}