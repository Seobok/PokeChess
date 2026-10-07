using System;
using System.IO;
using System.Reflection;
public static class VerifyEditorModelBoundaries {
    public static string Main() {
        var assembly=Assembly.Load("PokeChess.Core.Tests");
        string[] boundary={"CoreHasNoEngineOrOuterLayerDependencies","RuntimeAssembliesUseTheDeclaredReferenceDirection","PlayerCompilationExcludesEditorAndTestAssemblies"};
        int passed=0;
        foreach(string name in boundary) {
            var type=assembly.GetType("PokeChess.Core.Tests.AssemblyBoundaryTests");
            type.GetMethod(name).Invoke(Activator.CreateInstance(type),null);passed++;
        }
        var model=assembly.GetType("PokeChess.Core.Tests.DataModelTests");
        model.GetMethod("AssetConvertsToDetachedCoreDefinition").Invoke(Activator.CreateInstance(model),null);passed++;
        File.WriteAllText("C:/Users/user/Desktop/PokeChess/PokeChess/TestResults/WBS4.10/editor-model.json","{\"passed\":"+passed+",\"failed\":0}");
        return passed+" original Unity API / assembly boundary assertions passed.";
    }
}
