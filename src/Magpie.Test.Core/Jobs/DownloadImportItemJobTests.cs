using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Content;
using Magpie.Abstractions.Releases;
using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Jobs;
using Magpie.Core.Observability;
using Magpie.Data;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;
using Magpie.Models.Releases;
using Magpie.Models.Storage;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Releases;
using Magpie.Sdk.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs;

[TestFixture]
public sealed class DownloadImportItemJobTests
{
    private Mock<IDownloadRepository> _downloadRepository = null!;
    private Mock<IAuditRecordRepository> _auditRecords = null!;
    private Mock<IMediaFileRepository> _mediaFiles = null!;
    private Mock<IFileSystem> _fileSystem = null!;
    private Mock<IContentFileService> _contentFiles = null!;
    private Mock<IContentTypeRegistry> _registry = null!;
    private Mock<IProgressReporter> _progress = null!;
    private Mock<IContentHierarchyItemRepository> _nodes = null!;
    private Mock<IReleaseEvaluationService> _evaluation = null!;
    private Mock<IContentService> _content = null!;
    private Mock<ILogger<DownloadImportItemJob>> _logger = null!;
    private MagpieDbContext _dbContext = null!;
    private ITransactionManager _transactions = null!;
    private Download _download = null!;
    private Release _release = null!;
    private ContentHierarchyItem _node = null!;
    private MediaFile _priorFile = null!;
    private RootFolder _rootFolder = null!;
    private IJob _subject = null!;
    private JobContext _jobContext = null!;

