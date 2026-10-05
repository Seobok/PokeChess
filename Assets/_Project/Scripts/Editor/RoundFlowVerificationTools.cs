using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using PokeChess.Client.UI;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;

namespace PokeChess.Editor
{
    public static class RoundFlowVerificationTools
    {
        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        private static PlacementSandboxView View()
        {
            if (!EditorApplication.isPlaying) throw new Exception("Enter Play mode.");
            if (!EditorApplication.isPaused) throw new Exception("Pause Play mode for deterministic integration checks.");
            return UnityEngine.Object.FindFirstObjectByType<PlacementSandboxView>();
        }

        private static void Click(UnityEngine.UI.Button button)
        {
            Check(button.interactable, "Button must be interactable: " + button.name);
            ExecuteEvents.Execute(button.gameObject,
                new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left },
                ExecuteEvents.pointerClickHandler);
        }

        private static void FinishCombat(PlacementSandboxView view)
        {
            for (int i = 0; i < 1400 && view.Match.Phase == MatchPhase.Combat; i++)
                view.AdvanceRoundTime(1d / 30);
            Check(view.Match.Phase == MatchPhase.Result && view.RoundLoop.IsSettled, "Settled automatic Result");
            Check(view.RoundFlow.Fault == null, "No round flow error");
        }

        public static string VerifyAutomaticFlow()
        {
            var view = View();
            view.ResetMatchUI();
            Check(view.RoundTimerText.Contains("PREPARATION / 30s"), "Preparation countdown visible");
            Click(view.ShopButton(0));
            var unit = view.LastPurchase.Unit;
            var move = new PlayerPlacementSystem().Move(view.Match, "p1", unit.InstanceId,
                UnitPlacement.OnBoard(new BoardPosition(2, 0)), view.Player.PlacementRevision);
            Check(move.Accepted, "Deploy bought unit");
            view.RefreshFromState();
            view.BeginDrag(unit.InstanceId, view.ScreenPoint(unit.Placement));
            Check(view.IsDragging, "Drag started");
            view.AdvanceRoundTime(29);
            Check(view.Match.Phase == MatchPhase.Preparation && view.RoundTimerText.Contains("1s"), "Last second visible");
            MinimumMatchUITools.Capture("TestResults/WBS3.1/preparation.png");
            view.AdvanceRoundTime(1);
            Check(view.Match.Phase == MatchPhase.Combat && !view.IsDragging, "Automatic start cancels drag");
            Check(view.RoundTimerText.Contains("COMBAT"), "Combat phase visible");
            Check(!view.RoundButton.interactable && !view.ShopExpanded, "Combat input state");
            var battle = view.RoundLoop.Battle;
            Check(battle.Units.Count == 2 && battle.CurrentTick == 0, "New battle snapshot");
            MinimumMatchUITools.Capture("TestResults/WBS3.1/combat.png");
            view.ToggleShop();
            Click(view.ShopButton(1));
            string bought = view.LastPurchase.Unit.InstanceId;
            Check(view.RoundLoop.Battle == battle && battle.Units.Count == 2
                && battle.Units.All(u => u.UnitInstanceId != bought), "Combat purchase preserves snapshot");
            view.BeginDrag(bought, view.ScreenPoint(view.Player.GetUnit(bought).Placement));
            Check(view.IsDragging, "Bench drag during combat");
            FinishCombat(view);
            Check(!view.IsDragging, "Automatic Result cancels bench drag");
            Check(view.RoundTimerText.Contains("RESULT / 3s"), "Full result timer visible");
            Check(view.RoundResultText.Contains("Income") && view.RoundResultText.Contains("Automatic XP"), "Result summary visible");
            Check(view.TokenMatchesState(unit.InstanceId), "Preparation placement restored");
            MinimumMatchUITools.Capture("TestResults/WBS3.1/result.png");
            view.AdvanceRoundTime(2);
            Check(view.Match.Phase == MatchPhase.Result && view.RoundTimerText.Contains("1s"), "Result dwell");
            int gold = view.Player.Gold;
            view.AdvanceRoundTime(1);
            Check(view.Match.RoundNumber == 2 && view.Match.Phase == MatchPhase.Preparation, "Automatic next preparation");
            Check(view.RoundTimerText.Contains("PREPARATION / 30s") && view.ShopExpanded, "Fresh countdown and shop");
            Check(view.Player.Gold == gold && view.Player.Shop.LastRefreshRound == 2, "Once-only settlement and refresh");
            view.Match.Pool.AssertConservation(view.Match);
            MinimumMatchUITools.Capture("TestResults/WBS3.1/next-preparation-4x3.png", 1024, 768);
            return "Passed: pointer buy/deploy, preparation countdown, automatic start and drag cancel, combat purchase/frozen snapshot, automatic Result and bench drag cancel, 3s dwell, automatic next preparation, shop/XP/income/pool.";
        }

