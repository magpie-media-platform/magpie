using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Releases;
using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
using Magpie.Core;
using Magpie.Core.Jobs.Downloads;
using Magpie.Core.Observability;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Integrations;
using Magpie.Models.Jobs;
using Magpie.Sdk.Integrations;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs.Fulfillment;

[TestFixture]
[NonParallelizable]
public sealed class DownloadRefreshJobAcquisitionTests
{
    private static readonly Guid FailingId = new("00000000-0000-0000-0000-000000000001");
    private static readonly Guid LeadingId = new("00000000-0000-0000-0000-000000000002");
    private static readonly Guid QueuedId = new("00000000-0000-0000-0000-000000000003");

    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private List<string> _commands = null!;
    private List<MagpieEvent> _events = null!;
    private Mock<IEventBus> _eventBus = null!;
    private Mock<IDownloadClientIntegration> _integration = null!;
    private Mock<IDownloadClientRepository> _clients = null!;
    private DownloadClient _client = null!;
    private Dictionary<string, DownloadInfo> _statuses = null!;
    private DownloadRefreshJob _subject = null!;
    private JobContext _jobContext = null!;
    private Guid _sharedItemId;
    private Guid _singleItemId;

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
        _commands = [];
        _events = [];
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        _template.BackupDatabase(connection);
        _context = new MagpieDbContext(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite(connection, contextOwnsConnection: true)
            .LogTo(_commands.Add, [RelationalEventId.CommandExecuted, RelationalEventId.TransactionStarted])
            .Options);
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero));
        var root = new RootFolder { Name = "Library", Path = "/library", ContentType = "movie" };
        var sharedMetadata = new MetadataContainer();
        var singleMetadata = new MetadataContainer();
        var client = new DownloadClient { Name = "Client", ProviderType = "fake", Enabled = true };
        _context.AddRange(root, sharedMetadata, singleMetadata, client);
        _client = client;
        _context.SaveChanges();
        var shared = new ContentHierarchyItem
        {
            Name = "Shared",
            Slug = "shared",
            ContentType = "movie",
            Level = "movie",
            MetadataContainerId = sharedMetadata.Id,
            RootFolderId = root.Id,
            EffectiveRootFolderId = root.Id
        };
        var single = new ContentHierarchyItem
        {
            Name = "Single",
            Slug = "single",
            ContentType = "movie",
            Level = "movie",
            MetadataContainerId = singleMetadata.Id,
            RootFolderId = root.Id,
            EffectiveRootFolderId = root.Id
        };
        _context.AddRange(shared, single);
        _context.SaveChanges();
        var sharedItem = new ContentItem
        {
            ContentHierarchyItemId = shared.Id,
            Fulfillment = FulfillmentStatus.Missing,
            ActiveAcquisitionStatus = DownloadStatus.Queued
        };
        var singleItem = new ContentItem
        {
            ContentHierarchyItemId = single.Id,
            Fulfillment = FulfillmentStatus.Missing,
            ActiveAcquisitionStatus = DownloadStatus.Downloading
        };
        _context.AddRange(
            sharedItem,
            singleItem,
            new Download
            {
                Id = FailingId,
                Title = "Failing",
                ExternalId = "failing",
                DownloadClientId = client.Id,
                Status = DownloadStatus.Downloading,
                Progress = 10m,
                LastProgressAt = new DateTime(2026, 9, 23, 11, 50, 0, DateTimeKind.Utc),
                CreatedAt = new DateTime(2026, 9, 23, 11, 0, 0, DateTimeKind.Utc),
                ModifiedAt = new DateTime(2026, 9, 23, 11, 50, 0, DateTimeKind.Utc)
            },
            new Download
            {
                Id = LeadingId,
                Title = "Leading",
                ExternalId = "leading",
                DownloadClientId = client.Id,
                Status = DownloadStatus.Downloading,
                Progress = 40m,
                LastProgressAt = new DateTime(2026, 9, 23, 11, 50, 0, DateTimeKind.Utc),
                CreatedAt = new DateTime(2026, 9, 23, 11, 0, 0, DateTimeKind.Utc),
                ModifiedAt = new DateTime(2026, 9, 23, 11, 50, 0, DateTimeKind.Utc)
            },
            new Download
            {
                Id = QueuedId,
                Title = "Queued",
                ExternalId = "queued",
                DownloadClientId = client.Id,
                Status = DownloadStatus.Queued,
                Progress = 0m,
                LastProgressAt = new DateTime(2026, 9, 23, 11, 55, 0, DateTimeKind.Utc),
                CreatedAt = new DateTime(2026, 9, 23, 11, 0, 0, DateTimeKind.Utc),
                ModifiedAt = new DateTime(2026, 9, 23, 11, 55, 0, DateTimeKind.Utc)
            });
        _context.SaveChanges();
        _context.AddRange(
            new DownloadScope { DownloadId = FailingId, ContentHierarchyItemId = single.Id },
            new DownloadScope { DownloadId = LeadingId, ContentHierarchyItemId = shared.Id },
            new DownloadScope { DownloadId = QueuedId, ContentHierarchyItemId = shared.Id });
        _context.SaveChanges();
        _context.ChangeTracker.Clear();
        _sharedItemId = sharedItem.Id;
        _singleItemId = singleItem.Id;

        _statuses = new Dictionary<string, DownloadInfo>(StringComparer.Ordinal)
        {
            ["failing"] = new() { ExternalId = "failing", Status = DownloadStatus.Downloading, ProgressPercent = 10m },
            ["leading"] = new() { ExternalId = "leading", Status = DownloadStatus.Downloading, ProgressPercent = 40m },
            ["queued"] = new() { ExternalId = "queued", Status = DownloadStatus.Queued, ProgressPercent = 0m },
        };
        _integration = new Mock<IDownloadClientIntegration>();
        _integration.SetupGet(i => i.ProviderType).Returns("fake");
        _integration
            .Setup(i => i.GetDownloadsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new BatchDownloadStatusResult(
                _statuses,
                new Dictionary<string, Exception>(StringComparer.Ordinal)));
        _clients = new Mock<IDownloadClientRepository>();
        _clients.Setup(r => r.ListAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([client]);
        _eventBus = new Mock<IEventBus>();
        _eventBus
            .Setup(e => e.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()))
            .Callback<MagpieEvent, CancellationToken>((published, _) => _events.Add(published))
            .Returns(Task.CompletedTask);
        var transactions = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);
        var audits = new AuditRecordRepository(_context, _eventBus.Object,
            NullLogger<AuditRecordRepository>.Instance, transactions, Mock.Of<IJobExecutionScopeFactory>(), time);
        var downloads = new DownloadRepository(_context, audits, transactions, _eventBus.Object,
            NullLogger<DownloadRepository>.Instance, Mock.Of<IDataProtectionService>(), time);
        var service = new DownloadService([_integration.Object], _clients.Object, downloads,
            Mock.Of<IJobManager>(), transactions, time, NullLogger<DownloadService>.Instance);
        _subject = new DownloadRefreshJob([_integration.Object], _clients.Object, downloads, Mock.Of<IJobManager>(),
            NullLogger<DownloadRefreshJob>.Instance, service, time, Mock.Of<IProgressReporter>());
        _jobContext = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: null,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>(),
            Event: null);
        _commands.Clear();
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [Test]
    public async Task ExecuteAsync_SetsLatestInFlightStatus_WhenAProgressTickChangesTheLatestDownload()
    {
        // Arrange
        _statuses["leading"] = new()
        {
            ExternalId = "leading",
            Status = DownloadStatus.Downloading,
            ProgressPercent = 43m,
        };

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                _context.ContentItems.AsNoTracking().Single(c => c.Id == _sharedItemId).ActiveAcquisitionStatus,
                Is.EqualTo(DownloadStatus.Downloading));
            Assert.That(
                _context.ContentItems.AsNoTracking().Single(c => c.Id == _singleItemId).ActiveAcquisitionStatus,
                Is.EqualTo(DownloadStatus.Downloading));
        }
    }

    [Test]
    public async Task ExecuteAsync_RecomputesOnceAndWritesOnlyChangedItems_WhenProgressTicksShareACycle()
    {
        // Arrange
        _statuses["leading"] = new()
        {
            ExternalId = "leading",
            Status = DownloadStatus.Downloading,
            ProgressPercent = 43m,
        };
        _statuses["failing"] = new()
        {
            ExternalId = "failing",
            Status = DownloadStatus.Downloading,
            ProgressPercent = 12m,
        };

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_commands.Count(c => c.Contains("\"download_downloadScope\"", StringComparison.Ordinal)),
                Is.EqualTo(2));
            Assert.That(_commands.Count(c => c.Contains("UPDATE \"content_item\"", StringComparison.Ordinal)),
                Is.EqualTo(1));
            Assert.That(_commands.Count(c => c.Contains("UPDATE \"download_download\"", StringComparison.Ordinal)),
                Is.EqualTo(2));
            Assert.That(_commands.Count(c => c.Contains("Began transaction", StringComparison.Ordinal)), Is.Zero);
        }
    }

    [Test]
    public async Task ExecuteAsync_WritesNothing_WhenNoDownloadChanged()
    {
        // Arrange

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_commands.Count(c => c.Contains("UPDATE ", StringComparison.Ordinal)), Is.Zero);
            Assert.That(_commands.Count(c => c.Contains("\"download_downloadScope\"", StringComparison.Ordinal)),
                Is.Zero);
            Assert.That(_events, Is.Empty);
        }
    }

    [Test]
    public async Task ExecuteAsync_PublishesContentImpact_WhenARefreshUpdatesAndFailsDownloads()
    {
        // Arrange
        _statuses["leading"] = new()
        {
            ExternalId = "leading",
            Status = DownloadStatus.Downloading,
            ProgressPercent = 43m,
        };
        _statuses["failing"] = new()
        {
            ExternalId = "failing",
            Status = DownloadStatus.Failed,
            ProgressPercent = 10m,
        };

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        var updated = _events.Single(e => e.Name == "download.updated");
        var failed = _events.Single(e => e.Name == "download.failed");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated.Context["download.id"], Is.EqualTo("00000000-0000-0000-0000-000000000002"));
            Assert.That(updated.Context["content.itemId"], Is.EqualTo(_sharedItemId.ToString()));
            Assert.That(failed.Context["download.id"], Is.EqualTo("00000000-0000-0000-0000-000000000001"));
            Assert.That(failed.Context["content.itemId"], Is.EqualTo(_singleItemId.ToString()));
        }
    }

    [Test]
    public void ExecuteAsync_ClearsAcquisitionStatus_WhenADownloadFailsAndTheCycleIsCanceled()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        _statuses["failing"] = new()
        {
            ExternalId = "failing",
            Status = DownloadStatus.Failed,
            ProgressPercent = 10m,
        };
        _eventBus
            .Setup(e => e.PublishAsync(
                It.Is<MagpieEvent>(published => published.Name == "download.failed"),
                It.IsAny<CancellationToken>()))
            .Callback(cancellation.Cancel)
            .Returns(Task.CompletedTask);

        // Act
        Assert.CatchAsync<OperationCanceledException>(() => _subject.ExecuteAsync(_jobContext, cancellation.Token));

        // Assert
        Assert.That(
            _context.ContentItems.AsNoTracking().Single(c => c.Id == _singleItemId).ActiveAcquisitionStatus,
            Is.Null);
    }

    [Test]
    public async Task ExecuteAsync_AppliesOtherDownloads_WhenOneDownloadFailsToUpdate()
    {
        // Arrange
        await _context.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER reject_failing BEFORE UPDATE ON download_download WHEN OLD.ExternalId = 'failing' " +
            "BEGIN SELECT RAISE(ABORT, 'rejected'); END;").ConfigureAwait(false);
        _statuses["leading"] = new()
        {
            ExternalId = "leading",
            Status = DownloadStatus.Downloading,
            ProgressPercent = 43m,
        };
        _statuses["failing"] = new()
        {
            ExternalId = "failing",
            Status = DownloadStatus.Downloading,
            ProgressPercent = 12m,
        };

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_context.Downloads.AsNoTracking().Single(d => d.Id == FailingId).Progress, Is.EqualTo(10m));
            Assert.That(_context.Downloads.AsNoTracking().Single(d => d.Id == LeadingId).Progress, Is.EqualTo(43m));
            Assert.That(_events.Single().Context["download.id"], Is.EqualTo("00000000-0000-0000-0000-000000000002"));
            Assert.That(
                _context.ContentItems.AsNoTracking().Single(c => c.Id == _sharedItemId).ActiveAcquisitionStatus,
                Is.EqualTo(DownloadStatus.Downloading));
        }
    }

    [Test]
    public async Task ExecuteAsync_SetsOtherContentItems_WhenOneContentItemFailsToUpdate()
    {
        // Arrange
        await _context.ContentItems
            .Where(c => c.Id == _singleItemId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ActiveAcquisitionStatus, (DownloadStatus?)null))
            .ConfigureAwait(false);
        await _context.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER reject_shared BEFORE UPDATE ON content_item WHEN OLD.ActiveAcquisitionStatus IS NOT NULL " +
            "BEGIN SELECT RAISE(ABORT, 'rejected'); END;").ConfigureAwait(false);
        _statuses["leading"] = new()
        {
            ExternalId = "leading",
            Status = DownloadStatus.Downloading,
            ProgressPercent = 43m,
        };
        _statuses["failing"] = new()
        {
            ExternalId = "failing",
            Status = DownloadStatus.Downloading,
            ProgressPercent = 12m,
        };

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                _context.ContentItems.AsNoTracking().Single(c => c.Id == _sharedItemId).ActiveAcquisitionStatus,
                Is.EqualTo(DownloadStatus.Queued));
            Assert.That(
                _context.ContentItems.AsNoTracking().Single(c => c.Id == _singleItemId).ActiveAcquisitionStatus,
                Is.EqualTo(DownloadStatus.Downloading));
            Assert.That(_events, Has.Count.EqualTo(2));
        }
    }

    [Test]
    public async Task ExecuteAsync_CountsEachTransitionOnce_WhenACycleMixesProgressAndFailure()
    {
        // Arrange
        var transitions = new List<string>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == DownloadTelemetry.Name &&
                    instrument.Name is "magpie.downloads.transitions" or "magpie.downloads.failures")
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
            transitions.Add($"{instrument.Name}:{string.Join(",", tags.ToArray().Select(t => t.Value))}"));
        listener.Start();
        _statuses["leading"] = new()
        {
            ExternalId = "leading",
            Status = DownloadStatus.Downloading,
            ProgressPercent = 43m,
        };
        _statuses["failing"] = new()
        {
            ExternalId = "failing",
            Status = DownloadStatus.Failed,
            ProgressPercent = 10m,
        };

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        Assert.That(transitions, Is.EqualTo(
        [
            "magpie.downloads.transitions:Downloading,Faulted",
            "magpie.downloads.failures:DownloadErrored",
            "magpie.downloads.transitions:Downloading,Downloading",
        ]));
    }

    [Test]
    public async Task ExecuteAsync_AppliesTheTransitionInline_WhenTheStatusChangesAfterTheJobReadsIt()
    {
        // Arrange
        var transitions = new List<string>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == DownloadTelemetry.Name &&
                    instrument.Name is "magpie.downloads.transitions" or "magpie.downloads.failures")
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
            transitions.Add($"{instrument.Name}:{string.Join(",", tags.ToArray().Select(t => t.Value))}"));
        listener.Start();
        _statuses["leading"] = new()
        {
            ExternalId = "leading",
            Status = DownloadStatus.Downloading,
            ProgressPercent = 43m,
        };
        _integration
            .Setup(i => i.GetDownloadsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => _context.Downloads
                .Where(d => d.Id == LeadingId)
                .ExecuteUpdate(s => s.SetProperty(d => d.Status, DownloadStatus.Stalled)))
            .ReturnsAsync(() => new BatchDownloadStatusResult(
                _statuses,
                new Dictionary<string, Exception>(StringComparer.Ordinal)));

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        var updated = _events.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(transitions, Is.EqualTo(["magpie.downloads.transitions:Stalled,Downloading"]));
            Assert.That(_commands.Count(c => c.Contains("Began transaction", StringComparison.Ordinal)),
                Is.EqualTo(1));
            Assert.That(updated.Name, Is.EqualTo("download.updated"));
            Assert.That(updated.Context["content.itemId"], Is.EqualTo(_sharedItemId.ToString()));
            Assert.That(
                _context.Downloads.AsNoTracking().Single(d => d.Id == LeadingId).Status,
                Is.EqualTo(DownloadStatus.Downloading));
            Assert.That(
                _context.ContentItems.AsNoTracking().Single(c => c.Id == _sharedItemId).ActiveAcquisitionStatus,
                Is.EqualTo(DownloadStatus.Downloading));
        }
    }

    [Test]
    public async Task ExecuteAsync_CompletesDeferredDownloads_WhenTheRefreshIsCanceledAfterADeferral()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await _context.ContentItems
            .Where(c => c.Id == _singleItemId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ActiveAcquisitionStatus, (DownloadStatus?)null))
            .ConfigureAwait(false);
        _statuses["failing"] = new()
        {
            ExternalId = "failing",
            Status = DownloadStatus.Downloading,
            ProgressPercent = 12m,
        };
        _statuses["leading"] = new()
        {
            ExternalId = "leading",
            Status = DownloadStatus.Failed,
            ProgressPercent = 40m,
        };
        _eventBus
            .Setup(e => e.PublishAsync(
                It.Is<MagpieEvent>(published => published.Name == "download.failed"),
                It.IsAny<CancellationToken>()))
            .Callback(cancellation.Cancel)
            .Returns(Task.CompletedTask);

        // Act
        Assert.CatchAsync<OperationCanceledException>(() => _subject.ExecuteAsync(_jobContext, cancellation.Token));

        // Assert
        var updated = _events.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated.Name, Is.EqualTo("download.updated"));
            Assert.That(updated.Context["download.id"], Is.EqualTo("00000000-0000-0000-0000-000000000001"));
            Assert.That(updated.Context["content.itemId"], Is.EqualTo(_singleItemId.ToString()));
            Assert.That(
                _context.ContentItems.AsNoTracking().Single(c => c.Id == _singleItemId).ActiveAcquisitionStatus,
                Is.EqualTo(DownloadStatus.Downloading));
        }
    }

    [Test]
    public async Task ExecuteAsync_CompletesEachClientBeforeTheNext_WhenTwoClientsRefresh()
    {
        // Arrange
        var second = new DownloadClient { Name = "Second", ProviderType = "fake", Enabled = true };
        _context.Add(second);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Add(new Download
        {
            Title = "Late",
            ExternalId = "late",
            DownloadClientId = second.Id,
            Status = DownloadStatus.Downloading,
            CreatedAt = new DateTime(2026, 9, 23, 11, 0, 0, DateTimeKind.Utc)
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();
        _clients.Setup(r => r.ListAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([_client, second]);
        var eventsAtLookup = new List<int>();
        _integration
            .Setup(i => i.GetDownloadsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => eventsAtLookup.Add(_events.Count))
            .ReturnsAsync(() => new BatchDownloadStatusResult(
                _statuses,
                new Dictionary<string, Exception>(StringComparer.Ordinal)));
        _statuses["leading"] = new()
        {
            ExternalId = "leading",
            Status = DownloadStatus.Downloading,
            ProgressPercent = 43m,
        };

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        Assert.That(eventsAtLookup, Is.EqualTo([0, 1]));
    }
}
