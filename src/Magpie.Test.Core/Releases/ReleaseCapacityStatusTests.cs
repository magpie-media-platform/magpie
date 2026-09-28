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
using Magpie.Core.Jobs.Fulfillment;
using Magpie.Core.Jobs.Runtime;
using Magpie.Core.Releases;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Data.SQLite;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Integrations;
using Magpie.Models.Paging;
using Magpie.Models.Releases;
using Magpie.Models.Storage;
using Magpie.Sdk.Integrations;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Releases;

[TestFixture]
public sealed class ReleaseCapacityStatusTests
{
    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private IReleaseService _subject = null!;
    private IJobExecutionRepository _executions = null!;
    private IDownloadRepository _downloads = null!;
    private Mock<IFileSystem> _files = null!;
    private Mock<IDownloadService> _client = null!;
    private Mock<IReleaseRepository> _releases = null!;
    private FakeTimeProvider _time = null!;
    private Release _release = null!;
    private ContentHierarchyItem _node = null!;

    [OneTimeSetUp]
    public void CreateTemplate()
    {
        _template = new SqliteConnection("Data Source=:memory:");
        _template.Open();
        var options = new DbContextOptionsBuilder<MagpieDbContext>();
        MagpieDbContextFactory.ConfigureOptions(options, "Data Source=:memory:", [], []);
        using var context = new MagpieDbContext(options.UseSqlite(_template).Options);
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
        var options = new DbContextOptionsBuilder<MagpieDbContext>();
        MagpieDbContextFactory.ConfigureOptions(options, "Data Source=:memory:", [], []);
        _context = new MagpieDbContext(options.UseSqlite(connection, contextOwnsConnection: true).Options);
        _time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero));
        var transactions = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);
        var events = Mock.Of<IEventBus>();
        var scopes = Mock.Of<IJobExecutionScopeFactory>();
        var audits = new AuditRecordRepository(_context, events, NullLogger<AuditRecordRepository>.Instance,
            transactions, scopes, _time);
        _executions = new JobExecutionRepository(
            _context,
            audits,
            transactions,
            events,
            NullLogger<JobExecutionRepository>.Instance,
            _time,
            new JobRegistry([], NullLogger<JobRegistry>.Instance));
        _downloads = new DownloadRepository(_context, audits, transactions, events,
            NullLogger<DownloadRepository>.Instance, Mock.Of<IDataProtectionService>(), _time);
        var downloadScopes = new DownloadScopeRepository(_context, transactions, audits,
            NullLogger<DownloadScopeRepository>.Instance, _time);
        var registry = new Mock<IJobRegistry>();
        registry.Setup(value => value.ListJobSpecs()).Returns([ReleaseDownloadItemJob.Spec]);
        var jobs = new JobManager(Mock.Of<IJobRuntime>(), registry.Object, _executions,
            Mock.Of<IScheduleTriggerRepository>(), Mock.Of<IEventTriggerRepository>(),
            Mock.Of<IJobConcurrencyScheduler>(), scopes, _time, NullLogger<JobManager>.Instance, transactions);
        var root = new RootFolder { Name = "Library", Path = "/library", ContentType = "movie" };
        var metadata = new MetadataContainer();
        var tierList = new QualityTierList { Name = "Quality" };
        var client = new DownloadClient { Name = "Client", ProviderType = "test", Enabled = true };
        _release = new Release
        {
            Title = "Film",
            Protocol = "torrent",
            Size = 100,
            CreatedAt = new DateTime(2026, 9, 22, 11, 0, 0, DateTimeKind.Utc)
        };
        _context.AddRange(root, metadata, tierList, client, _release);
        _context.SaveChanges();
        _node = new ContentHierarchyItem
        {
            Name = "Film",
            Slug = "film",
            ContentType = "movie",
            Level = "movie",
            MetadataContainerId = metadata.Id,
            RootFolderId = root.Id,
            EffectiveRootFolderId = root.Id,
            EffectiveQualityTierListId = tierList.Id
        };
        _context.Add(_node);
        _context.SaveChanges();
        _context.Add(new ReleaseScope { ReleaseId = _release.Id, ContentHierarchyItemId = _node.Id });
        _context.SaveChanges();
        var hierarchy = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        hierarchy.Setup(value => value.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_node);
        hierarchy.Setup(value => value.QueryAsync(It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(), It.IsAny<SortParameter?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>());
        _releases = new Mock<IReleaseRepository> { CallBase = true };
        var storedReleases = new ReleaseRepository(_context, audits, transactions,
            NullLogger<ReleaseRepository>.Instance, _time);
        _releases.Setup(value => value.GetByIdAsync(_release.Id, It.IsAny<CancellationToken>()))
            .Returns((Guid id, CancellationToken token) => storedReleases.GetByIdAsync(id, token));
        _releases.Setup(value => value.QueryAsync(It.IsAny<ReleaseQuery>(), It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(), It.IsAny<CancellationToken>()))
            .Returns((ReleaseQuery query, PagingParameter paging, SortParameter? sort, CancellationToken token) =>
                storedReleases.QueryAsync(query, paging, sort, token));
        var releaseScopes = new Mock<IReleaseScopeRepository> { CallBase = true };
        releaseScopes.Setup(value => value.QueryAsync(It.IsAny<ReleaseScopeQuery>(), It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ReleaseScope>
            {
                Items = [new ReleaseScope { ReleaseId = _release.Id, ContentHierarchyItemId = _node.Id }]
            });
        var indexers = new Mock<IIndexerRepository>();
        indexers.Setup(value => value.ListAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var clients = new Mock<IDownloadClientRepository>();
        clients.Setup(value => value.ListAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([client]);
        var integration = new Mock<IDownloadClientIntegration>();
        integration.SetupGet(value => value.ProviderType).Returns("test");
        integration.SetupGet(value => value.SupportedProtocols).Returns(["torrent"]);
        var roots = new Mock<IRootFolderService>();
        roots.Setup(value => value.GetAsync(root.Id, It.IsAny<CancellationToken>())).ReturnsAsync(root);
        var evaluation = new Mock<IReleaseEvaluationService>();
        evaluation.Setup(value => value.CreateEvaluationContextAsync(It.IsAny<Guid>(),
            It.IsAny<CancellationToken>())).ReturnsAsync(new ReleaseEvaluationContext
            {
                ContentHierarchyItemId = _node.Id
            });
        _files = new Mock<IFileSystem>();
        _files.Setup(value => value.GetStorageCapacity(It.IsAny<string>()))
            .Returns(new StorageCapacity { VolumeId = "library", AvailableBytes = 99 });
        _client = new Mock<IDownloadService>();
        _client.Setup(value => value.GrabAsync(It.IsAny<Release>(), It.IsAny<DownloadClient>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string?>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(GrabResult.FromSuccess(new DownloadInfo
            {
                ExternalId = "recovered-transfer",
                Status = DownloadStatus.Downloading
            }));
        _subject = new ReleaseService(hierarchy.Object, indexers.Object, _releases.Object, releaseScopes.Object,
            downloadScopes, _downloads, new ReleaseBlockList(_downloads), clients.Object,
            [integration.Object], _client.Object, roots.Object, _files.Object, audits, jobs, _executions,
            transactions, events, evaluation.Object, [], _time, NullLogger<ReleaseService>.Instance);
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    [TestCase(100, 99, "insufficient free space. Required: 100 bytes. Available: 99 bytes.")]
    [TestCase(null, 99, "the capacity check is unavailable. Required: unknown. Available: 99 bytes.")]
    [TestCase(100, null, "the capacity check is unavailable. Required: 100 bytes. Available: unknown.")]
    public async Task GetReleaseViewAsync_RetainsCapacityReason_WhenStoredRetryIsReloaded(
        long? size, long? available, string expected)
    {
        // Arrange
        _release.Size = size;
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _files.Setup(value => value.GetStorageCapacity(It.IsAny<string>()))
            .Returns(available is { } bytes ? new StorageCapacity { VolumeId = "library", AvailableBytes = bytes }
                : null);

        // Act
        await _subject.AcquireAsync(_release.Id, null, grabTargetContentHierarchyItemId: _node.Id)
            .ConfigureAwait(false);
        _context.ChangeTracker.Clear();
        var view = await _subject.GetReleaseViewAsync(_node.Id).ConfigureAwait(false);
        var stored = await _context.JobExecutions.AsNoTracking().SingleAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stored.Status, Is.EqualTo(JobExecutionStatus.Queued));
            Assert.That(stored.StatusMessage, Is.EqualTo("Waiting for destination capacity: " + expected));
            Assert.That(view![0].AcquisitionWaitingReason, Is.EqualTo("Waiting for destination capacity: " + expected));
            Assert.That(view[0].AcquisitionStatus, Is.Null);
            Assert.That(view[0].FailureReason, Is.Null);
            Assert.That(await _context.Downloads.CountAsync().ConfigureAwait(false), Is.Zero);
            _client.VerifyNoOtherCalls();
        }
    }

    [TestCase(0, 0)]
    [TestCase(100, 100)]
    public async Task AcquireAsync_UsesKnownCapacity_WhenVolumeIdentityIsUnavailable(long size, long available)
    {
        // Arrange
        _release.Size = size;
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _files.Setup(value => value.GetStorageCapacity(It.IsAny<string>()))
            .Returns(new StorageCapacity { AvailableBytes = available });

        // Act
        var download = await _subject.AcquireAsync(_release.Id, null, grabTargetContentHierarchyItemId: _node.Id)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(download!.Status, Is.EqualTo(DownloadStatus.Downloading));
            Assert.That(await _context.Downloads.CountAsync().ConfigureAwait(false), Is.EqualTo(1));
            Assert.That(await _context.JobExecutions.CountAsync().ConfigureAwait(false), Is.Zero);
        }
    }

    [Test]
    public async Task GetReleaseViewAsync_UpdatesReasonAndClearsOnRecovery_WhenRepeatedChecksCoalesce()
    {
        // Arrange
        await _subject.AcquireAsync(_release.Id, null, grabTargetContentHierarchyItemId: _node.Id)
            .ConfigureAwait(false);
        _time.Advance(TimeSpan.FromMinutes(1));
        _files.Setup(value => value.GetStorageCapacity(It.IsAny<string>())).Returns((StorageCapacity?)null);

        // Act
        await _subject.AcquireAsync(_release.Id, null, grabTargetContentHierarchyItemId: _node.Id)
            .ConfigureAwait(false);
        _context.ChangeTracker.Clear();
        var waiting = await _subject.GetReleaseViewAsync(_node.Id).ConfigureAwait(false);
        var retry = await _context.JobExecutions.AsNoTracking().SingleAsync().ConfigureAwait(false);
        _time.Advance(TimeSpan.FromMinutes(10));
        _files.Setup(value => value.GetStorageCapacity(It.IsAny<string>()))
            .Returns(new StorageCapacity { VolumeId = "library", AvailableBytes = 100 });
        var job = new ReleaseDownloadItemJob(_subject, _downloads, Mock.Of<IProgressReporter>());
        var retryContext = new JobContext(retry.Id, null, null, null, JobExecutionMode.Queue, retry.Parameters, null);
        await job.ExecuteAsync(retryContext).ConfigureAwait(false);
        _context.ChangeTracker.Clear();
        var recovered = await _subject.GetReleaseViewAsync(_node.Id).ConfigureAwait(false);
        await job.ExecuteAsync(retryContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(waiting![0].AcquisitionWaitingReason, Is.EqualTo("Waiting for destination capacity: " +
                "the capacity check is unavailable. Required: 100 bytes. Available: unknown."));
            Assert.That(recovered![0].AcquisitionWaitingReason, Is.Null);
            Assert.That(recovered[0].AcquisitionStatus, Is.EqualTo(DownloadStatus.Downloading));
            Assert.That(await _context.JobExecutions.CountAsync().ConfigureAwait(false), Is.EqualTo(1));
            Assert.That(await _context.Downloads.CountAsync().ConfigureAwait(false), Is.EqualTo(1));
            Assert.That(await _context.DownloadScopes.CountAsync().ConfigureAwait(false), Is.EqualTo(1));
            _client.Verify(value => value.GrabAsync(It.IsAny<Release>(), It.IsAny<DownloadClient>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string?>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [TestCase(JobExecutionStatus.Completed)]
    [TestCase(JobExecutionStatus.Cancelled)]
    [TestCase(JobExecutionStatus.CancellationRequested)]
    [TestCase(JobExecutionStatus.Failed)]
    public async Task GetReleaseViewAsync_ClearsCapacityReason_WhenRetryDoesNotWait(JobExecutionStatus status)
    {
        // Arrange
        await _subject.AcquireAsync(_release.Id, null, grabTargetContentHierarchyItemId: _node.Id)
            .ConfigureAwait(false);

        // Act
        await _context.JobExecutions.ExecuteUpdateAsync(update => update.SetProperty(value => value.Status, status))
            .ConfigureAwait(false);
        _context.ChangeTracker.Clear();
        var view = await _subject.GetReleaseViewAsync(_node.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(view![0].AcquisitionWaitingReason, Is.Null);
            Assert.That(view[0].AcquisitionStatus, Is.Null);
            Assert.That(await _context.Downloads.CountAsync().ConfigureAwait(false), Is.Zero);
        }
    }

    [Test]
    public async Task ExecuteAsync_PreservesNewIntent_WhenThePreviousDownloadHasTheSameTimestamp()
    {
        // Arrange
        _files.Setup(value => value.GetStorageCapacity(It.IsAny<string>()))
            .Returns(new StorageCapacity { VolumeId = "library", AvailableBytes = 100 });
        var previous = await _subject.AcquireAsync(_release.Id, null,
            grabTargetContentHierarchyItemId: _node.Id).ConfigureAwait(false);
        await _downloads.UpdateRefreshedStatusAsync(previous!.Id, DownloadStatus.Cancelled, "cancelled", null,
            null, null, null, false, null, null, _time.GetUtcNow().UtcDateTime, null).ConfigureAwait(false);
        _files.Setup(value => value.GetStorageCapacity(It.IsAny<string>()))
            .Returns(new StorageCapacity { VolumeId = "library", AvailableBytes = 99 });

        // Act
        await _subject.AcquireAsync(_release.Id, null, grabTargetContentHierarchyItemId: _node.Id)
            .ConfigureAwait(false);
        _context.ChangeTracker.Clear();
        var waiting = await _subject.GetReleaseViewAsync(_node.Id).ConfigureAwait(false);
        var retry = await _context.JobExecutions.AsNoTracking().SingleAsync().ConfigureAwait(false);
        _files.Setup(value => value.GetStorageCapacity(It.IsAny<string>()))
            .Returns(new StorageCapacity { VolumeId = "library", AvailableBytes = 100 });
        var job = new ReleaseDownloadItemJob(_subject, _downloads, Mock.Of<IProgressReporter>());
        var execution = new JobContext(retry.Id, null, null, null, JobExecutionMode.Queue, retry.Parameters, null);
        await job.ExecuteAsync(execution).ConfigureAwait(false);
        await job.ExecuteAsync(execution).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(waiting![0].AcquisitionWaitingReason, Does.Contain("insufficient free space"));
            Assert.That(await _context.Downloads.CountAsync().ConfigureAwait(false), Is.EqualTo(2));
            _client.Verify(value => value.GrabAsync(It.IsAny<Release>(), It.IsAny<DownloadClient>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string?>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }
    }

    [TestCase(false, 1, 1)]
    [TestCase(true, 2, 2)]
    public async Task ExecuteAsync_RetainsOnlyCurrentIntent_WhenAnInterveningAcquisitionWasCancelled(
        bool requestAgain, int expectedJobs, int expectedDownloads)
    {
        // Arrange
        await _subject.AcquireAsync(_release.Id, null, grabTargetContentHierarchyItemId: _node.Id)
            .ConfigureAwait(false);
        var original = await _context.JobExecutions.AsNoTracking().SingleAsync().ConfigureAwait(false);
        _time.Advance(TimeSpan.FromMinutes(1));
        _files.Setup(value => value.GetStorageCapacity(It.IsAny<string>()))
            .Returns(new StorageCapacity { VolumeId = "library", AvailableBytes = 100 });
        var intervening = await _subject.AcquireAsync(_release.Id, null,
            grabTargetContentHierarchyItemId: _node.Id).ConfigureAwait(false);
        await _downloads.UpdateRefreshedStatusAsync(intervening!.Id, DownloadStatus.Cancelled, "cancelled", null,
            null, null, null, false, null, null, _time.GetUtcNow().UtcDateTime, null).ConfigureAwait(false);
        _time.Advance(TimeSpan.FromMinutes(1));
        _files.Setup(value => value.GetStorageCapacity(It.IsAny<string>()))
            .Returns(new StorageCapacity { VolumeId = "library", AvailableBytes = 99 });
        var correlation = new Guid("55555555-5555-5555-5555-555555555555");

        // Act
        if (requestAgain)
        {
            await _subject.AcquireAsync(_release.Id, null, correlation,
                grabTargetContentHierarchyItemId: _node.Id).ConfigureAwait(false);
            _time.Advance(TimeSpan.FromMinutes(1));
            await _subject.AcquireAsync(_release.Id, null, Guid.NewGuid(),
                grabTargetContentHierarchyItemId: _node.Id).ConfigureAwait(false);
        }

        _context.ChangeTracker.Clear();
        var waiting = await _subject.GetReleaseViewAsync(_node.Id).ConfigureAwait(false);
        var retries = await _context.JobExecutions.AsNoTracking().OrderBy(value => value.CreatedAt)
            .ToListAsync().ConfigureAwait(false);
        var job = new ReleaseDownloadItemJob(_subject, _downloads, Mock.Of<IProgressReporter>());
        await job.ExecuteAsync(new JobContext(original.Id, null, null, null, JobExecutionMode.Queue,
            original.Parameters, null)).ConfigureAwait(false);
        _files.Setup(value => value.GetStorageCapacity(It.IsAny<string>()))
            .Returns(new StorageCapacity { VolumeId = "library", AvailableBytes = 100 });
        var current = retries[^1];
        var execution = new JobContext(current.Id, null, current.CorrelationId, null, JobExecutionMode.Queue,
            current.Parameters, null);
        await job.ExecuteAsync(execution).ConfigureAwait(false);
        await job.ExecuteAsync(execution).ConfigureAwait(false);
        var recovered = await _subject.GetReleaseViewAsync(_node.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(retries, Has.Count.EqualTo(expectedJobs));
            Assert.That(await _context.Downloads.CountAsync().ConfigureAwait(false), Is.EqualTo(expectedDownloads));
            Assert.That(recovered![0].AcquisitionWaitingReason, Is.Null);
            Assert.That(retries[0].Parameters["capacityDeferredAt"], Is.EqualTo("2026-09-22T12:00:00.0000000Z"));
            if (requestAgain)
            {
                Assert.That(waiting![0].AcquisitionWaitingReason, Does.Contain("insufficient free space"));
                Assert.That(current.Parameters["capacityPreviousDownloadId"], Is.EqualTo(intervening.Id.ToString("D")));
                Assert.That(current.Parameters["capacityDeferredAt"], Is.EqualTo("2026-09-22T12:02:00.0000000Z"));
                Assert.That(current.CorrelationId, Is.EqualTo(correlation));
                Assert.That(current.CreatedAt, Is.EqualTo(new DateTime(2026, 9, 22, 12, 2, 0, DateTimeKind.Utc)));
            }
            else
            {
                Assert.That(waiting![0].AcquisitionWaitingReason, Is.Null);
            }

            _client.Verify(value => value.GrabAsync(It.IsAny<Release>(), It.IsAny<DownloadClient>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string?>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()), Times.Exactly(expectedDownloads));
        }
    }

    [TestCase("releaseId")]
    [TestCase("contentHierarchyItemId")]
    public async Task GetReleaseViewAsync_OmitsCapacityReason_WhenRetryBelongsToAnotherTarget(string parameter)
    {
        // Arrange
        await _subject.AcquireAsync(_release.Id, null, grabTargetContentHierarchyItemId: _node.Id)
            .ConfigureAwait(false);
        var retry = await _executions.QueryAllAsync(new JobExecutionQuery()).ConfigureAwait(false);
        retry[0].Parameters[parameter] = Guid.NewGuid().ToString("D");

        // Act
        await _executions.UpdateAsync(retry[0], null).ConfigureAwait(false);
        _context.ChangeTracker.Clear();
        var view = await _subject.GetReleaseViewAsync(_node.Id).ConfigureAwait(false);

        // Assert
        Assert.That(view![0].AcquisitionWaitingReason, Is.Null);
    }

    [TestCase(DownloadStatus.Failed)]
    [TestCase(DownloadStatus.Cancelled)]
    [TestCase(DownloadStatus.Imported)]
    public async Task GetReleaseViewAsync_ShowsNewCapacityWait_WhenAnOlderTransferIsTerminal(DownloadStatus status)
    {
        // Arrange
        var client = await _context.DownloadClients.AsNoTracking().SingleAsync().ConfigureAwait(false);
        await _downloads.CreateAsync(new Download
        {
            Title = "Old transfer",
            ExternalId = "old-transfer",
            ReleaseId = _release.Id,
            DownloadClientId = client.Id,
            Status = status
        }, null).ConfigureAwait(false);
        _time.Advance(TimeSpan.FromMinutes(1));

        // Act
        await _subject.AcquireAsync(_release.Id, null, grabTargetContentHierarchyItemId: _node.Id)
            .ConfigureAwait(false);
        _context.ChangeTracker.Clear();
        var view = await _subject.GetReleaseViewAsync(_node.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(view![0].AcquisitionWaitingReason, Is.EqualTo("Waiting for destination capacity: " +
                "insufficient free space. Required: 100 bytes. Available: 99 bytes."));
            Assert.That(await _context.Downloads.CountAsync().ConfigureAwait(false), Is.EqualTo(1));
            _client.VerifyNoOtherCalls();
        }
    }

    [Test]
    public async Task GetReleaseViewAsync_ShowsCapacityWait_WhenEquivalentReleaseSourcesCoalesce()
    {
        // Arrange
        _release.IdentityKeys.Add(new ReleaseIdentityKey
        {
            Authority = "btih",
            Value = "ffffffffffffffffffffffffffffffffffffffff"
        });
        var other = new Release
        {
            Id = Guid.NewGuid(),
            Title = "Other source",
            Protocol = "torrent",
            CreatedAt = _release.CreatedAt.AddMinutes(-1),
            IdentityKeys = { new ReleaseIdentityKey
            {
                Authority = "btih", Value = "ffffffffffffffffffffffffffffffffffffffff"
            } }
        };
        _releases.Setup(value => value.QueryAsync(It.IsAny<ReleaseQuery>(), It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Release> { Items = [other, _release] });

        // Act
        await _subject.AcquireAsync(_release.Id, null, grabTargetContentHierarchyItemId: _node.Id)
            .ConfigureAwait(false);
        _context.ChangeTracker.Clear();
        var view = await _subject.GetReleaseViewAsync(_node.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(view, Has.Count.EqualTo(1));
            Assert.That(view![0].AcquisitionWaitingReason, Is.EqualTo("Waiting for destination capacity: " +
                "insufficient free space. Required: 100 bytes. Available: 99 bytes."));
        }
    }
}
