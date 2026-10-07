using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using PokeChess.Client.UI;
public static class StartContentUIQA {
    public static string Main() {
        var v=UnityEngine.Object.FindFirstObjectByType<PlacementSandboxView>();
        File.WriteAllText("C:/Users/user/Desktop/PokeChess/PokeChess/TestResults/WBS4.10/editor-settings.json","{\"previousRunInBackground\":"+Application.runInBackground.ToString().ToLowerInvariant()+"}");
        Application.runInBackground=true;v.StartLocalSimulation(1,true);EditorApplication.isPaused=false;
        return "Seed 1 actual Update-driven 8-player match started.";
    }
}
