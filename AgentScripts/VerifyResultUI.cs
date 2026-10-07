using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using PokeChess.Client.UI;
using PokeChess.Core.Match;
public static class VerifyResultUI {
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static void Tick(PlacementSandboxView v,int n){for(int i=0;i<n;i++)v.AdvanceRoundTime(1d/30);v.RefreshFromState();}
    static void EndPairs(PlacementSandboxView v){foreach(var p in v.RoundLoop.Battles.Where(p=>!p.IsComplete))foreach(var u in p.Battle.Units.Where(u=>u.TeamId==2))u.SetVitals(0,0);Tick(v,2);}
    public static string Main() {
        Check(Application.isPlaying,"Play mode");EditorApplication.isPaused=true;
        var v=UnityEngine.Object.FindFirstObjectByType<PlacementSandboxView>();
        v.ResetPairingDemo(8);v.RoundLoop.StartCombat(v.Match.RoundNumber);v.RoundFlow.RefreshState();v.RefreshFromState();
        var own=v.RoundLoop.GetBattleFor("p1");int ownTeam=own.Pairing.PlayerOneId=="p1"?1:2;
        foreach(var u in own.Battle.Units.Where(u=>u.TeamId!=ownTeam))u.SetVitals(0,0);Tick(v,2);
        Check(v.Match.Phase==MatchPhase.Combat&&v.RoundResultVisible&&v.ResultHeadline.Contains("VICTORY"),"Early own outcome");
        Check(v.ResultHPText.Contains("pending")&&v.ResultRewardText.Contains("pending"),"No premature settlement");
        EndPairs(v);Check(v.Match.Phase==MatchPhase.Result&&v.RoundResultVisible,"Settled card");
        var damage=v.RoundLoop.LastResult.Damage["p1"];var income=v.RoundLoop.LastResult.Income["p1"];
        Check(v.ResultHPText.Contains(damage.HPBefore+" → "+damage.HPAfter)&&v.ResultRewardText.Contains("+"+income.TotalIncome+"G"),"Actual HP / rewards");
        double remaining=v.RoundFlow.RemainingSeconds;v.ResultDetailsButton.onClick.Invoke();v.ResultDetailsButton.onClick.Invoke();
        Check(v.RoundFlow.RemainingSeconds==remaining,"Details changed timer");
        v.AdvanceRoundTime(3.1);Check(!v.RoundResultVisible&&v.Match.Phase==MatchPhase.Preparation,"Result closes next preparation");
        v.SurrenderLocalPlayer();Check(v.EliminationVisible&&!v.RoundResultVisible&&!v.FinalResultVisible&&v.EliminationText.Contains("PLACEMENT #8"),"Own elimination priority");
        v.ContinueWatchingButton.onClick.Invoke();v.RefreshFromState();Check(!v.EliminationVisible&&v.HudProfileText("p1").Contains("SURRENDER"),"Dismiss once / persistent HUD");
        foreach(var p in v.Match.Players.Where(p=>p.PlayerId!="p2"&&!p.IsEliminated).ToArray())v.RoundLoop.Surrender(p.PlayerId,v.Match.RoundNumber,v.Match.Phase);
        v.RefreshFromState();Check(v.FinalResultVisible&&!v.EliminationVisible&&!v.RoundResultVisible&&v.FinalResultText.Contains("PLACEMENT #8"),"Final priority");
        Check(v.Match.FinalResult.Standings.Count==8,"Eight standings");
        v.NewMatchButton.onClick.Invoke();Check(!v.FinalResultVisible&&!v.EliminationVisible&&!v.RoundResultVisible&&v.Player.HP==60,"New match reset");
        v.ResetRosterDemo();v.RoundLoop.StartCombat(v.Match.RoundNumber);v.RoundFlow.RefreshState();
        foreach(var u in v.RoundLoop.Battle.Units.Where(u=>u.OwnerPlayerId=="p1"))u.SetVitals(0,0);Tick(v,2);
        Check(v.ResultHeadline.Contains("DEFEAT")&&v.RoundResultVisible&&v.ResultHPText.Contains("−"),"Loss / HP feedback");
        v.ResetRosterDemo();v.RoundLoop.StartCombat(v.Match.RoundNumber);v.RoundFlow.RefreshState();
        foreach(var u in v.RoundLoop.Battle.Units)u.SetVitals(0,0);Tick(v,2);
        Check(v.ResultHeadline.Contains("DRAW")&&v.RoundResultVisible&&v.ResultHPText.Contains("No HP lost"),"Draw / zero loss");
        v.ResetPairingDemo(4);v.Player.SetHP(1);v.RoundLoop.StartCombat(v.Match.RoundNumber);v.RoundFlow.RefreshState();
        own=v.RoundLoop.GetBattleFor("p1");ownTeam=own.Pairing.PlayerOneId=="p1"?1:2;
        foreach(var u in own.Battle.Units.Where(u=>u.TeamId==ownTeam))u.SetVitals(0,0);Tick(v,2);EndPairs(v);
        Check(v.EliminationVisible&&v.EliminationText.Contains("HP depleted"),"Damage elimination");
        Check(!v.ShopExpanded&&!v.SellButton.interactable&&!v.RerollButton.interactable,"Eliminated inputs / shop");
        v.ResetRosterDemo();foreach(var p in v.Match.Players)p.SetHP(1);v.RoundLoop.StartCombat(v.Match.RoundNumber);v.RoundFlow.RefreshState();
        foreach(var u in v.RoundLoop.Battle.Units)u.SetShield(1000000);Tick(v,1352);
        Check(v.FinalResultVisible&&v.Match.FinalResult.Reason==MatchEndReason.AllPlayersEliminated,"All eliminated final");
        Check(v.FinalResultText.Contains("WINNER / DAMAGE")&&v.Match.FinalResult.Standings.All(p=>!p.WasAlive),"Winner separate from survival");
        foreach(var phase in new[]{MatchPhase.Combat,MatchPhase.Result}) {
            v.ResetPairingDemo(4);v.RoundLoop.StartCombat(v.Match.RoundNumber);v.RoundFlow.RefreshState();
            if(phase==MatchPhase.Result)EndPairs(v);
            v.RefreshFromState();v.SurrenderLocalPlayer();
            Check(v.EliminationVisible&&!v.RoundResultVisible&&v.EliminationText.Contains("Surrender"),"Surrender UI in "+phase);
        }
        v.ResetPairingDemo(8);foreach(var p in v.Match.Players.Where(p=>p.PlayerId!="p1").ToArray())v.RoundLoop.Surrender(p.PlayerId,v.Match.RoundNumber,v.Match.Phase);
        v.RefreshFromState();Check(v.FinalResultVisible&&v.FinalResultText.Contains("VICTORY"),"Own victory");
        Canvas.ForceUpdateCanvases();EditorApplication.Step();
        return "Result UI passed: early outcome / pending settlement, HP / rewards, detail timer, elimination / dismissal, final 8 standings / priority, damage elimination, all-dead winner, new match reset.";
    }
}
