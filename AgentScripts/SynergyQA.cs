using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using PokeChess.Client.UI;
using PokeChess.Core.Battle;
using PokeChess.Core.Match;

public static class SynergyQA
{
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    public static string Run()
    {
        Check(UnityEditor.EditorApplication.isPlaying, "Play mode required.");
        var view = new GameObject("SynergyVerification").AddComponent<PlacementSandboxView>();
        view.ResetSynergyDemo(); view.enabled = false;
        foreach (var room in UnityEngine.Object.FindObjectsByType<RoomFlowView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        { room.enabled = false; room.transform.Find("RoomCanvas").gameObject.SetActive(false); }
        typeof(PlacementSandboxView).GetMethod("AnimateRosterSprites", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(view, null);
        Check(view.SynergyDisplay.Contains("Guardian 3/3") && view.SynergyDisplay.Contains("Assault 2/3") && view.SynergyDisplay.Contains("Link 3/3"), "Preparation counts.");
        view.SelectUnit("p1-synergy-3"); Check(view.DetailText.Contains("Guardian") && view.DetailText.Contains("Link"), "Unit traits.");
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../TestResults/Synergy")); Directory.CreateDirectory(folder);
        Capture(view, Path.Combine(folder, "preparation.png"));
        var trait = view.transform.Find("PlacementCanvas/Synergies/guardian").gameObject;
        ExecuteEvents.Execute(trait, new PointerEventData(EventSystem.current), ExecuteEvents.pointerEnterHandler);
        view.RefreshFromState();
        var help = view.transform.Find("PlacementCanvas/SynergyHelp");
        Check(help.gameObject.activeSelf && help.GetComponentInChildren<UnityEngine.UI.Text>().text.Contains("140 shield"), "Tooltip.");
        Capture(view, Path.Combine(folder, "tooltip.png"));
        ExecuteEvents.Execute(trait, new PointerEventData(EventSystem.current), ExecuteEvents.pointerExitHandler);
        view.AdvanceRound(); var battle = view.RoundLoop.GetBattleFor("p1").Battle;
        Check(battle.Units.First(u => u.UnitInstanceId == "p1-synergy-0").CurrentShield == 140, "Starting guardian shield.");
        bool link = false, assault = false, guard = false;
        for (int i = 0; i < 1400 && view.Match.Phase == MatchPhase.Combat; i++)
        {
            view.RoundLoop.Step();
            foreach (var e in battle.Synergies.EventsThisTick) { link |= e.TraitId == "link"; assault |= e.Detail == "STRIKE"; guard |= e.Detail == "GUARD"; }
            if (i == 120) { view.RefreshFromState(); Capture(view, Path.Combine(folder, "combat.png")); }
        }
        view.RefreshFromState();
        Check(view.Match.Phase != MatchPhase.Combat && link && assault && guard, "Combat completion and all active effects.");
        view.Match.Pool.AssertConservation(view.Match);
        var report = "Passed: Guardian 3 / Assault 2 / Link 3, unit details, tooltip, start shields, Link/Assault/Guardian effects, battle completion, pool conservation.";
        File.WriteAllText(Path.Combine(folder, "playmode.txt"), report);
        return report;
    }
    private static void Capture(PlacementSandboxView view, string path)
    {
        var canvas = view.GetComponentInChildren<Canvas>(); var scaler = canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
        var oldMode = canvas.renderMode; var oldCamera = canvas.worldCamera; var oldScale = canvas.scaleFactor; var oldPlane = canvas.planeDistance; bool oldEnabled = scaler.enabled;
        var obj = new GameObject("SynergyQACamera"); var camera = obj.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.035f,.06f,.1f);
        var target = new RenderTexture(1280,720,24); target.Create(); var oldTarget = RenderTexture.active; Texture2D image = null;
        try
        {
            camera.targetTexture = target; scaler.enabled = false; canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera; canvas.planeDistance = 1; canvas.scaleFactor = 1; Canvas.ForceUpdateCanvases(); camera.Render();
            RenderTexture.active = target; image = new Texture2D(1280,720,TextureFormat.RGBA32,false);
            image.ReadPixels(new Rect(0,0,1280,720),0,0); image.Apply(); File.WriteAllBytes(path,image.EncodeToPNG());
        }
        finally
        {
            canvas.renderMode = oldMode; canvas.worldCamera = oldCamera; canvas.scaleFactor = oldScale; canvas.planeDistance = oldPlane; scaler.enabled = oldEnabled;
            RenderTexture.active = oldTarget; camera.targetTexture = null; UnityEngine.Object.Destroy(obj);
            if(image != null) UnityEngine.Object.Destroy(image); target.Release(); UnityEngine.Object.Destroy(target);
        }
    }
}
