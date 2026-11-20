using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using PokeChess.Content;
namespace PokeChess.Editor
{
    public sealed class SkillContentBuildValidation:IPreprocessBuildWithReport
    {
        public int callbackOrder=>0;
        public void OnPreprocessBuild(BuildReport report)
        {
            try
            {
                var roster=AssetDatabase.LoadAssetAtPath<RosterData>("Assets/_Project/Resources/GameRoster.asset");roster.Validate();
                var registered=roster.Skills.Select(AssetDatabase.GetAssetPath).OrderBy(p=>p).ToArray();
                var authored=AssetDatabase.FindAssets("t:SkillData",new[]{"Assets/_Project/Content/Skills"}).Select(AssetDatabase.GUIDToAssetPath).OrderBy(p=>p).ToArray();
                if(!registered.SequenceEqual(authored))throw new InvalidOperationException("Skills folder and GameRoster registrations differ.");
            }
            catch(Exception ex){throw new BuildFailedException("Invalid skill content: "+ex.Message);}
        }
    }
    public sealed class SkillContentSaveValidation:AssetModificationProcessor
    {
        private static string[] OnWillSaveAssets(string[] paths)
        {
            if(!paths.Any(p=>p.StartsWith("Assets/_Project/Content/",StringComparison.Ordinal)||p=="Assets/_Project/Resources/GameRoster.asset"))return paths;
            var roster=AssetDatabase.LoadAssetAtPath<RosterData>("Assets/_Project/Resources/GameRoster.asset");if(roster==null)return paths;
            try{roster.Validate();GameContent.Reset();return paths;}
            catch(Exception ex)
            {
                Debug.LogError("Content save blocked: "+ex.Message);
                return paths.Where(p=>!p.StartsWith("Assets/_Project/Content/",StringComparison.Ordinal)&&p!="Assets/_Project/Resources/GameRoster.asset").ToArray();
            }
        }
    }
}
