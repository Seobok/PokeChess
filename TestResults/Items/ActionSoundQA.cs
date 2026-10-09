using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using PokeChess.Client.UI;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;
public static class ActionSoundQA
{
    public static string Link()
    {
        const string path="Assets/_Project/Resources/ActionSounds.asset";
        var library=AssetDatabase.LoadAssetAtPath<ActionSoundLibrary>(path);
        if(library==null){library=ScriptableObject.CreateInstance<ActionSoundLibrary>();AssetDatabase.CreateAsset(library,path);}
        const string folder="Assets/Casual Game Sounds U6/CasualGameSounds/";
        library.purchase=AssetDatabase.LoadAssetAtPath<AudioClip>(folder+"DM-CGS-07.wav");
        library.placement=AssetDatabase.LoadAssetAtPath<AudioClip>(folder+"DM-CGS-16.wav");
        if(library.purchase==null||library.placement==null)throw new Exception("Missing action sound clips.");
        EditorUtility.SetDirty(library);AssetDatabase.SaveAssets();return "Linked purchase 07 and placement 16.";
    }
    public static string Run()
    {
        var root=new GameObject("ActionSoundQA");var flags=BindingFlags.NonPublic|BindingFlags.Instance;
        try{
            var view=root.AddComponent<PlacementSandboxView>();if(view.Match==null)typeof(PlacementSandboxView).GetMethod("Awake",flags).Invoke(view,null);
            var player=root.AddComponent<ActionSoundPlayer>();player.Initialize();player.Source.mute=true;player.PurchaseSource.mute=true;
            typeof(PlacementSandboxView).GetField("actionSoundPlayer",flags).SetValue(view,player);
            root.GetComponent<PokemonCryPlayer>().Source.mute=true;
            view.Player.SetProgress(100,0,3);view.RefreshFromState();int slot=Enumerable.Range(0,5).First(i=>!view.Player.Shop.Slots[i].IsEmpty);
            view.BuySlot(slot);
            if(player.PlaybackCount!=1||player.LastKind!=ActionSoundKind.Purchase||player.PurchaseSource.clip!=Resources.Load<ActionSoundLibrary>("ActionSounds").purchase)throw new Exception("Purchase sound missing: count="+player.PlaybackCount+", clip="+player.PurchaseSource.clip?.name+", feedback="+view.LastFeedback);
            view.BuySlot(slot);view.RefreshFromState();if(player.PlaybackCount!=1)throw new Exception("Failed buy / refresh played sound.");
            typeof(ActionSoundPlayer).GetMethod("OnDisable",flags).Invoke(player,null);
            var unit=view.Player.Units.First();view.BeginDrag(unit.InstanceId,view.ScreenPoint(unit.Placement));view.EndDrag(view.ScreenPoint(UnitPlacement.OnBoard(new BoardPosition(2,3))));
            if(player.PlaybackCount!=2||player.LastKind!=ActionSoundKind.Placement||player.Source.clip!=Resources.Load<ActionSoundLibrary>("ActionSounds").placement)throw new Exception("Placement sound missing.");
            typeof(ActionSoundPlayer).GetMethod("OnDisable",flags).Invoke(player,null);
            view.BeginDrag(unit.InstanceId,view.ScreenPoint(unit.Placement));view.EndDrag(view.ScreenPoint(unit.Placement));
            view.BeginDrag(unit.InstanceId,view.ScreenPoint(unit.Placement));view.EndDrag(new Vector2(5,5));
            if(player.PlaybackCount!=2)throw new Exception("No-op / cancelled drop played sound.");
            view.BeginDrag(unit.InstanceId,view.ScreenPoint(unit.Placement));view.EndDrag(view.ScreenPoint(UnitPlacement.OnBoard(new BoardPosition(3,3))));
            if(player.PlaybackCount!=3)throw new Exception("Board rearrangement sound missing.");
            if(player.Play(ActionSoundKind.Placement))throw new Exception("Overlapping sound allowed.");
            int count=player.PlaybackCount;
            foreach(var offer in view.Player.Shop.Slots.Where(s=>!s.IsEmpty).ToArray()){
                int index=Enumerable.Range(0,5).First(i=>view.Player.Shop.Slots[i]==offer);view.BuySlot(index);count++;
                if(player.PlaybackCount!=count)throw new Exception("Rapid successful purchase sound was dropped.");
            }
            for(int i=0;i<5;i++)if(!player.Play(ActionSoundKind.Purchase))throw new Exception("Rapid purchase playback blocked.");
            Directory.CreateDirectory("TestResults/Items/ActionSoundQA");File.WriteAllText("TestResults/Items/ActionSoundQA/result.json","{\"status\":\"Passed\",\"purchase\":true,\"rapidPurchase\":true,\"placement16\":true,\"failedBuySilent\":true,\"cancelSilent\":true,\"noOpSilent\":true,\"overlapBlocked\":true}");
            return "Passed: rapid purchases all play; placement, failed/no-op/cancel behavior preserved.";
        }finally{UnityEngine.Object.DestroyImmediate(root);}
    }
}


