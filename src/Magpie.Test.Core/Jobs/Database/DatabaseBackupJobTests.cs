using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Database;
using Magpie.Core.Jobs;
using Magpie.Models;
using Magpie.Models.Enums;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs.Database;

[TestFixture]
public sealed class DatabaseBackupJobTests
{
    [Test]
    public async Task ExecuteAsync_ReportsUnmeasuredWorkAndCompletion_WhenBackupSucceeds()
    {
        // Arrange
        var databaseProvider = new Mock<IRecoverableDatabaseManagementProvider>();
        databaseProvider.SetupGet(provider => provider.Provider).Returns("test");
        databaseProvider
            .Setup(provider => provider.BackupAsync(It.IsAny<BackupCreateOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("backup-1");
        databaseProvider
            .Setup(provider => provider.GetBackupFilePathAsync("backup-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("backup-1.db");
        databaseProvider
            .Setup(provider => provider.ListBackupsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new BackupInfo
                {
                    Id = "backup-1",
                    Name = "backup-1",
                    CreatedAt = new DateTime(2026, 8, 4, 12, 0, 0, DateTimeKind.Utc),
                    SizeBytes = 1024,
                    Kind = BackupKind.Manual
                }
            ]);
        var eventBus = new Mock<IEventBus>();
        eventBus
            .Setup(bus => bus.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var progress = new Mock<IProgressReporter>();
        var subject = (IJob)new DatabaseBackupJob(
            eventBus.Object,
            NullLogger<DatabaseBackupJob>.Instance,
            progress.Object,
            databaseProvider.Object);
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: null,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>(),
            Event: null);

        // Act
        await subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            progress.Verify(
                value => value.Report(null, "The backup job will create a database backup."),
                Times.Once);
            progress.Verify(value => value.Report(1, "The database backup completed."), Times.Once);
        }
    }
}
