using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Releases;
using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
using Magpie.Core;
using Magpie.Core.Jobs;
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

using Microsoft.AspNetCore.Builder;
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
    private ReleaseRepository _storedReleases = null!;
    private FakeTimeProvider _time = null!;
    private Release _release = null!;
    private ContentHierarchyItem _node = null!;
    private JsonSerializerOptions _bindingOptions = null!;

    [OneTimeSetUp]
    public void CreateTemplate()
    {
        _bindingOptions = new JsonSerializerOptions(JsonSerializerOptions.Web);
        MagpieHostBuilderExtensions.ConfigureApiJsonOptions(_bindingOptions);
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
        _storedReleases = new ReleaseRepository(_context, audits, transactions,
            NullLogger<ReleaseRepository>.Instance, _time);
        _releases.Setup(value => value.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns((Guid id, CancellationToken token) => _storedReleases.GetByIdAsync(id, token));
        _releases.Setup(value => value.QueryAsync(It.IsAny<ReleaseQuery>(), It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(), It.IsAny<CancellationToken>()))
            .Returns((ReleaseQuery query, PagingParameter paging, SortParameter? sort, CancellationToken token) =>
                _storedReleases.QueryAsync(query, paging, sort, token));
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
        _client
            .Setup(value => value.GrabAsync(
                It.IsAny<Release>(),
                It.IsAny<DownloadClient>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<ReleaseGrabContext?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(GrabResult.FromSuccess(new DownloadInfo
            {
                ExternalId = "recovered-transfer",
                Status = DownloadStatus.Downloading
            }));
        _subject = new ReleaseService(
            hierarchy.Object,
            indexers.Object,
            Mock.Of<IIndexerService>(),
            _releases.Object,
            releaseScopes.Object,
            downloadScopes,
            _downloads,
            new ReleaseBlockList(_downloads),
            Mock.Of<ISettingsRepository>(),
            clients.Object,
            [integration.Object],
            _client.Object,
            roots.Object,
            _files.Object,
            audits,
            jobs,
            transactions,
            events,
            evaluation.Object,
            [],
            _time,
            NullLogger<ReleaseService>.Instance);
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    [Test]
    public async Task ResolveStatusesAsync_BlocksTheRelease_WhenTheGrabOfItsPostedBodyFailed()
    {
        // Arrange
        var body = """
            {"title":"Film","protocol":"torrent","size":10,
            "identityKeys":[{"authority":"btih","value":"0123456789abcdef0123456789abcdef01234567","ordinal":0}]}
            """;
        var posted = await _storedReleases.CreateAsync(
            JsonSerializer.Deserialize<Release>(body, _bindingOptions)!,
            null,
            default).ConfigureAwait(false);
        _client
            .Setup(value => value.GrabAsync(
                It.IsAny<Release>(),
                It.IsAny<DownloadClient>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<ReleaseGrabContext?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GrabResult { Success = false, ErrorMessage = "The client rejected the release." });
        var searched = new Release { Id = Guid.NewGuid(), Title = "Film", Protocol = "torrent" };
        searched.IdentityKeys.Add(new ReleaseIdentityKey
        {
            Authority = "btih",
            Value = "0123456789abcdef0123456789abcdef01234567"
        });

        // Act
        Assert.CatchAsync(() => _subject.AcquireAsync(posted.Id, null, grabTargetContentHierarchyItemId: _node.Id));
        var statuses = await new ReleaseBlockList(_downloads).ResolveStatusesAsync([searched], _node.Id, default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(statuses.GetValueOrDefault(searched.Id).Status, Is.EqualTo(DownloadStatus.Failed));
            Assert.That(ReleaseBlockList.Blocked(statuses), Does.Contain(searched.Id));
        }
    }

    [Test]
    public async Task ResolveStatusesAsync_BlocksTheRelease_WhenTheGrabOfTheKeylessReleaseFailed()
    {
        // Arrange
        var keyless = await _storedReleases.CreateAsync(
            new Release { Title = "Film", Protocol = "torrent", Size = 10 },
            null,
            default).ConfigureAwait(false);
        _client
            .Setup(value => value.GrabAsync(
                It.IsAny<Release>(),
                It.IsAny<DownloadClient>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<ReleaseGrabContext?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GrabResult { Success = false, ErrorMessage = "The client rejected the release." });

        // Act
        Assert.CatchAsync(() => _subject.AcquireAsync(keyless.Id, null, grabTargetContentHierarchyItemId: _node.Id));
        var statuses = await new ReleaseBlockList(_downloads).ResolveStatusesAsync([keyless], _node.Id, default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                statuses.GetValueOrDefault(keyless.Id),
                Is.EqualTo((DownloadStatus.Failed, (DownloadFailureReason?)DownloadFailureReason.ClientRejected)));
            Assert.That(ReleaseBlockList.Blocked(statuses), Does.Contain(keyless.Id));
        }
    }

    [TestCase(false, DownloadStatus.Failed, true)]
    [TestCase(true, DownloadStatus.Faulted, false)]
    public async Task AcquireAsync_BlocksOnlyAPermanentRejection_WhenTheClientRejectsThePayload(
        bool transient,
        DownloadStatus expectedStatus,
        bool expectedBlocked)
    {
        // Arrange
        var rejected = await _storedReleases.CreateAsync(
            new Release { Title = "Film", Protocol = "torrent", Size = 10 },
            null,
            default).ConfigureAwait(false);
        var integration = new Mock<IDownloadClientIntegration>();
        integration.SetupGet(value => value.ProviderType).Returns("test");
        integration
            .Setup(value => value.DownloadReleaseAsync(
                It.IsAny<Release>(),
                It.IsAny<ReleaseGrabContext>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ReleasePayloadRejectedException(
                "The indexer returned an HTML page instead of a .torrent file (HTTP 200, text/html).",
                transient));
        var downloadService = new DownloadService(
            [integration.Object],
            Mock.Of<IDownloadClientRepository>(),
            _downloads,
            Mock.Of<IJobManager>(),
            Mock.Of<ITransactionManager>(),
            _time,
            NullLogger<DownloadService>.Instance);
        _client
            .Setup(value => value.GrabAsync(
                It.IsAny<Release>(),
                It.IsAny<DownloadClient>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<ReleaseGrabContext?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Returns((
                Release release,
                DownloadClient source,
                IReadOnlyDictionary<string, string> settings,
                ReleaseGrabContext? context,
                bool interactive,
                CancellationToken token) =>
                downloadService.GrabAsync(release, source, settings, context, interactive, token));

        // Act
        Assert.CatchAsync(() => _subject.AcquireAsync(rejected.Id, null, grabTargetContentHierarchyItemId: _node.Id));
        var statuses = await new ReleaseBlockList(_downloads).ResolveStatusesAsync([rejected], _node.Id, default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                statuses.GetValueOrDefault(rejected.Id),
                Is.EqualTo((expectedStatus, (DownloadFailureReason?)DownloadFailureReason.ClientRejected)));
            Assert.That(ReleaseBlockList.Blocked(statuses).Contains(rejected.Id), Is.EqualTo(expectedBlocked));
        }
    }

    [TestCase(false, true, 0)]
    [TestCase(true, false, 1)]
    public async Task GrabAsync_AppliesTheTaint_WhenAGrabOfTheKeylessReleaseFailedOnTheClient(
        bool interactive,
        bool expectedTainted,
        int expectedDispatches)
    {
        // Arrange
        var keyless = await _storedReleases.CreateAsync(
            new Release { Title = "Film", Protocol = "torrent", Size = 10 },
            null,
            default).ConfigureAwait(false);
        var client = await _context.DownloadClients.AsNoTracking().SingleAsync().ConfigureAwait(false);
        await _downloads.CreateAsync(
            new Download
            {
                ReleaseId = keyless.Id,
                DownloadClientId = client.Id,
                ExternalId = string.Empty,
                Title = "Film",
                Status = DownloadStatus.Failed,
                FailureReason = DownloadFailureReason.ClientRejected
            },
            null,
            default).ConfigureAwait(false);
        var integration = new Mock<IDownloadClientIntegration>();
        integration.SetupGet(value => value.ProviderType).Returns("test");
        integration
            .Setup(value => value.DownloadReleaseAsync(
                It.IsAny<Release>(),
                It.IsAny<ReleaseGrabContext>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DownloadInfo { ExternalId = "regrab", Status = DownloadStatus.Queued });
        var downloadService = new DownloadService(
            [integration.Object],
            Mock.Of<IDownloadClientRepository>(),
            _downloads,
            Mock.Of<IJobManager>(),
            Mock.Of<ITransactionManager>(),
            _time,
            NullLogger<DownloadService>.Instance);

        // Act
        var result = await downloadService
            .GrabAsync(keyless, client, client.Settings, interactive: interactive)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.AlreadyTainted, Is.EqualTo(expectedTainted));
            integration.Verify(
                value => value.DownloadReleaseAsync(
                    It.IsAny<Release>(),
                    It.IsAny<ReleaseGrabContext>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()),
                Times.Exactly(expectedDispatches));
        }
    }

    [Test]
    public async Task CreateAsync_StoresTheReleaseWithoutKeysOrExternalIds_WhenTheBodyPostsThemAsNull()
    {
        // Arrange
        var body = """{"title":"Film","protocol":"torrent","size":10,"identityKeys":null,"externalIds":null}""";

        // Act
        var posted = await _storedReleases.CreateAsync(
            JsonSerializer.Deserialize<Release>(body, _bindingOptions)!,
            null,
            default).ConfigureAwait(false);
        _context.ChangeTracker.Clear();
        var stored = await _context.Releases
            .AsNoTracking()
            .Include(value => value.IdentityKeys)
            .Include(value => value.ExternalIds)
            .SingleAsync(value => value.Id == posted.Id)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stored.IdentityKeys, Is.Empty);
            Assert.That(stored.ExternalIds, Is.Empty);
        }
    }

    [TestCase(null)]
    [TestCase(-1)]
    public async Task AcquireAsync_GrabsTheRelease_WhenDestinationSpaceCannotBeRead(long? available)
    {
        // Arrange
        _files.Setup(value => value.GetStorageCapacity(It.IsAny<string>()))
            .Returns(available is { } bytes ? new StorageCapacity { AvailableBytes = bytes } : null);

        // Act
        var acquired = await _subject.AcquireAsync(_release.Id, null, grabTargetContentHierarchyItemId: _node.Id)
            .ConfigureAwait(false);
        _context.ChangeTracker.Clear();
        var stored = await _context.Downloads.AsNoTracking().SingleAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(acquired!.Status, Is.EqualTo(DownloadStatus.Downloading));
            Assert.That(stored.Status, Is.EqualTo(DownloadStatus.Downloading));
            Assert.That(stored.FailureReason, Is.Null);
            _client.Verify(
                value => value.GrabAsync(
                    It.IsAny<Release>(),
                    It.IsAny<DownloadClient>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<ReleaseGrabContext?>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [TestCase(0)]
    [TestCase(99)]
    public async Task GetReleaseViewAsync_ShowsRetryableFault_WhenReleaseExceedsDestinationSpace(long available)
    {
        // Arrange
        _files.Setup(value => value.GetStorageCapacity(It.IsAny<string>()))
            .Returns(new StorageCapacity { AvailableBytes = available });
        _release.IdentityKeys.Add(new ReleaseIdentityKey
        {
            Authority = "btih",
            Value = "ffffffffffffffffffffffffffffffffffffffff"
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var exception = Assert.ThrowsAsync<InvalidOperationException>(() =>
            _subject.AcquireAsync(_release.Id, null, grabTargetContentHierarchyItemId: _node.Id));
        _context.ChangeTracker.Clear();
        var view = await _subject.GetReleaseViewAsync(_node.Id).ConfigureAwait(false);
        var stored = await _context.Downloads.AsNoTracking().Include(value => value.ReleaseIdentityKeys)
            .ToListAsync().ConfigureAwait(false);
        var statuses = await new ReleaseBlockList(_downloads).ResolveStatusesAsync([_release], _node.Id, default)
            .ConfigureAwait(false);
        var scope = await _context.DownloadScopes.AsNoTracking().SingleAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(view![0].AcquisitionStatus, Is.EqualTo(DownloadStatus.Faulted));
            Assert.That(view[0].FailureReason, Is.EqualTo(DownloadFailureReason.InsufficientDestinationSpace));
            Assert.That(stored, Has.Count.EqualTo(1));
            Assert.That(stored[0].StatusReason, Is.EqualTo(exception!.Message));
            Assert.That(stored[0].ExternalId, Is.Empty);
            Assert.That(stored[0].FailedAt, Is.EqualTo(new DateTime(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc)));
            Assert.That(
                stored[0].ReleaseIdentityKeys.Single().Value,
                Is.EqualTo("ffffffffffffffffffffffffffffffffffffffff"));
            Assert.That(scope.DownloadId, Is.EqualTo(stored[0].Id));
            Assert.That(scope.ContentHierarchyItemId, Is.EqualTo(_node.Id));
            Assert.That(ReleaseBlockList.Blocked(statuses), Is.Empty);
            Assert.That(await _context.JobExecutions.CountAsync().ConfigureAwait(false), Is.Zero);
            _client.VerifyNoOtherCalls();
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task AcquireAsync_GrabsImmediatelyOnce_WhenSpaceBecomesAvailableAfterCapacityFault(bool interactive)
    {
        // Arrange
        Assert.ThrowsAsync<InvalidOperationException>(() =>
            _subject.AcquireAsync(
                _release.Id,
                null,
                interactive: interactive,
                grabTargetContentHierarchyItemId: _node.Id));

        // Act
        _files.Setup(value => value.GetStorageCapacity(It.IsAny<string>()))
            .Returns(new StorageCapacity { AvailableBytes = 100 });
        var acquired = await _subject.AcquireAsync(
            _release.Id,
            null,
            interactive: interactive,
            grabTargetContentHierarchyItemId: _node.Id)
            .ConfigureAwait(false);
        var repeated = await _subject.AcquireAsync(
            _release.Id,
            null,
            interactive: interactive,
            grabTargetContentHierarchyItemId: _node.Id)
            .ConfigureAwait(false);
        _context.ChangeTracker.Clear();
        var stored = await _context.Downloads.AsNoTracking().ToListAsync()
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(acquired!.Status, Is.EqualTo(DownloadStatus.Downloading));
            Assert.That(repeated!.Id, Is.EqualTo(acquired.Id));
            Assert.That(stored, Has.Count.EqualTo(2));
            Assert.That(
                stored.Single(value => value.Status == DownloadStatus.Faulted).FailedAt,
                Is.EqualTo(new DateTime(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc)));
            Assert.That(acquired.FailureReason, Is.Null);
            Assert.That(
                await _context.JobExecutions.Select(value => value.JobType).ToListAsync().ConfigureAwait(false),
                Is.EqualTo([MagpieJobs.Types.DownloadRefresh]));
            _client.Verify(
                value => value.GrabAsync(
                    It.IsAny<Release>(),
                    It.IsAny<DownloadClient>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<ReleaseGrabContext?>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [TestCase(null, 0)]
    [TestCase(null, null)]
    [TestCase(-1, 0)]
    [TestCase(-1, null)]
    public async Task AcquireAsync_GrabsWithoutCapacityWait_WhenReleaseSizeIsUnknown(long? size, long? available)
    {
        // Arrange
        _release.Size = size;
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _files.Setup(value => value.GetStorageCapacity(It.IsAny<string>()))
            .Returns(available is { } bytes ? new StorageCapacity { AvailableBytes = bytes } : null);

        // Act
        var acquired = await _subject.AcquireAsync(_release.Id, null, grabTargetContentHierarchyItemId: _node.Id)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(acquired!.Status, Is.EqualTo(DownloadStatus.Downloading));
            Assert.That(await _context.Downloads.CountAsync().ConfigureAwait(false), Is.EqualTo(1));
            Assert.That(await _context.JobExecutions.CountAsync().ConfigureAwait(false), Is.Zero);
            _files.Verify(value => value.GetStorageCapacity(It.IsAny<string>()), Times.Never);
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
    public async Task AcquireAsync_StopsBeforeTheCapacityCheck_WhenAnOlderTransferOfTheKeylessReleaseWasImported()
    {
        // Arrange
        var client = await _context.DownloadClients.AsNoTracking().SingleAsync().ConfigureAwait(false);
        await _downloads.CreateAsync(new Download
        {
            Title = "Old transfer",
            ExternalId = "old-transfer",
            ReleaseId = _release.Id,
            DownloadClientId = client.Id,
            Status = DownloadStatus.Imported
        }, null).ConfigureAwait(false);
        _time.Advance(TimeSpan.FromMinutes(1));

        // Act
        var download = await _subject.AcquireAsync(_release.Id, null, grabTargetContentHierarchyItemId: _node.Id)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(download, Is.Null);
            Assert.That(await _context.Downloads.CountAsync().ConfigureAwait(false), Is.EqualTo(1));
            _client.VerifyNoOtherCalls();
        }
    }

    [Test]
    public async Task GetReleaseViewAsync_ShowsCapacityFault_WhenEquivalentReleaseSourcesCoalesce()
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
        Assert.ThrowsAsync<InvalidOperationException>(() =>
            _subject.AcquireAsync(_release.Id, null, grabTargetContentHierarchyItemId: _node.Id));
        _context.ChangeTracker.Clear();
        var view = await _subject.GetReleaseViewAsync(_node.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(view, Has.Count.EqualTo(1));
            Assert.That(view![0].AcquisitionStatus, Is.EqualTo(DownloadStatus.Faulted));
            Assert.That(view[0].FailureReason, Is.EqualTo(DownloadFailureReason.InsufficientDestinationSpace));
        }
    }
}
