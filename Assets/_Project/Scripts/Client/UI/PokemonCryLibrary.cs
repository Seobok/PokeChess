using System;
using UnityEngine;
namespace PokeChess.Client.UI
{
    public sealed class PokemonCryLibrary : ScriptableObject
    {
        [Serializable] public sealed class Entry { public string pokemonId; public AudioClip clip; }
        public Entry[] entries=Array.Empty<Entry>();
        public AudioClip Get(string id)
        {foreach(var entry in entries)if(entry.pokemonId==id)return entry.clip;return null;}
    }
}
