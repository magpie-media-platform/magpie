using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Database;
using Magpie.Core;
using Magpie.Models;
using Magpie.Sdk.Events;
using Magpie.Sdk.Services;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core;

[TestFixture]
public sealed class DatabaseManagementServiceTests
{
    [Test]
    public async Task DeleteBackupAsync_PublishesDeleted_WhenProviderDeletesBackup()
    {
        // Arrange
        var provider = new Mock<IDatabaseManagementProvider>();
        var recoverable = provider.As<IRecoverableDatabaseManagementProvider>();
        recoverable
            .Setup(candidate => candidate.DeleteBackupAsync("backup-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var eventBus = new Mock<IEventBus>();
        eventBus
            .Setup(bus => bus.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var subject = new DatabaseManagementService(
            provider.Object,
            Mock.Of<IMagpieSystem>(),
            eventBus.Object);

        // Act
        var deleted = await subject.DeleteBackupAsync("backup-1", Guid.NewGuid()).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleted, Is.True);
            eventBus.Verify(
                bus => bus.PublishAsync(
                    It.Is<MagpieEvent>(magpieEvent =>
                        magpieEvent.Name == MagpieEvents.Names.BackupDeleted &&
                        magpieEvent.Context[MagpieEvents.Keys.BackupId] == "backup-1"),
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
            eventBus.Object);

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
}
