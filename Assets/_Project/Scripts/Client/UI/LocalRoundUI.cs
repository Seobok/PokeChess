using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using PokeChess.Core.Battle;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;

namespace PokeChess.Client.UI
{
    public sealed partial class PlacementSandboxView
    {
        public LocalRoundCoordinator RoundLoop { get; private set; }
        private UnityEngine.UI.Button roundButton;
        private RectTransform combatRoot,resultRoot;
        private UnityEngine.UI.Text combatInfo,resultInfo;
        private readonly Dictionary<string,UnityEngine.UI.Text> combatTokens=new Dictionary<string,UnityEngine.UI.Text>();
        private double combatAccumulator;
        private int displayedRound;
        private MatchPhase displayedPhase;
        private double lastRoundClick=-1;
        private string roundFault;
        public UnityEngine.UI.Button RoundButton => roundButton;
        public string RoundResultText => resultInfo.text;
        private void BuildRoundUI()
        {
            roundButton=ActionButton("Ready / Start battle",new Vector2(225,32),new Vector2(490,291),AdvanceRound);
            combatRoot=Rect("CombatView",canvasRect,new Vector2(600,350),Vector2.zero);
            for(int row=0;row<8;row++)for(int col=0;col<7;col++)
            {
                var cell=Rect("CombatCell_"+col+"_"+row,combatRoot,new Vector2(44,50),CombatPoint(new BoardPosition(col,row)));
                var graphic=cell.gameObject.AddComponent<PlacementHexGraphic>();graphic.color=row<4 ? new Color(.09f,.19f,.25f) : new Color(.23f,.13f,.17f);graphic.raycastTarget=false;
            }
            combatInfo=Label("CombatTime",combatRoot,"",15,new Vector2(350,24),new Vector2(0,280));
            resultRoot=Panel("RoundResult",new Vector2(600,280),new Vector2(0,105));
            resultInfo=Label("Summary",resultRoot,"",17,new Vector2(565,260),Vector2.zero);
            combatRoot.gameObject.SetActive(false);resultRoot.gameObject.SetActive(false);
        }
        private static Vector2 CombatPoint(BoardPosition position) => new Vector2(-165+position.Column*48+(position.Row%2)*24,-18+position.Row*38);
        private void AttachRoundLoop()
        {
            RoundLoop=new LocalRoundCoordinator(Match,catalog);combatAccumulator=0;roundFault=null;lastRoundClick=-1;
            foreach(var token in combatTokens.Values)Destroy(token.transform.parent.gameObject);
            combatTokens.Clear();
        }
        public void ResetOpponent()
        {
            if(Match.Phase!=MatchPhase.Preparation) { Feedback("Opponent setup is available during preparation only.");return; }
            var foe=Match.GetPlayer("p2");
            if(foe.Units.Count==0)SharedPoolSystem.RegisterUnit(Match,catalog.CreateUnit("sandbox-foe","bulbasaur","p2",UnitRank.One,0,UnitPlacement.OnBoard(new BoardPosition(2,0))),"bulbasaur");
            Feedback("Fixed opponent: one Rank One bulbasaur. Player HP damage is not part of this sandbox.");Render();
        }
        public void AdvanceRound()
        {
            if(Time.realtimeSinceStartupAsDouble-lastRoundClick<.25f)return;
            lastRoundClick=Time.realtimeSinceStartupAsDouble;
            int round=displayedRound;var phase=displayedPhase;
            CancelDrag("Round requested.");
            try
            {
                bool accepted;
                if(RoundLoop.NextPreparationPending)accepted=RoundLoop.NextRound(RoundLoop.LastResult.Round);
                else if(phase==MatchPhase.Result && !RoundLoop.IsSettled && Match.RoundNumber==round) { RoundLoop.SettleResult();accepted=true; }
                else accepted=phase==MatchPhase.Preparation ? RoundLoop.StartCombat(round) : phase==MatchPhase.Result && RoundLoop.NextRound(round);
                if(!accepted)Feedback("Round request no longer available.");
                else { combatAccumulator=0;roundFault=null;
                    foreach(var merge in RoundLoop.PreparationRankUps)if(Player.Units.Any(u=>u.InstanceId==merge.ResultUnitId))highlights[merge.ResultUnitId]=Time.unscaledTime+1.2f;
                    Feedback(Match.Phase==MatchPhase.Combat ? "Battle started. Bench move / swap remains available." : Match.Phase==MatchPhase.Result ? "Empty board result settled." : "Next preparation: income / XP applied; deferred merges: "+RoundLoop.PreparationRankUps.Count+"."); }
            }
            catch(Exception e) { roundFault=e.Message;Feedback("Round error: "+e.Message); }
            Render();
        }
        private void UpdateRoundLoop()
        {
            if(RoundLoop==null)return;
            if(Match.Phase==MatchPhase.Combat && RoundLoop.Battle!=null && roundFault==null)
            {
                combatAccumulator+=Time.unscaledDeltaTime;
                int count=0;
                try
                {
                    while(combatAccumulator>=1d/30 && Match.Phase==MatchPhase.Combat && count++<60)
                    { RoundLoop.Step();combatAccumulator-=1d/30; }
                    if(Match.Phase==MatchPhase.Result) { Feedback("Round completed. Income and automatic XP applied.");Render(); }
                }
                catch(Exception e) { roundFault=e.Message;Feedback("Round error: "+e.Message);Render(); }
            }
            RenderRoundUI();
        }
        private void RenderRoundUI()
        {
            if(roundButton==null || RoundLoop==null)return;
            displayedRound=Match.RoundNumber;displayedPhase=Match.Phase;
            roundButton.interactable=Player.HP>0 && !Player.IsEliminated &&
                (Match.Phase==MatchPhase.Preparation && (roundFault==null || RoundLoop.NextPreparationPending) || Match.Phase==MatchPhase.Result);
            ButtonText(roundButton,Match.Phase==MatchPhase.Preparation ? (RoundLoop.NextPreparationPending ? "Retry shop refresh" : "Ready / Start battle") : Match.Phase==MatchPhase.Result ? (RoundLoop.IsSettled ? "Next round" : "Retry settlement") : "Battle running");
            bool combat=Match.Phase==MatchPhase.Combat && RoundLoop.Battle!=null;
            combatRoot.gameObject.SetActive(combat);resultRoot.gameObject.SetActive(Match.Phase==MatchPhase.Result && RoundLoop.LastResult!=null);
            foreach(var slot in slots)if(slot.Placement.Kind==PlacementKind.Board)slot.Rect.gameObject.SetActive(!combat);
            foreach(var unit in Player.Units)if(unit.Placement.Kind==PlacementKind.Board)tokens[unit.InstanceId].gameObject.SetActive(!combat);
            if(combat)
            {
                foreach(var token in combatTokens.Values)token.transform.parent.gameObject.SetActive(false);
                var battle=RoundLoop.Battle;combatInfo.text="BATTLE  "+battle.ElapsedSeconds.ToString("0.0")+"s"+(battle.IsOvertime ? " / OVERTIME" : "")+"   BLUE: YOU / RED: FOE";
                foreach(var unit in battle.Units)
                {
                    if(!combatTokens.TryGetValue(unit.UnitInstanceId,out var label))
                    {
                        var rect=Rect("Fighter_"+unit.UnitInstanceId,combatRoot,new Vector2(43,35),Vector2.zero);
                        var image=rect.gameObject.AddComponent<UnityEngine.UI.Image>();image.color=unit.TeamId==1 ? new Color(.12f,.44f,.58f) : new Color(.6f,.2f,.23f);image.raycastTarget=false;
                        label=Label("HP",rect,"",10,new Vector2(43,35),Vector2.zero);combatTokens[unit.UnitInstanceId]=label;
                    }
                    label.transform.parent.gameObject.SetActive(unit.IsAlive && unit.IsOnBoard);
                    ((RectTransform)label.transform.parent).anchoredPosition=CombatPoint(unit.Position);
                    label.text=unit.DefinitionId.Substring(0,Math.Min(3,unit.DefinitionId.Length)).ToUpperInvariant()+" R"+(int)unit.Rank+"\n"+Mathf.CeilToInt(unit.CurrentHP)+" HP";
                }
            }
            if(resultRoot.gameObject.activeSelf)
            {
                var result=RoundLoop.LastResult;
                string verdict=result.Result==BattleResult.Draw ? "DRAW" : result.Result==BattleResult.TeamOneWin ? "VICTORY" : "DEFEAT";
                string summary="ROUND "+result.Round+" / "+verdict+"\n"+result.Reason+" / "+(result.EndTick/30d).ToString("0.0")+"s\n\n";
                if(result.Income.TryGetValue("p1",out var income))summary+="Income +"+income.TotalIncome+"G = base "+income.BaseIncome+" + interest "+income.Interest+" + streak "+income.StreakBonus+"\n";
                if(result.XP.TryGetValue("p1",out var xp))summary+="Automatic XP +"+xp.XPGranted+" / Level "+xp.LevelBefore+" -> "+xp.LevelAfter+"\n";
                resultInfo.text=summary+"\nPreparation board restored. Choose Next round."+(roundFault==null ? "" : "\nERROR: "+roundFault);
                resultRoot.SetAsLastSibling();
            }
        }
    }
}
