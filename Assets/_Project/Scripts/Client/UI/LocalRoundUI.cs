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
        private UnityEngine.UI.Button surrenderButton;
        public UnityEngine.UI.Button SurrenderButton => surrenderButton;
        private RectTransform combatRoot,resultRoot;
        private UnityEngine.UI.Text combatInfo,resultInfo;
        private readonly Dictionary<string,UnityEngine.UI.Text> combatTokens=new Dictionary<string,UnityEngine.UI.Text>();
        private readonly Dictionary<string,float> previousCombatHP=new Dictionary<string,float>();
        private readonly Dictionary<string,float> healFlashUntil=new Dictionary<string,float>();
        private readonly Dictionary<long,RectTransform> combatShots=new Dictionary<long,RectTransform>();
        private BattleState lastVisualBattle;

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
            surrenderButton=ActionButton("Surrender",new Vector2(225,24),new Vector2(490,320),SurrenderLocalPlayer);
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
            BuildFinalResultUI();
            BuildSimulationUI();
        }
        private static Vector2 CombatPoint(BoardPosition position) => new Vector2(-165+position.Column*48+(position.Row%2)*24,-18+position.Row*38);
        private void AttachRoundLoop()
        {
            Simulation?.Cancel();SaveSimulation();Simulation=null;
            RoundLoop=new LocalRoundCoordinator(Match,catalog,skillCatalog:PrototypeRoster.CreateSkills(),statusCatalog:PrototypeRoster.CreateStatuses());
            RoundFlow=new RoundFlowController(RoundLoop,new RoundFlowRules(preparationSeconds,resultSeconds));
            automaticRoundFlow=true;lastRoundClick=-1;
            foreach(var token in combatTokens.Values)Destroy(token.transform.parent.gameObject);
            combatTokens.Clear();
            previousCombatHP.Clear();healFlashUntil.Clear();
            lastVisualBattle=null;
            foreach(var shot in combatShots.Values)Destroy(shot.gameObject);combatShots.Clear();
        }
        public void ResetOpponent()
        {
            if(Match.Phase!=MatchPhase.Preparation) { Feedback("Opponent setup is available during preparation only.");return; }
            var foe=Match.GetPlayer("p2");
            string definition=catalog.TryGet("slowpoke",out _) ? "slowpoke" : "bulbasaur";
            if(foe.Units.Count==0)SharedPoolSystem.RegisterUnit(Match,catalog.CreateUnit("sandbox-foe",definition,"p2",UnitRank.One,0,UnitPlacement.OnBoard(new BoardPosition(2,0))),definition);
            Feedback("Fixed opponent: one Rank One "+definition+". Round damage applies after all battles finish.");Render();
        }
        public void SurrenderLocalPlayer()
        {
            if(Simulation?.Status==SimulationStatus.Running)return;
            CancelDrag("Surrender requested.");
            try
            {
                var result=RoundLoop.Surrender("p1",Match.RoundNumber,Match.Phase);
                Feedback(result.Message+(result.Elimination==null ? "" : " Placement #"+result.Elimination.Placement));
                RoundFlow.RefreshState();Render();
            }
            catch(Exception error) {Feedback("Surrender failed: "+error.Message);Render();}
        }
        public void AdvanceRound()
        {
            if(Simulation?.Status==SimulationStatus.Running)return;
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
            if(Simulation!=null)
            {
                Simulation.Advance(simulationFast ? elapsedSeconds*60 : elapsedSeconds,simulationFast ? 300 : 60);
                SaveSimulation();Render();RenderSimulationUI();return;
            }
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
            else if(Match.Phase==MatchPhase.Finished)Feedback("Match complete. Winner: "+Match.FinalResult.WinnerPlayerId);
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
        private static string OpponentLabel(LocalPairBattle battle,string playerId)
            => battle.Pairing.OpponentOf(playerId)+(battle.IsShadow ? " (SHADOW)" : "");
        private void RenderRoundUI()
        {
            if(roundButton==null || RoundLoop==null)return;
            RoundFlow.RefreshState();
            displayedRound=Match.RoundNumber;displayedPhase=Match.Phase;
            string clock;
            if(Match.Phase==MatchPhase.Finished) clock="MATCH COMPLETE";
            else if(!automaticRoundFlow) clock="DEBUG INPUT TEST / AUTO PAUSED";
            else if(roundFault!=null) clock="PAUSED / RETRY REQUIRED";
            else if(RoundFlow.AwaitingMatchEnd) clock="WAITING FOR MATCH END";
            else if(RoundLoop.NextPreparationPending) clock="WAITING FOR SHOP REFRESH";
            else if(Match.Phase==MatchPhase.Result && !RoundLoop.IsSettled) clock="WAITING FOR SETTLEMENT";
            else if(Match.Phase==MatchPhase.Preparation && !RoundLoop.PreparationReady) clock="WAITING FOR PREPARATION";
            else if(RoundFlow.IsTimerRunning) clock=Math.Ceiling(RoundFlow.RemainingSeconds).ToString("0")+"s";
            else clock=Match.Phase==MatchPhase.Combat && RoundLoop.GetBattleFor("p1")?.Battle!=null ? RoundLoop.GetBattleFor("p1").Battle.ElapsedSeconds.ToString("0.0")+"s" : Match.Phase==MatchPhase.Combat ? "WAITING" : "";
            roundTimer.text="ROUND "+Match.RoundNumber+" / "+Match.Phase.ToString().ToUpperInvariant()+" / "+clock;
            roundTimer.color=roundFault!=null ? new Color(1,.45f,.4f) : RoundFlow.IsTimerRunning && RoundFlow.RemainingSeconds<=5 ? new Color(1,.78f,.36f) : Color.white;
            roundButton.interactable=automaticRoundFlow && !RoundFlow.AwaitingMatchEnd && Player.HP>0 && !Player.IsEliminated &&
                (Match.Phase==MatchPhase.Preparation && (RoundLoop.PreparationReady || RoundLoop.NextPreparationPending) || Match.Phase==MatchPhase.Result);
            ButtonText(roundButton,Match.Phase==MatchPhase.Finished ? "Match complete" : Match.Phase==MatchPhase.Preparation ? (RoundLoop.NextPreparationPending ? "Retry shop refresh" : (roundFault==null ? "DEV: Start battle" : "Retry battle start")) : Match.Phase==MatchPhase.Result ? (RoundLoop.IsSettled ? "DEV: Next round" : "Retry settlement") : "Battle running");
            var ownPair=RoundLoop.GetBattleFor("p1");
            bool combat=Match.Phase==MatchPhase.Combat && ownPair?.Battle!=null;
            if(Match.Phase==MatchPhase.Combat && ownPair!=null)
                roundTimer.text+=" / VS "+OpponentLabel(ownPair,"p1")+(ownPair.IsComplete ? " / "+ownPair.OutcomeOf("p1")+" / WAITING FOR OTHER PAIRS" : "");
            if(selectedUnitId==null && (Match.Phase==MatchPhase.Combat || Match.Phase==MatchPhase.Result))
                detailLabel.text="DEV: ALL PAIRS (S=SHADOW)\n"+string.Join("\n",RoundLoop.Battles.Select(b=>b.Pairing.PlayerOneId+" vs "+b.Pairing.PlayerTwoId+(b.IsShadow ? " (S)" : "")+" : "+(b.IsComplete ? b.Result.ToString() : "RUNNING")))+"\n\n"+RoundLoop.Battles.Count(b=>b.IsComplete)+" / "+RoundLoop.Battles.Count+" completed";
            combatRoot.gameObject.SetActive(combat);resultRoot.gameObject.SetActive(Match.Phase==MatchPhase.Result && RoundLoop.LastResult!=null);
            foreach(var slot in slots)if(slot.Placement.Kind==PlacementKind.Board)slot.Rect.gameObject.SetActive(!combat);
            foreach(var unit in Player.Units)if(unit.Placement.Kind==PlacementKind.Board)tokens[unit.InstanceId].gameObject.SetActive(!combat);
            if(combat)
            {
                foreach(var token in combatTokens.Values)token.transform.parent.gameObject.SetActive(false);
                var battle=ownPair.Battle;combatInfo.text="VS "+OpponentLabel(ownPair,"p1")+" / "+battle.ElapsedSeconds.ToString("0.0")+"s"+(ownPair.IsComplete ? " / WAITING FOR OTHER PAIRS" : battle.IsOvertime ? " / OVERTIME" : "");
                if(lastVisualBattle!=battle)
                {
                    lastVisualBattle=battle;previousCombatHP.Clear();healFlashUntil.Clear();
                    foreach(var oldShot in combatShots.Values)Destroy(oldShot.gameObject);combatShots.Clear();
                }
                foreach(var shot in combatShots.Values)shot.gameObject.SetActive(false);
                foreach(var projectile in battle.Projectiles.Active)
                {
                    if(!combatShots.TryGetValue(projectile.Id,out var shot))
                    {
                        shot=Rect("Projectile_"+projectile.Id,combatRoot,new Vector2(7,7),Vector2.zero);
                        var shotImage=shot.gameObject.AddComponent<UnityEngine.UI.Image>();shotImage.raycastTarget=false;
                        shotImage.color=projectile.SkillDamage.HasValue?new Color(1,.8f,.15f):Color.white;combatShots[projectile.Id]=shot;
                    }
                    shot.gameObject.SetActive(true);
                    float progress=(float)(battle.CurrentTick-projectile.SpawnTick)/Math.Max(1,projectile.ArrivalTick-projectile.SpawnTick);
                    shot.anchoredPosition=Vector2.Lerp(CombatPoint(projectile.LaunchPosition),CombatPoint(projectile.TargetPositionAtLaunch),progress);
                }
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
                    if(previousCombatHP.TryGetValue(unit.UnitInstanceId,out var previous)&&unit.CurrentHP>previous)
                        healFlashUntil[unit.UnitInstanceId]=Time.unscaledTime+.4f;
                    previousCombatHP[unit.UnitInstanceId]=unit.CurrentHP;
                    bool healing=healFlashUntil.TryGetValue(unit.UnitInstanceId,out var until)&&Time.unscaledTime<until;
                    bool buffed=battle.StatusEffects.Active.Any(s=>s.TargetId==unit.UnitInstanceId&&s.Definition.CrowdControl==CrowdControlKind.None);
                    var tokenImage=label.transform.parent.GetComponent<UnityEngine.UI.Image>();
                    tokenImage.color=healing?new Color(.15f,.65f,.3f):unit.ActionState==CombatActionState.Casting?new Color(.55f,.3f,.75f):
                        buffed?new Color(.2f,.55f,.7f):unit.TeamId==(ownPair.Pairing.PlayerOneId=="p1" ? 1 : 2)?new Color(.12f,.43f,.55f):new Color(.6f,.2f,.23f);
                    label.text=unit.DefinitionId.Substring(0,Math.Min(3,unit.DefinitionId.Length)).ToUpperInvariant()+" R"+(int)unit.Rank+"\n"+Mathf.CeilToInt(unit.CurrentHP)+" HP\n"+
                        (healing?"HEAL":unit.ActionState==CombatActionState.Casting?"CAST":buffed?"BUFF":Mathf.FloorToInt(unit.CurrentEnergy)+"E");
                }
            }
            if(resultRoot.gameObject.activeSelf)
            {
                var result=RoundLoop.LastResult;
                result.PlayerResults.TryGetValue("p1",out var ownResult);
                string verdict=ownResult==null ? "SPECTATING" : ownResult.Outcome==RoundOutcome.Draw ? "DRAW" : ownResult.Outcome==RoundOutcome.Win ? "VICTORY" : "DEFEAT";
                string summary="ROUND "+result.Round+" / "+verdict+"\n"+(ownResult==null ? "" :  "VS "+ownResult.OpponentId+(ownResult.IsShadow ? " (SHADOW)" : "")+" / "+ownResult.Reason+" / "+(ownResult.EndTick/30d).ToString("0.0")+"s")+"\n\n";
                if(result.Damage.TryGetValue("p1",out var damage))summary+="Damage "+damage.TotalDamage+" = base "+damage.BaseDamage+" + survivors "+damage.SurvivorDamage+"\nHP "+damage.HPBefore+" -> "+damage.HPAfter+"\n";
                if(result.Income.TryGetValue("p1",out var income))summary+="Income +"+income.TotalIncome+"G = base "+income.BaseIncome+" + interest "+income.Interest+" + streak "+income.StreakBonus+"\n";
                if(result.XP.TryGetValue("p1",out var xp))summary+="Automatic XP +"+xp.XPGranted+" / Level "+xp.LevelBefore+" -> "+xp.LevelAfter+"\n";
                if(Player.Elimination!=null)summary+="Placement #"+Player.FinalPlacement+" / "+Player.Elimination.Reason+"\n";
                resultInfo.text=summary+"\n"+(Player.IsEliminated ? "Eliminated. Spectating remaining players." : "Preparation board restored. Next round starts automatically.")+(roundFault==null ? "" : "\nERROR: "+roundFault);
                resultRoot.SetAsLastSibling();
            }
            RenderFinalResultUI();
            RenderSimulationUI();
        }
    }
}










