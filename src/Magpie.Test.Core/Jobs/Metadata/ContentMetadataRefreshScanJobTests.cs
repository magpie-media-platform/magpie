using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Jobs.Metadata;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Jobs;
using Magpie.Models.Paging;
using Magpie.Sdk.Content;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs.Metadata;

[TestFixture]
public sealed class ContentMetadataRefreshScanJobTests
{
    private readonly IProgressReporter _progressReporter = Mock.Of<IProgressReporter>();

    [Test]
    public async Task ExecuteAsync_QueuesContentMetadataRefreshItem_WhenItemsHavePendingRefresh()
    {
        // Arrange
        var firstItemId = Guid.NewGuid();
        var secondItemId = Guid.NewGuid();
        var contentHierarchyItemRepository = new Mock<IContentHierarchyItemRepository>();
        contentHierarchyItemRepository
            .Setup(r => r.QueryIdsAsync(
                It.Is<ContentHierarchyItemCriteria>(c => c.PendingMetadataRefresh == true),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([firstItemId, secondItemId]);

        var jobManager = new Mock<IJobManager>();
        jobManager
            .Setup(m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        var subject = new ContentMetadataRefreshScanJob(
            [Mock.Of<IContentHierarchyMetadataProvider>(p => p.SupportedContentTypes == new[] { "*" })],
            contentHierarchyItemRepository.Object,
            jobManager.Object,
            TimeProvider.System,
            NullLogger<ContentMetadataRefreshScanJob>.Instance,
            _progressReporter);

        var userId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var executionId = Guid.NewGuid();

        // Act
        await subject
            .ExecuteAsync(
                new JobContext(
                    executionId,
                    null,
                    null,
                    userId,
                    JobExecutionMode.Queue,
                    new Dictionary<string, string>(),
                    null,
                    correlationId),
                default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            jobManager.Verify(
                m => m.ExecuteAsync(
                    It.Is<JobEnqueueRequest>(r =>
                        r.JobType == "ContentMetadataRefreshItem" &&
                        r.ExtensionId == null &&
                        r.Parameters != null &&
                        r.Parameters["contentHierarchyItemId"] == firstItemId.ToString("D") &&
                        r.Mode == JobExecutionMode.Queue &&
                        r.UserId == userId &&
                        r.CorrelationId == correlationId),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            jobManager.Verify(
                m => m.ExecuteAsync(
                    It.Is<JobEnqueueRequest>(r =>
                        r.JobType == "ContentMetadataRefreshItem" &&
                        r.Parameters != null &&
                        r.Parameters["contentHierarchyItemId"] == secondItemId.ToString("D") &&
                        r.UserId == userId &&
                        r.CorrelationId == correlationId),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            jobManager.Verify(
                m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()),
                Times.Exactly(2));
        }
    }

    [Test]
    public async Task ExecuteAsync_DoesNotQueueContentMetadataRefreshItem_WhenNoItemsHavePendingRefreshFlag()
    {
        // Arrange
        var contentHierarchyItemRepository = new Mock<IContentHierarchyItemRepository>();
        contentHierarchyItemRepository
            .Setup(r => r.QueryIdsAsync(
                It.Is<ContentHierarchyItemCriteria>(c => c.PendingMetadataRefresh == true),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var jobManager = new Mock<IJobManager>();
        var subject = new ContentMetadataRefreshScanJob(
            [Mock.Of<IContentHierarchyMetadataProvider>(p => p.SupportedContentTypes == new[] { "*" })],
            contentHierarchyItemRepository.Object,
            jobManager.Object,
            TimeProvider.System,
            NullLogger<ContentMetadataRefreshScanJob>.Instance,
            _progressReporter);

        // Act
        await subject
            .ExecuteAsync(
                new JobContext(
                    Guid.NewGuid(),
                    null,
                    null,
                    null,
                    JobExecutionMode.Queue,
                    new Dictionary<string, string>(),
                    null,
                    Guid.NewGuid()),
                default)
            .ConfigureAwait(false);

        // Assert
        jobManager.Verify(
            m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_AsksForTheStaleItemIdsOnly_WhenAnItemGotNoMetadataRecently()
    {
        // Arrange
        var staleItemId = Guid.NewGuid();
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero));
        var contentHierarchyItemRepository = new Mock<IContentHierarchyItemRepository>();
        contentHierarchyItemRepository
            .Setup(r => r.QueryIdsAsync(
                It.Is<ContentHierarchyItemCriteria>(c => c.PendingMetadataRefresh == true &&
                    c.LastRefreshedBefore == new DateTime(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc)),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([staleItemId]);

        var jobManager = new Mock<IJobManager>();
        jobManager
            .Setup(m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        var subject = new ContentMetadataRefreshScanJob(
            [Mock.Of<IContentHierarchyMetadataProvider>(p => p.SupportedContentTypes == new[] { "*" })],
            contentHierarchyItemRepository.Object,
            jobManager.Object,
            timeProvider,
            NullLogger<ContentMetadataRefreshScanJob>.Instance,
            _progressReporter);

        // Act
        await subject
            .ExecuteAsync(
                new JobContext(
                    Guid.NewGuid(),
                    null,
                    null,
                    null,
                    JobExecutionMode.Queue,
                    new Dictionary<string, string>(),
                    null,
                    Guid.NewGuid()),
                default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            jobManager.Verify(
                m => m.ExecuteAsync(
                    It.Is<JobEnqueueRequest>(r =>
                        r.Parameters != null &&
                        r.Parameters["contentHierarchyItemId"] == staleItemId.ToString("D")),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            jobManager.Verify(
                m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()),
                Times.Once);
            contentHierarchyItemRepository.Verify(
                r => r.QueryAsync(
                    It.IsAny<ContentHierarchyItemCriteria>(),
                    It.IsAny<PagingParameter>(),
                    It.IsAny<SortParameter?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_RestrictsTheScanToTheServedContentTypes_WhenNoProviderIsAWildcard()
    {
        // Arrange
        var contentHierarchyItemRepository = new Mock<IContentHierarchyItemRepository>();
        contentHierarchyItemRepository
            .Setup(r => r.QueryIdsAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var subject = new ContentMetadataRefreshScanJob(
            [Mock.Of<IContentHierarchyMetadataProvider>(p => p.SupportedContentTypes == new[] { "movie" })],
            contentHierarchyItemRepository.Object,
            Mock.Of<IJobManager>(),
            TimeProvider.System,
            NullLogger<ContentMetadataRefreshScanJob>.Instance,
            _progressReporter);

        // Act
        await subject
            .ExecuteAsync(
                new JobContext(
                    Guid.NewGuid(),
                    null,
                    null,
                    null,
                    JobExecutionMode.Queue,
                    new Dictionary<string, string>(),
                    null,
                    Guid.NewGuid()),
                default)
            .ConfigureAwait(false);

        // Assert
        contentHierarchyItemRepository.Verify(
            r => r.QueryIdsAsync(
                It.Is<ContentHierarchyItemCriteria>(c =>
                    c.ContentType.And != null &&
                    c.ContentType.And.Count() == 1 &&
                    c.ContentType.And.First().Operator == CriteriaValueOperator.In &&
                    c.ContentType.And.First().Values!.SequenceEqual(new[] { "movie" })),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_QueuesNothing_WhenNoMetadataProviderIsEnabled()
    {
        // Arrange
        var contentHierarchyItemRepository = new Mock<IContentHierarchyItemRepository>();
        var jobManager = new Mock<IJobManager>();
        var subject = new ContentMetadataRefreshScanJob(
            [],
            contentHierarchyItemRepository.Object,
            jobManager.Object,
            TimeProvider.System,
            NullLogger<ContentMetadataRefreshScanJob>.Instance,
            _progressReporter);

        // Act
        await subject
            .ExecuteAsync(
                new JobContext(
                    Guid.NewGuid(),
                    null,
                    null,
                    null,
                    JobExecutionMode.Queue,
                    new Dictionary<string, string>(),
                    null,
                    Guid.NewGuid()),
                default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            contentHierarchyItemRepository.Verify(
                r => r.QueryIdsAsync(
                    It.IsAny<ContentHierarchyItemCriteria>(),
                    It.IsAny<PagingParameter>(),
                    It.IsAny<SortParameter?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            jobManager.Verify(
                m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }
}
