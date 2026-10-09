using System.Linq;
using UnityEngine;
using UnityEditor;
using PokeChess.Client.UI;
public static class LinkPokemonCries
{
    public static string Run()
    {
        const string path="Assets/_Project/Resources/PokemonCries.asset";
        var library=AssetDatabase.LoadAssetAtPath<PokemonCryLibrary>(path);
        if(library==null){library=ScriptableObject.CreateInstance<PokemonCryLibrary>();AssetDatabase.CreateAsset(library,path);}
        string[] ids={"abra","bulbasaur","caterpie","chansey","charmander","gastly","geodude","magnemite","mankey","slowpoke","squirtle","weedle"};
        library.entries=ids.Select(id=>new PokemonCryLibrary.Entry{pokemonId=id,clip=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/Audio/Unit/"+id+".ogg")}).ToArray();
        if(library.entries.Any(e=>e.clip==null))throw new System.Exception("Missing cry clip.");
        EditorUtility.SetDirty(library);AssetDatabase.SaveAssets();return "Linked 12 original Pokémon cries.";
    }
}
