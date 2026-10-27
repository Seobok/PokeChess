using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using PokeChess.Client.Data;
namespace PokeChess.Editor
{
    // Uses the existing source sheets and marker convention. Never edits sprite .meta files directly.
    public static class RegisterRosterAnimations
    {
        private static string Key(string value)=>new string(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        public static void ApplyAndBuild()
        {
            var plan=JsonUtility.FromJson<SkillContentBuilder.Plan>(File.ReadAllText("Assets/_Project/Content/SkillPlan.json"));
            var library=PokemonAnimationLibrary.Load();var animations=library.Animations.ToList();
            var folders=Directory.GetDirectories("Assets/_Project/Sprites/Pokemons/1gen").ToDictionary(p=>Key(Path.GetFileName(p)));
            var factories=new SpriteDataProviderFactories();factories.Init();
            foreach(var row in plan.Rows.Where(r=>!animations.Any(a=>a.PokemonId==r.Id))) {
                if(!folders.TryGetValue(Key(row.Id),out var folder))throw new InvalidOperationException("No source art: "+row.Id);
                var nodes=XDocument.Load(Path.Combine(folder,"AnimData.xml")).Root.Element("Anims").Elements("Anim").ToDictionary(n=>(string)n.Element("Name"));
                var wanted=new HashSet<string>(new[]{"Idle","Walk","Attack","Hurt","Charge"}.Where(nodes.ContainsKey));
                foreach(var name in wanted.ToArray()){var current=name;var seen=new HashSet<string>();while(nodes[current].Element("CopyOf")!=null){if(!seen.Add(current))throw new Exception("Cyclic motion alias");current=(string)nodes[current].Element("CopyOf");wanted.Add(current);}}
                foreach(var name in wanted.Where(n=>nodes[n].Element("CopyOf")==null).OrderBy(n=>n,StringComparer.Ordinal)) {
                    var node=nodes[name];int width=(int)node.Element("FrameWidth"),height=(int)node.Element("FrameHeight");
                    var durations=node.Element("Durations").Elements("Duration").Select(n=>(int)n).ToArray();
                    var path=Path.Combine(folder,name+"-Anim.png").Replace('\\','/');var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                    var provider=factories.GetSpriteEditorDataProviderFromObject(importer);provider.InitSpriteEditorDataProvider();
                    var capability=provider.GetDataProvider<ISpriteFrameEditCapability>();if(capability==null)throw new Exception("Missing sprite capability: "+path);
                    foreach(var needed in new[]{EEditCapability.CreateAndDeleteSprite,EEditCapability.EditSpriteName,EEditCapability.EditSpriteRect,EEditCapability.EditPivot})
                        if(!capability.GetEditCapability().HasCapability(needed))throw new Exception("Unsupported sprite operation: "+needed+" at "+path);
                    var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);int directions=texture.height/height;
                    if(durations.Any(d=>d<=0)||texture.width!=width*durations.Length||texture.height!=height*directions||(directions!=1&&directions!=8))throw new Exception("Invalid sheet "+path);
                    var old=provider.GetSpriteRects();var rects=new List<SpriteRect>();var offsets=new List<Vector2>();var shadow=new Texture2D(2,2,TextureFormat.RGBA32,false);
                    try {
                        shadow.LoadImage(File.ReadAllBytes(Path.Combine(folder,name+"-Shadow.png")));var pixels=shadow.GetPixels32();
                        for(int direction=0;direction<directions;direction++)for(int frame=0;frame<durations.Length;frame++) {
                            int x=frame*width,y=texture.height-(direction+1)*height;var rect=new Rect(x,y,width,height);
                            var previous=old.FirstOrDefault(r=>r.rect==rect);var spriteName=row.Id+"_"+name+"_d"+direction+"_f"+frame;
                            rects.Add(new SpriteRect{name=spriteName,rect=rect,alignment=SpriteAlignment.Center,pivot=new Vector2(.5f,.5f),spriteID=previous?.spriteID??GUID.Generate()});
                            double sx=0,sy=0;int count=0;
                            for(int py=0;py<height;py++)for(int px=0;px<width;px++) {var color=pixels[x+px+(y+py)*shadow.width];if(color.a>127&&color.g>127&&color.r<127&&color.b<127){sx+=px+.5;sy+=py+.5;count++;}}
                            if(count==0)throw new Exception("Missing ground marker "+spriteName);
                            offsets.Add(new Vector2(width*.5f-(float)(sx/count),height*.5f-(float)(sy/count)));
                        }
                    }finally{UnityEngine.Object.DestroyImmediate(shadow);}
                    importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Multiple;importer.spritePixelsPerUnit=32;
                    importer.filterMode=FilterMode.Point;importer.mipmapEnabled=false;importer.textureCompression=TextureImporterCompression.Uncompressed;
                    provider.SetSpriteRects(rects.ToArray());var names=provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
                    if(names==null)throw new Exception("Missing sprite name provider "+path);
                    names.SetNameFileIdPairs(rects.Select(r=>new SpriteNameFileIdPair(r.name,r.spriteID)));provider.Apply();importer.SaveAndReimport();
                    var sprites=AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToDictionary(s=>s.name);
                    animations.Add(new PokemonSpriteAnimation{PokemonId=row.Id,Name=name,Directions=directions,FrameCount=durations.Length,
                        HitFrame=(int?)node.Element("HitFrame")??-1,Durations=durations,Frames=rects.Select(r=>sprites[r.name]).ToArray(),GroundOffsets=offsets.ToArray()});
                }
                var aliases=wanted.Where(n=>nodes[n].Element("CopyOf")!=null).ToList();
                while(aliases.Count>0){var name=aliases.FirstOrDefault(n=>animations.Any(a=>a.PokemonId==row.Id&&a.Name==(string)nodes[n].Element("CopyOf")));
                    if(name==null)throw new Exception("Unresolved motion alias: "+row.Id);var original=animations.Single(a=>a.PokemonId==row.Id&&a.Name==(string)nodes[name].Element("CopyOf"));
                    animations.Add(new PokemonSpriteAnimation{PokemonId=row.Id,Name=name,Directions=original.Directions,FrameCount=original.FrameCount,HitFrame=original.HitFrame,
                        Durations=original.Durations,Frames=original.Frames,GroundOffsets=original.GroundOffsets});aliases.Remove(name);}
                library.Animations=animations.ToArray();EditorUtility.SetDirty(library);AssetDatabase.SaveAssets();
            }
            SkillContentBuilder.Apply();
        }
    }
}
