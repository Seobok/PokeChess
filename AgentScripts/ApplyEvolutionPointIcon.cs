using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using PokeChess.Client.UI;
public static class ApplyEvolutionPointIcon
{
    public static string Main()
    {
        const string path="Assets/_Project/Sprites/Items/MoonStone.png";
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType=TextureImporterType.Sprite;
        importer.spriteImportMode=SpriteImportMode.Single;
        importer.mipmapEnabled=false;
        importer.SaveAndReimport();
        var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(path);
        var library=Resources.Load<ItemIconLibrary>("ItemIcons");
        if(sprite==null||library==null)throw new Exception("MoonStone sprite or ItemIcons library missing.");
        library.evolutionPoint=sprite;EditorUtility.SetDirty(library);AssetDatabase.SaveAssets();
        return "Evolution point icon references "+AssetDatabase.GetAssetPath(library.evolutionPoint);
    }
}
