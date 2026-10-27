using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using NUnit.Framework.Api;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;
class Program
{
    static int Main()
    {
        var runner = new NUnitTestAssemblyRunner(new DefaultTestAssemblyBuilder());
        runner.Load(Assembly.GetExecutingAssembly(), new Dictionary<string, object>());
        var result = runner.Run(TestListener.NULL, TestFilter.Empty);
        File.WriteAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../Core-standalone.xml")), result.ToXml(true).OuterXml);
        Console.WriteLine($"Passed {result.PassCount}; Failed {result.FailCount}; Skipped {result.SkipCount}");
        Report(result); return result.FailCount == 0 ? 0 : 1;
    }
    static void Report(ITestResult result)
    {
        if (!result.HasChildren && result.ResultState.Status == TestStatus.Failed) Console.WriteLine(result.FullName + "\n" + result.Message);
        foreach (var child in result.Children) Report(child);
    }
}

