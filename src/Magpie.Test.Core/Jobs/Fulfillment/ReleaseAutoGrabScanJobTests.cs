using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Jobs.Fulfillment;
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

namespace Magpie.Test.Core.Jobs.Fulfillment;

[TestFixture]
public sealed class ReleaseAutoGrabScanJobTests
{
    private readonly IProgressReporter _progressReporter = Mock.Of<IProgressReporter>();

    private const int DefaultBackpressureThreshold = 50;

    private Mock<IJobManager> _jobManager = null!;
    private Mock<IJobExecutionRepository> _jobExecutionRepository = null!;
    private Mock<IContentHierarchyItemRepository> _hierarchyRepository = null!;
    private IJob _subject = null!;
    private JobContext _jobContext = null!;
    private Guid _userId;

    [SetUp]
    public void SetUp()
    {
        _userId = Guid.NewGuid();

        _jobManager = new Mock<IJobManager>();
        _jobManager
            .Setup(m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        _jobManager
            .Setup(m => m.ResolveLockPartition(
                "ReleaseAutoGrabItem",
                It.IsAny<IReadOnlyDictionary<string, string?>>()))
            .Returns((string jobType, IReadOnlyDictionary<string, string?> parameters) =>
                $"type:{jobType}|{"contentItemId"}=" +
                parameters["contentItemId"]);

        _jobExecutionRepository = new Mock<IJobExecutionRepository>();
        _jobExecutionRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<JobExecutionQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution>());
        _jobExecutionRepository
            .Setup(r => r.GetLastAttemptTimesByLockPartitionAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, DateTime>(StringComparer.Ordinal));

        _hierarchyRepository = new Mock<IContentHierarchyItemRepository>();
        _hierarchyRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>());

