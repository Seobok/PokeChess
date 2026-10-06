using System;
using System.Collections.Generic;
using System.IO;
using PokeChess.Core.Match;
using UnityEditor;
using UnityEngine;

namespace PokeChess.Editor
{
    // Editor-update chunks keep a seed sweep responsive; each match uses the same runtime runner.
    public static class LocalSimulationBatchTools
    {
        private static readonly List<LocalMatchSimulation> runs=new List<LocalMatchSimulation>();
        private static LocalMatchSimulation current;
        private static ulong firstSeed;
        private static int count;
        public static bool IsRunning { get; private set; }
        public static string OutputDirectory { get; private set; }
        public static int CompletedCount=>runs.Count;
        public static string Error { get; private set; }
        [MenuItem("PokeChess/Simulation/Run 10 Seeds (8 Players)")]
        public static void RunTenSeeds()=>StartBatch(1,10,Path.Combine(Application.dataPath,"../TestResults/WBS3.7/Batch-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")));
        public static void StartBatch(ulong seed,int matches,string outputDirectory)
        {
            if(IsRunning)throw new InvalidOperationException("A simulation batch is already running.");
            if(matches<1 || matches>1000)throw new ArgumentOutOfRangeException(nameof(matches));
            if(string.IsNullOrWhiteSpace(outputDirectory))throw new ArgumentException("Output directory required.");
            // Verify the entire seed range before creating files or starting a match.
            checked {var last=seed+(ulong)(matches-1);}
            OutputDirectory=Path.GetFullPath(outputDirectory);Directory.CreateDirectory(OutputDirectory);
            firstSeed=seed;count=matches;runs.Clear();Error=null;current=null;IsRunning=true;
            EditorApplication.update+=Update;
        }
        [MenuItem("PokeChess/Simulation/Cancel Seed Batch")]
        public static void Cancel()
        {
            if(!IsRunning)return;
            current?.Cancel();
            if(current!=null)StoreCurrent();
            Finish();
        }
        private static void Update()
        {
            if(!IsRunning)return;
            try
            {
                if(current==null)current=new LocalMatchSimulation(new LocalSimulationSettings(firstSeed+(ulong)runs.Count));
                current.Advance(10,300);
                if(current.Status!=SimulationStatus.Running)
                {
                    StoreCurrent();current=null;
                    if(runs.Count==count)Finish();
                }
            }
            catch(Exception e) {Error=e.ToString();IsRunning=false;EditorApplication.update-=Update;Debug.LogError("Simulation batch failed: "+e.Message);}
        }
        private static void StoreCurrent()
        {
            File.WriteAllText(Path.Combine(OutputDirectory,"seed-"+current.Settings.Seed+".json"),SimulationReport.ToJson(current));runs.Add(current);
            File.WriteAllText(Path.Combine(OutputDirectory,"summary.csv"),SimulationReport.ToCsv(runs));
        }
        [Serializable] private sealed class Summary
        {
            public int total,completed;
            public double incompleteRate,meanGameSeconds,medianGameSeconds,minGameSeconds,maxGameSeconds;
            public bool hasCompletedMatches;
            public string botPolicy,content;
        }
        private static void Finish()
        {
            IsRunning=false;EditorApplication.update-=Update;
            var s=new SimulationBatchSummary(runs);
            File.WriteAllText(Path.Combine(OutputDirectory,"statistics.json"),JsonUtility.ToJson(new Summary {
                total=s.Total,completed=s.Completed,incompleteRate=s.IncompleteRate,hasCompletedMatches=s.Completed>0,
                meanGameSeconds=s.MeanSeconds??0,medianGameSeconds=s.MedianSeconds??0,minGameSeconds=s.MinSeconds??0,maxGameSeconds=s.MaxSeconds??0,
                botPolicy=LocalMatchSimulation.PolicyVersion,content=LocalMatchSimulation.ContentVersion},true));
            Debug.Log("Simulation batch complete: "+s.Completed+" / "+s.Total+"; "+OutputDirectory);
        }
    }
}
