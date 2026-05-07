using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Integrations;
using Magpie.App.Extensions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using NUnit.Framework;

namespace Magpie.Test.App.Extensions;

[TestFixture]
public class MagpieHostBuilderTests
{
    private string _tempRoot = null!;
    private string _extensionsRoot = null!;

    [SetUp]
    public void SetUp()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "magpie-host-builder-" + Guid.NewGuid().ToString("N"));
        _extensionsRoot = Path.Combine(_tempRoot, ExtensionPathResolver.DefaultDirectoryName);
        Directory.CreateDirectory(_extensionsRoot);

        // Stage the sample assembly under <extensions>/sample/ so the loader can find it.
        // The DLL itself comes from the test project's TestExtensions/sample/ staging
        // dir (see StageSampleExtension in Magpie.Test.App.csproj); we deliberately do
        // NOT compile-time-reference SampleIndexerExtension so that the loader actually
        // exercises LoadFromAssemblyPath instead of finding it already in the AppDomain.
        var sampleDir = Path.Combine(_extensionsRoot, "sample");
        Directory.CreateDirectory(sampleDir);
        var sampleAssemblyPath = StagedSampleExtension.GetSampleAssemblyPath();
        File.Copy(
            sampleAssemblyPath,
            Path.Combine(sampleDir, Path.GetFileName(sampleAssemblyPath)),
            overwrite: true);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch
        {
            // best effort
        }
    }

    [Test]
    public async Task AddMagpieExtensions_LoadsSampleIndexer_AndIntegrationResolvesAfterStart()
    {
        var builder = Host.CreateApplicationBuilder();

        // Override content root so the loader resolves <_tempRoot>/extensions.
        builder.Environment.ContentRootPath = _tempRoot;

        var loaded = builder.AddMagpieExtensions();

        Assert.That(loaded, Has.Count.EqualTo(1));
        Assert.That(loaded[0].Instance.Id, Is.EqualTo(StagedSampleExtension.SampleExtensionId));

        using var host = builder.Build();
        await host.StartAsync();

        try
        {
            var integration = host.Services.GetService<IIndexerIntegration>();
            Assert.That(integration, Is.Not.Null);
            Assert.That(integration!.IndexerType, Is.EqualTo("sample"));

            // The hosted service should have run by now -- but lifecycle dispatch is
            // a no-op for the sample (it doesn't implement IExtensionLifecycle), so
            // we only assert that no install record was written.
            var installFile = Path.Combine(_extensionsRoot, ExtensionLifecycleDispatcher.InstalledRecordFileName);
            Assert.That(File.Exists(installFile), Is.False);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public void AddMagpieExtensions_RespectsConfiguredPath()
    {
        var altExtensionsDir = Path.Combine(_tempRoot, "alt-plugins");
        Directory.CreateDirectory(altExtensionsDir);

        var builder = Host.CreateApplicationBuilder();
        builder.Environment.ContentRootPath = _tempRoot;
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Magpie:ExtensionsPath"] = altExtensionsDir
        });

        var loaded = builder.AddMagpieExtensions();

        // Empty directory -> no extensions loaded (the sample is staged elsewhere).
        Assert.That(loaded, Is.Empty);
    }
}
