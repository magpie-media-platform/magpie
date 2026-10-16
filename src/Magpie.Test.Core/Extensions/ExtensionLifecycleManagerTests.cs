using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Extensions;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Extensions;
using Magpie.Sdk.Services;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Extensions;

[TestFixture]
public sealed class ExtensionLifecycleManagerTests
{
    private TestExtension _extension = null!;
    private Mock<IExtensionRepository> _repository = null!;
    private Mock<IExtensionNodeStatusRepository> _nodeStatuses = null!;
    private FakeTimeProvider _timeProvider = null!;
    private Mock<IFileSystem> _fileSystem = null!;
    private Guid? _claimedId;
    private IServiceScopeFactory _scopeFactory = null!;
    private IExtensionLifecycleManager _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _extension = new TestExtension();
        _claimedId = null;
        _repository = new Mock<IExtensionRepository>();
        _repository
            .Setup(candidate => candidate.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _repository
            .Setup(candidate => candidate.TrySetLifecycleVersionAsync(
                It.IsAny<Guid>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, string?, string?, CancellationToken>((id, _, _, _) => _claimedId = id)
            .ReturnsAsync(true);
        _fileSystem = new Mock<IFileSystem>();
        _nodeStatuses = new Mock<IExtensionNodeStatusRepository>();
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero));

        var system = new Mock<IMagpieSystem>();
        system.SetupGet(candidate => candidate.NodeId).Returns("test-node");
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IExtensionRepository)))
            .Returns(_repository.Object);
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IFileSystem)))
            .Returns(_fileSystem.Object);
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IMagpieSystem)))
            .Returns(system.Object);
        serviceProvider
            .Setup(provider => provider.GetService(typeof(ILoggerFactory)))
            .Returns(NullLoggerFactory.Instance);
        var scope = new Mock<IServiceScope>();
        scope.SetupGet(candidate => candidate.ServiceProvider).Returns(serviceProvider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(factory => factory.CreateScope()).Returns(scope.Object);
        _scopeFactory = scopeFactory.Object;
        _subject = new ExtensionLifecycleManager(
            new ExtensionLoadContext([_extension], []),
            _scopeFactory,
            _repository.Object,
            _nodeStatuses.Object,
            _fileSystem.Object,
            new("test-node", false, false, ".", ".", ".", ".", DateTime.UtcNow),
            _timeProvider,
            NullLoggerFactory.Instance,
            NullLogger<ExtensionLifecycleManager>.Instance);
    }

    [Test]
    public async Task DispatchStartupHooksAsync_KeepsTheStoredEnablement_WhenAnExtensionFailsToLoad()
    {
        // Arrange
        var failedRow = new Extension
        {
            Id = new Guid("11111111-1111-1111-1111-111111111111"),
            PackageId = "Magpie.Extensions.Broken",
            Name = "Broken extension",
            Version = "1.0.0",
            FilePath = "broken.dll",
            AssemblyName = "Broken",
            Enabled = true,
        };
        var subject = new ExtensionLifecycleManager(
            new ExtensionLoadContext([], [failedRow]),
            _scopeFactory,
            _repository.Object,
            _nodeStatuses.Object,
            _fileSystem.Object,
            new("test-node", false, false, ".", ".", ".", ".", DateTime.UtcNow),
            _timeProvider,
            NullLoggerFactory.Instance,
            NullLogger<ExtensionLifecycleManager>.Instance);

        // Act
        await subject.DispatchStartupHooksAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _repository.Verify(
                candidate => candidate.PatchAsync(
                    It.IsAny<ExtensionPatch>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _repository.Verify(
                candidate => candidate.UpdateAsync(
                    It.IsAny<Extension>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task DispatchStartupHooksAsync_RetriesUpgrade_WhenUpgradeHookFails()
    {
        // Arrange
        var extensionId = new Guid("11111111-1111-1111-1111-111111111111");
        var lifecycle = new Mock<IExtensionLifecycle>();
        lifecycle.SetupGet(extension => extension.Id).Returns(extensionId);
        lifecycle.SetupGet(extension => extension.Version).Returns(new Version(1, 0, 1));
        lifecycle.SetupGet(extension => extension.Name).Returns("Test extension");
        lifecycle
            .Setup(extension => extension.OnExtensionUpgradeAsync(
                It.IsAny<ExtensionLifecycleContext>(),
                new Version(1, 0, 0),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Migration failed."));
        lifecycle
            .Setup(extension => extension.OnAppInitializeAsync(
                It.IsAny<ExtensionLifecycleContext>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var row = new Extension
        {
            Id = extensionId,
            Version = "1.0.1",
            LifecycleVersion = "1.0.0",
            Name = "Test extension",
            FilePath = "test-extension.dll",
            AssemblyName = "Test.Extension"
        };
        var repository = new Mock<IExtensionRepository>();
        repository
            .Setup(candidate => candidate.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([row]);
        repository
            .Setup(candidate => candidate.TrySetLifecycleVersionAsync(
                extensionId,
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var system = new Mock<IMagpieSystem>();
        system.SetupGet(candidate => candidate.NodeId).Returns("test-node");
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IExtensionRepository)))
            .Returns(repository.Object);
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IFileSystem)))
            .Returns(Mock.Of<IFileSystem>());
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IMagpieSystem)))
            .Returns(system.Object);
        serviceProvider
            .Setup(provider => provider.GetService(typeof(ILoggerFactory)))
            .Returns(NullLoggerFactory.Instance);
        var scope = new Mock<IServiceScope>();
        scope.SetupGet(candidate => candidate.ServiceProvider).Returns(serviceProvider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(factory => factory.CreateScope()).Returns(scope.Object);
        var subject = new ExtensionLifecycleManager(
            new ExtensionLoadContext([lifecycle.Object], []),
            scopeFactory.Object,
            repository.Object,
            _nodeStatuses.Object,
            _fileSystem.Object,
            new("test-node", false, false, ".", ".", ".", ".", DateTime.UtcNow),
            _timeProvider,
            NullLoggerFactory.Instance,
            NullLogger<ExtensionLifecycleManager>.Instance);

        // Act
        await subject.DispatchStartupHooksAsync().ConfigureAwait(false);
        await subject.DispatchStartupHooksAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(row.LifecycleVersion, Is.EqualTo("1.0.0"));
            lifecycle.Verify(extension => extension.OnExtensionUpgradeAsync(
                It.IsAny<ExtensionLifecycleContext>(),
                new Version(1, 0, 0),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
            repository.Verify(candidate => candidate.TrySetLifecycleVersionAsync(
                extensionId,
                "claim:1.0.0:test-node",
                "1.0.0",
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }
    }

    [TestCase(false, 0)]
    [TestCase(true, 1)]
    public async Task DispatchStartupHooksAsync_RunsTheInstallHook_WhenThisNodeClaimsIt(
        bool claimed,
        int expectedInstallCount)
    {
        // Arrange
        var extensionId = new Guid("11111111-1111-1111-1111-111111111111");
        var lifecycle = new Mock<IExtensionLifecycle>();
        lifecycle.SetupGet(extension => extension.Id).Returns(extensionId);
        lifecycle.SetupGet(extension => extension.Version).Returns(new Version(1, 0, 1));
        lifecycle.SetupGet(extension => extension.Name).Returns("Test extension");
        _repository
            .Setup(candidate => candidate.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Extension
                {
                    Id = extensionId,
                    Name = "Test extension",
                    Version = "1.0.1",
                    FilePath = "test-extension.dll",
                    AssemblyName = "Test.Extension",
                },
            ]);
        _repository
            .Setup(candidate => candidate.TrySetLifecycleVersionAsync(
                extensionId,
                null,
                "claim::test-node",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(claimed);
        var subject = new ExtensionLifecycleManager(
            new ExtensionLoadContext([lifecycle.Object], []),
            _scopeFactory,
            _repository.Object,
            _nodeStatuses.Object,
            _fileSystem.Object,
            new("test-node", true, true, ".", ".", ".", ".", DateTime.UtcNow),
            _timeProvider,
            NullLoggerFactory.Instance,
            NullLogger<ExtensionLifecycleManager>.Instance);

        // Act
        await subject.DispatchStartupHooksAsync().ConfigureAwait(false);

        // Assert
        lifecycle.Verify(
            extension => extension.OnExtensionInstallAsync(
                It.IsAny<ExtensionLifecycleContext>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(expectedInstallCount));
    }

    [Test]
    public async Task DispatchStartupHooksAsync_RemovesTheClaim_WhenTheInstallHookFails()
    {
        // Arrange
        var extensionId = new Guid("11111111-1111-1111-1111-111111111111");
        var lifecycle = new Mock<IExtensionLifecycle>();
        lifecycle.SetupGet(extension => extension.Id).Returns(extensionId);
        lifecycle.SetupGet(extension => extension.Version).Returns(new Version(1, 0, 1));
        lifecycle.SetupGet(extension => extension.Name).Returns("Test extension");
        lifecycle
            .Setup(extension => extension.OnExtensionInstallAsync(
                It.IsAny<ExtensionLifecycleContext>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Install failed."));
        _repository
            .Setup(candidate => candidate.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Extension
                {
                    Id = extensionId,
                    Name = "Test extension",
                    Version = "1.0.1",
                    FilePath = "test-extension.dll",
                    AssemblyName = "Test.Extension",
                },
            ]);
        var subject = new ExtensionLifecycleManager(
            new ExtensionLoadContext([lifecycle.Object], []),
            _scopeFactory,
            _repository.Object,
            _nodeStatuses.Object,
            _fileSystem.Object,
            new("test-node", true, true, ".", ".", ".", ".", DateTime.UtcNow),
            _timeProvider,
            NullLoggerFactory.Instance,
            NullLogger<ExtensionLifecycleManager>.Instance);

        // Act
        await subject.DispatchStartupHooksAsync().ConfigureAwait(false);

        // Assert
        _repository.Verify(
            candidate => candidate.TrySetLifecycleVersionAsync(
                extensionId,
                "claim::test-node",
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task DispatchStartupHooksAsync_RemovesTheClaimWithoutTheStopToken_WhenTheHostStopsDuringTheHook()
    {
        // Arrange
        var extensionId = new Guid("11111111-1111-1111-1111-111111111111");
        var lifecycle = new Mock<IExtensionLifecycle>();
        lifecycle.SetupGet(extension => extension.Id).Returns(extensionId);
        lifecycle.SetupGet(extension => extension.Version).Returns(new Version(1, 0, 1));
        lifecycle.SetupGet(extension => extension.Name).Returns("Test extension");
        lifecycle
            .Setup(extension => extension.OnExtensionInstallAsync(
                It.IsAny<ExtensionLifecycleContext>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        _repository
            .Setup(candidate => candidate.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Extension
                {
                    Id = extensionId,
                    Name = "Test extension",
                    Version = "1.0.1",
                    FilePath = "test-extension.dll",
                    AssemblyName = "Test.Extension",
                },
            ]);
        var subject = new ExtensionLifecycleManager(
            new ExtensionLoadContext([lifecycle.Object], []),
            _scopeFactory,
            _repository.Object,
            _nodeStatuses.Object,
            _fileSystem.Object,
            new("test-node", true, true, ".", ".", ".", ".", DateTime.UtcNow),
            _timeProvider,
            NullLoggerFactory.Instance,
            NullLogger<ExtensionLifecycleManager>.Instance);
        using var stop = new CancellationTokenSource();
        await stop.CancelAsync().ConfigureAwait(false);

        // Act
        await subject.DispatchStartupHooksAsync(stop.Token).ConfigureAwait(false);

        // Assert
        _repository.Verify(
            candidate => candidate.TrySetLifecycleVersionAsync(
                extensionId,
                "claim::test-node",
                null,
                CancellationToken.None),
            Times.Once);
    }

    [Test]
    public async Task DispatchStartupHooksAsync_RunsTheHookAgain_WhenThisNodeLeftAnUnfinishedClaim()
    {
        // Arrange
        var extensionId = new Guid("11111111-1111-1111-1111-111111111111");
        var lifecycle = new Mock<IExtensionLifecycle>();
        lifecycle.SetupGet(extension => extension.Id).Returns(extensionId);
        lifecycle.SetupGet(extension => extension.Version).Returns(new Version(1, 0, 1));
        lifecycle.SetupGet(extension => extension.Name).Returns("Test extension");
        _repository
            .Setup(candidate => candidate.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Extension
                {
                    Id = extensionId,
                    Name = "Test extension",
                    Version = "1.0.1",
                    LifecycleVersion = "claim:1.0.0:test-node",
                    FilePath = "test-extension.dll",
                    AssemblyName = "Test.Extension",
                },
            ]);
        var subject = new ExtensionLifecycleManager(
            new ExtensionLoadContext([lifecycle.Object], []),
            _scopeFactory,
            _repository.Object,
            _nodeStatuses.Object,
            _fileSystem.Object,
            new("test-node", true, true, ".", ".", ".", ".", DateTime.UtcNow),
            _timeProvider,
            NullLoggerFactory.Instance,
            NullLogger<ExtensionLifecycleManager>.Instance);

        // Act
        await subject.DispatchStartupHooksAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            lifecycle.Verify(
                extension => extension.OnExtensionUpgradeAsync(
                    It.IsAny<ExtensionLifecycleContext>(),
                    new Version(1, 0, 0),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _repository.Verify(
                candidate => candidate.TrySetLifecycleVersionAsync(
                    extensionId,
                    "claim:1.0.0:test-node",
                    "1.0.1",
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _nodeStatuses.Verify(
                repository => repository.GetByNodeIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [TestCase(57, 1)]
    [TestCase(59, 0)]
    public async Task DispatchStartupHooksAsync_TakesOverTheClaimOfAnotherNode_WhenThatNodeSentNoRecentHeartbeat(
        int lastSeenMinute,
        int expectedInstallCount)
    {
        // Arrange
        var extensionId = new Guid("11111111-1111-1111-1111-111111111111");
        var lifecycle = new Mock<IExtensionLifecycle>();
        lifecycle.SetupGet(extension => extension.Id).Returns(extensionId);
        lifecycle.SetupGet(extension => extension.Version).Returns(new Version(1, 0, 1));
        lifecycle.SetupGet(extension => extension.Name).Returns("Test extension");
        _repository
            .Setup(candidate => candidate.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Extension
                {
                    Id = extensionId,
                    Name = "Test extension",
                    Version = "1.0.1",
                    LifecycleVersion = "claim::magpie-1",
                    FilePath = "test-extension.dll",
                    AssemblyName = "Test.Extension",
                },
            ]);
        _nodeStatuses
            .Setup(repository => repository.GetByNodeIdAsync("magpie-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExtensionNodeStatus
            {
                NodeId = "magpie-1",
                AppliedGeneration = 1,
                ExtensionHealth = ExtensionNodeHealth.CurrentHealthy,
                SchedulerEligible = true,
                LastSeenAt = new DateTime(2026, 10, 12, 11, lastSeenMinute, 0, DateTimeKind.Utc),
                Message = "Extension activation is current and healthy.",
            });
        var subject = new ExtensionLifecycleManager(
            new ExtensionLoadContext([lifecycle.Object], []),
            _scopeFactory,
            _repository.Object,
            _nodeStatuses.Object,
            _fileSystem.Object,
            new("test-node", true, true, ".", ".", ".", ".", DateTime.UtcNow),
            _timeProvider,
            NullLoggerFactory.Instance,
            NullLogger<ExtensionLifecycleManager>.Instance);

        // Act
        await subject.DispatchStartupHooksAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _repository.Verify(
                candidate => candidate.TrySetLifecycleVersionAsync(
                    extensionId,
                    "claim::magpie-1",
                    "claim::test-node",
                    It.IsAny<CancellationToken>()),
                Times.Exactly(expectedInstallCount));
            lifecycle.Verify(
                extension => extension.OnExtensionInstallAsync(
                    It.IsAny<ExtensionLifecycleContext>(),
                    It.IsAny<CancellationToken>()),
                Times.Exactly(expectedInstallCount));
        }
    }

    [Test]
    public async Task DispatchStartupHooksAsync_SelectsIdentifierMatch_WhenMultipleIdentityFieldsMatch()
    {
        // Arrange
        _extension.Id = new Guid("11111111-1111-1111-1111-111111111111");
        var runtimeName = _extension.GetType().Assembly.GetName().Name ?? string.Empty;
        var idMatch = new Extension
        {
            Id = _extension.Id,
            FilePath = "id-match.dll",
            AssemblyName = "Id.Match",
            Name = "Identifier match",
            Version = "1.0.1",
        };
        var fallbackMatch = new Extension
        {
            Id = new Guid("22222222-2222-2222-2222-222222222222"),
            PackageId = runtimeName,
            AssemblyName = runtimeName,
            FilePath = _extension.GetType().Assembly.Location,
            Name = "Fallback match",
            Version = "1.0.1",
        };
        _repository
            .Setup(candidate => candidate.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([fallbackMatch, idMatch]);

        // Act
        await _subject.DispatchStartupHooksAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_claimedId, Is.EqualTo(idMatch.Id));
            _repository.Verify(candidate => candidate.ListAllAsync(It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Test]
    public async Task DispatchStartupHooksAsync_SelectsRuntimePathMatch_WhenPackageAndAssemblyAlsoMatch()
    {
        // Arrange
        var runtimePath = _extension.GetType().Assembly.Location;
        var runtimeName = _extension.GetType().Assembly.GetName().Name ?? string.Empty;
        var pathMatch = new Extension
        {
            Id = new Guid("11111111-1111-1111-1111-111111111111"),
            FilePath = "path-match.dll",
            AssemblyName = "Path.Match",
            Name = "Path match",
            Version = "1.0.1",
        };
        var packageMatch = new Extension
        {
            Id = new Guid("22222222-2222-2222-2222-222222222222"),
            PackageId = runtimeName,
            FilePath = "package-match.dll",
            AssemblyName = "Package.Match",
            Name = "Package match",
            Version = "1.0.1",
        };
        var assemblyMatch = new Extension
        {
            Id = Guid.NewGuid(),
            AssemblyName = runtimeName,
            FilePath = "assembly-match.dll",
            Name = "Assembly match",
            Version = "1.0.1",
        };
        _fileSystem.Setup(candidate => candidate.PathsEqual(runtimePath, pathMatch.FilePath)).Returns(true);
        _repository
            .Setup(candidate => candidate.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([assemblyMatch, packageMatch, pathMatch]);

        // Act
        await _subject.DispatchStartupHooksAsync().ConfigureAwait(false);

        // Assert
        Assert.That(_claimedId, Is.EqualTo(pathMatch.Id));
    }

    [Test]
    public async Task DispatchStartupHooksAsync_SelectsPackageBeforeAssembly_WhenRuntimePathIsInvalid()
    {
        // Arrange
        var runtimeName = _extension.GetType().Assembly.GetName().Name ?? string.Empty;
        var packageMatch = new Extension
        {
            Id = new Guid("11111111-1111-1111-1111-111111111111"),
            PackageId = runtimeName,
            FilePath = "invalid-path.dll",
            AssemblyName = "Package.Match",
            Name = "Package match",
            Version = "1.0.1",
        };
        var assemblyMatch = new Extension
        {
            Id = new Guid("22222222-2222-2222-2222-222222222222"),
            AssemblyName = runtimeName,
            FilePath = "assembly-match.dll",
            Name = "Assembly match",
            Version = "1.0.1",
        };
        _fileSystem
            .Setup(candidate => candidate.PathsEqual(It.IsAny<string>(), It.IsAny<string>()))
            .Throws(new ArgumentException("Invalid path."));
        _repository
            .Setup(candidate => candidate.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([assemblyMatch, packageMatch]);

        // Act
        await _subject.DispatchStartupHooksAsync().ConfigureAwait(false);

        // Assert
        Assert.That(_claimedId, Is.EqualTo(packageMatch.Id));
    }

    [Test]
    public async Task DispatchStartupHooksAsync_SelectsAssembly_WhenOtherIdentityFieldsDoNotMatch()
    {
        // Arrange
        var assemblyMatch = new Extension
        {
            Id = new Guid("11111111-1111-1111-1111-111111111111"),
            PackageId = "Magpie.Extensions.Missing",
            FilePath = "assembly-match.dll",
            AssemblyName = _extension.GetType().Assembly.GetName().Name ?? string.Empty,
            Name = "Assembly match",
            Version = "1.0.1",
        };
        _repository
            .Setup(candidate => candidate.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([assemblyMatch]);

        // Act
        await _subject.DispatchStartupHooksAsync().ConfigureAwait(false);

        // Assert
        Assert.That(_claimedId, Is.EqualTo(assemblyMatch.Id));
    }

    private sealed class TestExtension : IExtensionLifecycle
    {
        public Guid Id { get; set; }

        public Version Version { get; } = new(1, 0, 1);

        public string Name { get; } = "Test extension";

        public string? TargetABI => null;

        public void RegisterServices(IServiceCollection services)
        {
        }
    }
}
