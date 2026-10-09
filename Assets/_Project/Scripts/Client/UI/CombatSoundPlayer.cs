using UnityEngine;
namespace PokeChess.Client.UI
{
    public enum CombatSoundKind { Impact, Swing, Skill, Heal }
    public sealed class CombatSoundPlayer : MonoBehaviour
    {
        private readonly AudioSource[] voices=new AudioSource[3];
        private readonly double[] endTimes=new double[3];
        private readonly double[] categoryTimes=new double[4];
        private double nextTime;
        public int PlaybackCount {get;private set;}
        public AudioClip LastClip {get;private set;}
        public bool Mute {get;set;}
        public bool Play(AudioClip clip,CombatSoundKind kind,double now,float pitch=1)
        {
            bool important=kind==CombatSoundKind.Skill||kind==CombatSoundKind.Heal;
            if(clip==null||(!important&&now<nextTime)||now<categoryTimes[(int)kind])return false;
            // Keep one voice available for skills and support effects.
            int index=-1;for(int i=0;i<(important?3:2);i++)if(now>=endTimes[i]){index=i;break;}
            // Never cut an existing effect off to squeeze in another basic attack.
            if(index<0)return false;
            if(voices[index]==null){voices[index]=gameObject.AddComponent<AudioSource>();voices[index].playOnAwake=false;voices[index].loop=false;voices[index].spatialBlend=0;}
            var source=voices[index];source.mute=Mute;source.clip=clip;source.pitch=Mathf.Clamp(pitch,.85f,1.15f);
            source.volume=kind==CombatSoundKind.Impact?.13f:kind==CombatSoundKind.Swing?.08f:kind==CombatSoundKind.Heal?.22f:.2f;
            source.Play();endTimes[index]=now+clip.length/source.pitch;
            nextTime=now+.1;categoryTimes[(int)kind]=now+(kind==CombatSoundKind.Impact?.22:kind==CombatSoundKind.Swing?.4:.65);
            PlaybackCount++;LastClip=clip;return true;
        }
        public void ResetPlayback()
        {for(int i=0;i<3;i++){if(voices[i]!=null)voices[i].Stop();endTimes[i]=0;}System.Array.Clear(categoryTimes,0,4);nextTime=0;}
        private void OnDisable(){ResetPlayback();}
    }
}
