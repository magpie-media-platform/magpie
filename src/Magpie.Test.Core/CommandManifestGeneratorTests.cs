using System;
using System.Linq;

using Magpie.CommandLine.Generators;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using NUnit.Framework;

namespace Magpie.Test.Core;

[TestFixture]
public sealed class CommandManifestGeneratorTests
{
    private const string Source = """
        namespace Magpie.Api
        {
            public sealed class MagpieCommandGroupAttribute : System.Attribute
            {
                public MagpieCommandGroupAttribute(string name, string prefix, string description) { }
            }
            public sealed class MagpieCommandAttribute : System.Attribute
            {
                public MagpieCommandAttribute(string name, string verb, string route, string description) { }
            }
            public sealed class Routes
            {
                public void MapGet(string route, System.Action handler) { }
                public void MapPost(string route, System.Action handler) { }
            }
            [MagpieCommandGroup("test", "test", "Test commands")]
            public sealed class TestGroup
            {
                public string Name => "test";
                public void MapEndpoints(Routes routes) => routes.MapGet("", Handle);
                [MagpieCommand("test", "GET", "", "Test command")]
                public void Handle() { }
            }
        }
        """;

    private CSharpCompilation _api = null!;

    [SetUp]
    public void SetUp()
    {
        _api = CSharpCompilation.Create("Magpie.Api",
            [CSharpSyntaxTree.ParseText(Source)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    [TestCase("MapGet(\"\", Handle)", "MapGet(\"\", Handle)", false)]
    [TestCase("MapGet(\"\", Handle)", "MapPost(\"\", Handle)", true)]
    [TestCase("MapGet(\"\", Handle)", "MapGet(\"changed\", Handle)", true)]
    [TestCase("MapGet(\"\", Handle)", "MapGet(\"\", () => { })", false)]
    [TestCase("Name => \"test\"", "Name => \"changed\"", false)]
    [TestCase("=> routes.MapGet(\"\", Handle);", "{ var route = Name; routes.MapGet(route, Handle); }", false)]
    public void Initialize_ReportsRouteMismatch_WhenNoEndpointMapsTheDeclaredRoute(
        string before, string after, bool mismatch)
    {
        // Arrange
        var compilation = _api.ReplaceSyntaxTree(_api.SyntaxTrees.Single(),
            CSharpSyntaxTree.ParseText(Source.Replace(before, after, StringComparison.Ordinal)));
        var driver = CSharpGeneratorDriver.Create(new MagpieCommandManifestGenerator());

        // Act
        var result = driver.RunGenerators(compilation).GetRunResult();

        // Assert
        Assert.That(result.Diagnostics.Any(diagnostic => diagnostic.Id == "MCLI007"), Is.EqualTo(mismatch));
    }

    [Test]
    public void Initialize_ReusesTheApiAssembly_WhenOnlyTheConsumerSourceChanges()
    {
        // Arrange
        var consumer = CSharpCompilation.Create("Consumer", [CSharpSyntaxTree.ParseText("class Before { }")],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location), _api.ToMetadataReference()]);
        var driver = (GeneratorDriver)CSharpGeneratorDriver.Create(
            [new MagpieCommandManifestGenerator().AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, true));
        driver = driver.RunGenerators(consumer);
        var changed = consumer.ReplaceSyntaxTree(
            consumer.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText("class After { }"));

        // Act
        var result = driver.RunGenerators(changed).GetRunResult();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Results.Single().TrackedSteps["ApiAssembly"].Single().Outputs.Single().Reason,
                Is.EqualTo(IncrementalStepRunReason.Unchanged));
            Assert.That(result.GeneratedTrees.Single().ToString(), Does.Contain("api/test"));
        }
    }

    [Test]
    public void Initialize_RegeneratesTheRoute_WhenTheApiReferenceChanges()
    {
        // Arrange
        var reference = _api.ToMetadataReference();
        var consumer = CSharpCompilation.Create("Consumer", references:
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location), reference]);
        var driver = CSharpGeneratorDriver.Create(new MagpieCommandManifestGenerator()).RunGenerators(consumer);
        var changedApi = _api.ReplaceSyntaxTree(_api.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(
            Source.Replace("\"GET\", \"\"", "\"GET\", \"changed\"", StringComparison.Ordinal)));

        // Act
        var result = driver.RunGenerators(consumer.ReplaceReference(reference, changedApi.ToMetadataReference()))
            .GetRunResult();

        // Assert
        Assert.That(result.GeneratedTrees.Single().ToString(), Does.Contain("api/test/changed"));
    }
}
