using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Config;
using Magpie.Core.Diagnostics;
using Magpie.Core.Jobs;
using Magpie.Core.Jobs.Downloads;
using Magpie.Core.Jobs.Fulfillment;
using Magpie.Core.Jobs.Metadata;
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

    public static IEnumerable<TestCaseData> StoredImportTriggerCases
    {
        get
        {
            yield return new TestCaseData(new EventTrigger
            {
                Id = new Guid("00000000-0000-0000-0000-000000000300"),
                JobType = "MediaInfoExtractScan",
                Scope = JobScope.Content,
                Name = "Media info extract on import",
                EventFilter = new()
                {
                    EventName = new() { Operator = CriteriaValueOperator.Equals, Value = "content.imported" }
                },
                Enabled = true
            }, 1);
            yield return new TestCaseData(new EventTrigger
            {
                Id = new Guid("00000000-0000-0000-0000-000000000300"),
                JobType = "MediaInfoExtractScan",
                Scope = JobScope.Content,
                Name = "Media info extract on import",
                EventFilter = new()
                {
                    EventName = new() { Operator = CriteriaValueOperator.Equals, Value = "content.imported" }
                },
                Enabled = true,
                Parameters = new() { ["mediaInfoPolicy"] = "All" }
            }, 0);
            yield return new TestCaseData(new EventTrigger
            {
                Id = new Guid("00000000-0000-0000-0000-000000000300"),
                JobType = "MediaInfoExtractScan",
                Scope = JobScope.Content,
                Name = "My media info scan",
                EventFilter = new()
                {
                    EventName = new() { Operator = CriteriaValueOperator.Equals, Value = "content.imported" }
                },
                Enabled = true,
                CreatedByUserId = new Guid("00000000-0000-0000-0000-00000000000a")
            }, 0);
            yield return new TestCaseData(new EventTrigger
            {
                Id = new Guid("00000000-0000-0000-0000-000000000300"),
                JobType = "MediaInfoExtractScan",
                Scope = JobScope.Content,
                Name = "Media info extract on import",
                EventFilter = new()
                {
                    EventName = new() { Operator = CriteriaValueOperator.Equals, Value = "content.imported" }
                },
                Enabled = true,
                ModifiedByUserId = new Guid("00000000-0000-0000-0000-00000000000a")
            }, 0);
            yield return new TestCaseData(new EventTrigger
            {
                Id = new Guid("00000000-0000-0000-0000-000000000300"),
                JobType = "MediaInfoExtractScan",
                Scope = JobScope.Content,
                Name = "Media info extract on import",
                EventFilter = new()
                {
                    EventName = new() { Operator = CriteriaValueOperator.Equals, Value = "download.imported" }
                },
                Enabled = true
            }, 0);
        }
    }

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
                    "4200 queued execution(s) wait for it."));
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

    [TestCaseSource(nameof(StoredImportTriggerCases))]
    public async Task RunAsync_ReportsTheStoredImportTrigger_WhenItIsTheBuiltInTriggerWithNoParameters(
        EventTrigger stored,
        int expectedFindings)
    {
        // Arrange
        _eventTriggers.Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([stored]);

        // Act
        var findings = await _subject.RunAsync(DiagnosticContext.Empty).ConfigureAwait(false);

        // Assert
        var unbound = findings
            .Where(finding => finding.AffectedEntity == "EventTrigger:00000000-0000-0000-0000-000000000300")
            .ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(unbound, Has.Count.EqualTo(expectedFindings));
            Assert.That(unbound.Select(finding => finding.RepairId), Is.All.EqualTo("core.config.reseed-default-job"));
            Assert.That(
                unbound.Select(finding => finding.Detail),
                Is.All.EqualTo("The built-in MediaInfoExtractScan event trigger has no parameters, and the built-in " +
                    "trigger binds contentHierarchyItemId, mediaInfoPolicy."));
        }
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

    [TestCase("0", 0)]
    [TestCase("-1", 1)]
    public async Task RunAsync_ReportsInvalidFailedDownloadRetention_WhenRetentionDaysAreNegative(
        string days,
        int expectedCount)
    {
        // Arrange
        _releaseSearchTrigger.JobType = "RetentionCleanup";
        _releaseSearchTrigger.Name = "Retention cleanup";
        _releaseSearchTrigger.Parameters = new() { ["failedDownloadRetentionDays"] = days };
        var spec = RetentionCleanupJob.Spec;
        _jobRegistry.Setup(registry => registry.TryGetJobSpec("RetentionCleanup", out spec)).Returns(true);

        // Act
        var findings = await _subject.RunAsync(DiagnosticContext.Empty).ConfigureAwait(false);

        // Assert
        Assert.That(
            findings,
            Has.Exactly(expectedCount).Matches<DiagnosticFinding>(finding =>
                finding.Title ==
                    "Schedule Retention cleanup has invalid timing parameter failedDownloadRetentionDays" &&
                finding.Severity == DiagnosticFindingSeverity.Error));
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

    [TestCaseSource(nameof(ValidatedTimingParameters))]
    public void BuiltInSpec_DeclaresTheReadType_WhenTheTimingParameterIsValidated(
        JobSpec spec,
        string parameterName,
        DataType expectedType)
    {
        // Act
        var parameter = spec.Parameters.SingleOrDefault(candidate => candidate.Name == parameterName);

        // Assert
        Assert.That(parameter?.Type, Is.EqualTo(expectedType));
    }

    private static TestCaseData[] ValidatedTimingParameters() =>
    [
        new(ReleaseAutoGrabScanJob.Spec, MagpieJobs.Parameters.RetryCooldownHours, DataType.Number),
        new(RetentionCleanupJob.Spec, MagpieJobs.Parameters.JobExecutionRetentionDays, DataType.Number),
        new(RetentionCleanupJob.Spec, MagpieJobs.Parameters.AuditRetentionDays, DataType.Number),
        new(RetentionCleanupJob.Spec, MagpieJobs.Parameters.ReleaseObservationRetentionDays, DataType.Number),
        new(RetentionCleanupJob.Spec, MagpieJobs.Parameters.FailedDownloadRetentionDays, DataType.Number),
        new(DownloadStuckDetectionJob.Spec, MagpieJobs.Parameters.FetchingMinutes, DataType.Number),
        new(DownloadStuckDetectionJob.Spec, MagpieJobs.Parameters.StalledMinutes, DataType.Number),
        new(DownloadImportScanJob.Spec, MagpieJobs.Parameters.SettleMinutes, DataType.Number),
        new(DatabaseMaintenanceJob.Spec, MagpieJobs.Parameters.BackupEnabled, DataType.Boolean),
        new(DatabaseMaintenanceJob.Spec, MagpieJobs.Parameters.OptimizeEnabled, DataType.Boolean),
        new(DatabaseMaintenanceJob.Spec, MagpieJobs.Parameters.BackupIntervalDays, DataType.Number),
        new(DatabaseMaintenanceJob.Spec, MagpieJobs.Parameters.BackupRetentionCount, DataType.Number),
        new(DatabaseMaintenanceJob.Spec, MagpieJobs.Parameters.OptimizeIntervalDays, DataType.Number),
        new(IntegrationHealthCheckJob.Spec, MagpieJobs.Parameters.StaleAfterMinutes, DataType.Number),
        new(IntegrationHealthCheckJob.Spec, MagpieJobs.Parameters.TimeoutSeconds, DataType.Number),
        new(MediaInfoExtractScanJob.Spec, MagpieJobs.Parameters.MaxAgeHours, DataType.Number),
    ];
}
