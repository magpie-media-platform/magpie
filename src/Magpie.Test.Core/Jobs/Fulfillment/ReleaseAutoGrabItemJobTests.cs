using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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
    private List<AuditRecord> _audits = null!;
    private IJob _subject = null!;
    private JobContext _jobContext = null!;
    private Guid _userId;

    [SetUp]
    public void SetUp()
    {
        _userId = Guid.NewGuid();
        _audits = [];

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
            TimeProvider.System,
            NullLogger<ReleaseAutoGrabItemJob>.Instance);
    }

    [Test]
    public void Spec_DescribesPerTargetContentJob_WhenRead()
    {
        // Arrange
        // Act
        var spec = ReleaseAutoGrabItemJob.Spec;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(spec.JobType, Is.EqualTo("ReleaseAutoGrabItem"));
            Assert.That(spec.Scope, Is.EqualTo(JobScope.Content));
            Assert.That(spec.ConcurrencyScope, Is.EqualTo(ConcurrencyScope.PerTypeAndTarget));
            Assert.That(spec.LockPartitionFields, Is.EqualTo(["contentItemId"]));
            Assert.That(spec.Parameters, Has.Count.EqualTo(1));
            Assert.That(spec.Parameters[0].Name, Is.EqualTo("contentItemId"));
            Assert.That(spec.Parameters[0].Required, Is.True);
        }
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
    public async Task ExecuteAsync_EnqueuesReleaseAcquisition_ForSelectedRelease()
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
                        EffectiveMonitoring = true,
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
                EffectiveMonitoring = true
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
    public async Task ExecuteAsync_WritesGrabAttemptedAudit_OnEnqueue()
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
                        EffectiveMonitoring = true,
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
                EffectiveMonitoring = true
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
                        EffectiveMonitoring = true,
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

    [Test]
    public async Task ExecuteAsync_DoesNotEnqueue_WhenItemNoLongerEffectivelyMonitored()
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
                        EffectiveMonitoring = true,
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
                Slug = "unmonitored",
                Name = "Unmonitored",
                EffectiveMonitoring = false
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
                        EffectiveMonitoring = true,
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
                EffectiveMonitoring = true
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
}
