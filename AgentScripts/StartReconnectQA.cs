using UnityEngine;
using UnityEditor;
using PokeChess.Client.Services;
public static class StartReconnectQA
{
    public static string Main()
    {
        if(!EditorApplication.isPlaying)throw new System.InvalidOperationException("Enter Play mode first.");
        EditorApplication.isPaused=false;
        typeof(Editor).Assembly.GetType("UnityEditor.LogEntries").GetMethod("SetConsoleFlag",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public).Invoke(null,new object[]{4,false});
        var qa=Object.FindFirstObjectByType<RoomBuildQA>()??new GameObject("RoomBuildQA").AddComponent<RoomBuildQA>();
        qa.Configure("TestResults/WBS6.1/Editor");
        return "Editor QA configured";
    }
}
