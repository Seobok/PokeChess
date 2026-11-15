using System.Linq;
using UnityEngine;
using PokeChess.Core.Match;
namespace PokeChess.Client.UI
{
    public sealed partial class PlacementSandboxView
    {
        private RectTransform finalRoot;
        private RectTransform finalBackdrop, finalPresentation;
        private UnityEngine.UI.Text finalInfo;
        private UnityEngine.UI.Button newMatchButton;
        private UnityEngine.UI.Text finalTitle,finalSummary;
        private readonly UnityEngine.UI.Text[] finalRows=new UnityEngine.UI.Text[8];
        private readonly UnityEngine.UI.Text[,] finalCells=new UnityEngine.UI.Text[8,4];
        private readonly UnityEngine.UI.Image[] finalRowBackgrounds=new UnityEngine.UI.Image[8];
        public string FinalResultText => finalInfo.text;
        public UnityEngine.UI.Button NewMatchButton => newMatchButton;
        private void BuildFinalResultUI()
        {
            finalPresentation = UiPrefab.Instantiate("FinalResult", canvasRect);
            finalBackdrop = UiPrefab.Get<RectTransform>(finalPresentation, "FinalBackdrop");
            finalRoot = UiPrefab.Get<RectTransform>(finalPresentation, "MatchFinalResult");
            finalInfo = UiPrefab.Get<UnityEngine.UI.Text>(finalRoot, "FinalStandings");
            finalTitle = UiPrefab.Get<UnityEngine.UI.Text>(finalRoot, "FinalTitle");
            finalSummary = UiPrefab.Get<UnityEngine.UI.Text>(finalRoot, "FinalSummary");
            for (int i = 0; i < finalRows.Length; i++)
            {
                var image = finalRowBackgrounds[i] = UiPrefab.Get<UnityEngine.UI.Image>(finalRoot, $"FinalRow_{i}");
                finalRows[i] = UiPrefab.Get<UnityEngine.UI.Text>(image.transform, "Standing");
                for (int column = 0; column < finalCells.GetLength(1); column++)
                    finalCells[i, column] = UiPrefab.Get<UnityEngine.UI.Text>(image.transform, $"StandingCell_{column}");
            }
            newMatchButton = UiPrefab.Bind(finalRoot, "New match", ResetMatchUI);
        }

        private void RenderFinalResultUI()
        {
            bool finished=Match.Phase==MatchPhase.Finished && Match.FinalResult!=null;
            finalBackdrop.gameObject.SetActive(finished);
            finalRoot.gameObject.SetActive(finished);
            if(!finished)return;
            var result=Match.FinalResult;var own=result.Standings.Single(p=>p.PlayerId=="p1");
            string reason=result.Reason==MatchEndReason.LastPlayerAlive ? "LAST PLAYER ALIVE" : "ALL PLAYERS ELIMINATED / TIEBREAK WINNER";
            finalInfo.text="MATCH COMPLETE / "+(own.IsWinner ? "VICTORY" : "PLACEMENT #"+own.Placement)+"\nWinner: "+result.WinnerPlayerId+" / Round "+result.EndRound+"\n"+reason+"\n\nFINAL STANDINGS\n"+
                string.Join("\n",result.Standings.Select(p=>"#"+p.Placement+"  "+p.PlayerId+(p.PlayerId=="p1" ? " (YOU)" : "")+"  | HP "+p.HP+" | "+(p.IsWinner ? "WINNER / " : "")+(p.WasAlive?"ALIVE":p.EliminationReason.ToString().ToUpperInvariant()+" / R"+p.EliminationRound)));
            finalTitle.text=own.IsWinner?"VICTORY · CHAMPION":"MATCH COMPLETE · PLACEMENT #"+own.Placement;
            finalTitle.color=own.IsWinner?new Color(1,.85f,.4f):Color.white;
            finalSummary.text="Winner: "+result.WinnerPlayerId+" · End round "+result.EndRound+"\n"+
                (result.Reason==MatchEndReason.LastPlayerAlive?"Last player alive":"All players eliminated · winner decided by placement rules");
            for(int i=0;i<finalRows.Length;i++) {
                bool active=i<result.Standings.Count;finalRowBackgrounds[i].gameObject.SetActive(active);if(!active)continue;
                var p=result.Standings[i];
                finalRowBackgrounds[i].color=p.PlayerId=="p1"?new Color(.13f,.32f,.43f):p.IsWinner?new Color(.32f,.26f,.12f):new Color(.1f,.17f,.24f);
                finalRows[i].text="#"+p.Placement+"     "+p.PlayerId+(p.PlayerId=="p1"?" (YOU)":"")+"       HP "+p.HP+"       "+
                    (p.IsWinner?"WINNER · ":"")+(p.WasAlive?"Alive":(p.EliminationReason==EliminationReason.Surrender?"Surrender":"HP depleted")+" / R"+p.EliminationRound);
                finalRows[i].color=p.IsWinner?new Color(1,.88f,.5f):Color.white;
                finalCells[i,0].text="#"+p.Placement;
                finalCells[i,1].text=p.PlayerId+(p.PlayerId=="p1"?" (YOU)":"");
                finalCells[i,2].text=p.HP.ToString();
                finalCells[i,3].text=(p.IsWinner?"WINNER · ":"")+(p.WasAlive?"Alive":(p.EliminationReason==EliminationReason.Surrender?"Surrender":"HP depleted")+" / R"+p.EliminationRound);
                for(int c=0;c<4;c++)finalCells[i,c].color=finalRows[i].color;
            }
            newMatchButton.interactable=true;finalPresentation.SetAsLastSibling();
        }
    }
}
