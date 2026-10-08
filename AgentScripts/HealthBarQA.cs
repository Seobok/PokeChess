using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using PokeChess.Client.UI;
public static class HealthBarQA
{
    public static string Run()
    {
        var view = new GameObject("HealthBarVerification").AddComponent<PlacementSandboxView>();
        view.ResetSynergyDemo(); view.enabled = false;
        foreach (var room in UnityEngine.Object.FindObjectsByType<RoomFlowView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        { room.enabled = false; room.transform.Find("RoomCanvas").gameObject.SetActive(false); }
        view.AdvanceRound();
        var units = view.RoundLoop.GetBattleFor("p1").Battle.Units.Where(u => u.IsAlive && u.IsOnBoard).ToArray();
        float[] ratios = { 1, .5f, .1f };
        for (int i = 0; i < units.Length; i++) units[i].SetVitals(units[i].Stats.MaxHP * ratios[i % 3], units[i].Stats.MaxEnergy * ratios[i % 3]);
        view.RefreshFromState();
        foreach (var unit in units)
        {
            var node = view.GetComponentsInChildren<RectTransform>(true).First(t => t.name == "Fighter_" + unit.UnitInstanceId);
            var fill = (RectTransform)node.Find("HealthBar/Fill");
            if (Math.Abs(fill.sizeDelta.x - 40 * unit.CurrentHP / Math.Max(unit.Stats.MaxHP, unit.CurrentHP + unit.CurrentShield)) > .01) throw new Exception("HP ratio mismatch");
            var energyFill = (RectTransform)node.Find("EnergyBar/Fill");
            if (Math.Abs(energyFill.sizeDelta.x - (unit.Stats.MaxEnergy > 0 ? 40 * Math.Min(1, unit.CurrentEnergy / unit.Stats.MaxEnergy) : 0)) > .01) throw new Exception("Energy ratio mismatch");
            if (node.GetComponentsInChildren<UnityEngine.UI.Text>().Any(t => t.text.Contains("HP"))) throw new Exception("Numeric HP remains");
        }
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../TestResults/HealthBars")); Directory.CreateDirectory(folder);
        Capture(view, Path.Combine(folder, "combat.png"));
        var first = units[0]; first.SetVitals(first.Stats.MaxHP * .25f, first.CurrentEnergy); view.RefreshFromState();
        var fighter = view.GetComponentsInChildren<RectTransform>(true).First(t => t.name == "Fighter_" + first.UnitInstanceId);
        if (Math.Abs(((RectTransform)fighter.Find("HealthBar/Fill")).sizeDelta.x - 40 * first.CurrentHP / Math.Max(first.Stats.MaxHP, first.CurrentHP + first.CurrentShield)) > .01) throw new Exception("Damage update");
        first.SetVitals(first.Stats.MaxHP, first.CurrentEnergy); view.RefreshFromState();
        if (Math.Abs(((RectTransform)fighter.Find("HealthBar/Fill")).sizeDelta.x - 40 * first.CurrentHP / Math.Max(first.Stats.MaxHP, first.CurrentHP + first.CurrentShield)) > .01) throw new Exception("Heal update");
        var method = typeof(PlacementSandboxView).GetMethod("RenderUnitHealthBar", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Invoke(view, new object[] { fighter, 0d, 100d, false, 0d });
        if (fighter.Find("HealthBar").gameObject.activeSelf) throw new Exception("Dead bar visibility");
        method.Invoke(view, new object[] { fighter, 200d, 100d, false, 0d });
        if (((RectTransform)fighter.Find("HealthBar/Fill")).sizeDelta.x != 40) throw new Exception("Clamp");
        method.Invoke(view, new object[] { fighter, 10d, 0d, true, 0d });
        if (((RectTransform)fighter.Find("HealthBar/Fill")).sizeDelta.x != 0) throw new Exception("Zero max HP");
        var energyMethod = typeof(PlacementSandboxView).GetMethod("RenderUnitEnergyBar", BindingFlags.Instance | BindingFlags.NonPublic);
        foreach (double energy in new double[] { 0, 50, 100, 150 }) {
            energyMethod.Invoke(view, new object[] { fighter, energy, 100d, true });
            if (Math.Abs(((RectTransform)fighter.Find("EnergyBar/Fill")).sizeDelta.x - 40 * Math.Min(1, energy / 100)) > .01) throw new Exception("Energy gain/spend/overflow");
        }
        energyMethod.Invoke(view, new object[] { fighter, 10d, 0d, true });
        if (fighter.Find("EnergyBar").gameObject.activeSelf) throw new Exception("Zero max energy");
        energyMethod.Invoke(view, new object[] { fighter, 50d, 100d, false });
        if (fighter.Find("EnergyBar").gameObject.activeSelf) throw new Exception("Dead energy bar");
        method.Invoke(view, new object[] { fighter, 100d, 100d, true, 25d });
        var shieldBar = (RectTransform)fighter.Find("HealthBar/Shield");
        if (!shieldBar.gameObject.activeSelf || Math.Abs(shieldBar.sizeDelta.x - 8) > .01 || shieldBar.pivot.x != 1 || shieldBar.GetComponent<UnityEngine.UI.Image>().color != Color.white) throw new Exception("Right white shield");
        method.Invoke(view, new object[] { fighter, 100d, 100d, true, 10d });
        if (Math.Abs(shieldBar.sizeDelta.x - 40f / 11) > .01) throw new Exception("Shield depletion");
        method.Invoke(view, new object[] { fighter, 100d, 100d, true, 0d });
        if (shieldBar.gameObject.activeSelf) throw new Exception("Zero shield visibility");
        File.WriteAllText(Path.Combine(folder, "playmode.txt"), "Passed: full/half/low HP, damage, heal, dead visibility, clamp, zero max HP, no numeric overhead HP.");
        return "Health bar QA passed.";
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




