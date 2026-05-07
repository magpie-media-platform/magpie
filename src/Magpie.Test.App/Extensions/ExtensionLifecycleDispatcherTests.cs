using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.App.Extensions;
using Magpie.Models;
using Magpie.Test.Shared;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using NUnit.Framework;

namespace Magpie.Test.App.Extensions;

[TestFixture]
public class ExtensionLifecycleDispatcherTests
{
    private string _tempRoot = null!;
    private ILoggerFactory _loggerFactory = null!;
    private ExtensionLifecycleDispatcher _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _tempRoot = Path.Combine(
            Path.GetTempPath(),
            "magpie-ext-lifecycle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        _loggerFactory = LoggerFactory.Create(b => b.AddTestLogger());
        _subject = new ExtensionLifecycleDispatcher(
            _loggerFactory.CreateLogger<ExtensionLifecycleDispatcher>());
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
            // Best effort.
        }
    }

    [Test]
    public async Task DispatchAsync_NoExtensions_DoesNothing()
    {
        var ctx = MakeContext();

        await _subject.DispatchAsync([], _tempRoot, ctx);

        Assert.That(File.Exists(Path.Combine(_tempRoot, ExtensionLifecycleDispatcher.InstalledRecordFileName)),
            Is.False);
    }

    [Test]
    public async Task DispatchAsync_SkipsExtensionsThatDoNotImplementLifecycle()
    {
        var loaded = new List<LoadedExtension>
        {
            MakeLoaded(new BasicExtension())
        };

        await _subject.DispatchAsync(loaded, _tempRoot, MakeContext());

        // No-op: nothing should be persisted.
        Assert.That(File.Exists(Path.Combine(_tempRoot, ExtensionLifecycleDispatcher.InstalledRecordFileName)),
            Is.False);
    }

    [Test]
    public async Task DispatchAsync_FirstRun_CallsInstallAndInitialize_AndPersistsRecord()
    {
        var ext = new RecordingExtension();

        await _subject.DispatchAsync([MakeLoaded(ext)], _tempRoot, MakeContext());

        Assert.That(ext.InstallCalls, Is.EqualTo(1));
        Assert.That(ext.InitializeCalls, Is.EqualTo(1));
        Assert.That(File.Exists(Path.Combine(_tempRoot, ExtensionLifecycleDispatcher.InstalledRecordFileName)),
            Is.True);
    }

    [Test]
    public async Task DispatchAsync_SecondRun_DoesNotReinstall_ButReinitializes()
    {
        var ext = new RecordingExtension();

        await _subject.DispatchAsync([MakeLoaded(ext)], _tempRoot, MakeContext());
        await _subject.DispatchAsync([MakeLoaded(ext)], _tempRoot, MakeContext());

        Assert.That(ext.InstallCalls, Is.EqualTo(1));
        Assert.That(ext.InitializeCalls, Is.EqualTo(2));
    }

    [Test]
    public async Task DispatchAsync_StillCallsInitialize_WhenInstallThrows()
    {
        var ext = new RecordingExtension { ThrowInInstall = true };

        await _subject.DispatchAsync([MakeLoaded(ext)], _tempRoot, MakeContext());

        Assert.That(ext.InstallCalls, Is.EqualTo(1));
        Assert.That(ext.InitializeCalls, Is.EqualTo(1));

        // Failed install should NOT mark the extension as installed -- so a follow-up
        // dispatch will retry the install hook.
        var ext2 = new RecordingExtension();
        await _subject.DispatchAsync([MakeLoaded(ext2)], _tempRoot, MakeContext());
        Assert.That(ext2.InstallCalls, Is.EqualTo(1));
    }

    private static ExtensionLifecycleContext MakeContext()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        return new ExtensionLifecycleContext("test-node", services);
    }

    private static LoadedExtension MakeLoaded(IExtension instance)
    {
        return new LoadedExtension(instance, instance.GetType().Assembly, AppContext.BaseDirectory);
    }

    private sealed class BasicExtension : IExtension
    {
        public Guid Id => new("00000000-0000-0000-0000-000000000001");
        public Version Version => new(1, 0);
        public string Name => "Basic";
        public string? TargetABI => null;
        public void RegisterServices(IServiceCollection services)
        {
        }
    }

    private sealed class RecordingExtension : IExtensionLifecycle
    {
        public Guid Id => new("00000000-0000-0000-0000-000000000002");
        public Version Version => new(1, 0);
        public string Name => "Recording";
        public string? TargetABI => null;

        public bool ThrowInInstall { get; set; }
        public int InstallCalls { get; private set; }
        public int InitializeCalls { get; private set; }

        public void RegisterServices(IServiceCollection services)
        {
        }

        public Task OnAppInitializeAsync(ExtensionLifecycleContext context, CancellationToken cancellationToken = default)
        {
            InitializeCalls++;
            return Task.CompletedTask;
        }

        public Task OnExtensionInstallAsync(ExtensionLifecycleContext context, CancellationToken cancellationToken = default)
        {
            InstallCalls++;
            if (ThrowInInstall)
            {
                throw new InvalidOperationException("simulated install failure");
            }
            return Task.CompletedTask;
        }

        public Task OnExtensionUninstallAsync(ExtensionLifecycleContext context, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
