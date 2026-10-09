using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using PokeChess.Client.UI;
using PokeChess.Core.Match;
public static class CombatAudioQA
{
    public static string Link()
    {
        const string path="Assets/_Project/Resources/CombatSounds.asset";
        var library=AssetDatabase.LoadAssetAtPath<CombatSoundLibrary>(path);
        if(library==null){library=ScriptableObject.CreateInstance<CombatSoundLibrary>();AssetDatabase.CreateAsset(library,path);}
        string[] fields={"punch","kick","heavy","arrow","swingOne","swingTwo","whip","fire","heal"};
        string[] names={"小パンチ","小キック","打撃1","弓矢が刺さる","パンチの衣擦れ1","パンチの衣擦れ2","鞭を振り回す1","火炎魔法1","回復魔法1"};
        string result="";
        for(int i=0;i<fields.Length;i++){
            var clip=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/Audio/Attack/"+names[i]+".mp3");
            if(clip==null)throw new Exception("Missing combat clip: "+names[i]);
            typeof(CombatSoundLibrary).GetField(fields[i]).SetValue(library,clip);result+=fields[i]+"="+clip.length.ToString("0.00")+"s; ";
        }
        EditorUtility.SetDirty(library);AssetDatabase.SaveAssets();return result;
    }
    public static string Run()
    {
        var root=new GameObject("CombatAudioQA");var flags=BindingFlags.NonPublic|BindingFlags.Instance;
        try{
            var view=root.AddComponent<PlacementSandboxView>();if(view.Match==null)typeof(PlacementSandboxView).GetMethod("Awake",flags).Invoke(view,null);
            var player=root.GetComponent<CombatSoundPlayer>();player.Mute=true;
            root.GetComponent<PokemonCryPlayer>().Source.mute=true;
            var clips=Resources.Load<CombatSoundLibrary>("CombatSounds");if(clips==null)throw new Exception("Missing library.");
            foreach(var field in typeof(CombatSoundLibrary).GetFields())if(field.FieldType==typeof(AudioClip)&&field.GetValue(clips)==null)throw new Exception("Unlinked clip.");
            if(!player.Play(clips.punch,CombatSoundKind.Impact,100))throw new Exception("Impact rejected.");
            if(player.Play(clips.kick,CombatSoundKind.Impact,100.01))throw new Exception("Spam gate failed.");
            if(!player.Play(clips.fire,CombatSoundKind.Skill,101))throw new Exception("Skill rejected.");
            if(player.Play(clips.fire,CombatSoundKind.Skill,101.2))throw new Exception("Skill cooldown failed.");
            var skillSound=typeof(PlacementSandboxView).GetMethod("SkillSound",flags);
            player.ResetPlayback();skillSound.Invoke(view,new object[]{"charmander","ember-burst"});if(player.LastClip!=clips.fire)throw new Exception("Fire mapping failed.");
            player.ResetPlayback();skillSound.Invoke(view,new object[]{"chansey",PokeChess.Core.Pokemon.PrototypeRoster.CreateCatalog().Get("chansey").SkillId});if(player.LastClip!=clips.heal)throw new Exception("Heal mapping failed.");
            player.ResetPlayback();player.Play(clips.punch,CombatSoundKind.Impact,100);player.Play(clips.swingOne,CombatSoundKind.Swing,100.11);
            if(!player.Play(clips.heal,CombatSoundKind.Heal,100.12))throw new Exception("Reserved skill voice unavailable.");
            player.ResetPlayback();view.ResetSynergyDemo();int initial=player.PlaybackCount;
            if(!view.RoundLoop.StartCombat(1))throw new Exception("Battle rejected.");
            for(int i=0;i<500&&player.PlaybackCount==initial&&view.Match.Phase==MatchPhase.Combat;i++){view.RoundLoop.Step();view.RefreshFromState();}
            if(player.PlaybackCount==initial)throw new Exception("Combat events did not play SFX.");
            initial=player.PlaybackCount;view.RefreshFromState();view.RefreshFromState();if(player.PlaybackCount!=initial)throw new Exception("Repeated render replayed SFX.");
            foreach(var source in root.GetComponents<AudioSource>())if(source.playOnAwake||source.loop||source.spatialBlend!=0)throw new Exception("Unexpected source setup.");
            Directory.CreateDirectory("TestResults/Items/CombatAudioQA");File.WriteAllText("TestResults/Items/CombatAudioQA/result.json","{\"status\":\"Passed\",\"clips\":9,\"battleEvents\":true,\"spamGate\":true,\"skillCooldown\":true,\"renderDeduplication\":true}");
            return "Passed: nine clips, battle SFX, cooldowns and no duplicate render playback.";
        }finally{UnityEngine.Object.DestroyImmediate(root);}
    }
}
