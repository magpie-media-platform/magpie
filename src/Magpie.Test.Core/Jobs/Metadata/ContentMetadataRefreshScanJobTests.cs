using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Jobs.Metadata;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Jobs;
using Magpie.Models.Paging;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs.Metadata;

[TestFixture]
public sealed class ContentMetadataRefreshScanJobTests
{
    [Test]
    public async Task ExecuteAsync_QueuesContentMetadataRefreshItem_ForItemsWithPendingRefreshFlag()
    {
        // Arrange
        var firstItemId = Guid.NewGuid();
        var secondItemId = Guid.NewGuid();
        var contentHierarchyItemRepository = new Mock<IContentHierarchyItemRepository>();
        contentHierarchyItemRepository
            .Setup(r => r.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(c => c.PendingMetadataRefresh == true),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items =
                [
                    new ContentHierarchyItem
                    {
                        Id = firstItemId,
                        Slug = "first",
                        Name = "first",
                        ContentType = "type-b",
                        Level = "series",
                        PendingMetadataRefresh = true
                    },
                    new ContentHierarchyItem
                    {
                        Id = secondItemId,
                        Slug = "second",
                        Name = "second",
                        ContentType = "type-b",
                        Level = "series",
                        PendingMetadataRefresh = true
                    }
                ]
            });

        var jobManager = new Mock<IJobManager>();
        jobManager
            .Setup(m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        var subject = new ContentMetadataRefreshScanJob(
            contentHierarchyItemRepository.Object,
            jobManager.Object,
            NullLogger<ContentMetadataRefreshScanJob>.Instance);

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
                        r.JobType == ContentMetadataRefreshItemJob.Spec.JobType &&
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
                        r.JobType == ContentMetadataRefreshItemJob.Spec.JobType &&
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
            .Setup(r => r.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(c => c.PendingMetadataRefresh == true),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>());

        var jobManager = new Mock<IJobManager>();
        var subject = new ContentMetadataRefreshScanJob(
            contentHierarchyItemRepository.Object,
            jobManager.Object,
            NullLogger<ContentMetadataRefreshScanJob>.Instance);

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
    public void Spec_DescribesAdministrativeScheduledJob_WhenRead()
    {
        // Arrange
        // Act
        var spec = ContentMetadataRefreshScanJob.Spec;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(spec.JobType, Is.EqualTo("ContentMetadataRefreshScan"));
            Assert.That(spec.DisplayName, Is.EqualTo("Content metadata refresh scan"));
            Assert.That(spec.Category, Is.EqualTo("Metadata"));
            Assert.That(spec.Scope, Is.EqualTo(JobScope.Administrative));
            Assert.That(spec.OnDemand, Is.True);
            Assert.That(spec.SupportedTriggerModes, Is.EqualTo([JobTriggerMode.Manual, JobTriggerMode.Schedule]));
            Assert.That(spec.ConcurrencyScope, Is.EqualTo(ConcurrencyScope.PerType));
            Assert.That(spec.FailurePolicy, Is.EqualTo(JobFailurePolicy.Requeue));
        }
    }
}
