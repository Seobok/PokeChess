using System;
using UnityEngine;

namespace PokeChess.Client.Data
{
    [Serializable]
    public sealed class PokemonSpriteAnimation
    {
        public string PokemonId, Name;
        public int Directions, FrameCount, HitFrame = -1;
        public int[] Durations;
        // Direction-major, top-to-bottom source rows. Ground offset is in source pixels.
        public Sprite[] Frames;
        public Vector2[] GroundOffsets;
        public double DurationSeconds
        {
            get { int total=0; foreach(int d in Durations)total+=d; return total/60d; }
        }
        public double HitSeconds
        {
            get { int total=0; for(int i=0;i<Math.Max(0,HitFrame);i++)total+=Durations[i]; return total/60d; }
        }
        public int FrameAt(double seconds,bool loop)
        {
            double length=DurationSeconds;
            if(loop)seconds=((seconds%length)+length)%length;
            else seconds=Math.Max(0,Math.Min(seconds,length));
            double end=0;
            for(int i=0;i<FrameCount;i++) { end+=Durations[i]/60d; if(seconds<end)return i; }
            return FrameCount-1;
        }
        // Align the authored hit pose with the simulation's effect tick, preserving both phases.
        public double ActionTime(double elapsed,double effect,double end)
        {
            double hit=HitFrame>=0?HitSeconds:DurationSeconds*.5;
            if(elapsed<effect && effect>0)return Math.Max(0,elapsed/effect*hit);
            return hit+Math.Max(0,Math.Min(1,(elapsed-effect)/Math.Max(.000001,end-effect)))*(DurationSeconds-hit);
        }
    }
    public sealed class PokemonAnimationLibrary : ScriptableObject
    {
        public PokemonSpriteAnimation[] Animations=Array.Empty<PokemonSpriteAnimation>();
        public PokemonSpriteAnimation Find(string pokemon,string animation)
            => Array.Find(Animations,a=>a.PokemonId==pokemon&&a.Name==animation);
        public static PokemonAnimationLibrary Load()=>Resources.Load<PokemonAnimationLibrary>("PokemonAnimations");
    }
}
