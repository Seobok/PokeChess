using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using PokeChess.Content;
using PokeChess.Client.Data;

namespace PokeChess.Editor
{
    public static class RosterContentValidation
    {
        [MenuItem("PokeChess/Content/Validate Game Roster")]
        public static void Validate()
        {
            GameContent.Reset();var roster=GameContent.Roster;roster.Validate();
            var library=PokemonAnimationLibrary.Load();
            if(library==null)throw new InvalidOperationException("Missing PokemonAnimations");
            foreach(var p in roster.Pokemon)
                foreach(var name in new[]{p.Visual.Idle,p.Visual.Walk,p.Visual.Attack,p.Visual.Hurt,p.Visual.Skill})
                {
                    var a=library.Animations.FirstOrDefault(x=>x.PokemonId==p.Visual.AnimationId&&x.Name==name);
                    if(a==null||a.Directions!=1&&a.Directions!=8||a.FrameCount<1||a.Durations==null||
                        a.Durations.Length!=a.FrameCount||a.Durations.Any(d=>d<=0)||a.Frames==null||
                        a.Frames.Length!=a.FrameCount*a.Directions||a.Frames.Any(f=>f==null)||
                        a.GroundOffsets==null||a.GroundOffsets.Length!=a.Frames.Length)
                        throw new InvalidOperationException(p.Id+": invalid animation "+p.Visual.AnimationId+"/"+name);
                }
            Debug.Log("Validated "+roster.Pokemon.Length+" Pokemon forms, "+roster.Skills.Length+" skills and all visual references.");
        }
    }
}
