using System.Linq;
using UnityEngine;
using PokeChess.Core.Match;
namespace PokeChess.Client.UI
{
    public sealed partial class PlacementSandboxView
    {
        private RectTransform finalRoot;
        private UnityEngine.UI.Text finalInfo;
        private UnityEngine.UI.Button newMatchButton;
        public string FinalResultText => finalInfo.text;
        public UnityEngine.UI.Button NewMatchButton => newMatchButton;
        private void BuildFinalResultUI()
        {
            finalRoot=Panel("MatchFinalResult",new Vector2(700,450),new Vector2(0,35));
            finalInfo=Label("FinalStandings",finalRoot,"",17,new Vector2(670,370),new Vector2(0,20));
            newMatchButton=ActionButton("New match",new Vector2(220,32),new Vector2(0,-190),ResetMatchUI);
            newMatchButton.transform.SetParent(finalRoot,false);finalRoot.gameObject.SetActive(false);
        }
        private void RenderFinalResultUI()
        {
            bool finished=Match.Phase==MatchPhase.Finished && Match.FinalResult!=null;
            finalRoot.gameObject.SetActive(finished);
            if(!finished)return;
            var result=Match.FinalResult;var own=result.Standings.Single(p=>p.PlayerId=="p1");
            string reason=result.Reason==MatchEndReason.LastPlayerAlive ? "LAST PLAYER ALIVE" : "ALL PLAYERS ELIMINATED / TIEBREAK WINNER";
            finalInfo.text="MATCH COMPLETE / "+(own.IsWinner ? "VICTORY" : "PLACEMENT #"+own.Placement)+"\nWinner: "+result.WinnerPlayerId+" / Round "+result.EndRound+"\n"+reason+"\n\nFINAL STANDINGS\n"+
                string.Join("\n",result.Standings.Select(p=>"#"+p.Placement+"  "+p.PlayerId+(p.PlayerId=="p1" ? " (YOU)" : "")+"  | HP "+p.HP+" | "+(p.WasAlive ? "WINNER" : p.EliminationReason.ToString().ToUpperInvariant()+" / R"+p.EliminationRound)));
            newMatchButton.interactable=true;finalRoot.SetAsLastSibling();
        }
    }
}
