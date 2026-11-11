using System;
using System.Linq;
using PokeChess.Client.UI;
using UnityEditor;
using UnityEngine;

public static class ConnectNatureVisuals
{
    public static string Apply()
    {
        const string folder = "Assets/_Project/Sprites/Effects/Synergies/nature/";
        const string target = "Assets/_Project/Resources/NatureVisuals.asset";
        var specs = new[]
        {
            ("summon", "summon", 11, 128), ("worldtreeSummon", "worldtree-summon", 15, 192),
            ("tauntWave", "taunt-wave", 7, 192), ("tauntMarker", "taunt-marker", 1, 32),
            ("rootEnter", "root-enter", 8, 64), ("rootLoop", "root-loop", 8, 64), ("rootExit", "root-exit", 5, 64),
            ("shieldEnter", "shield-enter", 8, 96), ("shieldLoop", "shield-loop", 8, 96),
            ("shieldBreak", "shield-break", 5, 96), ("shieldExpire", "shield-expire", 5, 96),
            ("pulse", "worldtree-pulse", 11, 192), ("pulseHit", "worldtree-pulse-hit", 7, 64),
            ("judgment", "worldtree-judgment", 11, 192), ("judgmentHit", "worldtree-judgment-hit", 9, 96),
            ("heal", "worldtree-heal", 9, 64)
        };
        var frames = specs.Select(spec =>
        {
            string path = folder + "nature-" + spec.Item2 + ".png";
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
                throw new InvalidOperationException("Missing nature texture: " + path);
            if (importer.textureCompression != TextureImporterCompression.Uncompressed ||
                importer.filterMode != FilterMode.Point || importer.mipmapEnabled)
            {
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.filterMode = FilterMode.Point;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }
            var sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>()
                .OrderBy(sprite => int.TryParse(sprite.name.Substring(sprite.name.LastIndexOf('_') + 1), out int index) ? index : 0)
                .ToArray();
            if (sprites.Length != spec.Item3 || sprites.Any(sprite => sprite.rect.width != spec.Item4 || sprite.rect.height != spec.Item4))
                throw new InvalidOperationException("Unexpected authored slices: " + path);
            return sprites;
        }).ToArray();

        var art = AssetDatabase.LoadAssetAtPath<NatureVisuals>(target);
        if (art == null)
        {
            art = ScriptableObject.CreateInstance<NatureVisuals>();
            AssetDatabase.CreateAsset(art, target);
        }
        for (int i = 0; i < specs.Length; i++)
            typeof(NatureVisuals).GetField(specs[i].Item1).SetValue(art, frames[i]);
        EditorUtility.SetDirty(art);
        AssetDatabase.SaveAssetIfDirty(art);
        return "Registered all 16 nature sheets using the supplied sprite slices.";
    }
}
