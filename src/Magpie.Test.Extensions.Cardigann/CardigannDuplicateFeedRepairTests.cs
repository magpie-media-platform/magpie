using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Cardigann;
using Magpie.Models.Data;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Cardigann;

[TestFixture]
public sealed class CardigannDuplicateFeedRepairTests
{
    [Test]
    public async Task RunAsync_CollapsesCommunityFeedsToOne_WhenDuplicatesExist()
    {
        // Arrange
        var canonicalId = Guid.NewGuid();
        var customId = Guid.NewGuid();
        var store = new List<IndexerFeed>
        {
            new()
            {
                Id = customId,
                ProviderType = "cardigann",
                Name = "My private tracker",
                Enabled = true,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                Settings = new Dictionary<string, string>
                {
                    ["gitUrl"] = "https://example.com/custom.git",
                },
            },
            new()
            {
                Id = canonicalId,
                ProviderType = "cardigann",
                Name = "Cardigann community definitions",
                Enabled = true,
                CreatedAt = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                Settings = new Dictionary<string, string>
                {
                    ["gitUrl"] = "https://github.com/Jackett/Jackett.git",
                },
            },
            new()
            {
                Id = Guid.NewGuid(),
                ProviderType = "cardigann",
                Name = "Cardigann community definitions (copy)",
                Enabled = false,
                CreatedAt = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
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
            .Setup(s => s.DeleteFeedAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, Guid? _, CancellationToken _) => store.RemoveAll(f => f.Id == id) > 0);
        var subject = new CardigannDuplicateFeedRepair(
            indexers.Object,
            NullLogger<CardigannDuplicateFeedRepair>.Instance);

        // Act
        var result = await subject.RunAsync(new DiagnosticFinding
        {
            DiagnosticCheckId = "test",
            Title = "test",
            Detail = "test",
        }).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.True);
            Assert.That(store, Has.Count.EqualTo(2));
            Assert.That(store.Select(f => f.Id), Is.EquivalentTo([canonicalId, customId]));
        }
    }

    [Test]
    public async Task RunAsync_RemovesNothing_WhenNoDuplicatesExist()
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
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
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
            .Setup(s => s.DeleteFeedAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, Guid? _, CancellationToken _) => store.RemoveAll(f => f.Id == id) > 0);
        var subject = new CardigannDuplicateFeedRepair(
            indexers.Object,
            NullLogger<CardigannDuplicateFeedRepair>.Instance);

        // Act
        var result = await subject.RunAsync(new DiagnosticFinding
        {
            DiagnosticCheckId = "test",
            Title = "test",
            Detail = "test",
        }).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.True);
            Assert.That(store, Has.Count.EqualTo(1));
        }

        indexers.Verify(
            s => s.DeleteFeedAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
