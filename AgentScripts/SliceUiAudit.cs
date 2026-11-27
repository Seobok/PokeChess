using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
public static class SliceUiAudit
{
    static readonly HashSet<string> Names = new HashSet<string>(("BenchFrame BenchLabelPlate InventoryEpBadge InventoryHeader InventoryPageBadge SpectatorListHeader SpectatorReturnButton SpectatorRow SpectatorRowEliminated SpectatorRowSelected SpectatorWatchBanner ShopActionButton ShopEconomyFrame ShopGoldPanel ShopLevelPanel ShopPriceBadge ShopSynergyBadge SynergyHeader SynergyRow SynergyRowInactive SynergyRowSelected SynergyThresholdBadge DetailHeader DetailRoleBadge DetailSkillRow WildRewardHeader WildRewardNamePlate").Split(' '));
    public static string Verify()
    {
        int checkedImages=0;var errors=new List<string>();
        foreach(var guid in AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/_Project/Resources/UI"}))
        {
            string path=AssetDatabase.GUIDToAssetPath(guid);var root=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            foreach(var image in root.GetComponentsInChildren<Image>(true))
                if(image.sprite!=null&&image.sprite.border!=Vector4.zero&&image.type!=Image.Type.Filled)
                {checkedImages++;if(image.type!=Image.Type.Sliced)errors.Add(path+"/"+image.name);}
        }
        return "Checked bordered prefab images="+checkedImages+", unsliced="+errors.Count+"\n"+string.Join("\n",errors);
    }
    public static string Preview()
    {
        var root=new GameObject("SlicePreview",typeof(RectTransform),typeof(Canvas));
        var cameraObject=new GameObject("SlicePreviewCamera",typeof(Camera));
        var camera=cameraObject.GetComponent<Camera>();camera.orthographic=true;camera.orthographicSize=220;
        camera.transform.position=new Vector3(0,0,-100);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.65f,.65f,.65f);camera.cullingMask=1<<30;
        var canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;root.layer=30;
        root.GetComponent<RectTransform>().sizeDelta=new Vector2(1000,440);
        var paths=new[]{"InventoryEvolution/InventoryHeader","SynergyHud/SynergyHeader","ShopEconomy/ShopActionButton","PlayerSpectator/SpectatorReturnButton","UnitDetail/DetailHeader","WildReward/WildRewardHeader"};
        for(int row=0;row<paths.Length;row++)for(int col=0;col<3;col++)
        {
            var go=new GameObject("Sample",typeof(RectTransform),typeof(Image));go.layer=30;go.transform.SetParent(root.transform,false);
            var rect=go.GetComponent<RectTransform>();rect.sizeDelta=new Vector2(new[]{120,240,400}[col],42);rect.anchoredPosition=new Vector2(new[]{-420,-200,180}[col],175-row*68);
            var image=go.GetComponent<Image>();image.sprite=Resources.Load<Sprite>(paths[row]);image.type=Image.Type.Sliced;image.pixelsPerUnitMultiplier=Mathf.Max(1,image.sprite.rect.height/42);
        }
        var target=new RenderTexture(1000,440,24);camera.targetTexture=target;Canvas.ForceUpdateCanvases();camera.Render();
        var previous=RenderTexture.active;RenderTexture.active=target;var texture=new Texture2D(1000,440,TextureFormat.RGBA32,false);texture.ReadPixels(new Rect(0,0,1000,440),0,0);texture.Apply();
        Directory.CreateDirectory("TestResults/UiSlice");File.WriteAllBytes("TestResults/UiSlice/preview.png",texture.EncodeToPNG());RenderTexture.active=previous;camera.targetTexture=null;
        UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(cameraObject);UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(target);
        return Verify();
    }
    public static string Apply()
    {
        var report = new List<string>();
        foreach(var guid in AssetDatabase.FindAssets("t:Texture2D", new[]{"Assets/_Project/Resources"}))
        {
            string path=AssetDatabase.GUIDToAssetPath(guid);
            if(!Names.Contains(Path.GetFileNameWithoutExtension(path)))continue;
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            var texture=new Texture2D(2,2); ImageConversion.LoadImage(texture,File.ReadAllBytes(path));
            int width=texture.width,height=texture.height;
            // Preserve the entire beveled end and its outline, leaving a stretchable center.
            int corner=Math.Max(4,Math.Min(width/3,height/3));
            var border=new Vector4(corner,Math.Max(3,height/5),corner,Math.Max(3,height/5));
            if(Path.GetFileNameWithoutExtension(path)=="ShopLevelPanel")border.x=width*.30f;
            if(importer.spriteBorder!=border){importer.spriteBorder=border;importer.SaveAndReimport();}
            UnityEngine.Object.DestroyImmediate(texture);
            report.Add(path+" border="+border);
        }
        int prefabs=0,images=0;
        foreach(var guid in AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/_Project/Resources/UI"}))
        {
            string path=AssetDatabase.GUIDToAssetPath(guid);var root=PrefabUtility.LoadPrefabContents(path);bool dirty=false;
            try{
                foreach(var image in root.GetComponentsInChildren<Image>(true))
                {
                    if(image.sprite==null||image.sprite.border==Vector4.zero||image.type==Image.Type.Filled)continue;
                    if(image.type!=Image.Type.Sliced){image.type=Image.Type.Sliced;dirty=true;images++;}
                    var rect=image.rectTransform.rect;
                    float scale=Mathf.Max(1,Mathf.Min(image.sprite.rect.width/Mathf.Max(1,rect.width),image.sprite.rect.height/Mathf.Max(1,rect.height)));
                    // Existing atlas surfaces already have authored border scale values.
                    if(Names.Contains(image.sprite.name)&&!Mathf.Approximately(image.pixelsPerUnitMultiplier,scale)){image.pixelsPerUnitMultiplier=scale;dirty=true;}
                }
                if(dirty){PrefabUtility.SaveAsPrefabAsset(root,path);prefabs++;}
            }finally{PrefabUtility.UnloadPrefabContents(root);}
        }
        AssetDatabase.SaveAssets();
        report.Add("Updated prefabs="+prefabs+", newly sliced images="+images);
        Directory.CreateDirectory("TestResults/UiSlice");File.WriteAllLines("TestResults/UiSlice/audit.txt",report);
        return string.Join("\n",report);
    }
}
