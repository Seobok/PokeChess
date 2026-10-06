using System;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Tests
{
    public sealed class LocalMatchSimulationTests
    {
        private static LocalMatchSimulation Run(ulong seed=123,int count=8,int hp=10)
        {
            var r=new LocalMatchSimulation(new LocalSimulationSettings(seed,count,flowRules:new RoundFlowRules(.2,.1)),matchRules:new MatchRules(startingHP:hp));r.RunToEnd();return r;
        }
        [TestCase(2)][TestCase(3)][TestCase(4)][TestCase(5)][TestCase(6)][TestCase(7)][TestCase(8)]
        public void BotsCompleteLegalMatchesWithAllUniquePlacements(int count)
        {
            var r=Run(count:count);Assert.That(r.Status,Is.EqualTo(SimulationStatus.Completed),r.Error);
            Assert.That(r.Match.FinalResult.Standings.Select(p=>p.Placement),Is.EqualTo(Enumerable.Range(1,count)));
            Assert.That(r.Rounds.All(round=>round.Settled),Is.True);
            Assert.That(r.Rounds.Count,Is.EqualTo(r.Match.RoundNumber));
            r.Match.Pool.AssertConservation(r.Match);
            Assert.That(r.Flow.MatchElapsedSeconds,Is.EqualTo(r.Rounds.Sum(x=>x.PreparationSeconds+x.CombatSeconds+x.ResultSeconds)).Within(1e-7));
            Assert.That(r.Rounds.Last().ResultSeconds,Is.Zero);
        }
        [Test] public void SameSeedAndDifferentFrameBudgetsHaveIdenticalGameTrace()
        {
            var a=Run();var b=new LocalMatchSimulation(new LocalSimulationSettings(123,8,flowRules:new RoundFlowRules(.2,.1)),matchRules:new MatchRules(startingHP:10));
            while(b.Status==SimulationStatus.Running)b.Advance(1d/30,1);
            Assert.That(SimulationReport.ToJson(a).Split(new[]{"\"executionSeconds\":"},StringSplitOptions.None)[0],Is.EqualTo(SimulationReport.ToJson(b).Split(new[]{"\"executionSeconds\":"},StringSplitOptions.None)[0]));
            Assert.That(Trace(a),Is.EqualTo(Trace(b)));Assert.That(a.Flow.MatchElapsedSeconds,Is.EqualTo(b.Flow.MatchElapsedSeconds));
        }
        private static string Trace(LocalMatchSimulation r)=>string.Join(";",r.Rounds.SelectMany(x=>x.Players.Select(p=>x.Round+":"+p.PlayerId+":"+p.HPAfter+":"+p.Battle?.OpponentId+":"+p.Battle?.Outcome+":"+p.Battle?.EndTick+":"+p.Battle?.IsShadow)));
        [Test] public void DifferentSeedsChangeTheMatchTrace()
        { Assert.That(Trace(Run(1)),Is.Not.EqualTo(Trace(Run(2)))); }
        [Test] public void OddSurvivorsProduceRecordedShadowAndEliminatedPlayersStopReceivingBattles()
        {
            var r=Run(count:7);Assert.That(r.Rounds.Any(x=>x.Players.Any(p=>p.Battle?.IsShadow==true)),Is.True);
            foreach(var round in r.Rounds)foreach(var p in round.Players.Where(p=>p.HPBefore==0))Assert.That(p.Battle,Is.Null);
        }
        [Test] public void CompletedRunIsFrozenAndDoesNotRestartTimeOrRewriteResults()
        {
            var r=Run();string report=SimulationReport.ToJson(r);r.Advance(10000);r.RunToEnd();r.Cancel();
            Assert.That(SimulationReport.ToJson(r),Is.EqualTo(report));
        }
        [Test] public void LimitsAndCancellationRemainIncomplete()
        {
            var ticks=new LocalMatchSimulation(new LocalSimulationSettings(maxCombatTicks:1,flowRules:new RoundFlowRules(.01,.01)));ticks.RunToEnd();
            Assert.That(ticks.Status,Is.EqualTo(SimulationStatus.TickLimit));Assert.That(ticks.Match.FinalResult,Is.Null);Assert.That(ticks.Flow.CombatTicks,Is.EqualTo(1));
            var rounds=new LocalMatchSimulation(new LocalSimulationSettings(maxRounds:1,flowRules:new RoundFlowRules(.01,.01)));rounds.RunToEnd();
            Assert.That(rounds.Status,Is.EqualTo(SimulationStatus.RoundLimit));Assert.That(rounds.Match.FinalResult,Is.Null);
            var cancel=new LocalMatchSimulation();cancel.Cancel();Assert.That(cancel.Status,Is.EqualTo(SimulationStatus.Cancelled));
            var summary=new SimulationBatchSummary(new[]{ticks,rounds,cancel});Assert.That(summary.Completed,Is.Zero);Assert.That(summary.MeanSeconds,Is.Null);Assert.That(summary.IncompleteRate,Is.EqualTo(1));
        }
        [Test] public void FaultIsRecordedWithoutInventingWinner()
        {
            var r=new LocalMatchSimulation();typeof(ShopState).GetProperty("Revision").SetValue(r.Match.GetPlayer("p1").Shop,long.MaxValue);
            r.RunToEnd();Assert.That(r.Status,Is.EqualTo(SimulationStatus.Faulted));Assert.That(r.Error,Is.Not.Empty);Assert.That(r.Match.FinalResult,Is.Null);
        }
        [Test] public void SummaryUsesCompletedGameTimeOnly()
        {
            var a=Run(1);var b=Run(2);var c=new LocalMatchSimulation();c.Cancel();
            var s=new SimulationBatchSummary(new[]{a,b,c});Assert.That(s.Total,Is.EqualTo(3));Assert.That(s.Completed,Is.EqualTo(2));Assert.That(s.IncompleteRate,Is.EqualTo(1d/3));
            Assert.That(s.MedianSeconds,Is.EqualTo((a.Flow.MatchElapsedSeconds+b.Flow.MatchElapsedSeconds)/2));Assert.That(s.MinSeconds,Is.LessThanOrEqualTo(s.MaxSeconds));
        }
        [Test] public void ReportUsesInvariantNumbersAndProperJsonEscapes()
        {
            var r=Run();var previous=CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture=new CultureInfo("fr-FR");var json=SimulationReport.ToJson(r);
                Assert.That(json,Does.Contain("\"status\":\"Completed\""));Assert.That(json,Does.Contain("\"standings\":["));Assert.That(json,Does.Contain("\"rounds\":["));
                Assert.That(SimulationReport.ToCsv(new[]{r}).Split('\n').Length,Is.EqualTo(3));
            }
            finally {CultureInfo.CurrentCulture=previous;}
        }
        [TestCase(double.NaN)][TestCase(double.PositiveInfinity)][TestCase(-1)]
        public void InvalidTimeCannotMutateRun(double time)
        {var r=new LocalMatchSimulation();Assert.Throws<ArgumentOutOfRangeException>(()=>r.Advance(time));Assert.That(r.Flow.MatchElapsedSeconds,Is.Zero);}
        [Test] public void ClockClampsPreparationAndCountsOnlyProcessedCombatTicks()
        {
            var r=new LocalMatchSimulation();r.Flow.AdvanceTime(1000);
            Assert.That(r.Flow.PreparationElapsedSeconds,Is.EqualTo(30));
            // Empty boards finish instantly, so no combat time or final result hold is added.
            Assert.That(r.Flow.CombatTicks,Is.Zero);Assert.That(r.Flow.MatchElapsedSeconds,Is.EqualTo(30));
            r.Flow.AdvanceTime(1000);Assert.That(r.Flow.ResultElapsedSeconds,Is.EqualTo(3));
        }
        [Test] public void LargeCombatInputCountsProcessedTicksAndKeepsBacklogOutOfDuration()
        {
            var r=new LocalMatchSimulation();new LocalSimulationBot(r.Catalog,123).Prepare(r.Match);
            r.Flow.AdvanceTime(30);Assert.That(r.Match.Phase,Is.EqualTo(MatchPhase.Combat));
            r.Flow.AdvanceTime(1000);Assert.That(r.Flow.CombatTicks,Is.EqualTo(60));
            Assert.That(r.Flow.CombatElapsedSeconds,Is.EqualTo(2));Assert.That(r.Flow.PendingCombatSeconds,Is.EqualTo(998).Within(1e-7));
            Assert.That(r.Flow.MatchElapsedSeconds,Is.EqualTo(32));
        }
    }
}
