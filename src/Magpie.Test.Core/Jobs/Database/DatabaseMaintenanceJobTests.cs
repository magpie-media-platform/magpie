using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Database;
using Magpie.Core.Jobs;
using Magpie.Models;
using Magpie.Models.Enums;
using Magpie.Sdk.Events;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs.Database;

[TestFixture]
public sealed class DatabaseMaintenanceJobTests
{
    [Test]
    public void Spec_DeclaresMaintenanceParameters_WithDefaults()
    {
        // Act
        var parameters = DatabaseMaintenanceJob.Spec.Parameters;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(DatabaseMaintenanceJob.Spec.JobType, Is.EqualTo("DatabaseMaintenance"));
            Assert.That(DatabaseMaintenanceJob.Spec.Scope, Is.EqualTo(JobScope.Administrative));
            Assert.That(DatabaseMaintenanceJob.Spec.ConcurrencyScope, Is.EqualTo(ConcurrencyScope.Global));
            Assert.That(parameters, Has.Count.EqualTo(5));
            Assert.That(
                parameters.Single(p => p.Name == "backupEnabled").DefaultValue,
                Is.EqualTo("false"));
            Assert.That(
                parameters.Single(p => p.Name == "backupIntervalDays").DefaultValue,
                Is.EqualTo("1"));
            Assert.That(
                parameters.Single(p => p.Name == "backupRetentionCount").DefaultValue,
                Is.EqualTo("7"));
            Assert.That(
                parameters.Single(p => p.Name == "optimizeEnabled").DefaultValue,
                Is.EqualTo("true"));
            Assert.That(
                parameters.Single(p => p.Name == "optimizeIntervalDays").DefaultValue,
                Is.EqualTo("7"));
        }
    }

    [Test]
    public async Task ExecuteAsync_CreatesScheduledBackup_WhenBackupsEnabledAndNoScheduledBackupExists()
    {
        // Arrange
        var backupProvider = new Mock<IRecoverableDatabaseManagementProvider>();
        backupProvider.SetupGet(provider => provider.Provider).Returns("test");
        BackupCreateOptions? options = null;
        backupProvider
            .Setup(provider => provider.ListBackupsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        backupProvider
            .Setup(provider => provider.BackupAsync(It.IsAny<BackupCreateOptions>(), It.IsAny<CancellationToken>()))
            .Callback<BackupCreateOptions, CancellationToken>((captured, _) => options = captured)
            .ReturnsAsync("20240115_103000");
        var eventBus = new Mock<IEventBus>();
        eventBus
            .Setup(bus => bus.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var progress = new Mock<IProgressReporter>();
        var subject = (IJob)new DatabaseMaintenanceJob(
            TimeProvider.System,
            NullLoggerFactory.Instance.CreateLogger<DatabaseMaintenanceJob>(),
            eventBus.Object,
            backupProvider.Object,
            progress.Object);
        var jobContext = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: null,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>
            {
                ["backupEnabled"] = "true",
                ["optimizeEnabled"] = "false"
            },
            Event: null);

        // Act
        await subject.ExecuteAsync(jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(options, Is.Not.Null);
            Assert.That(options!.Kind, Is.EqualTo(BackupKind.Scheduled));
            progress.Verify(
                value => value.Report(0, "The maintenance job will read the database capabilities."),
                Times.Once);
            progress.Verify(
                value => value.Report(null, "The maintenance job will load the scheduled database backups."),
                Times.Once);
            progress.Verify(
                value => value.Report(null, "The maintenance job will create a scheduled database backup."),
                Times.Once);
            progress.Verify(value => value.Report(1, "The database maintenance completed."), Times.Once);
            backupProvider.Verify(
                provider => provider.BackupAsync(It.IsAny<BackupCreateOptions>(), It.IsAny<CancellationToken>()),
                Times.Once);
            eventBus.Verify(
                bus => bus.PublishAsync(
                    It.Is<MagpieEvent>(magpieEvent =>
                        magpieEvent.Name == MagpieEvents.Names.BackupCompleted &&
                        magpieEvent.Context[MagpieEvents.Keys.BackupId] == "20240115_103000"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_DoesNotCreateScheduledBackup_WhenNewestScheduledBackupIsInsideInterval()
    {
        // Arrange
        var backupProvider = new Mock<IRecoverableDatabaseManagementProvider>();
        backupProvider.SetupGet(provider => provider.Provider).Returns("test");
        backupProvider
            .Setup(provider => provider.ListBackupsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new BackupInfo
                {
                    Id = "20240115_103000",
                    Name = "scheduled",
                    CreatedAt = DateTime.UtcNow,
                    SizeBytes = 1024,
                    Kind = BackupKind.Scheduled
                }
            ]);
        var subject = (IJob)new DatabaseMaintenanceJob(
            TimeProvider.System,
            NullLoggerFactory.Instance.CreateLogger<DatabaseMaintenanceJob>(),
            Mock.Of<IEventBus>(),
            backupProvider.Object);
        var jobContext = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: null,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>
            {
                ["backupEnabled"] = "true",
                ["backupIntervalDays"] = "7",
                ["optimizeEnabled"] = "false"
            },
            Event: null);

        // Act
        await subject.ExecuteAsync(jobContext).ConfigureAwait(false);

        // Assert
        backupProvider.Verify(
            provider => provider.BackupAsync(It.IsAny<BackupCreateOptions>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_PrunesOnlyOldScheduledBackups_BeyondRetention()
    {
        // Arrange
        var backupProvider = new Mock<IRecoverableDatabaseManagementProvider>();
        backupProvider.SetupGet(provider => provider.Provider).Returns("test");
        backupProvider
            .Setup(provider => provider.ListBackupsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new BackupInfo
                {
                    Id = "20240115_103000",
                    Name = "scheduled-new",
                    CreatedAt = DateTime.UtcNow,
                    SizeBytes = 1024,
                    Kind = BackupKind.Scheduled
                },
                new BackupInfo
                {
                    Id = "20240114_103000",
                    Name = "scheduled-old",
                    CreatedAt = DateTime.UtcNow.AddDays(-1),
                    SizeBytes = 1024,
                    Kind = BackupKind.Scheduled
                },
                new BackupInfo
                {
                    Id = "20240113_103000",
                    Name = "scheduled-oldest",
                    CreatedAt = DateTime.UtcNow.AddDays(-2),
                    SizeBytes = 1024,
                    Kind = BackupKind.Scheduled
                },
                new BackupInfo
                {
                    Id = "20240112_103000",
                    Name = "manual-old",
                    CreatedAt = DateTime.UtcNow.AddDays(-3),
                    SizeBytes = 1024,
                    Kind = BackupKind.Manual
                }
            ]);
        backupProvider
            .Setup(provider => provider.DeleteBackupAsync("20240114_103000", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        backupProvider
            .Setup(provider => provider.DeleteBackupAsync("20240113_103000", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var eventBus = new Mock<IEventBus>();
        eventBus
            .Setup(bus => bus.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var progress = new Mock<IProgressReporter>();
        var subject = (IJob)new DatabaseMaintenanceJob(
            TimeProvider.System,
            NullLoggerFactory.Instance.CreateLogger<DatabaseMaintenanceJob>(),
            eventBus.Object,
            backupProvider.Object,
            progress.Object);
        var jobContext = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: null,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>
            {
                ["backupEnabled"] = "true",
                ["backupIntervalDays"] = "7",
                ["backupRetentionCount"] = "1",
                ["optimizeEnabled"] = "false"
            },
            Event: null);

        // Act
        await subject.ExecuteAsync(jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            backupProvider.Verify(
                provider => provider.DeleteBackupAsync("20240114_103000", It.IsAny<CancellationToken>()),
                Times.Once);
            backupProvider.Verify(
                provider => provider.DeleteBackupAsync("20240113_103000", It.IsAny<CancellationToken>()),
                Times.Once);
            backupProvider.Verify(
                provider => provider.DeleteBackupAsync("20240112_103000", It.IsAny<CancellationToken>()),
                Times.Never);
            eventBus.Verify(
                bus => bus.PublishAsync(
                    It.Is<MagpieEvent>(magpieEvent =>
                        magpieEvent.Name == MagpieEvents.Names.BackupDeleted &&
                        magpieEvent.Context[MagpieEvents.Keys.BackupId] == "20240114_103000"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            progress.Verify(
                value => value.Report(0, "The maintenance job will delete old scheduled database backups."),
                Times.Once);
            progress.Verify(
                value => value.Report(0.5, "The maintenance job will delete old scheduled database backups."),
                Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_OptimizesDatabase_WhenOptimizationIsDue()
    {
        // Arrange
        var databaseProvider = new Mock<IDatabaseManagementProvider>();
        databaseProvider.SetupGet(provider => provider.Provider).Returns("test");
        databaseProvider
            .As<IOptimizingDatabaseManagementProvider>()
            .Setup(provider => provider.GetLastOptimizedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(DateTime.UtcNow.AddDays(-10));
        var progress = new Mock<IProgressReporter>();
        var subject = (IJob)new DatabaseMaintenanceJob(
            TimeProvider.System,
            NullLoggerFactory.Instance.CreateLogger<DatabaseMaintenanceJob>(),
            Mock.Of<IEventBus>(),
            databaseProvider.Object,
            progress.Object);
        var jobContext = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: null,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>
            {
                ["backupEnabled"] = "false",
                ["optimizeEnabled"] = "true",
                ["optimizeIntervalDays"] = "7"
            },
            Event: null);

        // Act
        await subject.ExecuteAsync(jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            databaseProvider
                .As<IOptimizingDatabaseManagementProvider>()
                .Verify(provider => provider.OptimizeAsync(It.IsAny<CancellationToken>()), Times.Once);
            progress.Verify(
                value => value.Report(null, "The maintenance job will optimize the database."),
                Times.Once);
            progress.Verify(value => value.Report(1, "The database maintenance completed."), Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_DoesNotOptimizeDatabase_WhenOptimizationIsInsideInterval()
    {
        // Arrange
        var databaseProvider = new Mock<IDatabaseManagementProvider>();
        databaseProvider.SetupGet(provider => provider.Provider).Returns("test");
        databaseProvider
            .As<IOptimizingDatabaseManagementProvider>()
            .Setup(provider => provider.GetLastOptimizedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(DateTime.UtcNow);
        var subject = (IJob)new DatabaseMaintenanceJob(
            TimeProvider.System,
            NullLoggerFactory.Instance.CreateLogger<DatabaseMaintenanceJob>(),
            Mock.Of<IEventBus>(),
            databaseProvider.Object);
        var jobContext = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: null,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>
            {
                ["backupEnabled"] = "false",
                ["optimizeEnabled"] = "true",
                ["optimizeIntervalDays"] = "7"
            },
            Event: null);

        // Act
        await subject.ExecuteAsync(jobContext).ConfigureAwait(false);

        // Assert
        databaseProvider
            .As<IOptimizingDatabaseManagementProvider>()
            .Verify(provider => provider.OptimizeAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public void ExecuteAsync_DoesNotThrow_WhenProviderDoesNotSupportEnabledMaintenance()
    {
        // Arrange
        var databaseProvider = new Mock<IDatabaseManagementProvider>();
        databaseProvider.SetupGet(provider => provider.Provider).Returns("test");
        var progress = new Mock<IProgressReporter>();
        var subject = (IJob)new DatabaseMaintenanceJob(
            TimeProvider.System,
            NullLoggerFactory.Instance.CreateLogger<DatabaseMaintenanceJob>(),
            Mock.Of<IEventBus>(),
            databaseProvider.Object,
            progress.Object);

        // Act
        var jobContext = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: null,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>
            {
                ["backupEnabled"] = "true",
                ["optimizeEnabled"] = "true"
            },
            Event: null);

        // Assert
        Assert.DoesNotThrowAsync(() => subject.ExecuteAsync(jobContext));
        using (Assert.EnterMultipleScope())
        {
            progress.Verify(
                value => value.Report(
                    null,
                    "The maintenance job skipped database backups because the provider does not support them."),
                Times.Once);
            progress.Verify(
                value => value.Report(
                    null,
                    "The maintenance job skipped database optimization because the provider does not support it."),
                Times.Once);
            progress.Verify(value => value.Report(1, "The database maintenance completed."), Times.Once);
        }
    }
}
