using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using PokeChess.Client.Data;
using PokeChess.Core.Battle;
namespace PokeChess.Editor
{
    public static class StatusVisualAuthoring
    {
        public static void Apply()
        {
            const string folder="Assets/_Project/Sprites/Effects/";
            var names=new[]{"burn","poison","paralysis","freeze","bleed","blind","confusion","sleep"};
            var behaviors=new[]{StatusBehavior.Burn,StatusBehavior.Poison,StatusBehavior.Paralysis,StatusBehavior.Freeze,StatusBehavior.Bleed,StatusBehavior.Blind,StatusBehavior.Confusion,StatusBehavior.Sleep};
            var effects=names.Select((name,i)=>{
                var path=folder+name+".png";
                var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                if(importer==null)throw new InvalidOperationException("Missing effect: "+path);
                importer.filterMode=FilterMode.Point;importer.mipmapEnabled=false;importer.textureCompression=TextureImporterCompression.Uncompressed;
                importer.alphaIsTransparency=true;importer.SaveAndReimport();
                var frames=AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().OrderByDescending(s=>s.rect.y).ThenBy(s=>s.rect.x).ToArray();
                if(name=="freeze") {
                    // The supplied automatic slice splits off a small shard. Use the complete crystal.
                    var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    const string spritePath="Assets/_Project/Resources/FreezeOverlay.asset";
                    var crystal=AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
                    if(crystal==null){crystal=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,0),64);AssetDatabase.CreateAsset(crystal,spritePath);}
                    frames=new[]{crystal};
                }
                if(frames.Length==0)throw new InvalidOperationException("Effect has no sprites: "+name);
                return new StatusVisual{Behavior=behaviors[i],Frames=frames,Position=i<3?StatusVisualPosition.Feet:i==3?StatusVisualPosition.Overlay:StatusVisualPosition.Head,Opacity=i==3?.55f:1};
            }).ToArray();
            const string libraryPath="Assets/_Project/Resources/StatusVisuals.asset";
            var library=AssetDatabase.LoadAssetAtPath<StatusVisualLibrary>(libraryPath);
            if(library==null){library=ScriptableObject.CreateInstance<StatusVisualLibrary>();AssetDatabase.CreateAsset(library,libraryPath);}
            library.Effects=effects;EditorUtility.SetDirty(library);AssetDatabase.SaveAssets();
            Debug.Log("Registered "+effects.Length+" status sprite effects");
        }
    }
}
