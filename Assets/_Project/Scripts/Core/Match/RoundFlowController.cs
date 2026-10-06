using System;
using System.Linq;

namespace PokeChess.Core.Match
{
    // The caller supplies elapsed time. Core has no Unity clock, coroutine or UI dependency.
    public sealed class RoundFlowController
    {
        private readonly IRoundFlowRuntime runtime;
        private int observedRound;
        private MatchPhase observedPhase;
        private double phaseElapsed, combatAccumulator;
        private bool timerStarted;

        public RoundFlowRules Rules { get; }
        public MatchState Match => runtime.Match;
        public Exception Fault { get; private set; }
        public bool IsTimerRunning => timerStarted && Fault == null && TimedPhaseReady;
        public double RemainingSeconds => Math.Max(0, Duration - phaseElapsed);
        public double PendingCombatSeconds => combatAccumulator;
        public bool AwaitingMatchEnd => Match.Phase==MatchPhase.Preparation && Match.Players.Count(p=>p.HP>0 && !p.IsEliminated)<2;

        public RoundFlowController(IRoundFlowRuntime runtime, RoundFlowRules rules = null)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            if (runtime.TickRate < 1) throw new ArgumentException("Positive battle tick rate required.", nameof(runtime));
            Rules = rules ?? new RoundFlowRules();
            observedRound = Match.RoundNumber;
            observedPhase = Match.Phase;
            Synchronize();
        }

        private double Duration => Match.Phase == MatchPhase.Preparation ? Rules.PreparationSeconds
            : Match.Phase == MatchPhase.Result ? Rules.ResultSeconds : 0;
        private bool TimedPhaseReady => Match.Phase == MatchPhase.Preparation ? runtime.PreparationReady && !AwaitingMatchEnd
            : Match.Phase == MatchPhase.Result && runtime.IsSettled;

        // Also handles explicit sandbox actions performed outside this controller.
        public void RefreshState() => Synchronize();

        private bool Synchronize()
        {
            bool changed = observedRound != Match.RoundNumber || observedPhase != Match.Phase;
            if (changed)
            {
                observedRound = Match.RoundNumber;
                observedPhase = Match.Phase;
                phaseElapsed = combatAccumulator = 0;
                timerStarted = false;
            }
            if (!TimedPhaseReady)
            {
                timerStarted = false;
                phaseElapsed = 0;
            }
            if (TimedPhaseReady && Fault == null && !timerStarted)
            {
                timerStarted = true;
                return true;
            }
            return changed;
        }

        public void AdvanceTime(double elapsedSeconds)
        {
            if (double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds) || elapsedSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
            // A newly observed phase/readiness gets its entire duration. Old frame time is discarded.
            if (Synchronize() || Fault != null) return;
            if (Match.Phase == MatchPhase.Combat)
            {
                combatAccumulator = Math.Min(double.MaxValue - elapsedSeconds, combatAccumulator) + elapsedSeconds;
                Execute(() =>
                {
                    int ticks = 0;
                    double tickSeconds = 1d / runtime.TickRate;
                    while (combatAccumulator + 1e-9 >= tickSeconds && Match.Phase == MatchPhase.Combat
                        && ticks++ < Rules.MaxCombatTicksPerUpdate)
                    {
                        runtime.Step();
                        combatAccumulator = Math.Max(0, combatAccumulator - tickSeconds);
                    }
                    return true;
                });
                return;
            }
            if (!IsTimerRunning) return;
            phaseElapsed += Math.Min(elapsedSeconds, RemainingSeconds);
            if (RemainingSeconds > 1e-9) return;
            phaseElapsed = Duration;
            Execute(() =>
            {
                bool accepted = Match.Phase == MatchPhase.Preparation
                    ? runtime.StartCombat(Match.RoundNumber) : runtime.NextRound(Match.RoundNumber);
                if (!accepted) throw new InvalidOperationException("Automatic round transition was rejected.");
                return true;
            });
        }

        // Development skip/retry. The displayed round AND phase must still be current.
        public bool RequestAdvance(int expectedRound, MatchPhase expectedPhase)
        {
            if (Match.RoundNumber != expectedRound || Match.Phase != expectedPhase) return false;
            Synchronize();
            if (expectedPhase == MatchPhase.Preparation)
            {
                if (runtime.NextPreparationPending)
                    return Execute(() => runtime.NextRound(expectedRound - 1));
                if (!runtime.PreparationReady || AwaitingMatchEnd) return false;
                return Execute(() => runtime.StartCombat(expectedRound));
            }
            if (expectedPhase != MatchPhase.Result) return false;
            if (!runtime.IsSettled)
                return Execute(() => { runtime.SettleResult(); return true; });
            return Execute(() => runtime.NextRound(expectedRound));
        }

        private bool Execute(Func<bool> action)
        {
            try
            {
                bool accepted = action();
                if (accepted) Fault = null;
                return accepted;
            }
            catch (Exception error)
            {
                // No per-frame retry storm. An explicit retry/reset is required after a failure.
                Fault = error;
                return false;
            }
            finally { Synchronize(); }
        }
    }
}

