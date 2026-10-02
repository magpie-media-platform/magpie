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
public sealed class ReleaseSearchScanJobTests
{
    private readonly IProgressReporter _progressReporter = Mock.Of<IProgressReporter>();

    private const int DefaultBackpressureThreshold = 50;

    private Mock<IJobManager> _jobManager = null!;
    private Mock<IJobExecutionRepository> _jobExecutionRepository = null!;
    private Mock<IContentHierarchyItemRepository> _hierarchyRepository = null!;
    private IReadOnlyDictionary<Guid, ContentHierarchyItemAncestry> _ancestry = null!;
    private IReadOnlyList<Guid> _unfulfilledLeafIds = null!;
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

        _jobExecutionRepository = new Mock<IJobExecutionRepository>();
        _jobExecutionRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<JobExecutionQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution>());

        _unfulfilledLeafIds = [];
        _ancestry = new Dictionary<Guid, ContentHierarchyItemAncestry>();
        _hierarchyRepository = new Mock<IContentHierarchyItemRepository>();
        _hierarchyRepository
            .Setup(r => r.QueryIdsAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _unfulfilledLeafIds);
        _hierarchyRepository
            .Setup(r => r.GetAncestryAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _ancestry);

        _jobContext = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: _userId,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>(),
            Event: null);

        _subject = new ReleaseSearchScanJob(
            _jobManager.Object,
            _jobExecutionRepository.Object,
            _hierarchyRepository.Object,
            TimeProvider.System,
            NullLogger<ReleaseSearchScanJob>.Instance,
            _progressReporter);
    }

    [Test]
    public async Task ExecuteAsync_DoesNothing_WhenNoRootsExist()
    {
        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_EnqueuesReleaseSearch_WhenMultipleSubtreeRootsNeedSearch()
    {
        // Arrange
        var rootId = Guid.NewGuid();
        _unfulfilledLeafIds = [rootId];
        _ancestry = new Dictionary<Guid, ContentHierarchyItemAncestry>
        {
            [rootId] = new() { Id = rootId, EffectiveMonitoring = MonitoringMode.Monitored }
        };

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            m => m.ExecuteAsync(
                It.Is<JobEnqueueRequest>(r =>
                    r.JobType == "ReleaseSearchItem" &&
                    r.Parameters != null &&
                    r.Parameters["contentHierarchyItemId"] ==
                        rootId.ToString("D") &&
                    r.Parameters["searchKind"] ==
                        "node" &&
                    r.Mode == JobExecutionMode.Queue &&
                    r.UserId == _userId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_QueriesUnfulfilledLeavesAndAncestry_WhenLeavesNeedSearch()
    {
        // Arrange
        var leafId = Guid.NewGuid();
        _unfulfilledLeafIds = [leafId];
        _ancestry = new Dictionary<Guid, ContentHierarchyItemAncestry>
        {
            [leafId] = new() { Id = leafId, EffectiveMonitoring = MonitoringMode.Monitored }
        };

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _hierarchyRepository.Verify(
                r => r.QueryIdsAsync(
                    It.Is<ContentHierarchyItemCriteria>(c =>
                        c.LeafOnly == true &&
                        c.EffectiveMonitoring == true &&
                        c.ImportPending == false &&
                        c.Fulfillment.And.Any(criterion =>
                            criterion.Operator == CriteriaValueOperator.In &&
                            criterion.Values!.Contains(FulfillmentStatus.Missing) &&
                            criterion.Values!.Contains(FulfillmentStatus.Upgradable))),
                    It.IsAny<PagingParameter>(),
                    It.IsAny<SortParameter?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _hierarchyRepository.Verify(
                r => r.GetAncestryAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()),
                Times.Once);
            _hierarchyRepository.Verify(
                r => r.QueryAsync(
                    It.IsAny<ContentHierarchyItemCriteria>(),
                    It.IsAny<PagingParameter>(),
                    It.IsAny<SortParameter?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_EnqueuesReleaseSearch_WhenTheLibraryChangesAfterTheFirstPage()
    {
        // Arrange
        var firstPage = Enumerable.Range(0, 500).Select(_ => Guid.NewGuid()).ToList();
        var lastOfFirstPage = firstPage[^1];
        var afterTheChangeId = Guid.NewGuid();
        _ancestry = new Dictionary<Guid, ContentHierarchyItemAncestry>
        {
            [afterTheChangeId] = new() { Id = afterTheChangeId, EffectiveMonitoring = MonitoringMode.Monitored }
        };
        _hierarchyRepository
            .Setup(r => r.QueryIdsAsync(
                It.Is<ContentHierarchyItemCriteria>(c => c.AfterId == null),
                It.Is<PagingParameter>(p => p.PageNumber == 1),
                It.Is<SortParameter?>(s =>
                    s != null && s.PropertyName == "Id" && s.Direction == SortDirection.Ascending),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(firstPage);
        _hierarchyRepository
            .Setup(r => r.QueryIdsAsync(
                It.Is<ContentHierarchyItemCriteria>(c => c.AfterId == lastOfFirstPage),
                It.IsAny<PagingParameter>(),
                It.Is<SortParameter?>(s =>
                    s != null && s.PropertyName == "Id" && s.Direction == SortDirection.Ascending),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([afterTheChangeId]);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            m => m.ExecuteAsync(
                It.Is<JobEnqueueRequest>(r =>
                    r.JobType == "ReleaseSearchItem" &&
                    r.Parameters != null &&
                    r.Parameters["contentHierarchyItemId"] == afterTheChangeId.ToString("D")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_ReadsTheAncestryOnce_WhenManyNeedyLeavesShareAnAncestor()
    {
        // Arrange
        var rootId = Guid.NewGuid();
        var firstLeafId = Guid.NewGuid();
        var secondLeafId = Guid.NewGuid();
        _unfulfilledLeafIds = [firstLeafId, secondLeafId];
        _ancestry = new Dictionary<Guid, ContentHierarchyItemAncestry>
        {
            [firstLeafId] = new()
            {
                Id = firstLeafId,
                ParentId = rootId,
                EffectiveMonitoring = MonitoringMode.Monitored
            },
            [secondLeafId] = new()
            {
                Id = secondLeafId,
                ParentId = rootId,
                EffectiveMonitoring = MonitoringMode.Monitored
            },
            [rootId] = new() { Id = rootId, EffectiveMonitoring = MonitoringMode.Monitored }
        };

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _hierarchyRepository.Verify(
                r => r.GetAncestryAsync(
                    It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _hierarchyRepository.Verify(
                r => r.GetLineageAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
                Times.Never);
            _jobManager.Verify(
                m => m.ExecuteAsync(
                    It.Is<JobEnqueueRequest>(r =>
                        r.Parameters != null &&
                        r.Parameters["contentHierarchyItemId"] == rootId.ToString("D")),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_EnqueuesReleaseSearchForTheSubtreeRoot_WhenTheNeedyLeafIsNested()
    {
        // Arrange
        var rootId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        var leafId = Guid.NewGuid();
        var libraryId = Guid.NewGuid();
        _unfulfilledLeafIds = [leafId];
        _ancestry = new Dictionary<Guid, ContentHierarchyItemAncestry>
        {
            [leafId] = new() { Id = leafId, ParentId = seasonId, EffectiveMonitoring = MonitoringMode.Monitored },
            [seasonId] = new() { Id = seasonId, ParentId = rootId, EffectiveMonitoring = MonitoringMode.Monitored },
            [rootId] = new() { Id = rootId, ParentId = libraryId, EffectiveMonitoring = MonitoringMode.Monitored },
            [libraryId] = new() { Id = libraryId, EffectiveMonitoring = MonitoringMode.Unmonitored }
        };

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            m => m.ExecuteAsync(
                It.Is<JobEnqueueRequest>(r =>
                    r.Parameters != null &&
                    r.Parameters["contentHierarchyItemId"] == rootId.ToString("D")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_EnqueuesReleaseSearch_WhenRootIsMonitoredAndMissing()
    {
        // Arrange
        var rootId = Guid.NewGuid();
        _unfulfilledLeafIds = [rootId];
        _ancestry = new Dictionary<Guid, ContentHierarchyItemAncestry>
        {
            [rootId] = new() { Id = rootId, EffectiveMonitoring = MonitoringMode.Monitored }
        };

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            m => m.ExecuteAsync(
                It.Is<JobEnqueueRequest>(r =>
                    r.Parameters != null &&
                    r.Parameters["contentHierarchyItemId"] == rootId.ToString("D")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_DoesNotEnqueueReleaseSearch_WhenRootIsFulfilledAtCutoff()
    {
        // Arrange
        var rootId = Guid.NewGuid();
        _ancestry = new Dictionary<Guid, ContentHierarchyItemAncestry>
        {
            [rootId] = new() { Id = rootId, EffectiveMonitoring = MonitoringMode.Monitored }
        };

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_DoesNotEnqueueReleaseSearch_WhenItemIsUnmonitored()
    {
        // Arrange
        var leafId = Guid.NewGuid();
        _unfulfilledLeafIds = [leafId];
        _ancestry = new Dictionary<Guid, ContentHierarchyItemAncestry>
        {
            [leafId] = new() { Id = leafId, EffectiveMonitoring = MonitoringMode.Unmonitored }
        };

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_ContinuesProcessingSiblings_WhenAcquisitionIsEnqueuedThrowsForOneRoot()
    {
        // Arrange
        var firstRootId = Guid.NewGuid();
        var secondRootId = Guid.NewGuid();
        _unfulfilledLeafIds = [firstRootId, secondRootId];
        _ancestry = new Dictionary<Guid, ContentHierarchyItemAncestry>
        {
            [firstRootId] = new() { Id = firstRootId, EffectiveMonitoring = MonitoringMode.Monitored },
            [secondRootId] = new() { Id = secondRootId, EffectiveMonitoring = MonitoringMode.Monitored }
        };
        _jobManager
            .Setup(m => m.ExecuteAsync(
                It.Is<JobEnqueueRequest>(r =>
                    r.Parameters != null &&
                    r.Parameters["contentHierarchyItemId"] ==
                        firstRootId.ToString("D")),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            m => m.ExecuteAsync(
                It.Is<JobEnqueueRequest>(r =>
                    r.JobType == "ReleaseSearchItem" &&
                    r.Parameters != null &&
                    r.Parameters["contentHierarchyItemId"] ==
                        secondRootId.ToString("D") &&
                    r.Mode == JobExecutionMode.Queue &&
                    r.UserId == _userId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_Bails_WhenReleaseSearchBacklogIsOlderThanOneHour()
    {
        // Arrange
        var rootId = Guid.NewGuid();
        _unfulfilledLeafIds = [rootId];
        _ancestry = new Dictionary<Guid, ContentHierarchyItemAncestry>
        {
            [rootId] = new() { Id = rootId, EffectiveMonitoring = MonitoringMode.Monitored }
        };
        var rows = Enumerable.Range(0, DefaultBackpressureThreshold + 1)
            .Select(_ => new JobExecution
            {
                JobType = "ReleaseSearchItem",
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
                        q.JobType == "ReleaseSearchItem" &&
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
    public async Task ExecuteAsync_Bails_WhenReleaseSearchBacklogExceedsThreshold()
    {
        // Arrange
        var rootId = Guid.NewGuid();
        _unfulfilledLeafIds = [rootId];
        _ancestry = new Dictionary<Guid, ContentHierarchyItemAncestry>
        {
            [rootId] = new() { Id = rootId, EffectiveMonitoring = MonitoringMode.Monitored }
        };
        var rows = Enumerable.Range(0, DefaultBackpressureThreshold + 1)
            .Select(_ => new JobExecution
            {
                JobType = "ReleaseSearchItem",
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
    public async Task ExecuteAsync_ProceedsWithEnqueue_WhenReleaseSearchBacklogIsAtThreshold()
    {
        // Arrange
        var rootId = Guid.NewGuid();
        _unfulfilledLeafIds = [rootId];
        _ancestry = new Dictionary<Guid, ContentHierarchyItemAncestry>
        {
            [rootId] = new() { Id = rootId, EffectiveMonitoring = MonitoringMode.Monitored }
        };
        var rows = Enumerable.Range(0, DefaultBackpressureThreshold)
            .Select(_ => new JobExecution
            {
                JobType = "ReleaseSearchItem",
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
                    r.JobType == "ReleaseSearchItem" &&
                    r.Mode == JobExecutionMode.Queue &&
                    r.UserId == _userId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_Bails_WhenReleaseSearchBacklogExceedsAThresholdAboveThePageSizeLimit()
    {
        // Arrange
        var rootId = Guid.NewGuid();
        _unfulfilledLeafIds = [rootId];
        _ancestry = new Dictionary<Guid, ContentHierarchyItemAncestry>
        {
            [rootId] = new() { Id = rootId, EffectiveMonitoring = MonitoringMode.Monitored }
        };
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
    public void ExecuteAsync_PropagatesOperationCanceledException_WhenTheJobManagerCancels()
    {
        // Arrange
        var rootId = Guid.NewGuid();
        _unfulfilledLeafIds = [rootId];
        _ancestry = new Dictionary<Guid, ContentHierarchyItemAncestry>
        {
            [rootId] = new() { Id = rootId, EffectiveMonitoring = MonitoringMode.Monitored }
        };
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
