using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Core.Jobs.Fulfillment;
using Magpie.Models;
using Magpie.Models.Enums;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs.Fulfillment;

[TestFixture]
public sealed class ReleaseSearchItemJobTests
{
    private static readonly Guid ItemId = new("11111111-1111-1111-1111-111111111111");

    private static readonly TestCaseData[] ForcingRequests =
    [
        new TestCaseData(
            JobExecutionMode.Queue,
            new Dictionary<string, string>
            {
                ["contentHierarchyItemId"] = "11111111-1111-1111-1111-111111111111",
                ["force"] = "true"
            }).SetArgDisplayNames("Queue", "force=true"),
        new TestCaseData(
            JobExecutionMode.Immediate,
            new Dictionary<string, string>
            {
                ["contentHierarchyItemId"] = "11111111-1111-1111-1111-111111111111"
            }).SetArgDisplayNames("Immediate", "force=absent")
    ];

    private static readonly TestCaseData[] NonForcingRequests =
    [
        new TestCaseData(
            JobExecutionMode.Queue,
            new Dictionary<string, string>
            {
                ["contentHierarchyItemId"] = "11111111-1111-1111-1111-111111111111"
            }).SetArgDisplayNames("Queue", "force=absent"),
        new TestCaseData(
            JobExecutionMode.Queue,
            new Dictionary<string, string>
            {
                ["contentHierarchyItemId"] = "11111111-1111-1111-1111-111111111111",
                ["force"] = "false"
            }).SetArgDisplayNames("Queue", "force=false"),
        new TestCaseData(
            JobExecutionMode.Queue,
            new Dictionary<string, string>
            {
                ["contentHierarchyItemId"] = "11111111-1111-1111-1111-111111111111",
                ["force"] = "not-a-bool"
            }).SetArgDisplayNames("Queue", "force=not-a-bool")
    ];

    private readonly IProgressReporter _progressReporter = Mock.Of<IProgressReporter>();

    private Mock<IReleaseSearchService> _releaseSearchService = null!;
    private IJob _subject = null!;
    private JobContext _jobContext = null!;

    [SetUp]
    public void SetUp()
    {
        _releaseSearchService = new Mock<IReleaseSearchService>();
        _releaseSearchService
            .Setup(s => s.SearchAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                It.IsAny<IProgressReporter?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _jobContext = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: Guid.NewGuid(),
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>(),
            Event: null);

        _subject = new ReleaseSearchItemJob(_releaseSearchService.Object, _progressReporter);
    }

    [TestCaseSource(nameof(ForcingRequests))]
    public async Task ExecuteAsync_PassesTheOverride_WhenTheRequestForcesTheSearch(
        JobExecutionMode mode,
        IReadOnlyDictionary<string, string> parameters)
    {
        // Arrange
        var context = _jobContext with { ExecutionMode = mode, Parameters = parameters };

        // Act
        await _subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        _releaseSearchService.Verify(
            s => s.SearchAsync(
                ItemId,
                context.ManualTriggerUserId,
                context.CorrelationId,
                context.ExecutionId,
                It.IsAny<IProgressReporter?>(),
                true,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestCaseSource(nameof(NonForcingRequests))]
    public async Task ExecuteAsync_PassesNoOverride_WhenTheRequestDoesNotForceTheSearch(
        JobExecutionMode mode,
        IReadOnlyDictionary<string, string> parameters)
    {
        // Arrange
        var context = _jobContext with { ExecutionMode = mode, Parameters = parameters };

        // Act
        await _subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        _releaseSearchService.Verify(
            s => s.SearchAsync(
                ItemId,
                context.ManualTriggerUserId,
                context.CorrelationId,
                context.ExecutionId,
                It.IsAny<IProgressReporter?>(),
                false,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public void ExecuteAsync_Throws_WhenContentHierarchyItemIdMissing()
    {
        // Arrange
        var context = _jobContext with { Parameters = new Dictionary<string, string>() };

        // Assert
        Assert.ThrowsAsync<ArgumentException>(async () => await _subject.ExecuteAsync(context).ConfigureAwait(false));
    }
}