        _jobContext = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: _userId,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>(),
            Event: null);

        _subject = new ReleaseAutoGrabScanJob(
            _jobManager.Object,
            _jobExecutionRepository.Object,
            _hierarchyRepository.Object,
            TimeProvider.System,
            NullLogger<ReleaseAutoGrabScanJob>.Instance,
            _progressReporter);
    }

    [Test]
    public async Task ExecuteAsync_DoesNothing_WhenNoCandidatesExist()
    {
        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_FansOutAutoGrabItem_WhenEligibleCandidatesExist()
    {
        // Arrange
        var contentAId = Guid.NewGuid();
        var contentBId = Guid.NewGuid();
        _hierarchyRepository
            .Setup(r => r.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(c => c.Fulfillment != null &&
                    c.Fulfillment.And.Any(p => p.Value == FulfillmentStatus.Missing)),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items =
                [
                    new ContentHierarchyItem
                    {
                        Id = Guid.NewGuid(),
                        ContentType = "movie",
                        Level = "movie",
                        Slug = "a",
                        Name = "A",
                        EffectiveMonitoring = true,
                        ContentItem = new ContentItem
                        {
                            Id = contentAId,
                            Fulfillment = FulfillmentStatus.Missing,
                            DateLastSearched = DateTime.UtcNow.AddDays(-1)
                        }
                    },
                    new ContentHierarchyItem
                    {
                        Id = Guid.NewGuid(),
                        ContentType = "movie",
                        Level = "movie",
                        Slug = "b",
                        Name = "B",
                        EffectiveMonitoring = true,
                        ContentItem = new ContentItem
                        {
                            Id = contentBId,
                            Fulfillment = FulfillmentStatus.Missing,
                            DateLastSearched = DateTime.UtcNow.AddDays(-1)
                        }
                    }
                ]
            });

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _jobManager.Verify(
                m => m.ExecuteAsync(
                    It.Is<JobEnqueueRequest>(r =>
                        r.JobType == "ReleaseAutoGrabItem" &&
                        r.Parameters != null &&
                        r.Parameters["contentItemId"] == contentAId.ToString() &&
                        r.Mode == JobExecutionMode.Queue &&
                        r.UserId == _userId),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _jobManager.Verify(
                m => m.ExecuteAsync(
                    It.Is<JobEnqueueRequest>(r =>
                        r.JobType == "ReleaseAutoGrabItem" &&
                        r.Parameters != null &&
                        r.Parameters["contentItemId"] == contentBId.ToString() &&
                        r.Mode == JobExecutionMode.Queue &&
                        r.UserId == _userId),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_FansOutAutoGrabItem_WhenAnUpgradableCandidateExists()
    {
        // Arrange
        var contentItemId = Guid.NewGuid();
        _hierarchyRepository
            .Setup(r => r.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(c => c.Fulfillment != null &&
                    c.Fulfillment.And.Any(p => p.Value == FulfillmentStatus.Upgradable)),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items =
                [
                    new ContentHierarchyItem
                    {
                        Id = Guid.NewGuid(),
                        ContentType = "movie",
                        Level = "movie",
                        Slug = "u",
                        Name = "U",
                        EffectiveMonitoring = true,
                        ContentItem = new ContentItem
                        {
                            Id = contentItemId,
                            Fulfillment = FulfillmentStatus.Upgradable,
                            DateLastSearched = DateTime.UtcNow.AddDays(-1)
                        }
                    }
                ]
            });

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            m => m.ExecuteAsync(
                It.Is<JobEnqueueRequest>(r =>
                    r.JobType == "ReleaseAutoGrabItem" &&
                    r.Parameters != null &&
                    r.Parameters["contentItemId"] == contentItemId.ToString() &&
                    r.Mode == JobExecutionMode.Queue &&
                    r.UserId == _userId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_EnqueuesReleaseSearch_WhenCandidateNeverSearched()
    {
        // Arrange
        var hierarchyItemId = Guid.NewGuid();
        _hierarchyRepository
            .Setup(r => r.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(c => c.Fulfillment != null &&
                    c.Fulfillment.And.Any(p => p.Value == FulfillmentStatus.Missing)),
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
                        Slug = "fresh",
                        Name = "Fresh",
                        EffectiveMonitoring = true,
                        ContentItem = new ContentItem
                        {
                            Id = Guid.NewGuid(),
                            Fulfillment = FulfillmentStatus.Missing,
                            DateLastSearched = null
                        }
                    }
                ]
            });

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _jobManager.Verify(
                m => m.ExecuteAsync(
                    It.Is<JobEnqueueRequest>(r =>
                        r.JobType == "ReleaseSearchItem" &&
                        r.Parameters != null &&
                        r.Parameters["contentHierarchyItemId"] ==
                            hierarchyItemId.ToString("D") &&
                        r.Mode == JobExecutionMode.Queue &&
                        r.UserId == _userId),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _jobManager.Verify(
                m => m.ExecuteAsync(
                    It.Is<JobEnqueueRequest>(r => r.JobType == "ReleaseAutoGrabItem"),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_Bails_WhenReleaseAcquisitionBacklogIsOlderThanOneHour()
    {
        // Arrange
        _hierarchyRepository
            .Setup(r => r.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(c => c.Fulfillment != null &&
                    c.Fulfillment.And.Any(p => p.Value == FulfillmentStatus.Missing)),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items =
                [
                    new ContentHierarchyItem
                    {
                        Id = Guid.NewGuid(),
                        ContentType = "movie",
                        Level = "movie",
                        Slug = "x",
                        Name = "X",
                        EffectiveMonitoring = true,
                        ContentItem = new ContentItem
                        {
                            Id = Guid.NewGuid(),
                            Fulfillment = FulfillmentStatus.Missing,
                            DateLastSearched = DateTime.UtcNow.AddDays(-1)
                        }
                    }
                ]
            });
        var rows = Enumerable.Range(0, DefaultBackpressureThreshold + 1)
            .Select(_ => new JobExecution
            {
                JobType = "ReleaseDownloadItem",
                Scope = JobScope.System,
                Status = JobExecutionStatus.Queued,
                CreatedAt = DateTime.UtcNow.AddHours(-2)
            })
            .ToList();
        _jobExecutionRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<JobExecutionQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution> { Items = rows, TotalItems = rows.Count });

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _jobExecutionRepository.Verify(
                r => r.QueryAsync(
                    It.Is<JobExecutionQuery>(q =>
                        q.JobType == "ReleaseDownloadItem" &&
                        q.Timestamp == null),
                    It.IsAny<PagingParameter>(),
                    It.IsAny<SortParameter?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _jobManager.Verify(
                m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_Bails_WhenReleaseAcquisitionBacklogExceedsThreshold()
    {
        // Arrange
        _hierarchyRepository
            .Setup(r => r.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(c => c.Fulfillment != null &&
                    c.Fulfillment.And.Any(p => p.Value == FulfillmentStatus.Missing)),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items =
                [
                    new ContentHierarchyItem
                    {
                        Id = Guid.NewGuid(),
                        ContentType = "movie",
                        Level = "movie",
                        Slug = "x",
                        Name = "X",
                        EffectiveMonitoring = true,
                        ContentItem = new ContentItem
                        {
                            Id = Guid.NewGuid(),
                            Fulfillment = FulfillmentStatus.Missing,
                            DateLastSearched = DateTime.UtcNow.AddDays(-1)
                        }
                    }
                ]
            });
        var rows = Enumerable.Range(0, DefaultBackpressureThreshold + 1)
            .Select(_ => new JobExecution
            {
                JobType = "ReleaseDownloadItem",
                Scope = JobScope.System,
                Status = JobExecutionStatus.Queued,
                CreatedAt = DateTime.UtcNow
            })
            .ToList();
        _jobExecutionRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<JobExecutionQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution> { Items = rows, TotalItems = rows.Count });

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_Bails_WhenReleaseAcquisitionBacklogExceedsAThresholdAboveThePageSizeLimit()
    {
        // Arrange
        var context = _jobContext with
        {
            Parameters = new Dictionary<string, string> { ["backpressureThreshold"] = "2000" }
        };
        _jobExecutionRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<JobExecutionQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution> { TotalItems = 2001 });

        // Act
        await _subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_ProceedsWithFanOut_WhenReleaseAcquisitionBacklogIsAtThreshold()
    {
        // Arrange
        var contentItemId = Guid.NewGuid();
        _hierarchyRepository
            .Setup(r => r.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(c => c.Fulfillment != null &&
                    c.Fulfillment.And.Any(p => p.Value == FulfillmentStatus.Missing)),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items =
                [
                    new ContentHierarchyItem
                    {
                        Id = Guid.NewGuid(),
                        ContentType = "movie",
                        Level = "movie",
                        Slug = "x",
                        Name = "X",
                        EffectiveMonitoring = true,
                        ContentItem = new ContentItem
                        {
                            Id = contentItemId,
                            Fulfillment = FulfillmentStatus.Missing,
                            DateLastSearched = DateTime.UtcNow.AddDays(-1)
                        }
                    }
                ]
            });
        var rows = Enumerable.Range(0, DefaultBackpressureThreshold)
            .Select(_ => new JobExecution
            {
                JobType = "ReleaseDownloadItem",
                Scope = JobScope.System,
                Status = JobExecutionStatus.Queued,
                CreatedAt = DateTime.UtcNow
            })
            .ToList();
        _jobExecutionRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<JobExecutionQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution> { Items = rows, TotalItems = rows.Count });

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            m => m.ExecuteAsync(
                It.Is<JobEnqueueRequest>(r =>
                    r.JobType == "ReleaseAutoGrabItem" &&
                    r.Parameters != null &&
                    r.Parameters["contentItemId"] == contentItemId.ToString()),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_SkipsCandidate_WhenLastAutoGrabAttemptWithinCooldownWindow()
    {
        // Arrange
        var contentItemId = Guid.NewGuid();
        _hierarchyRepository
            .Setup(r => r.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(c => c.Fulfillment != null &&
                    c.Fulfillment.And.Any(p => p.Value == FulfillmentStatus.Missing)),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items =
                [
                    new ContentHierarchyItem
                    {
                        Id = Guid.NewGuid(),
                        ContentType = "movie",
                        Level = "movie",
                        Slug = "x",
                        Name = "X",
                        EffectiveMonitoring = true,
                        ContentItem = new ContentItem
                        {
                            Id = contentItemId,
                            Fulfillment = FulfillmentStatus.Missing,
                            DateLastSearched = DateTime.UtcNow.AddDays(-1)
                        }
                    }
                ]
            });
        _jobExecutionRepository
            .Setup(r => r.GetLastAttemptTimesByLockPartitionAsync(
                "ReleaseAutoGrabItem",
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, DateTime>(StringComparer.Ordinal)
            {
                [$"type:{"ReleaseAutoGrabItem"}|" +
                    $"{"contentItemId"}={contentItemId}"] =
                    DateTime.UtcNow.AddHours(-1)
            });

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            m => m.ExecuteAsync(
                It.Is<JobEnqueueRequest>(r => r.JobType == "ReleaseAutoGrabItem"),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_EnqueuesCandidate_WhenLastAutoGrabAttemptOlderThanCooldownWindow()
    {
        // Arrange
        var contentItemId = Guid.NewGuid();
        _hierarchyRepository
            .Setup(r => r.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(c => c.Fulfillment != null &&
                    c.Fulfillment.And.Any(p => p.Value == FulfillmentStatus.Missing)),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items =
                [
                    new ContentHierarchyItem
                    {
                        Id = Guid.NewGuid(),
                        ContentType = "movie",
                        Level = "movie",
                        Slug = "x",
                        Name = "X",
                        EffectiveMonitoring = true,
                        ContentItem = new ContentItem
                        {
                            Id = contentItemId,
                            Fulfillment = FulfillmentStatus.Missing,
                            DateLastSearched = DateTime.UtcNow.AddDays(-1)
                        }
                    }
                ]
            });
        _jobExecutionRepository
            .Setup(r => r.GetLastAttemptTimesByLockPartitionAsync(
                "ReleaseAutoGrabItem",
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, DateTime>(StringComparer.Ordinal)
            {
                [$"type:{"ReleaseAutoGrabItem"}|" +
                    $"{"contentItemId"}={contentItemId}"] =
                    DateTime.UtcNow.AddHours(-7)
            });

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            m => m.ExecuteAsync(
                It.Is<JobEnqueueRequest>(r =>
                    r.JobType == "ReleaseAutoGrabItem" &&
                    r.Parameters != null &&
                    r.Parameters["contentItemId"] == contentItemId.ToString()),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_HonorsRetryCooldownParameter_WhenShortened()
    {
        // Arrange
        var contentItemId = Guid.NewGuid();
        _hierarchyRepository
            .Setup(r => r.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(c => c.Fulfillment != null &&
                    c.Fulfillment.And.Any(p => p.Value == FulfillmentStatus.Missing)),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items =
                [
                    new ContentHierarchyItem
                    {
                        Id = Guid.NewGuid(),
                        ContentType = "movie",
                        Level = "movie",
                        Slug = "x",
                        Name = "X",
                        EffectiveMonitoring = true,
                        ContentItem = new ContentItem
                        {
                            Id = contentItemId,
                            Fulfillment = FulfillmentStatus.Missing,
                            DateLastSearched = DateTime.UtcNow.AddDays(-1)
                        }
                    }
                ]
            });
        _jobExecutionRepository
            .Setup(r => r.GetLastAttemptTimesByLockPartitionAsync(
                "ReleaseAutoGrabItem",
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, DateTime>(StringComparer.Ordinal)
            {
                [$"type:{"ReleaseAutoGrabItem"}|" +
                    $"{"contentItemId"}={contentItemId}"] =
                    DateTime.UtcNow.AddHours(-2)
            });
        var context = _jobContext with
        {
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal) { ["retryCooldownHours"] = "1" }
        };

        // Act
        await _subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            m => m.ExecuteAsync(
                It.Is<JobEnqueueRequest>(r =>
                    r.JobType == "ReleaseAutoGrabItem" &&
                    r.Parameters != null &&
                    r.Parameters["contentItemId"] == contentItemId.ToString()),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_EnqueuesCandidate_WhenRetryCooldownIsZero()
    {
        // Arrange
        var contentItemId = Guid.NewGuid();
        _hierarchyRepository
            .Setup(r => r.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(c => c.Fulfillment != null &&
                    c.Fulfillment.And.Any(p => p.Value == FulfillmentStatus.Missing)),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items =
                [
                    new ContentHierarchyItem
                    {
                        Id = Guid.NewGuid(),
                        ContentType = "movie",
                        Level = "movie",
                        Slug = "x",
                        Name = "X",
                        EffectiveMonitoring = true,
                        ContentItem = new ContentItem
                        {
                            Id = contentItemId,
                            Fulfillment = FulfillmentStatus.Missing,
                            DateLastSearched = DateTime.UtcNow.AddDays(-1)
                        }
                    }
                ]
            });
        _jobExecutionRepository
            .Setup(r => r.GetLastAttemptTimesByLockPartitionAsync(
                "ReleaseAutoGrabItem",
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, DateTime>(StringComparer.Ordinal)
            {
                [$"type:{"ReleaseAutoGrabItem"}|" +
                    $"{"contentItemId"}={contentItemId}"] =
                    DateTime.UtcNow.AddMinutes(-1)
            });
        var context = _jobContext with
        {
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal) { ["retryCooldownHours"] = "0" }
        };

        // Act
        await _subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            m => m.ExecuteAsync(
                It.Is<JobEnqueueRequest>(r =>
                    r.JobType == "ReleaseAutoGrabItem" &&
                    r.Parameters != null &&
                    r.Parameters["contentItemId"] == contentItemId.ToString()),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public void ExecuteAsync_PropagatesCancellation_WhenOperationIsCanceled()
    {
        // Arrange
        _hierarchyRepository
            .Setup(r => r.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(c => c.Fulfillment != null &&
                    c.Fulfillment.And.Any(p => p.Value == FulfillmentStatus.Missing)),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items =
                [
                    new ContentHierarchyItem
                    {
                        Id = Guid.NewGuid(),
                        ContentType = "movie",
                        Level = "movie",
                        Slug = "x",
                        Name = "X",
                        EffectiveMonitoring = true,
                        ContentItem = new ContentItem
                        {
                            Id = Guid.NewGuid(),
                            Fulfillment = FulfillmentStatus.Missing,
                            DateLastSearched = DateTime.UtcNow.AddDays(-1)
                        }
                    }
                ]
            });
        using var cts = new CancellationTokenSource();
        _jobManager
            .Setup(m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        // Act
        async Task Act() => await _subject.ExecuteAsync(_jobContext, cts.Token).ConfigureAwait(false);

        // Assert
        Assert.ThrowsAsync<OperationCanceledException>(Act);
    }
}
