using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Cardigann;
using Magpie.Models.Data;
using Magpie.Models.Jobs;
using Magpie.Sdk.Extensions;
using Magpie.Sdk.Services;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Cardigann;

[TestFixture]
public sealed class CardigannExtensionSeedTests
{
    [Test]
    public async Task OnExtensionInstallAsync_CreatesSingleFeed_WhenRunTwice()
    {
        // Arrange
        var store = new List<IndexerFeed>
        {
            new()
            {
                Id = Guid.NewGuid(),
                ProviderType = "other-feed",
                Name = "Other feed",
                Enabled = true,
                Settings = new Dictionary<string, string>
                {
                    ["gitUrl"] = "https://github.com/Jackett/Jackett.git",
                },
            },
        };
        var indexers = new Mock<IIndexerService>();
        indexers
            .Setup(s => s.ListFeedsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => [.. store]);
        indexers
            .Setup(s => s.CreateFeedAsync(It.IsAny<IndexerFeed>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IndexerFeed feed, Guid? _, CancellationToken _) =>
            {
                var view = new IndexerFeed
                {
                    Id = feed.Id,
                    ProviderType = feed.ProviderType,
                    Name = feed.Name,
                    Enabled = feed.Enabled,
                    Settings = feed.Settings,
                };
                store.Add(view);
                return view;
            });

        using var serviceProvider = new ServiceCollection()
            .AddSingleton(indexers.Object)
            .AddSingleton(Mock.Of<IJobManager>())
            .BuildServiceProvider();
        var context = new ExtensionLifecycleContext("node", serviceProvider, NullLoggerFactory.Instance);
        var extension = new CardigannExtension();

        // Act
        await extension.OnExtensionInstallAsync(context).ConfigureAwait(false);
        await extension.OnExtensionInstallAsync(context).ConfigureAwait(false);

        // Assert
        var cardigannFeed = store.Single(feed =>
            string.Equals(feed.ProviderType, "cardigann", StringComparison.OrdinalIgnoreCase));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(store, Has.Count.EqualTo(2));
            Assert.That(cardigannFeed.Name, Is.EqualTo("Cardigann community definitions"));
            indexers.Verify(
                s => s.CreateFeedAsync(It.IsAny<IndexerFeed>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task OnExtensionInstallAsync_EnqueuesFeedSync_WhenFeedIsSeeded()
    {
        // Arrange
        var store = new List<IndexerFeed>();
        var indexers = new Mock<IIndexerService>();
        indexers
            .Setup(s => s.ListFeedsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => [.. store]);
        indexers
            .Setup(s => s.CreateFeedAsync(It.IsAny<IndexerFeed>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IndexerFeed feed, Guid? _, CancellationToken _) =>
            {
                var view = new IndexerFeed
                {
                    Id = feed.Id,
                    ProviderType = feed.ProviderType,
                    Name = feed.Name,
                    Enabled = feed.Enabled,
                    Settings = feed.Settings,
                };
                store.Add(view);
                return view;
            });
        var jobManager = new Mock<IJobManager>();
        jobManager
            .Setup(m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        using var serviceProvider = new ServiceCollection()
            .AddSingleton(indexers.Object)
            .AddSingleton(jobManager.Object)
            .BuildServiceProvider();
        var context = new ExtensionLifecycleContext("node", serviceProvider, NullLoggerFactory.Instance);
        var extension = new CardigannExtension();

        // Act
        await extension.OnExtensionInstallAsync(context).ConfigureAwait(false);

        // Assert
        jobManager.Verify(
            m => m.ExecuteAsync(
                It.Is<JobEnqueueRequest>(r => r.JobType == "IndexerFeedSyncScan"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task OnExtensionInstallAsync_DoesNotEnqueueFeedSync_WhenFeedAlreadyExists()
    {
        // Arrange
        var store = new List<IndexerFeed>
        {
            new()
            {
                Id = Guid.NewGuid(),
                ProviderType = "cardigann",
                Name = "Cardigann community definitions",
                Enabled = true,
                Settings = new Dictionary<string, string>
                {
                    ["gitUrl"] = "https://github.com/Jackett/Jackett.git",
                },
            },
        };
        var indexers = new Mock<IIndexerService>();
        indexers
            .Setup(s => s.ListFeedsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => [.. store]);
        var jobManager = new Mock<IJobManager>();

        using var serviceProvider = new ServiceCollection()
            .AddSingleton(indexers.Object)
            .AddSingleton(jobManager.Object)
            .BuildServiceProvider();
        var context = new ExtensionLifecycleContext("node", serviceProvider, NullLoggerFactory.Instance);
        var extension = new CardigannExtension();

        // Act
        await extension.OnExtensionInstallAsync(context).ConfigureAwait(false);

        // Assert
        jobManager.Verify(
            m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
