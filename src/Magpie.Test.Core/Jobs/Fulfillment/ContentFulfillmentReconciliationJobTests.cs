using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Content;
using Magpie.Abstractions.Releases;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Content;
using Magpie.Core.Jobs.Fulfillment;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;
using Magpie.Models.Releases;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs.Fulfillment;

[TestFixture]
public sealed class ContentFulfillmentReconciliationJobTests
{
    private readonly IProgressReporter _progressReporter = Mock.Of<IProgressReporter>();

    private Mock<IContentService> _contentService = null!;
    private Mock<IContentHierarchyItemRepository> _hierarchyRepository = null!;
    private Mock<IContentRepository> _contentRepository = null!;
    private Mock<IDownloadRepository> _downloadRepository = null!;
    private Mock<IReleaseRepository> _releaseRepository = null!;
    private Mock<IQualityService> _qualityService = null!;
    private Mock<IReleaseEvaluationService> _releaseEvaluationService = null!;
    private IJob _subject = null!;
    private JobContext _jobContext = null!;
    private Guid _userId;

    [SetUp]
    public void SetUp()
    {
        _userId = Guid.NewGuid();

        _contentService = new Mock<IContentService>();
        _contentService
            .Setup(s => s.MaterializeFulfillmentStatusAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContentHierarchyItem?)null);
        _contentService
            .Setup(s => s.GetOrCreateContentItemAsync(
                It.IsAny<Guid>(),
                It.IsAny<DateTime>(),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentItem());

        _hierarchyRepository = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        _hierarchyRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>());

