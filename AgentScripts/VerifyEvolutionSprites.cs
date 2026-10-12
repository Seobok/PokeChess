using System;
using System.Linq;
using UnityEngine;
using PokeChess.Client.Data;
using PokeChess.Client.UI;
using PokeChess.Core.Battle;
using PokeChess.Core.Match;

public static class VerifyEvolutionSprites
{
    public static string Main()
    {
        var library=PokemonAnimationLibrary.Load();
        var species=library.Animations.Select(a=>a.PokemonId).Distinct().ToArray();
        if(species.Length!=31)throw new Exception("Expected 31 species, found "+species.Length);
        foreach(var a in library.Animations)
        {
            if(a.Frames.Length!=a.FrameCount*a.Directions||a.GroundOffsets.Length!=a.Frames.Length||a.Frames.Any(s=>s==null))
                throw new Exception("Invalid frames: "+a.PokemonId+"/"+a.Name);
            foreach(var s in a.Frames)
                if(s.texture.filterMode!=FilterMode.Point||s.texture.mipmapCount!=1)throw new Exception("Invalid import: "+s.name);
        }
        foreach(var id in species)
        {
            var go=new GameObject("SpriteCheck",typeof(RectTransform));
            try
            {
                var view=go.AddComponent<PokemonSpriteView>();view.Initialize(id);
                if(!view.ShowIdle(0)||!go.GetComponent<UnityEngine.UI.Image>().sprite.name.StartsWith(id+"_"))
                    throw new Exception("Idle mismatch: "+id);
                if(go.GetComponent<UnityEngine.UI.Image>().color!=Color.white)throw new Exception("Placeholder tint: "+id);
                foreach(var state in new[]{CombatActionState.Moving,CombatActionState.Attacking,CombatActionState.Casting})
                for(int d=0;d<8;d++)
                {
                    var unit=new CombatUnitView{hp=100,action=state,startTick=0,effectTick=6,endTick=24};
                    view.ShowPlayback(unit,6,30,new Vector2((float)Math.Sin(d*Math.PI/4),-(float)Math.Cos(d*Math.PI/4)));
                    var sprite=go.GetComponent<UnityEngine.UI.Image>().sprite;
                    var clip=library.Animations.First(a=>a.Frames.Contains(sprite));
                    if(!view.Visible||!sprite.name.StartsWith(id+"_")||!sprite.name.Contains("_d"+(clip.Directions==1?0:d)+"_"))
                        throw new Exception("State/direction mismatch: "+id+"/"+state+"/"+d);
                    if((id=="wartortle"||id=="blastoise")&&state==CombatActionState.Attacking&&!sprite.name.Contains("_Shoot_"))
                        throw new Exception("Ranged evolution attack mismatch: "+id);
                }
            }
            finally{UnityEngine.Object.DestroyImmediate(go);}
        }
        return species.Length+" species, "+library.Animations.Length+" animations; all frames/imports and idle/movement/attack/cast in 8 directions passed.";
    }
}
