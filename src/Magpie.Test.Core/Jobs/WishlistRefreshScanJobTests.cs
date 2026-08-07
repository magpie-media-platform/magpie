using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Jobs;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Jobs;
using Magpie.Models.Paging;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs;

[TestFixture]
public sealed class WishlistRefreshScanJobTests
{
    private Mock<IWishlistRepository> _wishlistRepository = null!;
    private Mock<IJobManager> _jobManager = null!;
    private IJob _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _wishlistRepository = new Mock<IWishlistRepository>();
        _jobManager = new Mock<IJobManager>();
        _jobManager
            .Setup(m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        _subject = new WishlistRefreshScanJob(
            _wishlistRepository.Object,
            _jobManager.Object,
            Mock.Of<IProgressReporter>(),
            NullLogger<WishlistRefreshScanJob>.Instance);
    }

    [Test]
    public async Task ExecuteAsync_EnqueuesWishlistRefreshItem_ForEachMonitoredWishlist()
    {
        // Arrange
        var firstWishlistId = Guid.NewGuid();
        var secondWishlistId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        _wishlistRepository
            .Setup(r => r.QueryAsync(
                It.Is<WishlistQuery>(q => q.Monitored == true),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Wishlist>
            {
                Items =
                [
                    new Wishlist { Id = firstWishlistId, Name = "first", Enabled = true },
                    new Wishlist { Id = secondWishlistId, Name = "second", Enabled = true }
                ]
            });

        // Act
        await _subject.ExecuteAsync(
            new JobContext(
                ExecutionId: Guid.NewGuid(),
                ScheduleTriggerId: null,
                EventTriggerId: null,
                ManualTriggerUserId: userId,
                ExecutionMode: JobExecutionMode.Queue,
                Parameters: new Dictionary<string, string>(),
                Event: null,
                CorrelationId: correlationId)).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _jobManager.Verify(
                m => m.ExecuteAsync(
                    It.Is<JobEnqueueRequest>(r =>
                        r.JobType == WishlistRefreshItemJob.Spec.JobType &&
                        r.Mode == JobExecutionMode.Queue &&
                        r.UserId == userId &&
                        r.CorrelationId == correlationId &&
                        r.Parameters != null &&
                        r.Parameters["wishlistId"] == firstWishlistId.ToString("D")),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _jobManager.Verify(
                m => m.ExecuteAsync(
                    It.Is<JobEnqueueRequest>(r =>
                        r.JobType == WishlistRefreshItemJob.Spec.JobType &&
                        r.Parameters != null &&
                        r.Parameters["wishlistId"] == secondWishlistId.ToString("D")),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _jobManager.Verify(
                m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()),
                Times.Exactly(2));
        }
    }

    [Test]
    public async Task ExecuteAsync_DoesNotEnqueue_WhenNoMonitoredWishlists()
    {
        // Arrange
        _wishlistRepository
            .Setup(r => r.QueryAsync(
                It.Is<WishlistQuery>(q => q.Monitored == true),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Wishlist>());

        // Act
        await _subject.ExecuteAsync(
            new JobContext(
                ExecutionId: Guid.NewGuid(),
                ScheduleTriggerId: null,
                EventTriggerId: null,
                ManualTriggerUserId: Guid.NewGuid(),
                ExecutionMode: JobExecutionMode.Queue,
                Parameters: new Dictionary<string, string>(),
                Event: null,
                CorrelationId: Guid.NewGuid())).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public void Spec_DescribesAdministrativePerTypeScan_WhenRead()
    {
        // Arrange
        // Act
        var spec = WishlistRefreshScanJob.Spec;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(spec.JobType, Is.EqualTo("WishlistRefreshScan"));
            Assert.That(spec.DisplayName, Is.EqualTo("Wishlist refresh scan"));
            Assert.That(spec.Category, Is.EqualTo("Content"));
            Assert.That(spec.Scope, Is.EqualTo(JobScope.Administrative));
            Assert.That(spec.ConcurrencyScope, Is.EqualTo(ConcurrencyScope.PerType));
            Assert.That(spec.LockPartitionFields, Is.Empty);
        }
    }
}
