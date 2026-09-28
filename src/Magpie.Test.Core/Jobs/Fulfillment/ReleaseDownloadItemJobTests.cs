using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Jobs.Fulfillment;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs.Fulfillment;

[TestFixture]
public sealed class ReleaseDownloadItemJobTests
{
    private readonly IProgressReporter _progressReporter = Mock.Of<IProgressReporter>();

    private Mock<IReleaseService> _releaseService = null!;
    private Mock<IDownloadRepository> _downloads = null!;
    private Guid _userId;
    private Guid _releaseId;
    private Guid _grabTargetId;

    [SetUp]
    public void SetUp()
    {
        _releaseService = new Mock<IReleaseService>();
        _downloads = new Mock<IDownloadRepository> { CallBase = true };
        _downloads.Setup(repository => repository.QueryAsync(It.IsAny<DownloadQuery>(),
            It.IsAny<PagingParameter>(), It.IsAny<SortParameter?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Download>());
        _releaseService
            .Setup(s => s.AcquireAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                It.IsAny<IProgressReporter?>(),
                It.IsAny<bool>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Download?)null);

        _userId = Guid.NewGuid();
        _releaseId = Guid.NewGuid();
        _grabTargetId = Guid.NewGuid();
    }

    [TestCase(DownloadStatus.Imported)]
    [TestCase(DownloadStatus.Importing)]
    [TestCase(DownloadStatus.Cancelled)]
    [TestCase(DownloadStatus.Downloading)]
    public async Task ExecuteAsync_SkipsDelayedCapacityRetry_WhenAnotherAcquisitionHasTakenPlace(DownloadStatus status)
    {
        // Arrange
        _downloads.Setup(repository => repository.QueryAsync(It.IsAny<DownloadQuery>(),
            It.IsAny<PagingParameter>(), It.IsAny<SortParameter?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Download>
            {
                Items = [new Download
                {
                    Title = "Film", ExternalId = "acquired", Status = status,
                    CreatedAt = new DateTime(2026, 9, 22, 12, 1, 0, DateTimeKind.Utc)
                }]
            });
        var subject = new ReleaseDownloadItemJob(_releaseService.Object, _downloads.Object, _progressReporter);
        var context = new JobContext(Guid.NewGuid(), null, null, _userId, JobExecutionMode.Queue,
            new Dictionary<string, string>
            {
                ["releaseId"] = _releaseId.ToString("D"),
                ["contentHierarchyItemId"] = _grabTargetId.ToString("D"),
                ["capacityDeferredAt"] = "2026-09-22T12:00:00.0000000Z"
            }, null);

        // Act
        await subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        _releaseService.VerifyNoOtherCalls();
    }

    [Test]
    public async Task ExecuteAsync_RechecksCapacity_WhenDeferredAcquisitionHasNotStarted()
    {
        // Arrange
        var subject = new ReleaseDownloadItemJob(_releaseService.Object, _downloads.Object, _progressReporter);
        var context = new JobContext(Guid.NewGuid(), null, null, _userId, JobExecutionMode.Queue,
            new Dictionary<string, string>
            {
                ["releaseId"] = _releaseId.ToString("D"),
                ["contentHierarchyItemId"] = _grabTargetId.ToString("D"),
                ["capacityDeferredAt"] = "2026-09-22T12:00:00.0000000Z"
            }, null);

        // Act
        await subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        _releaseService.Verify(service => service.AcquireAsync(_releaseId, _userId, null,
            _progressReporter, false, _grabTargetId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public void ExecuteAsync_ThrowsArgumentException_WhenReleaseIdParameterMissing()
    {
        // Arrange
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: _userId,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>
            {
                ["contentHierarchyItemId"] = _grabTargetId.ToString()
            },
            Event: null);
        var subject = new ReleaseDownloadItemJob(_releaseService.Object, _downloads.Object, _progressReporter);

        // Act
        async Task Act() => await subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        Assert.ThrowsAsync<ArgumentException>(Act);
    }

    [Test]
    public void ExecuteAsync_ThrowsArgumentException_WhenReleaseIdIsNotAGuid()
    {
        // Arrange
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: _userId,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>
            {
                ["releaseId"] = "not-a-guid",
                ["contentHierarchyItemId"] = _grabTargetId.ToString()
            },
            Event: null);
        var subject = new ReleaseDownloadItemJob(_releaseService.Object, _downloads.Object, _progressReporter);

        // Act
        async Task Act() => await subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        Assert.ThrowsAsync<ArgumentException>(Act);
    }

    [Test]
    public void ExecuteAsync_ThrowsArgumentException_WhenContentHierarchyItemIdParameterMissing()
    {
        // Arrange
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: _userId,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>
            {
                ["releaseId"] = _releaseId.ToString()
            },
            Event: null);
        var subject = new ReleaseDownloadItemJob(_releaseService.Object, _downloads.Object, _progressReporter);

        // Act
        async Task Act() => await subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        Assert.ThrowsAsync<ArgumentException>(Act);
    }

    [Test]
    public void ExecuteAsync_ThrowsArgumentException_WhenContentHierarchyItemIdIsNotAGuid()
    {
        // Arrange
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: _userId,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>
            {
                ["releaseId"] = _releaseId.ToString(),
                ["contentHierarchyItemId"] = "not-a-guid"
            },
            Event: null);
        var subject = new ReleaseDownloadItemJob(_releaseService.Object, _downloads.Object, _progressReporter);

        // Act
        async Task Act() => await subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        Assert.ThrowsAsync<ArgumentException>(Act);
    }

    [Test]
    public async Task ExecuteAsync_DelegatesToReleaseServiceAndIgnoresRetiredParameters_WhenContextAndFlagsAreProvided()
    {
        // Arrange
        var executionId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var context = new JobContext(
            ExecutionId: executionId,
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: _userId,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>
            {
                ["releaseId"] = _releaseId.ToString(),
                ["contentHierarchyItemId"] = _grabTargetId.ToString(),
                ["suspectLastResort"] = "true",
                ["interactive"] = "true"
            },
            Event: null,
            CorrelationId: correlationId);
        var subject = new ReleaseDownloadItemJob(_releaseService.Object, _downloads.Object, _progressReporter);

        // Act
        await subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        _releaseService.Verify(
            s => s.AcquireAsync(
                _releaseId,
                _userId,
                correlationId,
                It.IsAny<IProgressReporter?>(),
                true,
                _grabTargetId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_DelegatesWithFlagsFalse_WhenFlagParametersAbsent()
    {
        // Arrange
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: _userId,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>
            {
                ["releaseId"] = _releaseId.ToString(),
                ["contentHierarchyItemId"] = _grabTargetId.ToString()
            },
            Event: null);
        var subject = new ReleaseDownloadItemJob(_releaseService.Object, _downloads.Object, _progressReporter);

        // Act
        await subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        _releaseService.Verify(
            s => s.AcquireAsync(
                _releaseId,
                _userId,
                It.IsAny<Guid?>(),
                It.IsAny<IProgressReporter?>(),
                false,
                _grabTargetId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
