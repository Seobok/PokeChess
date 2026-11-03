using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using PokeChess.Client.UI;
using PokeChess.Client.Data;
using PokeChess.Content;
namespace PokeChess.Editor
{
    public static class EffectLabSceneBuilder
    {
        public const string ScenePath="Assets/_Project/Scenes/EffectLab.unity";
        [MenuItem("PokeChess/Testing/Open Effect Lab")]
        public static void Open()
        {
            if(Application.isPlaying){Debug.LogWarning("Stop Play Mode before opening Effect Lab.");return;}
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            if(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath)!=null){EditorSceneManager.OpenScene(ScenePath);return;}
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var root=new GameObject("EffectLab");var view=root.AddComponent<EffectLabView>();
            view.Roster=AssetDatabase.LoadAssetAtPath<RosterData>("Assets/_Project/Resources/GameRoster.asset");
            view.Visuals=AssetDatabase.LoadAssetAtPath<SkillVisualCatalog>(SkillVisualCatalogBuilder.CatalogPath);
            var camera=new GameObject("LabCamera",typeof(Camera),typeof(AudioListener));camera.tag="MainCamera";
            camera.transform.position=new Vector3(0,0,-10);camera.GetComponent<Camera>().clearFlags=CameraClearFlags.SolidColor;
            camera.GetComponent<Camera>().backgroundColor=new Color(.045f,.065f,.105f);
            EditorSceneManager.SaveScene(scene,ScenePath);Selection.activeGameObject=root;
        }
    }
}