    [SetUp]
    public void SetUp()
    {
        _downloadRepository = new Mock<IDownloadRepository> { CallBase = true };
        _dbContext = new MagpieDbContext(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite("Data Source=:memory:").Options);
        _transactions = new TransactionManager(_dbContext, NullLogger<TransactionManager>.Instance);
        _auditRecords = new Mock<IAuditRecordRepository> { CallBase = true };
        _mediaFiles = new Mock<IMediaFileRepository> { CallBase = true };
        _fileSystem = new Mock<IFileSystem>();
        _fileSystem.Setup(files => files.GetFileIdentity(It.IsAny<string>())).Returns("owned-file");
        _fileSystem.Setup(files => files.GetFileVersion(It.IsAny<string>())).Returns("owned-version");
        _fileSystem.Setup(files => files.GetFileSize(It.Is<string>(path => Path.GetFileName(path) == "payload")))
            .Throws<FileNotFoundException>();
        _fileSystem.Setup(files => files.TransferToOwnedPathAsync(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<ImportMode>(), It.IsAny<Func<string, Task>>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, ImportMode, Func<string, Task>, CancellationToken>(
                (_, _, _, created, _) => created("owned-file"));
        _fileSystem.Setup(files => files.TransferToOwnedPathAsync(It.IsAny<string>(), It.IsAny<string>(),
                ImportMode.Move, It.IsAny<Func<string, Task>>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, ImportMode, Func<string, Task>, CancellationToken>(
                async (source, _, _, created, _) =>
            {
                await created("owned-file").ConfigureAwait(false);
                _fileSystem.Setup(files => files.GetFileSize(source)).Throws<FileNotFoundException>();
            });
        _fileSystem.Setup(fileSystem => fileSystem.GetStorageCapacity(It.IsAny<string>()))
            .Returns(new StorageCapacity { VolumeId = "library", AvailableBytes = 1000 });
        _contentFiles = new Mock<IContentFileService>();
        _progress = new Mock<IProgressReporter>();
        _node = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ContentType = "type-a",
            Level = "leaf",
            Slug = "item",
            Name = "Item",
            EffectiveRootFolderId = Guid.NewGuid()
        };
        var contentItem = new ContentItem
        {
            Id = Guid.NewGuid(),
            ContentHierarchyItemId = _node.Id,
            Fulfillment = FulfillmentStatus.Upgradable
        };
        _release = new Release { Id = Guid.NewGuid(), Title = "Release", Protocol = "protocol-a" };
        _download = new Download
        {
            Id = new Guid("3d7b1e88-2a4c-4f51-9a6d-0b8e5c4f2a19"),
            Title = "Release",
            ExternalId = "external-id",
            Status = DownloadStatus.Downloaded,
            ReleaseId = _release.Id,
            OutputPath = "/downloads/new.mkv"
        };
        _downloadRepository.Setup(repository => repository.GetByIdAsync(
                _download.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_download);
        _downloadRepository.Setup(repository => repository.MarkImportingAsync(
                _download.Id, It.IsAny<DateTime>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var releases = new Mock<IReleaseRepository> { CallBase = true };
        releases.Setup(repository => repository.GetByIdAsync(_release.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_release);
        _nodes = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        _nodes.Setup(repository => repository.GetLineageAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([_node]);
        var scopes = new Mock<IDownloadScopeRepository> { CallBase = true };
        scopes.Setup(repository => repository.QueryAsync(
                It.IsAny<DownloadScopeQuery>(), It.IsAny<PagingParameter>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<DownloadScope>
            {
                Items = [new DownloadScope { DownloadId = _download.Id, ContentHierarchyItemId = _node.Id }]
            });
        var roots = new Mock<IRootFolderRepository> { CallBase = true };
        _rootFolder = new RootFolder
        {
            Name = "Library",
            Path = Path.GetFullPath("/library"),
            ContentType = "type-a",
            UpgradeFileHandling = UpgradeFileHandling.Replace
        };
        roots.Setup(repository => repository.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_rootFolder);
        _content = new Mock<IContentService>();
        _content.Setup(service => service.GetOrCreateContentItemAsync(
                _node.Id, It.IsAny<DateTime>(), It.IsAny<Guid?>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(contentItem);
        var contentRepository = new Mock<IContentRepository> { CallBase = true };
        contentRepository.Setup(repository => repository.QueryAsync(
                It.IsAny<ContentItemQuery>(), It.IsAny<PagingParameter>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentItem>());
        var mappings = new Mock<IRemotePathMappingRepository> { CallBase = true };
        mappings.Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var blocked = new Mock<IBlockedFileTypeRepository> { CallBase = true };
        blocked.Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _fileSystem.Setup(fileSystem => fileSystem.FileExists(It.IsAny<string>())).Returns(true);
        _fileSystem.Setup(fileSystem => fileSystem.FileExists(new FileInfo("/library/new.mkv").FullName))
            .Returns(false);
        _fileSystem.Setup(fileSystem => fileSystem.MatchesAnyFileTypeAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _contentFiles.Setup(service => service.PreviewRenameAsync(
                _node.Id, It.IsAny<FileInfo>(), It.IsAny<MediaInfo?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentRename
            {
                Original = new FileInfo("/downloads/new.mkv"),
                Rename = new FileInfo("/library/new.mkv"),
                Item = _node
            });
        _evaluation = new Mock<IReleaseEvaluationService>();
        _evaluation.Setup(service => service.ResolveScopeAsync(
                _release, It.IsAny<IReadOnlyList<ContentFact>>(),
                _node.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReleaseScopeResolution([], []));
        _priorFile = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = contentItem.Id,
            FilePath = "/library/old.mkv",
            FileKind = "Video"
        };
        _mediaFiles.Setup(repository => repository.QueryAsync(
                It.IsAny<MediaFileQuery>(), It.IsAny<PagingParameter>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<MediaFile> { Items = [_priorFile] });
        _registry = new Mock<IContentTypeRegistry>();
        _logger = new Mock<ILogger<DownloadImportItemJob>>();
        _subject = new DownloadImportItemJob(
            _auditRecords.Object,
            _downloadRepository.Object,
            roots.Object,
            _nodes.Object,
            _mediaFiles.Object,
            releases.Object,
            Mock.Of<IReleaseScopeRepository>(),
            scopes.Object,
            Mock.Of<IJobExecutionRepository>(),
            _fileSystem.Object,
            _registry.Object,
            _contentFiles.Object,
            blocked.Object,
            mappings.Object,
            Mock.Of<IDownloadClientRepository>(),
            Mock.Of<IRemotePathMappingService>(),
            _transactions,
            Mock.Of<IQualityService>(),
            contentRepository.Object,
            _content.Object,
            Mock.Of<ILibraryImportService>(),
            Mock.Of<IEventBus>(),
            _evaluation.Object,
            TimeProvider.System,
            _logger.Object,
            _progress.Object);

        _jobContext = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: Guid.NewGuid(),
            ManualTriggerUserId: null,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["downloadId"] = "3d7b1e88-2a4c-4f51-9a6d-0b8e5c4f2a19"
            },
            Event: null);
    }

    [TearDown]
    public void TearDown()
    {
        _dbContext.Dispose();
    }

    [TestCase("library")]
    [TestCase(null)]
    public async Task ExecuteAsync_HoldsBeforeTransfer_WhenCombinedFilesExceedAvailableCapacity(string? volumeId)
    {
        // Arrange
        _release.Protocol = "torrent";
        _fileSystem.Setup(fileSystem => fileSystem.DirectoryExists("/downloads/new.mkv")).Returns(true);
        _fileSystem.Setup(fileSystem => fileSystem.EnumerateFiles("/downloads/new.mkv"))
            .Returns(["/downloads/first.mkv", "/downloads/second.mkv"]);
        _fileSystem.Setup(fileSystem => fileSystem.GetFileSize(It.IsAny<string>())).Returns(100);
        _fileSystem.Setup(fileSystem => fileSystem.GetStorageCapacity(It.IsAny<string>()))
            .Returns(new StorageCapacity { VolumeId = volumeId, AvailableBytes = 150 });
        _fileSystem.Setup(fileSystem => fileSystem.FileExists(new FileInfo("/library/first.mkv").FullName))
            .Returns(false);
        _fileSystem.Setup(fileSystem => fileSystem.FileExists(new FileInfo("/library/second.mkv").FullName))
            .Returns(false);
        _contentFiles.Setup(service => service.PreviewRenameAsync(_node.Id,
                It.Is<FileInfo>(file => file.Name == "first.mkv"),
                It.IsAny<MediaInfo?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentRename
            {
                Original = new FileInfo("/downloads/first.mkv"),
                Rename = new FileInfo("/library/first.mkv"),
                Item = _node
            });
        _contentFiles.Setup(service => service.PreviewRenameAsync(_node.Id,
                It.Is<FileInfo>(file => file.Name == "second.mkv"),
                It.IsAny<MediaInfo?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentRename
            {
                Original = new FileInfo("/downloads/second.mkv"),
                Rename = new FileInfo("/library/second.mkv"),
                Item = _node
            });

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _downloadRepository.Verify(repository => repository.SetImportCapacityReasonAsync(_download.Id,
            "Waiting for destination capacity: insufficient free space. Required: 200 bytes. Available: 150 bytes.",
            It.IsAny<DateTime>(), null, It.IsAny<CancellationToken>()), Times.Once);
        _fileSystem.Verify(fileSystem => fileSystem.Copy(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ExecuteAsync_KeepsExistingFile_WhenOmittedPolicyResolvesToSkip(bool recovering)
    {
        // Arrange
        _rootFolder.DefaultImportCollisionMode = recovering ? ImportCollisionMode.Fail : ImportCollisionMode.Skip;
        _download.Status = recovering ? DownloadStatus.Importing : DownloadStatus.Downloaded;
        _download.ImportCollisionMode = recovering ? ImportCollisionMode.Skip : null;
        _fileSystem.Setup(fileSystem => fileSystem.FileExists(new FileInfo("/library/new.mkv").FullName)).Returns(true);

        // Act
        var error = Assert.CatchAsync<InvalidOperationException>(() => _subject.ExecuteAsync(_jobContext));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(error!.Message, Does.Contain("every planned operation collided under Skip collision mode"));
            _downloadRepository.Verify(repository => repository.MarkFailedAsync(
                _download.Id, DownloadFailureReason.NoUsableContent, It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Once);
            _fileSystem.Verify(fileSystem => fileSystem.Move(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _fileSystem.Verify(fileSystem => fileSystem.DeleteFile(It.IsAny<string>()), Times.Never);
            _mediaFiles.Verify(repository => repository.CreateAsync(It.IsAny<MediaFile>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Never);
            _downloadRepository.Verify(repository => repository.MarkImportedAsync(
                _download.Id, It.IsAny<Guid?>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_DoesNotLogTheStagingDirectory_WhenItIsAlreadyGone()
    {
        // Arrange
        _fileSystem.Setup(fileSystem => fileSystem.DeleteEmptyDirectory(It.IsAny<string>()))
            .Throws<DirectoryNotFoundException>();

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _logger.Verify(
            logger => logger.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsNotNull<DirectoryNotFoundException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_DoesNotRepeatFileOperations_WhenRecoveredImportIsReplayed()
    {
        // Arrange
        _download.Status = DownloadStatus.Importing;
        _download.ImportCollisionMode = ImportCollisionMode.Replace;
        _downloadRepository.Setup(repository => repository.MarkImportedAsync(
                _download.Id, It.IsAny<Guid?>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, Guid?, int, DateTime, DateTime, Guid?, CancellationToken>((_, _, _, _, _, _, _) =>
                _download.Status = DownloadStatus.Imported).Returns(Task.CompletedTask);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _fileSystem.Verify(fileSystem => fileSystem.Move(It.IsAny<string>(),
                It.Is<string>(path => path.Replace('\\', '/').EndsWith("/library/new.mkv", StringComparison.Ordinal))),
                Times.Once);
            _mediaFiles.Verify(repository => repository.CreateAsync(It.IsAny<MediaFile>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Once);
            _fileSystem.Verify(fileSystem => fileSystem.DeleteFile("/library/old.mkv"), Times.Once);
        }
    }

    [TestCase("fail", ImportCollisionMode.Fail)]
    [TestCase("skip", ImportCollisionMode.Skip)]
    [TestCase("replace", ImportCollisionMode.Replace)]
    public async Task ExecuteAsync_StoresExplicitPolicyBeforeFileOperations_WhenImportStarts(
        string parameter, ImportCollisionMode mode)
    {
        // Arrange
        _jobContext = _jobContext with
        {
            Parameters = new Dictionary<string, string>(_jobContext.Parameters) { ["collisionMode"] = parameter }
        };
        _downloadRepository.Setup(repository => repository.MarkImportingAsync(_download.Id,
                It.IsAny<DateTime>(), It.IsAny<Guid?>(), mode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _downloadRepository.Verify(repository => repository.MarkImportingAsync(_download.Id,
                It.IsAny<DateTime>(), It.IsAny<Guid?>(), mode, It.IsAny<CancellationToken>()), Times.Once);
            _fileSystem.VerifyNoOtherCalls();
            _mediaFiles.VerifyNoOtherCalls();
        }
    }

    [Test]
    public async Task ExecuteAsync_StoresRecoveryPolicyBeforeFileOperations_WhenResumingAnOldImport()
    {
        // Arrange
        _download.Status = DownloadStatus.Importing;
        _jobContext = _jobContext with
        {
            Parameters = new Dictionary<string, string>(_jobContext.Parameters) { ["collisionMode"] = "replace" }
        };
        _downloadRepository.Setup(repository => repository.SetImportCollisionModeAsync(_download.Id,
            ImportCollisionMode.Replace, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _downloadRepository.Verify(repository => repository.SetImportCollisionModeAsync(_download.Id,
                ImportCollisionMode.Replace, It.IsAny<CancellationToken>()), Times.Once);
            _fileSystem.VerifyNoOtherCalls();
            _mediaFiles.VerifyNoOtherCalls();
        }
    }

    [Test]
    public void Spec_DoesNotPopulateCollisionDefault_WhenPolicyShouldBeInherited()
    {
        // Arrange
        var parameter = DownloadImportItemJob.Spec.Parameters.Single(parameter => parameter.Name == "collisionMode");

        // Act
        var value = parameter.DefaultValue;

        // Assert
        Assert.That(value, Is.Null);
    }

    [Test]
    public async Task ExecuteAsync_DoesNotStartASecondImport_WhenTheDownloadIsAlreadyImported()
    {
        // Arrange
        _downloadRepository
            .Setup(repository => repository.GetByIdAsync(
                new Guid("3d7b1e88-2a4c-4f51-9a6d-0b8e5c4f2a19"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Download
            {
                Id = new Guid("3d7b1e88-2a4c-4f51-9a6d-0b8e5c4f2a19"),
                Status = DownloadStatus.Imported,
                Title = "Some release",
                ExternalId = "abc",
                DownloadClientId = Guid.NewGuid(),
                CreatedAt = new DateTime(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc)
            });

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _downloadRepository.Verify(
                repository => repository.GetByIdAsync(
                    new Guid("3d7b1e88-2a4c-4f51-9a6d-0b8e5c4f2a19"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _downloadRepository.Verify(
                repository => repository.MarkImportingAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_LeavesFilesUntouched_WhenCancellationWinsTheImportTransition()
    {
        // Arrange
        _downloadRepository.Setup(repository => repository.MarkImportingAsync(
                _download.Id, It.IsAny<DateTime>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _fileSystem.VerifyNoOtherCalls();
            _mediaFiles.VerifyNoOtherCalls();
            _contentFiles.VerifyNoOtherCalls();
            _auditRecords.VerifyNoOtherCalls();
            _progress.Verify(progress => progress.Report(
                1, "The download status changed before import could start."), Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_LeavesFilesUntouchedWithoutFailure_WhenCompletedLoserWasCancelled()
    {
        // Arrange
        _download.Status = DownloadStatus.Cancelled;

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _fileSystem.VerifyNoOtherCalls();
            _mediaFiles.VerifyNoOtherCalls();
            _contentFiles.VerifyNoOtherCalls();
            _auditRecords.VerifyNoOtherCalls();
            _downloadRepository.Verify(repository => repository.MarkImportingAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
            _progress.Verify(progress => progress.Report(
                1, "The download was cancelled. No files were imported."), Times.Once);
        }
    }

    [Test]
    public void ExecuteAsync_ReportsPartialRollback_WhenCancellationRecoveryCannotRestoreTransferredFile()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var activities = new List<Activity>();
        using var trace = new Activity("test").Start();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == DownloadTelemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (activity.TraceId == trace.TraceId)
                {
                    activities.Add(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);
        _fileSystem.Setup(fileSystem => fileSystem.DirectoryExists("/downloads/new.mkv")).Returns(true);
        _fileSystem.Setup(fileSystem => fileSystem.EnumerateFiles("/downloads/new.mkv"))
            .Returns(["/downloads/a.mkv", "/downloads/b.mkv"]);
        _fileSystem.Setup(fileSystem => fileSystem.FileExists(new FileInfo("/library/a.mkv").FullName))
            .Returns(false);
        _fileSystem.Setup(fileSystem => fileSystem.FileExists(new FileInfo("/library/b.mkv").FullName))
            .Returns(false);
        _contentFiles.SetupSequence(service => service.PreviewRenameAsync(
                _node.Id, It.IsAny<FileInfo>(), It.IsAny<MediaInfo?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentRename
            {
                Original = new FileInfo("/downloads/a.mkv"),
                Rename = new FileInfo("/library/a.mkv"),
                Item = _node
            })
            .ReturnsAsync(new ContentRename
            {
                Original = new FileInfo("/downloads/b.mkv"),
                Rename = new FileInfo("/library/b.mkv"),
                Item = _node
            });
        _fileSystem.Setup(fileSystem => fileSystem.Move(
                It.IsAny<string>(),
                new FileInfo("/library/a.mkv").FullName,
                false))
            .Callback(cancellation.Cancel);
        _fileSystem.Setup(fileSystem => fileSystem.Move(
                new FileInfo("/library/a.mkv").FullName,
                "/downloads/a.mkv",
                false))
            .Throws(new IOException("The source directory is read-only."));

        // Act
        var error = Assert.CatchAsync<OperationCanceledException>(
            () => _subject.ExecuteAsync(_jobContext, cancellation.Token));

        // Assert
        var transfer = activities.Single(activity => activity.OperationName == "Transfer");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(error!.CancellationToken, Is.EqualTo(cancellation.Token));
            Assert.That(
                transfer.GetTagItem("magpie.import.outcome"),
                Is.EqualTo("partially-reverted"));
            _fileSystem.Verify(fileSystem => fileSystem.Move(
                new FileInfo("/library/a.mkv").FullName,
                "/downloads/a.mkv",
                false), Times.Once);
        }
    }

    [Test]
    public void ExecuteAsync_ReportsCompleteRollback_WhenTransferFailsAfterOneFile()
    {
        // Arrange
        var activities = new List<Activity>();
        using var trace = new Activity("test").Start();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == DownloadTelemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (activity.TraceId == trace.TraceId)
                {
                    activities.Add(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);
        _fileSystem.Setup(fileSystem => fileSystem.DirectoryExists("/downloads/new.mkv")).Returns(true);
        _fileSystem.Setup(fileSystem => fileSystem.EnumerateFiles("/downloads/new.mkv"))
            .Returns(["/downloads/a.mkv", "/downloads/b.mkv"]);
        _fileSystem.Setup(fileSystem => fileSystem.FileExists(new FileInfo("/library/a.mkv").FullName))
            .Returns(false);
        _fileSystem.Setup(fileSystem => fileSystem.FileExists(new FileInfo("/library/b.mkv").FullName))
            .Returns(false);
        _contentFiles.SetupSequence(service => service.PreviewRenameAsync(
                _node.Id, It.IsAny<FileInfo>(), It.IsAny<MediaInfo?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentRename
            {
                Original = new FileInfo("/downloads/a.mkv"),
                Rename = new FileInfo("/library/a.mkv"),
                Item = _node
            })
            .ReturnsAsync(new ContentRename
            {
                Original = new FileInfo("/downloads/b.mkv"),
                Rename = new FileInfo("/library/b.mkv"),
                Item = _node
            });
        var original = new IOException("The destination is unavailable.");
        _fileSystem.Setup(fileSystem => fileSystem.TransferToOwnedPathAsync("/downloads/b.mkv", It.IsAny<string>(),
                ImportMode.Move, It.IsAny<Func<string, Task>>(), It.IsAny<CancellationToken>()))
            .Throws(original);
        _fileSystem.Setup(files => files.GetFileSize(new FileInfo("/library/b.mkv").FullName))
            .Throws<FileNotFoundException>();

        // Act
        var error = Assert.ThrowsAsync<InvalidOperationException>(() => _subject.ExecuteAsync(_jobContext));

        // Assert
        var transfer = activities.Single(activity => activity.OperationName == "Transfer");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(error!.InnerException, Is.SameAs(original));
            Assert.That(error.Message, Does.Contain("Rollback outcome: reverted."));
            Assert.That(error.Message, Does.Contain("Reverted 1 of 1 transferred file(s)"));
            Assert.That(transfer.Status, Is.EqualTo(ActivityStatusCode.Error));
            Assert.That(transfer.StatusDescription, Is.EqualTo("The destination is unavailable."));
            Assert.That(transfer.GetTagItem("error.type"), Is.EqualTo("System.IO.IOException"));
            Assert.That(transfer.GetTagItem("magpie.import.outcome"), Is.EqualTo("reverted"));
            Assert.That(transfer.GetTagItem("magpie.import.rollback_transfer_count"), Is.EqualTo(1));
            Assert.That(transfer.GetTagItem("magpie.import.rollback_reverted_transfer_count"), Is.EqualTo(1));
            _fileSystem.Verify(fileSystem => fileSystem.Move(
                new FileInfo("/library/a.mkv").FullName,
                "/downloads/a.mkv",
                false), Times.Once);
        }
    }

    [Test]
    public void ExecuteAsync_ReportsPartialRollback_WhenCopiedFileCannotBeDeleted()
    {
        // Arrange
        _release.Protocol = "torrent";
        var activities = new List<Activity>();
        using var trace = new Activity("test").Start();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == DownloadTelemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (activity.TraceId == trace.TraceId)
                {
                    activities.Add(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);
        _fileSystem.Setup(fileSystem => fileSystem.DirectoryExists("/downloads/new.mkv")).Returns(true);
        _fileSystem.Setup(fileSystem => fileSystem.EnumerateFiles("/downloads/new.mkv"))
            .Returns(["/downloads/a.mkv", "/downloads/b.mkv"]);
        _fileSystem.Setup(fileSystem => fileSystem.FileExists(new FileInfo("/library/a.mkv").FullName))
            .Returns(false);
        _fileSystem.Setup(fileSystem => fileSystem.FileExists(new FileInfo("/library/b.mkv").FullName))
            .Returns(false);
        _contentFiles.SetupSequence(service => service.PreviewRenameAsync(
                _node.Id, It.IsAny<FileInfo>(), It.IsAny<MediaInfo?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentRename
            {
                Original = new FileInfo("/downloads/a.mkv"),
                Rename = new FileInfo("/library/a.mkv"),
                Item = _node
            })
            .ReturnsAsync(new ContentRename
            {
                Original = new FileInfo("/downloads/b.mkv"),
                Rename = new FileInfo("/library/b.mkv"),
                Item = _node
            });
        var original = new IOException("The destination is unavailable.");
        _fileSystem.Setup(fileSystem => fileSystem.TransferToOwnedPathAsync("/downloads/b.mkv", It.IsAny<string>(),
                ImportMode.Copy, It.IsAny<Func<string, Task>>(), It.IsAny<CancellationToken>()))
            .Throws(original);
        _fileSystem.Setup(files => files.GetFileSize(new FileInfo("/library/b.mkv").FullName))
            .Throws<FileNotFoundException>();
        _fileSystem.Setup(fileSystem => fileSystem.DeleteFile(new FileInfo("/library/a.mkv").FullName))
            .Throws(new IOException("The copied file is read-only."));

        // Act
        var error = Assert.ThrowsAsync<InvalidOperationException>(() => _subject.ExecuteAsync(_jobContext));

        // Assert
        var transfer = activities.Single(activity => activity.OperationName == "Transfer");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(error!.InnerException, Is.SameAs(original));
            Assert.That(error.Message, Does.Contain("Rollback outcome: partially-reverted."));
            Assert.That(transfer.GetTagItem("magpie.import.outcome"), Is.EqualTo("partially-reverted"));
            Assert.That(transfer.GetTagItem("magpie.import.rollback_transfer_count"), Is.EqualTo(1));
            Assert.That(transfer.GetTagItem("magpie.import.rollback_reverted_transfer_count"), Is.EqualTo(0));
            _fileSystem.Verify(fileSystem => fileSystem.TransferToOwnedPathAsync("/downloads/a.mkv",
                It.IsAny<string>(), ImportMode.Copy, It.IsAny<Func<string, Task>>(),
                It.IsAny<CancellationToken>()), Times.Once);
            _fileSystem.Verify(fileSystem => fileSystem.DeleteFile(
                new FileInfo("/library/a.mkv").FullName), Times.Once);
        }
    }

    [Test]
    public void ExecuteAsync_AttemptsEveryRollback_WhenMultipleTransferredFilesCannotBeRestored()
    {
        // Arrange
        var activities = new List<Activity>();
        using var trace = new Activity("test").Start();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == DownloadTelemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (activity.TraceId == trace.TraceId)
                {
                    activities.Add(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);
        _fileSystem.Setup(fileSystem => fileSystem.DirectoryExists("/downloads/new.mkv")).Returns(true);
        _fileSystem.Setup(fileSystem => fileSystem.EnumerateFiles("/downloads/new.mkv"))
            .Returns(["/downloads/a.mkv", "/downloads/b.mkv", "/downloads/c.mkv"]);
        _fileSystem.Setup(fileSystem => fileSystem.FileExists(It.Is<string>(path =>
                path.EndsWith("a.mkv", StringComparison.Ordinal) ||
                path.EndsWith("b.mkv", StringComparison.Ordinal) ||
                path.EndsWith("c.mkv", StringComparison.Ordinal))))
            .Returns(false);
        _contentFiles.SetupSequence(service => service.PreviewRenameAsync(
                _node.Id, It.IsAny<FileInfo>(), It.IsAny<MediaInfo?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentRename
            {
                Original = new FileInfo("/downloads/a.mkv"),
                Rename = new FileInfo("/library/a.mkv"),
                Item = _node
            })
            .ReturnsAsync(new ContentRename
            {
                Original = new FileInfo("/downloads/b.mkv"),
                Rename = new FileInfo("/library/b.mkv"),
                Item = _node
            })
            .ReturnsAsync(new ContentRename
            {
                Original = new FileInfo("/downloads/c.mkv"),
                Rename = new FileInfo("/library/c.mkv"),
                Item = _node
            });
        var original = new IOException("The destination is unavailable.");
        _fileSystem.Setup(fileSystem => fileSystem.TransferToOwnedPathAsync("/downloads/c.mkv", It.IsAny<string>(),
                ImportMode.Move, It.IsAny<Func<string, Task>>(), It.IsAny<CancellationToken>()))
            .Throws(original);
        _fileSystem.Setup(files => files.GetFileSize(new FileInfo("/library/c.mkv").FullName))
            .Throws<FileNotFoundException>();
        _fileSystem.Setup(fileSystem => fileSystem.Move(
                new FileInfo("/library/a.mkv").FullName,
                "/downloads/a.mkv",
                false))
            .Throws(new IOException("The first source directory is read-only."));
        _fileSystem.Setup(fileSystem => fileSystem.Move(
                new FileInfo("/library/b.mkv").FullName,
                "/downloads/b.mkv",
                false))
            .Throws(new IOException("The second source directory is read-only."));

        // Act
        var error = Assert.ThrowsAsync<InvalidOperationException>(() => _subject.ExecuteAsync(_jobContext));

        // Assert
        var transfer = activities.Single(activity => activity.OperationName == "Transfer");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(error!.InnerException, Is.SameAs(original));
            Assert.That(error.Message, Does.Contain("Rollback outcome: partially-reverted."));
            Assert.That(error.Message, Does.Contain("Reverted 0 of 2 transferred file(s)"));
            Assert.That(transfer.GetTagItem("magpie.import.outcome"), Is.EqualTo("partially-reverted"));
            _fileSystem.Verify(fileSystem => fileSystem.Move(
                new FileInfo("/library/a.mkv").FullName,
                "/downloads/a.mkv",
                false), Times.Once);
            _fileSystem.Verify(fileSystem => fileSystem.Move(
                new FileInfo("/library/b.mkv").FullName,
                "/downloads/b.mkv",
                false), Times.Once);
        }
    }

    [TestCase("Import")]
    [TestCase("ImportUpgradable")]
    public void ExecuteAsync_PreservesSupersededFile_WhenImportAuditFails(string operation)
    {
        // Arrange
        var destination = new FileInfo("/library/new.mkv").FullName;
        _fileSystem.Setup(fileSystem => fileSystem.Move(
                It.Is<string>(path => Path.GetFileName(path) == "payload"), destination, false))
            .Callback(() =>
            {
                _fileSystem.Setup(fileSystem => fileSystem.FileExists("/downloads/new.mkv")).Returns(false);
                _fileSystem.Setup(fileSystem => fileSystem.FileExists(destination)).Returns(true);
            });
        _auditRecords.Setup(repository => repository.CreateAsync(
                It.Is<AuditRecord>(record => record.Operation == operation), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("The audit write failed."));

        // Act
        var exception = Assert.ThrowsAsync<InvalidOperationException>(() => _subject.ExecuteAsync(_jobContext));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception.Message, Is.EqualTo("The audit write failed."));
            _mediaFiles.Verify(repository => repository.DeleteAsync(
                _priorFile.Id, null, It.IsAny<CancellationToken>()), Times.Never);
            _fileSystem.Verify(fileSystem => fileSystem.DeleteFile("/library/old.mkv"), Times.Never);
            _fileSystem.Verify(fileSystem => fileSystem.Move(
                It.Is<string>(path => path.Replace('\\', '/').EndsWith("/library/new.mkv", StringComparison.Ordinal)),
                "/downloads/new.mkv"), Times.Once);
        }
    }

    [Test]
    public void ExecuteAsync_PreservesPersistenceFailure_WhenReplaceBackupCannotBeRestored()
    {
        // Arrange
        var activities = new List<Activity>();
        using var trace = new Activity("test").Start();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == DownloadTelemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (activity.TraceId == trace.TraceId)
                {
                    activities.Add(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);
        _jobContext = _jobContext with
        {
            Parameters = new Dictionary<string, string>(_jobContext.Parameters) { ["collisionMode"] = "replace" }
        };
        _downloadRepository.Setup(repository => repository.MarkImportingAsync(
                _download.Id,
                It.IsAny<DateTime>(),
                It.IsAny<Guid?>(),
                ImportCollisionMode.Replace,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var destination = new FileInfo("/library/new.mkv").FullName;
        var backup = $"{destination}.magpie-replace-{_download.Id:N}.orig";
        _fileSystem.Setup(fileSystem => fileSystem.FileExists(destination)).Returns(true);
        var original = new InvalidOperationException("The audit write failed.");
        _auditRecords.Setup(repository => repository.CreateAsync(
                It.Is<AuditRecord>(record => record.Operation.StartsWith("Import", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(original);
        _fileSystem.Setup(fileSystem => fileSystem.Move(backup, destination, false))
            .Throws(new IOException("The backup volume is unavailable."));
        _fileSystem.Setup(fileSystem => fileSystem.Move(
                It.Is<string>(path => Path.GetFileName(path) == "payload"), destination, false))
            .Callback(() => _fileSystem.Setup(fileSystem => fileSystem.FileExists("/downloads/new.mkv"))
                .Returns(false));
        _fileSystem.Setup(fileSystem => fileSystem.Move(destination, "/downloads/new.mkv", false))
            .Callback(() => _fileSystem.Setup(fileSystem => fileSystem.GetFileSize(destination))
                .Throws<FileNotFoundException>());

        // Act
        var error = Assert.CatchAsync<InvalidOperationException>(() => _subject.ExecuteAsync(_jobContext));

        // Assert
        var import = activities.Single(activity => activity.OperationName == "DownloadImport");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(error!.InnerException, Is.SameAs(original));
            Assert.That(import.GetTagItem("magpie.import.rollback_outcome"), Is.EqualTo("partially-reverted"));
            Assert.That(import.GetTagItem("magpie.import.rollback_transfer_count"), Is.EqualTo(1));
            Assert.That(import.GetTagItem("magpie.import.rollback_reverted_transfer_count"), Is.EqualTo(1));
            Assert.That(import.GetTagItem("magpie.import.rollback_replace_backup_count"), Is.EqualTo(1));
            Assert.That(import.GetTagItem("magpie.import.rollback_restored_replace_backup_count"), Is.EqualTo(0));
            _fileSystem.Verify(fileSystem => fileSystem.Move(destination, "/downloads/new.mkv", false), Times.Once);
            _fileSystem.Verify(fileSystem => fileSystem.Move(backup, destination, false), Times.Once);
        }
    }

    [TestCase(MonitoringMode.Monitored)]
    [TestCase(MonitoringMode.Shotgun)]
    public async Task ExecuteAsync_DeletesSupersededFile_WhenOutermostTransactionCommits(MonitoringMode monitoring)
    {
        // Arrange
        _node.EffectiveMonitoring = monitoring;
        _mediaFiles.Setup(repository => repository.DeleteAsync(_priorFile.Id, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _transactions.ExecuteWithTransactionAsync(async token =>
        {
            await _subject.ExecuteAsync(_jobContext, token).ConfigureAwait(false);
        }).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _fileSystem.Verify(fileSystem => fileSystem.DeleteFile("/library/old.mkv"), Times.Once);
            _mediaFiles.Verify(repository => repository.DeleteAsync(
                _priorFile.Id, null, It.IsAny<CancellationToken>()), Times.Once);
            _auditRecords.Verify(repository => repository.CreateAsync(
                It.Is<AuditRecord>(record => record.Operation == "SupersedeUpgrade" &&
                    record.EntityId == "3d7b1e88-2a4c-4f51-9a6d-0b8e5c4f2a19" &&
                    record.Detail == "Removed previous file /library/old.mkv (0) after importing a quality upgrade " +
                        "for Release."),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Test]
    public void ExecuteAsync_PreservesSupersededFile_WhenOutermostTransactionRollsBack()
    {
        // Arrange
        // Act
        var exception = Assert.ThrowsAsync<InvalidOperationException>(() =>
            _transactions.ExecuteWithTransactionAsync(async token =>
            {
                await _subject.ExecuteAsync(_jobContext, token).ConfigureAwait(false);
                throw new InvalidOperationException("The outer transaction failed.");
            }));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception.Message, Is.EqualTo("The outer transaction failed."));
            _fileSystem.Verify(fileSystem => fileSystem.DeleteFile("/library/old.mkv"), Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_WritesOneAuditRecordForThePlannedOperation_WhenDryRunIsRequested()
    {
        // Arrange
        var context = _jobContext with
        {
            Parameters = new Dictionary<string, string>(_jobContext.Parameters) { ["dryRun"] = "true" }
        };

        // Act
        await _subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _auditRecords.Verify(repository => repository.CreateAsync(
                It.Is<AuditRecord>(record =>
                    record.Category == "Integrations" &&
                    record.Operation == "ImportDryRun" &&
                    record.EntityType == "Download" &&
                    record.EntityId == "3d7b1e88-2a4c-4f51-9a6d-0b8e5c4f2a19" &&
                    record.Detail!.StartsWith("Dry run: move /downloads/new.mkv -> ", StringComparison.Ordinal) &&
                    record.Detail!.Replace('\\', '/').EndsWith("/library/new.mkv", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()), Times.Once);
            _progress.Verify(progress => progress.Report(
                1, "Dry run: move 1 file(s). The audit history of the download holds the plan."), Times.Once);
            _fileSystem.Verify(fileSystem => fileSystem.Move(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _fileSystem.Verify(fileSystem => fileSystem.Copy(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _mediaFiles.Verify(repository => repository.CreateAsync(
                It.IsAny<MediaFile>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
            _downloadRepository.Verify(repository => repository.MarkImportingAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [TestCase("Movie.en.srt")]
    [TestCase("Movie.en.srt", "Movie.fr.srt")]
    [TestCase("Movie.en.forced.srt", "Movie.en.sdh.srt", "Movie.en.cc.srt", "Movie.en.srt")]
    public async Task ExecuteAsync_KeepsSidecarSuffix_WhenMaskRenamesPrimary(params string[] sidecars)
    {
        // Arrange
        var context = ArrangeSidecarDryRun("Movie.mkv", "/library/Masked (1999).mkv", sidecars);

        // Act
        await _subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            foreach (var sidecar in sidecars)
            {
                VerifyPlannedMove(sidecar, "Masked (1999)" + sidecar["Movie".Length..], Times.Once());
            }

            _contentFiles.Verify(service => service.PreviewRenameAsync(
                _node.Id, It.IsAny<FileInfo>(), It.IsAny<MediaInfo?>(), It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_KeepsSidecarSuffix_WhenAPathProviderNamesThePrimaryWithoutAMask()
    {
        // Arrange
        var context = ArrangeSidecarDryRun(
            "Show.S01E01.mkv",
            "/library/Show - S01E01 - Title.mkv",
            ["Show.S01E01.en.srt"]);

        // Act
        await _subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        VerifyPlannedMove("Show.S01E01.en.srt", "Show - S01E01 - Title.en.srt", Times.Once());
    }

    [Test]
    public async Task ExecuteAsync_LeavesUnrelatedSubtitles_WhenOnlyAFlagOrLanguageLooksAlike()
    {
        // Arrange
        var context = ArrangeSidecarDryRun(
            "Movie.mkv",
            "/library/Masked (1999).mkv",
            ["Movie.en.forced.srt", "Other.en.forced.srt", "Movie.en.forcedx.srt", "Movie Extras.en.sdh.srt"]);

        // Act
        await _subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            VerifyPlannedMove("Movie.en.forced.srt", "Masked (1999).en.forced.srt", Times.Once());
            string[] unrelatedFiles = ["Other.en.forced.srt", "Movie.en.forcedx.srt", "Movie Extras.en.sdh.srt"];
            foreach (var unrelated in unrelatedFiles)
            {
                _auditRecords.Verify(repository => repository.CreateAsync(
                    It.Is<AuditRecord>(record => record.Operation == "ImportDryRun" &&
                        record.Detail!.StartsWith($"Dry run: move /downloads/{unrelated} ", StringComparison.Ordinal)),
                    It.IsAny<CancellationToken>()), Times.Never);
            }
        }
    }

    private JobContext ArrangeSidecarDryRun(string primary, string primaryDestination, IEnumerable<string> sidecars)
    {
        _registry.Setup(registry => registry.GetDefinitionFor("type-a")).Returns(new ContentTypeDefinition
        {
            Slug = "type-a",
            DisplayName = "Type A",
            LevelDefinitions =
            [
                new()
                {
                    Slug = "leaf",
                    DisplayName = "Leaf",
                    CustomMetadataFields = [],
                    AcceptedFileTypes = [".mkv"],
                    AcceptedSidecarTypes = [".srt"]
                }
            ]
        });
        _fileSystem.Setup(fileSystem => fileSystem.MatchesAnyFileTypeAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, IReadOnlyList<string>? types, CancellationToken _) =>
                types?.Any(type => path.EndsWith(type, StringComparison.OrdinalIgnoreCase)) == true);
        _fileSystem.Setup(fileSystem => fileSystem.DirectoryExists("/downloads/new.mkv")).Returns(true);
        _fileSystem.Setup(fileSystem => fileSystem.EnumerateFiles("/downloads/new.mkv"))
            .Returns(["/downloads/" + primary, .. sidecars.Select(sidecar => "/downloads/" + sidecar)]);
        _contentFiles.Setup(service => service.PreviewRenameAsync(
                _node.Id, It.IsAny<FileInfo>(), It.IsAny<MediaInfo?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentRename
            {
                Original = new FileInfo("/downloads/" + primary),
                Rename = new FileInfo(primaryDestination),
                Item = _node
            });
        return _jobContext with
        {
            Parameters = new Dictionary<string, string>(_jobContext.Parameters) { ["dryRun"] = "true" }
        };
    }

    private void VerifyPlannedMove(string sidecar, string expectedName, Times times)
    {
        var expected = Path.Combine(new FileInfo("/library/x").DirectoryName!, expectedName);
        _auditRecords.Verify(repository => repository.CreateAsync(
            It.Is<AuditRecord>(record => record.Operation == "ImportDryRun" &&
                record.Detail == $"Dry run: move /downloads/{sidecar} -> {expected}"),
            It.IsAny<CancellationToken>()), times);
    }

    [Test]
    public async Task ExecuteAsync_WritesOneAuditRecordForEachDestination_WhenDryRunPlansTwoFiles()
    {
        // Arrange
        _fileSystem.Setup(fileSystem => fileSystem.DirectoryExists("/downloads/new.mkv")).Returns(true);
        _fileSystem.Setup(fileSystem => fileSystem.EnumerateFiles("/downloads/new.mkv"))
            .Returns(["/downloads/a.mkv", "/downloads/b.mkv"]);
        _contentFiles.SetupSequence(service => service.PreviewRenameAsync(
                _node.Id, It.IsAny<FileInfo>(), It.IsAny<MediaInfo?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentRename
            {
                Original = new FileInfo("/downloads/a.mkv"),
                Rename = new FileInfo("/library/new.mkv"),
                Item = _node
            })
            .ReturnsAsync(new ContentRename
            {
                Original = new FileInfo("/downloads/b.mkv"),
                Rename = new FileInfo("/library/other.mkv"),
                Item = _node
            });
        var context = _jobContext with
        {
            Parameters = new Dictionary<string, string>(_jobContext.Parameters) { ["dryRun"] = "true" }
        };

        // Act
        await _subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _auditRecords.Verify(repository => repository.CreateAsync(
                It.Is<AuditRecord>(record => record.Operation == "ImportDryRun" &&
                    record.Detail!.StartsWith("Dry run: move /downloads/a.mkv -> ", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()), Times.Once);
            _auditRecords.Verify(repository => repository.CreateAsync(
                It.Is<AuditRecord>(record => record.Operation == "ImportDryRun" &&
                    record.Detail!.StartsWith("Dry run: move /downloads/b.mkv -> ", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()), Times.Once);
            _progress.Verify(progress => progress.Report(
                1, "Dry run: move 2 file(s). The audit history of the download holds the plan."), Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_KeepsSupersededFileTracked_WhenSupersededFileCannotBeDeleted()
    {
        // Arrange
        _fileSystem.Setup(fileSystem => fileSystem.DeleteFile("/library/old.mkv"))
            .Throws(new IOException("The file is locked."));

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _mediaFiles.Verify(repository => repository.DeleteAsync(
                _priorFile.Id, null, It.IsAny<CancellationToken>()), Times.Never);
            _auditRecords.Verify(repository => repository.CreateAsync(
                It.Is<AuditRecord>(record => record.Operation == "FileDeleteFailed" &&
                    record.EntityId == _priorFile.Id.ToString() &&
                    record.Detail == "Could not delete the superseded file /library/old.mkv."),
                It.IsAny<CancellationToken>()), Times.Once);
            _fileSystem.Verify(fileSystem => fileSystem.Move(
                It.IsAny<string>(), "/downloads/new.mkv"), Times.Never);
            _downloadRepository.Verify(repository => repository.MarkImportedAsync(
                _download.Id, _node.Id, 1, It.IsAny<DateTime>(), It.IsAny<DateTime>(), null,
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_KeepsExistingFile_WhenImportPlacesNoPrimaryFile()
    {
        // Arrange
        _rootFolder.DefaultImportCollisionMode = ImportCollisionMode.Skip;
        _download.OutputPath = "/downloads/Release";
        _registry.Setup(registry => registry.GetDefinitionFor("type-a")).Returns(new ContentTypeDefinition
        {
            Slug = "type-a",
            DisplayName = "Type A",
            LevelDefinitions =
            [
                new()
                {
                    Slug = "leaf",
                    DisplayName = "Leaf",
                    CustomMetadataFields = [],
                    AcceptedFileTypes = [".mkv"],
                    AcceptedSidecarTypes = [".srt"]
                }
            ]
        });
        _fileSystem.Setup(fileSystem => fileSystem.MatchesAnyFileTypeAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, IReadOnlyList<string>? types, CancellationToken _) =>
                types?.Any(type => path.EndsWith(type, StringComparison.OrdinalIgnoreCase)) == true);
        _fileSystem.Setup(fileSystem => fileSystem.DirectoryExists("/downloads/Release")).Returns(true);
        _fileSystem.Setup(fileSystem => fileSystem.EnumerateFiles("/downloads/Release"))
            .Returns(["/downloads/Release/new.mkv", "/downloads/Release/new.en.srt"]);
        _fileSystem.Setup(fileSystem => fileSystem.FileExists(new FileInfo("/library/new.mkv").FullName)).Returns(true);
        _fileSystem.Setup(fileSystem => fileSystem.FileExists(new FileInfo("/library/new.en.srt").FullName))
            .Returns(false);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _downloadRepository.Verify(repository => repository.MarkImportedAsync(
                _download.Id, _node.Id, 1, It.IsAny<DateTime>(), It.IsAny<DateTime>(), null,
                It.IsAny<CancellationToken>()), Times.Once);
            _fileSystem.Verify(fileSystem => fileSystem.DeleteFile("/library/old.mkv"), Times.Never);
            _mediaFiles.Verify(repository => repository.DeleteAsync(
                _priorFile.Id, null, It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public void ExecuteAsync_KeepsExistingFile_WhenDownloadHoldsOnlyExtras()
    {
        // Arrange
        _download.OutputPath = "/downloads/Release";
        _fileSystem.Setup(fileSystem => fileSystem.DirectoryExists("/downloads/Release")).Returns(true);
        _fileSystem.Setup(fileSystem => fileSystem.EnumerateFiles("/downloads/Release"))
            .Returns(
            [
                "/downloads/Release/Featurettes/Making Of.mkv",
                "/downloads/Release/Featurettes/Interview.mkv"
            ]);

        // Act
        Assert.CatchAsync<InvalidOperationException>(() => _subject.ExecuteAsync(_jobContext));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _downloadRepository.Verify(repository => repository.MarkFailedAsync(
                _download.Id,
                DownloadFailureReason.NoUsableContent,
                "No file matched content that needs it. Skipped 2 files: 2 extra. Making Of.mkv (extra); " +
                    "Interview.mkv (extra)",
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Once);
            _downloadRepository.Verify(repository => repository.MarkImportedAsync(
                _download.Id, It.IsAny<Guid?>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
            _fileSystem.Verify(fileSystem => fileSystem.DeleteFile("/library/old.mkv"), Times.Never);
        }
    }

    [TestCase("/downloads/Release/Featurettes/Making Of.mkv")]
    [TestCase("/downloads/Release/Deleted Scenes/Alternate Ending.mkv")]
    [TestCase("/downloads/Release/Behind The Scenes/Crew.mkv")]
    [TestCase("/downloads/Release/Extras/Gag Reel.mkv")]
    [TestCase("/downloads/Release/Shorts/1) Short.mkv")]
    [TestCase("/downloads/Release/Sample/Clip.mkv")]
    [TestCase("/downloads/Release/Movie-featurette.mkv")]
    [TestCase("/downloads/Release/Movie-deleted.mkv")]
    [TestCase("/downloads/Release/Movie-trailer.mkv")]
    [TestCase("/downloads/Release/Bonus Features/Commentary.mkv")]
    [TestCase("/downloads/Release/Special Features/Cast.mkv")]
    [TestCase("/downloads/Release/Samples/Clip.mkv")]
    [TestCase("/downloads/Release/Movie-scene.mkv")]
    [TestCase("/downloads/Release/Movie-short.mkv")]
    [TestCase("/downloads/Release/Movie-other.mkv")]
    [TestCase("/downloads/Release/trailer.mkv")]
    [TestCase("/downloads/Release/Movie.sample.mkv")]
    [TestCase("/downloads/Release/Movie.sample.1080p.mkv")]
    [TestCase("/downloads/Release/Movie [sample].mkv")]
    [TestCase("/downloads/Release/sample.mkv")]
    public async Task ExecuteAsync_SkipsExtra_WhenDownloadHoldsTheFilmAndAnExtra(string extra)
    {
        // Arrange
        _download.OutputPath = "/downloads/Release";
        _fileSystem.Setup(fileSystem => fileSystem.DirectoryExists("/downloads/Release")).Returns(true);
        _fileSystem.Setup(fileSystem => fileSystem.EnumerateFiles("/downloads/Release"))
            .Returns(["/downloads/Release/Movie.mkv", extra]);
        _contentFiles.Setup(service => service.PreviewRenameAsync(
                _node.Id, It.IsAny<FileInfo>(), It.IsAny<MediaInfo?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, FileInfo file, MediaInfo? _, CancellationToken _) => new ContentRename
            {
                Original = file,
                Rename = new FileInfo("/library/" + file.Name),
                Item = _node
            });
        var context = _jobContext with
        {
            Parameters = new Dictionary<string, string>(_jobContext.Parameters) { ["dryRun"] = "true" }
        };

        // Act
        await _subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _auditRecords.Verify(repository => repository.CreateAsync(
                It.Is<AuditRecord>(record => record.Operation == "ImportDryRun" &&
                    record.Detail!.StartsWith($"Dry run: move {extra} ", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()), Times.Never);
            _progress.Verify(progress => progress.Report(
                1, "Dry run: move 1 file(s). The audit history of the download holds the plan."), Times.Once);
        }
    }

    [TestCase("/downloads/Release/The Big Short.mkv")]
    [TestCase("/downloads/Release/Scenes from a Marriage.mkv")]
    [TestCase("/downloads/Release/Movie-GROUP.mkv")]
    [TestCase("/downloads/Release/Specials/Movie.mkv")]
    public async Task ExecuteAsync_ImportsTheFilm_WhenItsNameOnlyResemblesAnExtra(string film)
    {
        // Arrange
        _download.OutputPath = "/downloads/Release";
        _fileSystem.Setup(fileSystem => fileSystem.DirectoryExists("/downloads/Release")).Returns(true);
        _fileSystem.Setup(fileSystem => fileSystem.EnumerateFiles("/downloads/Release")).Returns([film]);
        var context = _jobContext with
        {
            Parameters = new Dictionary<string, string>(_jobContext.Parameters) { ["dryRun"] = "true" }
        };

        // Act
        await _subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        _progress.Verify(progress => progress.Report(
            1, "Dry run: move 1 file(s). The audit history of the download holds the plan."), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_DeletesSupersededRowOnRetry_WhenTheRowDeleteFailsAfterTheFileDelete()
    {
        // Arrange
        _downloadRepository.Setup(repository => repository.MarkImportedAsync(
                _download.Id, It.IsAny<Guid?>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, Guid?, int, DateTime, DateTime, Guid?, CancellationToken>((_, _, _, _, _, _, _) =>
                _download.Status = DownloadStatus.Imported).Returns(Task.CompletedTask);
        _downloadRepository.Setup(repository => repository.SetImportRecoveryStateAsync(
                _download.Id, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<DateTime>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, string?, string?, DateTime, Guid?, CancellationToken>((_, state, _, _, _, _) =>
                _download.ImportRecoveryState = state).Returns(Task.CompletedTask);
        _mediaFiles.SetupSequence(repository => repository.DeleteAsync(
                _priorFile.Id, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("The database is unavailable."))
            .ReturnsAsync(true);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_download.ImportRecoveryState, Is.Null);
            _fileSystem.Verify(fileSystem => fileSystem.DeleteFile("/library/old.mkv"), Times.Exactly(2));
            _mediaFiles.Verify(repository => repository.DeleteAsync(
                _priorFile.Id, null, It.IsAny<CancellationToken>()), Times.Exactly(2));
            _auditRecords.Verify(repository => repository.CreateAsync(
                It.Is<AuditRecord>(record => record.Operation == "SupersedeUpgrade"),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_KeepsSupersededFile_WhenTheFileChangesBeforeItIsDeleted()
    {
        // Arrange
        _fileSystem.SetupSequence(fileSystem => fileSystem.GetFileVersion("/library/old.mkv"))
            .Returns("superseded-version")
            .Returns("replaced-version");

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _fileSystem.Verify(fileSystem => fileSystem.DeleteFile("/library/old.mkv"), Times.Never);
            _mediaFiles.Verify(repository => repository.DeleteAsync(
                _priorFile.Id, null, It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_DeletesSupersededFile_WhenTheImportedFileChangesBeforeCleanup()
    {
        // Arrange
        _fileSystem.SetupSequence(fileSystem => fileSystem.GetFileVersion(new FileInfo("/library/new.mkv").FullName))
            .Returns("owned-version")
            .Returns("rewritten-version");
        _mediaFiles.Setup(repository => repository.DeleteAsync(_priorFile.Id, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _fileSystem.Verify(fileSystem => fileSystem.DeleteFile("/library/old.mkv"), Times.Once);
            _mediaFiles.Verify(repository => repository.DeleteAsync(
                _priorFile.Id, null, It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_SupersedesOnlyEpisodesWithAPlacedPrimaryFile_WhenASeasonPackReplacesSeveralEpisodes()
    {
        // Arrange
        _node.Level = "season";
        _rootFolder.DefaultImportCollisionMode = ImportCollisionMode.Skip;
        ContentHierarchyItem[] episodes =
        [
            new()
            {
                Id = Guid.NewGuid(),
                ParentId = _node.Id,
                ContentType = "type-a",
                Level = "episode",
                Slug = "s01e01",
                Name = "Episode 1",
                EffectiveRootFolderId = _node.EffectiveRootFolderId
            },
            new()
            {
                Id = Guid.NewGuid(),
                ParentId = _node.Id,
                ContentType = "type-a",
                Level = "episode",
                Slug = "s01e02",
                Name = "Episode 2",
                EffectiveRootFolderId = _node.EffectiveRootFolderId
            },
            new()
            {
                Id = Guid.NewGuid(),
                ParentId = _node.Id,
                ContentType = "type-a",
                Level = "episode",
                Slug = "s01e03",
                Name = "Episode 3",
                EffectiveRootFolderId = _node.EffectiveRootFolderId
            }
        ];
        ContentItem[] items =
        [
            new()
            {
                Id = Guid.NewGuid(),
                ContentHierarchyItemId = episodes[0].Id,
                Fulfillment = FulfillmentStatus.Upgradable
            },
            new()
            {
                Id = Guid.NewGuid(),
                ContentHierarchyItemId = episodes[1].Id,
                Fulfillment = FulfillmentStatus.Upgradable
            },
            new()
            {
                Id = Guid.NewGuid(),
                ContentHierarchyItemId = episodes[2].Id,
                Fulfillment = FulfillmentStatus.Upgradable
            }
        ];
        MediaFile[] priors =
        [
            new()
            {
                Id = Guid.NewGuid(),
                ContentItemId = items[0].Id,
                FilePath = "/library/Show.S01E01.old.mkv",
                FileKind = "Video"
            },
            new()
            {
                Id = Guid.NewGuid(),
                ContentItemId = items[1].Id,
                FilePath = "/library/Show.S01E02.old.mkv",
                FileKind = "Video"
            },
            new()
            {
                Id = Guid.NewGuid(),
                ContentItemId = items[2].Id,
                FilePath = "/library/Show.S01E03.old.mkv",
                FileKind = "Video"
            }
        ];
        _registry.Setup(registry => registry.GetDefinitionFor("type-a")).Returns(new ContentTypeDefinition
        {
            Slug = "type-a",
            DisplayName = "Type A",
            LevelDefinitions =
            [
                new()
                {
                    Slug = "season",
                    DisplayName = "Season",
                    CustomMetadataFields = []
                },
                new()
                {
                    Slug = "episode",
                    DisplayName = "Episode",
                    ParentSlug = "season",
                    CustomMetadataFields = [],
                    AcceptedFileTypes = [".mkv"],
                    AcceptedSidecarTypes = [".srt"]
                }
            ]
        });
        _fileSystem.Setup(fileSystem => fileSystem.MatchesAnyFileTypeAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, IReadOnlyList<string>? types, CancellationToken _) =>
                types?.Any(type => path.EndsWith(type, StringComparison.OrdinalIgnoreCase)) == true);
        _nodes.Setup(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(), It.IsAny<PagingParameter>(), It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem> { Items = [_node, .. episodes] });
        _evaluation.Setup(service => service.AssignFilesToLeavesAsync(
                _release, _node, It.IsAny<IReadOnlyList<ContentHierarchyItem>>(),
                It.IsAny<IReadOnlyList<ReleaseFileRef>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new ReleaseFileAssignment
                {
                    FileKey = "/downloads/Release/Show.S01E01.mkv",
                    LeafContentHierarchyItemId = episodes[0].Id
                },
                new ReleaseFileAssignment
                {
                    FileKey = "/downloads/Release/Show.S01E02.mkv",
                    LeafContentHierarchyItemId = episodes[1].Id
                },
                new ReleaseFileAssignment
                {
                    FileKey = "/downloads/Release/Show.S01E03.mkv",
                    LeafContentHierarchyItemId = episodes[2].Id
                }
            ]);
        _contentFiles.Setup(service => service.PreviewRenameAsync(
                episodes[0].Id, It.IsAny<FileInfo>(), It.IsAny<MediaInfo?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentRename
            {
                Original = new FileInfo("/downloads/Release/Show.S01E01.mkv"),
                Rename = new FileInfo("/library/Show.S01E01.mkv"),
                Item = episodes[0]
            });
        _content.Setup(service => service.GetOrCreateContentItemAsync(
                episodes[0].Id, It.IsAny<DateTime>(), It.IsAny<Guid?>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(items[0]);
        _mediaFiles.Setup(repository => repository.QueryAsync(
                It.Is<MediaFileQuery>(query => query.ContentItemIds!.Contains(items[0].Id)),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<MediaFile> { Items = [priors[0]] });
        _mediaFiles.Setup(repository => repository.DeleteAsync(priors[0].Id, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _contentFiles.Setup(service => service.PreviewRenameAsync(
                episodes[1].Id, It.IsAny<FileInfo>(), It.IsAny<MediaInfo?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentRename
            {
                Original = new FileInfo("/downloads/Release/Show.S01E02.mkv"),
                Rename = new FileInfo("/library/Show.S01E02.mkv"),
                Item = episodes[1]
            });
        _content.Setup(service => service.GetOrCreateContentItemAsync(
                episodes[1].Id, It.IsAny<DateTime>(), It.IsAny<Guid?>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(items[1]);
        _mediaFiles.Setup(repository => repository.QueryAsync(
                It.Is<MediaFileQuery>(query => query.ContentItemIds!.Contains(items[1].Id)),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<MediaFile> { Items = [priors[1]] });
        _mediaFiles.Setup(repository => repository.DeleteAsync(priors[1].Id, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _contentFiles.Setup(service => service.PreviewRenameAsync(
                episodes[2].Id, It.IsAny<FileInfo>(), It.IsAny<MediaInfo?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentRename
            {
                Original = new FileInfo("/downloads/Release/Show.S01E03.mkv"),
                Rename = new FileInfo("/library/Show.S01E03.mkv"),
                Item = episodes[2]
            });
        _content.Setup(service => service.GetOrCreateContentItemAsync(
                episodes[2].Id, It.IsAny<DateTime>(), It.IsAny<Guid?>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(items[2]);
        _mediaFiles.Setup(repository => repository.QueryAsync(
                It.Is<MediaFileQuery>(query => query.ContentItemIds!.Contains(items[2].Id)),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<MediaFile> { Items = [priors[2]] });
        _mediaFiles.Setup(repository => repository.DeleteAsync(priors[2].Id, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _download.OutputPath = "/downloads/Release";
        _fileSystem.Setup(fileSystem => fileSystem.DirectoryExists("/downloads/Release")).Returns(true);
        _fileSystem.Setup(fileSystem => fileSystem.EnumerateFiles("/downloads/Release"))
            .Returns(
            [
                "/downloads/Release/Show.S01E01.mkv",
                "/downloads/Release/Show.S01E02.mkv",
                "/downloads/Release/Show.S01E03.mkv",
                "/downloads/Release/Show.S01E03.en.srt"
            ]);
        _fileSystem.Setup(fileSystem => fileSystem.FileExists(new FileInfo("/library/Show.S01E01.mkv").FullName))
            .Returns(false);
        _fileSystem.Setup(fileSystem => fileSystem.FileExists(new FileInfo("/library/Show.S01E02.mkv").FullName))
            .Returns(false);
        _fileSystem.Setup(fileSystem => fileSystem.FileExists(new FileInfo("/library/Show.S01E03.en.srt").FullName))
            .Returns(false);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _fileSystem.Verify(fileSystem => fileSystem.DeleteFile("/library/Show.S01E01.old.mkv"), Times.Once);
            _fileSystem.Verify(fileSystem => fileSystem.DeleteFile("/library/Show.S01E02.old.mkv"), Times.Once);
            _fileSystem.Verify(fileSystem => fileSystem.DeleteFile("/library/Show.S01E03.old.mkv"), Times.Never);
            _mediaFiles.Verify(repository => repository.DeleteAsync(
                priors[0].Id, null, It.IsAny<CancellationToken>()), Times.Once);
            _mediaFiles.Verify(repository => repository.DeleteAsync(
                priors[1].Id, null, It.IsAny<CancellationToken>()), Times.Once);
            _mediaFiles.Verify(repository => repository.DeleteAsync(
                priors[2].Id, null, It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_KeepsSupersededRow_WhenTheFileIsMissingAndTheRootCannotBeListed()
    {
        // Arrange
        _fileSystem.Setup(fileSystem => fileSystem.FileExists("/library/old.mkv")).Returns(false);
        _fileSystem.Setup(fileSystem => fileSystem.EnumerateFileSystemEntries(_rootFolder.Path))
            .Throws(new IOException("The network path was not found."));

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _mediaFiles.Verify(repository => repository.DeleteAsync(
                _priorFile.Id, null, It.IsAny<CancellationToken>()), Times.Never);
            _auditRecords.Verify(repository => repository.CreateAsync(
                It.Is<AuditRecord>(record => record.Operation == "FileDeleteFailed" &&
                    record.EntityId == _priorFile.Id.ToString()),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_DeletesSupersededRow_WhenTheFileIsMissingAndTheRootCanBeListed()
    {
        // Arrange
        _fileSystem.Setup(fileSystem => fileSystem.FileExists("/library/old.mkv")).Returns(false);
        _fileSystem.Setup(fileSystem => fileSystem.EnumerateFileSystemEntries(_rootFolder.Path))
            .Returns(["/library/new.mkv"]);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _fileSystem.Verify(fileSystem => fileSystem.DeleteFile("/library/old.mkv"), Times.Never);
            _mediaFiles.Verify(repository => repository.DeleteAsync(
                _priorFile.Id, null, It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_BindsTheSpecialToItsEpisode_WhenASeasonPackHoldsItInAnExtrasFolder()
    {
        // Arrange
        _node.Level = "season";
        var episode = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = _node.Id,
            ContentType = "type-a",
            Level = "episode",
            Slug = "s00e03",
            Name = "Behind the Scenes",
            EffectiveRootFolderId = _node.EffectiveRootFolderId
        };
        _registry.Setup(registry => registry.GetDefinitionFor("type-a")).Returns(new ContentTypeDefinition
        {
            Slug = "type-a",
            DisplayName = "Type A",
            LevelDefinitions =
            [
                new()
                {
                    Slug = "season",
                    DisplayName = "Season",
                    CustomMetadataFields = []
                },
                new()
                {
                    Slug = "episode",
                    DisplayName = "Episode",
                    ParentSlug = "season",
                    CustomMetadataFields = [],
                    AcceptedFileTypes = [".mkv"]
                }
            ]
        });
        _nodes.Setup(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(), It.IsAny<PagingParameter>(), It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem> { Items = [_node, episode] });
        _evaluation.Setup(service => service.AssignFilesToLeavesAsync(
                _release, _node, It.IsAny<IReadOnlyList<ContentHierarchyItem>>(),
                It.IsAny<IReadOnlyList<ReleaseFileRef>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new ReleaseFileAssignment
                {
                    FileKey = "/downloads/Release/Extras/Show.S00E03.Behind.the.Scenes.mkv",
                    LeafContentHierarchyItemId = episode.Id
                }
            ]);
        _contentFiles.Setup(service => service.PreviewRenameAsync(
                episode.Id, It.IsAny<FileInfo>(), It.IsAny<MediaInfo?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentRename
            {
                Original = new FileInfo("/downloads/Release/Extras/Show.S00E03.Behind.the.Scenes.mkv"),
                Rename = new FileInfo("/library/Show.S00E03.mkv"),
                Item = episode
            });
        _download.OutputPath = "/downloads/Release";
        _fileSystem.Setup(fileSystem => fileSystem.DirectoryExists("/downloads/Release")).Returns(true);
        _fileSystem.Setup(fileSystem => fileSystem.EnumerateFiles("/downloads/Release"))
            .Returns(["/downloads/Release/Extras/Show.S00E03.Behind.the.Scenes.mkv"]);
        var context = _jobContext with
        {
            Parameters = new Dictionary<string, string>(_jobContext.Parameters) { ["dryRun"] = "true" }
        };

        // Act
        await _subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        _progress.Verify(progress => progress.Report(
            1, "Dry run: move 1 file(s). The audit history of the download holds the plan."), Times.Once);
    }

    [Test]
    public void SummarizeSkippedFiles_ListsEveryFile_WhenTheListFitsTheLimit()
    {
        // Arrange
        (string File, string? ItemName, DownloadImportItemJob.SkipReason Reason)[] skipped =
        [
            ("a.mkv", "Episode 1", DownloadImportItemJob.SkipReason.Fulfilled),
            ("b.nfo", null, DownloadImportItemJob.SkipReason.NoSingleItem),
            ("c.mkv", "Episode 3", DownloadImportItemJob.SkipReason.Fulfilled),
        ];

        // Act
        var summary = DownloadImportItemJob.SummarizeSkippedFiles("No file matched. ", skipped);

        // Assert
        Assert.That(summary, Is.EqualTo(
            "No file matched. Skipped 3 files: 2 already fulfilled, 1 matches no single item. " +
            "a.mkv (Episode 1: already fulfilled); b.nfo (matches no single item); " +
            "c.mkv (Episode 3: already fulfilled)"));
    }

    [Test]
    public void SummarizeSkippedFiles_EndsWithTheRemainingCount_WhenTheListExceedsTheLimit()
    {
        // Arrange
        (string File, string? ItemName, DownloadImportItemJob.SkipReason Reason)[] skipped =
        [
            .. Enumerable.Range(1, 40).Select(index => (
                $"Show.Name.S01E{index:00}.Kontrollverlust.German.1080p.WEB.x264-GROUP.mkv",
                $"Episode {index}",
                DownloadImportItemJob.SkipReason.Fulfilled))
        ];

        // Act
        var summary = DownloadImportItemJob.SummarizeSkippedFiles("No file matched. ", skipped);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(summary, Has.Length.LessThanOrEqualTo(1024));
            Assert.That(summary, Does.StartWith("No file matched. Skipped 40 files: 40 already fulfilled. "));
            Assert.That(summary, Does.Match(@"\(Episode \d+: already fulfilled\) …and \d+ more$"));
        }
    }
}
