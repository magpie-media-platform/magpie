using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Database;
using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Sdk.Services;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core;

[TestFixture]
public sealed class DatabaseManagementServiceTests
{
    [Test]
    public async Task DeleteBackupAsync_WritesAuditRecordWithCaller_WhenProviderDeletesBackup()
    {
        // Arrange
        var callerUserId = Guid.Parse("2b1c9d54-3f77-4a0b-9c1e-6d5f8a2b7c31");
        var provider = new Mock<IDatabaseManagementProvider>();
        var recoverable = provider.As<IRecoverableDatabaseManagementProvider>();
        recoverable
            .Setup(candidate => candidate.DeleteBackupAsync("backup-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var eventBus = new Mock<IEventBus>();
        eventBus
            .Setup(bus => bus.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var auditRecords = new Mock<IAuditRecordRepository>();
        var subject = new DatabaseManagementService(
            provider.Object,
            Mock.Of<IMagpieSystem>(),
            auditRecords.Object,
            eventBus.Object,
            TimeProvider.System);

        // Act
        var deleted = await subject.DeleteBackupAsync("backup-1", callerUserId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleted, Is.True);
            auditRecords.Verify(
                repository => repository.CreateAsync(
                    It.Is<AuditRecord>(record =>
                        record.Category == "System" &&
                        record.Operation == "BackupDeleted" &&
                        record.EntityId == "backup-1" &&
                        record.UserId == callerUserId),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            eventBus.Verify(
                bus => bus.PublishAsync(
                    It.Is<MagpieEvent>(magpieEvent =>
                        magpieEvent.Name == "backup.deleted" &&
                        magpieEvent.Context["backup.id"] == "backup-1"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task DeleteBackupAsync_DoesNotPublish_WhenProviderDoesNotDeleteBackup()
    {
        // Arrange
        var provider = new Mock<IDatabaseManagementProvider>();
        provider.As<IRecoverableDatabaseManagementProvider>()
            .Setup(candidate => candidate.DeleteBackupAsync("backup-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var eventBus = new Mock<IEventBus>();
        eventBus
            .Setup(bus => bus.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var subject = new DatabaseManagementService(
            provider.Object,
            Mock.Of<IMagpieSystem>(),
            Mock.Of<IAuditRecordRepository>(),
            eventBus.Object,
            TimeProvider.System);

        // Act
        var deleted = await subject.DeleteBackupAsync("backup-1", Guid.NewGuid()).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleted, Is.False);
            eventBus.Verify(
                bus => bus.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task GetInfo_ReportsDeniedReasons_WhenProviderDeniesRestoreAndDelete()
    {
        // Arrange
        var provider = new Mock<IDatabaseManagementProvider>();
        provider.Setup(candidate => candidate.Provider).Returns("MSSQL");
        var recoverable = provider.As<IRecoverableDatabaseManagementProvider>();
        recoverable
            .Setup(candidate => candidate.GetBackupDeniedReasonAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        recoverable
            .Setup(candidate => candidate.GetRestoreDeniedReasonAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("Restore needs the database owner, dbcreator or sysadmin role on SQL Server");
        recoverable
            .Setup(candidate => candidate.GetDeleteDeniedReasonAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("Delete needs the sysadmin role on SQL Server");
        var subject = new DatabaseManagementService(
            provider.Object,
            Mock.Of<IMagpieSystem>(),
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IEventBus>(),
            TimeProvider.System);

        // Act
        var info = await subject.GetInfo().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(info.BackupRestoreSupported, Is.True);
            Assert.That(info.BackupDeniedReason, Is.Null);
            Assert.That(
                info.RestoreDeniedReason,
                Is.EqualTo("Restore needs the database owner, dbcreator or sysadmin role on SQL Server"));
            Assert.That(info.DeleteDeniedReason, Is.EqualTo("Delete needs the sysadmin role on SQL Server"));
        }
    }

    [Test]
    public void RestoreBackupAsync_ThrowsWithoutStaging_WhenRestartIsNotSupported()
    {
        // Arrange
        var provider = new Mock<IDatabaseManagementProvider>();
        var recoverable = provider.As<IRecoverableDatabaseManagementProvider>();
        var system = new Mock<IMagpieSystem>();
        system
            .Setup(candidate => candidate.GetInfo())
            .Returns(new SystemInfo
            {
                Version = "1.0.0",
                Runtime = "runtime",
                OperatingSystem = "os",
                Architecture = "x64",
                SystemRoot = "root",
                NodeId = "node-1",
                StartedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                RestartSupported = false
            });
        var subject = new DatabaseManagementService(
            provider.Object,
            system.Object,
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IEventBus>(),
            TimeProvider.System);

        // Act
        var exception = Assert.ThrowsAsync<MagpieShutdownException>(
            () => subject.RestoreBackupAsync("backup-1", Guid.NewGuid()));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception.Message, Is.EqualTo("The host is not supervised."));
            recoverable.Verify(
                candidate => candidate.StageRestoreAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }
}
