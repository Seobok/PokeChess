using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using PokeChess.Client.UI;
using PokeChess.Client.Data;
namespace PokeChess.Editor
{
    public static class SynergyLabSceneBuilder
    {
        public const string ScenePath="Assets/_Project/Scenes/SynergyLab.unity";
        [MenuItem("PokeChess/Testing/Open Synergy Lab")]
        public static void Open()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play Mode before opening Synergy Lab.");
            if(Enumerable.Range(0,SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Any(s=>s.isDirty))throw new InvalidOperationException("Save modified scenes before opening Synergy Lab.");
            if(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath)!=null){EditorSceneManager.OpenScene(ScenePath);return;}
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var root=new GameObject("SynergyLab");var view=root.AddComponent<PlacementSandboxView>();
            view.SkillVisuals=AssetDatabase.LoadAssetAtPath<SkillVisualCatalog>(SkillVisualCatalogBuilder.CatalogPath);
            root.AddComponent<SynergyLabController>();
            var camera=new GameObject("SynergyLabCamera",typeof(Camera),typeof(AudioListener));camera.tag="MainCamera";camera.transform.position=new Vector3(0,0,-10);
            camera.GetComponent<Camera>().orthographic=true;camera.GetComponent<Camera>().clearFlags=CameraClearFlags.SolidColor;camera.GetComponent<Camera>().backgroundColor=new Color(.035f,.06f,.10f);
            EditorSceneManager.SaveScene(scene,ScenePath);Selection.activeGameObject=root;
        }
    }
}
