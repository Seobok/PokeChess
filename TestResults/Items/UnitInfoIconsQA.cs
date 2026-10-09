using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using PokeChess.Client.UI;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;
public static class UnitInfoIconsQA
{
    public static string Link()
    {
        const string path="Assets/_Project/Resources/UnitInfoIcons.asset";
        var library=AssetDatabase.LoadAssetAtPath<UnitInfoIconLibrary>(path);
        if(library==null){library=ScriptableObject.CreateInstance<UnitInfoIconLibrary>();AssetDatabase.CreateAsset(library,path);}
        string[] names={"HP","Attack","AttackSpeed","Armor","MagicResistance","Range","Energe"};
        // Some images have tiny fragments from automatic slicing. Use the main sprite.
        library.icons=names.Select(n=>AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Sprites/Icon/"+n+".png").OfType<Sprite>().OrderByDescending(s=>s.rect.width*s.rect.height).FirstOrDefault()).ToArray();
        if(library.icons.Any(i=>i==null))throw new Exception("Missing stat icon.");
        EditorUtility.SetDirty(library);AssetDatabase.SaveAssets();return "Linked seven original stat icons.";
    }
    public static string Run()
    {
        var flags=BindingFlags.NonPublic|BindingFlags.Instance;var root=new GameObject("UnitInfoQA");const string output="TestResults/Items/UnitInfoIconsQA";Directory.CreateDirectory(output);
        try{
            var view=root.AddComponent<PlacementSandboxView>();if(view.Match==null)typeof(PlacementSandboxView).GetMethod("Awake",flags).Invoke(view,null);view.enabled=false;
            var catalog=PrototypeRoster.CreateCatalog();var unit=catalog.CreateUnit("info-qa","geodude","p1",UnitRank.Two,0,UnitPlacement.OnBoard(new BoardPosition(2,3)));view.Player.SetProgress(100,0,3);SharedPoolSystem.RegisterUnit(view.Match,unit,"geodude");view.RefreshFromState();view.SelectUnit(unit.InstanceId);
            var panel=(RectTransform)typeof(PlacementSandboxView).GetField("unitInfoRoot",flags).GetValue(view);
            if(!panel.gameObject.activeSelf||panel.GetComponentsInChildren<UnityEngine.UI.Image>().Count(i=>i.name=="Icon"&&i.sprite!=null)!=7)throw new Exception("Missing stat icons.");
            var labels=(UnityEngine.UI.Text[])typeof(PlacementSandboxView).GetField("unitStatValues",flags).GetValue(view);
            var stats=RankRules.Default.Apply(catalog.Get("geodude").BaseStats,UnitRank.Two);
            if(labels[0].text!=stats.MaxHP.ToString()+"/"+stats.MaxHP||labels[5].text!=stats.AttackRange.ToString())throw new Exception("Wrong stat values.");
            var qa=root.AddComponent<ContentAlphaBuildQA>();typeof(ContentAlphaBuildQA).GetField("view",flags).SetValue(qa,view);typeof(ContentAlphaBuildQA).GetField("output",flags).SetValue(qa,output);typeof(ContentAlphaBuildQA).GetMethod("Capture",flags).Invoke(qa,new object[]{"unit-info.png"});
            typeof(PlacementSandboxView).GetMethod("ShowUnitInfoHint",flags).Invoke(view,new object[]{0});
            var tooltip=(UnityEngine.UI.Text)typeof(PlacementSandboxView).GetField("unitInfoTooltipText",flags).GetValue(view);if(!tooltip.text.StartsWith("체력"))throw new Exception("Missing Korean hint.");
            typeof(ContentAlphaBuildQA).GetMethod("Capture",flags).Invoke(qa,new object[]{"stat-tooltip.png"});view.SelectUnit(null);
            if(panel.gameObject.activeSelf||tooltip.transform.parent.gameObject.activeSelf)throw new Exception("Unselected info not hidden.");
            view.SelectUnit(unit.InstanceId);root.GetComponent<PokemonCryPlayer>().Source.mute=true;root.GetComponent<CombatSoundPlayer>().Mute=true;
            if(!view.RoundLoop.StartCombat(1))throw new Exception("Battle rejected.");view.RoundLoop.Step();view.RefreshFromState();
            var fighter=view.RoundLoop.GetBattleFor("p1").Battle.Units.First(u=>u.UnitInstanceId==unit.InstanceId);
            string hp=fighter.CurrentHP.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture)+"/"+fighter.Stats.MaxHP.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture);
            if(labels[0].text!=hp)throw new Exception("Live HP not displayed.");
            typeof(PlacementSandboxView).GetMethod("ShowUnitInfoHint",flags).Invoke(view,new object[]{-1});if(!tooltip.text.StartsWith("바위 수호"))throw new Exception("Skill title not localized.");
            typeof(ContentAlphaBuildQA).GetMethod("Capture",flags).Invoke(qa,new object[]{"skill-tooltip.png"});
            File.WriteAllText(output+"/result.json","{\"status\":\"Passed\",\"icons\":7,\"rankStats\":true,\"koreanTooltip\":true,\"emptySelection\":true}");return "Passed: seven icons, rank stats, Korean tooltip and empty state.";
        }finally{UnityEngine.Object.DestroyImmediate(root);}
    }
}
