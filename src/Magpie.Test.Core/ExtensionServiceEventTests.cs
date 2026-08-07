using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Config;
using Magpie.Core.Extensions;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Sdk.Events;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core;

[TestFixture]
public sealed class ExtensionServiceEventTests
{
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
            Enabled = false
        };
        var extensions = new Mock<IExtensionRepository>();
        extensions
            .Setup(repository => repository.GetByIdAsync(extensionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(extension);
        extensions
            .Setup(repository => repository.UpdateAsync(
                It.Is<Extension>(candidate => candidate.Id == extensionId && candidate.Enabled),
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
        using var httpClient = new HttpClient();
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var packageSources = Mock.Of<IExtensionPackageSourceRepository>();
        var subject = new ExtensionService(
            httpClient,
            extensions.Object,
            packageSources,
            activationGeneration.Object,
            new("node-1", false, false, ".", ".", ".", ".", DateTime.UtcNow),
            new MagpieHostOptions(),
            Mock.Of<IExtensionLifecycleManager>(),
            eventBus.Object,
            memoryCache,
            Mock.Of<IFileSystem>(),
            TimeProvider.System,
            NullLogger<ExtensionService>.Instance);

        // Act
        var result = await subject.EnableAsync(extensionId, Guid.NewGuid()).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(Extension.Enabled)).True);
            eventBus.Verify(
                bus => bus.PublishAsync(
                    It.Is<MagpieEvent>(magpieEvent =>
                        magpieEvent.Name == MagpieEvents.Names.ExtensionEnabled &&
                        magpieEvent.Context[MagpieEvents.Keys.ExtensionId] == extensionId.ToString("D")),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }
}
