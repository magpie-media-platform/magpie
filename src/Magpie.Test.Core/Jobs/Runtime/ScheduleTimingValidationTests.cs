using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Jobs;
using Magpie.Core.Jobs.Downloads;
using Magpie.Core.Jobs.Fulfillment;
using Magpie.Core.Jobs.Runtime;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs.Runtime;

[TestFixture]
public sealed class ScheduleTimingValidationTests
{
    private List<JobSpec> _specs = null!;
    private Mock<IScheduleTriggerRepository> _repository = null!;
    private IJobManager _subject = null!;
    private FakeTimeProvider _timeProvider = null!;

    public static IEnumerable<TestCaseData> TimingCases =>
    [
        new(DownloadImportScanJob.Spec, "settleMinutes", "1065357360", "1065357361",
            new Dictionary<string, string>(), "Parameter settleMinutes exceeds the supported date range."),
        new(DownloadStuckDetectionJob.Spec, "fetchingMinutes", "1065357360", "1065357361",
            new Dictionary<string, string>(), "Parameter fetchingMinutes exceeds the supported date range."),
        new(DownloadStuckDetectionJob.Spec, "stalledMinutes", "1065357360", "1065357361",
            new Dictionary<string, string>(), "Parameter stalledMinutes exceeds the supported date range."),
        new(RetentionCleanupJob.Spec, "jobExecutionRetentionDays", "739831", "739832",
            new Dictionary<string, string>(), "Parameter jobExecutionRetentionDays exceeds the supported date range."),
        new(RetentionCleanupJob.Spec, "auditRetentionDays", "739831", "739832",
            new Dictionary<string, string>(), "Parameter auditRetentionDays exceeds the supported date range."),
        new(RetentionCleanupJob.Spec, "releaseObservationRetentionDays", "739831", "739832",
            new Dictionary<string, string>(),
            "Parameter releaseObservationRetentionDays exceeds the supported date range."),
        new(RetentionCleanupJob.Spec, "failedDownloadRetentionDays", "739831", "739832",
            new Dictionary<string, string>(),
            "Parameter failedDownloadRetentionDays exceeds the supported date range."),
        new(ReleaseAutoGrabScanJob.Spec, "retryCooldownHours", "17755956", "17755957",
            new Dictionary<string, string>(), "Parameter retryCooldownHours exceeds the supported date range."),
        new(IntegrationHealthCheckJob.Spec, "staleAfterMinutes", "1065357360", "1065357361",
            new Dictionary<string, string>(), "Parameter staleAfterMinutes exceeds the supported date range."),
        new(IntegrationHealthCheckJob.Spec, "timeoutSeconds", "4294967", "4294968",
            new Dictionary<string, string>(), "Parameter timeoutSeconds exceeds the supported timer range."),
        new(DatabaseMaintenanceJob.Spec, "backupIntervalDays", "739831", "739832",
            new Dictionary<string, string> { ["backupEnabled"] = "true" },
            "Parameter backupIntervalDays exceeds the supported date range."),
        new(DatabaseMaintenanceJob.Spec, "optimizeIntervalDays", "739831", "739832",
            new Dictionary<string, string>(), "Parameter optimizeIntervalDays exceeds the supported date range."),
        new(DownloadImportScanJob.Spec, "settleMinutes", "10", "2147483647",
            new Dictionary<string, string>(), "Parameter settleMinutes exceeds the supported date range."),
        new(DownloadStuckDetectionJob.Spec, "fetchingMinutes", "20", "2147483647",
            new Dictionary<string, string>(), "Parameter fetchingMinutes exceeds the supported date range."),
        new(DownloadStuckDetectionJob.Spec, "stalledMinutes", "90", "2147483647",
            new Dictionary<string, string>(), "Parameter stalledMinutes exceeds the supported date range."),
        new(RetentionCleanupJob.Spec, "jobExecutionRetentionDays", "30", "2147483647",
            new Dictionary<string, string>(), "Parameter jobExecutionRetentionDays exceeds the supported date range."),
        new(RetentionCleanupJob.Spec, "auditRetentionDays", "90", "2147483647",
            new Dictionary<string, string>(), "Parameter auditRetentionDays exceeds the supported date range."),
        new(RetentionCleanupJob.Spec, "releaseObservationRetentionDays", "7", "2147483647",
            new Dictionary<string, string>(),
            "Parameter releaseObservationRetentionDays exceeds the supported date range."),
        new(RetentionCleanupJob.Spec, "failedDownloadRetentionDays", "30", "2147483647",
            new Dictionary<string, string>(),
            "Parameter failedDownloadRetentionDays exceeds the supported date range."),
        new(ReleaseAutoGrabScanJob.Spec, "retryCooldownHours", "6", "2147483647",
            new Dictionary<string, string>(), "Parameter retryCooldownHours exceeds the supported date range."),
        new(IntegrationHealthCheckJob.Spec, "staleAfterMinutes", "60", "2147483647",
            new Dictionary<string, string>(), "Parameter staleAfterMinutes exceeds the supported date range."),
        new(IntegrationHealthCheckJob.Spec, "timeoutSeconds", "10", "2147483647",
            new Dictionary<string, string>(), "Parameter timeoutSeconds exceeds the supported timer range."),
        new(DatabaseMaintenanceJob.Spec, "backupIntervalDays", "1", "2147483647",
            new Dictionary<string, string> { ["backupEnabled"] = "true" },
            "Parameter backupIntervalDays exceeds the supported date range."),
        new(DatabaseMaintenanceJob.Spec, "optimizeIntervalDays", "7", "2147483647",
            new Dictionary<string, string> { ["optimizeEnabled"] = "true" },
            "Parameter optimizeIntervalDays exceeds the supported date range."),
    ];

