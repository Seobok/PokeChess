using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.Compilation;

namespace PokeChess.Core.Tests
{
    public sealed class AssemblyBoundaryTests
    {
        [Test]
        public void CoreHasNoEngineOrOuterLayerDependencies()
        {
            var references = System.Reflection.Assembly.Load("PokeChess.Core").GetReferencedAssemblies();
            Assert.That(references.Any(r => r.Name.StartsWith("Unity") || r.Name.StartsWith("PokeChess.Network") || r.Name.StartsWith("PokeChess.Client")), Is.False);
        }

        [Test]
        public void RuntimeAssembliesUseTheDeclaredReferenceDirection()
        {
            var assemblies = CompilationPipeline.GetAssemblies(AssembliesType.Player);
            var network = assemblies.Single(a => a.name == "PokeChess.Network");
            var client = assemblies.Single(a => a.name == "PokeChess.Client");
            Assert.That(network.assemblyReferences.Select(a => a.name), Does.Contain("PokeChess.Core"));
            Assert.That(client.assemblyReferences.Select(a => a.name), Does.Contain("PokeChess.Core"));
            Assert.That(client.assemblyReferences.Select(a => a.name), Does.Contain("PokeChess.Network"));
        }

        [Test]
        public void PlayerCompilationExcludesEditorAndTestAssemblies()
        {
            var names = CompilationPipeline.GetAssemblies(AssembliesType.Player).Select(a => a.name).ToArray();
            Assert.That(names, Does.Contain("PokeChess.Core"));
            Assert.That(names, Does.Contain("PokeChess.Network"));
            Assert.That(names, Does.Contain("PokeChess.Client"));
            Assert.That(names, Does.Not.Contain("PokeChess.Editor"));
            Assert.That(names, Does.Not.Contain("PokeChess.Core.Tests"));
        }
    }
}
