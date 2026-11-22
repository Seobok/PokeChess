#if UNITY_IOS
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
namespace PokeChess.Editor
{
    public static class AccountKeychainBuild
    {
        [PostProcessBuild(100)]
        public static void LinkSecurity(BuildTarget target,string path)
        {
            if(target!=BuildTarget.iOS) return;
            var file=PBXProject.GetPBXProjectPath(path); var project=new PBXProject(); project.ReadFromFile(file);
            project.AddFrameworkToProject(project.GetUnityFrameworkTargetGuid(),"Security.framework",false);
            project.WriteToFile(file);
        }
    }
}
#endif
