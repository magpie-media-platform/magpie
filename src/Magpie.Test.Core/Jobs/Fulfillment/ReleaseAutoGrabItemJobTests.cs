using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Jobs.Fulfillment;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Jobs;
using Magpie.Models.Paging;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs.Fulfillment;

[TestFixture]
public sealed class ReleaseAutoGrabItemJobTests
{
    private Mock<IReleaseService> _releaseService = null!;
    private Mock<IContentHierarchyItemRepository> _hierarchyRepository = null!;
    private Mock<IJobManager> _jobManager = null!;
    private Mock<IAuditRecordRepository> _auditRecordRepository = null!;
    private Mock<ITransactionManager> _transactionManager = null!;
    private List<AuditRecord> _audits = null!;
    private int _committedTransactions;
    private IJob _subject = null!;
    private JobContext _jobContext = null!;
    private Guid _userId;

    [SetUp]
    public void SetUp()
    {
        _userId = Guid.NewGuid();
        _audits = [];
        _committedTransactions = 0;

        _releaseService = new Mock<IReleaseService>();
        _releaseService
            .Setup(s => s.SelectBestReleaseAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Release?)null);

        _hierarchyRepository = new Mock<IContentHierarchyItemRepository>();
        _hierarchyRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>());

        _jobManager = new Mock<IJobManager>();
        _jobManager
            .Setup(m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        _auditRecordRepository = new Mock<IAuditRecordRepository>();
        _auditRecordRepository
            .Setup(r => r.CreateAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()))
            .Callback((AuditRecord record, CancellationToken _) => _audits.Add(record))
            .Returns((AuditRecord record, CancellationToken _) => Task.FromResult(record));

        _transactionManager = new Mock<ITransactionManager>();
        _transactionManager
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (Func<CancellationToken, Task> operation, CancellationToken token) =>
            {
                await operation(token).ConfigureAwait(false);
                _committedTransactions++;
            });

        _jobContext = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: _userId,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>(),
            Event: null);

        _subject = new ReleaseAutoGrabItemJob(
            _releaseService.Object,
            _hierarchyRepository.Object,
            _jobManager.Object,
            _auditRecordRepository.Object,
            _transactionManager.Object,
            TimeProvider.System,
            NullLogger<ReleaseAutoGrabItemJob>.Instance);
    }

    [Test]
    public void ExecuteAsync_Throws_WhenContentItemIdMissing()
    {
        // Arrange
        var context = _jobContext with { Parameters = new Dictionary<string, string>() };

        // Act
        void Act() => _subject.ExecuteAsync(context).GetAwaiter().GetResult();

        // Assert
        var ex = Assert.Throws<ArgumentException>(Act);
        Assert.That(ex.Message, Does.Contain("contentItemId"));
    }

    [Test]
    public void ExecuteAsync_Throws_WhenContentItemIdNotGuid()
    {
        // Arrange
        var context = _jobContext with
        {
            Parameters = new Dictionary<string, string> { ["contentItemId"] = "not-a-guid" }
        };

        // Act
        void Act() => _subject.ExecuteAsync(context).GetAwaiter().GetResult();

        // Assert
        Assert.Throws<ArgumentException>(Act);
    }

    [Test]
    public async Task ExecuteAsync_EnqueuesReleaseAcquisition_WhenAReleaseIsSelected()
    {
        // Arrange
        var contentItemId = Guid.NewGuid();
        var hierarchyItemId = Guid.NewGuid();
        var release = new Release
        {
            Id = Guid.NewGuid(),
            Protocol = "torrent",
            Title = "Chosen release",
            ExternalId = Guid.NewGuid().ToString("N"),
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
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
                        Id = hierarchyItemId,
                        ContentType = "movie",
                        Level = "movie",
                        Slug = "chosen",
                        Name = "Chosen",
                        EffectiveMonitoring = MonitoringMode.Monitored,
                        ContentItem = new ContentItem { Id = contentItemId, Fulfillment = FulfillmentStatus.Missing }
                    }
                ]
            });
        _hierarchyRepository
            .Setup(r => r.GetByIdAsync(hierarchyItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyItem
            {
                Id = hierarchyItemId,
                ContentType = "movie",
                Level = "movie",
                Slug = "chosen",
                Name = "Chosen",
                EffectiveMonitoring = MonitoringMode.Monitored
            });
        _releaseService
            .Setup(s => s.SelectBestReleaseAsync(
                contentItemId,
                _userId,
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(release);
        var context = _jobContext with
        {
            Parameters = new Dictionary<string, string> { ["contentItemId"] = contentItemId.ToString() }
        };

        // Act
        await _subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            m => m.ExecuteAsync(
                It.Is<JobEnqueueRequest>(r =>
                    r.JobType == "ReleaseDownloadItem" &&
                    r.Parameters != null &&
                    r.Parameters["releaseId"] == release.Id.ToString() &&
                    r.Parameters["contentHierarchyItemId"] == hierarchyItemId.ToString() &&
                    r.Mode == JobExecutionMode.Queue &&
                    r.UserId == _userId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_WritesGrabAttemptedAudit_WhenAcquisitionIsEnqueued()
    {
        // Arrange
        var contentItemId = Guid.NewGuid();
        var hierarchyItemId = Guid.NewGuid();
        var release = new Release
        {
            Id = Guid.NewGuid(),
            Protocol = "torrent",
            Title = "Chosen release",
            ExternalId = Guid.NewGuid().ToString("N"),
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
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
                        Id = hierarchyItemId,
                        ContentType = "movie",
                        Level = "movie",
                        Slug = "chosen",
                        Name = "Chosen",
                        EffectiveMonitoring = MonitoringMode.Monitored,
                        ContentItem = new ContentItem { Id = contentItemId, Fulfillment = FulfillmentStatus.Missing }
                    }
                ]
            });
        _hierarchyRepository
            .Setup(r => r.GetByIdAsync(hierarchyItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyItem
            {
                Id = hierarchyItemId,
                ContentType = "movie",
                Level = "movie",
                Slug = "chosen",
                Name = "Chosen",
                EffectiveMonitoring = MonitoringMode.Monitored
            });
        _releaseService
            .Setup(s => s.SelectBestReleaseAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(release);
        var context = _jobContext with
        {
            Parameters = new Dictionary<string, string> { ["contentItemId"] = contentItemId.ToString() }
        };

        // Act
        await _subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_audits, Has.Exactly(1).Items);
            Assert.That(_audits[0].Operation, Is.EqualTo("GrabAttempted"));
            Assert.That(_audits[0].EntityId, Is.EqualTo(contentItemId.ToString()));
            Assert.That(_audits[0].Detail, Does.Contain(release.Id.ToString()));
        }
    }

    [Test]
    public void ExecuteAsync_FailsWithoutCommittingTheEnqueue_WhenTheGrabAttemptedAuditWriteFails()
    {
        // Arrange
        var contentItemId = Guid.NewGuid();
        var hierarchyItemId = Guid.NewGuid();
        var release = new Release
        {
            Id = Guid.NewGuid(),
            Protocol = "torrent",
            Title = "Chosen release",
            ExternalId = Guid.NewGuid().ToString("N"),
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
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
                        Id = hierarchyItemId,
                        ContentType = "movie",
                        Level = "movie",
                        Slug = "chosen",
                        Name = "Chosen",
                        EffectiveMonitoring = MonitoringMode.Monitored,
                        ContentItem = new ContentItem { Id = contentItemId, Fulfillment = FulfillmentStatus.Missing }
                    }
                ]
            });
        _releaseService
            .Setup(s => s.SelectBestReleaseAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(release);
        _auditRecordRepository
            .Setup(r => r.CreateAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("The audit store rejected the record"));
        var context = _jobContext with
        {
            Parameters = new Dictionary<string, string> { ["contentItemId"] = contentItemId.ToString() }
        };

        // Act
        void Act() => _subject.ExecuteAsync(context).GetAwaiter().GetResult();

        // Assert
        Assert.Throws<InvalidOperationException>(Act);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_committedTransactions, Is.EqualTo(0));
            _jobManager.Verify(
                m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()),
                Times.Once);
            _auditRecordRepository.Verify(
                r => r.CreateAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_DoesNotEnqueue_WhenContentItemNoLongerExists()
    {
        // Arrange
        var context = _jobContext with
        {
            Parameters = new Dictionary<string, string> { ["contentItemId"] = Guid.NewGuid().ToString() }
        };

        // Act
        await _subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_DoesNotEnqueue_WhenContentItemAlreadyFulfilled()
    {
        // Arrange
        var contentItemId = Guid.NewGuid();
        var hierarchyItemId = Guid.NewGuid();
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
                        Id = hierarchyItemId,
                        ContentType = "movie",
                        Level = "movie",
                        Slug = "done",
                        Name = "Done",
                        EffectiveMonitoring = MonitoringMode.Monitored,
                        ContentItem = new ContentItem { Id = contentItemId, Fulfillment = FulfillmentStatus.Fulfilled }
                    }
                ]
            });
        var context = _jobContext with
        {
            Parameters = new Dictionary<string, string> { ["contentItemId"] = contentItemId.ToString() }
        };

        // Act
        await _subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestCase(MonitoringMode.Unmonitored, false)]
    [TestCase(MonitoringMode.Monitored, true)]
    public async Task ExecuteAsync_DoesNotEnqueue_WhenItemIsUnmonitoredOrAFileWaitsToImport(
        MonitoringMode monitoring,
        bool importPending)
    {
        // Arrange
        var contentItemId = Guid.NewGuid();
        var hierarchyItemId = Guid.NewGuid();
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
                        Id = hierarchyItemId,
                        ContentType = "movie",
                        Level = "movie",
                        Slug = "unmonitored",
                        Name = "Unmonitored",
                        EffectiveMonitoring = monitoring,
                        ImportPending = importPending,
                        ContentItem = new ContentItem { Id = contentItemId, Fulfillment = FulfillmentStatus.Missing }
                    }
                ]
            });
        var context = _jobContext with
        {
            Parameters = new Dictionary<string, string> { ["contentItemId"] = contentItemId.ToString() }
        };

        // Act
        await _subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _jobManager.Verify(
                m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()),
                Times.Never);
            _releaseService.Verify(
                s => s.SelectBestReleaseAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_DoesNotEnqueue_WhenNoReleaseSelected()
    {
        // Arrange
        var contentItemId = Guid.NewGuid();
        var hierarchyItemId = Guid.NewGuid();
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
                        Id = hierarchyItemId,
                        ContentType = "movie",
                        Level = "movie",
                        Slug = "nomatch",
                        Name = "No match",
                        EffectiveMonitoring = MonitoringMode.Monitored,
                        ContentItem = new ContentItem { Id = contentItemId, Fulfillment = FulfillmentStatus.Missing }
                    }
                ]
            });
        _hierarchyRepository
            .Setup(r => r.GetByIdAsync(hierarchyItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyItem
            {
                Id = hierarchyItemId,
                ContentType = "movie",
                Level = "movie",
                Slug = "nomatch",
                Name = "No match",
                EffectiveMonitoring = MonitoringMode.Monitored
            });
        var context = _jobContext with
        {
            Parameters = new Dictionary<string, string> { ["contentItemId"] = contentItemId.ToString() }
        };

        // Act
        await _subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestCase(MonitoringMode.Shotgun, false, 2, 1)]
    [TestCase(MonitoringMode.Monitored, false, 1, 0)]
    [TestCase(MonitoringMode.Shotgun, true, 0, 0)]
    public async Task ExecuteAsync_QueuesEveryShotgunRelease_WhenContentUsesShotgun(
        MonitoringMode monitoring,
        bool importPending,
        int expectedQueued,
        int expectedShotgunSelections)
    {
        // Arrange
        var contentItemId = Guid.NewGuid();
        var hierarchyItemId = Guid.NewGuid();
        var best = new Release
        {
            Id = Guid.NewGuid(),
            Protocol = "torrent",
            Title = "Best release",
            ExternalId = Guid.NewGuid().ToString("N"),
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        var other = new Release
        {
            Id = Guid.NewGuid(),
            Protocol = "torrent",
            Title = "Other release",
            ExternalId = Guid.NewGuid().ToString("N"),
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
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
                        Id = hierarchyItemId,
                        ContentType = "movie",
                        Level = "movie",
                        Slug = "chosen",
                        Name = "Chosen",
                        EffectiveMonitoring = monitoring,
                        ImportPending = importPending,
                        ContentItem = new ContentItem { Id = contentItemId, Fulfillment = FulfillmentStatus.Missing }
                    }
                ]
            });
        _releaseService
            .Setup(s => s.SelectBestReleaseAsync(
                contentItemId,
                _userId,
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(best);
        _releaseService
            .Setup(s => s.SelectAcceptableReleasesAsync(
                contentItemId,
                _userId,
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([best, other]);
        var context = _jobContext with
        {
            Parameters = new Dictionary<string, string> { ["contentItemId"] = contentItemId.ToString() }
        };

        // Act
        await _subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _jobManager.Verify(
                m => m.ExecuteAsync(
                    It.Is<JobEnqueueRequest>(r =>
                        r.JobType == "ReleaseDownloadItem" &&
                        r.Parameters!["contentHierarchyItemId"] == hierarchyItemId.ToString()),
                    It.IsAny<CancellationToken>()),
                Times.Exactly(expectedQueued));
            _releaseService.Verify(
                s => s.SelectAcceptableReleasesAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Exactly(expectedShotgunSelections));
        }
    }
}
