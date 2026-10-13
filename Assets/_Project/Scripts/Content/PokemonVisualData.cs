using UnityEngine;

namespace PokeChess.Content
{
    [CreateAssetMenu(menuName="PokeChess/Content/Pokemon Visual")]
    public sealed class PokemonVisualData : ScriptableObject
    {
        public string AnimationId;
        public string Idle="Idle", Walk="Walk", Attack="Attack", Hurt="Hurt", Skill="Charge";
        public Color Tint=Color.white;
    }
}
