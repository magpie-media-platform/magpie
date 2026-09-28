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
public sealed class ContentHierarchyItemRepositoryPatchTests
{
    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private IContentHierarchyItemRepository _subject = null!;
    private Guid _metadataContainerId;
    private Guid _userId;

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
        var user = new User
        {
            Username = "TestUser",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Admin,
            CreatedAt = DateTime.UtcNow
        };
        _context.Users.Add(user);
        _context.SaveChanges();
        _userId = user.Id;
        var metadataContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(metadataContainer);
        _context.SaveChanges();
        _metadataContainerId = metadataContainer.Id;
        var transactions = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);
        var auditRecords = new AuditRecordRepository(
            _context, Mock.Of<IEventBus>(), NullLogger<AuditRecordRepository>.Instance,
            transactions, Mock.Of<IJobExecutionScopeFactory>(), TimeProvider.System);
        _subject = new ContentHierarchyItemRepository(
            _context, auditRecords, transactions,
            Mock.Of<IMetadataContainerRepository>(),
            Mock.Of<IContentTypeRegistry>(), Mock.Of<IEventBus>(),
            NullLogger<ContentHierarchyItemRepository>.Instance, TimeProvider.System, Mock.Of<IContentRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [TestCase(null)]
    [TestCase(MonitoringMode.Monitored)]
    [TestCase(MonitoringMode.Unmonitored)]
    [TestCase(MonitoringMode.Shotgun)]
    public async Task PatchAsync_SetsMonitoringAndAudits_WhenMonitoringIsPatched(MonitoringMode? monitoring)
    {
        // Arrange
        var item = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "monitor-movie",
            Name = "Monitor Movie",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(item);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        // Act
        var result = await _subject.PatchAsync(
            new()
            {
                Id = item.Id,
                Monitoring = new() { Value = monitoring }
            },
            _userId).ConfigureAwait(false);

        // Assert
        var updated = await _subject.GetByIdAsync(item.Id).ConfigureAwait(false);
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(updated, Has.Property(nameof(updated.Monitoring)).EqualTo(monitoring));
            Assert.That(updated, Has.Property(nameof(updated.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Patch"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityId)).EqualTo(item.Id.ToString()));
        }
    }

    [Test]
    public async Task PatchAsync_ReturnsFalse_WhenMonitoringItemDoesNotExist()
    {
        // Act
        var result = await _subject.PatchAsync(
            new()
            {
                Id = Guid.NewGuid(),
                Monitoring = new() { Value = MonitoringMode.Monitored }
            },
            _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task PatchAsync_RecomputesEffectiveMonitoringForSubtree_WhenParentMonitoringIsPatched()
    {
        // Arrange
        var rootFolder = new RootFolder
        {
            Id = Guid.NewGuid(),
            Name = "Type A",
            Path = "/library/type-a",
            ContentType = "type-a",
            IsDefault = true,
            CreatedAt = DateTime.UtcNow
        };
        var qualityTierList = new QualityTierList
        {
            Id = Guid.NewGuid(),
            Name = "Default",
            CreatedAt = DateTime.UtcNow
        };
        var parentContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var childContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.RootFolders.Add(rootFolder);
        _context.QualityTierLists.Add(qualityTierList);
        _context.MetadataContainers.AddRange(parentContainer, childContainer);
        var parent = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainer = parentContainer,
            ContentType = "type-a",
            Level = "root",
            Slug = "effective-parent",
            Name = "Parent",
            Monitoring = MonitoringMode.Monitored,
            EffectiveMonitoring = MonitoringMode.Monitored,
            EffectiveRootFolderId = rootFolder.Id,
            EffectiveQualityTierListId = qualityTierList.Id,
            CreatedAt = DateTime.UtcNow
        };
        var child = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainer = childContainer,
            ParentId = parent.Id,
            ContentType = "type-a",
            Level = "child",
            Slug = "effective-child",
            Name = "Child",
            Monitoring = null,
            EffectiveMonitoring = MonitoringMode.Monitored,
            EffectiveRootFolderId = rootFolder.Id,
            EffectiveQualityTierListId = qualityTierList.Id,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(parent, child);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.PatchAsync(
            new()
            {
                Id = parent.Id,
                Monitoring = new() { Value = MonitoringMode.Unmonitored }
            },
            _userId).ConfigureAwait(false);

        // Assert
        var stored = await _context.ContentHierarchyItems.AsNoTracking()
            .Where(item => item.Id == parent.Id || item.Id == child.Id)
            .ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(
                stored.Single(item => item.Id == parent.Id).EffectiveMonitoring,
                Is.EqualTo(MonitoringMode.Unmonitored));
            Assert.That(stored.Single(item => item.Id == child.Id).Monitoring, Is.Null);
            Assert.That(
                stored.Single(item => item.Id == child.Id).EffectiveMonitoring,
                Is.EqualTo(MonitoringMode.Unmonitored));
        }
    }

    [Test]
    public async Task PatchAsync_RecomputesOnlyTheTargetSubtree_WhenAnItemIsPatched()
    {
        // Arrange
        var rootFolder = new RootFolder
        {
            Id = Guid.NewGuid(),
            Name = "Type A",
            Path = "/library/type-a",
            ContentType = "type-a",
            IsDefault = true,
            DefaultMonitoring = null,
            CreatedAt = DateTime.UtcNow
        };
        _context.RootFolders.Add(rootFolder);
        var parent = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow },
            ContentType = "type-a",
            Level = "root",
            Slug = "scoped-parent",
            Name = "Parent",
            Monitoring = MonitoringMode.Unmonitored,
            EffectiveMonitoring = MonitoringMode.Unmonitored,
            EffectiveRootFolderId = rootFolder.Id,
            CreatedAt = DateTime.UtcNow
        };
        var child = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow },
            ParentId = parent.Id,
            ContentType = "type-a",
            Level = "child",
            Slug = "scoped-child",
            Name = "Child",
            Monitoring = null,
            EffectiveMonitoring = MonitoringMode.Monitored,
            EffectiveRootFolderId = rootFolder.Id,
            CreatedAt = DateTime.UtcNow
        };
        var unrelated = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow },
            ContentType = "type-a",
            Level = "root",
            Slug = "scoped-unrelated",
            Name = "Unrelated",
            Monitoring = null,
            EffectiveMonitoring = MonitoringMode.Unmonitored,
            EffectiveRootFolderId = rootFolder.Id,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(parent, child, unrelated);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.PatchAsync(
            new()
            {
                Id = child.Id,
                RootFolderId = new() { Value = rootFolder.Id }
            },
            _userId).ConfigureAwait(false);

        // Assert
        var stored = await _context.ContentHierarchyItems.AsNoTracking()
            .ToDictionaryAsync(item => item.Slug, item => item.EffectiveMonitoring)
            .ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(stored["scoped-child"], Is.EqualTo(MonitoringMode.Unmonitored));
            Assert.That(stored["scoped-unrelated"], Is.EqualTo(MonitoringMode.Unmonitored));
        }
    }

    [Test]
    public async Task PatchAsync_SetsAndClearsQualityTierListAndAudits_WhenPatched()
    {
        // Arrange
        var tierList = new QualityTierList { Id = Guid.NewGuid(), Name = "HD Tiers", CreatedAt = DateTime.UtcNow };
        _context.QualityTierLists.Add(tierList);
        var item = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "tier-movie",
            Name = "Tier Movie",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(item);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        // Act
        var setResult = await _subject.PatchAsync(
            new()
            {
                Id = item.Id,
                QualityTierListId = new() { Value = tierList.Id }
            },
            _userId).ConfigureAwait(false);
        var afterSet = await _subject.GetByIdAsync(item.Id).ConfigureAwait(false);

        // Act
        var clearResult = await _subject.PatchAsync(
            new()
            {
                Id = item.Id,
                QualityTierListId = new() { Value = null }
            },
            _userId).ConfigureAwait(false);
        var afterClear = await _subject.GetByIdAsync(item.Id).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(setResult, Is.True);
            Assert.That(afterSet, Has.Property(nameof(item.QualityTierListId)).EqualTo(tierList.Id));
            Assert.That(clearResult, Is.True);
            Assert.That(afterClear!.QualityTierListId, Is.Null);
            Assert.That(auditRecords, Has.Some.With.Property(nameof(AuditRecord.Operation)).EqualTo("Patch"));
        }
    }

    [Test]
    public async Task PatchAsync_ReturnsFalse_WhenQualityTierListItemDoesNotExist()
    {
        // Act
        var result = await _subject.PatchAsync(
            new()
            {
                Id = Guid.NewGuid(),
                QualityTierListId = new() { Value = null }
            },
            _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task BulkPatchAsync_SetsMonitoringOnMatching_WhenSomeMatchesAreExcluded()
    {
        // Arrange
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var a = new ContentHierarchyItem
        {
            MetadataContainerId = container1.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-a",
            Name = "movie-a",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(a);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var b = new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-b",
            Name = "movie-b",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(b);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var container3 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container3);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var c = new ContentHierarchyItem
        {
            MetadataContainerId = container3.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-c",
            Name = "movie-c",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(c);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            ContentType = new() { And = [new() { Value = "type-a" }] },
            ExcludeIds = [c.Id]
        };

        // Act
        var result = await _subject.BulkPatchAsync(
            new()
            {
                Filter = criteria,
                Patch = new() { Monitoring = new() { Value = MonitoringMode.Unmonitored } }
            },
            _userId).ConfigureAwait(false);

        // Assert
        var refreshed = await _context.ContentHierarchyItems.AsNoTracking().ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result.Error, Is.Null);
            Assert.That(refreshed.Single(i => i.Id == a.Id).Monitoring, Is.EqualTo(MonitoringMode.Unmonitored));
            Assert.That(refreshed.Single(i => i.Id == b.Id).Monitoring, Is.EqualTo(MonitoringMode.Unmonitored));
            Assert.That(refreshed.Single(i => i.Id == c.Id).Monitoring, Is.EqualTo(MonitoringMode.Monitored));
        }
    }

    [Test]
    public async Task BulkPatchAsync_SetsAndClearsRootFolder_WhenIdsAreProvided()
    {
        // Arrange
        var rootFolder = new RootFolder
        {
            Id = Guid.NewGuid(),
            Name = "Movies",
            Path = "/media/movies",
            ContentType = "type-a"
        };
        _context.RootFolders.Add(rootFolder);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var a = new ContentHierarchyItem
        {
            MetadataContainerId = container1.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-a",
            Name = "movie-a",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(a);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var b = new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-b",
            Name = "movie-b",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(b);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var setResult = await _subject.BulkPatchAsync(
            new()
            {
                Ids = [a.Id, b.Id],
                Patch = new() { RootFolderId = new() { Value = rootFolder.Id } }
            },
            _userId).ConfigureAwait(false);
        var afterSet = await _context.ContentHierarchyItems.AsNoTracking().ToListAsync().ConfigureAwait(false);

        var clearResult = await _subject.BulkPatchAsync(
            new()
            {
                Ids = [a.Id, b.Id],
                Patch = new() { RootFolderId = new() { Value = null } }
            },
            _userId).ConfigureAwait(false);
        var afterClear = await _context.ContentHierarchyItems.AsNoTracking().ToListAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(setResult.Count, Is.EqualTo(2));
            Assert.That(afterSet.Single(i => i.Id == a.Id).RootFolderId, Is.EqualTo(rootFolder.Id));
            Assert.That(afterSet.Single(i => i.Id == b.Id).RootFolderId, Is.EqualTo(rootFolder.Id));
            Assert.That(clearResult.Count, Is.EqualTo(2));
            Assert.That(afterClear.Single(i => i.Id == a.Id).RootFolderId, Is.Null);
            Assert.That(afterClear.Single(i => i.Id == b.Id).RootFolderId, Is.Null);
        }
    }

    [Test]
    public async Task BulkPatchAsync_ReturnsExistingCount_WhenSomeIdsAreMissing()
    {
        // Arrange
        var rootFolder = new RootFolder
        {
            Id = Guid.NewGuid(),
            Name = "Movies",
            Path = "/media/movies",
            ContentType = "type-a"
        };
        _context.RootFolders.Add(rootFolder);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var a = new ContentHierarchyItem
        {
            MetadataContainerId = container.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-a",
            Name = "movie-a",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(a);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var missing = Guid.NewGuid();

        // Act
        var result = await _subject.BulkPatchAsync(
            new()
            {
                Ids = [a.Id, missing],
                Patch = new() { RootFolderId = new() { Value = rootFolder.Id } }
            },
            _userId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result.Error, Is.Null);
        }
    }

    [Test]
    public async Task BulkPatchAsync_SetsRootFolderOnMatching_WhenSomeMatchesAreExcluded()
    {
        // Arrange
        var rootFolder = new RootFolder
        {
            Id = Guid.NewGuid(),
            Name = "Movies",
            Path = "/media/movies",
            ContentType = "type-a"
        };
        _context.RootFolders.Add(rootFolder);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var a = new ContentHierarchyItem
        {
            MetadataContainerId = container1.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-a",
            Name = "movie-a",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(a);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var b = new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-b",
            Name = "movie-b",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(b);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var container3 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container3);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var c = new ContentHierarchyItem
        {
            MetadataContainerId = container3.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-c",
            Name = "movie-c",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(c);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            ContentType = new() { And = [new() { Value = "type-a" }] },
            ExcludeIds = [c.Id]
        };

        // Act
        var result = await _subject.BulkPatchAsync(
            new()
            {
                Filter = criteria,
                Patch = new() { RootFolderId = new() { Value = rootFolder.Id } }
            },
            _userId).ConfigureAwait(false);

        // Assert
        var refreshed = await _context.ContentHierarchyItems.AsNoTracking().ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(refreshed.Single(i => i.Id == a.Id).RootFolderId, Is.EqualTo(rootFolder.Id));
            Assert.That(refreshed.Single(i => i.Id == b.Id).RootFolderId, Is.EqualTo(rootFolder.Id));
            Assert.That(refreshed.Single(i => i.Id == c.Id).RootFolderId, Is.Null);
        }
    }

    [Test]
    public async Task BulkPatchAsync_SetsAndClearsQualityTierList_WhenIdsAreProvided()
    {
        // Arrange
        var tierList = new QualityTierList { Id = Guid.NewGuid(), Name = "HD Tiers", CreatedAt = DateTime.UtcNow };
        _context.QualityTierLists.Add(tierList);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var a = new ContentHierarchyItem
        {
            MetadataContainerId = container1.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-a",
            Name = "movie-a",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(a);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var b = new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-b",
            Name = "movie-b",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(b);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var setResult = await _subject.BulkPatchAsync(
            new()
            {
                Ids = [a.Id, b.Id],
                Patch = new() { QualityTierListId = new() { Value = tierList.Id } }
            },
            _userId).ConfigureAwait(false);
        var afterSet = await _context.ContentHierarchyItems.AsNoTracking().ToListAsync().ConfigureAwait(false);

        var clearResult = await _subject.BulkPatchAsync(
            new()
            {
                Ids = [a.Id, b.Id],
                Patch = new() { QualityTierListId = new() { Value = null } }
            },
            _userId).ConfigureAwait(false);
        var afterClear = await _context.ContentHierarchyItems.AsNoTracking().ToListAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(setResult.Count, Is.EqualTo(2));
            Assert.That(afterSet.Single(i => i.Id == a.Id).QualityTierListId, Is.EqualTo(tierList.Id));
            Assert.That(afterSet.Single(i => i.Id == b.Id).QualityTierListId, Is.EqualTo(tierList.Id));
            Assert.That(clearResult.Count, Is.EqualTo(2));
            Assert.That(afterClear.Single(i => i.Id == a.Id).QualityTierListId, Is.Null);
            Assert.That(afterClear.Single(i => i.Id == b.Id).QualityTierListId, Is.Null);
        }
    }

    [Test]
    public async Task BulkPatchAsync_SetsQualityTierListOnMatching_WhenSomeMatchesAreExcluded()
    {
        // Arrange
        var tierList = new QualityTierList { Id = Guid.NewGuid(), Name = "HD Tiers", CreatedAt = DateTime.UtcNow };
        _context.QualityTierLists.Add(tierList);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var a = new ContentHierarchyItem
        {
            MetadataContainerId = container1.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-a",
            Name = "movie-a",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(a);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var b = new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-b",
            Name = "movie-b",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(b);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var container3 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container3);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var c = new ContentHierarchyItem
        {
            MetadataContainerId = container3.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-c",
            Name = "movie-c",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(c);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            ContentType = new() { And = [new() { Value = "type-a" }] },
            ExcludeIds = [c.Id]
        };

        // Act
        var result = await _subject.BulkPatchAsync(
            new()
            {
                Filter = criteria,
                Patch = new() { QualityTierListId = new() { Value = tierList.Id } }
            },
            _userId).ConfigureAwait(false);

        // Assert
        var refreshed = await _context.ContentHierarchyItems.AsNoTracking().ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(refreshed.Single(i => i.Id == a.Id).QualityTierListId, Is.EqualTo(tierList.Id));
            Assert.That(refreshed.Single(i => i.Id == b.Id).QualityTierListId, Is.EqualTo(tierList.Id));
            Assert.That(refreshed.Single(i => i.Id == c.Id).QualityTierListId, Is.Null);
        }
    }

    [Test]
    public async Task BulkPatchAsync_TagsMatching_WhenSomeMatchesAreExcluded()
    {
        // Arrange
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var a = new ContentHierarchyItem
        {
            MetadataContainerId = container1.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-a",
            Name = "movie-a",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(a);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var b = new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-b",
            Name = "movie-b",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(b);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var container3 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container3);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var c = new ContentHierarchyItem
        {
            MetadataContainerId = container3.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-c",
            Name = "movie-c",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(c);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            ContentType = new() { And = [new() { Value = "type-a" }] },
            ExcludeIds = [c.Id]
        };

        // Act
        var result = await _subject.BulkPatchAsync(
            new()
            {
                Filter = criteria,
                Patch = new() { Tags = new() { Add = ["Favorites"], Remove = [] } }
            },
            _userId).ConfigureAwait(false);

        // Assert
        var tag = await _context.Tags.AsNoTracking().Include(t => t.MetadataContainers)
            .FirstOrDefaultAsync(t => t.Name == "Favorites").ConfigureAwait(false);
        var taggedContainerIds = tag!.MetadataContainers.Select(mc => mc.Id).ToList();
        var refreshed = await _context.ContentHierarchyItems.AsNoTracking().ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(taggedContainerIds, Does.Contain(refreshed.Single(i => i.Id == a.Id).MetadataContainerId));
            Assert.That(taggedContainerIds, Does.Contain(refreshed.Single(i => i.Id == b.Id).MetadataContainerId));
            Assert.That(taggedContainerIds, Does.Not.Contain(refreshed.Single(i => i.Id == c.Id).MetadataContainerId));
        }
    }

    [Test]
    public async Task PatchAsync_UpdatesMonitoring_WhenMonitoringIsSet()
    {
        // Arrange
        var item = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "patch-movie",
            Name = "Patch Movie",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow,
            MetadataContainer = await _context.MetadataContainers.FindAsync(_metadataContainerId).ConfigureAwait(false)
        };

        _context.ContentHierarchyItems.Add(item);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new ContentHierarchyItemPatch
        {
            Id = item.Id,
            Monitoring = new() { Value = MonitoringMode.Unmonitored }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);

        var updated = await _subject.GetByIdAsync(item.Id).ConfigureAwait(false);
        Assert.That(updated, Has.Property(nameof(updated.Monitoring)).EqualTo(MonitoringMode.Unmonitored));
    }

    [Test]
    public async Task PatchAsync_UpdatesQualityTierListId_WhenQualityTierListIdIsSet()
    {
        // Arrange
        var tierList = new QualityTierList { Name = "Test Tier List", CreatedAt = DateTime.UtcNow };
        _context.QualityTierLists.Add(tierList);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var item = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "quality-movie",
            Name = "Quality Movie",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow,
            MetadataContainer = await _context.MetadataContainers.FindAsync(_metadataContainerId).ConfigureAwait(false)
        };

        _context.ContentHierarchyItems.Add(item);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new ContentHierarchyItemPatch
        {
            Id = item.Id,
            QualityTierListId = new() { Value = tierList.Id }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);

        var updated = await _subject.GetByIdAsync(item.Id).ConfigureAwait(false);
        Assert.That(updated, Has.Property(nameof(updated.QualityTierListId)).EqualTo(tierList.Id));
    }

    [Test]
    public async Task PatchAsync_ClearsQualityTierListId_WhenQualityTierListIdIsCleared()
    {
        // Arrange
        var tierList = new QualityTierList { Name = "To Clear Tier List", CreatedAt = DateTime.UtcNow };
        _context.QualityTierLists.Add(tierList);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var item = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "clear-quality-movie",
            Name = "Clear Quality Movie",
            Monitoring = MonitoringMode.Monitored,
            QualityTierListId = tierList.Id,
            CreatedAt = DateTime.UtcNow,
            MetadataContainer = await _context.MetadataContainers.FindAsync(_metadataContainerId).ConfigureAwait(false)
        };

        _context.ContentHierarchyItems.Add(item);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new ContentHierarchyItemPatch
        {
            Id = item.Id,
            QualityTierListId = new() { Value = null }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);

        var updated = await _subject.GetByIdAsync(item.Id).ConfigureAwait(false);
        Assert.That(updated, Has.Property(nameof(updated.QualityTierListId)).Null);
    }

    [Test]
    public async Task PatchAsync_UpdatesPath_WhenPathIsSet()
    {
        // Arrange
        var item = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "path-movie",
            Name = "Path Movie",
            Monitoring = MonitoringMode.Monitored,
            Path = "/old/path",
            CreatedAt = DateTime.UtcNow,
            MetadataContainer = await _context.MetadataContainers.FindAsync(_metadataContainerId).ConfigureAwait(false)
        };

        _context.ContentHierarchyItems.Add(item);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new ContentHierarchyItemPatch
        {
            Id = item.Id,
            Path = new() { Value = "/new/path" }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);

        var updated = await _subject.GetByIdAsync(item.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated, Has.Property(nameof(updated.Path)).EqualTo("/new/path"));
            Assert.That(updated!.Settings.ContainsKey("content.path.origin"), Is.False);
        }
    }

    [Test]
    public async Task PatchAsync_ClearsPathAndOrigin_WhenNullablePatchIsUsed()
    {
        // Arrange
        var item = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "inherited-path-movie",
            Name = "Inherited Path Movie",
            Monitoring = MonitoringMode.Monitored,
            Path = "/old/path",
            CreatedAt = DateTime.UtcNow,
            MetadataContainer = await _context.MetadataContainers.FindAsync(_metadataContainerId)
                .ConfigureAwait(false)
        };
        item.Path = "/old/path";
        item.Settings["content.path.origin"] = "Generated";
        _context.ContentHierarchyItems.Add(item);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new ContentHierarchyItemPatch
        {
            Id = item.Id,
            Path = new() { Value = null }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        var updated = await _subject.GetByIdAsync(item.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(updated, Has.Property(nameof(updated.Path)).Null);
            Assert.That(updated!.Settings, Is.Empty);
        }
    }

    [Test]
    public async Task PatchAsync_UpdatesLanguagePreferences_WhenPreferencesAreSet()
    {
        // Arrange
        var item = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "lang-movie",
            Name = "Language Movie",
            Monitoring = MonitoringMode.Monitored,
            LanguagePreferences = ["en"],
            CreatedAt = DateTime.UtcNow,
            MetadataContainer = await _context.MetadataContainers.FindAsync(_metadataContainerId).ConfigureAwait(false)
        };

        _context.ContentHierarchyItems.Add(item);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new ContentHierarchyItemPatch
        {
            Id = item.Id,
            LanguagePreferences = new()
            {
                Insert = new() { [1] = "es" }
            }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);

        var updated = await _subject.GetByIdAsync(item.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated!.LanguagePreferences, Has.Count.EqualTo(2));
            Assert.That(updated!.LanguagePreferences[0], Is.EqualTo("en"));
            Assert.That(updated!.LanguagePreferences[1], Is.EqualTo("es"));
        }
    }

    [Test]
    public async Task PatchAsync_ReturnsFalse_WhenNotExists()
    {
        // Arrange
        var patch = new ContentHierarchyItemPatch
        {
            Id = Guid.NewGuid(),
            Monitoring = new() { Value = MonitoringMode.Unmonitored }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task PatchAsync_CreatesAuditRecord_WhenItemIsPatched()
    {
        // Arrange
        var item = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "audit-patch-movie",
            Name = "Audit Patch Movie",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow,
            MetadataContainer = await _context.MetadataContainers.FindAsync(_metadataContainerId).ConfigureAwait(false)
        };

        _context.ContentHierarchyItems.Add(item);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new ContentHierarchyItemPatch
        {
            Id = item.Id,
            Monitoring = new() { Value = MonitoringMode.Unmonitored }
        };

        // Act
        await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Patch"));
            Assert.That(
                auditRecords,
                Has.One.With.Property(nameof(AuditRecord.EntityType))
                    .EqualTo("ContentHierarchyItem"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task PatchAsync_AddsTags_WhenTagAdditionsAreSet()
    {
        // Arrange
        var item = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "tag-movie",
            Name = "Tag Movie",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow,
            MetadataContainer = await _context.MetadataContainers.FindAsync(_metadataContainerId).ConfigureAwait(false)
        };

        _context.ContentHierarchyItems.Add(item);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new ContentHierarchyItemPatch
        {
            Id = item.Id,
            Tags = new() { Add = ["action", "adventure"] }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);

        var tags = await _context.Tags.Include(t => t.MetadataContainers).ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tags, Has.Count.EqualTo(2));
            Assert.That(tags, Has.One.With.Property(nameof(Tag.Name)).EqualTo("action"));
            Assert.That(tags, Has.One.With.Property(nameof(Tag.Name)).EqualTo("adventure"));
        }
    }

    [Test]
    public async Task PatchAsync_RemovesTags_WhenTagRemovalsAreSet()
    {
        // Arrange
        var metadataContainer = await _context.MetadataContainers.FindAsync(_metadataContainerId).ConfigureAwait(false);

        var tag = new Tag { Name = "to-remove" };
        tag.MetadataContainers.Add(metadataContainer!);
        _context.Tags.Add(tag);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var item = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "remove-tag-movie",
            Name = "Remove Tag Movie",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow,
            MetadataContainer = metadataContainer
        };

        _context.ContentHierarchyItems.Add(item);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new ContentHierarchyItemPatch
        {
            Id = item.Id,
            Tags = new() { Remove = ["to-remove"] }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);

        var updatedTag = await _context.Tags
            .Include(t => t.MetadataContainers)
            .FirstOrDefaultAsync(t => t.Name == "to-remove").ConfigureAwait(false);
        Assert.That(
            updatedTag!.MetadataContainers,
            Has
                .None.Matches<MetadataContainer>(mc => mc.Id == _metadataContainerId));
    }

    [Test]
    public async Task PatchAsync_CreatesAuditRecordsForTagChanges_WhenTagsChange()
    {
        // Arrange
        var metadataContainer = await _context.MetadataContainers.FindAsync(_metadataContainerId).ConfigureAwait(false);

        var existingTag = new Tag { Name = "existing-tag" };
        existingTag.MetadataContainers.Add(metadataContainer!);
        _context.Tags.Add(existingTag);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var item = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "tag-audit-movie",
            Name = "Tag Audit Movie",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow,
            MetadataContainer = metadataContainer
        };

        _context.ContentHierarchyItems.Add(item);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new ContentHierarchyItemPatch
        {
            Id = item.Id,
            Tags = new()
            {
                Add = ["new-tag"],
                Remove = ["existing-tag"]
            }
        };

        // Act
        await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(3));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Patch"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("AddTag"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("RemoveTag"));
        }
    }

    [Test]
    public async Task PatchAsync_AddsUpdatesAndRemovesSettings_WhenTheSettingsPatchNamesEachOperation()
    {
        // Arrange
        var item = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "settings-patch-movie",
            Name = "Settings Patch Movie",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow,
            MetadataContainer = await _context.MetadataContainers.FindAsync(_metadataContainerId)
                .ConfigureAwait(false)
        };
        item.Settings["mode"] = "old";
        item.Settings["stale"] = "drop";
        _context.ContentHierarchyItems.Add(item);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new ContentHierarchyItemPatch
        {
            Id = item.Id,
            Settings = new()
            {
                Add = new() { ["limit"] = "10" },
                Update = new() { ["mode"] = "new", ["absent"] = "ignored" },
                Remove = ["stale"]
            }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        var updated = await _subject.GetByIdAsync(item.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(updated!.Settings["limit"], Is.EqualTo("10"));
            Assert.That(updated.Settings["mode"], Is.EqualTo("new"));
            Assert.That(updated.Settings.ContainsKey("stale"), Is.False);
            Assert.That(updated.Settings.ContainsKey("absent"), Is.False);
        }
    }
}
