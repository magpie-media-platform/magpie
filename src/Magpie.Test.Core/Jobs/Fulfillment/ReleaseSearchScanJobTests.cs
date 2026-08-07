using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Jobs;
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

        _jobExecutionRepository = new Mock<IJobExecutionRepository>();
        _jobExecutionRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<JobExecutionQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution>());

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

        _subject = new ReleaseSearchScanJob(
            _jobManager.Object,
            _jobExecutionRepository.Object,
            _hierarchyRepository.Object,
            TimeProvider.System,
            NullLogger<ReleaseSearchScanJob>.Instance);
    }

    private void SetupRoots(params Guid[] rootIds)
    {
        _hierarchyRepository
            .Setup(r => r.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(c =>
                    c.EffectiveMonitoring == true && c.HasMonitoredParent == false),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>
            {
                Items =
                [
                    .. rootIds.Select(id => new ContentHierarchyItem
                    {
                        Id = id,
                        ContentType = "tv",
                        Level = "show",
                        Slug = id.ToString("N"),
                        Name = id.ToString("N"),
                        EffectiveMonitoring = true,
                    }),
                ],
            });
    }

    [Test]
    public void Spec_DefinesSweepParameters_WithoutRequesterTriggerModes()
    {
        // Arrange
        // Act
        var spec = ReleaseSearchScanJob.Spec;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(spec.RequesterTriggerModes, Is.Empty);
            Assert.That(spec.Parameters, Has.Count.EqualTo(1));
            Assert.That(spec.Parameters[0].Name, Is.EqualTo("backpressureThreshold"));
            Assert.That(spec.Parameters[0].DefaultValue, Is.EqualTo("50"));
        }
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
    public async Task ExecuteAsync_EnqueuesReleaseSearch_ForEachSubtreeRoot()
    {
        // Arrange
        var rootId = Guid.NewGuid();
        SetupRoots(rootId);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            m => m.ExecuteAsync(
                It.Is<JobEnqueueRequest>(r =>
                    r.JobType == MagpieJobs.Types.ReleaseSearchItem &&
                    r.Parameters != null &&
                    r.Parameters[MagpieJobs.Parameters.ContentHierarchyItemId] ==
                        rootId.ToString("D") &&
                    r.Parameters[MagpieJobs.Parameters.SearchKind] ==
                        MagpieJobs.ParameterValues.NodeSearchKind &&
                    r.Mode == JobExecutionMode.Queue &&
                    r.UserId == _userId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_SelectsMonitoredRoots_WithoutFilteringOnResidualWork()
    {
        // Arrange
        SetupRoots(Guid.NewGuid());

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _hierarchyRepository.Verify(
            r => r.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(c =>
                    c.EffectiveMonitoring == true &&
                    c.HasMonitoredParent == false &&
                    c.LeafOnly == null &&
                    !c.Fulfillment.And.Any()),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_ContinuesProcessingSiblings_WhenEnqueueThrowsForOneRoot()
    {
        // Arrange
        var firstRootId = Guid.NewGuid();
        var secondRootId = Guid.NewGuid();
        SetupRoots(firstRootId, secondRootId);
        _jobManager
            .Setup(m => m.ExecuteAsync(
                It.Is<JobEnqueueRequest>(r =>
                    r.Parameters != null &&
                    r.Parameters[MagpieJobs.Parameters.ContentHierarchyItemId] ==
                        firstRootId.ToString("D")),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            m => m.ExecuteAsync(
                It.Is<JobEnqueueRequest>(r =>
                    r.JobType == MagpieJobs.Types.ReleaseSearchItem &&
                    r.Parameters != null &&
                    r.Parameters[MagpieJobs.Parameters.ContentHierarchyItemId] ==
                        secondRootId.ToString("D") &&
                    r.Mode == JobExecutionMode.Queue &&
                    r.UserId == _userId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_Bails_WhenReleaseSearchBacklogExceedsThreshold()
    {
        // Arrange
        SetupRoots(Guid.NewGuid());
        var rows = Enumerable.Range(0, DefaultBackpressureThreshold + 1)
            .Select(_ => new JobExecution
            {
                JobType = MagpieJobs.Types.ReleaseSearchItem,
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
            .ReturnsAsync(new PagedList<JobExecution> { Items = rows });

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
        SetupRoots(Guid.NewGuid());
        var rows = Enumerable.Range(0, DefaultBackpressureThreshold)
            .Select(_ => new JobExecution
            {
                JobType = MagpieJobs.Types.ReleaseSearchItem,
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
            .ReturnsAsync(new PagedList<JobExecution> { Items = rows });

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            m => m.ExecuteAsync(
                It.Is<JobEnqueueRequest>(r =>
                    r.JobType == MagpieJobs.Types.ReleaseSearchItem &&
                    r.Mode == JobExecutionMode.Queue &&
                    r.UserId == _userId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public void ExecuteAsync_PropagatesOperationCanceledException_FromJobManager()
    {
        // Arrange
        SetupRoots(Guid.NewGuid());
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
