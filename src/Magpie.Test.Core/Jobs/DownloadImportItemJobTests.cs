using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Content;
using Magpie.Abstractions.Releases;
using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Jobs;
using Magpie.Data;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;
using Magpie.Models.Releases;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Releases;
using Magpie.Sdk.Services;

using Microsoft.EntityFrameworkCore;
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
    private Mock<IProgressReporter> _progress = null!;
    private MagpieDbContext _dbContext = null!;
    private ITransactionManager _transactions = null!;
    private Download _download = null!;
    private ContentHierarchyItem _node = null!;
    private MediaFile _priorFile = null!;
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
        var release = new Release { Id = Guid.NewGuid(), Title = "Release", Protocol = "protocol-a" };
        _download = new Download
        {
            Id = new Guid("3d7b1e88-2a4c-4f51-9a6d-0b8e5c4f2a19"),
            Title = "Release",
            ExternalId = "external-id",
            Status = DownloadStatus.Downloaded,
            ReleaseId = release.Id,
            OutputPath = "/downloads/new.mkv"
        };
        _downloadRepository.Setup(repository => repository.GetByIdAsync(
                _download.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_download);
        var releases = new Mock<IReleaseRepository> { CallBase = true };
        releases.Setup(repository => repository.GetByIdAsync(release.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(release);
        var nodes = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        nodes.Setup(repository => repository.GetLineageAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([_node]);
        var scopes = new Mock<IDownloadScopeRepository> { CallBase = true };
        scopes.Setup(repository => repository.QueryAsync(
                It.IsAny<DownloadScopeQuery>(), It.IsAny<PagingParameter>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<DownloadScope>
            {
                Items = [new DownloadScope { DownloadId = _download.Id, ContentHierarchyItemId = _node.Id }]
            });
        var roots = new Mock<IRootFolderRepository> { CallBase = true };
        roots.Setup(repository => repository.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RootFolder
            {
                Name = "Library",
                Path = Path.GetFullPath("/library"),
                ContentType = "type-a",
                UpgradeFileHandling = UpgradeFileHandling.Replace
            });
        var content = new Mock<IContentService>();
        content.Setup(service => service.GetOrCreateContentItemAsync(
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
                _node.Id, It.IsAny<FileInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentRename
            {
                Original = new FileInfo("/downloads/new.mkv"),
                Rename = new FileInfo("/library/new.mkv"),
                Item = _node
            });
        var evaluation = new Mock<IReleaseEvaluationService>();
        evaluation.Setup(service => service.ResolveScopeAsync(
                release, It.IsAny<IReadOnlyList<ContentFact>>(),
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
        _subject = new DownloadImportItemJob(
            _auditRecords.Object,
            _downloadRepository.Object,
            roots.Object,
            nodes.Object,
            _mediaFiles.Object,
            releases.Object,
            Mock.Of<IReleaseScopeRepository>(),
            scopes.Object,
            Mock.Of<IJobExecutionRepository>(),
            _fileSystem.Object,
            Mock.Of<IContentTypeRegistry>(),
            _contentFiles.Object,
            blocked.Object,
            mappings.Object,
            Mock.Of<IDownloadClientRepository>(),
            Mock.Of<IRemotePathMappingService>(),
            _transactions,
            Mock.Of<IQualityService>(),
            contentRepository.Object,
            content.Object,
            Mock.Of<ILibraryImportService>(),
            Mock.Of<IEventBus>(),
            evaluation.Object,
            TimeProvider.System,
            NullLogger<DownloadImportItemJob>.Instance,
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

    [TestCase("Import")]
    [TestCase("ImportUpgradable")]
    public void ExecuteAsync_PreservesSupersededFile_WhenImportAuditFails(string operation)
    {
        // Arrange
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
                _priorFile.Id, null, It.IsAny<CancellationToken>()), Times.Once);
            _fileSystem.Verify(fileSystem => fileSystem.DeleteFile("/library/old.mkv"), Times.Never);
            _fileSystem.Verify(fileSystem => fileSystem.Move(
                It.Is<string>(path => path.Replace('\\', '/').EndsWith("/library/new.mkv", StringComparison.Ordinal)),
                "/downloads/new.mkv"), Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_DeletesSupersededFile_WhenOutermostTransactionCommits()
    {
        // Arrange
        // Act
        await _transactions.ExecuteWithTransactionAsync(async token =>
        {
            await _subject.ExecuteAsync(_jobContext, token).ConfigureAwait(false);
        }).ConfigureAwait(false);

        // Assert
        _fileSystem.Verify(fileSystem => fileSystem.DeleteFile("/library/old.mkv"), Times.Once);
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

    [Test]
    public async Task ExecuteAsync_WritesOneAuditRecordForEachDestination_WhenDryRunPlansTwoFiles()
    {
        // Arrange
        _fileSystem.Setup(fileSystem => fileSystem.DirectoryExists("/downloads/new.mkv")).Returns(true);
        _fileSystem.Setup(fileSystem => fileSystem.EnumerateFiles("/downloads/new.mkv"))
            .Returns(["/downloads/a.mkv", "/downloads/b.mkv"]);
        _contentFiles.SetupSequence(service => service.PreviewRenameAsync(
                _node.Id, It.IsAny<FileInfo>(), It.IsAny<CancellationToken>()))
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
    public async Task ExecuteAsync_KeepsImportedFile_WhenSupersededFileCannotBeDeleted()
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
                _priorFile.Id, null, It.IsAny<CancellationToken>()), Times.Once);
            _fileSystem.Verify(fileSystem => fileSystem.Move(
                It.IsAny<string>(), "/downloads/new.mkv"), Times.Never);
            _downloadRepository.Verify(repository => repository.MarkImportedAsync(
                _download.Id, _node.Id, 1, It.IsAny<DateTime>(), It.IsAny<DateTime>(), null,
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
