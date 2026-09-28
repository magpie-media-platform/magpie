using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Releases;
using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
using Magpie.Core;
using Magpie.Core.Jobs;
using Magpie.Core.Jobs.Downloads;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Jobs;
using Magpie.Sdk.Events;
using Magpie.Sdk.Integrations;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Downloads;

[TestFixture]
public sealed class FulfilledDownloadCancellationTests
{
    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private IDownloadRepository _downloads = null!;
    private ITransactionManager _transactions = null!;
    private Mock<IDownloadClientIntegration> _integration = null!;
    private Mock<IDownloadClientRepository> _clients = null!;
    private Mock<IEventBus> _events = null!;
    private IDownloadService _service = null!;
    private IJob _subject = null!;
    private JobContext _jobContext = null!;
    private Download _imported = null!;
    private Download _fulfilled = null!;
    private DownloadClient _client = null!;
    private ContentHierarchyItem _node = null!;
    private ContentHierarchyItem _other = null!;

    [OneTimeSetUp]
    public void CreateTemplate()
    {
        _template = new SqliteConnection("Data Source=:memory:");
        _template.Open();
        using var context = new MagpieDbContext(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite(_template)
            .Options);
        context.Database.EnsureCreated();
    }

    [OneTimeTearDown]
    public void DisposeTemplate()
    {
        _template.Dispose();
    }

