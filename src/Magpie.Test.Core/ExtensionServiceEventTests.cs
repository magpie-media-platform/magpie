using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Caching;
using Magpie.Core.Config;
using Magpie.Core.Extensions;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core;

[TestFixture]
public sealed class ExtensionServiceEventTests
{
    private string _extensionsRoot = null!;

    [SetUp]
    public void SetUp()
    {
        _extensionsRoot = Path.Combine(Path.GetTempPath(), $"magpie-extension-service-{Guid.NewGuid():N}");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_extensionsRoot))
        {
            Directory.Delete(_extensionsRoot, recursive: true);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task PatchAsync_ReturnsExistenceWithoutWriting_WhenPatchIsEmpty(bool exists)
    {
        // Arrange
        var extensionId = Guid.NewGuid();
        var extension = new Extension
        {
            Id = extensionId,
            Name = "Test extension",
            Version = "1.0.0",
            FilePath = "test.dll",
            AssemblyName = "Test.Extension"
        };
        var extensions = new Mock<IExtensionRepository>();
        extensions
            .Setup(repository => repository.GetByIdAsync(extensionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(exists ? extension : null);
        var activationGeneration = new Mock<IExtensionActivationGenerationRepository>();
        var eventBus = new Mock<IEventBus>();
        using var cacheStore = new MemoryCacheStore(TimeProvider.System);
        using var cacheFactory = new MemoryCacheFactory(cacheStore);
        var subject = new ExtensionService(
            Mock.Of<IHttpClientFactory>(),
            extensions.Object,
            Mock.Of<IExtensionPackageSourceRepository>(),
            activationGeneration.Object,
            new("node-1", false, false, ".", ".", ".", ".", DateTime.UtcNow),
            new MagpieHostOptions(),
            Mock.Of<IExtensionLifecycleManager>(),
            eventBus.Object,
            cacheFactory,
            Mock.Of<IFileSystem>(),
            TimeProvider.System,
            NullLogger<ExtensionService>.Instance);

        // Act
        var result = await subject.PatchAsync(extensionId, new(), null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo(exists));
            extensions.Verify(repository => repository.PatchAsync(
                It.IsAny<ExtensionPatch>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
            activationGeneration.VerifyNoOtherCalls();
            eventBus.VerifyNoOtherCalls();
        }
    }

    [Test]
    public async Task PatchAsync_WritesOnePatch_WhenThePatchAlsoDisablesTheExtension()
    {
        // Arrange
        var extensionId = Guid.NewGuid();
        var extension = new Extension
        {
            Id = extensionId,
            Name = "Test extension",
            Version = "1.0.0",
            FilePath = "test.dll",
            AssemblyName = "Test.Extension",
            Enabled = true
        };
        var extensions = new Mock<IExtensionRepository>();
        extensions
            .Setup(repository => repository.GetByIdAsync(extensionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(extension);
        var applied = default(ExtensionPatch);
        extensions
            .Setup(repository => repository.PatchAsync(
                It.IsAny<ExtensionPatch>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback<ExtensionPatch, Guid?, CancellationToken>((patch, _, _) => applied = patch)
            .ReturnsAsync(true);
        var activationGeneration = new Mock<IExtensionActivationGenerationRepository>();
        activationGeneration
            .Setup(repository => repository.BumpAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var eventBus = new Mock<IEventBus>();
        eventBus
            .Setup(bus => bus.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var lifecycle = new Mock<IExtensionLifecycleManager>();
        lifecycle
            .Setup(manager => manager.InvokeDisableAsync(It.IsAny<Extension>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        using var cacheStore = new MemoryCacheStore(TimeProvider.System);
        using var cacheFactory = new MemoryCacheFactory(cacheStore);
        var subject = new ExtensionService(
            Mock.Of<IHttpClientFactory>(),
            extensions.Object,
            Mock.Of<IExtensionPackageSourceRepository>(),
            activationGeneration.Object,
            new("node-1", false, false, ".", ".", ".", ".", DateTime.UtcNow),
            new MagpieHostOptions(),
            lifecycle.Object,
            eventBus.Object,
            cacheFactory,
            Mock.Of<IFileSystem>(),
            TimeProvider.System,
            NullLogger<ExtensionService>.Instance);

        // Act
        var result = await subject.PatchAsync(
            extensionId,
            new() { Enabled = new() { Value = false }, LoadOrder = new() { Value = 7 } },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(applied?.Enabled?.Value, Is.False);
            Assert.That(applied?.LoadOrder?.Value, Is.EqualTo(7));
            extensions.Verify(repository => repository.PatchAsync(
                It.IsAny<ExtensionPatch>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
            lifecycle.Verify(manager => manager.InvokeDisableAsync(
                It.IsAny<Extension>(), It.IsAny<CancellationToken>()), Times.Once);
            eventBus.Verify(
                bus => bus.PublishAsync(
                    It.Is<MagpieEvent>(magpieEvent => magpieEvent.Name == "extension.disabled"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task EnableAsync_PublishesEnabled_WhenExtensionIsEnabled()
    {
        // Arrange
        var extensionId = Guid.NewGuid();
        var extension = new Extension
        {
            Id = extensionId,
            Name = "Test extension",
            Version = "1.0.0",
            FilePath = "test.dll",
            AssemblyName = "Test.Extension",
            Enabled = false,
            DisabledReason = "Activation failed"
        };
        var extensions = new Mock<IExtensionRepository>();
        extensions
            .Setup(repository => repository.GetByIdAsync(extensionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(extension);
        extensions
            .Setup(repository => repository.PatchAsync(
                It.Is<ExtensionPatch>(patch => patch.Id == extensionId && patch.Enabled!.Value &&
                    patch.DisabledReason != null && patch.DisabledReason.Value == null),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var activationGeneration = new Mock<IExtensionActivationGenerationRepository>();
        activationGeneration
            .Setup(repository => repository.BumpAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var eventBus = new Mock<IEventBus>();
        eventBus
            .Setup(bus => bus.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        using var cacheStore = new MemoryCacheStore(TimeProvider.System);
        using var cacheFactory = new MemoryCacheFactory(cacheStore);
        var packageSources = Mock.Of<IExtensionPackageSourceRepository>();
        var subject = new ExtensionService(
            Mock.Of<IHttpClientFactory>(),
            extensions.Object,
            packageSources,
            activationGeneration.Object,
            new("node-1", false, false, ".", ".", ".", ".", DateTime.UtcNow),
            new MagpieHostOptions(),
            Mock.Of<IExtensionLifecycleManager>(),
            eventBus.Object,
            cacheFactory,
            Mock.Of<IFileSystem>(),
            TimeProvider.System,
            NullLogger<ExtensionService>.Instance);

        // Act
        var result = await subject.EnableAsync(extensionId, Guid.NewGuid()).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(Extension.Enabled)).True);
            Assert.That(result, Has.Property(nameof(Extension.DisabledReason)).Null);
            eventBus.Verify(
                bus => bus.PublishAsync(
                    It.Is<MagpieEvent>(magpieEvent =>
                        magpieEvent.Name == "extension.enabled" &&
                        magpieEvent.Context["extension.id"] == extensionId.ToString("D")),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task UninstallAsync_PreservesDirectory_WhenRepositoryDeleteFails()
    {
        // Arrange
        var extensionId = Guid.NewGuid();
        var extensionDirectory = Path.Combine(_extensionsRoot, "test-extension");

        Directory.CreateDirectory(extensionDirectory);

        var extension = new Extension
        {
            Id = extensionId,
            Name = "Test extension",
            Version = "1.0.0",
            FilePath = Path.Combine(extensionDirectory, "test.dll"),
            AssemblyName = "Test.Extension",
            Enabled = true
        };

        var extensions = new Mock<IExtensionRepository>();
        extensions
            .Setup(repository => repository.GetByIdAsync(extensionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(extension);
        extensions
            .Setup(repository => repository.DeleteAsync(
                extensionId,
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var lifecycleManager = new Mock<IExtensionLifecycleManager>();
        lifecycleManager
            .Setup(manager => manager.InvokeUninstallAsync(extension, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        using var cacheStore = new MemoryCacheStore(TimeProvider.System);
        using var cacheFactory = new MemoryCacheFactory(cacheStore);

        var fileSystem = new FileSystem(
            Mock.Of<IBlockedFileTypeRepository>(),
            Mock.Of<IMimeMappingRepository>(),
            cacheFactory,
            NullLogger<FileSystem>.Instance);
        var subject = new ExtensionService(
            Mock.Of<IHttpClientFactory>(),
            extensions.Object,
            Mock.Of<IExtensionPackageSourceRepository>(),
            Mock.Of<IExtensionActivationGenerationRepository>(),
            new(
                "node-1",
                false,
                false,
                _extensionsRoot,
                _extensionsRoot,
                _extensionsRoot,
                _extensionsRoot,
                DateTime.UtcNow),
            new MagpieHostOptions(),
            lifecycleManager.Object,
            Mock.Of<IEventBus>(),
            cacheFactory,
            fileSystem,
            TimeProvider.System,
            NullLogger<ExtensionService>.Instance);

        // Act
        var result = await subject.UninstallAsync(extensionId, Guid.NewGuid()).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.False);
            Assert.That(Directory.Exists(extensionDirectory), Is.True);
        }
    }

    [Test]
    public void UninstallAsync_PreservesDirectory_WhenRepositoryDeleteThrows()
    {
        // Arrange
        var extensionId = Guid.NewGuid();
        var extensionDirectory = Path.Combine(_extensionsRoot, "test-extension");
        Directory.CreateDirectory(extensionDirectory);
        var extension = new Extension
        {
            Id = extensionId,
            Name = "Test extension",
            Version = "1.0.0",
            FilePath = Path.Combine(extensionDirectory, "test.dll"),
            AssemblyName = "Test.Extension",
            Enabled = true
        };
        var extensions = new Mock<IExtensionRepository>();
        extensions
            .Setup(repository => repository.GetByIdAsync(extensionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(extension);
        extensions
            .Setup(repository => repository.DeleteAsync(
                extensionId,
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("delete failed"));
        var lifecycleManager = new Mock<IExtensionLifecycleManager>();
        lifecycleManager
            .Setup(manager => manager.InvokeUninstallAsync(extension, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        using var cacheStore = new MemoryCacheStore(TimeProvider.System);
        using var cacheFactory = new MemoryCacheFactory(cacheStore);
        var fileSystem = new FileSystem(
            Mock.Of<IBlockedFileTypeRepository>(),
            Mock.Of<IMimeMappingRepository>(),
            cacheFactory,
            NullLogger<FileSystem>.Instance);
        var subject = new ExtensionService(
            Mock.Of<IHttpClientFactory>(),
            extensions.Object,
            Mock.Of<IExtensionPackageSourceRepository>(),
            Mock.Of<IExtensionActivationGenerationRepository>(),
            new(
                "node-1",
                false,
                false,
                _extensionsRoot,
                _extensionsRoot,
                _extensionsRoot,
                _extensionsRoot,
                DateTime.UtcNow),
            new MagpieHostOptions(),
            lifecycleManager.Object,
            Mock.Of<IEventBus>(),
            cacheFactory,
            fileSystem,
            TimeProvider.System,
            NullLogger<ExtensionService>.Instance);

        // Act
        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            () => subject.UninstallAsync(extensionId, Guid.NewGuid()));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Is.EqualTo("delete failed"));
            Assert.That(Directory.Exists(extensionDirectory), Is.True);
        }
    }
}
