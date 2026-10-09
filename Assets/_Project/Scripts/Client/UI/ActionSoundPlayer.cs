using UnityEngine;
namespace PokeChess.Client.UI
{
    public enum ActionSoundKind { Purchase, Placement }
    public sealed class ActionSoundPlayer : MonoBehaviour
    {
        private AudioSource source;
        private AudioSource purchaseSource;
        private ActionSoundLibrary library;
        private double nextTime;
        public AudioSource Source=>source;
        public AudioSource PurchaseSource=>purchaseSource;
        public int PlaybackCount {get;private set;}
        public ActionSoundKind LastKind {get;private set;}
        public void Initialize()
        {
            if(library==null)library=Resources.Load<ActionSoundLibrary>("ActionSounds");
            if(source!=null)return;
            source=gameObject.AddComponent<AudioSource>();source.playOnAwake=false;source.loop=false;source.spatialBlend=0;
            purchaseSource=gameObject.AddComponent<AudioSource>();purchaseSource.playOnAwake=false;purchaseSource.loop=false;purchaseSource.spatialBlend=0;purchaseSource.volume=.25f;
        }
        public bool Play(ActionSoundKind kind)
        {
            Initialize();var clip=kind==ActionSoundKind.Purchase?library?.purchase:library?.placement;
            if(clip==null)return false;
            if(kind==ActionSoundKind.Purchase){
                purchaseSource.clip=clip;purchaseSource.PlayOneShot(clip);
                LastKind=kind;PlaybackCount++;return true;
            }
            if(Time.unscaledTimeAsDouble<nextTime||source.isPlaying)return false;
            source.clip=clip;source.volume=.18f;source.Play();
            nextTime=Time.unscaledTimeAsDouble+.08;LastKind=kind;PlaybackCount++;return true;
        }
        private void OnDisable(){if(source!=null)source.Stop();if(purchaseSource!=null)purchaseSource.Stop();nextTime=0;}
    }
    public sealed partial class PlacementSandboxView
    {
        private ActionSoundPlayer actionSoundPlayer;
        private void PlayActionSound(ActionSoundKind kind)
        {
            if(Simulation?.Status==PokeChess.Core.Match.SimulationStatus.Running)return;
            if(actionSoundPlayer==null)actionSoundPlayer=gameObject.AddComponent<ActionSoundPlayer>();
            actionSoundPlayer.Play(kind);
        }
    }
}
