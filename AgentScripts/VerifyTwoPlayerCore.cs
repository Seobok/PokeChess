using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
public static class VerifyTwoPlayerCore
{
    [Serializable]private sealed class Result{public int passed,failed;public string[] errors;}
    public static string Main(){var assembly=Assembly.Load("PokeChess.Core.Tests");int passed=0,failed=0;var errors=new List<string>();
        foreach(var name in new[]{"HostCommandProcessorTests","MatchCommandGatewayTests","MatchStateSyncTests","OnlineCombatTests","ClientPresentationStateTests","MatchReconnectTests","TwoPlayerRegressionTests"}){var type=assembly.GetType("PokeChess.Core.Tests."+name);if(type==null)throw new Exception("Missing compiled fixture: "+name);foreach(var method in type.GetMethods(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly)){if(method.Name=="Setup"||method.GetParameters().Length!=0)continue;
            try{var fixture=Activator.CreateInstance(type);type.GetMethod("Setup").Invoke(fixture,null);method.Invoke(fixture,null);passed++;}catch(Exception e){failed++;errors.Add(name+"."+method.Name+": "+(e.InnerException??e).ToString());}}}
        var boundaries=assembly.GetType("PokeChess.Core.Tests.AssemblyBoundaryTests");foreach(var name in new[]{"CoreHasNoEngineOrOuterLayerDependencies","RuntimeAssembliesUseTheDeclaredReferenceDirection","PlayerCompilationExcludesEditorAndTestAssemblies"})try{boundaries.GetMethod(name).Invoke(Activator.CreateInstance(boundaries),null);passed++;}catch(Exception e){failed++;errors.Add(name+": "+(e.InnerException??e).ToString());}
        File.WriteAllText("TestResults/TwoPlayerRegression/core-tests.json",JsonUtility.ToJson(new Result{passed=passed,failed=failed,errors=errors.ToArray()},true));return passed+" passed / "+failed+" failed";}
}
