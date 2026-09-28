using System;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Data.SQLite;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Models.Paging;
using Magpie.Sdk.Services;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public sealed class ContentHierarchyItemRepositoryShotgunTests
{
    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private IContentHierarchyItemRepository _subject = null!;
    private Guid _userId;
    private RootFolder _rootFolder = null!;
    private QualityTierList _qualityTierList = null!;

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
        _rootFolder = new RootFolder
        {
            Id = Guid.NewGuid(),
            Name = "Type A",
            Path = "/library/type-a",
            ContentType = "type-a",
            IsDefault = true,
            CreatedAt = DateTime.UtcNow
        };
        _qualityTierList = new QualityTierList
        {
            Id = Guid.NewGuid(),
            Name = "Default",
            CreatedAt = DateTime.UtcNow
        };
        _context.Users.Add(user);
        _context.RootFolders.Add(_rootFolder);
        _context.QualityTierLists.Add(_qualityTierList);
        _context.SaveChanges();
        _userId = user.Id;
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

    [TestCase(null, null, MonitoringMode.Shotgun)]
    [TestCase(MonitoringMode.Monitored, MonitoringMode.Monitored, MonitoringMode.Monitored)]
    [TestCase(MonitoringMode.Unmonitored, MonitoringMode.Unmonitored, MonitoringMode.Unmonitored)]
    [TestCase(MonitoringMode.Monitored, MonitoringMode.Shotgun, MonitoringMode.Shotgun)]
    public async Task PatchAsync_ResolvesChildMonitoringFromNearestExplicitMonitoring_WhenParentIsPatchedToShotgun(
        MonitoringMode? parentMonitoring,
        MonitoringMode? childMonitoring,
        MonitoringMode expectedChildMonitoring)
    {
        // Arrange
        var parent = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow },
            ContentType = "type-a",
            Level = "root",
            Slug = "parent",
            Name = "Parent",
            Monitoring = parentMonitoring,
            EffectiveRootFolderId = _rootFolder.Id,
            EffectiveQualityTierListId = _qualityTierList.Id,
            CreatedAt = DateTime.UtcNow
        };
        var child = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow },
            ParentId = parent.Id,
            ContentType = "type-a",
            Level = "child",
            Slug = "child",
            Name = "Child",
            Monitoring = childMonitoring,
            EffectiveRootFolderId = _rootFolder.Id,
            EffectiveQualityTierListId = _qualityTierList.Id,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(parent, child);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.PatchAsync(
            new() { Id = parent.Id, Monitoring = new() { Value = MonitoringMode.Shotgun } },
            _userId).ConfigureAwait(false);

        // Assert
        var storedParent = await _context.ContentHierarchyItems.AsNoTracking()
            .SingleAsync(item => item.Id == parent.Id).ConfigureAwait(false);
        var storedChild = await _context.ContentHierarchyItems.AsNoTracking()
            .SingleAsync(item => item.Id == child.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(storedParent.Monitoring, Is.EqualTo(MonitoringMode.Shotgun));
            Assert.That(storedParent.EffectiveMonitoring, Is.EqualTo(MonitoringMode.Shotgun));
            Assert.That(storedChild.Monitoring, Is.EqualTo(childMonitoring));
            Assert.That(storedChild.EffectiveMonitoring, Is.EqualTo(expectedChildMonitoring));
        }
    }

    [TestCase(MonitoringMode.Monitored, MonitoringMode.Monitored)]
    [TestCase(MonitoringMode.Unmonitored, MonitoringMode.Unmonitored)]
    public async Task PatchAsync_ClearsShotgunForInheritingChild_WhenParentMonitoringIsPatched(
        MonitoringMode monitoring,
        MonitoringMode expectedChildMonitoring)
    {
        // Arrange
        var parent = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow },
            ContentType = "type-a",
            Level = "root",
            Slug = "parent",
            Name = "Parent",
            Monitoring = MonitoringMode.Shotgun,
            EffectiveMonitoring = MonitoringMode.Shotgun,
            EffectiveRootFolderId = _rootFolder.Id,
            EffectiveQualityTierListId = _qualityTierList.Id,
            CreatedAt = DateTime.UtcNow
        };
        var child = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow },
            ParentId = parent.Id,
            ContentType = "type-a",
            Level = "child",
            Slug = "child",
            Name = "Child",
            EffectiveMonitoring = MonitoringMode.Shotgun,
            EffectiveRootFolderId = _rootFolder.Id,
            EffectiveQualityTierListId = _qualityTierList.Id,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(parent, child);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.PatchAsync(
            new() { Id = parent.Id, Monitoring = new() { Value = monitoring } },
            _userId).ConfigureAwait(false);

        // Assert
        var storedParent = await _context.ContentHierarchyItems.AsNoTracking()
            .SingleAsync(item => item.Id == parent.Id).ConfigureAwait(false);
        var storedChild = await _context.ContentHierarchyItems.AsNoTracking()
            .SingleAsync(item => item.Id == child.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(storedParent.Monitoring, Is.EqualTo(monitoring));
            Assert.That(storedChild.EffectiveMonitoring, Is.EqualTo(expectedChildMonitoring));
        }
    }

    [Test]
    public async Task PatchAsync_LimitsShotgunToChildSubtree_WhenMonitoredParentHasShotgunChild()
    {
        // Arrange
        var parent = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow },
            ContentType = "type-a",
            Level = "root",
            Slug = "parent",
            Name = "Parent",
            Monitoring = MonitoringMode.Monitored,
            EffectiveMonitoring = MonitoringMode.Monitored,
            EffectiveRootFolderId = _rootFolder.Id,
            EffectiveQualityTierListId = _qualityTierList.Id,
            CreatedAt = DateTime.UtcNow
        };
        var child = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow },
            ParentId = parent.Id,
            ContentType = "type-a",
            Level = "child",
            Slug = "child",
            Name = "Child",
            EffectiveMonitoring = MonitoringMode.Monitored,
            EffectiveRootFolderId = _rootFolder.Id,
            EffectiveQualityTierListId = _qualityTierList.Id,
            CreatedAt = DateTime.UtcNow
        };
        var grandchild = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow },
            ParentId = child.Id,
            ContentType = "type-a",
            Level = "grandchild",
            Slug = "grandchild",
            Name = "Grandchild",
            EffectiveMonitoring = MonitoringMode.Monitored,
            EffectiveRootFolderId = _rootFolder.Id,
            EffectiveQualityTierListId = _qualityTierList.Id,
            CreatedAt = DateTime.UtcNow
        };
        var sibling = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow },
            ParentId = parent.Id,
            ContentType = "type-a",
            Level = "child",
            Slug = "sibling",
            Name = "Sibling",
            EffectiveMonitoring = MonitoringMode.Monitored,
            EffectiveRootFolderId = _rootFolder.Id,
            EffectiveQualityTierListId = _qualityTierList.Id,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(parent, child, grandchild, sibling);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject.PatchAsync(
            new() { Id = child.Id, Monitoring = new() { Value = MonitoringMode.Shotgun } },
            _userId).ConfigureAwait(false);

        // Assert
        var monitoringBySlug = await _context.ContentHierarchyItems.AsNoTracking()
            .ToDictionaryAsync(item => item.Slug, item => item.EffectiveMonitoring).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(monitoringBySlug["parent"], Is.EqualTo(MonitoringMode.Monitored));
            Assert.That(monitoringBySlug["child"], Is.EqualTo(MonitoringMode.Shotgun));
            Assert.That(monitoringBySlug["grandchild"], Is.EqualTo(MonitoringMode.Shotgun));
            Assert.That(monitoringBySlug["sibling"], Is.EqualTo(MonitoringMode.Monitored));
        }
    }

    [TestCase(MonitoringMode.Shotgun, new[] { "shotgun" })]
    [TestCase(MonitoringMode.Monitored, new[] { "monitored" })]
    [TestCase(MonitoringMode.Unmonitored, new[] { "unmonitored" })]
    public async Task QueryAsync_MatchesTheEffectiveMonitoringMode_WhenMonitoringCriterionIsSet(
        MonitoringMode monitoring,
        string[] expected)
    {
        // Arrange
        _context.ContentHierarchyItems.AddRange(
            new ContentHierarchyItem
            {
                MetadataContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow },
                ContentType = "type-a",
                Level = "root",
                Slug = "shotgun",
                Name = "Shotgun",
                Monitoring = MonitoringMode.Shotgun,
                EffectiveMonitoring = MonitoringMode.Shotgun,
                EffectiveRootFolderId = _rootFolder.Id,
                CreatedAt = DateTime.UtcNow
            },
            new ContentHierarchyItem
            {
                MetadataContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow },
                ContentType = "type-a",
                Level = "root",
                Slug = "monitored",
                Name = "Monitored",
                Monitoring = MonitoringMode.Monitored,
                EffectiveMonitoring = MonitoringMode.Monitored,
                EffectiveRootFolderId = _rootFolder.Id,
                CreatedAt = DateTime.UtcNow
            },
            new ContentHierarchyItem
            {
                MetadataContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow },
                ContentType = "type-a",
                Level = "root",
                Slug = "unmonitored",
                Name = "Unmonitored",
                Monitoring = MonitoringMode.Unmonitored,
                EffectiveRootFolderId = _rootFolder.Id,
                CreatedAt = DateTime.UtcNow
            });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(
            new ContentHierarchyItemCriteria
            {
                Monitoring = new CriteriaSet<PropertyCriterion<MonitoringMode>>
                {
                    And = [new PropertyCriterion<MonitoringMode> { Value = monitoring }]
                }
            },
            new PagingParameter { PageNumber = 1, PageSize = 10 },
            null).ConfigureAwait(false);

        // Assert
        Assert.That(result.Items.Select(item => item.Slug), Is.EqualTo(expected));
    }

    [TestCase(true, new[] { "monitored", "shotgun" })]
    [TestCase(false, new[] { "unmonitored" })]
    public async Task QueryAsync_MatchesEveryMonitoredMode_WhenEffectiveMonitoringIsSet(
        bool effectiveMonitoring,
        string[] expected)
    {
        // Arrange
        _context.ContentHierarchyItems.AddRange(
            new ContentHierarchyItem
            {
                MetadataContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow },
                ContentType = "type-a",
                Level = "root",
                Slug = "shotgun",
                Name = "Shotgun",
                Monitoring = MonitoringMode.Shotgun,
                EffectiveMonitoring = MonitoringMode.Shotgun,
                EffectiveRootFolderId = _rootFolder.Id,
                CreatedAt = DateTime.UtcNow
            },
            new ContentHierarchyItem
            {
                MetadataContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow },
                ContentType = "type-a",
                Level = "root",
                Slug = "monitored",
                Name = "Monitored",
                Monitoring = MonitoringMode.Monitored,
                EffectiveMonitoring = MonitoringMode.Monitored,
                EffectiveRootFolderId = _rootFolder.Id,
                CreatedAt = DateTime.UtcNow
            },
            new ContentHierarchyItem
            {
                MetadataContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow },
                ContentType = "type-a",
                Level = "root",
                Slug = "unmonitored",
                Name = "Unmonitored",
                Monitoring = MonitoringMode.Unmonitored,
                EffectiveRootFolderId = _rootFolder.Id,
                CreatedAt = DateTime.UtcNow
            });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(
            new ContentHierarchyItemCriteria { EffectiveMonitoring = effectiveMonitoring },
            new PagingParameter { PageNumber = 1, PageSize = 10 },
            null).ConfigureAwait(false);

        // Assert
        Assert.That(result.Items.Select(item => item.Slug).Order(), Is.EqualTo(expected));
    }
}