    public static IEnumerable<TestCaseData> FallbackCases =>
    [
        new(DownloadImportScanJob.Spec, "settleMinutes", "0"),
        new(DownloadImportScanJob.Spec, "settleMinutes", "-1"),
        new(DownloadImportScanJob.Spec, "settleMinutes", "abc"),
        new(DownloadImportScanJob.Spec, "settleMinutes", "2147483648"),
        new(DownloadStuckDetectionJob.Spec, "fetchingMinutes", "abc"),
        new(DownloadStuckDetectionJob.Spec, "stalledMinutes", "1.5"),
        new(RetentionCleanupJob.Spec, "jobExecutionRetentionDays", "0"),
        new(RetentionCleanupJob.Spec, "auditRetentionDays", "-1"),
        new(RetentionCleanupJob.Spec, "releaseObservationRetentionDays", "abc"),
        new(RetentionCleanupJob.Spec, "failedDownloadRetentionDays", "2147483648"),
        new(ReleaseAutoGrabScanJob.Spec, "retryCooldownHours", "0"),
        new(ReleaseAutoGrabScanJob.Spec, "retryCooldownHours", "-1"),
        new(ReleaseAutoGrabScanJob.Spec, "retryCooldownHours", "abc"),
        new(ReleaseAutoGrabScanJob.Spec, "retryCooldownHours", "1.5"),
        new(ReleaseAutoGrabScanJob.Spec, "retryCooldownHours", "2147483648"),
        new(IntegrationHealthCheckJob.Spec, "staleAfterMinutes", "abc"),
        new(IntegrationHealthCheckJob.Spec, "timeoutSeconds", "abc"),
        new(IntegrationHealthCheckJob.Spec, "timeoutSeconds", "0"),
        new(IntegrationHealthCheckJob.Spec, "timeoutSeconds", "-1"),
        new(IntegrationHealthCheckJob.Spec, "timeoutSeconds", "2147483648"),
        new(IntegrationHealthCheckJob.Spec, "timeoutSeconds", "1e309"),
        new(DatabaseMaintenanceJob.Spec, "backupIntervalDays", "abc"),
        new(DatabaseMaintenanceJob.Spec, "optimizeIntervalDays", "0"),
        new(DatabaseMaintenanceJob.Spec, "optimizeIntervalDays", "-1"),
        new(DatabaseMaintenanceJob.Spec, "optimizeIntervalDays", "1.5"),
        new(DatabaseMaintenanceJob.Spec, "optimizeIntervalDays", "2147483648"),
        new(DatabaseMaintenanceJob.Spec, "optimizeIntervalDays", ""),
        new(DatabaseMaintenanceJob.Spec, "optimizeIntervalDays", null),
    ];

