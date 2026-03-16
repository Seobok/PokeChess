using PokeChess.Autobattler;
using UnityEngine;

public abstract class UnitStatusEffectDefinition : ScriptableObject
{
    [Min(0.1f)] public float durationSeconds = 1f;

    public abstract bool Apply(UnitController source, UnitController target);
}