    [SetUp]
    public void SetUp()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        _template.BackupDatabase(connection);
        _context = new MagpieDbContext(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite(connection, contextOwnsConnection: true)
            .Options);
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero));
        _transactions = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);
        _events = new Mock<IEventBus>();
        var audit = new AuditRecordRepository(_context, _events.Object,
            NullLogger<AuditRecordRepository>.Instance, _transactions, Mock.Of<IJobExecutionScopeFactory>(), time);
        _downloads = new DownloadRepository(_context, audit, _transactions, _events.Object,
            NullLogger<DownloadRepository>.Instance, Mock.Of<IDataProtectionService>(), time);
        _client = new DownloadClient { Name = "Client", ProviderType = "test", Enabled = true };
        _node = new ContentHierarchyItem
        {
            MetadataContainer = new(),
            ContentType = "type-a",
            Level = "leaf",
            Slug = "a",
            Name = "A",
            ContentItem = new() { Fulfillment = FulfillmentStatus.Fulfilled }
        };
        _other = new ContentHierarchyItem
        {
            MetadataContainer = new(),
            ContentType = "type-a",
            Level = "leaf",
            Slug = "b",
            Name = "B",
            ContentItem = new() { Fulfillment = FulfillmentStatus.Missing }
        };
        _context.AddRange(_client, _node, _other);
        _context.SaveChanges();
        _imported = new Download
        {
            DownloadClientId = _client.Id,
            Title = "Winning release",
            ExternalId = "imported",
            Status = DownloadStatus.Imported,
            HeldByPolicy = true,
            OutputPath = "/library/imported.mkv"
        };
        _fulfilled = new Download
        {
            DownloadClientId = _client.Id,
            Title = "Unrelated title",
            ExternalId = "fulfilled",
            Status = DownloadStatus.Downloading,
            ReleaseIdentityKeys = { new ReleaseIdentityKey { Authority = "torrent", Value = "fulfilled-hash" } }
        };
        _context.AddRange(_imported, _fulfilled);
        _context.SaveChanges();
        _context.DownloadScopes.AddRange(
            new DownloadScope { DownloadId = _imported.Id, ContentHierarchyItemId = _node.Id },
            new DownloadScope { DownloadId = _fulfilled.Id, ContentHierarchyItemId = _node.Id });
        _context.MediaFiles.Add(new MediaFile
        {
            ContentItemId = _node.ContentItem!.Id,
            FilePath = "/library/imported.mkv",
            PathKey = "/LIBRARY/IMPORTED.MKV",
            FileKind = "Video",
            FileSize = 1024
        });
        _context.SaveChanges();
        _clients = new Mock<IDownloadClientRepository>();
        _clients.Setup(repository => repository.GetByIdAsync(_client.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_client);
        _integration = new Mock<IDownloadClientIntegration>();
        _integration.SetupGet(integration => integration.ProviderType).Returns("test");
        _integration.Setup(integration => integration.RemoveDownloadAsync(
                "fulfilled", false, _client.Settings, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _service = new DownloadService([_integration.Object], _clients.Object, _downloads,
            Mock.Of<IJobManager>(), _transactions, time, NullLogger<DownloadService>.Instance);
        _subject = new DownloadFulfilledCancellationJob(_downloads, _service, Mock.Of<IProgressReporter>());
        _jobContext = new(Guid.NewGuid(), null, Guid.NewGuid(), null, JobExecutionMode.Queue,
            new Dictionary<string, string> { [MagpieJobs.Parameters.DownloadId] = _imported.Id.ToString("D") }, null);
    }

    [TearDown]
    public void TearDown()
    {
        _context.Dispose();
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ExecuteAsync_CancelsSharedTransferOnce_WhenAllDuplicateAttemptsAreFulfilled(bool sameScope)
    {
        // Arrange
        var duplicate = new Download
        {
            DownloadClientId = _client.Id,
            ExternalId = "fulfilled",
            Title = "Another indexer's attempt",
            Status = DownloadStatus.Downloading
        };
        _context.Downloads.Add(duplicate);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _other.ContentItem!.Fulfillment = FulfillmentStatus.Fulfilled;
        _context.DownloadScopes.Add(new()
        {
            DownloadId = duplicate.Id,
            ContentHierarchyItemId = sameScope ? _node.Id : _other.Id
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);
        var first = await _downloads.GetByIdAsync(_fulfilled.Id).ConfigureAwait(false);
        var second = await _downloads.GetByIdAsync(duplicate.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first!.Status, Is.EqualTo(DownloadStatus.Cancelled));
            Assert.That(second!.Status, Is.EqualTo(DownloadStatus.Cancelled));
            Assert.That(first.StatusReason, Is.EqualTo("Content fulfilled"));
            Assert.That(second.StatusReason, Is.EqualTo("Content fulfilled"));
            Assert.That(first.FailureReason, Is.Null);
            Assert.That(second.FailureReason, Is.Null);
            Assert.That((await _downloads.GetByIdAsync(_imported.Id).ConfigureAwait(false))!.Status,
                Is.EqualTo(DownloadStatus.Imported));
            Assert.That(await _context.Downloads.CountAsync().ConfigureAwait(false), Is.EqualTo(3));
            Assert.That(await _context.AuditRecords.CountAsync().ConfigureAwait(false), Is.EqualTo(2));
            _integration.Verify(integration => integration.RemoveDownloadAsync(
                "fulfilled", false, _client.Settings, It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [TestCase(DownloadStatus.Imported)]
    [TestCase(DownloadStatus.Importing)]
    public async Task ExecuteAsync_PreservesSharedTransfer_WhenAnAliasIsImportedOrImporting(DownloadStatus status)
    {
        // Arrange
        var alias = new Download
        {
            DownloadClientId = _client.Id,
            ExternalId = "fulfilled",
            Title = "Protected alias",
            Status = status
        };
        _context.Downloads.Add(alias);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.DownloadScopes.Add(new() { DownloadId = alias.Id, ContentHierarchyItemId = _node.Id });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((await _downloads.GetByIdAsync(_fulfilled.Id).ConfigureAwait(false))!.Status,
                Is.EqualTo(DownloadStatus.Downloading));
            Assert.That((await _downloads.GetByIdAsync(alias.Id).ConfigureAwait(false))!.Status, Is.EqualTo(status));
            _integration.Verify(integration => integration.RemoveDownloadAsync(
                "fulfilled", false, _client.Settings, It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [TestCase(FulfillmentStatus.Missing, true)]
    [TestCase(FulfillmentStatus.Partial, true)]
    [TestCase(FulfillmentStatus.Upgradable, true)]
    [TestCase(null, true)]
    [TestCase(FulfillmentStatus.Fulfilled, false)]
    public async Task ExecuteAsync_PreservesSharedTransfer_WhenAliasContentIsNeededOrUnknown(
        FulfillmentStatus? fulfillment, bool hasScope)
    {
        // Arrange
        var alias = new Download
        {
            DownloadClientId = _client.Id,
            ExternalId = "fulfilled",
            Title = "Other content",
            Status = DownloadStatus.Downloading
        };
        _context.Downloads.Add(alias);
        _other.ContentItem!.Fulfillment = fulfillment ?? FulfillmentStatus.Missing;
        await _context.SaveChangesAsync().ConfigureAwait(false);
        if (hasScope)
        {
            _context.DownloadScopes.Add(new() { DownloadId = alias.Id, ContentHierarchyItemId = _other.Id });
            await _context.SaveChangesAsync().ConfigureAwait(false);
        }

        if (fulfillment is null)
        {
            await _context.ContentItems.Where(item => item.ContentHierarchyItemId == _other.Id).ExecuteDeleteAsync()
                .ConfigureAwait(false);
        }

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((await _downloads.GetByIdAsync(_fulfilled.Id).ConfigureAwait(false))!.Status,
                Is.EqualTo(DownloadStatus.Downloading));
            Assert.That((await _downloads.GetByIdAsync(alias.Id).ConfigureAwait(false))!.Status,
                Is.EqualTo(DownloadStatus.Downloading));
            _integration.Verify(integration => integration.RemoveDownloadAsync(
                "fulfilled", false, _client.Settings, It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_RetriesWholeSharedTransfer_WhenClientFails()
    {
        // Arrange
        var alias = new Download
        {
            DownloadClientId = _client.Id,
            ExternalId = "fulfilled",
            Title = "Duplicate",
            Status = DownloadStatus.Downloading
        };
        _context.Downloads.Add(alias);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.DownloadScopes.Add(new() { DownloadId = alias.Id, ContentHierarchyItemId = _node.Id });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _integration.SetupSequence(integration => integration.RemoveDownloadAsync(
            "fulfilled", false, _client.Settings, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("offline")).ReturnsAsync(true);

        // Act
        Assert.ThrowsAsync<InvalidOperationException>(() => _subject.ExecuteAsync(_jobContext));
        var failedFirst = await _downloads.GetByIdAsync(_fulfilled.Id).ConfigureAwait(false);
        var failedSecond = await _downloads.GetByIdAsync(alias.Id).ConfigureAwait(false);
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failedFirst!.Status, Is.EqualTo(DownloadStatus.Downloading));
            Assert.That(failedSecond!.Status, Is.EqualTo(DownloadStatus.Downloading));
            Assert.That((await _downloads.GetByIdAsync(_fulfilled.Id).ConfigureAwait(false))!.Status,
                Is.EqualTo(DownloadStatus.Cancelled));
            Assert.That((await _downloads.GetByIdAsync(alias.Id).ConfigureAwait(false))!.Status,
                Is.EqualTo(DownloadStatus.Cancelled));
            _integration.Verify(integration => integration.RemoveDownloadAsync(
                "fulfilled", false, _client.Settings, It.IsAny<CancellationToken>()), Times.Exactly(2));
        }
    }

    [Test]
    public async Task ExecuteAsync_RechecksAllAliasScopes_WhenAliasBecomesNeededAfterSelection()
    {
        // Arrange
        var alias = new Download
        {
            DownloadClientId = _client.Id,
            ExternalId = "fulfilled",
            Title = "Duplicate",
            Status = DownloadStatus.Downloading
        };
        _context.Downloads.Add(alias);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.DownloadScopes.Add(new() { DownloadId = alias.Id, ContentHierarchyItemId = _node.Id });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _clients.Setup(repository => repository.GetByIdAsync(_client.Id, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                _context.DownloadScopes.Add(new() { DownloadId = alias.Id, ContentHierarchyItemId = _other.Id });
                await _context.SaveChangesAsync().ConfigureAwait(false);
                return _client;
            });

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((await _downloads.GetByIdAsync(_fulfilled.Id).ConfigureAwait(false))!.Status,
                Is.EqualTo(DownloadStatus.Downloading));
            Assert.That((await _downloads.GetByIdAsync(alias.Id).ConfigureAwait(false))!.Status,
                Is.EqualTo(DownloadStatus.Downloading));
            _integration.Verify(integration => integration.RemoveDownloadAsync(
                "fulfilled", false, _client.Settings, It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_RetriesBeforeClientRemoval_WhenAnotherAliasAppearsAfterLocks()
    {
        // Arrange
        var alias = new Download
        {
            DownloadClientId = _client.Id,
            ExternalId = "fulfilled",
            Title = "New alias",
            Status = DownloadStatus.Downloading
        };
        _clients.Setup(repository => repository.GetByIdAsync(_client.Id, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                _context.Downloads.Add(alias);
                await _context.SaveChangesAsync().ConfigureAwait(false);
                _context.DownloadScopes.Add(new() { DownloadId = alias.Id, ContentHierarchyItemId = _node.Id });
                await _context.SaveChangesAsync().ConfigureAwait(false);
                return _client;
            });

        // Act
        var error = Assert.ThrowsAsync<InvalidOperationException>(() => _subject.ExecuteAsync(_jobContext));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(error!.Message, Is.EqualTo("The transfer attempts changed during cancellation."));
            Assert.That((await _downloads.GetByIdAsync(_fulfilled.Id).ConfigureAwait(false))!.Status,
                Is.EqualTo(DownloadStatus.Downloading));
            _integration.Verify(integration => integration.RemoveDownloadAsync(
                "fulfilled", false, _client.Settings, It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_KeepsAllAliasesCancelled_WhenSharedTransferCompletesDuringRemoval()
    {
        // Arrange
        var alias = new Download
        {
            DownloadClientId = _client.Id,
            ExternalId = "fulfilled",
            Title = "Duplicate",
            Status = DownloadStatus.Downloading
        };
        _context.Downloads.Add(alias);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.DownloadScopes.Add(new() { DownloadId = alias.Id, ContentHierarchyItemId = _node.Id });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _integration.Setup(integration => integration.RemoveDownloadAsync(
            "fulfilled", false, _client.Settings, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await _context.Downloads.Where(download => download.ExternalId == "fulfilled")
                    .ExecuteUpdateAsync(setters => setters.SetProperty(download => download.Status,
                        DownloadStatus.Downloaded)).ConfigureAwait(false);
                return true;
            });

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);
        var firstStarted = await _downloads.MarkImportingAsync(
            _fulfilled.Id,
            DateTime.UtcNow,
            null).ConfigureAwait(false);
        var secondStarted = await _downloads.MarkImportingAsync(alias.Id, DateTime.UtcNow, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstStarted, Is.False);
            Assert.That(secondStarted, Is.False);
            Assert.That((await _downloads.GetByIdAsync(_fulfilled.Id).ConfigureAwait(false))!.FailureReason, Is.Null);
            Assert.That((await _downloads.GetByIdAsync(alias.Id).ConfigureAwait(false))!.FailureReason, Is.Null);
            _integration.Verify(integration => integration.RemoveDownloadAsync(
                "fulfilled", false, _client.Settings, It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [TestCase(null, MonitoringMode.Unmonitored, false)]
    [TestCase(null, MonitoringMode.Monitored, true)]
    [TestCase(MonitoringMode.Monitored, MonitoringMode.Monitored, false)]
    [TestCase(MonitoringMode.Monitored, MonitoringMode.Monitored, true)]
    [TestCase(MonitoringMode.Unmonitored, MonitoringMode.Unmonitored, false)]
    [TestCase(MonitoringMode.Unmonitored, MonitoringMode.Unmonitored, true)]
    [TestCase(MonitoringMode.Shotgun, MonitoringMode.Shotgun, false)]
    [TestCase(MonitoringMode.Shotgun, MonitoringMode.Shotgun, true)]
    public async Task ExecuteAsync_CancelsOnceAndKeepsImportedAndHistory_WhenScopesFulfilledRegardlessOfMonitoring(
        MonitoringMode? monitoring, MonitoringMode effectiveMonitoring, bool manual)
    {
        // Arrange
        _node.Monitoring = monitoring;
        _node.EffectiveMonitoring = effectiveMonitoring;
        _fulfilled.CreatedByUserId = manual ? Guid.NewGuid() : null;
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);
        var fulfilledDownload = await _downloads.GetByIdAsync(_fulfilled.Id).ConfigureAwait(false);
        var importedDownload = await _downloads.GetByIdAsync(_imported.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(fulfilledDownload!.Status, Is.EqualTo(DownloadStatus.Cancelled));
            Assert.That(fulfilledDownload.StatusReason, Is.EqualTo("Content fulfilled"));
            Assert.That(fulfilledDownload.FailureReason, Is.Null);
            Assert.That(fulfilledDownload.FailedAt, Is.Null);
            Assert.That(importedDownload!.Status, Is.EqualTo(DownloadStatus.Imported));
            Assert.That(importedDownload.ClientRemovedAt, Is.Null);
            Assert.That(importedDownload.HeldByPolicy, Is.True);
            Assert.That(await _context.MediaFiles.AsNoTracking().Select(file => file.FilePath).SingleAsync()
                .ConfigureAwait(false), Is.EqualTo("/library/imported.mkv"));
            Assert.That(await _context.Downloads.CountAsync().ConfigureAwait(false), Is.EqualTo(2));
            Assert.That(await _context.AuditRecords.CountAsync().ConfigureAwait(false), Is.EqualTo(1));
            _integration.Verify(integration => integration.RemoveDownloadAsync(
                "fulfilled", false, _client.Settings, It.IsAny<CancellationToken>()), Times.Once);
            _integration.Verify(integration => integration.RemoveDownloadAsync(
                "imported", It.IsAny<bool>(), It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [TestCase(DownloadStatus.Grabbed)]
    [TestCase(DownloadStatus.Queued)]
    [TestCase(DownloadStatus.Paused)]
    [TestCase(DownloadStatus.Stalled)]
    [TestCase(DownloadStatus.Downloaded)]
    public async Task ExecuteAsync_CancelsClientTransfer_WhenFulfilledDownloadHasNotStartedImport(DownloadStatus status)
    {
        // Arrange
        _fulfilled.Status = status;
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((await _downloads.GetByIdAsync(_fulfilled.Id).ConfigureAwait(false))!.Status,
                Is.EqualTo(DownloadStatus.Cancelled));
            _integration.Verify(integration => integration.RemoveDownloadAsync(
                "fulfilled", false, _client.Settings, It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_KeepsSharedPack_WhenAnAttachedScopeHasNoContentState()
    {
        // Arrange
        _context.DownloadScopes.Add(new() { DownloadId = _fulfilled.Id, ContentHierarchyItemId = _other.Id });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        await _context.ContentItems.Where(item => item.Id == _other.ContentItem!.Id).ExecuteDeleteAsync()
            .ConfigureAwait(false);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        Assert.That((await _downloads.GetByIdAsync(_fulfilled.Id).ConfigureAwait(false))!.Status,
            Is.EqualTo(DownloadStatus.Downloading));
    }

    [Test]
    public async Task ExecuteAsync_PreservesNewAcquisition_WhenAnOldImportNotificationArrivesAfterContentIsLost()
    {
        // Arrange
        _node.ContentItem!.Fulfillment = FulfillmentStatus.Missing;
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        Assert.That((await _downloads.GetByIdAsync(_fulfilled.Id).ConfigureAwait(false))!.Status,
            Is.EqualTo(DownloadStatus.Downloading));
    }

    [TestCase(FulfillmentStatus.Missing)]
    [TestCase(FulfillmentStatus.Partial)]
    [TestCase(FulfillmentStatus.Upgradable)]
    public async Task ExecuteAsync_PreservesSharedPack_WhenAnotherUnmonitoredScopeStillNeedsContent(
        FulfillmentStatus fulfillment)
    {
        // Arrange
        _other.ContentItem!.Fulfillment = fulfillment;
        _context.DownloadScopes.Add(new() { DownloadId = _fulfilled.Id, ContentHierarchyItemId = _other.Id });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((await _downloads.GetByIdAsync(_fulfilled.Id).ConfigureAwait(false))!.Status,
                Is.EqualTo(DownloadStatus.Downloading));
            _integration.Verify(integration => integration.RemoveDownloadAsync(
                It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [TestCase(true, FulfillmentStatus.Fulfilled, DownloadStatus.Cancelled)]
    [TestCase(false, FulfillmentStatus.Fulfilled, DownloadStatus.Cancelled)]
    [TestCase(true, FulfillmentStatus.Missing, DownloadStatus.Downloading)]
    [TestCase(false, FulfillmentStatus.Missing, DownloadStatus.Downloading)]
    [TestCase(true, FulfillmentStatus.Upgradable, DownloadStatus.Downloading)]
    [TestCase(false, FulfillmentStatus.Partial, DownloadStatus.Downloading)]
    public async Task ExecuteAsync_ResolvesFolderScopesThroughDescendants_WhenPackIncludesParentHierarchy(
        bool includeEpisodeScope, FulfillmentStatus otherEpisodeFulfillment, DownloadStatus expectedStatus)
    {
        // Arrange
        var show = new ContentHierarchyItem
        {
            MetadataContainer = new(),
            ContentType = "type-a",
            Level = "show",
            Slug = "show",
            Name = "Show"
        };
        _context.ContentHierarchyItems.Add(show);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var season = new ContentHierarchyItem
        {
            MetadataContainer = new(),
            ContentType = "type-a",
            Level = "season",
            Slug = "season",
            Name = "Season",
            ParentId = show.Id
        };
        _context.ContentHierarchyItems.Add(season);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _node.ParentId = season.Id;
        _other.ParentId = season.Id;
        _other.ContentItem!.Fulfillment = otherEpisodeFulfillment;
        _context.DownloadScopes.Add(new() { DownloadId = _fulfilled.Id, ContentHierarchyItemId = show.Id });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        if (!includeEpisodeScope)
        {
            await _context.DownloadScopes.Where(scope => scope.DownloadId == _fulfilled.Id &&
                scope.ContentHierarchyItemId == _node.Id).ExecuteDeleteAsync().ConfigureAwait(false);
        }

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((await _downloads.GetByIdAsync(_fulfilled.Id).ConfigureAwait(false))!.Status,
                Is.EqualTo(expectedStatus));
            Assert.That(await _context.ContentItems.CountAsync(item => item.ContentHierarchyItemId == show.Id ||
                item.ContentHierarchyItemId == season.Id).ConfigureAwait(false), Is.Zero);
        }
    }

    [TestCase(FulfillmentStatus.Missing, DownloadStatus.Downloading)]
    [TestCase(FulfillmentStatus.Partial, DownloadStatus.Downloading)]
    [TestCase(FulfillmentStatus.Upgradable, DownloadStatus.Downloading)]
    [TestCase(FulfillmentStatus.Fulfilled, DownloadStatus.Cancelled)]
    public async Task ExecuteAsync_ChecksParentOwnedContent_WhenChildrenAreFulfilled(
        FulfillmentStatus parentFulfillment, DownloadStatus expectedStatus)
    {
        // Arrange
        var parent = new ContentHierarchyItem
        {
            MetadataContainer = new(),
            ContentType = "type-a",
            Level = "parent",
            Slug = "parent",
            Name = "Parent",
            ContentItem = new() { Fulfillment = parentFulfillment }
        };
        _context.ContentHierarchyItems.Add(parent);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _node.ParentId = parent.Id;
        _context.DownloadScopes.Add(new() { DownloadId = _fulfilled.Id, ContentHierarchyItemId = parent.Id });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        Assert.That((await _downloads.GetByIdAsync(_fulfilled.Id).ConfigureAwait(false))!.Status,
            Is.EqualTo(expectedStatus));
    }

    [Test]
    public async Task ExecuteAsync_ResolvesDescendantOverlap_WhenImportedDownloadKeepsOnlyItsParentScope()
    {
        // Arrange
        var season = new ContentHierarchyItem
        {
            MetadataContainer = new(),
            ContentType = "type-a",
            Level = "season",
            Slug = "season",
            Name = "Season"
        };
        _context.ContentHierarchyItems.Add(season);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _node.ParentId = season.Id;
        await _context.SaveChangesAsync().ConfigureAwait(false);
        await _context.DownloadScopes.Where(scope => scope.DownloadId == _imported.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(scope => scope.ContentHierarchyItemId, season.Id))
            .ConfigureAwait(false);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        Assert.That((await _downloads.GetByIdAsync(_fulfilled.Id).ConfigureAwait(false))!.Status,
            Is.EqualTo(DownloadStatus.Cancelled));
    }

    [TestCase(FulfillmentStatus.Fulfilled)]
    [TestCase(FulfillmentStatus.FulfilledExternally)]
    public async Task ExecuteAsync_CancelsSharedPack_WhenEveryAttachedScopeIsFulfilled(FulfillmentStatus fulfillment)
    {
        // Arrange
        _other.ContentItem!.Fulfillment = fulfillment;
        _context.DownloadScopes.Add(new() { DownloadId = _fulfilled.Id, ContentHierarchyItemId = _other.Id });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        Assert.That((await _downloads.GetByIdAsync(_fulfilled.Id).ConfigureAwait(false))!.Status,
            Is.EqualTo(DownloadStatus.Cancelled));
    }

    [TestCase(DownloadStatus.Downloaded)]
    [TestCase(DownloadStatus.Importing)]
    [TestCase(DownloadStatus.Failed)]
    public async Task ExecuteAsync_KeepsFulfilledDownload_WhenImportDidNotSucceed(DownloadStatus status)
    {
        // Arrange
        _imported.Status = status;
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        Assert.That((await _downloads.GetByIdAsync(_fulfilled.Id).ConfigureAwait(false))!.Status,
            Is.EqualTo(DownloadStatus.Downloading));
    }

    [TestCase(DownloadStatus.Imported)]
    [TestCase(DownloadStatus.Importing)]
    [TestCase(DownloadStatus.Failed)]
    public async Task ExecuteAsync_PreservesProtectedAttempt_WhenFulfilledDownloadAlreadyAdvanced(DownloadStatus status)
    {
        // Arrange
        _fulfilled.Status = status;
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        Assert.That((await _downloads.GetByIdAsync(_fulfilled.Id).ConfigureAwait(false))!.Status, Is.EqualTo(status));
    }

    [Test]
    public async Task ExecuteAsync_KeepsTransfer_WhenClientIdentityIsSharedWithImportedDownload()
    {
        // Arrange
        _fulfilled.ExternalId = "imported";
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        Assert.That((await _downloads.GetByIdAsync(_fulfilled.Id).ConfigureAwait(false))!.Status,
            Is.EqualTo(DownloadStatus.Downloading));
    }

    [Test]
    public async Task ExecuteAsync_KeepsTransfer_WhenTitleMatchesButScopesDoNotOverlap()
    {
        // Arrange
        _fulfilled.Title = "Winning release";
        await _context.SaveChangesAsync().ConfigureAwait(false);
        await _context.DownloadScopes.Where(scope => scope.DownloadId == _fulfilled.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(scope => scope.ContentHierarchyItemId, _other.Id))
            .ConfigureAwait(false);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        Assert.That((await _downloads.GetByIdAsync(_fulfilled.Id).ConfigureAwait(false))!.Status,
            Is.EqualTo(DownloadStatus.Downloading));
    }

    [Test]
    public async Task ExecuteAsync_RechecksFulfillment_WhenItChangesAfterCandidateSelection()
    {
        // Arrange
        _clients.Setup(repository => repository.GetByIdAsync(_client.Id, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await _context.ContentItems.Where(item => item.Id == _node.ContentItem!.Id)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Fulfillment,
                        FulfillmentStatus.Upgradable)).ConfigureAwait(false);
                return _client;
            });

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((await _downloads.GetByIdAsync(_fulfilled.Id).ConfigureAwait(false))!.Status,
                Is.EqualTo(DownloadStatus.Downloading));
            _integration.Verify(integration => integration.RemoveDownloadAsync(
                It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_RechecksScope_WhenNeededMemberIsAttachedAfterCandidateSelection()
    {
        // Arrange
        _clients.Setup(repository => repository.GetByIdAsync(_client.Id, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                _context.DownloadScopes.Add(new() { DownloadId = _fulfilled.Id, ContentHierarchyItemId = _other.Id });
                await _context.SaveChangesAsync().ConfigureAwait(false);
                return _client;
            });

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        Assert.That((await _downloads.GetByIdAsync(_fulfilled.Id).ConfigureAwait(false))!.Status,
            Is.EqualTo(DownloadStatus.Downloading));
    }

    [Test]
    public async Task ExecuteAsync_LeavesRetryableAttempt_WhenClientCancellationFails()
    {
        // Arrange
        _integration.SetupSequence(integration => integration.RemoveDownloadAsync(
                "fulfilled", false, _client.Settings, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("offline")).ReturnsAsync(true);

        // Act
        var failure = Assert.ThrowsAsync<InvalidOperationException>(() => _subject.ExecuteAsync(_jobContext));
        var unchanged = await _downloads.GetByIdAsync(_fulfilled.Id).ConfigureAwait(false);
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure!.Message, Is.EqualTo("The client could not cancel 1 fulfilled download(s)."));
            Assert.That(unchanged!.Status, Is.EqualTo(DownloadStatus.Downloading));
            Assert.That(unchanged.FailureReason, Is.Null);
            Assert.That((await _downloads.GetByIdAsync(_fulfilled.Id).ConfigureAwait(false))!.StatusReason,
                Is.EqualTo("Content fulfilled"));
        }
    }

    [Test]
    public async Task ExecuteAsync_CancelsWithoutImportFailure_WhenFulfilledDownloadCompletesDuringClientRemoval()
    {
        // Arrange
        _integration.Setup(integration => integration.RemoveDownloadAsync(
                "fulfilled", false, _client.Settings, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await _context.Downloads.Where(download => download.Id == _fulfilled.Id)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(download => download.Status,
                        DownloadStatus.Downloaded)).ConfigureAwait(false);
                return true;
            });

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);
        var importing = await _downloads.MarkImportingAsync(_fulfilled.Id, DateTime.UtcNow, null).ConfigureAwait(false);
        var fulfilledDownload = await _downloads.GetByIdAsync(_fulfilled.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(importing, Is.False);
            Assert.That(fulfilledDownload!.Status, Is.EqualTo(DownloadStatus.Cancelled));
            Assert.That(fulfilledDownload.FailureReason, Is.Null);
            Assert.That((await _downloads.GetByIdAsync(_imported.Id).ConfigureAwait(false))!.Status,
                Is.EqualTo(DownloadStatus.Imported));
        }
    }

    [Test]
    public async Task MarkImportedAsync_PublishesCancellationTriggerOnlyAfterCommit_WhenImportSucceeds()
    {
        // Arrange
        var trigger = DefaultJobTriggerCatalog.EventTriggers.Single(item =>
            item.JobType == MagpieJobs.Types.DownloadFulfilledCancellation);
        _imported.Status = DownloadStatus.Importing;
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _transactions.ExecuteWithTransactionAsync(async token =>
        {
            await _downloads.MarkImportedAsync(_imported.Id, _node.Id, 1, DateTime.UtcNow, DateTime.UtcNow, null, token)
                .ConfigureAwait(false);
            _events.Verify(events => events.PublishAsync(It.Is<MagpieEvent>(item =>
                item.Name == MagpieEvents.Names.DownloadImported), It.IsAny<CancellationToken>()), Times.Never);
        }, default).ConfigureAwait(false);
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(trigger.Enabled, Is.True);
            Assert.That(trigger.EventFilter.EventName!.Value, Is.EqualTo("download.imported"));
            Assert.That(trigger.Parameters!["downloadId"], Is.EqualTo("${event.download.id}"));
            Assert.That(DownloadFulfilledCancellationJob.Spec.FailurePolicy, Is.EqualTo(JobFailurePolicy.Retry));
            _events.Verify(events => events.PublishAsync(It.Is<MagpieEvent>(item =>
                item.Name == MagpieEvents.Names.DownloadImported &&
                item.Context[MagpieEvents.Keys.DownloadId] == _imported.Id.ToString("D")),
                It.IsAny<CancellationToken>()), Times.Once);
            Assert.That((await _downloads.GetByIdAsync(_fulfilled.Id).ConfigureAwait(false))!.Status,
                Is.EqualTo(DownloadStatus.Cancelled));
        }
    }
}
