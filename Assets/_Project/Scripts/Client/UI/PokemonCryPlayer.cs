using System.Collections.Generic;
using UnityEngine;
namespace PokeChess.Client.UI
{
    public enum PokemonCryReason { Purchase, Deploy, RankUp, Skill, Defeat }
    [DisallowMultipleComponent]
    public sealed class PokemonCryPlayer : MonoBehaviour
    {
        private PokemonCryLibrary library;
        private AudioSource source;
        private double nextPlayback;
        private readonly Dictionary<string,double> nextSpecies=new Dictionary<string,double>();
        public AudioSource Source=>source;
        public string LastPokemonId {get;private set;}
        public PokemonCryReason LastReason {get;private set;}
        public int PlaybackCount {get;private set;}
        public void Initialize()
        {
            if(library==null)library=Resources.Load<PokemonCryLibrary>("PokemonCries");
            if(source!=null)return;
            source=gameObject.AddComponent<AudioSource>();source.playOnAwake=false;source.loop=false;source.spatialBlend=0;source.volume=.65f;
        }
        public bool Play(string id,PokemonCryReason reason)=>TryPlay(id,reason,Time.unscaledTimeAsDouble);
        public bool TryPlay(string id,PokemonCryReason reason,double now)
        {
            if(reason!=PokemonCryReason.RankUp&&reason!=PokemonCryReason.Skill)return false;
            Initialize();var clip=library?.Get(id);if(clip==null||now<nextPlayback||source.isPlaying)return false;
            string key=id+":"+reason;
            if(nextSpecies.TryGetValue(key,out var next)&&now<next)return false;
            source.clip=clip;source.pitch=1f;source.volume=reason==PokemonCryReason.Skill?.4f:.65f;
            source.Play();nextPlayback=now+System.Math.Max(.35,clip.length/source.pitch);
            nextSpecies[key]=now+1;
            LastPokemonId=id;LastReason=reason;PlaybackCount++;return true;
        }
        public void ResetPlayback()
        {if(source!=null)source.Stop();nextPlayback=0;nextSpecies.Clear();}
        private void OnDisable(){ResetPlayback();}
    }
}