        public static string VerifyDeferredMerge()
        {
            var view = View();
            view.ResetRankDemo();
            view.ResetOpponent();
            view.AdvanceRoundTime(30);
            var battle = view.RoundLoop.Battle;
            view.ToggleShop();
            Click(view.ShopButton(0));
            Check(view.LastPurchase.MergeNextPreparation, "Deferred board merge flag");
            Check(view.Player.GetUnit("A").Rank == UnitRank.Two
                && battle.Units.Single(u => u.UnitInstanceId == "A").Rank == UnitRank.Two, "Board and fighter rank preserved");
            FinishCombat(view);
            view.AdvanceRoundTime(3);
            Check(view.Player.GetUnit("A").Rank == UnitRank.Three && view.TokenMatchesState("A"), "Deferred rank reflected in preparation UI");
            Check(view.RoundTimerText.Contains("30s"), "Merge completed before timer");
            view.Match.Pool.AssertConservation(view.Match);
            return "Passed: combat bench merge, deferred board merge, automatic next-preparation Rank Three and matching token.";
        }

        public static string VerifyFailureRecovery()
        {
            var view = View();
            view.ResetMatchUI();
            view.Match.GetPlayer("p2").SetProgress(int.MaxValue, 0, 1);
            view.AdvanceRoundTime(30);
            Check(view.RoundFlow.Fault is OverflowException && view.Match.Phase == MatchPhase.Result, "Failed settlement pauses Result");
            Check(view.RoundTimerText.Contains("PAUSED") && view.RoundButton.interactable, "Visible pause and retry");
            view.Match.GetPlayer("p2").SetProgress(5, 0, 1);
            view.AdvanceRoundTime(100);
            Check(!view.RoundLoop.IsSettled, "No automatic retry storm");
            Click(view.RoundButton);
            Check(view.RoundFlow.Fault == null && view.RoundTimerText.Contains("RESULT / 3s"), "Settlement retry starts full result timer");
            view.AdvanceRoundTime(3);
            Check(view.Match.RoundNumber == 2, "Recovered automatic loop");

            view.ResetMatchUI();
            view.AdvanceRoundTime(30);
            var foeShop = view.Match.GetPlayer("p2").Shop;
            long revision = foeShop.Revision;
            typeof(ShopState).GetProperty("Revision").SetValue(foeShop, long.MaxValue);
            view.AdvanceRoundTime(3);
            Check(view.RoundLoop.NextPreparationPending && view.RoundFlow.Fault != null, "Partial shop refresh pauses");
            Check(view.RoundTimerText.Contains("PAUSED") && !view.ShopButton(0).interactable, "Pending shop blocks commands");
            long playerRevision = view.Player.Shop.Revision;
            ulong rng = view.Player.Shop.RandomState;
            typeof(ShopState).GetProperty("Revision").SetValue(foeShop, revision);
            Click(view.RoundButton);
            Check(!view.RoundLoop.NextPreparationPending && view.RoundFlow.Fault == null, "Shop retry succeeds");
            Check(view.Player.Shop.Revision == playerRevision && view.Player.Shop.RandomState == rng, "Completed player not redrawn");
            Check(view.RoundTimerText.Contains("PREPARATION / 30s") && view.ShopButton(0).interactable, "Full preparation restored");
            view.Match.Pool.AssertConservation(view.Match);
            return "Passed: paused error HUD, settlement and partial refresh pointer retries, full resumed timers, input gating and RNG preservation.";
        }

