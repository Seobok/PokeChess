using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using PokeChess.Client.UI;
using PokeChess.Core.Pokemon;
using PokeChess.Core.Match;
public static class VerifyUnitContext {
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    public static string Main() {
        EditorApplication.isPaused=true;
        var v=UnityEngine.Object.FindFirstObjectByType<PlacementSandboxView>();
        int inspected=0;
        foreach(UnitRank rank in Enum.GetValues(typeof(UnitRank)))for(int group=0;group<3;group++) {
            if(group==0)v.ResetRosterDemo(rank);else if(group==1)v.ResetAdvancedRosterDemo(rank);else v.ResetHighCostRosterDemo(rank);
            foreach(var unit in v.Player.Units) {
                v.SelectUnit(unit.InstanceId);
                Check(v.DetailText.Contains("(R"+(int)rank+")")&&v.DetailText.Contains("Items (0): None"),"Rank / item display");
                Check(!v.DetailText.Contains("stage ")&&!v.DetailText.Contains(unit.InstanceId)&&!v.DetailText.Contains("Description unavailable"),"Player description");
                if(unit.DefinitionId=="gastly")Check(v.DetailText.Contains(new[]{"300","450","700"}[(int)rank-1]),"Ranked damage");
                if(unit.DefinitionId=="mankey")Check(v.DetailText.Contains(new[]{"40%","70%","100%"}[(int)rank-1]),"Ranked buff");
                var label=v.GetComponentsInChildren<UnityEngine.UI.Text>().First(t=>t.name=="UnitDetailText");
                Canvas.ForceUpdateCanvases();
                Check(label.preferredHeight<=label.rectTransform.rect.height,"Context overflow: "+unit.DefinitionId+" height="+label.preferredHeight);
                inspected++;
            }
        }
        v.ResetRankDemo();v.SelectUnit("B");v.BuyCopy();
        Check(v.SelectedUnitId=="A"&&v.DetailText.Contains("(R3)")&&v.RankFeedbackText.Contains("R2 → R3"),"Chained merge selection / feedback");
        Check(v.ItemFeedbackText.Contains("1 item")&&v.Player.ItemInventory.Contains("demo-item"),"Merge item return");
        v.RefreshFromState();Check(v.ItemFeedbackText.Contains("1 item"),"Repeated refresh");
        v.ResetRankDemo();v.SelectUnit("B");v.SellSelected();
        Check(v.SelectedUnitId==null&&v.ItemFeedbackText.Contains("1 item"),"Sale selection / return");
        v.ResetRankDemo();v.ResetOpponent();v.SelectUnit("B");v.RoundLoop.StartCombat(v.Match.RoundNumber);v.RoundFlow.RefreshState();v.RefreshFromState();v.ToggleShop();v.BuyCopy();
        Check(v.SelectedUnitId=="B"&&!v.RankFeedbackText.Contains("R3")&&!v.SellButton.interactable,"Deferred / combat sale");
        foreach(var u in v.RoundLoop.Battle.Units.Where(u=>u.TeamId==2))u.SetVitals(0,0);
        for(int i=0;i<3;i++)v.AdvanceRoundTime(1d/30);
        v.AdvanceRoundTime(3.1);v.RefreshFromState();
        Check(v.Match.Phase==MatchPhase.Preparation&&v.SelectedUnitId=="A"&&v.RankFeedbackText.Length>0,"Deferred merge selection");
        v.ResetHighCostRosterDemo(UnitRank.Three);v.SelectUnit("p1-roster-3");v.RoundLoop.StartCombat(v.Match.RoundNumber);v.RoundFlow.RefreshState();v.RefreshFromState();
        Check(v.DetailText.Contains("HP ")&&v.DetailText.Contains("Energy ")&&!v.SellButton.interactable,"Battle vitals");
        var unitId=v.SelectedUnitId;var fighter=v.GetComponentsInChildren<UnityEngine.UI.Button>().First(b=>b.name=="Fighter_"+unitId);
        v.SelectUnit(null);fighter.onClick.Invoke();Check(v.SelectedUnitId==unitId,"Battle click");
        return inspected+" roster/rank contexts passed; chain/deferred selection, item return, sale, combat vitals/click passed.";
    }
}
