using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Releases;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Jobs;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Jobs;
using Magpie.Models.Paging;
using Magpie.Models.Storage;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Media;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs;

[TestFixture]
public sealed class MediaFileReconciliationJobTests
{
    private static readonly object?[][] MoveCases =
    [
        [
            new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc),
            "/music/Kind of Blue/In Sight/02 The Inside Around Me.wav",
            new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc),
            Array.Empty<LibraryImportCandidate>(),
            "/music/Kind of Blue/In Sight/02 The Inside Around Me.wav",
            0
        ],
        [
            new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc),
            "/music/Kind of Blue/In Sight/02 The Inside Around Me.wav",
            new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc).AddTicks(7),
            Array.Empty<LibraryImportCandidate>(),
            "/music/Kind of Blue/In Sight/02 The Inside Around Me.wav",
            0
        ],
        [
            new DateTime(1999, 6, 15, 12, 0, 0, DateTimeKind.Utc).AddTicks(1_234_570),
            "/music/Kind of Blue/In Sight/02 The Inside Around Me.wav",
            new DateTime(1999, 6, 15, 12, 0, 0, DateTimeKind.Utc).AddTicks(1_234_567),
            Array.Empty<LibraryImportCandidate>(),
            "/music/Kind of Blue/In Sight/02 The Inside Around Me.wav",
            0
        ],
        [
            new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc),
            "/music/Kind of Blue/In Sight/02 The Inside Around Me.wav",
            new DateTime(2026, 9, 21, 0, 0, 1, DateTimeKind.Utc),
            Array.Empty<LibraryImportCandidate>(),
            null,
            1
        ],
        [
            new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc),
            "/music/Kind of Blue/In Sight/02 The Inside Around Me.wav",
            new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc),
            new LibraryImportCandidate[]
            {
                new()
                {
                    RootFolderId = Guid.Empty,
                    ScanRunId = Guid.Empty,
                    FilePath = "/music/Kind of Blue/In Sight/02 The Inside Around Me.wav",
                    ContentType = "music",
                    Status = LibraryImportCandidateStatus.Rejected,
                    CreatedAt = new(2026, 9, 22, 0, 0, 0, DateTimeKind.Utc)
                }
            },
            null,
            1
        ],
        [
            new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc),
            "/music/Kind of Blue/In Sight/01 Bitter Blue.flac",
            new DateTime(2026, 9, 25, 0, 0, 0, DateTimeKind.Utc),
            new LibraryImportCandidate[]
            {
                new()
                {
                    RootFolderId = Guid.Empty,
                    ScanRunId = Guid.Empty,
                    FilePath = "/music/Kind of Blue/In Sight/01 Bitter Blue.flac",
                    ContentType = "music",
                    Status = LibraryImportCandidateStatus.Rejected,
                    CreatedAt = new(2026, 9, 26, 0, 0, 0, DateTimeKind.Utc)
                }
            },
            null,
            1
        ],
        [
            null,
            "/music/Kind of Blue/In Sight/02 The Inside Around Me.wav",
            new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc),
            Array.Empty<LibraryImportCandidate>(),
            null,
            1
        ]
    ];

    private static readonly object?[][] ExtractionCases =
    [
        [
            new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 27, 2, 0, 0, DateTimeKind.Utc),
            1_584L,
            Task.FromException<MediaInfo?>(new IOException("The media file is locked.")),
            null,
            null,
            null
        ],
        [
            new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 27, 2, 0, 0, DateTimeKind.Utc),
            1_584L,
            Task.FromResult<MediaInfo?>(null),
            1_584L,
            new DateTime(2026, 9, 27, 2, 0, 0, DateTimeKind.Utc),
            null
        ],
        [
            new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc),
            1_219L,
            Task.FromResult<MediaInfo?>(null),
            null,
            null,
            null
        ],
        [
            new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc).AddTicks(7),
            1_219L,
            Task.FromResult<MediaInfo?>(null),
            null,
            null,
            null
        ],
        [
            new DateTime(1999, 6, 15, 12, 0, 0, DateTimeKind.Utc).AddTicks(1_234_570),
            new DateTime(1999, 6, 15, 12, 0, 0, DateTimeKind.Utc).AddTicks(1_234_567),
            1_219L,
            Task.FromResult<MediaInfo?>(null),
            null,
            null,
            null
        ]
    ];

    [TestCase(null, QualityStatus.UpgradeWanted, null)]
    [TestCase(null, QualityStatus.Met, null)]
    [TestCase(2, QualityStatus.Met, QualityStatus.UpgradeWanted)]
    [TestCase(1, QualityStatus.UpgradeWanted, QualityStatus.Met)]
    public async Task ExecuteAsync_SavesQualityStatus_WhenTheStoredQualityStatusDiffers(
        int? matchedRankOrdinal,
        QualityStatus? storedQualityStatus,
        QualityStatus? expectedQualityStatus)
    {
        // Arrange
        var hierarchyId = Guid.NewGuid();
        var tierListId = Guid.NewGuid();
        var rootFolderId = Guid.NewGuid();
        var file = new MediaFile
        {
            Id = Guid.NewGuid(),
            FileKind = "Video",
            FilePath = "/movies/Super Troopers 3 (2026)/Super Troopers 3 (2026).mkv",
            FileSize = 4_000_000_000,
            CreatedAt = new(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc)
        };
        var content = new ContentItem
        {
            Id = Guid.NewGuid(),
            ContentHierarchyItemId = hierarchyId,
            Fulfillment = FulfillmentStatus.Upgradable,
            QualityStatus = storedQualityStatus,
            MediaReconciledAt = new(2026, 9, 26, 3, 0, 0, DateTimeKind.Utc),
            MediaFiles = { file }
        };
        var node = new ContentHierarchyItem
        {
            Id = hierarchyId,
            ContentType = "movie",
            Level = "movie",
            Slug = "super-troopers-3",
            Name = "Super Troopers 3",
            EffectiveRootFolderId = rootFolderId,
            EffectiveQualityTierListId = tierListId,
            CreatedAt = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            ContentItem = content
        };
        var mediaFiles = new Mock<IMediaFileRepository> { CallBase = true };
        mediaFiles
            .Setup(repository => repository.QueryAsync(
                It.IsAny<MediaFileQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<MediaFile>());
        var contentItems = new Mock<IContentRepository> { CallBase = true };
        contentItems
            .Setup(repository => repository.QueryAsync(
                It.IsAny<ContentItemQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentItem> { Items = [content], TotalItems = 1 });
        contentItems
            .Setup(repository => repository.GetByIdAsync(content.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(content);
        ContentItem? saved = null;
        contentItems
            .Setup(repository => repository.UpdateMediaStateAsync(
                It.IsAny<ContentItem>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback((ContentItem item, Guid? _, CancellationToken _) => saved = item)
            .ReturnsAsync(true);
        var hierarchy = new Mock<IContentHierarchyItemRepository>();
        hierarchy
            .Setup(repository => repository.GetByIdAsync(hierarchyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(node);
        hierarchy
            .Setup(repository => repository.GetLineageAsync(hierarchyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([node]);
        var rootFolders = new Mock<IRootFolderRepository>();
        rootFolders
            .Setup(repository => repository.GetByIdAsync(rootFolderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RootFolder
            {
                Id = rootFolderId,
                Name = "Movies",
                Path = "/movies",
                ContentType = "movie",
                CreatedAt = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)
            });
        var contentTypes = new Mock<IContentTypeRegistry>();
        contentTypes
            .Setup(registry => registry.GetDefinitionFor("movie"))
            .Returns(new ContentTypeDefinition
            {
                Slug = "movie",
                DisplayName = "Movie",
                LevelDefinitions =
                [
                    new()
                    {
                        Slug = "movie",
                        DisplayName = "Movie",
                        CustomMetadataFields = []
                    }
                ]
            });
        var evaluation = new Mock<IReleaseEvaluationService>();
        evaluation
            .Setup(service => service.ScoreMediaQualityAsync(
                It.Is<IEnumerable<MediaFile>>(files => files.Single() == file),
                tierListId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((expectedQualityStatus, matchedRankOrdinal));
        var transactions = new Mock<ITransactionManager>();
        transactions
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
                operation(cancellationToken));
        var subject = new MediaFileReconciliationJob(
            mediaFiles.Object,
            Mock.Of<ILibraryImportCandidateRepository>(),
            contentItems.Object,
            hierarchy.Object,
            rootFolders.Object,
            contentTypes.Object,
            Mock.Of<IContentService>(),
            evaluation.Object,
            Mock.Of<IFileSystem>(),
            transactions.Object,
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IJobManager>(),
            Mock.Of<IProgressReporter>(),
            new FakeTimeProvider(new DateTimeOffset(2026, 9, 27, 3, 0, 0, TimeSpan.Zero)),
            NullLogger<MediaFileReconciliationJob>.Instance);
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: null,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>(),
            Event: null);

        // Act
        await subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(saved, Is.Not.Null);
            Assert.That(saved?.QualityStatus, Is.EqualTo(expectedQualityStatus));
            Assert.That(saved?.CurrentFileQualityRank, Is.EqualTo(matchedRankOrdinal));
        }
    }

    [TestCase(true, false, 1)]
    [TestCase(false, false, 0)]
    [TestCase(true, true, 0)]
    public async Task ExecuteAsync_DeletesTheRecordOfAMissingFile_WhenTheRootIsListableAndNoPlacementHoldsThePath(
        bool rootListable,
        bool pathLocked,
        int expectedDeletes)
    {
        // Arrange
        var hierarchyId = Guid.NewGuid();
        var rootFolderId = Guid.NewGuid();
        var contentId = Guid.NewGuid();
        var file = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = contentId,
            FileKind = "Video",
            FilePath = "/movies/Super Troopers 3 (2026)/Super Troopers 3 (2026).mkv",
            FileSize = 4_000_000_000,
            CreatedAt = new(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc)
        };
        var content = new ContentItem
        {
            Id = contentId,
            ContentHierarchyItemId = hierarchyId,
            Fulfillment = FulfillmentStatus.Fulfilled,
            MediaReconciledAt = new(2026, 9, 26, 3, 0, 0, DateTimeKind.Utc),
            MediaFiles = { file }
        };
        var node = new ContentHierarchyItem
        {
            Id = hierarchyId,
            ContentType = "movie",
            Level = "movie",
            Slug = "super-troopers-3",
            Name = "Super Troopers 3",
            EffectiveRootFolderId = rootFolderId,
            CreatedAt = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            ContentItem = content
        };
        var mediaFiles = new Mock<IMediaFileRepository> { CallBase = true };
        mediaFiles
            .Setup(repository => repository.QueryAsync(
                It.IsAny<MediaFileQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<MediaFile> { Items = [file], TotalItems = 1 });
        mediaFiles
            .Setup(repository => repository.GetByIdAsync(file.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(file);
        mediaFiles
            .Setup(repository => repository.DeleteAsync(file.Id, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var contentItems = new Mock<IContentRepository> { CallBase = true };
        contentItems
            .Setup(repository => repository.QueryAsync(
                It.IsAny<ContentItemQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentItem> { Items = [content], TotalItems = 1 });
        contentItems
            .Setup(repository => repository.GetByIdAsync(contentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(content);
        var hierarchy = new Mock<IContentHierarchyItemRepository>();
        hierarchy
            .Setup(repository => repository.GetByIdAsync(hierarchyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(node);
        hierarchy
            .Setup(repository => repository.GetLineageAsync(hierarchyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([node]);
        var rootFolders = new Mock<IRootFolderRepository>();
        rootFolders
            .Setup(repository => repository.GetByIdAsync(rootFolderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RootFolder
            {
                Id = rootFolderId,
                Name = "Movies",
                Path = "/movies",
                ContentType = "movie",
                CreatedAt = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)
            });
        var contentTypes = new Mock<IContentTypeRegistry>();
        contentTypes
            .Setup(registry => registry.GetDefinitionFor("movie"))
            .Returns(new ContentTypeDefinition
            {
                Slug = "movie",
                DisplayName = "Movie",
                LevelDefinitions =
                [
                    new()
                    {
                        Slug = "movie",
                        DisplayName = "Movie",
                        CustomMetadataFields = []
                    }
                ]
            });
        var fileSystem = new Mock<IFileSystem>();
        fileSystem
            .Setup(system => system.IsPathWithin("/movies", file.FilePath))
            .Returns(true);
        fileSystem
            .Setup(system => system.PathsEqual(file.FilePath, file.FilePath))
            .Returns(true);
        fileSystem
            .Setup(system => system.EnumerateFileSystemEntries("/movies"))
            .Returns(() => rootListable ? [] : throw new IOException("The share is offline."));
        fileSystem
            .Setup(system => system.ReadFileState(file.FilePath))
            .Throws(new FileNotFoundException("The file is gone.", file.FilePath));
        fileSystem
            .Setup(system => system.AcquireImportPaths(
                It.Is<IReadOnlyList<string>>(paths => paths.Single() == file.FilePath)))
            .Returns(() => pathLocked ? throw new IOException("A placement holds the path.") : Mock.Of<IDisposable>());
        var transactions = new Mock<ITransactionManager>();
        transactions
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
                operation(cancellationToken));
        transactions
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<bool>> operation, CancellationToken cancellationToken) =>
                operation(cancellationToken));
        var subject = new MediaFileReconciliationJob(
            mediaFiles.Object,
            Mock.Of<ILibraryImportCandidateRepository>(),
            contentItems.Object,
            hierarchy.Object,
            rootFolders.Object,
            contentTypes.Object,
            Mock.Of<IContentService>(),
            Mock.Of<IReleaseEvaluationService>(),
            fileSystem.Object,
            transactions.Object,
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IJobManager>(),
            Mock.Of<IProgressReporter>(),
            new FakeTimeProvider(new DateTimeOffset(2026, 9, 27, 3, 0, 0, TimeSpan.Zero)),
            NullLogger<MediaFileReconciliationJob>.Instance);
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: null,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>(),
            Event: null);

        // Act
        await subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        mediaFiles.Verify(
            repository => repository.DeleteAsync(file.Id, null, It.IsAny<CancellationToken>()),
            Times.Exactly(expectedDeletes));
    }

    [Test]
    public async Task ExecuteAsync_RefreshesTheQualityOfTheCoveredEpisode_WhenTheOwnerIsTheTarget()
    {
        // Arrange
        var ownerNodeId = Guid.NewGuid();
        var coveredNodeId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var coveredId = Guid.NewGuid();
        var tierListId = Guid.NewGuid();
        var rootFolderId = Guid.NewGuid();
        var file = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = ownerId,
            FileKind = "Video",
            FilePath = "/tv/Show/Season 1/Show S01E01-E02.mkv",
            FileSize = 4_000_000_000,
            FileModifiedAt = new(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc),
            MediaInfo = new(),
            CreatedAt = new(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc)
        };
        file.Coverage.Add(new() { MediaFileId = file.Id, ContentItemId = coveredId });
        var owner = new ContentItem
        {
            Id = ownerId,
            ContentHierarchyItemId = ownerNodeId,
            Fulfillment = FulfillmentStatus.Upgradable,
            QualityStatus = QualityStatus.UpgradeWanted,
            CurrentFileQualityRank = 2,
            MediaReconciledAt = new(2026, 9, 26, 3, 0, 0, DateTimeKind.Utc),
            MediaFiles = { file }
        };
        var covered = new ContentItem
        {
            Id = coveredId,
            ContentHierarchyItemId = coveredNodeId,
            Fulfillment = FulfillmentStatus.Upgradable,
            QualityStatus = QualityStatus.UpgradeWanted,
            CurrentFileQualityRank = 2,
            MediaReconciledAt = new(2026, 9, 26, 3, 0, 0, DateTimeKind.Utc),
            CoveringMediaFiles = { file }
        };
        var ownerNode = new ContentHierarchyItem
        {
            Id = ownerNodeId,
            ContentType = "tv",
            Level = "episode",
            Slug = "episode-1",
            Name = "Episode 1",
            EffectiveRootFolderId = rootFolderId,
            EffectiveQualityTierListId = tierListId,
            CreatedAt = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            ContentItem = owner
        };
        var coveredNode = new ContentHierarchyItem
        {
            Id = coveredNodeId,
            ContentType = "tv",
            Level = "episode",
            Slug = "episode-2",
            Name = "Episode 2",
            EffectiveRootFolderId = rootFolderId,
            EffectiveQualityTierListId = tierListId,
            CreatedAt = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            ContentItem = covered
        };
        var mediaFiles = new Mock<IMediaFileRepository> { CallBase = true };
        mediaFiles
            .Setup(repository => repository.QueryAsync(
                It.IsAny<MediaFileQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<MediaFile> { Items = [file], TotalItems = 1 });
        var contentItems = new Mock<IContentRepository> { CallBase = true };
        contentItems
            .Setup(repository => repository.QueryAsync(
                It.IsAny<ContentItemQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentItem> { Items = [owner], TotalItems = 1 });
        contentItems
            .Setup(repository => repository.GetByIdAsync(ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(owner);
        contentItems
            .Setup(repository => repository.GetByIdAsync(coveredId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(covered);
        var saved = new List<ContentItem>();
        contentItems
            .Setup(repository => repository.UpdateMediaStateAsync(
                It.IsAny<ContentItem>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback((ContentItem item, Guid? _, CancellationToken _) => saved.Add(item))
            .ReturnsAsync(true);
        var hierarchy = new Mock<IContentHierarchyItemRepository>();
        hierarchy
            .Setup(repository => repository.GetByIdAsync(ownerNodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ownerNode);
        hierarchy
            .Setup(repository => repository.GetByIdAsync(coveredNodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(coveredNode);
        hierarchy
            .Setup(repository => repository.GetLineageAsync(ownerNodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([ownerNode]);
        hierarchy
            .Setup(repository => repository.GetLineageAsync(coveredNodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([coveredNode]);
        var rootFolders = new Mock<IRootFolderRepository>();
        rootFolders
            .Setup(repository => repository.GetByIdAsync(rootFolderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RootFolder
            {
                Id = rootFolderId,
                Name = "TV",
                Path = "/tv",
                ContentType = "tv",
                CreatedAt = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)
            });
        var contentTypes = new Mock<IContentTypeRegistry>();
        contentTypes
            .Setup(registry => registry.GetDefinitionFor("tv"))
            .Returns(new ContentTypeDefinition
            {
                Slug = "tv",
                DisplayName = "TV",
                LevelDefinitions =
                [
                    new()
                    {
                        Slug = "episode",
                        DisplayName = "Episode",
                        CustomMetadataFields = []
                    }
                ]
            });
        var fileSystem = new Mock<IFileSystem>();
        fileSystem
            .Setup(system => system.IsPathWithin("/tv", file.FilePath))
            .Returns(true);
        fileSystem
            .Setup(system => system.EnumerateFileSystemEntries("/tv"))
            .Returns([]);
        fileSystem
            .Setup(system => system.ReadFileState(file.FilePath))
            .Returns(new FileState
            {
                Size = 4_000_000_000,
                LastWriteTimeUtc = new(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc)
            });
        var evaluation = new Mock<IReleaseEvaluationService>();
        evaluation
            .Setup(service => service.ScoreMediaQualityAsync(
                It.Is<IEnumerable<MediaFile>>(files => files.Single() == file),
                tierListId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((QualityStatus.Met, 1));
        var transactions = new Mock<ITransactionManager>();
        transactions
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
                operation(cancellationToken));
        var subject = new MediaFileReconciliationJob(
            mediaFiles.Object,
            Mock.Of<ILibraryImportCandidateRepository>(),
            contentItems.Object,
            hierarchy.Object,
            rootFolders.Object,
            contentTypes.Object,
            Mock.Of<IContentService>(),
            evaluation.Object,
            fileSystem.Object,
            transactions.Object,
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IJobManager>(),
            Mock.Of<IProgressReporter>(),
            new FakeTimeProvider(new DateTimeOffset(2026, 9, 27, 3, 0, 0, TimeSpan.Zero)),
            NullLogger<MediaFileReconciliationJob>.Instance);
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: null,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>
            {
                [MagpieJobs.Parameters.ContentHierarchyItemId] = ownerNodeId.ToString("D")
            },
            Event: null);

        // Act
        await subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        Assert.That(
            saved.Select(item => (item.Id, item.QualityStatus, item.CurrentFileQualityRank)),
            Is.EquivalentTo(new (Guid, QualityStatus?, int?)[]
            {
                (ownerId, QualityStatus.Met, 1),
                (coveredId, QualityStatus.Met, 1)
            }));
    }

    [TestCaseSource(nameof(MoveCases))]
    public async Task ExecuteAsync_ReattachesAMissingFile_WhenTheMoveIsProven(
        DateTime? storedWriteTime,
        string otherPath,
        DateTime otherWriteTime,
        LibraryImportCandidate[] rejectedCandidates,
        string? expectedPath,
        int expectedDeletes)
    {
        // Arrange
        var hierarchyId = Guid.NewGuid();
        var rootFolderId = Guid.NewGuid();
        var contentId = Guid.NewGuid();
        var mediaInfo = new MediaInfo
        {
            Container = "wav",
            DurationSeconds = 1,
            Tracks = [new() { Type = "Audio", Name = "audio:0", Codec = "pcm_s16le", Channels = 1 }]
        };
        var file = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = contentId,
            FileKind = "Audio",
            FilePath = "/music/Kind of Blue/In Sight/01 Bitter Blue.wav",
            FileSize = 88_278,
            FileModifiedAt = storedWriteTime,
            MediaInfo = mediaInfo,
            CreatedAt = new(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc)
        };
        var content = new ContentItem
        {
            Id = contentId,
            ContentHierarchyItemId = hierarchyId,
            Fulfillment = FulfillmentStatus.Fulfilled,
            MediaReconciledAt = new(2026, 9, 26, 3, 0, 0, DateTimeKind.Utc),
            MediaFiles = { file }
        };
        var node = new ContentHierarchyItem
        {
            Id = hierarchyId,
            ContentType = "music",
            Level = "track",
            Slug = "bitter-blue",
            Name = "Bitter Blue",
            EffectiveRootFolderId = rootFolderId,
            CreatedAt = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            ContentItem = content
        };
        var mediaFiles = new Mock<IMediaFileRepository> { CallBase = true };
        mediaFiles
            .Setup(repository => repository.QueryAsync(
                It.IsAny<MediaFileQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<MediaFile> { Items = [file], TotalItems = 1 });
        mediaFiles
            .Setup(repository => repository.GetByIdAsync(file.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(file);
        mediaFiles
            .Setup(repository => repository.DeleteAsync(file.Id, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        MediaFile? saved = null;
        mediaFiles
            .Setup(repository => repository.UpdateAsync(
                It.IsAny<MediaFile>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback((MediaFile item, Guid? _, CancellationToken _) => saved = item)
            .ReturnsAsync(true);
        var candidates = new Mock<ILibraryImportCandidateRepository> { CallBase = true };
        candidates
            .Setup(repository => repository.QueryAsync(
                It.Is<LibraryImportCandidateQuery>(query =>
                    query.RootFolderId == rootFolderId && query.Status == LibraryImportCandidateStatus.Rejected),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<LibraryImportCandidate>
            {
                Items = rejectedCandidates,
                TotalItems = rejectedCandidates.Length
            });
        var contentItems = new Mock<IContentRepository> { CallBase = true };
        contentItems
            .Setup(repository => repository.QueryAsync(
                It.IsAny<ContentItemQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentItem> { Items = [content], TotalItems = 1 });
        contentItems
            .Setup(repository => repository.GetByIdAsync(contentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(content);
        var hierarchy = new Mock<IContentHierarchyItemRepository>();
        hierarchy
            .Setup(repository => repository.GetByIdAsync(hierarchyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(node);
        hierarchy
            .Setup(repository => repository.GetLineageAsync(hierarchyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([node]);
        var rootFolders = new Mock<IRootFolderRepository>();
        rootFolders
            .Setup(repository => repository.GetByIdAsync(rootFolderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RootFolder
            {
                Id = rootFolderId,
                Name = "Music",
                Path = "/music",
                ContentType = "music",
                CreatedAt = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)
            });
        var contentTypes = new Mock<IContentTypeRegistry>();
        contentTypes
            .Setup(registry => registry.GetDefinitionFor("music"))
            .Returns(new ContentTypeDefinition
            {
                Slug = "music",
                DisplayName = "Music",
                LevelDefinitions =
                [
                    new()
                    {
                        Slug = "track",
                        DisplayName = "Track",
                        CustomMetadataFields = []
                    }
                ]
            });
        var fileSystem = new Mock<IFileSystem>();
        fileSystem
            .Setup(system => system.IsPathWithin("/music", It.IsAny<string>()))
            .Returns(true);
        fileSystem
            .Setup(system => system.PathsEqual(file.FilePath, file.FilePath))
            .Returns(true);
        fileSystem
            .Setup(system => system.PathsEqual(otherPath, otherPath))
            .Returns(true);
        fileSystem
            .Setup(system => system.DirectoryExists(It.IsAny<string>()))
            .Returns(true);
        fileSystem
            .Setup(system => system.EnumerateFileSystemEntries(It.IsAny<string>()))
            .Returns([otherPath]);
        fileSystem
            .Setup(system => system.FileExists(otherPath))
            .Returns(true);
        fileSystem
            .Setup(system => system.ReadFileState(file.FilePath))
            .Throws(new FileNotFoundException("The file is gone.", file.FilePath));
        fileSystem
            .Setup(system => system.ReadFileState(otherPath))
            .Returns(new FileState { Size = 88_278, LastWriteTimeUtc = otherWriteTime });
        var extractor = new Mock<IMediaInfoExtractor>();
        extractor
            .Setup(service => service.ExtractAsync(otherPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mediaInfo);
        var transactions = new Mock<ITransactionManager>();
        transactions
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
                operation(cancellationToken));
        transactions
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<bool>> operation, CancellationToken cancellationToken) =>
                operation(cancellationToken));
        var subject = new MediaFileReconciliationJob(
            mediaFiles.Object,
            candidates.Object,
            contentItems.Object,
            hierarchy.Object,
            rootFolders.Object,
            contentTypes.Object,
            Mock.Of<IContentService>(),
            Mock.Of<IReleaseEvaluationService>(),
            fileSystem.Object,
            transactions.Object,
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IJobManager>(),
            Mock.Of<IProgressReporter>(),
            new FakeTimeProvider(new DateTimeOffset(2026, 9, 27, 3, 0, 0, TimeSpan.Zero)),
            NullLogger<MediaFileReconciliationJob>.Instance,
            extractor.Object);
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: null,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>(),
            Event: null);

        // Act
        await subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(saved?.FilePath, Is.EqualTo(expectedPath));
            mediaFiles.Verify(
                repository => repository.DeleteAsync(file.Id, null, It.IsAny<CancellationToken>()),
                Times.Exactly(expectedDeletes));
        }
    }

    [TestCaseSource(nameof(ExtractionCases))]
    public async Task ExecuteAsync_SavesAReplacedFile_WhenItChangedAndTheExtractionDoesNotFail(
        DateTime storedWriteTime,
        DateTime diskWriteTime,
        long diskSize,
        Task<MediaInfo?> extraction,
        long? expectedSize,
        DateTime? expectedModifiedAt,
        DateTime? expectedReplacedAt)
    {
        // Arrange
        var hierarchyId = Guid.NewGuid();
        var rootFolderId = Guid.NewGuid();
        var contentId = Guid.NewGuid();
        var file = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = contentId,
            FileKind = "Document",
            FilePath = "/books/Frank Herbert/Dune/Dune.epub",
            FileSize = 1_219,
            FileModifiedAt = storedWriteTime,
            CreatedAt = new(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc)
        };
        var content = new ContentItem
        {
            Id = contentId,
            ContentHierarchyItemId = hierarchyId,
            Fulfillment = FulfillmentStatus.Fulfilled,
            MediaReconciledAt = new(2026, 9, 26, 3, 0, 0, DateTimeKind.Utc),
            MediaFiles = { file }
        };
        var node = new ContentHierarchyItem
        {
            Id = hierarchyId,
            ContentType = "book",
            Level = "book",
            Slug = "dune",
            Name = "Dune",
            EffectiveRootFolderId = rootFolderId,
            CreatedAt = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            ContentItem = content
        };
        var mediaFiles = new Mock<IMediaFileRepository> { CallBase = true };
        mediaFiles
            .Setup(repository => repository.QueryAsync(
                It.IsAny<MediaFileQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<MediaFile> { Items = [file], TotalItems = 1 });
        mediaFiles
            .Setup(repository => repository.GetByIdAsync(file.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(file);
        MediaFile? saved = null;
        mediaFiles
            .Setup(repository => repository.UpdateAsync(
                It.IsAny<MediaFile>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback((MediaFile item, Guid? _, CancellationToken _) => saved = item)
            .ReturnsAsync(true);
        var contentItems = new Mock<IContentRepository> { CallBase = true };
        contentItems
            .Setup(repository => repository.QueryAsync(
                It.IsAny<ContentItemQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentItem> { Items = [content], TotalItems = 1 });
        contentItems
            .Setup(repository => repository.GetByIdAsync(contentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(content);
        var hierarchy = new Mock<IContentHierarchyItemRepository>();
        hierarchy
            .Setup(repository => repository.GetByIdAsync(hierarchyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(node);
        hierarchy
            .Setup(repository => repository.GetLineageAsync(hierarchyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([node]);
        var rootFolders = new Mock<IRootFolderRepository>();
        rootFolders
            .Setup(repository => repository.GetByIdAsync(rootFolderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RootFolder
            {
                Id = rootFolderId,
                Name = "Books",
                Path = "/books",
                ContentType = "book",
                CreatedAt = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)
            });
        var contentTypes = new Mock<IContentTypeRegistry>();
        contentTypes
            .Setup(registry => registry.GetDefinitionFor("book"))
            .Returns(new ContentTypeDefinition
            {
                Slug = "book",
                DisplayName = "Book",
                LevelDefinitions =
                [
                    new()
                    {
                        Slug = "book",
                        DisplayName = "Book",
                        CustomMetadataFields = []
                    }
                ]
            });
        var fileSystem = new Mock<IFileSystem>();
        fileSystem
            .Setup(system => system.IsPathWithin("/books", file.FilePath))
            .Returns(true);
        fileSystem
            .Setup(system => system.PathsEqual(file.FilePath, file.FilePath))
            .Returns(true);
        fileSystem
            .Setup(system => system.EnumerateFileSystemEntries("/books"))
            .Returns([]);
        fileSystem
            .Setup(system => system.ReadFileState(file.FilePath))
            .Returns(new FileState { Size = diskSize, LastWriteTimeUtc = diskWriteTime });
        var extractor = new Mock<IMediaInfoExtractor>();
        extractor
            .Setup(service => service.ExtractAsync(file.FilePath, It.IsAny<CancellationToken>()))
            .Returns(extraction);
        var transactions = new Mock<ITransactionManager>();
        transactions
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
                operation(cancellationToken));
        transactions
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<bool>> operation, CancellationToken cancellationToken) =>
                operation(cancellationToken));
        var subject = new MediaFileReconciliationJob(
            mediaFiles.Object,
            Mock.Of<ILibraryImportCandidateRepository>(),
            contentItems.Object,
            hierarchy.Object,
            rootFolders.Object,
            contentTypes.Object,
            Mock.Of<IContentService>(),
            Mock.Of<IReleaseEvaluationService>(),
            fileSystem.Object,
            transactions.Object,
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IJobManager>(),
            Mock.Of<IProgressReporter>(),
            new FakeTimeProvider(new DateTimeOffset(2026, 9, 27, 3, 0, 0, TimeSpan.Zero)),
            NullLogger<MediaFileReconciliationJob>.Instance,
            extractor.Object);
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: null,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>(),
            Event: null);

        // Act
        await subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(saved?.FileSize, Is.EqualTo(expectedSize));
            Assert.That(saved?.FileModifiedAt, Is.EqualTo(expectedModifiedAt));
            Assert.That(saved?.ReplacedAt, Is.EqualTo(expectedReplacedAt));
            Assert.That(saved?.MediaInfo, Is.Null);
        }
    }
}
