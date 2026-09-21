using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Config;
using Magpie.Core.Diagnostics;
using Magpie.Core.Jobs.Fulfillment;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;
using Magpie.Sdk.Diagnostics;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Diagnostics;

[TestFixture]
public sealed class DefaultJobScheduleDiagnosticTests
{
    private Mock<IScheduleTriggerRepository> _scheduleTriggers = null!;
    private Mock<IEventTriggerRepository> _eventTriggers = null!;
    private Mock<IJobExecutionRepository> _executions = null!;
    private Mock<IJobRegistry> _jobRegistry = null!;
    private SearchOptions _searchOptions = null!;
    private ScheduleTrigger _releaseSearchTrigger = null!;
    private IDiagnosticCheck _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _searchOptions = new SearchOptions();
        _releaseSearchTrigger = new ScheduleTrigger
        {
            Id = Guid.NewGuid(),
            JobType = "ReleaseSearchScan",
            Scope = JobScope.Content,
            Name = "Release search scan schedule",
            Schedule = "*/30 * * * *",
            ExecutionMode = JobExecutionMode.Queue,
            Enabled = true,
            CreatedAt = DateTime.UtcNow,
        };

        _scheduleTriggers = new Mock<IScheduleTriggerRepository>();
        _scheduleTriggers
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([_releaseSearchTrigger]);

        _eventTriggers = new Mock<IEventTriggerRepository>();
        _eventTriggers
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _executions = new Mock<IJobExecutionRepository>();
        _executions.Setup(repository => repository.QueryAsync(
                It.IsAny<JobExecutionQuery>(), It.IsAny<PagingParameter>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution>());
        _jobRegistry = new Mock<IJobRegistry>();
        var spec = ReleaseSearchScanJob.Spec;
        _jobRegistry
            .Setup(registry => registry.TryGetJobSpec("ReleaseSearchScan", out spec))
            .Returns(true);

        _subject = new DefaultJobScheduleDiagnostic(
            _scheduleTriggers.Object,
            _eventTriggers.Object,
            _executions.Object,
            _jobRegistry.Object,
            _searchOptions,
            NullLogger<DefaultJobScheduleDiagnostic>.Instance);
    }

    [Test]
    public async Task RunAsync_ReportsNoMinimumSearchAgeHoursFinding_WhenReleaseSearchScanIsRequested()
    {
        // Act
        var findings = await _subject.RunAsync(DiagnosticContext.Empty).ConfigureAwait(false);

        // Assert
        Assert.That(
            findings,
            Has.None.Matches<DiagnosticFinding>(finding =>
                finding.Title.Contains("minimumSearchAgeHours", StringComparison.Ordinal)));
    }

