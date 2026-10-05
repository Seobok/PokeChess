using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
namespace PokeChess.Core.Tests
{
    public sealed class CombatSandboxGuardTests
    {
        private static string Diagnostics=>Path.Combine(CombatSandboxRunner.ReportFolder,"HarnessChecks");
        [Test] public void StepCallLimitFailsWithSeedTickAndStateDiagnostics()
        {
            var s=CombatSandboxScenarios.Create(0,4242);
            var error=Assert.Throws<InvalidOperationException>(()=>CombatSandboxRunner.Run(s,4242,"expected-limit",1,Diagnostics));
            Assert.That(error.Message,Does.Contain("seed=4242"));Assert.That(error.Message,Does.Contain("tick=1"));
            var content=File.ReadAllText(Path.Combine(Diagnostics,"MeleeDuel_4242_expected-limit_failure.txt"));
            Assert.That(content,Does.Contain("Step-call limit exceeded"));Assert.That(content,Does.Contain("Recent steps:"));Assert.That(content,Does.Contain("Current state:"));
        }
        [Test] public void ReusingEndedBattleCannotPassAsFreshExecution()
        {
            var s=CombatSandboxScenarios.Create(0,4243);s.Battle.Units.Single(u=>u.TeamId==2).SetVitals(0,0);
            CombatSandboxRunner.Run(s,4243,"warmup",diagnosticFolder:Diagnostics);
            var error=Assert.Throws<InvalidOperationException>(()=>CombatSandboxRunner.Run(s,4243,"expected-reuse",diagnosticFolder:Diagnostics));
            Assert.That(error.InnerException.Message,Does.Contain("Invalid final lifecycle"));
        }
    }
}
