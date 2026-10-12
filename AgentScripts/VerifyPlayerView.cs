using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using PokeChess.Client.UI;
using PokeChess.Core.Match;
public static class VerifyPlayerView {
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    public static string Main(){
        EditorApplication.isPaused=true;
        var v=UnityEngine.Object.FindFirstObjectByType<PlacementSandboxView>();
        v.ResetPairingDemo(8);v.AdvanceRoundTime(0);
        var root=v.GetComponentsInChildren<RectTransform>(true).First(t=>t.name=="PlayerSidebar");
        var row=root.Find("Profile_p2");
        Check(row.GetComponent<UnityEngine.UI.Image>().raycastTarget,"Profile raycast");
        row.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
        Check(v.ViewedPlayerId=="p2","Profile click");
        var unit=v.Match.GetPlayer("p2").Units.First();
        var token=v.GetComponentsInChildren<RectTransform>(true).First(t=>t.name=="Unit_"+unit.InstanceId||t.name==unit.InstanceId||t.name=="Token_"+unit.InstanceId);
        Check(token.gameObject.activeSelf,"Observed roster visible");
        v.BeginDrag(unit.InstanceId,Vector2.zero);Check(!v.IsDragging,"Read only");
        v.RequestPlayerView("missing");Check(v.ViewedPlayerId=="p2","Invalid selection");
        v.RequestPlayerView("p1");Check(v.ViewedPlayerId=="p1","Return self");
        typeof(MatchRules).GetField("<EnableWildRounds>k__BackingField",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(v.Match.Rules,false);
        v.AdvanceRoundTime(0);v.AdvanceRoundTime(30);
        v.RequestPlayerView("p3");Check(v.HudNoticeText.Contains("p3"),"Watching notice");
        var combat=v.GetComponentsInChildren<RectTransform>(true).First(t=>t.name=="CombatView");
        Check(combat.gameObject.activeSelf,"Observed combat visible");
        v.RequestPlayerView("p2");
        v.ResetPairingDemo(4);Check(v.ViewedPlayerId=="p1","Reset view");
        return "PASS: profile click, roster, read only, invalid target, self return, combat selection, reset.";
    }
}




