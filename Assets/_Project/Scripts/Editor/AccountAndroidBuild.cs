#if UNITY_ANDROID && UNITY_EDITOR
using System.IO;
using UnityEditor.Android;
namespace PokeChess.Editor
{
    // Called with the unityLibrary Gradle module. Preserve the JNI entrypoints under R8/minification.
    public sealed class AccountAndroidBuild : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 100;
        public void OnPostGenerateGradleAndroidProject(string path)
        {
            var file=Path.Combine(path,"proguard-unity.txt");
            const string rule="-keep class com.pokechess.security.SecretSessionStore { public static *; }";
            var content=File.Exists(file)?File.ReadAllText(file):"";
            if(!content.Contains(rule)) File.AppendAllText(file,"\n"+rule+"\n");
        }
    }
}
#endif
