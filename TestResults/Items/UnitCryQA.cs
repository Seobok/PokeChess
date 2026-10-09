using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using PokeChess.Client.UI;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;
public static class UnitCryQA
{
    public static string Run()
    {
        var library=Resources.Load<PokemonCryLibrary>("PokemonCries");
        if(library==null||library.entries.Length!=12||library.entries.Any(e=>e.clip==null||e.clip.samples<=0))throw new Exception("Invalid clip library.");
        var root=new GameObject("UnitCryQA");var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        try{
            var view=root.AddComponent<PlacementSandboxView>();if(view.Match==null)typeof(PlacementSandboxView).GetMethod("Awake",flags).Invoke(view,null);
            var player=root.GetComponent<PokemonCryPlayer>();player.Initialize();player.Source.mute=true;
            if(player.PlaybackCount!=0)throw new Exception("Initial snapshot played cries.");
            view.Player.SetProgress(100,0,3);view.RefreshFromState();
            int slot=Enumerable.Range(0,5).First(i=>!view.Player.Shop.Slots[i].IsEmpty);
            string species=view.Player.Shop.Slots[slot].DefinitionId;
            view.BuySlot(slot);
            if(player.PlaybackCount!=0)throw new Exception("Purchase should be silent.");
            int before=player.PlaybackCount;view.RefreshFromState();view.RefreshFromState();
            if(player.PlaybackCount!=before)throw new Exception("Repeated render duplicated cry.");
            player.ResetPlayback();var unit=view.Player.Units.First();
            var destination=UnitPlacement.OnBoard(new BoardPosition(2,3));
            view.BeginDrag(unit.InstanceId,view.ScreenPoint(unit.Placement));view.EndDrag(view.ScreenPoint(destination));
            if(player.PlaybackCount!=before)throw new Exception("Deployment should be silent.");
            player.ResetPlayback();before=player.PlaybackCount;
            view.BeginDrag(unit.InstanceId,view.ScreenPoint(unit.Placement));view.EndDrag(view.ScreenPoint(UnitPlacement.OnBoard(new BoardPosition(3,3))));
            if(player.PlaybackCount!=before)throw new Exception("Board rearrangement produced cry.");
            view.ResetRankDemo();player.ResetPlayback();before=player.PlaybackCount;view.BuyCopy();
            if(player.PlaybackCount!=before+1||player.LastReason!=PokemonCryReason.RankUp)throw new Exception("Rank-up cry missing.");
            view.ResetSynergyDemo();player.ResetPlayback();before=player.PlaybackCount;
            if(!view.RoundLoop.StartCombat(1))throw new Exception("Battle rejected.");
            for(int tick=0;tick<900&&player.PlaybackCount==before&&view.Match.Phase==MatchPhase.Combat;tick++){view.RoundLoop.Step();view.RefreshFromState();}
            if(player.PlaybackCount==before||player.LastReason!=PokemonCryReason.Skill)throw new Exception("Skill cry missing.");
            before=player.PlaybackCount;view.RefreshFromState();view.RefreshFromState();if(player.PlaybackCount!=before)throw new Exception("Repeated skill render played twice.");
            player.ResetPlayback();
            foreach(var reason in new[]{PokemonCryReason.Purchase,PokemonCryReason.Deploy,PokemonCryReason.Defeat})
                if(player.TryPlay("abra",reason,100))throw new Exception("Disabled cry reason played.");
            if(!player.TryPlay("abra",PokemonCryReason.RankUp,100))throw new Exception("Rank-up cry not played.");
            if(player.TryPlay("bulbasaur",PokemonCryReason.RankUp,100))throw new Exception("Overlapping cries allowed.");
            player.Source.Stop();if(player.TryPlay("unknown",PokemonCryReason.RankUp,120))throw new Exception("Unknown species played cry.");            player.ResetPlayback();if(player.Source.isPlaying||player.Source.loop||player.Source.playOnAwake||player.Source.spatialBlend!=0)throw new Exception("Invalid audio source settings.");
            Directory.CreateDirectory("TestResults/Items/UnitCryQA");File.WriteAllText("TestResults/Items/UnitCryQA/result.json","{\"status\":\"Passed\",\"clips\":12,\"purchaseSilent\":true,\"deploySilent\":true,\"rankUp\":true,\"skillCry\":true,\"renderDeduplication\":true,\"rearrangementSilent\":true,\"overlapBlocked\":true,\"disabledReasonsBlocked\":true,\"missingClipSafe\":true}");
            return "Passed: rank-up and skill cries; purchase, deployment and defeat silent.";
        }finally{UnityEngine.Object.DestroyImmediate(root);}
    }
}


