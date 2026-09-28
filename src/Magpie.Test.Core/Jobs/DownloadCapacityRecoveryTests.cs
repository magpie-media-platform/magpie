using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Content;
using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Releases;
using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
using Magpie.Core;
using Magpie.Core.Jobs;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Releases;
using Magpie.Models.Storage;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Releases;
using Magpie.Sdk.Services;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs;

[TestFixture]
public sealed class DownloadCapacityRecoveryTests
{
    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private IDownloadRepository _downloads = null!;
    private DownloadImportItemJob _import = null!;
    private Mock<IFileSystem> _files = null!;
    private Mock<IJobManager> _jobs = null!;
    private DownloadImportScanJob _scan = null!;
    private FakeTimeProvider _time = null!;
    private Download _download = null!;
    private Release _release = null!;
    private JobContext _job = null!;
    private string _destination = null!;
    private Mock<IContentFileService> _rename = null!;
    private ContentHierarchyItem _node = null!;

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
            .UseSqlite(connection, contextOwnsConnection: true).Options);
        _time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero));
        var transactions = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);
        var events = Mock.Of<IEventBus>();
        var audits = new AuditRecordRepository(_context, events, NullLogger<AuditRecordRepository>.Instance,
            transactions, Mock.Of<IJobExecutionScopeFactory>(), _time);
        _downloads = new DownloadRepository(_context, audits, transactions, events,
            NullLogger<DownloadRepository>.Instance, Mock.Of<IDataProtectionService>(), _time);
        var root = new RootFolder { Name = "Library", Path = "/library", ContentType = "movie" };
        var metadata = new MetadataContainer();
        var client = new DownloadClient { Name = "Client", ProviderType = "test", Enabled = true };
        _release = new Release { Title = "Film", Protocol = "torrent", Size = 100 };
        _context.AddRange(root, metadata, client, _release);
        _context.SaveChanges();
        var node = new ContentHierarchyItem
        {
            Name = "Film",
            Slug = "film",
            ContentType = "movie",
            Level = "movie",
            MetadataContainerId = metadata.Id,
            RootFolderId = root.Id,
            EffectiveRootFolderId = root.Id
        };
        _context.Add(node);
        _context.SaveChanges();
        var content = new ContentItem { ContentHierarchyItemId = node.Id, Fulfillment = FulfillmentStatus.Missing };
        _download = new Download
        {
            Title = "Film",
            ExternalId = "original-client-transfer",
            DownloadClientId = client.Id,
            ReleaseId = _release.Id,
            Status = DownloadStatus.Downloaded,
            OutputPath = "/downloads/film.mkv",
            DateCompleted = _time.GetUtcNow().UtcDateTime,
            CreatedAt = _time.GetUtcNow().UtcDateTime
        };
        _context.AddRange(content, _download);
        _context.SaveChanges();
        _context.Add(new DownloadScope { DownloadId = _download.Id, ContentHierarchyItemId = node.Id });
        _context.SaveChanges();
        _context.ChangeTracker.Clear();
        var roots = new Mock<IRootFolderRepository>();
        roots.Setup(repository => repository.GetByIdAsync(root.Id, It.IsAny<CancellationToken>())).ReturnsAsync(root);
        var nodes = new Mock<IContentHierarchyItemRepository>();
        nodes.Setup(repository => repository.GetLineageAsync(node.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([node]);
        var releases = new Mock<IReleaseRepository>();
        releases.Setup(repository => repository.GetByIdAsync(_release.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_release);
        var contents = new Mock<IContentRepository> { CallBase = true };
        var contentService = new Mock<IContentService>();
        contentService.Setup(service => service.GetOrCreateContentItemAsync(node.Id, It.IsAny<DateTime>(),
            It.IsAny<Guid?>(), null, It.IsAny<CancellationToken>())).ReturnsAsync(content);
        var scopes = new DownloadScopeRepository(_context, transactions, audits,
            NullLogger<DownloadScopeRepository>.Instance, _time);
        var media = new MediaFileRepository(_context, NullLogger<MediaFileRepository>.Instance, transactions,
            contents.Object, nodes.Object, events);
        var blocked = new Mock<IBlockedFileTypeRepository>();
        blocked.Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var mappings = new Mock<IRemotePathMappingRepository>();
        mappings.Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _files = new Mock<IFileSystem>();
        _destination = new FileInfo("/library/film.mkv").FullName;
        _files.Setup(files => files.FileExists("/downloads/film.mkv")).Returns(true);
        _files.Setup(files => files.GetFileSize(It.IsAny<string>())).Returns(100);
        _files.Setup(files => files.GetFileIdentity(It.IsAny<string>())).Returns("owned-file");
        _files.Setup(files => files.GetFileVersion(It.IsAny<string>())).Returns("owned-version");
        _files.Setup(files => files.GetFileSize(It.Is<string>(path => Path.GetFileName(path) == "payload")))
            .Throws<FileNotFoundException>();
        _files.Setup(files => files.TransferToOwnedPathAsync(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<ImportMode>(), It.IsAny<Func<string, Task>>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, ImportMode, Func<string, Task>, CancellationToken>(
                (_, _, _, created, _) => created("owned-file"));
        _files.Setup(files => files.GetStorageCapacity(It.IsAny<string>()))
            .Returns(new StorageCapacity { VolumeId = "library-volume", AvailableBytes = 99 });
        _files.Setup(files => files.MatchesAnyFileTypeAsync(It.IsAny<string>(),
            It.IsAny<IReadOnlyList<string>?>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var rename = new Mock<IContentFileService>();
        _rename = rename;
        _node = node;
        rename.Setup(service => service.PreviewRenameAsync(node.Id, It.IsAny<FileInfo>(),
                It.IsAny<MediaInfo?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentRename
            {
                Original = new FileInfo("/downloads/film.mkv"),
                Rename = new FileInfo(_destination),
                Item = node
            });
        var evaluation = new Mock<IReleaseEvaluationService>();
        evaluation.Setup(service => service.ResolveScopeAsync(_release, It.IsAny<IReadOnlyList<ContentFact>>(),
            node.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new ReleaseScopeResolution([], []));
        var executions = new Mock<IJobExecutionRepository> { CallBase = true };
        executions.Setup(repository => repository.QueryAsync(It.IsAny<JobExecutionQuery>(),
                It.IsAny<Magpie.Models.Paging.PagingParameter>(), It.IsAny<Magpie.Models.Paging.SortParameter?>(),
                It.IsAny<CancellationToken>())).ReturnsAsync(new Magpie.Models.Paging.PagedList<JobExecution>());
        _jobs = new Mock<IJobManager>();
        _scan = new DownloadImportScanJob(_downloads, executions.Object, _jobs.Object, _time,
            NullLogger<DownloadImportScanJob>.Instance, Mock.Of<IProgressReporter>());
        _import = new DownloadImportItemJob(audits, _downloads, roots.Object, nodes.Object, media, releases.Object,
            Mock.Of<IReleaseScopeRepository>(), scopes, executions.Object, _files.Object,
            Mock.Of<IContentTypeRegistry>(), rename.Object, blocked.Object, mappings.Object,
            Mock.Of<IDownloadClientRepository>(), Mock.Of<IRemotePathMappingService>(), transactions,
            Mock.Of<IQualityService>(), contents.Object, contentService.Object,
            Mock.Of<ILibraryImportService>(),
            events, evaluation.Object, _time, NullLogger<DownloadImportItemJob>.Instance, Mock.Of<IProgressReporter>());
        _job = new JobContext(Guid.NewGuid(), null, null, null, JobExecutionMode.Queue,
            new Dictionary<string, string> { ["downloadId"] = _download.Id.ToString("D") }, null);
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    [TestCase(unchecked((int)0x80070070),
        "Waiting for destination capacity: file coordination reported insufficient space.")]
    [TestCase(32, "Waiting for exclusive access to the import files.")]
    public async Task ExecuteAsync_RetainsOriginalDownload_WhenPathCoordinationFails(int code, string reason)
    {
        // Arrange
        _files.Setup(files => files.AcquireImportPaths(It.IsAny<IReadOnlyList<string>>()))
            .Throws(new IOException("Synthetic path coordination failure.", code));

        // Act
        Assert.CatchAsync<InvalidOperationException>(() => _import.ExecuteAsync(_job));
        var stored = await _downloads.GetByIdAsync(_download.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stored!.Status, Is.EqualTo(DownloadStatus.Importing));
            Assert.That(stored.StatusReason, Is.EqualTo(reason));
            Assert.That(stored.FailureReason, Is.Null);
            Assert.That(await _downloads.ListCleanupCandidatesAsync().ConfigureAwait(false), Is.Empty);
            _files.Verify(files => files.TransferToOwnedPathAsync(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<ImportMode>(), It.IsAny<Func<string, Task>>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_ResumesOriginalImportOnce_WhenCapacityRecoversAfterRefreshAndScan()
    {
        // Arrange
        await _import.ExecuteAsync(_job).ConfigureAwait(false);
        var held = await _downloads.GetByIdAsync(_download.Id).ConfigureAwait(false);
        _time.Advance(TimeSpan.FromMinutes(11));

        // Act
        var refreshed = await _downloads.UpdateRefreshedStatusAsync(_download.Id, DownloadStatus.Downloaded,
            "client complete", "/other/path", _time.GetUtcNow().UtcDateTime, 100, _time.GetUtcNow().UtcDateTime,
            false, null, null, _time.GetUtcNow().UtcDateTime, null).ConfigureAwait(false);
        await _scan.ExecuteAsync(_job).ConfigureAwait(false);
        await _import.ExecuteAsync(_job).ConfigureAwait(false);
        var repeated = await _downloads.GetByIdAsync(_download.Id).ConfigureAwait(false);
        _files.Setup(files => files.GetStorageCapacity(It.IsAny<string>()))
            .Returns(new StorageCapacity { VolumeId = "library-volume", AvailableBytes = 100 });
        await _import.ExecuteAsync(_job).ConfigureAwait(false);
        await _import.ExecuteAsync(_job).ConfigureAwait(false);
        var imported = await _downloads.GetByIdAsync(_download.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(held!.Status, Is.EqualTo(DownloadStatus.Importing));
            Assert.That(held.StatusReason, Is.EqualTo(
                "Waiting for destination capacity: insufficient free space. " +
                "Required: 100 bytes. Available: 99 bytes."));
            Assert.That(held.FailureReason, Is.Null);
            Assert.That(held.FailedAt, Is.Null);
            Assert.That(refreshed, Is.False);
            Assert.That(repeated!.ModifiedAt, Is.EqualTo(held.ModifiedAt));
            Assert.That(imported!.Status, Is.EqualTo(DownloadStatus.Imported));
            Assert.That(imported.StatusReason, Is.Null);
            Assert.That(imported.ExternalId, Is.EqualTo("original-client-transfer"));
            Assert.That(imported.OutputPath, Is.EqualTo("/downloads/film.mkv"));
            Assert.That(imported.DownloadClientId, Is.EqualTo(_download.DownloadClientId));
            Assert.That(imported.ReleaseId, Is.EqualTo(_release.Id));
            Assert.That(await _context.DownloadScopes.CountAsync().ConfigureAwait(false), Is.EqualTo(1));
            Assert.That(await _context.MediaFiles.CountAsync().ConfigureAwait(false), Is.EqualTo(1));
            Assert.That(await _context.Downloads.CountAsync().ConfigureAwait(false), Is.EqualTo(1));
            _files.Verify(files => files.TransferToOwnedPathAsync("/downloads/film.mkv", It.IsAny<string>(),
                ImportMode.Copy, It.IsAny<Func<string, Task>>(), It.IsAny<CancellationToken>()), Times.Once);
            _files.Verify(files => files.DeleteFile(It.IsAny<string>()), Times.Never);
            _jobs.Verify(jobs => jobs.ExecuteAsync(It.Is<Magpie.Models.Jobs.JobEnqueueRequest>(request =>
                request.JobType == "DownloadImportItem" && request.OnlyIfInactive), It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [TestCase(false, 2, false)]
    [TestCase(true, 2, true)]
    [TestCase(false, 3, true)]
    public async Task ExecuteAsync_RechecksRestoredReplacement_WhenRecoveryIsReloadedAfterInterruption(
        bool unreadableBackup, long originalSize, bool remainsPending)
    {
        // Arrange
        var backup = $"{_destination}.magpie-replace-{_download.Id:N}.orig";
        var recovery = JsonSerializer.Serialize(new
        {
            Files = Array.Empty<object>(),
            Backups = new[]
            {
                new { OriginalPath = _destination, BackupPath = backup, OriginalSize = 2, Identity = "owned-file",
                    Version = "owned-version" }
            },
            Reason = "Waiting for destination capacity: a transfer reported insufficient space."
        });
        await _downloads.MarkImportingAsync(_download.Id, _time.GetUtcNow().UtcDateTime, null,
            CancellationToken.None).ConfigureAwait(false);
        await _downloads.SetImportRecoveryStateAsync(_download.Id, recovery,
            "File recovery is incomplete.", _time.GetUtcNow().UtcDateTime, null, CancellationToken.None)
            .ConfigureAwait(false);
        _files.Setup(files => files.FileExists(_destination)).Returns(true);
        _files.Setup(files => files.GetFileSize(_destination)).Returns(originalSize);
        _files.Setup(files => files.GetFileSize(backup)).Throws(unreadableBackup
            ? new IOException("Synthetic inaccessible backup.") : new FileNotFoundException("No backup remains."));
        _job = _job with
        {
            Parameters = new Dictionary<string, string>(_job.Parameters) { ["collisionMode"] = "replace" }
        };

        // Act
        _context.ChangeTracker.Clear();
        await _import.ExecuteAsync(_job).ConfigureAwait(false);
        var stored = await _downloads.GetByIdAsync(_download.Id).ConfigureAwait(false);
        var cleanup = await _downloads.ListCleanupCandidatesAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stored!.Status, Is.EqualTo(DownloadStatus.Importing));
            Assert.That(stored.ImportRecoveryState is not null, Is.EqualTo(remainsPending));
            Assert.That(stored.FailureReason, Is.Null);
            Assert.That(cleanup, Is.Empty);
            Assert.That(await _context.MediaFiles.CountAsync().ConfigureAwait(false), Is.Zero);
            _files.Verify(files => files.DeleteFile(It.IsAny<string>()), Times.Never);
            _files.Verify(files => files.Move(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
            _files.Verify(files => files.Copy(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_DoesNotRequireAnotherPayload_WhenSameVolumeMoveReportsCapacityFailure()
    {
        // Arrange
        _release.Protocol = "usenet";
        _files.Setup(files => files.GetFileSize(_destination)).Throws<FileNotFoundException>();
        _files.Setup(files => files.GetStorageCapacity(It.IsAny<string>()))
            .Returns(new StorageCapacity { VolumeId = "same-volume", AvailableBytes = 0 });
        _files.Setup(files => files.TransferToOwnedPathAsync("/downloads/film.mkv", It.IsAny<string>(),
                ImportMode.Move, It.IsAny<Func<string, Task>>(), It.IsAny<CancellationToken>()))
            .Throws(new IOException("Synthetic full-volume metadata error.", unchecked((int)0x80070027)));

        // Act
        await _import.ExecuteAsync(_job).ConfigureAwait(false);
        var stored = await _downloads.GetByIdAsync(_download.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stored!.Status, Is.EqualTo(DownloadStatus.Importing));
            Assert.That(stored.StatusReason, Does.Contain("Required: 0 bytes. Available: 0 bytes."));
            Assert.That(stored.FailureReason, Is.Null);
            Assert.That(await _context.MediaFiles.CountAsync().ConfigureAwait(false), Is.Zero);
        }
    }

    [Test]
    public async Task ExecuteAsync_DoesNotReportCapacityWaiting_WhenTransferHasAnOrdinaryFailure()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        _files.Setup(files => files.GetStorageCapacity(It.IsAny<string>()))
            .Returns(new StorageCapacity { VolumeId = "library-volume", AvailableBytes = 100 });
        _files.Setup(files => files.GetFileSize(_destination)).Throws<FileNotFoundException>();
        _files.Setup(files => files.TransferToOwnedPathAsync("/downloads/film.mkv", It.IsAny<string>(),
                ImportMode.Copy, It.IsAny<Func<string, Task>>(), It.IsAny<CancellationToken>()))
            .Throws(new IOException("Synthetic sharing violation.", unchecked((int)0x80070020)));

        // Act
        var exception = Assert.CatchAsync(() => _import.ExecuteAsync(_job, cancellation.Token));
        _context.ChangeTracker.Clear();
        var stored = await _downloads.GetByIdAsync(_download.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception, Is.Not.Null);
            Assert.That(stored!.Status, Is.EqualTo(DownloadStatus.Faulted));
            Assert.That(stored.FailureReason, Is.EqualTo(DownloadFailureReason.ImportFailed));
            Assert.That(stored.StatusReason, Does.Not.Contain("Waiting for destination capacity"));
            Assert.That(stored.ExternalId, Is.EqualTo("original-client-transfer"));
            Assert.That(await _context.MediaFiles.CountAsync().ConfigureAwait(false), Is.Zero);
            Assert.That(await _context.DownloadScopes.CountAsync().ConfigureAwait(false), Is.EqualTo(1));
        }
    }

    [Test]
    public async Task ExecuteAsync_DoesNotReportCapacityWaiting_WhenTransferIsCancelled()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        _files.Setup(files => files.GetStorageCapacity(It.IsAny<string>()))
            .Returns(new StorageCapacity { VolumeId = "library-volume", AvailableBytes = 100 });
        _files.Setup(files => files.GetFileSize(_destination)).Throws<FileNotFoundException>();
        _files.Setup(files => files.TransferToOwnedPathAsync("/downloads/film.mkv", It.IsAny<string>(),
                ImportMode.Copy, It.IsAny<Func<string, Task>>(), It.IsAny<CancellationToken>()))
            .Callback(() => cancellation.Cancel())
            .Throws(() => new OperationCanceledException(cancellation.Token));

        // Act
        var exception = Assert.CatchAsync(() => _import.ExecuteAsync(_job, cancellation.Token));
        _context.ChangeTracker.Clear();
        var stored = await _downloads.GetByIdAsync(_download.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception, Is.Not.Null);
            Assert.That(stored!.Status, Is.EqualTo(DownloadStatus.Importing));
            Assert.That(stored.FailureReason, Is.Null);
            Assert.That(stored.StatusReason, Does.Not.Contain("Waiting for destination capacity"));
            Assert.That(stored.ExternalId, Is.EqualTo("original-client-transfer"));
            Assert.That(await _context.MediaFiles.CountAsync().ConfigureAwait(false), Is.Zero);
            Assert.That(await _context.DownloadScopes.CountAsync().ConfigureAwait(false), Is.EqualTo(1));
        }
    }

    [TestCase("torrent", "library-volume", 0, DownloadStatus.Importing, 0)]
    [TestCase("torrent", "library-volume", 100, DownloadStatus.Imported, 1)]
    [TestCase("usenet", "library-volume", 0, DownloadStatus.Imported, 1)]
    [TestCase("usenet", "other-volume", 99, DownloadStatus.Importing, 0)]
    [TestCase("usenet", "other-volume", 100, DownloadStatus.Imported, 1)]
    public async Task ExecuteAsync_UsesAdditionalAllocation_WhenTransferModeChanges(
        string protocol, string sourceVolume, long available, DownloadStatus expectedStatus, int expectedFiles)
    {
        // Arrange
        _release.Protocol = protocol;
        _files.Setup(files => files.GetStorageCapacity(It.IsAny<string>()))
            .Returns(new StorageCapacity { VolumeId = "library-volume", AvailableBytes = available });
        _files.Setup(files => files.GetStorageCapacity("/downloads/film.mkv"))
            .Returns(new StorageCapacity { VolumeId = sourceVolume, AvailableBytes = 0 });

        // Act
        await _import.ExecuteAsync(_job).ConfigureAwait(false);
        var stored = await _downloads.GetByIdAsync(_download.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stored!.Status, Is.EqualTo(expectedStatus));
            Assert.That(stored.FailureReason, Is.Null);
            Assert.That(await _context.MediaFiles.CountAsync().ConfigureAwait(false), Is.EqualTo(expectedFiles));
        }
    }

    [TestCase(true, "Required: unknown. Available: 99 bytes.")]
    [TestCase(false, "Required: 100 bytes. Available: unknown.")]
    public async Task ExecuteAsync_KeepsCompletedDownload_WhenCapacityCheckIsUnavailable(bool unknownSize,
        string expected)
    {
        // Arrange
        if (unknownSize)
        {
            _files.Setup(files => files.GetFileSize("/downloads/film.mkv")).Throws<IOException>();
        }
        else
        {
            _files.Setup(files => files.GetStorageCapacity(It.IsAny<string>())).Throws<IOException>();
        }

        // Act
        await _import.ExecuteAsync(_job).ConfigureAwait(false);
        var held = await _downloads.GetByIdAsync(_download.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(held!.Status, Is.EqualTo(DownloadStatus.Importing));
            Assert.That(held.StatusReason, Is.EqualTo(
                "Waiting for destination capacity: the capacity check is unavailable. " + expected));
            Assert.That(held.FailedAt, Is.Null);
            Assert.That(held.FailureReason, Is.Null);
            Assert.That(await _context.DownloadScopes.CountAsync().ConfigureAwait(false), Is.EqualTo(1));
            Assert.That(await _context.MediaFiles.CountAsync().ConfigureAwait(false), Is.Zero);
            _files.Verify(files => files.Copy(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _files.Verify(files => files.Move(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
            _files.Verify(files => files.DeleteFile(It.IsAny<string>()), Times.Never);
        }
    }

    [TestCase("torrent", null, null, 100, DownloadStatus.Imported)]
    [TestCase("torrent", null, null, 99, DownloadStatus.Importing)]
    [TestCase("usenet", null, null, 1000, DownloadStatus.Importing)]
    [TestCase("usenet", "share-a:volume-1", "share-a:volume-1", 0, DownloadStatus.Imported)]
    [TestCase("usenet", "share-a:volume-1", "share-b:volume-1", 99, DownloadStatus.Importing)]
    [TestCase("usenet", "share-a:volume-1", "share-b:volume-1", 100, DownloadStatus.Imported)]
    [TestCase("usenet", "share-a:volume-1", "share-a:volume-2", 99, DownloadStatus.Importing)]
    public async Task ExecuteAsync_DoesNotEquateUnknownOrDifferentVolumes_WhenCapacityIsReadable(
        string protocol, string? destinationVolume, string? sourceVolume, long available, DownloadStatus expected)
    {
        // Arrange
        _release.Protocol = protocol;
        _files.Setup(files => files.GetStorageCapacity(It.IsAny<string>()))
            .Returns(new StorageCapacity { VolumeId = destinationVolume, AvailableBytes = available });
        _files.Setup(files => files.GetStorageCapacity("/downloads/film.mkv"))
            .Returns(new StorageCapacity { VolumeId = sourceVolume, AvailableBytes = 0 });

        // Act
        await _import.ExecuteAsync(_job).ConfigureAwait(false);
        var stored = await _downloads.GetByIdAsync(_download.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stored!.Status, Is.EqualTo(expected));
            Assert.That(stored.FailureReason, Is.Null);
        }
    }

    [Test]
    public async Task ExecuteAsync_PreservesRecovery_WhenHeldAttemptIsCancelled()
    {
        // Arrange
        await _import.ExecuteAsync(_job).ConfigureAwait(false);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync().ConfigureAwait(false);
        _time.Advance(TimeSpan.FromDays(1));

        // Act
        Assert.CatchAsync<OperationCanceledException>(() => _import.ExecuteAsync(_job, cancellation.Token));
        var stuck = await _downloads.ListStuckCandidatesAsync(_time.GetUtcNow().UtcDateTime,
            _time.GetUtcNow().UtcDateTime).ConfigureAwait(false);
        var cleanup = await _downloads.ListCleanupCandidatesAsync().ConfigureAwait(false);
        var markedStuck = await _downloads.MarkStuckAsync(_download.Id, "Film", _download.CreatedAt,
            TimeSpan.FromMinutes(1), _time.GetUtcNow().UtcDateTime, null, null).ConfigureAwait(false);
        var held = await _downloads.GetByIdAsync(_download.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stuck, Is.Empty);
            Assert.That(cleanup, Is.Empty);
            Assert.That(markedStuck, Is.False);
            Assert.That(held!.Status, Is.EqualTo(DownloadStatus.Importing));
            Assert.That(held.FailureReason, Is.Null);
            Assert.That(held.ClientRemovedAt, Is.Null);
            _files.Verify(files => files.Copy(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _files.Verify(files => files.DeleteFile(It.IsAny<string>()), Times.Never);
        }
    }
}
