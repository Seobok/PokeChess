using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using PokeChess.Client.Data;

// Run with unity command run_script --file AgentScripts/GeneratePokemonAnimations.cs.
public static class GeneratePokemonAnimations
{
    public static string Main()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play mode before generating assets.");
        var animations=new List<PokemonSpriteAnimation>();
        var factories=new SpriteDataProviderFactories();factories.Init();
        foreach(string folder in Directory.GetDirectories("Assets/_Project/Sprites").OrderBy(p=>p))
        {
            string xml=Path.Combine(folder,"AnimData.xml");if(!File.Exists(xml))continue;
            string species=Path.GetFileName(folder).ToLowerInvariant();
            var nodes=XDocument.Load(xml).Root.Element("Anims").Elements("Anim").ToArray();
            foreach(var node in nodes.Where(n=>n.Element("CopyOf")==null))
            {
                string name=(string)node.Element("Name");int width=(int)node.Element("FrameWidth"),height=(int)node.Element("FrameHeight");
                int[] durations=node.Element("Durations").Elements("Duration").Select(n=>(int)n).ToArray();
                string path=Path.Combine(folder,name+"-Anim.png").Replace('\\','/');
                var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                var provider=factories.GetSpriteEditorDataProviderFromObject(importer);provider.InitSpriteEditorDataProvider();
                var capability=provider.GetDataProvider<ISpriteFrameEditCapability>();
                if(capability==null)throw new Exception("Sprite edit capability missing: "+path);
                foreach(var needed in new[]{EEditCapability.CreateAndDeleteSprite,EEditCapability.EditSpriteName,EEditCapability.EditSpriteRect,EEditCapability.EditPivot})
                    if(!capability.GetEditCapability().HasCapability(needed))throw new Exception("Unsupported sprite edit: "+needed+" at "+path);
                var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                int directions=texture.height/height;
                if(durations.Any(d=>d<=0)||texture.width!=width*durations.Length||texture.height!=height*directions||
                    (directions!=1&&directions!=8))throw new Exception("Invalid sheet: "+path);
                var rects=new List<SpriteRect>();var offsets=new List<Vector2>();
                var previous=provider.GetSpriteRects().ToDictionary(r=>r.name,r=>r.spriteID);
                var shadow=new Texture2D(2,2,TextureFormat.RGBA32,false);
                try
                {
                    shadow.LoadImage(File.ReadAllBytes(Path.Combine(folder,name+"-Shadow.png")));
                    for(int direction=0;direction<directions;direction++)for(int frame=0;frame<durations.Length;frame++)
                    {
                        string frameName=species+"_"+name+"_d"+direction+"_f"+frame;
                        int x=frame*width,y=texture.height-(direction+1)*height;
                        rects.Add(new SpriteRect{name=frameName,rect=new Rect(x,y,width,height),alignment=SpriteAlignment.Center,
                            pivot=new Vector2(.5f,.5f),spriteID=previous.TryGetValue(frameName,out var id)?id:GUID.Generate()});
                        double sx=0,sy=0;int count=0;
                        for(int py=0;py<height;py++)for(int px=0;px<width;px++)
                        {
                            var c=shadow.GetPixel(x+px,y+py);
                            // The small shadow area identifies ground contact; exclude the white sprite-center marker.
                            if(c.a>.5f&&c.g>.5f&&c.r<.5f&&c.b<.5f){sx+=px+.5;sy+=py+.5;count++;}
                        }
                        if(count==0)throw new Exception("Ground marker missing: "+frameName);
                        offsets.Add(new Vector2(width*.5f-(float)(sx/count),height*.5f-(float)(sy/count)));
                    }
                }
                finally {UnityEngine.Object.DestroyImmediate(shadow);}
                importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Multiple;
                importer.filterMode=FilterMode.Point;importer.mipmapEnabled=false;
                importer.textureCompression=TextureImporterCompression.Uncompressed;importer.spritePixelsPerUnit=32;
                provider.SetSpriteRects(rects.ToArray());
                var names=provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
                if(names==null)throw new Exception("Sprite name provider missing: "+path);
                names.SetNameFileIdPairs(rects.Select(r=>new SpriteNameFileIdPair(r.name,r.spriteID)));
                provider.Apply();importer.SaveAndReimport();
                var sprites=AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToDictionary(s=>s.name);
                animations.Add(new PokemonSpriteAnimation{PokemonId=species,Name=name,Directions=directions,FrameCount=durations.Length,
                    HitFrame=(int?)node.Element("HitFrame")??-1,Durations=durations,
                    Frames=rects.Select(r=>sprites[r.name]).ToArray(),GroundOffsets=offsets.ToArray()});
            }
            foreach(var node in nodes.Where(n=>n.Element("CopyOf")!=null))
            {
                var source=animations.Single(a=>a.PokemonId==species&&a.Name==(string)node.Element("CopyOf"));
                animations.Add(new PokemonSpriteAnimation{PokemonId=species,Name=(string)node.Element("Name"),Directions=source.Directions,
                    FrameCount=source.FrameCount,HitFrame=source.HitFrame,Durations=source.Durations,Frames=source.Frames,GroundOffsets=source.GroundOffsets});
            }
        }
        if(!AssetDatabase.IsValidFolder("Assets/_Project/Resources"))AssetDatabase.CreateFolder("Assets/_Project","Resources");
        const string output="Assets/_Project/Resources/PokemonAnimations.asset";
        var library=AssetDatabase.LoadAssetAtPath<PokemonAnimationLibrary>(output);
        if(library==null){library=ScriptableObject.CreateInstance<PokemonAnimationLibrary>();AssetDatabase.CreateAsset(library,output);}
        library.Animations=animations.ToArray();EditorUtility.SetDirty(library);AssetDatabase.SaveAssets();
        return "Generated "+animations.Count+" animations including aliases; "+animations.Sum(a=>a.Frames.Length)+" frame references.";
    }
}
