using System;
using System.Collections.Generic;
using System.IO;
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
using Magpie.Models.Releases;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs;

[TestFixture]
public sealed class MediaFileReconciliationJobTests
{
    [TestCase(null, false, false, QualityStatus.UpgradeWanted, null)]
    [TestCase(null, false, false, QualityStatus.Met, null)]
    [TestCase(2, true, false, QualityStatus.Met, QualityStatus.UpgradeWanted)]
    [TestCase(1, true, true, QualityStatus.UpgradeWanted, QualityStatus.Met)]
    public async Task ExecuteAsync_SavesQualityStatus_WhenTheStoredQualityStatusDiffers(
        int? matchedRankOrdinal,
        bool isAcceptable,
        bool isCutoffMet,
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
        var unrankedFile = new MediaFile
        {
            Id = Guid.NewGuid(),
            FileKind = "Video",
            FilePath = "/movies/Super Troopers 3 (2026)/Super Troopers 3 (2026).mp4",
            FileSize = 3_000_000_000,
            CreatedAt = new(2026, 9, 23, 0, 0, 0, DateTimeKind.Utc)
        };
        var content = new ContentItem
        {
            Id = Guid.NewGuid(),
            ContentHierarchyItemId = hierarchyId,
            Fulfillment = FulfillmentStatus.Upgradable,
            QualityStatus = storedQualityStatus,
            MediaReconciledAt = new(2026, 9, 26, 3, 0, 0, DateTimeKind.Utc),
            MediaFiles = { file, unrankedFile }
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
            .Setup(service => service.ScoreFileForContentAsync(file, hierarchyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReleaseAssessment
            {
                MatchResult = ReleaseMatchResult.Indeterminate,
                MatchedRankOrdinal = matchedRankOrdinal,
                IsAcceptable = isAcceptable,
                IsExcluded = false,
                IsCutoffMet = isCutoffMet
            });
        evaluation
            .Setup(service => service.ScoreFileForContentAsync(
                unrankedFile,
                hierarchyId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReleaseAssessment
            {
                MatchResult = ReleaseMatchResult.Indeterminate,
                MatchedRankOrdinal = null,
                IsAcceptable = false,
                IsExcluded = false,
                IsCutoffMet = false
            });
        var tierLists = new Mock<IQualityTierListRepository>();
        tierLists
            .Setup(repository => repository.GetByIdAsync(tierListId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QualityTierList
            {
                Id = tierListId,
                Name = "Video - HD",
                UpgradeAllowed = true,
                CreatedAt = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)
            });
        var transactions = new Mock<ITransactionManager>();
        transactions
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
                operation(cancellationToken));
        var subject = new MediaFileReconciliationJob(
            mediaFiles.Object,
            contentItems.Object,
            hierarchy.Object,
            rootFolders.Object,
            contentTypes.Object,
            Mock.Of<IContentService>(),
            evaluation.Object,
            tierLists.Object,
            Mock.Of<IFileSystem>(),
            transactions.Object,
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IEventBus>(),
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

    [TestCase(true, 1)]
    [TestCase(false, 0)]
    public async Task ExecuteAsync_DeletesTheRecordOfAMissingFile_OnlyWhenTheRootFolderIsListable(
        bool rootListable,
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
            contentItems.Object,
            hierarchy.Object,
            rootFolders.Object,
            contentTypes.Object,
            Mock.Of<IContentService>(),
            Mock.Of<IReleaseEvaluationService>(),
            Mock.Of<IQualityTierListRepository>(),
            fileSystem.Object,
            transactions.Object,
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IEventBus>(),
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
}
