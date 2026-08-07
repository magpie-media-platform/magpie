using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Core.Jobs.Fulfillment;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs.Fulfillment;

[TestFixture]
public sealed class ReleaseDownloadItemJobTests
{
    private Mock<IReleaseService> _releaseService = null!;
    private Guid _userId;
    private Guid _releaseId;
    private Guid _grabTargetId;

    [SetUp]
    public void SetUp()
    {
        _releaseService = new Mock<IReleaseService>();
        _releaseService
            .Setup(s => s.AcquireAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                It.IsAny<IProgressReporter?>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Download?)null);

        _userId = Guid.NewGuid();
        _releaseId = Guid.NewGuid();
        _grabTargetId = Guid.NewGuid();
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
        var subject = new ReleaseDownloadItemJob(_releaseService.Object);

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
        var subject = new ReleaseDownloadItemJob(_releaseService.Object);

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
        var subject = new ReleaseDownloadItemJob(_releaseService.Object);

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
        var subject = new ReleaseDownloadItemJob(_releaseService.Object);

        // Act
        async Task Act() => await subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        Assert.ThrowsAsync<ArgumentException>(Act);
    }

    [Test]
    public async Task ExecuteAsync_DelegatesToReleaseService_WithMappedContextAndFlags()
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
                ["ignoreFailedTaint"] = "true"
            },
            Event: null,
            CorrelationId: correlationId);
        var subject = new ReleaseDownloadItemJob(_releaseService.Object);

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
        var subject = new ReleaseDownloadItemJob(_releaseService.Object);

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
                false,
                _grabTargetId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