        _contentRepository = new Mock<IContentRepository> { CallBase = true };
        _contentRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<ContentItemQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentItem>());

        _downloadRepository = new Mock<IDownloadRepository> { CallBase = true };
        _downloadRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<DownloadQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Download>());

        _releaseRepository = new Mock<IReleaseRepository> { CallBase = true };
        _qualityService = new Mock<IQualityService>();
        _releaseEvaluationService = new Mock<IReleaseEvaluationService>();

        _jobContext = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: _userId,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>(),
            Event: null);

        _subject = new ContentFulfillmentReconciliationJob(
            _contentService.Object,
            _hierarchyRepository.Object,
            _contentRepository.Object,
            _downloadRepository.Object,
            _releaseRepository.Object,
            _qualityService.Object,
            _releaseEvaluationService.Object,
            TimeProvider.System,
            NullLogger<ContentFulfillmentReconciliationJob>.Instance,
            _progressReporter);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task MaterializeFulfillmentStatusAsync_RollsUpParent_WhenLeafIsFulfilled(bool includeSelf)
    {
        // Arrange
        var leaf = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ContentType = "movie",
            Level = "movie",
            Slug = "leaf",
            Name = "Leaf"
        };
        var parent = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ContentType = "movie",
            Level = "collection",
            Slug = "parent",
            Name = "Parent"
        };
        var leafContent = new ContentItem
        {
            Id = Guid.NewGuid(),
            ContentHierarchyItemId = leaf.Id,
            Fulfillment = FulfillmentStatus.Upgradable,
            QualityStatus = QualityStatus.UpgradeWanted
        };
        var parentContent = new ContentItem
        {
            Id = Guid.NewGuid(),
            ContentHierarchyItemId = parent.Id,
            Fulfillment = FulfillmentStatus.Missing
        };
        var hierarchyRepository = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        hierarchyRepository
            .Setup(repository => repository.GetLineageAsync(leaf.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([leaf, parent]);
        hierarchyRepository
            .Setup(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                ContentHierarchyItemCriteria criteria,
                PagingParameter _,
                SortParameter? _,
                CancellationToken _) => new PagedList<ContentHierarchyItem>
                {
                    Items = criteria.ParentId.And.Single().Value == parent.Id ? [leaf] : []
                });
        hierarchyRepository
            .Setup(repository => repository.GetByIdAsync(leaf.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(leaf);
        var contentRepository = new Mock<IContentRepository> { CallBase = true };
        contentRepository
            .Setup(repository => repository.QueryAsync(
                It.IsAny<ContentItemQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContentItemQuery query, PagingParameter _, SortParameter? _, CancellationToken _) =>
                new PagedList<ContentItem>
                {
                    Items = query.ContentHierarchyItemId == leaf.Id ||
                        query.ContentHierarchyItemIds?.Contains(leaf.Id) == true
                        ? [leafContent]
                        : [parentContent]
                });
        contentRepository
            .Setup(repository => repository.UpdateAsync(
                It.IsAny<ContentItem>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var transactionManager = new Mock<ITransactionManager>();
        transactionManager
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
                operation(cancellationToken));
        transactionManager
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<bool>> operation, CancellationToken cancellationToken) =>
                operation(cancellationToken));
        var subject = new ContentService(
            hierarchyRepository.Object,
            contentRepository.Object,
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IRootFolderRepository>(),
            Mock.Of<IQualityTierListRepository>(),
            Mock.Of<IMetadataContainerRepository>(),
            [],
            Mock.Of<IDownloadRepository>(),
            Mock.Of<IDownloadClientRepository>(),
            [],
            transactionManager.Object,
            Mock.Of<IContentTypeRegistry>(),
            Mock.Of<IContentFileService>(),
            TimeProvider.System,
            NullLogger<ContentService>.Instance);

        // Act
        await subject.MaterializeFulfillmentStatusAsync(leaf.Id, _userId, includeSelf).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(leafContent.Fulfillment, Is.EqualTo(FulfillmentStatus.Upgradable));
            Assert.That(parentContent.Fulfillment, Is.EqualTo(FulfillmentStatus.Upgradable));
        }

        contentRepository.Verify(
            repository => repository.UpdateAsync(
                It.Is<ContentItem>(item => item.ContentHierarchyItemId == leaf.Id),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        contentRepository.Verify(
            repository => repository.UpdateAsync(
                It.Is<ContentItem>(item => item.ContentHierarchyItemId == parent.Id &&
                    item.Fulfillment == FulfillmentStatus.Upgradable),
                _userId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_RecomputesStoredEffectiveValues_WhenReconciliationStarts()
    {
        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _hierarchyRepository.Verify(r => r.RecomputeAllEffectiveValuesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_QueriesOneLatestImportPerLeaf_WhenManyReconciledLeavesExist()
    {
        // Arrange
        var parentId = Guid.NewGuid();
        var leaves = Enumerable.Range(0, 200)
            .Select(index => new ContentHierarchyItem
            {
                Id = Guid.NewGuid(),
                ParentId = parentId,
                ContentType = "tv",
                Level = "episode",
                Slug = $"e{index}",
                Name = $"E{index}",
                ContentItem = new ContentItem
                {
                    Id = Guid.NewGuid(),
                    Fulfillment = FulfillmentStatus.Fulfilled
                }
            })
            .ToList();
        var items = new List<ContentHierarchyItem>
        {
            new()
            {
                Id = parentId,
                ContentType = "tv",
                Level = "season",
                Slug = "s1",
                Name = "S1",
                ContentItem = new ContentItem
                {
                    Id = Guid.NewGuid(),
                    Fulfillment = FulfillmentStatus.Fulfilled
                }
            }
        };
        items.AddRange(leaves);
        _hierarchyRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem> { Items = items, TotalItems = items.Count });

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _hierarchyRepository.Verify(
                r => r.QueryAsync(
                    It.IsAny<ContentHierarchyItemCriteria>(),
                    It.IsAny<PagingParameter>(),
                    It.IsAny<SortParameter?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _hierarchyRepository.Verify(
                r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
                Times.Never);
            _contentRepository.Verify(
                r => r.QueryAsync(
                    It.IsAny<ContentItemQuery>(),
                    It.IsAny<PagingParameter>(),
                    It.IsAny<SortParameter?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _contentRepository.Verify(
                r => r.UpdateAsync(It.IsAny<ContentItem>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
                Times.Never);
            _contentService.Verify(
                s => s.GetOrCreateContentItemAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _contentService.Verify(
                s => s.MaterializeFulfillmentStatusAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _downloadRepository.Verify(
                r => r.QueryAsync(
                    It.Is<DownloadQuery>(query => query.Status == DownloadStatus.Imported &&
                        query.ContentHierarchyItemIds!.Count == 1 &&
                        leaves.Any(leaf => leaf.Id == query.ContentHierarchyItemIds.Single())),
                    It.Is<PagingParameter>(paging => paging.PageNumber == 1 && paging.PageSize == 1),
                    It.Is<SortParameter>(sort => sort.PropertyName == "DateCompleted" &&
                        sort.Direction == SortDirection.Descending),
                    It.IsAny<CancellationToken>()),
                Times.Exactly(200));
        }
    }

    [Test]
    public async Task ExecuteAsync_MaterializesFulfillmentForTheStaleParentOnly_WhenAnotherParentAgrees()
    {
        // Arrange
        var staleParentId = Guid.NewGuid();
        var currentParentId = Guid.NewGuid();
        var staleLeafId = Guid.NewGuid();
        var currentLeafId = Guid.NewGuid();
        var items = new List<ContentHierarchyItem>
        {
            new()
            {
                Id = staleParentId,
                ContentType = "tv",
                Level = "season",
                Slug = "s1",
                Name = "S1",
                ContentItem = new ContentItem { Id = Guid.NewGuid(), Fulfillment = FulfillmentStatus.Missing }
            },
            new()
            {
                Id = currentParentId,
                ContentType = "tv",
                Level = "season",
                Slug = "s2",
                Name = "S2",
                ContentItem = new ContentItem { Id = Guid.NewGuid(), Fulfillment = FulfillmentStatus.Missing }
            },
            new()
            {
                Id = staleLeafId,
                ParentId = staleParentId,
                ContentType = "tv",
                Level = "episode",
                Slug = "e1",
                Name = "E1",
                ContentItem = new ContentItem { Id = Guid.NewGuid(), Fulfillment = FulfillmentStatus.Fulfilled }
            },
            new()
            {
                Id = currentLeafId,
                ParentId = currentParentId,
                ContentType = "tv",
                Level = "episode",
                Slug = "e2",
                Name = "E2",
                ContentItem = new ContentItem { Id = Guid.NewGuid(), Fulfillment = FulfillmentStatus.Missing }
            }
        };
        _hierarchyRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem> { Items = items, TotalItems = items.Count });

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _contentService.Verify(
                s => s.MaterializeFulfillmentStatusAsync(staleLeafId, _userId, false, It.IsAny<CancellationToken>()),
                Times.Once);
            _contentService.Verify(
                s => s.MaterializeFulfillmentStatusAsync(currentLeafId, _userId, false, It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_CreatesMissingContentItems_WhenALeafHasNone()
    {
        // Arrange
        var leafA = Guid.NewGuid();
        var leafB = Guid.NewGuid();
        var items = new List<ContentHierarchyItem>
        {
            new()
            {
                Id = leafA, ContentType = "tv", Level = "episode", Slug = "a", Name = "A"
            },
            new()
            {
                Id = leafB,
                ContentType = "tv",
                Level = "episode",
                Slug = "b",
                Name = "B",
                ContentItem = new ContentItem { Id = Guid.NewGuid() }
            }
        };
        _hierarchyRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem> { Items = items, TotalItems = items.Count });

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _contentService.Verify(
                r => r.GetOrCreateContentItemAsync(
                    leafA,
                    It.IsAny<DateTime>(),
                    _userId,
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _contentService.Verify(
                r => r.GetOrCreateContentItemAsync(
                    leafB,
                    It.IsAny<DateTime>(),
                    _userId,
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_DoesNotMaterialize_WhenNoLeavesExist()
    {
        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _contentService.Verify(
            s => s.MaterializeFulfillmentStatusAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_StampsUpgradeWanted_WhenImportedReleaseBelowCutoffAndUpgradeAllowed()
    {
        // Arrange
        var leafId = Guid.NewGuid();
        var contentItemId = Guid.NewGuid();
        var releaseId = Guid.NewGuid();
        var downloadId = Guid.NewGuid();
        var tierListId = Guid.NewGuid();
        _hierarchyRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items =
                [
                    new ContentHierarchyItem
                    {
                        Id = leafId,
                        ContentType = "movie",
                        Level = "movie",
                        Slug = "a",
                        Name = "A",
                        EffectiveQualityTierListId = tierListId,
                        ContentItem = new ContentItem { Id = contentItemId }
                    }
                ],
                TotalItems = 1
            });
        _contentRepository
            .Setup(r => r.GetByIdAsync(contentItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentItem { Id = contentItemId });
        _downloadRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<DownloadQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Download>
            {
                Items =
                [
                    new Download
                    {
                        Id = downloadId,
                        ReleaseId = releaseId,
                        DownloadClientId = Guid.NewGuid(),
                        ExternalId = Guid.NewGuid().ToString("N"),
                        Title = "dl",
                        Status = DownloadStatus.Imported,
                        CreatedAt = DateTime.UtcNow
                    }
                ],
                TotalItems = 1
            });
        _releaseRepository
            .Setup(r => r.GetByIdAsync(releaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Release
            {
                Id = releaseId,
                Protocol = "torrent",
                Title = "Imported",
                ExternalId = Guid.NewGuid().ToString("N"),
                PublishDate = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            });
        _releaseEvaluationService
            .Setup(s => s.ScoreForContentAsync(
                It.IsAny<Release>(),
                leafId,
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReleaseAssessment
            {
                MatchResult = ReleaseMatchResult.Indeterminate,
                IsAcceptable = true,
                IsCutoffMet = false
            });
        _qualityService
            .Setup(service => service.GetTierListAsync(tierListId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QualityTierList { Id = tierListId, Name = "Tiers", UpgradeAllowed = true });

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _contentRepository.Verify(
            r => r.UpdateAsync(
                It.Is<ContentItem>(item =>
                    item.Id == contentItemId && item.QualityStatus == QualityStatus.UpgradeWanted),
                _userId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_StampsMet_WhenImportedReleaseMeetsCutoff()
    {
        // Arrange
        var leafId = Guid.NewGuid();
        var contentItemId = Guid.NewGuid();
        var releaseId = Guid.NewGuid();
        var downloadId = Guid.NewGuid();
        _hierarchyRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items =
                [
                    new ContentHierarchyItem
                    {
                        Id = leafId,
                        ContentType = "movie",
                        Level = "movie",
                        Slug = "a",
                        Name = "A",
                        ContentItem = new ContentItem { Id = contentItemId }
                    }
                ],
                TotalItems = 1
            });
        _contentRepository
            .Setup(r => r.GetByIdAsync(contentItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentItem { Id = contentItemId });
        _downloadRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<DownloadQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Download>
            {
                Items =
                [
                    new Download
                    {
                        Id = downloadId,
                        ReleaseId = releaseId,
                        DownloadClientId = Guid.NewGuid(),
                        ExternalId = Guid.NewGuid().ToString("N"),
                        Title = "dl",
                        Status = DownloadStatus.Imported,
                        CreatedAt = DateTime.UtcNow
                    }
                ],
                TotalItems = 1
            });
        _releaseRepository
            .Setup(r => r.GetByIdAsync(releaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Release
            {
                Id = releaseId,
                Protocol = "torrent",
                Title = "Imported",
                ExternalId = Guid.NewGuid().ToString("N"),
                PublishDate = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            });
        _releaseEvaluationService
            .Setup(s => s.ScoreForContentAsync(
                It.IsAny<Release>(),
                leafId,
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReleaseAssessment
            {
                MatchResult = ReleaseMatchResult.Indeterminate,
                IsAcceptable = true,
                IsCutoffMet = true
            });

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _contentRepository.Verify(
            r => r.UpdateAsync(
                It.Is<ContentItem>(item => item.Id == contentItemId && item.QualityStatus == QualityStatus.Met),
                _userId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestCase(QualityStatus.Met, 1)]
    [TestCase(null, 0)]
    public async Task ExecuteAsync_ClearsQualityStatusOnlyWhenItChanges_WhenNoImportedDownloadExists(
        QualityStatus? storedQualityStatus,
        int expectedUpdateCount)
    {
        // Arrange
        var leafId = Guid.NewGuid();
        var contentItemId = Guid.NewGuid();
        _hierarchyRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items =
                [
                    new ContentHierarchyItem
                    {
                        Id = leafId,
                        ContentType = "movie",
                        Level = "movie",
                        Slug = "a",
                        Name = "A",
                        ContentItem = new ContentItem
                        {
                            Id = contentItemId,
                            QualityStatus = storedQualityStatus
                        }
                    }
                ],
                TotalItems = 1
            });
        _contentRepository
            .Setup(r => r.GetByIdAsync(contentItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentItem { Id = contentItemId, QualityStatus = storedQualityStatus });

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _contentRepository.Verify(
            r => r.UpdateAsync(
                It.Is<ContentItem>(item => item.Id == contentItemId && item.QualityStatus == null),
                _userId,
                It.IsAny<CancellationToken>()),
            Times.Exactly(expectedUpdateCount));
    }

    [Test]
    public async Task ExecuteAsync_KeepsTheStoredFulfillmentAndSearchTime_WhenAnImportLandsAfterTheSnapshot()
    {
        // Arrange
        var leafId = Guid.NewGuid();
        var contentItemId = Guid.NewGuid();
        var releaseId = Guid.NewGuid();
        var downloadId = Guid.NewGuid();
        _hierarchyRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items =
                [
                    new ContentHierarchyItem
                    {
                        Id = leafId,
                        ContentType = "movie",
                        Level = "movie",
                        Slug = "a",
                        Name = "A",
                        ContentItem = new ContentItem
                        {
                            Id = contentItemId,
                            Fulfillment = FulfillmentStatus.Missing,
                            DateLastSearched = null
                        }
                    }
                ],
                TotalItems = 1
            });
        _contentRepository
            .Setup(r => r.GetByIdAsync(contentItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentItem
            {
                Id = contentItemId,
                Fulfillment = FulfillmentStatus.Fulfilled,
                DateLastSearched = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc)
            });
        _downloadRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<DownloadQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Download>
            {
                Items =
                [
                    new Download
                    {
                        Id = downloadId,
                        ReleaseId = releaseId,
                        DownloadClientId = Guid.NewGuid(),
                        ExternalId = Guid.NewGuid().ToString("N"),
                        Title = "dl",
                        Status = DownloadStatus.Imported,
                        CreatedAt = DateTime.UtcNow
                    }
                ],
                TotalItems = 1
            });
        _releaseRepository
            .Setup(r => r.GetByIdAsync(releaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Release
            {
                Id = releaseId,
                Protocol = "torrent",
                Title = "Imported",
                ExternalId = Guid.NewGuid().ToString("N"),
                PublishDate = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            });
        _releaseEvaluationService
            .Setup(s => s.ScoreForContentAsync(
                It.IsAny<Release>(),
                leafId,
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReleaseAssessment
            {
                MatchResult = ReleaseMatchResult.Indeterminate,
                IsAcceptable = true,
                IsCutoffMet = true
            });

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _contentRepository.Verify(
            r => r.UpdateAsync(
                It.Is<ContentItem>(item =>
                    item.Id == contentItemId &&
                    item.QualityStatus == QualityStatus.Met &&
                    item.Fulfillment == FulfillmentStatus.Fulfilled &&
                    item.DateLastSearched == new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc)),
                _userId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
