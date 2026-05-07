using System;
using System.IO;
using System.Linq;

using Magpie.Abstractions;
using Magpie.Abstractions.Integrations;
using Magpie.App.Extensions;
using Magpie.Test.Shared;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using NUnit.Framework;

namespace Magpie.Test.App.Extensions;

[TestFixture]
public class ExtensionLoaderTests
{
    private string _tempRoot = null!;
    private ILoggerFactory _loggerFactory = null!;
    private ExtensionLoader _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "magpie-ext-loader-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        _loggerFactory = LoggerFactory.Create(b => b.AddTestLogger());
        _subject = new ExtensionLoader(_loggerFactory.CreateLogger<ExtensionLoader>());
    }

    [TearDown]
    public void TearDown()
    {
        _loggerFactory.Dispose();
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch
        {
            // Best effort -- tempdir cleanup is not load-bearing.
        }
    }

    [Test]
    public void LoadExtensions_ReturnsEmpty_WhenPathIsMissing()
    {
        var path = Path.Combine(_tempRoot, "does-not-exist");
        var services = new ServiceCollection();

        var result = _subject.LoadExtensions(path, services);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void LoadExtensions_ReturnsEmpty_WhenPathIsEmptyString()
    {
        var services = new ServiceCollection();

        var result = _subject.LoadExtensions(string.Empty, services);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void LoadExtensions_ReturnsEmpty_WhenNoSubdirectories()
    {
        var services = new ServiceCollection();

        var result = _subject.LoadExtensions(_tempRoot, services);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void LoadExtensions_LoadsSampleIndexer_AndIntegrationIsResolvable()
    {
        // This test exercises the real on-disk load path: the SampleIndexer assembly
        // is staged into the test bin via MSBuild but is NOT compile-time-referenced,
        // so AssemblyLoadContext.Default actually has to LoadFromAssemblyPath here.
        var sampleDir = Path.Combine(_tempRoot, "sample");
        CopySampleAssemblyTo(sampleDir);

        var services = new ServiceCollection();

        var loaded = _subject.LoadExtensions(_tempRoot, services);

        Assert.That(loaded, Has.Count.EqualTo(1));
        Assert.That(loaded[0].Instance.Id, Is.EqualTo(StagedSampleExtension.SampleExtensionId));

        using var provider = services.BuildServiceProvider();
        var integration = provider.GetService<IIndexerIntegration>();
        Assert.That(integration, Is.Not.Null);
        Assert.That(integration!.IndexerType, Is.EqualTo("sample"));
    }

    [Test]
    public void LoadExtensions_SkipsBrokenExtension_AndStillLoadsTheGoodOne()
    {
        // One subdirectory with a bogus dll (not a valid assembly) and another with the
        // real sample. Loader must log+skip the broken one and continue.
        var brokenDir = Path.Combine(_tempRoot, "broken");
        Directory.CreateDirectory(brokenDir);
        File.WriteAllText(Path.Combine(brokenDir, "broken.dll"), "this is not a real assembly");

        var sampleDir = Path.Combine(_tempRoot, "sample");
        CopySampleAssemblyTo(sampleDir);

        var services = new ServiceCollection();

        var loaded = _subject.LoadExtensions(_tempRoot, services);

        Assert.That(loaded, Has.Count.EqualTo(1));
        Assert.That(loaded[0].Instance.Id, Is.EqualTo(StagedSampleExtension.SampleExtensionId));
    }

    [Test]
    public void LoadExtensions_SkipsDirectoryWithNoDlls()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, "empty"));

        var sampleDir = Path.Combine(_tempRoot, "sample");
        CopySampleAssemblyTo(sampleDir);

        var services = new ServiceCollection();

        var loaded = _subject.LoadExtensions(_tempRoot, services);

        Assert.That(loaded, Has.Count.EqualTo(1));
    }

    private static void CopySampleAssemblyTo(string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);

        var sampleAssemblyPath = StagedSampleExtension.GetSampleAssemblyPath();
        var sourceDir = Path.GetDirectoryName(sampleAssemblyPath)!;
        var assemblyName = Path.GetFileName(sampleAssemblyPath);
        File.Copy(sampleAssemblyPath, Path.Combine(targetDirectory, assemblyName), overwrite: true);

        // Copy the pdb if it was staged alongside, purely for nicer stack traces if
        // the test process does end up loading it.
        foreach (var pdb in Directory.EnumerateFiles(sourceDir, "Magpie.Extensions.SampleIndexer.pdb"))
        {
            File.Copy(pdb, Path.Combine(targetDirectory, Path.GetFileName(pdb)), overwrite: true);
        }
    }
}
