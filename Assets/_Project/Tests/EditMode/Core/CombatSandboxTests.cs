using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using PokeChess.Core.Battle;
namespace PokeChess.Core.Tests
{
    public sealed class CombatSandboxTests
    {
        private readonly List<(string scenario,ulong seed,string variant,CombatSandboxResult result,double milliseconds)> runs=
            new List<(string,ulong,string,CombatSandboxResult,double)>();
        private bool failed;
        [OneTimeSetUp] public void StartReport()
        {
            Directory.CreateDirectory(CombatSandboxRunner.ReportFolder);
            File.WriteAllText(Path.Combine(CombatSandboxRunner.ReportFolder,"runs.csv"),
                "Scenario,Seed,Variant,Result,EndReason,EndTick,Steps,Overtimes,Moves,DamageEvents,ProjectileSpawns,ProjectileHits,Casts,Heals,Shields,StatusApplications,Stacks,Ignored,Deaths,LongestQuietTicks,Milliseconds,FinalTickHash\n");
            File.WriteAllText(Path.Combine(CombatSandboxRunner.ReportFolder,"summary.md"),"# Combat Sandbox WBS 1.17\n\nRun started: "+DateTime.UtcNow.ToString("O")+"\nStatus: Running\n");
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)]
        [TestCase(5)] [TestCase(6)] [TestCase(7)] [TestCase(8)] [TestCase(9)]
        [Timeout(600000)]
        public void RepresentativeBattlesCompleteAndReplayIdentically(int scenario)
        {
            ulong currentSeed=0;
            try {
                for(ulong seed=1001;seed<=1010;seed++) {
                    currentSeed=seed;
                    var baseline=Run(scenario,seed,"baseline");
                    Coverage(scenario,seed,baseline);
                    var replay=Run(scenario,seed,"replay");Compare(scenario,seed,"replay",baseline,replay);
                    var reversed=Run(scenario,seed,"reversed");Compare(scenario,seed,"reversed",baseline,reversed);
                }
            } catch(Exception error) {
                failed=true;
                File.WriteAllText(Path.Combine(CombatSandboxRunner.ReportFolder,CombatSandboxScenarios.Names[scenario]+"_"+currentSeed+"_test_failure.txt"),error.ToString());
                throw;
            }
        }
        private CombatSandboxResult Run(int scenario,ulong seed,string variant)
        {
            var stopwatch=System.Diagnostics.Stopwatch.StartNew();
            var result=CombatSandboxRunner.Run(CombatSandboxScenarios.Create(scenario,seed,variant=="reversed"),seed,variant);
            stopwatch.Stop();runs.Add((CombatSandboxScenarios.Names[scenario],seed,variant,result,stopwatch.Elapsed.TotalMilliseconds));
            File.AppendAllText(Path.Combine(CombatSandboxRunner.ReportFolder,"runs.csv"),string.Join(",",new object[]{
                CombatSandboxScenarios.Names[scenario],seed,variant,result.Result,result.Reason,result.EndTick,result.Steps,result.Overtimes,
                result.Moves,result.Hits,result.Spawns,result.ProjectileHits,result.Casts,result.Heals,result.Shields,result.Statuses,result.Stacks,
                result.Ignored,result.Deaths,result.LongestQuiet,stopwatch.Elapsed.TotalMilliseconds.ToString("F3",CultureInfo.InvariantCulture),result.TickHashes.Last()})+"\n");
            return result;
        }
        private static void Compare(int scenario,ulong seed,string variant,CombatSandboxResult expected,CombatSandboxResult actual)
        {
            Assert.That(actual.InitialDefinitions,Is.EqualTo(expected.InitialDefinitions),"Definition mismatch");
            int mismatch=Enumerable.Range(0,Math.Min(expected.TickHashes.Count,actual.TickHashes.Count))
                .Where(i=>expected.TickHashes[i]!=actual.TickHashes[i]).DefaultIfEmpty(-1).First();
            Assert.That(actual.TickHashes.Count,Is.EqualTo(expected.TickHashes.Count),"Trace length: "+CombatSandboxScenarios.Names[scenario]+" seed="+seed+" "+variant);
            Assert.That(mismatch,Is.EqualTo(-1),"First divergent Step="+mismatch+" scenario="+CombatSandboxScenarios.Names[scenario]+" seed="+seed+" variant="+variant);
            Assert.That(actual.FinalState,Is.EqualTo(expected.FinalState),"Final state mismatch");
        }
        private static void Coverage(int scenario,ulong seed,CombatSandboxResult r)
        {
            var context=CombatSandboxScenarios.Names[scenario]+" seed="+seed;
            Assert.That(r.Hits,Is.GreaterThan(0),context+" did not deal damage");
            if(scenario!=4)Assert.That(r.Moves,Is.GreaterThan(0),context+" did not move");
            if(scenario==1||scenario==2||scenario==3||scenario==5) {
                Assert.That(r.Spawns,Is.GreaterThan(0),context+" did not spawn projectiles");Assert.That(r.ProjectileHits,Is.GreaterThan(0),context+" had no projectile hits");
            }
            if(scenario==4)Assert.That(r.TargetRngDraws,Is.GreaterThan(0),context+" did not exercise targeting RNG");
            if(scenario==7)Assert.That(r.CcKinds.Count,Is.EqualTo(6),context+" missing CC kinds");
            if(scenario==8)Assert.That(r.Policies.Count,Is.EqualTo(4),context+" missing stack policies");
            if(scenario>=5&&scenario<=8)Assert.That(r.Casts,Is.GreaterThan(0),context+" had no casts");
            if(scenario==6){Assert.That(r.Heals,Is.GreaterThan(0),context+" no effective healing");Assert.That(r.Shields,Is.GreaterThan(0),context+" no shields");}
            if(scenario==7||scenario==8)Assert.That(r.Statuses,Is.GreaterThan(0),context+" no statuses");
            if(scenario==8){Assert.That(r.Stacks,Is.GreaterThan(0),context+" no stacking");Assert.That(r.Ignored,Is.GreaterThan(0),context+" None policy not exercised");}
            if(scenario==9){Assert.That(r.Overtimes,Is.EqualTo(1));Assert.That(r.Result,Is.EqualTo(BattleResult.Draw));Assert.That(r.Reason,Is.EqualTo(BattleEndReason.TimeLimit));}
        }
        [OneTimeTearDown] public void FinishReport()
        {
            var text=new StringBuilder("# Combat Sandbox WBS 1.17\n\nCompleted UTC: "+DateTime.UtcNow.ToString("O")+"\n\n");
            text.Append("Execution checks: ").Append(!failed&&runs.Count==300?"Passed (Unity test verdict is authoritative)":"Incomplete or failed").Append("\n\n");
            text.Append("Baseline: ").Append(runs.Count(r=>r.variant=="baseline")).Append("; total executions: ").Append(runs.Count).Append("\n\n");
            text.Append("| Scenario | Baseline runs | Team 1 wins | Team 2 wins | Draws | OT runs | End Tick min/max |\n|---|---:|---:|---:|---:|---:|---|\n");
            foreach(var g in runs.Where(r=>r.variant=="baseline").GroupBy(r=>r.scenario))
                text.Append('|').Append(g.Key).Append('|').Append(g.Count()).Append('|').Append(g.Count(r=>r.result.Result==BattleResult.TeamOneWin)).Append('|')
                    .Append(g.Count(r=>r.result.Result==BattleResult.TeamTwoWin)).Append('|').Append(g.Count(r=>r.result.Result==BattleResult.Draw)).Append('|')
                    .Append(g.Count(r=>r.result.Overtimes>0)).Append('|').Append(g.Min(r=>r.result.EndTick)).Append('/').Append(g.Max(r=>r.result.EndTick)).Append("|\n");
            text.Append("\nTotal execution milliseconds (includes validation/hashing; reference only): ")
                .Append(runs.Sum(r=>r.milliseconds).ToString("F3",CultureInfo.InvariantCulture)).Append("\n\nSee runs.csv for each Seed and variant. Failure diagnostics are generated only on failure; old failure files may be from earlier runs.\n");
            File.WriteAllText(Path.Combine(CombatSandboxRunner.ReportFolder,"summary.md"),text.ToString());
            TestContext.Progress.WriteLine("Combat Sandbox report: "+CombatSandboxRunner.ReportFolder);
        }
    }
}
