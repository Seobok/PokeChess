using System.Linq;
using UnityEngine;
using PokeChess.Core.Match;
namespace PokeChess.Client.UI
{
    public sealed partial class PlacementSandboxView
    {
        private RectTransform finalRoot;
        private RectTransform finalBackdrop;
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
            finalBackdrop=Panel("FinalBackdrop",new Vector2(1280,720),Vector2.zero);
            finalBackdrop.anchorMin=Vector2.zero;finalBackdrop.anchorMax=Vector2.one;finalBackdrop.sizeDelta=Vector2.zero;
            finalBackdrop.GetComponent<UnityEngine.UI.Image>().color=new Color(.02f,.035f,.06f,.94f);
            finalBackdrop.gameObject.SetActive(false);
            finalRoot=Panel("MatchFinalResult",new Vector2(700,450),new Vector2(0,35));
            finalInfo=Label("FinalStandings",finalRoot,"",17,new Vector2(670,370),new Vector2(0,20));
            finalInfo.gameObject.SetActive(false);
            finalTitle=Label("FinalTitle",finalRoot,"",27,new Vector2(660,35),new Vector2(0,187));
            finalSummary=Label("FinalSummary",finalRoot,"",14,new Vector2(660,45),new Vector2(0,143));
            float[] xs={-280,-175,-60,145};float[] widths={65,140,70,320};
            string[] headers={"RANK","PLAYER","HP","STATUS / ELIMINATION"};
            for(int c=0;c<4;c++)Label("StandingsHeader_"+c,finalRoot,headers[c],13,new Vector2(widths[c],24),new Vector2(xs[c],105));
            for(int i=0;i<8;i++) {
                var image=HudImage("FinalRow_"+i,finalRoot,new Vector2(650,30),new Vector2(0,70-i*34),new Color(.1f,.17f,.24f));
                finalRowBackgrounds[i]=image;
                finalRows[i]=Label("Standing",image.transform,"",15,new Vector2(635,28),Vector2.zero);
                finalRows[i].gameObject.SetActive(false);
                for(int c=0;c<4;c++)finalCells[i,c]=Label("StandingCell_"+c,image.transform,"",14,new Vector2(widths[c],28),new Vector2(xs[c],0));
            }
            newMatchButton=ActionButton("New match",new Vector2(220,28),new Vector2(0,-203),ResetMatchUI);
            newMatchButton.transform.SetParent(finalRoot,false);finalRoot.gameObject.SetActive(false);
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
            newMatchButton.interactable=true;finalBackdrop.SetAsLastSibling();finalRoot.SetAsLastSibling();
        }
    }
}
