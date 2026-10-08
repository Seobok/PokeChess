using UnityEngine;
using UnityEditor;
using PokeChess.Client.UI;
public static class LinkItemIcons
{
    public static string Run()
    {
        var library=Resources.Load<ItemIconLibrary>("ItemIcons");
        string[] names={"Leftovers","SitrusBerry","AssaultVest","FocusSash","WiseGlasses","RockyHelmet","ChoiceScarf","ExpShare"};
        library.icons=new Sprite[names.Length];
        for(int i=0;i<names.Length;i++)library.icons[i]=AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Sprites/Items/"+names[i]+".png");
        EditorUtility.SetDirty(library);AssetDatabase.SaveAssets();
        return "Linked original item sprites.";
    }
}
