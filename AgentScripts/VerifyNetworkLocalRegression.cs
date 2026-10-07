using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;
public static class VerifyNetworkLocalRegression
{
    public static string Main(){Run();return "Local regression started";}
    private static async void Run()
    {
        try {
            var run=new LocalMatchSimulation(new LocalSimulationSettings(1),PrototypeRoster.CreateCatalog(),PrototypeRoster.CreateDefinitions().Select(d=>d.Id),skillCatalog:PrototypeRoster.CreateSkills(),statusCatalog:PrototypeRoster.CreateStatuses());
            while(run.Status==SimulationStatus.Running){run.Advance(10,300);await Task.Yield();}
            if(run.Status!=SimulationStatus.Completed)throw new Exception(run.Error);
            run.Match.Pool.AssertConservation(run.Match);
            File.WriteAllText("TestResults/WBS5.2/local-regression-result.json","{\"status\":\"Completed\",\"endRound\":"+run.Match.RoundNumber+"}");
        }catch(Exception e){File.WriteAllText("TestResults/WBS5.2/local-regression-result.json",e.ToString());}
    }
}
