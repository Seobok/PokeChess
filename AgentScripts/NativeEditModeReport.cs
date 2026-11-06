using System.IO;
using UnityEngine;
using UnityEditor.TestTools.TestRunner.Api;
public static class NativeEditModeReport
{
    private static TestRunnerApi api;
    private sealed class Reporter:ICallbacks
    {
        private int finished;
        private static readonly string DirectoryPath=Path.GetFullPath("TestResults/SynergyV09");
        public void RunStarted(ITestAdaptor test){Directory.CreateDirectory(DirectoryPath);File.WriteAllText(Path.Combine(DirectoryPath,"Live-Progress.txt"),"Started: "+test.FullName);}
        public void RunFinished(ITestResultAdaptor result){TestRunnerApi.SaveResultToFile(result,Path.Combine(DirectoryPath,"Unity-All-Final.xml"));File.WriteAllText(Path.Combine(DirectoryPath,"Live-Progress.txt"),"Completed: "+result.PassCount+" passed; "+result.FailCount+" failed; "+result.SkipCount+" skipped.");}
        public void TestStarted(ITestAdaptor test){}
        public void TestFinished(ITestResultAdaptor result){if(!result.Test.IsSuite&&++finished%50==0)File.WriteAllText(Path.Combine(DirectoryPath,"Live-Progress.txt"),"Finished "+finished+": "+result.Test.FullName);}
    }
    public static string Register(){api=ScriptableObject.CreateInstance<TestRunnerApi>();api.RegisterCallbacks(new Reporter());return "Native XML and progress reporter registered.";}
}
