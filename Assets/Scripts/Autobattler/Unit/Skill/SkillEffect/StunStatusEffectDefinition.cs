using PokeChess.Autobattler;
using UnityEngine;

[CreateAssetMenu(menuName = "PokeChess/Autobattler/Status Effects/Stun", fileName = "StunStatus")]
public class StunStatusEffectDefinition : UnitStatusEffectDefinition
{
    public override bool Apply(UnitController source, UnitController target)
    {
        return target != null && target.ApplyStun(durationSeconds);
    }
}