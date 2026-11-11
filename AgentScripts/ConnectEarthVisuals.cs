using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using PokeChess.Client.UI;
public static class ConnectEarthVisuals
{
    public static string Apply()
    {
        const string target="Assets/_Project/Resources/EarthVisuals.asset";
        var art=AssetDatabase.LoadAssetAtPath<EarthVisuals>(target);
        if(art==null){art=ScriptableObject.CreateInstance<EarthVisuals>();AssetDatabase.CreateAsset(art,target);}
        var specs=new[]{("preview","earth-rock-preview",1,64),("idle","earth-rock-idle",1,64),
            ("spawn","earth-rock-spawn",8,64),("end","earth-rock-end",5,64),("range","earth-range-cell",8,64),
            ("guard","earth-ranged-guard-hit",6,64),("shieldEnter","earth-shield-enter",8,64),("shieldHold","earth-shield-hold",12,64),
            ("shieldBreak","earth-shield-break",5,64),("shieldExpire","earth-shield-expire",5,64),("pulse","earth-rock-pulse",9,192)};
        foreach(var spec in specs) {
            string path="Assets/_Project/Sprites/Effects/Synergies/earth/"+spec.Item2+".png";
            var importer=AssetImporter.GetAtPath(path) as TextureImporter;
            if(importer==null)throw new InvalidOperationException("Missing texture: "+path);
            if(importer.textureCompression!=TextureImporterCompression.Uncompressed||importer.filterMode!=FilterMode.Point||importer.mipmapEnabled) {
                importer.textureCompression=TextureImporterCompression.Uncompressed;importer.filterMode=FilterMode.Point;importer.mipmapEnabled=false;importer.SaveAndReimport();
            }
            var frames=AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().OrderBy(s=>int.Parse(s.name.Substring(s.name.LastIndexOf('_')+1))).ToArray();
            if(frames.Length!=spec.Item3||frames.Any(s=>s.rect.width!=spec.Item4||s.rect.height!=spec.Item4))throw new InvalidOperationException("Unexpected authored slices: "+path);
            typeof(EarthVisuals).GetField(spec.Item1).SetValue(art,frames);
        }
        EditorUtility.SetDirty(art);AssetDatabase.SaveAssets();
        return "Connected 11 earth sheets; preserved sprite IDs/rects; point filtering and uncompressed pixels.";
    }
}
