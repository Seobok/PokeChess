using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using PokeChess.Client.UI;
using PokeChess.Core.Match;
public static class VerifyMatchHud {
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static void Tick(PlacementSandboxView v,int n){for(int i=0;i<n;i++)v.AdvanceRoundTime(1d/30);}
    public static string Main() {
        Check(Application.isPlaying,"Enter Play mode.");EditorApplication.isPaused=true;
        var v=UnityEngine.Object.FindFirstObjectByType<PlacementSandboxView>();
        v.ResetPairingDemo(8);v.AdvanceRoundTime(0);
        Check(v.HudProfileCount==8,"Eight profiles missing.");Check(v.HudTimerKind=="Preparation","Preparation tone.");
        Check(Math.Abs(v.HudTimerProgress-1)<.01,"Initial timer.");
        v.AdvanceRoundTime(10);Check(v.HudTimerProgress>.65&&v.HudTimerProgress<.68,"Preparation progress.");
        v.AdvanceRoundTime(16);Check(v.HudTimerProgress>.12&&v.HudTimerProgress<.15,"Preparation warning boundary.");
        v.AdvanceRoundTime(4);Check(v.Match.Phase==MatchPhase.Combat,"Automatic preparation boundary.");
        foreach(var pair in v.RoundLoop.Battles)foreach(var u in pair.Battle.Units)u.SetShield(1000000);
        Tick(v,450);Check(v.HudTimerKind=="Combat"&&Math.Abs(v.HudTimerProgress-.5)<.01,"Combat half timer.");
        Tick(v,452);Check(v.HudTimerKind=="Overtime"&&v.HudTimerProgress>.98,"Overtime reset.");
        var own=v.RoundLoop.GetBattleFor("p1");
        foreach(var u in own.Battle.Units.Where(u=>u.TeamId==2))u.SetVitals(0,0);
        Tick(v,2);Check(own.IsComplete,"Own battle finish.");
        Check(v.Match.Phase==MatchPhase.Combat&&v.HudNoticeText.Contains("other battles"),"Wait for other pairs.");
        foreach(var pair in v.RoundLoop.Battles.Where(p=>!p.IsComplete))
            foreach(var u in pair.Battle.Units.Where(u=>u.TeamId==2))u.SetVitals(0,0);
        Tick(v,2);Check(v.Match.Phase==MatchPhase.Result&&v.HudTimerKind=="Result","Result tone.");
        Check(v.Match.Players.Any(p=>p.HP<60),"Round HP damage.");
        v.AdvanceRoundTime(1.5);Check(Math.Abs(v.HudTimerProgress-.5)<.02,"Result half timer.");
        v.AdvanceRoundTime(2);Check(v.Match.Phase==MatchPhase.Preparation&&v.HudTimerProgress>.99,"Next round reset.");
        v.SurrenderLocalPlayer();v.AdvanceRoundTime(0);
        Check(v.HudProfileText("p1").Contains("SURRENDER"),"Own surrender profile.");
        Check(v.Match.Phase!=MatchPhase.Finished,"Own elimination keeps match active.");
        foreach(var p in v.Match.Players.Where(p=>!p.IsEliminated&&p.PlayerId!="p2").ToArray())
            v.RoundLoop.Surrender(p.PlayerId,v.Match.RoundNumber,v.Match.Phase);
        v.AdvanceRoundTime(0);
        Check(v.Match.Phase==MatchPhase.Finished,"Final winner.");
        Check(v.HudProfileText("p2").Contains("WINNER"),"Winner profile.");
        Check(v.HudNoticeText=="MATCH COMPLETE","Final notice.");
        v.ResetPairingDemo(8);v.AdvanceRoundTime(0);
        Check(v.HudProfileCount==8&&v.HudProfileText("p1").Contains("60 HP")&&!v.HudProfileText("p1").Contains("SURRENDER"),"Reset cached profile.");
        Canvas.ForceUpdateCanvases();EditorApplication.Step();
        return "HUD passed: 8 profiles, preparation progress / warning, combat / overtime, early battle wait, result / HP damage, next round, self surrender / continued match, final winner, reset.";
    }
}
