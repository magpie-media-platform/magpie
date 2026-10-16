using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Database;
using Magpie.App.Hosting;
using Magpie.Models;
using Magpie.Sdk.Services;

using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Host;

[TestFixture]
public sealed class DatabaseStartupServiceTests
{
    private Mock<IDatabaseManagementProvider> _databaseManagement = null!;
    private Mock<IMigrator> _migrator = null!;
    private Mock<IEventBus> _eventBus = null!;
    private DatabaseMigrationState _migrationState = null!;
    private DatabaseStartupService _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _databaseManagement = new Mock<IDatabaseManagementProvider>();
        _databaseManagement
            .Setup(provider => provider.GetUnknownMigrationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _migrator = new Mock<IMigrator>();
        _eventBus = new Mock<IEventBus>();
        _migrationState = new DatabaseMigrationState();
        _subject = new DatabaseStartupService(
            Mock.Of<IServiceScopeFactory>(),
            new LoadedExtensionSet([], [], new Dictionary<Guid, string>(), 0),
            _migrationState,
            new MagpieHostEnvironment(
                "node-1",
                IsClustered: true,
                IsSupervised: true,
                SystemRoot: string.Empty,
                ExtensionsRoot: string.Empty,
                ConfigRoot: string.Empty,
                DataRoot: string.Empty,
                StartedAt: new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc)),
            _eventBus.Object,
            NullLogger<DatabaseStartupService>.Instance);
    }

    [Test]
    public async Task MigrateAsync_PublishesDatabaseMigrated_WhenTheMigrateAppliesMigrations()
    {
        // Arrange
        _databaseManagement
            .SetupSequence(provider => provider.GetAppliedMigrationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(["20260509010401_Initial"])
            .ReturnsAsync(["20260509010401_Initial", "20260805235539_sdk_redesign"]);

        // Act
        await _subject.MigrateAsync(_migrator.Object, _databaseManagement.Object, CancellationToken.None)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _migrator.Verify(
                migrator => migrator.MigrateAsync(null, It.IsAny<CancellationToken>()),
                Times.Once);
            _eventBus.Verify(
                bus => bus.PublishAsync(
                    It.Is<MagpieEvent>(magpieEvent => magpieEvent.Name == "database.migrated"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task MigrateAsync_PublishesNothing_WhenTheMigrateAppliesNoMigration()
    {
        // Arrange
        _databaseManagement
            .Setup(provider => provider.GetAppliedMigrationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(["20260509010401_Initial"]);

        // Act
        await _subject.MigrateAsync(_migrator.Object, _databaseManagement.Object, CancellationToken.None)
            .ConfigureAwait(false);

        // Assert
        _eventBus.Verify(
            bus => bus.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task MigrateAsync_RecordsMigrated_WhenTheSchemaIsKnownAfterTheMigrate()
    {
        // Arrange
        _databaseManagement
            .Setup(provider => provider.GetAppliedMigrationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(["20260509010401_Initial"]);

        // Act
        var migrated = await _subject.MigrateAsync(_migrator.Object, _databaseManagement.Object, CancellationToken.None)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(migrated, Is.True);
            Assert.That(_migrationState.Migrated.IsCompleted, Is.True);
        }
    }

    [Test]
    public async Task MigrateAsync_DoesNotRecordMigrated_WhenANewerNodeMigratedFirst()
    {
        // Arrange
        _databaseManagement
            .Setup(provider => provider.GetAppliedMigrationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(["20260509010401_Initial", "20991231000000_Future"]);
        _databaseManagement
            .Setup(provider => provider.GetUnknownMigrationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(["20991231000000_Future"]);

        // Act
        var migrated = await _subject.MigrateAsync(_migrator.Object, _databaseManagement.Object, CancellationToken.None)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(migrated, Is.False);
            Assert.That(_migrationState.Migrated.IsCompleted, Is.False);
        }
    }
}
