using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Data.SQLite;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Models.Paging;
using Magpie.Sdk.Services;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public sealed class ContentHierarchyItemRepositoryEffectiveValuesTests
{
    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private IContentHierarchyItemRepository _subject = null!;
    private Guid _metadataContainerId;
    private Mock<IEventBus> _eventBus = null!;
    private Mock<IContentTypeRegistry> _contentTypeRegistry = null!;
    private Mock<ILogger<ContentHierarchyItemRepository>> _logger = null!;

    [OneTimeSetUp]
    public void CreateTemplate()
    {
        _template = new SqliteConnection("Data Source=:memory:");
        _template.Open();
        var options = new DbContextOptionsBuilder<MagpieDbContext>();
        MagpieDbContextFactory.ConfigureOptions(options, "Data Source=:memory:", [], []);
        using var context = new MagpieDbContext(options.UseSqlite(_template).Options);
        context.Database.EnsureCreated();
    }

    [OneTimeTearDown]
    public void DisposeTemplate()
    {
        _template.Dispose();
    }

    [SetUp]
    public void SetUp()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        _template.BackupDatabase(connection);
        var options = new DbContextOptionsBuilder<MagpieDbContext>();
        MagpieDbContextFactory.ConfigureOptions(options, "Data Source=:memory:", [], []);
        _context = new MagpieDbContext(options.UseSqlite(connection, contextOwnsConnection: true).Options);
        var metadataContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(metadataContainer);
        _context.SaveChanges();
        _metadataContainerId = metadataContainer.Id;
        _eventBus = new Mock<IEventBus>();
        _contentTypeRegistry = new Mock<IContentTypeRegistry>();
        _logger = new Mock<ILogger<ContentHierarchyItemRepository>>();
        var transactions = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);
        _subject = new ContentHierarchyItemRepository(
            _context, Mock.Of<IAuditRecordRepository>(), transactions,
            Mock.Of<IMetadataContainerRepository>(),
            _contentTypeRegistry.Object, _eventBus.Object,
            _logger.Object, TimeProvider.System, Mock.Of<IContentRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context.Dispose();
    }

    [Test]
    public async Task RecomputeAllEffectiveValuesAsync_UsesCurrentDefinition_WhenParentLevelChanges()
    {
        // Arrange
        var rootTierList = new QualityTierList
        {
            Id = Guid.NewGuid(),
            Name = "Root",
            CreatedAt = DateTime.UtcNow
        };
        var middleTierList = new QualityTierList
        {
            Id = Guid.NewGuid(),
            Name = "Middle",
            CreatedAt = DateTime.UtcNow
        };
        var rootFolder = new RootFolder
        {
            Name = "Type A",
            Path = "/library/definition-change",
            ContentType = "type-a",
            IsDefault = true,
            Defaults = new()
            {
                ["root"] = new() { QualityTierListId = rootTierList.Id },
                ["middle"] = new() { QualityTierListId = middleTierList.Id }
            },
            CreatedAt = DateTime.UtcNow
        };
        var item = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "leaf",
            Slug = "definition-change",
            Name = "Definition change",
            EffectiveRootFolderId = rootFolder.Id,
            EffectiveQualityTierListId = middleTierList.Id,
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.QualityTierLists.AddRange(rootTierList, middleTierList);
        _context.RootFolders.Add(rootFolder);
        _context.ContentHierarchyItems.Add(item);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _contentTypeRegistry.Setup(registry => registry.GetDefinitionFor("type-a"))
            .Returns(new ContentTypeDefinition
            {
                Slug = "type-a",
                DisplayName = "Type A",
                LevelDefinitions =
                [
                    new ContentHierarchyLevelDefinition
                    {
                        Slug = "root",
                        DisplayName = "Root",
                        CustomMetadataFields = []
                    },
                    new ContentHierarchyLevelDefinition
                    {
                        Slug = "leaf",
                        ParentSlug = "root",
                        DisplayName = "Leaf",
                        CustomMetadataFields = []
                    }
                ]
            });

        // Act
        await _subject.RecomputeAllEffectiveValuesAsync().ConfigureAwait(false);

        // Assert
        var stored = await _context.ContentHierarchyItems
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == item.Id)
            .ConfigureAwait(false);
        Assert.That(stored.EffectiveQualityTierListId, Is.EqualTo(rootTierList.Id));
    }

    [Test]
    public async Task RecomputeAllEffectiveValuesAsync_RepairsTreeAndPublishesAfterCommit_WhenEffectiveValuesAreStale()
    {
        // Arrange
        var qualityTierList = new QualityTierList
        {
            Id = Guid.NewGuid(),
            Name = "Default",
            CreatedAt = DateTime.UtcNow
        };
        var rootFolder = new RootFolder
        {
            Id = Guid.NewGuid(),
            Name = "Type A",
            Path = "/library/type-a",
            ContentType = "type-a",
            IsDefault = true,
            DefaultMonitoring = MonitoringMode.Monitored,
            Defaults = new()
            {
                ["root"] = new() { QualityTierListId = qualityTierList.Id },
                ["child"] = new() { QualityTierListId = qualityTierList.Id }
            },
            CreatedAt = DateTime.UtcNow
        };
        var root = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow },
            ContentType = "type-a",
            Level = "root",
            Slug = "stale-root",
            Name = "Root",
            EffectiveMonitoring = MonitoringMode.Unmonitored,
            EffectiveRootFolderId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        };
        var child = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow },
            ParentId = root.Id,
            ContentType = "type-a",
            Level = "child",
            Slug = "stale-child",
            Name = "Child",
            EffectiveMonitoring = MonitoringMode.Unmonitored,
            EffectiveRootFolderId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        };
        _context.QualityTierLists.Add(qualityTierList);
        _context.RootFolders.Add(rootFolder);
        _context.ContentHierarchyItems.AddRange(root, child);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var eventPublishedInsideTransaction = false;
        _eventBus
            .Setup(bus => bus.PublishAsync(
                It.Is<MagpieEvent>(magpieEvent =>
                    magpieEvent.Name == "content.hierarchyItem.updated"),
                It.IsAny<CancellationToken>()))
            .Callback(() => eventPublishedInsideTransaction |= _context.Database.CurrentTransaction is not null)
            .Returns(Task.CompletedTask);

        // Act
        await _subject.RecomputeAllEffectiveValuesAsync().ConfigureAwait(false);

        // Assert
        var stored = await _context.ContentHierarchyItems
            .AsNoTracking()
            .Where(item => item.Id == root.Id || item.Id == child.Id)
            .ToListAsync()
            .ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stored, Has.Count.EqualTo(2));
            Assert.That(
                stored,
                Has.All.Property(nameof(ContentHierarchyItem.EffectiveMonitoring)).EqualTo(MonitoringMode.Monitored));
            Assert.That(
                stored,
                Has.All.Property(nameof(ContentHierarchyItem.EffectiveRootFolderId)).EqualTo(rootFolder.Id));
            Assert.That(
                stored,
                Has.All.Property(nameof(ContentHierarchyItem.EffectiveQualityTierListId)).EqualTo(qualityTierList.Id));
            Assert.That(eventPublishedInsideTransaction, Is.False);
        }

        _eventBus.Verify(
            bus => bus.PublishAsync(
                It.Is<MagpieEvent>(magpieEvent =>
                    magpieEvent.Name == "content.hierarchyItem.updated"),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [TestCase("false", MonitoringMode.Unmonitored, 0)]
    [TestCase("true", MonitoringMode.Monitored, 0)]
    [TestCase("0", MonitoringMode.Unmonitored, 0)]
    [TestCase("1", MonitoringMode.Monitored, 0)]
    [TestCase("Unmonitored", MonitoringMode.Unmonitored, 0)]
    [TestCase("Monitored", MonitoringMode.Monitored, 0)]
    [TestCase("Shotgun", MonitoringMode.Shotgun, 0)]
    [TestCase("2", MonitoringMode.Monitored, 1)]
    [TestCase("not-a-boolean", MonitoringMode.Monitored, 1)]
    public async Task RecomputeAllEffectiveValuesAsync_ResolvesTheSystemDefault_WhenTheGroupHoldsAValue(
        string storedValue,
        MonitoringMode expectedMonitoring,
        int expectedWarnings)
    {
        // Arrange
        var settingsGroup = new SettingsGroup
        {
            Name = "monitoring",
            ExtensionId = null,
            CreatedAt = DateTime.UtcNow
        };
        _context.SettingsGroups.Add(settingsGroup);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Settings.Add(new Setting
        {
            SettingsGroupId = settingsGroup.Id,
            Key = "system-default",
            Value = storedValue,
            CreatedAt = DateTime.UtcNow
        });
        var rootFolder = new RootFolder
        {
            Name = "Library",
            Path = "/library",
            ContentType = "type-a",
            IsDefault = true,
            DefaultMonitoring = null,
            CreatedAt = DateTime.UtcNow
        };
        var item = new ContentHierarchyItem
        {
            MetadataContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow },
            ContentType = "type-a",
            Level = "level-a",
            Slug = "inherits-global",
            Name = "Inherits",
            Monitoring = null,
            EffectiveMonitoring = MonitoringMode.Unmonitored,
            EffectiveRootFolderId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        };
        _context.RootFolders.Add(rootFolder);
        _context.ContentHierarchyItems.Add(item);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject.RecomputeAllEffectiveValuesAsync().ConfigureAwait(false);

        // Assert
        var stored = await _context.ContentHierarchyItems
            .AsNoTracking()
            .FirstAsync(candidate => candidate.Id == item.Id)
            .ConfigureAwait(false);
        var warnings = _logger.Invocations.Count(invocation =>
            invocation.Arguments.Contains(LogLevel.Warning));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stored.EffectiveMonitoring, Is.EqualTo(expectedMonitoring));
            Assert.That(warnings, Is.EqualTo(expectedWarnings));
        }
    }
}
