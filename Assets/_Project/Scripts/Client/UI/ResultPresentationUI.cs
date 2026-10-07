using System.Linq;
using UnityEngine;
using PokeChess.Core.Match;

namespace PokeChess.Client.UI
{
    public sealed partial class PlacementSandboxView
    {
        private UnityEngine.UI.Text resultTitle,resultVitals,resultRewards,resultNext,resultDetails;
        private UnityEngine.UI.Button resultDetailsButton,continueWatchingButton;
        private RectTransform eliminationRoot;
        private UnityEngine.UI.Text eliminationInfo;
        private MatchState resultPresentationMatch;
        private bool resultExpanded,eliminationDismissed;
        private int presentationRound=-1;
        public bool RoundResultVisible => resultRoot.gameObject.activeSelf;
        public bool EliminationVisible => eliminationRoot.gameObject.activeSelf;
        public bool FinalResultVisible => finalRoot.gameObject.activeSelf;
        public string ResultHeadline => resultTitle.text;
        public string ResultHPText => resultVitals.text;
        public string ResultRewardText => resultRewards.text;
        public string EliminationText => eliminationInfo.text;
        public UnityEngine.UI.Button ResultDetailsButton => resultDetailsButton;
        public UnityEngine.UI.Button ContinueWatchingButton => continueWatchingButton;
        private static string OutcomeLabel(RoundOutcome outcome) => outcome==RoundOutcome.Win?"VICTORY":outcome==RoundOutcome.Loss?"DEFEAT":"DRAW";
        private void BuildResultPresentation()
        {
            resultInfo.gameObject.SetActive(false);
            resultTitle=Label("ResultTitle",resultRoot,"",26,new Vector2(560,34),new Vector2(0,88));
            resultVitals=Label("ResultHP",resultRoot,"",22,new Vector2(560,32),new Vector2(0,44));
            resultRewards=Label("ResultRewards",resultRoot,"",17,new Vector2(560,28),new Vector2(0,6));
            resultNext=Label("ResultNext",resultRoot,"",13,new Vector2(560,40),new Vector2(0,-34));
            resultDetailsButton=ActionButton("ResultDetails",new Vector2(180,28),new Vector2(0,-76),()=>{resultExpanded=!resultExpanded;RenderRoundUI();});
            resultDetailsButton.transform.SetParent(resultRoot,false);
            resultDetails=Label("ResultBreakdown",resultRoot,"",13,new Vector2(560,110),new Vector2(0,-150));
            eliminationRoot=Panel("EliminationNotice",new Vector2(550,235),new Vector2(0,110));
            eliminationInfo=Label("EliminationText",eliminationRoot,"",22,new Vector2(510,160),new Vector2(0,25));
            continueWatchingButton=ActionButton("Continue watching",new Vector2(230,32),new Vector2(0,-82),()=>{eliminationDismissed=true;RenderRoundUI();});
            continueWatchingButton.transform.SetParent(eliminationRoot,false);eliminationRoot.gameObject.SetActive(false);
        }
        private void RenderResultPresentation()
        {
            if(resultPresentationMatch!=Match)
            {
                resultPresentationMatch=Match;eliminationDismissed=false;resultExpanded=false;presentationRound=-1;
            }
            if(presentationRound!=Match.RoundNumber){presentationRound=Match.RoundNumber;resultExpanded=false;}
            bool finished=Match.Phase==MatchPhase.Finished&&Match.FinalResult!=null;
            bool eliminated=Player.Elimination!=null;
            bool showElimination=eliminated&&!eliminationDismissed&&!finished;
            eliminationRoot.gameObject.SetActive(showElimination);
            if(showElimination)
            {
                var e=Player.Elimination;
                eliminationInfo.text="ELIMINATED · PLACEMENT #"+e.Placement+"\n\nRound "+e.Round+" · "+(e.Reason==EliminationReason.Surrender?"Surrender":"HP depleted")+"\nThe remaining match continues.";
                eliminationRoot.SetAsLastSibling();
            }
            var pair=RoundLoop.GetBattleFor("p1");
            bool early=!eliminated&&Match.Phase==MatchPhase.Combat&&pair!=null&&pair.IsComplete;
            bool settled=Match.Phase==MatchPhase.Result&&RoundLoop.LastResult!=null;
            resultRoot.gameObject.SetActive(!finished&&!showElimination&&!eliminated&&(early||settled));
            if(!resultRoot.gameObject.activeSelf)return;
            resultRoot.sizeDelta=new Vector2(600,resultExpanded&&!early?360:230);
            resultRoot.anchoredPosition=new Vector2(0,105);
            float shift=resultExpanded&&!early?65:0;
            resultTitle.rectTransform.anchoredPosition=new Vector2(0,88+shift);
            resultVitals.rectTransform.anchoredPosition=new Vector2(0,44+shift);
            resultRewards.rectTransform.anchoredPosition=new Vector2(0,6+shift);
            resultNext.rectTransform.anchoredPosition=new Vector2(0,-34+shift);
            ((RectTransform)resultDetailsButton.transform).anchoredPosition=new Vector2(0,-76+shift);
            resultDetails.rectTransform.anchoredPosition=new Vector2(0,-95);
            var outcome=early?pair.OutcomeOf("p1"):RoundLoop.LastResult.PlayerResults.TryGetValue("p1",out var own)?own.Outcome:RoundOutcome.Draw;
            resultTitle.text="ROUND "+Match.RoundNumber+" · "+OutcomeLabel(outcome);
            resultTitle.color=outcome==RoundOutcome.Win?new Color(.4f,1,.7f):outcome==RoundOutcome.Loss?new Color(1,.5f,.5f):new Color(1,.85f,.5f);
            resultDetailsButton.gameObject.SetActive(!early);resultDetails.gameObject.SetActive(!early&&resultExpanded);
            if(early)
            {
                resultVitals.text="HP "+Player.HP+" · Settlement pending";
                resultVitals.color=Color.white;
                resultRewards.text="Rewards pending";
                resultNext.text="VS "+OpponentLabel(pair,"p1")+"\nWaiting for other battles.";
                resultInfo.text=resultTitle.text+"\n"+resultVitals.text+"\n"+resultNext.text;
            }
            else
            {
                var result=RoundLoop.LastResult;
                result.Damage.TryGetValue("p1",out var damage);result.Income.TryGetValue("p1",out var income);result.XP.TryGetValue("p1",out var xp);
                resultVitals.text=damage==null?"HP "+Player.HP:"HP "+damage.HPBefore+" → "+damage.HPAfter+
                    (damage.AppliedDamage==0?"   (No HP lost)":"   (−"+damage.AppliedDamage+" HP)");
                resultVitals.color=damage!=null&&damage.TotalDamage>0?new Color(1,.6f,.5f):Color.white;
                resultRewards.text=(income==null?"":"+"+income.TotalIncome+"G")+(xp==null?"":"   +"+xp.XPGranted+" XP")+(xp!=null&&xp.LevelAfter!=xp.LevelBefore?" · LEVEL "+xp.LevelAfter:"");
                resultNext.text=(pair==null?"":"VS "+OpponentLabel(pair,"p1")+"\n")+(roundFault!=null?"Settlement paused · retry required":RoundLoop.IsSettled?"Next preparation starts automatically.":"Settlement pending.");
                resultDetails.text=(damage==null?"":"Damage "+damage.TotalDamage+" = base "+damage.BaseDamage+" + survivors "+damage.SurvivorDamage+"\n")+
                    (income==null?"":"Income "+income.TotalIncome+" = base "+income.BaseIncome+" + interest "+income.Interest+" + streak "+income.StreakBonus+"\n")+
                    (xp==null?"":"Automatic XP +"+xp.XPGranted+" · Level "+xp.LevelBefore+" → "+xp.LevelAfter+"\n")+
                    (pair==null?"":"Battle: "+pair.Reason+" · "+(pair.EndTick/30d).ToString("0.0")+"s");
                ButtonText(resultDetailsButton,resultExpanded?"Hide details":"Show details");
                resultInfo.text=resultTitle.text+"\n"+resultVitals.text+"\n"+resultRewards.text+"\n"+resultNext.text+"\n"+resultDetails.text;
            }
            resultRoot.SetAsLastSibling();
        }
    }
}
