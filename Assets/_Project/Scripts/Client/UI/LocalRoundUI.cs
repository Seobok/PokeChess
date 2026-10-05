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

        private int displayedRound;
        private MatchPhase displayedPhase;
        private double lastRoundClick=-1;
        public RoundFlowController RoundFlow { get; private set; }
        [SerializeField, Min(.01f)] private float preparationSeconds = 30;
        [SerializeField, Min(.01f)] private float resultSeconds = 3;
        private bool automaticRoundFlow = true;
        public bool AutomaticRoundFlowEnabled => automaticRoundFlow;
        private UnityEngine.UI.Text roundTimer;
        private string roundFault => RoundFlow?.Fault?.Message;
        public string RoundTimerText => roundTimer.text;

        public UnityEngine.UI.Button RoundButton => roundButton;
        public string RoundResultText => resultInfo.text;
        private void BuildRoundUI()
        {
            roundTimer=Label("RoundTimer",canvasRect,"",16,new Vector2(730,24),new Vector2(0,337));
            roundButton=ActionButton("DEV: Start battle",new Vector2(225,32),new Vector2(490,291),AdvanceRound);
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
            RoundLoop=new LocalRoundCoordinator(Match,catalog);
            RoundFlow=new RoundFlowController(RoundLoop,new RoundFlowRules(preparationSeconds,resultSeconds));
            automaticRoundFlow=true;lastRoundClick=-1;
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
            if (!automaticRoundFlow || Time.realtimeSinceStartupAsDouble-lastRoundClick < .25f) return;
            lastRoundClick = Time.realtimeSinceStartupAsDouble;
            var phase = Match.Phase; int round = Match.RoundNumber;
            bool pending = RoundLoop.NextPreparationPending; var fault = RoundFlow.Fault;
            int expectedRound = displayedRound; var expectedPhase = displayedPhase;
            CancelDrag("Round requested.");
            bool accepted = RoundFlow.RequestAdvance(expectedRound, expectedPhase);
            RefreshRoundProgress(phase, round, pending, fault);
            if (!accepted && RoundFlow.Fault == null) Feedback("Round request no longer available.");
            Render();
        }

        private void UpdateRoundLoop() => AdvanceRoundTime(Time.unscaledDeltaTime);

        // Same path used by Update and editor integration checks.
        public void AdvanceRoundTime(double elapsedSeconds)
        {
            if (RoundFlow == null) return;
            if (automaticRoundFlow)
            {
                var phase = Match.Phase; int round = Match.RoundNumber;
                bool pending = RoundLoop.NextPreparationPending; var fault = RoundFlow.Fault;
                RoundFlow.AdvanceTime(elapsedSeconds);
                RefreshRoundProgress(phase, round, pending, fault);
            }
            RenderRoundUI();
        }

        private void RefreshRoundProgress(MatchPhase phase, int round, bool pending, Exception fault)
        {
            bool changed = phase != Match.Phase || round != Match.RoundNumber
                || pending != RoundLoop.NextPreparationPending || fault != RoundFlow.Fault;
            if (!changed) return;
            if (IsDragging) CancelDrag("Round advanced; drag cancelled.");
            if (RoundFlow.Fault != null) Feedback("Round error: " + roundFault);
            else if (Match.Phase == MatchPhase.Preparation && RoundLoop.PreparationReady)
            {
                foreach (var merge in RoundLoop.PreparationRankUps)
                    if (Player.Units.Any(u => u.InstanceId == merge.ResultUnitId))
                        highlights[merge.ResultUnitId] = Time.unscaledTime + 1.2f;
                Feedback("Next preparation: income / XP applied; deferred merges: " + RoundLoop.PreparationRankUps.Count + ".");
            }
            else if (Match.Phase == MatchPhase.Combat) Feedback("Battle started. Bench move / swap remains available.");
            else if (Match.Phase == MatchPhase.Result) Feedback("Round completed. Income and automatic XP applied.");
            Render();
        }
        private void RenderRoundUI()
        {
            if(roundButton==null || RoundLoop==null)return;
            RoundFlow.RefreshState();
            displayedRound=Match.RoundNumber;displayedPhase=Match.Phase;
            string clock;
            if(!automaticRoundFlow) clock="DEBUG INPUT TEST / AUTO PAUSED";
            else if(roundFault!=null) clock="PAUSED / RETRY REQUIRED";
            else if(RoundLoop.NextPreparationPending) clock="WAITING FOR SHOP REFRESH";
            else if(Match.Phase==MatchPhase.Result && !RoundLoop.IsSettled) clock="WAITING FOR SETTLEMENT";
            else if(Match.Phase==MatchPhase.Preparation && !RoundLoop.PreparationReady) clock="WAITING FOR PREPARATION";
            else if(RoundFlow.IsTimerRunning) clock=Math.Ceiling(RoundFlow.RemainingSeconds).ToString("0")+"s";
            else clock=Match.Phase==MatchPhase.Combat && RoundLoop.GetBattleFor("p1")?.Battle!=null ? RoundLoop.GetBattleFor("p1").Battle.ElapsedSeconds.ToString("0.0")+"s" : Match.Phase==MatchPhase.Combat ? "WAITING" : "";
            roundTimer.text="ROUND "+Match.RoundNumber+" / "+Match.Phase.ToString().ToUpperInvariant()+" / "+clock;
            roundTimer.color=roundFault!=null ? new Color(1,.45f,.4f) : RoundFlow.IsTimerRunning && RoundFlow.RemainingSeconds<=5 ? new Color(1,.78f,.36f) : Color.white;
            roundButton.interactable=automaticRoundFlow && Player.HP>0 && !Player.IsEliminated &&
                (Match.Phase==MatchPhase.Preparation && (RoundLoop.PreparationReady || RoundLoop.NextPreparationPending) || Match.Phase==MatchPhase.Result);
            ButtonText(roundButton,Match.Phase==MatchPhase.Preparation ? (RoundLoop.NextPreparationPending ? "Retry shop refresh" : (roundFault==null ? "DEV: Start battle" : "Retry battle start")) : Match.Phase==MatchPhase.Result ? (RoundLoop.IsSettled ? "DEV: Next round" : "Retry settlement") : "Battle running");
            var ownPair=RoundLoop.GetBattleFor("p1");
            bool combat=Match.Phase==MatchPhase.Combat && ownPair?.Battle!=null;
            if(Match.Phase==MatchPhase.Combat && ownPair!=null)
                roundTimer.text+=" / VS "+ownPair.Pairing.OpponentOf("p1")+(ownPair.IsComplete ? " / "+ownPair.OutcomeOf("p1")+" / WAITING FOR OTHER PAIRS" : "");
            if(selectedUnitId==null && (Match.Phase==MatchPhase.Combat || Match.Phase==MatchPhase.Result))
                detailLabel.text="DEV: ALL PAIRS\n"+string.Join("\n",RoundLoop.Battles.Select(b=>b.Pairing.PlayerOneId+" vs "+b.Pairing.PlayerTwoId+" : "+(b.IsComplete ? b.Result.ToString() : "RUNNING")))+"\n\n"+RoundLoop.Battles.Count(b=>b.IsComplete)+" / "+RoundLoop.Battles.Count+" completed";
            combatRoot.gameObject.SetActive(combat);resultRoot.gameObject.SetActive(Match.Phase==MatchPhase.Result && RoundLoop.LastResult!=null);
            foreach(var slot in slots)if(slot.Placement.Kind==PlacementKind.Board)slot.Rect.gameObject.SetActive(!combat);
            foreach(var unit in Player.Units)if(unit.Placement.Kind==PlacementKind.Board)tokens[unit.InstanceId].gameObject.SetActive(!combat);
            if(combat)
            {
                foreach(var token in combatTokens.Values)token.transform.parent.gameObject.SetActive(false);
                var battle=ownPair.Battle;combatInfo.text="VS "+ownPair.Pairing.OpponentOf("p1")+" / "+battle.ElapsedSeconds.ToString("0.0")+"s"+(ownPair.IsComplete ? " / WAITING FOR OTHER PAIRS" : battle.IsOvertime ? " / OVERTIME" : "");
                foreach(var unit in battle.Units)
                {
                    if(!combatTokens.TryGetValue(unit.UnitInstanceId,out var label))
                    {
                        var rect=Rect("Fighter_"+unit.UnitInstanceId,combatRoot,new Vector2(43,35),Vector2.zero);
                        var image=rect.gameObject.AddComponent<UnityEngine.UI.Image>();image.color=unit.TeamId==(ownPair.Pairing.PlayerOneId=="p1" ? 1 : 2) ? new Color(.12f,.44f,.58f) : new Color(.6f,.2f,.23f);image.raycastTarget=false;
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
                result.PlayerResults.TryGetValue("p1",out var ownResult);
                string verdict=ownResult==null ? "SPECTATING" : ownResult.Outcome==RoundOutcome.Draw ? "DRAW" : ownResult.Outcome==RoundOutcome.Win ? "VICTORY" : "DEFEAT";
                string summary="ROUND "+result.Round+" / "+verdict+"\n"+(ownResult==null ? "" : "VS "+ownResult.OpponentId+" / "+ownResult.Reason+" / "+(ownResult.EndTick/30d).ToString("0.0")+"s")+"\n\n";
                if(result.Income.TryGetValue("p1",out var income))summary+="Income +"+income.TotalIncome+"G = base "+income.BaseIncome+" + interest "+income.Interest+" + streak "+income.StreakBonus+"\n";
                if(result.XP.TryGetValue("p1",out var xp))summary+="Automatic XP +"+xp.XPGranted+" / Level "+xp.LevelBefore+" -> "+xp.LevelAfter+"\n";
                resultInfo.text=summary+"\nPreparation board restored. Next round starts automatically."+(roundFault==null ? "" : "\nERROR: "+roundFault);
                resultRoot.SetAsLastSibling();
            }
        }
    }
}





