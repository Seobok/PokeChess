using System;
using System.Linq;
using UnityEngine;
using PokeChess.Client.Data;
using PokeChess.Client.UI;

public static class VerifyWildPokemonSprites
{
    public static string Main()
    {
        var library=PokemonAnimationLibrary.Load();
        for(int round=1;round<=3;round++)
        {
            string species=round<=2?"magikarp":"rattata";
            foreach(string motion in new[]{"Idle","Walk","Attack","Hurt"})
            {
                var animation=library.Find(species,motion);
                if(animation==null||animation.Frames.Any(s=>s==null)||animation.Frames.Length!=animation.FrameCount*animation.Directions)
                    throw new Exception("Missing frames: "+species+"/"+motion);
            }
            var obj=new GameObject("WildSpriteVerification",typeof(RectTransform));
            try
            {
                var view=obj.AddComponent<PokemonSpriteView>();
                view.Initialize("caterpie","wild-"+round+"-player-with-hyphens-0");
                if(view.PokemonId!=species||!view.ShowIdle(0)||!view.Visible||!obj.GetComponent<UnityEngine.UI.Image>().sprite.name.StartsWith(species+"_Idle_"))
                    throw new Exception("Wrong wild sprite for round "+round);
                view.Initialize("caterpie","ordinary-unit");
                if(view.PokemonId!="caterpie")throw new Exception("Non-wild sprite changed.");
            }
            finally{UnityEngine.Object.DestroyImmediate(obj);}
        }
        return "Rounds 1/2 Magikarp and round 3 Rattata render correctly; Idle/Walk/Attack/Hurt frames valid; ordinary units unchanged.";
    }
}