    [SetUp]
    public void SetUp()
    {
        _specs = [];
        _repository = new Mock<IScheduleTriggerRepository>();
        var transactions = new Mock<ITransactionManager>();
        transactions.Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<bool>> operation, CancellationToken token) => operation(token));
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero));
        _subject = new JobManager(Mock.Of<IJobRuntime>(),
            Mock.Of<IJobRegistry>(registry => registry.ListJobSpecs() == _specs),
            Mock.Of<IJobExecutionRepository>(), _repository.Object, Mock.Of<IEventTriggerRepository>(),
            Mock.Of<IJobConcurrencyScheduler>(), Mock.Of<IJobExecutionScopeFactory>(), _timeProvider,
            NullLogger<JobManager>.Instance, transactions.Object);
    }

    [TestCaseSource(nameof(TimingCases))]
    public async Task CreateScheduleTriggerAsync_RejectsOverflow_WhenTimingExceedsBoundary(
        JobSpec spec, string field, string valid, string invalid, Dictionary<string, string> other, string expected)
    {
        // Arrange
        _specs.Add(spec);
        var create = new ScheduleTriggerCreate
        {
            JobType = spec.JobType,
            Scope = spec.Scope,
            Name = "Boundary",
            Enabled = true,
            Schedule = "0 0 1 1 *",
            Parameters = new Dictionary<string, string>(other) { [field] = valid }
        };

        // Act
        await _subject.CreateScheduleTriggerAsync(create, Guid.Empty, Role.Admin).ConfigureAwait(false);
        var error = Assert.ThrowsAsync<MagpieException>(() => _subject.CreateScheduleTriggerAsync(
            create with { Parameters = new Dictionary<string, string>(other) { [field] = invalid } },
            Guid.Empty, Role.Admin));

        // Assert
        Assert.That(error!.Message, Is.EqualTo(expected));
        _repository.Verify(repository => repository.CreateAsync(
            It.Is<ScheduleTrigger>(trigger => trigger.Parameters[field] == valid),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(repository => repository.CreateAsync(It.IsAny<ScheduleTrigger>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestCaseSource(nameof(TimingCases))]
    public async Task PatchScheduleTriggerAsync_RejectsOverflow_WhenEffectiveTimingExceedsBoundary(
        JobSpec spec, string field, string valid, string invalid, Dictionary<string, string> other, string expected)
    {
        // Arrange
        _specs.Add(spec);
        var stored = new ScheduleTrigger
        {
            Id = Guid.Empty,
            JobType = spec.JobType,
            Scope = spec.Scope,
            Name = "Original",
            Enabled = true,
            Schedule = "0 0 1 1 *",
            Parameters = new Dictionary<string, string>(other) { [field] = valid }
        };
        _repository.Setup(repository => repository.GetByIdAsync(Guid.Empty, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        var patch = new ScheduleTriggerPatch
        {
            Name = new() { Value = "Must not persist" },
            Parameters = new() { Update = new Dictionary<string, string> { [field] = invalid } }
        };

        // Act
        var error = Assert.ThrowsAsync<MagpieException>(() =>
            _subject.PatchScheduleTriggerAsync(Guid.Empty, patch, Guid.Empty, callerRole: Role.Admin));
        await _subject.PatchScheduleTriggerAsync(Guid.Empty,
            new ScheduleTriggerPatch
            {
                Parameters = new() { Update = new Dictionary<string, string> { [field] = valid } }
            }, Guid.Empty, callerRole: Role.Admin).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(error!.Message, Is.EqualTo(expected));
            Assert.That(stored.Parameters[field], Is.EqualTo(valid));
            Assert.That(stored.Name, Is.EqualTo("Original"));
        }

        _repository.Verify(repository => repository.PatchAsync(It.IsAny<ScheduleTriggerPatch>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(repository => repository.PatchAsync(
            It.Is<ScheduleTriggerPatch>(applied => applied.Parameters!.Update![field] == valid),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestCaseSource(nameof(FallbackCases))]
    public async Task CreateScheduleTriggerAsync_AcceptsFallback_WhenReaderUsesDefault(
        JobSpec spec, string field, string? value)
    {
        // Arrange
        _specs.Add(spec);
        var create = new ScheduleTriggerCreate
        {
            JobType = spec.JobType,
            Scope = spec.Scope,
            Name = "Fallback",
            Enabled = true,
            Schedule = "0 0 1 1 *",
            Parameters = new Dictionary<string, string> { [field] = value!, ["backupEnabled"] = "true" }
        };

        // Act
        await _subject.CreateScheduleTriggerAsync(create, Guid.Empty, Role.Admin).ConfigureAwait(false);

        // Assert
        _repository.Verify(repository => repository.CreateAsync(
            It.Is<ScheduleTrigger>(trigger => trigger.Parameters[field] == value),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestCase("backupEnabled", "backupIntervalDays", "false")]
    [TestCase("backupEnabled", "backupIntervalDays", "invalid")]
    [TestCase("optimizeEnabled", "optimizeIntervalDays", "false")]
    public async Task CreateScheduleTriggerAsync_AcceptsUnusedInterval_WhenMaintenanceOperationIsDisabled(
        string enabledField, string intervalField, string enabledValue)
    {
        // Arrange
        _specs.Add(DatabaseMaintenanceJob.Spec);
        var create = new ScheduleTriggerCreate
        {
            JobType = DatabaseMaintenanceJob.Spec.JobType,
            Scope = JobScope.Administrative,
            Name = "Disabled operation",
            Enabled = true,
            Schedule = "0 0 1 1 *",
            Parameters = new Dictionary<string, string>
            {
                [enabledField] = enabledValue,
                [intervalField] = "2147483647"
            }
        };

        // Act
        await _subject.CreateScheduleTriggerAsync(create, Guid.Empty, Role.Admin).ConfigureAwait(false);

        // Assert
        _repository.Verify(repository => repository.CreateAsync(It.IsAny<ScheduleTrigger>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestCase("backupEnabled", "backupIntervalDays", "Parameter backupIntervalDays exceeds the supported date range.")]
    [TestCase("optimizeEnabled", "optimizeIntervalDays",
        "Parameter optimizeIntervalDays exceeds the supported date range.")]
    public void PatchScheduleTriggerAsync_RejectsStoredOverflow_WhenMaintenanceOperationIsEnabled(
        string enabledField, string intervalField, string expected)
    {
        // Arrange
        _specs.Add(DatabaseMaintenanceJob.Spec);
        var stored = new ScheduleTrigger
        {
            Id = Guid.Empty,
            JobType = DatabaseMaintenanceJob.Spec.JobType,
            Scope = JobScope.Administrative,
            Name = "Original",
            Enabled = true,
            Schedule = "0 0 1 1 *",
            Parameters = new Dictionary<string, string>
            {
                [enabledField] = "false",
                [intervalField] = "2147483647"
            }
        };
        _repository.Setup(repository => repository.GetByIdAsync(Guid.Empty, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        var patch = new ScheduleTriggerPatch
        {
            Parameters = new() { Update = new Dictionary<string, string> { [enabledField] = "true" } }
        };

        // Act
        var error = Assert.ThrowsAsync<MagpieException>(() =>
            _subject.PatchScheduleTriggerAsync(Guid.Empty, patch, Guid.Empty, callerRole: Role.Admin));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(error!.Message, Is.EqualTo(expected));
            Assert.That(stored.Parameters[enabledField], Is.EqualTo("false"));
        }

        _repository.Verify(repository => repository.PatchAsync(It.IsAny<ScheduleTriggerPatch>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task PatchScheduleTriggerAsync_AcceptsRepair_WhenOperationAndIntervalArePatchedTogether()
    {
        // Arrange
        _specs.Add(DatabaseMaintenanceJob.Spec);
        _repository.Setup(repository => repository.GetByIdAsync(Guid.Empty, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScheduleTrigger
            {
                Id = Guid.Empty,
                JobType = DatabaseMaintenanceJob.Spec.JobType,
                Scope = JobScope.Administrative,
                Name = "Original",
                Enabled = true,
                Schedule = "0 0 1 1 *",
                Parameters = new Dictionary<string, string>
                {
                    ["backupEnabled"] = "false",
                    ["backupIntervalDays"] = "2147483647"
                }
            });
        var patch = new ScheduleTriggerPatch
        {
            Parameters = new()
            {
                Update = new Dictionary<string, string> { ["backupEnabled"] = "true", ["backupIntervalDays"] = "1" }
            }
        };

        // Act
        await _subject.PatchScheduleTriggerAsync(Guid.Empty, patch, Guid.Empty, callerRole: Role.Admin)
            .ConfigureAwait(false);

        // Assert
        _repository.Verify(repository => repository.PatchAsync(It.IsAny<ScheduleTriggerPatch>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task CreateScheduleTriggerAsync_UsesCurrentUtc_WhenDateAdvances()
    {
        // Arrange
        _specs.Add(RetentionCleanupJob.Spec);
        var create = new ScheduleTriggerCreate
        {
            JobType = RetentionCleanupJob.Spec.JobType,
            Scope = JobScope.Administrative,
            Name = "Clock boundary",
            Enabled = true,
            Schedule = "0 0 1 1 *",
            Parameters = new Dictionary<string, string> { ["jobExecutionRetentionDays"] = "739832" }
        };

        // Act
        var error = Assert.ThrowsAsync<MagpieException>(() =>
            _subject.CreateScheduleTriggerAsync(create, Guid.Empty, Role.Admin));
        _timeProvider.Advance(TimeSpan.FromDays(1));
        await _subject.CreateScheduleTriggerAsync(create, Guid.Empty, Role.Admin).ConfigureAwait(false);

        // Assert
        Assert.That(error!.Message,
            Is.EqualTo("Parameter jobExecutionRetentionDays exceeds the supported date range."));
        _repository.Verify(repository => repository.CreateAsync(It.IsAny<ScheduleTrigger>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PatchScheduleTriggerAsync_RejectsStoredOverflow_WhenScheduleWillBeEnabled(bool previouslyEnabled)
    {
        // Arrange
        _specs.Add(RetentionCleanupJob.Spec);
        _repository.Setup(repository => repository.GetByIdAsync(Guid.Empty, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScheduleTrigger
            {
                Id = Guid.Empty,
                JobType = RetentionCleanupJob.Spec.JobType,
                Scope = JobScope.Administrative,
                Name = "Original",
                Enabled = previouslyEnabled,
                Schedule = "0 0 1 1 *",
                Parameters = new Dictionary<string, string> { ["jobExecutionRetentionDays"] = "2147483647" }
            });
        var patch = new ScheduleTriggerPatch
        {
            Name = new() { Value = "Renamed" },
            Enabled = new() { Value = true }
        };

        // Act
        var error = Assert.ThrowsAsync<MagpieException>(() =>
            _subject.PatchScheduleTriggerAsync(Guid.Empty, patch, Guid.Empty, callerRole: Role.Admin));

        // Assert
        Assert.That(error!.Message,
            Is.EqualTo("Parameter jobExecutionRetentionDays exceeds the supported date range."));
        _repository.Verify(repository => repository.PatchAsync(It.IsAny<ScheduleTriggerPatch>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestCase("backupEnabled", "backupIntervalDays")]
    [TestCase("optimizeEnabled", "optimizeIntervalDays")]
    public async Task PatchScheduleTriggerAsync_AcceptsUnusedInterval_WhenMaintenanceOperationIsDisabled(
        string enabledField, string intervalField)
    {
        // Arrange
        _specs.Add(DatabaseMaintenanceJob.Spec);
        _repository.Setup(repository => repository.GetByIdAsync(Guid.Empty, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScheduleTrigger
            {
                Id = Guid.Empty,
                JobType = DatabaseMaintenanceJob.Spec.JobType,
                Scope = JobScope.Administrative,
                Name = "Original",
                Enabled = true,
                Schedule = "0 0 1 1 *",
                Parameters = new Dictionary<string, string>
                {
                    [enabledField] = "true",
                    [intervalField] = "2147483647"
                }
            });
        var patch = new ScheduleTriggerPatch
        {
            Parameters = new() { Update = new Dictionary<string, string> { [enabledField] = "false" } }
        };

        // Act
        await _subject.PatchScheduleTriggerAsync(Guid.Empty, patch, Guid.Empty, callerRole: Role.Admin)
            .ConfigureAwait(false);

        // Assert
        _repository.Verify(repository => repository.PatchAsync(It.IsAny<ScheduleTriggerPatch>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public void CreateScheduleTriggerAsync_RejectsOverflow_WhenOptimizationFlagUsesEnabledDefault()
    {
        // Arrange
        _specs.Add(DatabaseMaintenanceJob.Spec);
        var create = new ScheduleTriggerCreate
        {
            JobType = DatabaseMaintenanceJob.Spec.JobType,
            Scope = JobScope.Administrative,
            Name = "Default flag",
            Enabled = true,
            Schedule = "0 0 1 1 *",
            Parameters = new Dictionary<string, string>
            {
                ["optimizeEnabled"] = "invalid",
                ["optimizeIntervalDays"] = "2147483647"
            }
        };

        // Act
        var error = Assert.ThrowsAsync<MagpieException>(() =>
            _subject.CreateScheduleTriggerAsync(create, Guid.Empty, Role.Admin));

        // Assert
        Assert.That(error!.Message, Is.EqualTo("Parameter optimizeIntervalDays exceeds the supported date range."));
        _repository.Verify(repository => repository.CreateAsync(It.IsAny<ScheduleTrigger>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