        public static string VerifyAutomaticDeployment()
        {
            var view = View();
            foreach (bool manual in new[] { false, true })
            {
                view.ResetMatchUI();
                view.Player.SetProgress(5, 0, 2);
                view.RefreshFromState();
                var ids = new string[3];
                int[] slots = { 0, 1, 3 };
                for (int i = 0; i < slots.Length; i++)
                {
                    Click(view.ShopButton(slots[i]));
                    ids[i] = view.LastPurchase.Unit.InstanceId;
                }
                Check(view.Player.DeployedUnitCount == 0, "Purchased roster starts on bench");
                view.BeginDrag(ids[0], view.ScreenPoint(view.Player.GetUnit(ids[0]).Placement));
                if (manual) Click(view.RoundButton);
                else view.AdvanceRoundTime(30);
                Check(!view.IsDragging && view.Match.Phase == MatchPhase.Combat, "Start cancels drag after automatic deployment");
                Check(view.Player.DeployedUnitCount == 2, "Automatic fill respects level capacity");
                Check(view.Player.GetUnit(ids[0]).Placement.Position.Value.Equals(new BoardPosition(0, 0)), "First bench unit at first free cell");
                Check(view.Player.GetUnit(ids[1]).Placement.Position.Value.Equals(new BoardPosition(1, 0)), "Second bench unit at second free cell");
                Check(view.Player.GetUnit(ids[2]).Placement.Kind == PlacementKind.Bench, "Surplus remains on bench");
                Check(view.RoundLoop.Battle.Units.Count(u => u.TeamId == 1) == 2, "Auto-deployed units in battle snapshot");
                if (!manual) MinimumMatchUITools.Capture("TestResults/WBS3.1/AutoDeployment/combat.png");
                FinishCombat(view);
                Check(view.TokenMatchesState(ids[0]) && view.TokenMatchesState(ids[1]), "Auto-deployed positions restored in result UI");
                Check(view.Player.GetUnit(ids[2]).Placement.Kind == PlacementKind.Bench, "Next level capacity waits for next battle");
                if (!manual) MinimumMatchUITools.Capture("TestResults/WBS3.1/AutoDeployment/result.png");
                view.Match.Pool.AssertConservation(view.Match);
            }
            return "Passed: pointer purchases stay on bench during preparation; timer and manual starts deploy lowest bench slots up to level; drag cancels; surplus remains; snapshot and result tokens match; pool preserved.";
        }

        public static string VerifyTenRounds()
        {
            var view = View();
            view.ResetMatchUI();
            Click(view.ShopButton(0));
            string id = view.LastPurchase.Unit.InstanceId;
            var move = new PlayerPlacementSystem().Move(view.Match, "p1", id,
                UnitPlacement.OnBoard(new BoardPosition(2, 0)), view.Player.PlacementRevision);
            Check(move.Accepted, "Deploy for automatic loop");
            view.RefreshFromState();
            for (int round = 1; round <= 10; round++)
            {
                view.AdvanceRoundTime(30);
                FinishCombat(view);
                Check(view.Player.LastEconomyRound == round && view.Player.LastAutomaticXPRound == round, "Exactly once rewards");
                view.AdvanceRoundTime(3);
                Check(view.Match.RoundNumber == round + 1 && view.RoundTimerText.Contains("30s"), "Fresh automatic preparation");
                Check(view.TokenMatchesState(id), "Owned token restored");
                view.Match.Pool.AssertConservation(view.Match);
            }
            return "Passed: 10 real battles through the UI Update path without Ready/Next input; rewards, refreshes, timers, tokens and pool preserved.";
        }
    }
}

