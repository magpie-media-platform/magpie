using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Jobs;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Integrations;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs.Fulfillment;

[TestFixture]
public sealed class IndexerFeedSyncItemJobTests
{
    private readonly IProgressReporter _progressReporter = Mock.Of<IProgressReporter>();

    private Mock<IIndexerFeedRepository> _indexerFeedRepository = null!;
    private Mock<IIndexerService> _indexerService = null!;
    private Mock<IAuditRecordRepository> _auditRecordRepository = null!;
    private Mock<ITransactionManager> _transactionManager = null!;
    private List<IndexerFeed> _updatedFeeds = null!;
    private List<(IndexerFeed Feed, IReadOnlyList<Indexer> Discovered, Guid? UserId)> _syncCalls = null!;
    private List<AuditRecord> _auditRecords = null!;
    private JobContext _jobContext = null!;
    private Guid _userId;

    [SetUp]
    public void SetUp()
    {
        _userId = Guid.NewGuid();
        _updatedFeeds = [];
        _syncCalls = [];
        _auditRecords = [];

        _indexerFeedRepository = new Mock<IIndexerFeedRepository>();
        _indexerFeedRepository
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IndexerFeed?)null);
        _indexerFeedRepository
            .Setup(r => r.UpdateAsync(It.IsAny<IndexerFeed>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Returns((IndexerFeed feed, Guid? _, CancellationToken _) =>
            {
                _updatedFeeds.Add(feed);
                return Task.FromResult(true);
            });

        _indexerService = new Mock<IIndexerService>();
        _indexerService
            .Setup(r => r.SyncFeedIndexersAsync(
                It.IsAny<IndexerFeed>(),
                It.IsAny<IReadOnlyList<Indexer>>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IndexerFeed, IReadOnlyList<Indexer>, Guid?, CancellationToken>(
                (feed, discovered, userId, _) => _syncCalls.Add((feed, discovered, userId)))
            .Returns(Task.CompletedTask);

        _auditRecordRepository = new Mock<IAuditRecordRepository>();
        _auditRecordRepository
            .Setup(r => r.CreateAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()))
            .Returns((AuditRecord record, CancellationToken _) =>
            {
                _auditRecords.Add(record);
                return Task.FromResult(record);
            });

        _transactionManager = new Mock<ITransactionManager>();
        _transactionManager
            .Setup(m => m.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
                operation(cancellationToken));

        _jobContext = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: _userId,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>(),
            Event: null);
    }

    [Test]
    public void ExecuteAsync_Throws_WhenFeedIdParameterIsMissing()
    {
        // Arrange
        var provider = new Mock<IIndexerFeedProvider>();
        provider.SetupGet(p => p.ProviderType).Returns("fake");
        var service = new IndexerFeedSyncItemJob(
            [provider.Object],
            [],
            _indexerFeedRepository.Object,
            _indexerService.Object,
            _auditRecordRepository.Object,
            _transactionManager.Object,
            TimeProvider.System,
            NullLogger<IndexerFeedSyncItemJob>.Instance,
            _progressReporter);

        // Act
        var ex = Assert.ThrowsAsync<ArgumentException>(() => service.ExecuteAsync(_jobContext));

        // Assert
        Assert.That(ex!.Message, Does.Contain("An indexer feed ID is required."));
    }

    [Test]
    public void ExecuteAsync_Throws_WhenFeedIdParameterIsNotAGuid()
    {
        // Arrange
        var provider = new Mock<IIndexerFeedProvider>();
        provider.SetupGet(p => p.ProviderType).Returns("fake");
        var service = new IndexerFeedSyncItemJob(
            [provider.Object],
            [],
            _indexerFeedRepository.Object,
            _indexerService.Object,
            _auditRecordRepository.Object,
            _transactionManager.Object,
            TimeProvider.System,
            NullLogger<IndexerFeedSyncItemJob>.Instance,
            _progressReporter);
        var context = _jobContext with
        {
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["feedId"] = "not-a-guid"
            }
        };

        // Act
        var ex = Assert.ThrowsAsync<ArgumentException>(() => service.ExecuteAsync(context));

        // Assert
        Assert.That(ex!.Message, Does.Contain("The indexer feed ID has an invalid format."));
    }

    [Test]
    public void ExecuteAsync_Throws_WhenFeedIsNotFound()
    {
        // Arrange
        var feedId = Guid.NewGuid();
        var provider = new Mock<IIndexerFeedProvider>();
        provider.SetupGet(p => p.ProviderType).Returns("fake");
        var service = new IndexerFeedSyncItemJob(
            [provider.Object],
            [],
            _indexerFeedRepository.Object,
            _indexerService.Object,
            _auditRecordRepository.Object,
            _transactionManager.Object,
            TimeProvider.System,
            NullLogger<IndexerFeedSyncItemJob>.Instance,
            _progressReporter);
        var context = _jobContext with
        {
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["feedId"] = feedId.ToString("D")
            }
        };

        // Act
        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteAsync(context));

        // Assert
        Assert.That(ex!.Message, Does.Contain(feedId.ToString()));
    }

    [Test]
    public async Task ExecuteAsync_DoesNothing_WhenFeedIsDisabled()
    {
        // Arrange
        var feed = new IndexerFeed
        {
            Id = Guid.NewGuid(),
            Name = "Disabled",
            ProviderType = "fake",
            Enabled = false,
            Settings = [],
            CreatedAt = DateTime.UtcNow
        };
        _indexerFeedRepository
            .Setup(r => r.GetByIdAsync(feed.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(feed);
        var provider = new Mock<IIndexerFeedProvider>();
        provider.SetupGet(p => p.ProviderType).Returns("fake");
        provider
            .Setup(p => p.SyncAsync(It.IsAny<IndexerFeed>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var service = new IndexerFeedSyncItemJob(
            [provider.Object],
            [],
            _indexerFeedRepository.Object,
            _indexerService.Object,
            _auditRecordRepository.Object,
            _transactionManager.Object,
            TimeProvider.System,
            NullLogger<IndexerFeedSyncItemJob>.Instance,
            _progressReporter);
        var context = _jobContext with
        {
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["feedId"] = feed.Id.ToString("D")
            }
        };

        // Act
        await service.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            provider.Verify(p => p.SyncAsync(It.IsAny<IndexerFeed>(), It.IsAny<CancellationToken>()), Times.Never);
            Assert.That(_syncCalls, Is.Empty);
            Assert.That(_updatedFeeds, Is.Empty);
        }
    }

    [Test]
    public async Task ExecuteAsync_SkipsSync_WhenNoProviderIsRegisteredForProviderType()
    {
        // Arrange
        var feed = new IndexerFeed
        {
            Id = Guid.NewGuid(),
            Name = "Unknown",
            ProviderType = "missing",
            Enabled = true,
            Settings = [],
            CreatedAt = DateTime.UtcNow
        };
        _indexerFeedRepository
            .Setup(r => r.GetByIdAsync(feed.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(feed);
        var provider = new Mock<IIndexerFeedProvider>();
        provider.SetupGet(p => p.ProviderType).Returns("fake");
        provider
            .Setup(p => p.SyncAsync(It.IsAny<IndexerFeed>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var service = new IndexerFeedSyncItemJob(
            [provider.Object],
            [],
            _indexerFeedRepository.Object,
            _indexerService.Object,
            _auditRecordRepository.Object,
            _transactionManager.Object,
            TimeProvider.System,
            NullLogger<IndexerFeedSyncItemJob>.Instance,
            _progressReporter);
        var context = _jobContext with
        {
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["feedId"] = feed.Id.ToString("D")
            }
        };

        // Act
        await service.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            provider.Verify(p => p.SyncAsync(It.IsAny<IndexerFeed>(), It.IsAny<CancellationToken>()), Times.Never);
            Assert.That(_syncCalls, Is.Empty);
            Assert.That(_updatedFeeds, Is.Empty);
            Assert.That(_auditRecords, Is.Empty);
        }
    }

    [Test]
    public async Task ExecuteAsync_InsertsNewIndexers_WhenTheProviderReturnsNewIndexers()
    {
        // Arrange
        var feed = new IndexerFeed
        {
            Id = Guid.NewGuid(),
            Name = "Indexer A",
            ProviderType = "fake",
            Enabled = true,
            Settings = [],
            CreatedAt = DateTime.UtcNow
        };
        _indexerFeedRepository
            .Setup(r => r.GetByIdAsync(feed.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(feed);
        var provider = new Mock<IIndexerFeedProvider>();
        provider.SetupGet(p => p.ProviderType).Returns("fake");
        provider
            .Setup(p => p.SyncAsync(It.Is<IndexerFeed>(f => f.Id == feed.Id), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Indexer
                {
                    Name = "Tracker A",
                    ProviderType = "tracker-a",
                    Description = "A torznab tracker",
                    Availability = IndexerAvailability.Public,
                    Categories = ["movies", "tv"],
                    Languages = ["en"]
                },
                new Indexer
                {
                    Name = "Tracker B",
                    ProviderType = "tracker-b",
                    Availability = IndexerAvailability.Private
                }
            ]);
        var service = new IndexerFeedSyncItemJob(
            [provider.Object],
            [],
            _indexerFeedRepository.Object,
            _indexerService.Object,
            _auditRecordRepository.Object,
            _transactionManager.Object,
            TimeProvider.System,
            NullLogger<IndexerFeedSyncItemJob>.Instance,
            _progressReporter);
        var context = _jobContext with
        {
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["feedId"] = feed.Id.ToString("D")
            }
        };

        // Act
        await service.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        Assert.That(_syncCalls, Has.Count.EqualTo(1));
        var (syncedFeed, discovered, syncedByUserId) = _syncCalls[0];
        var ordered = discovered.OrderBy(i => i.Name).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(syncedFeed.Id, Is.EqualTo(feed.Id));
            Assert.That(syncedByUserId, Is.EqualTo(_userId));
            Assert.That(ordered, Has.Count.EqualTo(2));
            Assert.That(ordered[0].Name, Is.EqualTo("Tracker A"));
            Assert.That(ordered[0].ProviderType, Is.EqualTo("tracker-a"));
            Assert.That(ordered[0].Description, Is.EqualTo("A torznab tracker"));
            Assert.That(ordered[0].Availability, Is.EqualTo(IndexerAvailability.Public));
            Assert.That(ordered[0].Categories, Is.EquivalentTo(["movies", "tv"]));
            Assert.That(ordered[0].Languages, Is.EquivalentTo(["en"]));
            Assert.That(ordered[1].Name, Is.EqualTo("Tracker B"));
            Assert.That(ordered[1].Availability, Is.EqualTo(IndexerAvailability.Private));
        }
    }

    [Test]
    public async Task ExecuteAsync_DoesNotPrune_WhenFeedReturnsEmptySet()
    {
        // Arrange
        var feed = new IndexerFeed
        {
            Id = Guid.NewGuid(),
            Name = "Indexer A",
            ProviderType = "fake",
            Enabled = true,
            Settings = [],
            CreatedAt = DateTime.UtcNow
        };
        _indexerFeedRepository
            .Setup(r => r.GetByIdAsync(feed.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(feed);
        var provider = new Mock<IIndexerFeedProvider>();
        provider.SetupGet(p => p.ProviderType).Returns("fake");
        provider
            .Setup(p => p.SyncAsync(It.IsAny<IndexerFeed>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var service = new IndexerFeedSyncItemJob(
            [provider.Object],
            [],
            _indexerFeedRepository.Object,
            _indexerService.Object,
            _auditRecordRepository.Object,
            _transactionManager.Object,
            TimeProvider.System,
            NullLogger<IndexerFeedSyncItemJob>.Instance,
            _progressReporter);
        var context = _jobContext with
        {
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["feedId"] = feed.Id.ToString("D")
            }
        };

        // Act
        await service.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        _indexerService.Verify(
            r => r.SyncFeedIndexersAsync(
                It.IsAny<IndexerFeed>(),
                It.IsAny<IReadOnlyList<Indexer>>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_BumpsLastSync_WhenFeedSyncSucceeds()
    {
        // Arrange
        var beforeSync = DateTime.UtcNow;
        var feed = new IndexerFeed
        {
            Id = Guid.NewGuid(),
            Name = "Indexer A",
            ProviderType = "fake",
            Enabled = true,
            Settings = [],
            CreatedAt = DateTime.UtcNow
        };
        _indexerFeedRepository
            .Setup(r => r.GetByIdAsync(feed.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(feed);
        var provider = new Mock<IIndexerFeedProvider>();
        provider.SetupGet(p => p.ProviderType).Returns("fake");
        provider
            .Setup(p => p.SyncAsync(It.IsAny<IndexerFeed>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var service = new IndexerFeedSyncItemJob(
            [provider.Object],
            [],
            _indexerFeedRepository.Object,
            _indexerService.Object,
            _auditRecordRepository.Object,
            _transactionManager.Object,
            TimeProvider.System,
            NullLogger<IndexerFeedSyncItemJob>.Instance,
            _progressReporter);
        var context = _jobContext with
        {
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["feedId"] = feed.Id.ToString("D")
            }
        };

        // Act
        await service.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        var stamped = _updatedFeeds.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stamped.Id, Is.EqualTo(feed.Id));
            Assert.That(stamped.LastSync, Is.Not.Null);
            Assert.That(stamped.LastSync, Is.GreaterThanOrEqualTo(beforeSync));
        }
    }

    [Test]
    public async Task ExecuteAsync_WritesIntegrationsAuditRow_WhenIndexersAreUpserted()
    {
        // Arrange
        var feed = new IndexerFeed
        {
            Id = Guid.NewGuid(),
            Name = "Indexer A",
            ProviderType = "fake",
            Enabled = true,
            Settings = [],
            CreatedAt = DateTime.UtcNow
        };
        _indexerFeedRepository
            .Setup(r => r.GetByIdAsync(feed.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(feed);
        var provider = new Mock<IIndexerFeedProvider>();
        provider.SetupGet(p => p.ProviderType).Returns("fake");
        provider
            .Setup(p => p.SyncAsync(It.Is<IndexerFeed>(f => f.Id == feed.Id), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Indexer { Name = "Tracker A", ProviderType = "tracker-a" },
                new Indexer { Name = "Tracker B", ProviderType = "tracker-b" }
            ]);
        var service = new IndexerFeedSyncItemJob(
            [provider.Object],
            [],
            _indexerFeedRepository.Object,
            _indexerService.Object,
            _auditRecordRepository.Object,
            _transactionManager.Object,
            TimeProvider.System,
            NullLogger<IndexerFeedSyncItemJob>.Instance,
            _progressReporter);
        var context = _jobContext with
        {
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["feedId"] = feed.Id.ToString("D")
            }
        };

        // Act
        await service.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        var syncAudit = _auditRecords
            .Where(a => a.Operation == "Sync" && a.EntityType == nameof(IndexerFeed))
            .ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_syncCalls, Has.Count.EqualTo(1));
            Assert.That(_syncCalls[0].Discovered, Has.Count.EqualTo(2));
            Assert.That(syncAudit, Has.Count.EqualTo(1));
            Assert.That(syncAudit[0].Category, Is.EqualTo(AuditCategories.Integrations));
            Assert.That(syncAudit[0].EntityId, Is.EqualTo(feed.Id.ToString()));
            Assert.That(syncAudit[0].UserId, Is.EqualTo(_userId));
            Assert.That(syncAudit[0].Detail, Does.Contain("2"));
            Assert.That(syncAudit[0].Detail, Does.Contain("Indexer A"));
        }
    }

    [Test]
    public void ExecuteAsync_PropagatesFailure_WhenProviderSyncThrows()
    {
        // Arrange
        var feed = new IndexerFeed
        {
            Id = Guid.NewGuid(),
            Name = "Indexer A",
            ProviderType = "fake-fail",
            Enabled = true,
            Settings = [],
            CreatedAt = DateTime.UtcNow
        };
        _indexerFeedRepository
            .Setup(r => r.GetByIdAsync(feed.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(feed);
        var provider = new Mock<IIndexerFeedProvider>();
        provider.SetupGet(p => p.ProviderType).Returns("fake-fail");
        provider
            .Setup(p => p.SyncAsync(It.IsAny<IndexerFeed>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Provider failed"));
        var service = new IndexerFeedSyncItemJob(
            [provider.Object],
            [],
            _indexerFeedRepository.Object,
            _indexerService.Object,
            _auditRecordRepository.Object,
            _transactionManager.Object,
            TimeProvider.System,
            NullLogger<IndexerFeedSyncItemJob>.Instance,
            _progressReporter);
        var context = _jobContext with
        {
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["feedId"] = feed.Id.ToString("D")
            }
        };

        // Act
        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteAsync(context));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ex!.Message, Is.EqualTo("Provider failed"));
            Assert.That(_syncCalls, Is.Empty);
            Assert.That(_updatedFeeds, Is.Empty);
            Assert.That(_auditRecords, Is.Empty);
        }
    }

    [Test]
    public async Task ExecuteAsync_PropagatesCancellation_WhenTheTokenIsAlreadyCancelled()
    {
        // Arrange
        var feed = new IndexerFeed
        {
            Id = Guid.NewGuid(),
            Name = "Slow",
            ProviderType = "fake",
            Enabled = true,
            Settings = [],
            CreatedAt = DateTime.UtcNow
        };
        _indexerFeedRepository
            .Setup(r => r.GetByIdAsync(feed.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(feed);
        var provider = new Mock<IIndexerFeedProvider>();
        provider.SetupGet(p => p.ProviderType).Returns("fake");
        provider
            .Setup(p => p.SyncAsync(It.IsAny<IndexerFeed>(), It.IsAny<CancellationToken>()))
            .Returns(async (IndexerFeed _, CancellationToken cancellationToken) =>
            {
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                return [];
            });
        var service = new IndexerFeedSyncItemJob(
            [provider.Object],
            [],
            _indexerFeedRepository.Object,
            _indexerService.Object,
            _auditRecordRepository.Object,
            _transactionManager.Object,
            TimeProvider.System,
            NullLogger<IndexerFeedSyncItemJob>.Instance,
            _progressReporter);
        var context = _jobContext with
        {
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["feedId"] = feed.Id.ToString("D")
            }
        };

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync().ConfigureAwait(false);

        // Act
        var caught = Assert.CatchAsync<OperationCanceledException>(
            () => service.ExecuteAsync(context, cts.Token));

        // Assert
        Assert.That(caught, Is.Not.Null);
    }
}
