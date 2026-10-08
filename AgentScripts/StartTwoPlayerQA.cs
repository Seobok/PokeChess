using UnityEngine;
using UnityEditor;
using PokeChess.Client.Services;
public static class StartTwoPlayerQA
{
    public static string Main()
    {
        if(!EditorApplication.isPlaying)throw new System.InvalidOperationException("Enter Play mode first.");
        EditorApplication.isPaused=false;
        typeof(Editor).Assembly.GetType("UnityEditor.LogEntries").GetMethod("SetConsoleFlag",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public).Invoke(null,new object[]{4,false});
        var qa=Object.FindFirstObjectByType<RoomBuildQA>()??new GameObject("RoomBuildQA").AddComponent<RoomBuildQA>();
        qa.Configure("TestResults/TwoPlayerRegression/Editor");
        return "Two-player QA configured";
    }
}
