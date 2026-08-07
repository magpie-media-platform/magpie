using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Extensions;
using Magpie.Models.Data;
using Magpie.Sdk.Extensions;
using Magpie.Sdk.Services;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Extensions;

[TestFixture]
public sealed class ExtensionLifecycleManagerTests
{
    private TestExtension _extension = null!;
    private Mock<IExtensionRepository> _repository = null!;
    private Mock<IFileSystem> _fileSystem = null!;
    private Extension? _updatedRow;
    private IExtensionLifecycleManager _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _extension = new TestExtension();
        _updatedRow = null;
        _repository = new Mock<IExtensionRepository>();
        _repository
            .Setup(candidate => candidate.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _repository
            .Setup(candidate => candidate.UpdateAsync(
                It.IsAny<Extension>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback<Extension, Guid?, CancellationToken>((row, _, _) => _updatedRow = row)
            .ReturnsAsync(true);
        _fileSystem = new Mock<IFileSystem>();

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
        _subject = new ExtensionLifecycleManager(
            new ExtensionLoadContext([_extension], []),
            Mock.Of<IEventBus>(),
            scopeFactory.Object,
            TimeProvider.System,
            NullLogger<ExtensionLifecycleManager>.Instance);
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
        IExtensionLifecycleManager subject = new ExtensionLifecycleManager(
            new ExtensionLoadContext([lifecycle.Object], []),
            Mock.Of<IEventBus>(),
            scopeFactory.Object,
            TimeProvider.System,
            NullLogger<ExtensionLifecycleManager>.Instance);

        // Act
        await subject.DispatchStartupHooksAsync().ConfigureAwait(false);
        await subject.DispatchStartupHooksAsync().ConfigureAwait(false);

        // Assert
        Assert.That(row.LifecycleVersion, Is.EqualTo("1.0.0"));
        lifecycle.Verify(extension => extension.OnExtensionUpgradeAsync(
            It.IsAny<ExtensionLifecycleContext>(),
            new Version(1, 0, 0),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
        repository.Verify(candidate => candidate.UpdateAsync(
            It.IsAny<Extension>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Never);
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
            Assert.That(_updatedRow?.Id, Is.EqualTo(idMatch.Id));
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
            Id = new Guid("33333333-3333-3333-3333-333333333333"),
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
        Assert.That(_updatedRow?.Id, Is.EqualTo(pathMatch.Id));
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
        Assert.That(_updatedRow?.Id, Is.EqualTo(packageMatch.Id));
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
        Assert.That(_updatedRow?.Id, Is.EqualTo(assemblyMatch.Id));
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
