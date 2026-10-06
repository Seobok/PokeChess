using System;
using System.IO;
using PokeChess.Core.Match;
using UnityEngine;

namespace PokeChess.Client.UI
{
    public sealed partial class PlacementSandboxView
    {
        public LocalMatchSimulation Simulation { get; private set; }
        public string SimulationSaveDirectory { get; private set; }
        public string SimulationStatusText=>simulationInfo?.text;
        public UnityEngine.UI.Button SimulationStartButton { get; private set; }
        public UnityEngine.UI.Button SimulationSpeedButton { get; private set; }
        private UnityEngine.UI.Text simulationInfo;
        private bool simulationFast;
        private LocalMatchSimulation savedSimulation;
        private string simulationSaveError;
        private void BuildSimulationUI()
        {
            Panel("SimulationPanel",new Vector2(205,135),new Vector2(-510,-210));
            SimulationStartButton=ActionButton("Simulate 8",new Vector2(98,25),new Vector2(-561,-160),()=>StartLocalSimulation());
            SimulationSpeedButton=ActionButton("Speed: 1x",new Vector2(98,25),new Vector2(-459,-160),ToggleSimulationSpeed);
            simulationInfo=Label("SimulationStatus",canvasRect,"LOCAL SIMULATION\nSeed 123 / 8 players",11,new Vector2(195,90),new Vector2(-510,-222));
        }
        public void StartLocalSimulation(ulong seed=123,bool fast=false)
        {
            CancelDrag("Simulation started.");Simulation?.Cancel();
            var run=new LocalMatchSimulation(new LocalSimulationSettings(seed));
            catalog=run.Catalog;Match=run.Match;AttachMinimumMatch();
            Simulation=run;RoundLoop=run.Coordinator;RoundFlow=run.Flow;simulationFast=fast;
            savedSimulation=null;simulationSaveError=null;SimulationSaveDirectory=null;
            shopCollapsed=true;LastResult=null;
            Feedback("8 local bots / Seed "+seed+" / automatic match and duration recording.");Render();RenderSimulationUI();
        }
        public void ToggleSimulationSpeed() { simulationFast=!simulationFast;RenderSimulationUI(); }
        private void SaveSimulation()
        {
            if(Simulation==null || Simulation.Status==SimulationStatus.Running || savedSimulation==Simulation)return;
            // Attempt once per run to avoid per-frame IO failures; explicit retry is available.
            try
            {
                SimulationSaveDirectory=Path.Combine(Application.persistentDataPath,"MatchSimulations",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N").Substring(0,8));
                Directory.CreateDirectory(SimulationSaveDirectory);
                File.WriteAllText(Path.Combine(SimulationSaveDirectory,"match.json"),SimulationReport.ToJson(Simulation));
                File.WriteAllText(Path.Combine(SimulationSaveDirectory,"summary.csv"),SimulationReport.ToCsv(new[]{Simulation}));
                savedSimulation=Simulation;simulationSaveError=null;
            }
            catch(Exception error) { simulationSaveError=error.Message;savedSimulation=Simulation; }
        }
        public void RetrySimulationSave() { savedSimulation=null;SaveSimulation();RenderSimulationUI(); }
        private void RenderSimulationUI()
        {
            if(simulationInfo==null)return;
            ButtonText(SimulationSpeedButton,simulationFast ? "Speed: fast" : "Speed: 1x");
            var run=Simulation;
            if(run==null) { simulationInfo.text="LOCAL SIMULATION\nSeed 123 / 8 players";return; }
            simulationInfo.text="SEED "+run.Settings.Seed+" / "+run.Status+"\nR"+Match.RoundNumber+" / ALIVE "+run.AliveCount+"\nGAME "+run.Flow.MatchElapsedSeconds.ToString("0.0")+"s / WALL "+run.ExecutionSeconds.ToString("0.0")+"s\n"+
                (simulationSaveError!=null ? "SAVE FAILED" : savedSimulation==run ? "JSON / CSV SAVED" : "AUTO RECORDING");
            if(run.Status==SimulationStatus.Running)
            {
                roundButton.interactable=surrenderButton.interactable=false;
                foreach(var button in shopCards)button.interactable=false;
                xpButton.interactable=rerollButton.interactable=lockButton.interactable=sellButton.interactable=shopToggle.interactable=false;
            }
        }
    }
}
