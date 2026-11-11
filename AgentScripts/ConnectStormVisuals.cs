using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using PokeChess.Client.UI;
public static class ConnectStormVisuals
{
    public static string Apply()
    {
        var art=AssetDatabase.LoadAssetAtPath<StormVisuals>("Assets/_Project/Resources/StormVisuals.asset");
        if(art==null)throw new InvalidOperationException("StormVisuals missing");
        var specs=new[]{("badgeTwo","storm-badge-two",8,32),("badgeThree","storm-badge-three",8,32),
            ("ready","storm-charge-ready",5,64),("spent","storm-charge-spent",7,64),
            ("slowEnter","storm-slow-enter",3,64),("slowHold","storm-slow-hold",6,64),("slowExit","storm-slow-exit",2,64)};
        foreach(var spec in specs) {
            string path="Assets/_Project/Sprites/Effects/Synergies/storm/"+spec.Item2+".png";
            var frames=AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().OrderBy(s=>int.Parse(s.name.Substring(s.name.LastIndexOf('_')+1))).ToArray();
            if(frames.Length!=spec.Item3||frames.Any(s=>s.rect.width!=spec.Item4||s.rect.height!=spec.Item4))throw new InvalidOperationException("Unexpected authored slices: "+path);
            typeof(StormVisuals).GetField(spec.Item1).SetValue(art,frames);
        }
        EditorUtility.SetDirty(art);AssetDatabase.SaveAssets();
        return "Connected 7 storm sheets; preserved authored sprite IDs and slices.";
    }
}