    [TestCase(false, 1)]
    [TestCase(true, 0)]
    public async Task RunAsync_ReportsMissingHandlerOnce_WhenEnabledTriggersReferenceTheSameJob(
        bool registered,
        int expectedCount)
    {
        // Arrange
        _releaseSearchTrigger.JobType = "extension-job";
        _eventTriggers.Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new EventTrigger
            {
                JobType = "extension-job", Name = "Extension event", Scope = JobScope.System,
                EventFilter = new(), Enabled = true
            }]);
        var spec = new JobSpec
        {
            JobType = "extension-job",
            DisplayName = "Extension job",
            Category = "Test",
            Scope = JobScope.System,
            Parameters = []
        };
        _jobRegistry.Setup(registry => registry.TryGetJobSpec("extension-job", out spec)).Returns(registered);

        // Act
        var findings = await _subject.RunAsync(DiagnosticContext.Empty).ConfigureAwait(false);

        // Assert
        Assert.That(
            findings,
            Has.Exactly(expectedCount).Matches<DiagnosticFinding>(finding =>
                finding.Title == "Job type extension-job has no handler on this node" &&
                finding.AffectedEntity == "Job:extension-job" &&
                finding.Severity == DiagnosticFindingSeverity.Warning));
    }

    [Test]
    public async Task RunAsync_CountsQueuedExecutionsWithOneBoundedQuery_WhenATriggerHasNoHandler()
    {
        // Arrange
        _releaseSearchTrigger.JobType = "extension-job";
        _executions.Setup(repository => repository.QueryAsync(
                It.Is<JobExecutionQuery>(query =>
                    query.Status == JobExecutionStatus.Queued && query.JobTypes!.Contains("extension-job")),
                It.Is<PagingParameter>(paging => paging.PageSize == 1),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution> { Items = [], TotalItems = 4200 });

        // Act
        var findings = await _subject.RunAsync(DiagnosticContext.Empty).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                findings.Single(finding => finding.AffectedEntity == "Job:extension-job").Detail,
                Is.EqualTo("Enabled triggers reference extension-job, but this node cannot run it, and " +
                    "4200 queued execution(s) wait for it. " +
                    "Restore its extension or remove its triggers and cancel its queued executions. " +
                    "In a cluster, first check whether another scheduler-eligible node provides the handler."));
            _executions.Verify(
                repository => repository.QueryAsync(
                    It.IsAny<JobExecutionQuery>(), It.IsAny<PagingParameter>(), null, It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task RunAsync_OmitsMissingHandler_WhenOnlyDisabledTriggersReferenceTheJob()
    {
        // Arrange
        _releaseSearchTrigger.JobType = "extension-job";
        _releaseSearchTrigger.Enabled = false;
        _eventTriggers.Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new EventTrigger
            {
                JobType = "extension-job", Name = "Extension event", Scope = JobScope.System,
                EventFilter = new(), Enabled = false
            }]);

        // Act
        var findings = await _subject.RunAsync(DiagnosticContext.Empty).ConfigureAwait(false);

        // Assert
        Assert.That(findings, Has.None.Matches<DiagnosticFinding>(finding =>
            finding.Title == "Job type extension-job has no handler on this node"));
    }

    [Test]
    public async Task RunAsync_UsesConfiguredMinimumSearchAge_WhenReleaseSearchScanCadenceIsRequested()
    {
        // Arrange
        _searchOptions.MinimumSearchAge = TimeSpan.FromHours(1);
        _releaseSearchTrigger.Schedule = "0 */2 * * *";

        // Act
        var findings = await _subject.RunAsync(DiagnosticContext.Empty).ConfigureAwait(false);

        // Assert
        Assert.That(
            findings.SingleOrDefault(finding =>
                finding.Title == "ReleaseSearchScan schedule runs less often than " +
                    "Search:MinimumSearchAge"),
            Is.Not.Null);
    }

    [TestCase(DataType.String)]
    [TestCase(DataType.Boolean)]
    public async Task RunAsync_OmitsSpecAuthoringErrors_WhenTimingParameterHasWrongType(DataType type)
    {
        // Arrange
        var trigger = new ScheduleTrigger
        {
            Id = Guid.NewGuid(),
            JobType = "ReleaseAutoGrabScan",
            Name = "Automatic grab",
            Schedule = "0 */6 * * *",
            Enabled = true,
            Scope = JobScope.System,
        };
        var schedules = new Mock<IScheduleTriggerRepository>();
        schedules.Setup(value => value.ListAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([trigger]);
        var events = new Mock<IEventTriggerRepository>();
        events.Setup(value => value.ListAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var registry = new Mock<IJobRegistry>();
        var spec = new JobSpec
        {
            JobType = "ReleaseAutoGrabScan",
            DisplayName = "Automatic grab",
            Category = "Test",
            Scope = JobScope.System,
            Parameters = [new("retryCooldownHours", "Cooldown", type, false, "6")],
        };
        registry.Setup(value => value.TryGetJobSpec("ReleaseAutoGrabScan", out spec)).Returns(true);
        var subject = new DefaultJobScheduleDiagnostic(
            schedules.Object, events.Object, _executions.Object, registry.Object, _searchOptions,
            NullLogger<DefaultJobScheduleDiagnostic>.Instance);

        // Act
        var findings = await subject.RunAsync(DiagnosticContext.Empty).ConfigureAwait(false);

        // Assert
        Assert.That(findings, Has.None.Matches<DiagnosticFinding>(value => value.Severity ==
             DiagnosticFindingSeverity.Error));
    }
}
