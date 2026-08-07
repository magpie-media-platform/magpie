using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Models.Paging;
using Magpie.Sdk.Events;
using Magpie.Sdk.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public class ContentHierarchyItemRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IContentHierarchyItemRepository _subject = null!;
    private ITransactionManager _transactionManager = null!;
    private Mock<IContentTypeRegistry> _contentTypeRegistry = null!;
    private Mock<IEventBus> _eventBus = null!;
    private Guid _userId;
    private Guid _metadataContainerId;

    [SetUp]
    public void SetUp()
    {
        _context = new MagpieDbContext(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options);

        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();

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

        var metadataContainer = new MetadataContainer
        {
            CreatedAt = DateTime.UtcNow
        };

        _context.MetadataContainers.Add(metadataContainer);
        _context.SaveChanges();

        _metadataContainerId = metadataContainer.Id;

        _eventBus = new Mock<IEventBus>();
        _contentTypeRegistry = new Mock<IContentTypeRegistry>();
        _transactionManager = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);
        var auditRecordRepository = new AuditRecordRepository(
            _context,
            _eventBus.Object,
            NullLoggerFactory.Instance.CreateLogger<AuditRecordRepository>(),
            _transactionManager);

        var subjectRepository = new SubjectRepository(
            _context,
            auditRecordRepository,
            _transactionManager,
            Mock.Of<IEventBus>(),
            NullLoggerFactory.Instance.CreateLogger<SubjectRepository>());

        var metadataContainerRepository = new MetadataContainerRepository(
            _context,
            auditRecordRepository,
            subjectRepository,
            _transactionManager,
            NullLoggerFactory.Instance.CreateLogger<MetadataContainerRepository>());

        _subject = new ContentHierarchyItemRepository(
            _context,
            auditRecordRepository,
            _transactionManager,
            metadataContainerRepository,
            _contentTypeRegistry.Object,
            _eventBus.Object,
            NullLoggerFactory.Instance.CreateLogger<ContentHierarchyItemRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [Test]
    public async Task GetByIdAsync_ReturnsContentHierarchyItem_WhenExists()
    {
        // Arrange
        var item = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "test-movie",
            Name = "Test Movie",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(item);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.GetByIdAsync(item.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("Test Movie"));
            Assert.That(result, Has.Property(nameof(result.Slug)).EqualTo("test-movie"));
            Assert.That(result, Has.Property(nameof(result.ContentType)).EqualTo("type-a"));
            Assert.That(result, Has.Property(nameof(result.Level)).EqualTo("level-a"));
            Assert.That(result, Has.Property(nameof(result.Monitoring)).EqualTo(true));
        }
    }

    [Test]
    public async Task GetByIdAsync_ReturnsNull_WhenNotExists()
    {
        // Act
        var result = await _subject.GetByIdAsync(Guid.NewGuid()).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task QueryAsync_ReturnsTheAncestorFields_WhenSelectingByIds()
    {
        // Arrange
        var childContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(childContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var parent = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "parent",
            Name = "Parent",
            EffectiveMonitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(parent);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var child = new ContentHierarchyItem
        {
            MetadataContainerId = childContainer.Id,
            ParentId = parent.Id,
            ContentType = "type-b",
            Level = "level-b",
            Slug = "child",
            Name = "Child",
            EffectiveMonitoring = false,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(child);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject
            .QueryAsync(new ContentHierarchyItemCriteria { Ids = [child.Id] },
                new() { PageNumber = 1, PageSize = 100 },
                null)
            .ConfigureAwait(false);

        // Assert
        var node = result.Items.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node.Id, Is.EqualTo(child.Id));
            Assert.That(node.ParentId, Is.EqualTo(parent.Id));
            Assert.That(node.ContentType, Is.EqualTo("type-b"));
            Assert.That(node.Level, Is.EqualTo("level-b"));
            Assert.That(node.Name, Is.EqualTo("Child"));
            Assert.That(node.EffectiveMonitoring, Is.False);
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsContentHierarchyItem_WhenSlugExists()
    {
        // Arrange
        var item = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "test-movie-slug",
            Name = "Test Movie Slug",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(item);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.QueryAsync(
            new()
            {
                ContentType = new() { And = [new() { Value = "type-a" }] },
                Level = new() { And = [new() { Value = "level-a" }] },
                Slug = new() { And = [new() { Value = "test-movie-slug" }] }
            },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false)).Items.SingleOrDefault();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("Test Movie Slug"));
            Assert.That(result, Has.Property(nameof(result.Slug)).EqualTo("test-movie-slug"));
            Assert.That(result, Has.Property(nameof(result.ContentType)).EqualTo("type-a"));
            Assert.That(result, Has.Property(nameof(result.Level)).EqualTo("level-a"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsNull_WhenSlugDoesNotExist()
    {
        // Act
        var result = (await _subject.QueryAsync(
            new()
            {
                ContentType = new() { And = [new() { Value = "type-a" }] },
                Level = new() { And = [new() { Value = "level-a" }] },
                Slug = new() { And = [new() { Value = "nonexistent-slug" }] }
            },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false)).Items.SingleOrDefault();

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ListAllAsync_ReturnsAllItems_WhenItemsExist()
    {
        // Arrange
        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var item1 = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-1",
            Name = "Movie 1",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        var item2 = new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-2",
            Name = "Movie 2",
            Monitoring = null,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(item1, item2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.ListAllAsync().ConfigureAwait(false);

        // Assert
        Assert.That(result, Has.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_ReturnsEmptyList_WhenNoItems()
    {
        // Act
        var result = await _subject.ListAllAsync().ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task CreateAsync_CreatesContentHierarchyItem_WhenRequestIsValid()
    {
        // Arrange
        var defaultRootFolder = new RootFolder
        {
            Name = "Type A",
            Path = "/library/type-a",
            ContentType = "type-a",
            IsDefault = true,
            Defaults = new()
            {
                ["level-a"] = new()
            },
            CreatedAt = DateTime.UtcNow
        };
        var defaultQualityTierList = new QualityTierList
        {
            Name = "Default",
            CreatedAt = DateTime.UtcNow
        };
        _context.RootFolders.Add(defaultRootFolder);
        _context.QualityTierLists.Add(defaultQualityTierList);
        defaultRootFolder.Defaults["level-a"].QualityTierListId = defaultQualityTierList.Id;
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var item = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "new-movie",
            Name = "New Movie",
            Monitoring = true,
            Path = "/movies/new-movie",
            LanguagePreferences = ["en", "es"]
        };

        // Act
        var result = await _subject.CreateAsync(item, _userId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Id)).Not.EqualTo(Guid.Empty));
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("New Movie"));
            Assert.That(result, Has.Property(nameof(result.Slug)).EqualTo("new-movie"));
            Assert.That(result, Has.Property(nameof(result.ContentType)).EqualTo("type-a"));
            Assert.That(result, Has.Property(nameof(result.CreatedByUserId)).EqualTo(_userId));
            Assert.That(result, Has.Property(nameof(result.CreatedAt)).Not.EqualTo(default(DateTime)));
            Assert.That(result.EffectiveMonitoring, Is.True);
            Assert.That(result.EffectiveRootFolderId, Is.EqualTo(defaultRootFolder.Id));
            Assert.That(result.EffectiveQualityTierListId, Is.EqualTo(defaultQualityTierList.Id));
        }
    }

    [Test]
    public async Task CreateAsync_UsesAncestorQualityTierDefault_WhenExactDefaultIsMissing()
    {
        // Arrange
        var qualityTierList = new QualityTierList
        {
            Id = Guid.NewGuid(),
            Name = "Ancestor default",
            CreatedAt = DateTime.UtcNow
        };
        var rootFolder = new RootFolder
        {
            Name = "Type A",
            Path = "/library/ancestor-default",
            ContentType = "type-a",
            IsDefault = true,
            Defaults = new()
            {
                ["root"] = new() { QualityTierListId = qualityTierList.Id }
            },
            CreatedAt = DateTime.UtcNow
        };
        _context.QualityTierLists.Add(qualityTierList);
        _context.RootFolders.Add(rootFolder);
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
                        Slug = "child",
                        ParentSlug = "root",
                        DisplayName = "Child",
                        CustomMetadataFields = []
                    }
                ]
            });
        var item = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "child",
            Slug = "ancestor-default",
            Name = "Ancestor default",
            Monitoring = true
        };

        // Act
        var result = await _subject.CreateAsync(item, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result.EffectiveQualityTierListId, Is.EqualTo(qualityTierList.Id));
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
            Monitoring = true,
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
    public async Task CreateAsync_LeavesEffectiveQualityTierListNull_WhenNoDefaultExists()
    {
        // Arrange
        var rootFolder = new RootFolder
        {
            Name = "Type A",
            Path = "/library/missing-quality-default",
            ContentType = "type-a",
            IsDefault = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.RootFolders.Add(rootFolder);
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
                    }
                ]
            });
        var item = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "root",
            Slug = "missing-quality-default",
            Name = "Missing quality default",
            Monitoring = true
        };

        // Act
        var result = await _subject.CreateAsync(item, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result.EffectiveQualityTierListId, Is.Null);
    }

    [Test]
    public async Task CreateAsync_UsesExplicitQualityTierList_WhenRootFolderDefaultExists()
    {
        // Arrange
        var defaultTierList = new QualityTierList
        {
            Id = Guid.NewGuid(),
            Name = "Default",
            CreatedAt = DateTime.UtcNow
        };
        var explicitTierList = new QualityTierList
        {
            Id = Guid.NewGuid(),
            Name = "Explicit",
            CreatedAt = DateTime.UtcNow
        };
        var rootFolder = new RootFolder
        {
            Name = "Type A",
            Path = "/library/explicit-quality",
            ContentType = "type-a",
            IsDefault = true,
            Defaults = new()
            {
                ["root"] = new() { QualityTierListId = defaultTierList.Id }
            },
            CreatedAt = DateTime.UtcNow
        };
        _context.QualityTierLists.AddRange(defaultTierList, explicitTierList);
        _context.RootFolders.Add(rootFolder);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var item = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "root",
            Slug = "explicit-quality",
            Name = "Explicit quality",
            QualityTierListId = explicitTierList.Id,
            Monitoring = true
        };

        // Act
        var result = await _subject.CreateAsync(item, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result.EffectiveQualityTierListId, Is.EqualTo(explicitTierList.Id));
    }

    [Test]
    public async Task TryCreateAsync_CreatesRootWithMetadataContainer_InsideTransaction()
    {
        // Arrange
        var rootFolder = new RootFolder
        {
            Name = "Root A",
            Path = "D:\\RootA",
            ContentType = "type-a",
            IsDefault = true,
            Defaults = new()
            {
                ["level-a"] = new()
            },
            CreatedAt = DateTime.UtcNow
        };
        _context.RootFolders.Add(rootFolder);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var item = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow },
            ContentType = "type-a",
            Level = "level-a",
            Slug = "provider-a-4cea14c3-7f94-41a8-8e58-3384af834b0d",
            Name = "provider-a-4cea14c3-7f94-41a8-8e58-3384af834b0d",
            RootFolderId = rootFolder.Id,
            PendingMetadataRefresh = true
        };

        // Act
        var result = false;
        await _transactionManager.ExecuteWithTransactionAsync(
            async ct => result = await _subject.TryCreateAsync(item, _userId, ct).ConfigureAwait(false),
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(item.MetadataContainerId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(
                await _context.ContentHierarchyItems.FindAsync(item.Id).ConfigureAwait(false),
                Is.Not.Null);
        }
    }

    [Test]
    public async Task TryCreateAsync_ReturnsFalseAndDetachesLoser_WhenUniqueKeyExists()
    {
        // Arrange
        var rootFolder = new RootFolder
        {
            Id = Guid.NewGuid(),
            Name = "Root A",
            Path = "D:\\RootA",
            ContentType = "type-a",
            IsDefault = true,
            Defaults = new()
            {
                ["level-a"] = new()
            },
            CreatedAt = DateTime.UtcNow
        };
        var winner = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow },
            ContentType = "type-a",
            Level = "level-a",
            Slug = "provider-a-duplicate",
            Name = "Winner",
            RootFolderId = rootFolder.Id,
            EffectiveRootFolderId = rootFolder.Id,
            CreatedAt = DateTime.UtcNow
        };
        _context.RootFolders.Add(rootFolder);
        _context.ContentHierarchyItems.Add(winner);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var loser = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow },
            ContentType = "type-a",
            Level = "level-a",
            Slug = "provider-a-duplicate",
            Name = "Loser",
            RootFolderId = rootFolder.Id
        };

        // Act
        var result = await _subject.TryCreateAsync(loser, _userId).ConfigureAwait(false);

        // Assert
        var keyRows = await _context.ContentHierarchyItems
            .AsNoTracking()
            .CountAsync(item => item.ContentType == "type-a" &&
                item.ParentId == null &&
                item.Level == "level-a" &&
                item.Slug == "provider-a-duplicate")
            .ConfigureAwait(false);
        var loserAuditExists = await _context.AuditRecords
            .AsNoTracking()
            .AnyAsync(record => record.EntityId == loser.Id.ToString())
            .ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.False);
            Assert.That(keyRows, Is.EqualTo(1));
            Assert.That(_context.Entry(loser).State, Is.EqualTo(EntityState.Detached));
            Assert.That(_context.Entry(loser.MetadataContainer!).State, Is.EqualTo(EntityState.Detached));
            Assert.That(loserAuditExists, Is.False);
        }

        _eventBus.Verify(
            bus => bus.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task TryCreateAsync_DefersCreatedEvent_UntilSharedTransactionCommits()
    {
        // Arrange
        var rootFolder = new RootFolder
        {
            Name = "Root A",
            Path = "D:\\RootA",
            ContentType = "type-a",
            IsDefault = true,
            Defaults = new()
            {
                ["level-a"] = new()
            },
            CreatedAt = DateTime.UtcNow
        };
        _context.RootFolders.Add(rootFolder);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var item = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow },
            ContentType = "type-a",
            Level = "level-a",
            Slug = "provider-a-deferred",
            Name = "Deferred",
            RootFolderId = rootFolder.Id
        };

        // Act
        await _transactionManager.ExecuteWithTransactionAsync(
            async cancellationToken =>
            {
                await _subject.TryCreateAsync(item, _userId, cancellationToken).ConfigureAwait(false);
                _eventBus.Verify(
                    bus => bus.PublishAsync(
                        It.Is<MagpieEvent>(magpieEvent =>
                            magpieEvent.Name == MagpieEvents.Names.ContentHierarchyItemCreated),
                        It.IsAny<CancellationToken>()),
                    Times.Never);
            },
            default).ConfigureAwait(false);

        // Assert
        _eventBus.Verify(
            bus => bus.PublishAsync(
                It.Is<MagpieEvent>(magpieEvent =>
                    magpieEvent.Name == MagpieEvents.Names.ContentHierarchyItemCreated),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task TryCreateAsync_Throws_WhenInsertViolationIsNotUniqueKey()
    {
        // Arrange
        await _context.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"content_hierarchyItem\" " +
            "ADD COLUMN \"MetadataRefreshAttempts\" INTEGER NOT NULL;").ConfigureAwait(false);
        var rootFolder = new RootFolder
        {
            Name = "Root A",
            Path = "D:\\RootA",
            ContentType = "type-a",
            IsDefault = true,
            Defaults = new()
            {
                ["level-a"] = new()
            },
            CreatedAt = DateTime.UtcNow
        };
        _context.RootFolders.Add(rootFolder);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var item = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow },
            ContentType = "type-a",
            Level = "level-a",
            Slug = "provider-a-4cea14c3-7f94-41a8-8e58-3384af834b0d",
            Name = "provider-a-4cea14c3-7f94-41a8-8e58-3384af834b0d",
            RootFolderId = rootFolder.Id,
            PendingMetadataRefresh = true
        };

        // Act
        var exception = Assert.ThrowsAsync<DbUpdateException>(
            async () => await _subject.TryCreateAsync(item, _userId).ConfigureAwait(false));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception!.InnerException, Has.Message.Contain("MetadataRefreshAttempts"));
            Assert.That(_context.Entry(item).State, Is.EqualTo(EntityState.Detached));
            Assert.That(_context.Entry(item.MetadataContainer!).State, Is.EqualTo(EntityState.Detached));
        }
    }

    [Test]
    public async Task TryCreateAsync_ThrowsAndRollsBack_WhenAuditCreateFails()
    {
        // Arrange
        var rootFolder = new RootFolder
        {
            Name = "Root A",
            Path = "D:\\RootA",
            ContentType = "type-a",
            IsDefault = true,
            Defaults = new()
            {
                ["level-a"] = new()
            },
            CreatedAt = DateTime.UtcNow
        };
        _context.RootFolders.Add(rootFolder);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var item = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "audit-failure",
            Name = "Audit failure",
            RootFolderId = rootFolder.Id,
            PendingMetadataRefresh = true
        };
        var auditRecordRepository = new Mock<IAuditRecordRepository>();
        auditRecordRepository
            .Setup(repository => repository.CreateAsync(
                It.IsAny<AuditRecord>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("Audit insert failed."));
        var subject = new ContentHierarchyItemRepository(
            _context,
            auditRecordRepository.Object,
            _transactionManager,
            Mock.Of<IMetadataContainerRepository>(),
            Mock.Of<IContentTypeRegistry>(),
            _eventBus.Object,
            NullLoggerFactory.Instance.CreateLogger<ContentHierarchyItemRepository>());

        // Act
        var exception = Assert.ThrowsAsync<DbUpdateException>(
            async () => await _transactionManager.ExecuteWithTransactionAsync(
                async cancellationToken =>
                {
                    await subject.TryCreateAsync(item, _userId, cancellationToken).ConfigureAwait(false);
                },
                default).ConfigureAwait(false));
        _context.ChangeTracker.Clear();
        var itemExists = await _context.ContentHierarchyItems
            .AsNoTracking()
            .AnyAsync(candidate => candidate.Id == item.Id)
            .ConfigureAwait(false);
        var auditExists = await _context.AuditRecords.AsNoTracking().AnyAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception, Has.Message.EqualTo("Audit insert failed."));
            Assert.That(itemExists, Is.False);
            Assert.That(auditExists, Is.False);
        }

        _eventBus.Verify(
            bus => bus.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public void CreateAsync_Throws_WhenContentDefaultsAreNotResolvable()
    {
        // Arrange
        var item = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "missing-defaults",
            Level = "root",
            Slug = "missing-defaults",
            Name = "Missing defaults"
        };

        // Act
        var exception = Assert.ThrowsAsync<MagpieException>(
            async () => await _subject.CreateAsync(item, _userId).ConfigureAwait(false));

        // Assert
        Assert.That(
            exception,
            Has.Message.Contain("Defaults could not be determined for content type missing-defaults."));
    }

    [Test]
    public async Task CreateAsync_CreatesAuditRecord_WhenItemIsCreated()
    {
        // Arrange
        _context.RootFolders.Add(new RootFolder
        {
            Name = "Type A",
            Path = "/library/type-a",
            ContentType = "type-a",
            IsDefault = true,
            CreatedAt = DateTime.UtcNow
        });
        _context.QualityTierLists.Add(new QualityTierList
        {
            Name = "Default",
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var item = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "audited-movie",
            Name = "Audited Movie",
            Monitoring = true
        };

        // Act
        await _subject.CreateAsync(item, _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Create"));
            Assert.That(
                auditRecords,
                Has.One.With.Property(nameof(AuditRecord.EntityType))
                    .EqualTo("ContentHierarchyItem"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesContentHierarchyItem_WhenExists()
    {
        // Arrange
        var item = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "original-movie",
            Name = "Original Movie",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(item);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        item.Name = "Updated Movie";
        item.Monitoring = false;
        item.Path = "/movies/updated-movie";
        item.Settings["content.path.origin"] = "Generated";
        item.Settings["visible"] = "value";

        // Act
        var result = await _subject.UpdateAsync(item, _userId).ConfigureAwait(false);

        // Assert
        var updated = await _subject.GetByIdAsync(item.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(updated, Has.Property(nameof(updated.Name)).EqualTo("Updated Movie"));
            Assert.That(updated, Has.Property(nameof(updated.Monitoring)).EqualTo(false));
            Assert.That(updated, Has.Property(nameof(updated.Path)).EqualTo("/movies/updated-movie"));
            Assert.That(updated!.Settings["content.path.origin"], Is.EqualTo("Generated"));
            Assert.That(
                updated!.Settings,
                Is.EqualTo(new Dictionary<string, string>
                {
                    ["visible"] = "value",
                    ["content.path.origin"] = "Generated"
                }));
            Assert.That(updated, Has.Property(nameof(updated.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(updated, Has.Property(nameof(updated.ModifiedAt)).Not.Null);
        }
    }

    [Test]
    public async Task UpdateAsync_PersistsSuccessfulRefreshState_WhenPendingFlagCleared()
    {
        // Arrange
        var refreshedAt = new DateTime(2026, 7, 26, 3, 43, 11, DateTimeKind.Utc);
        var item = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "pending-refresh-show",
            Name = "Pending Refresh Show",
            PendingMetadataRefresh = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(item);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        var update = new ContentHierarchyItem
        {
            Id = item.Id,
            MetadataContainerId = item.MetadataContainerId,
            ContentType = item.ContentType,
            Level = item.Level,
            Slug = item.Slug,
            Name = "Resolved Show Title",
            PendingMetadataRefresh = false,
            DateLastRefreshed = refreshedAt,
            CreatedAt = item.CreatedAt
        };

        // Act
        var result = await _subject.UpdateAsync(update, _userId).ConfigureAwait(false);

        // Assert
        var updated = await _subject.GetByIdAsync(item.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(updated!.Name, Is.EqualTo("Resolved Show Title"));
            Assert.That(updated.PendingMetadataRefresh, Is.False);
            Assert.That(updated.DateLastRefreshed, Is.EqualTo(refreshedAt));
        }
    }

    [TestCase(null)]
    [TestCase(true)]
    [TestCase(false)]
    public async Task PatchAsync_SetsMonitoring_AndAudits(bool? monitoring)
    {
        // Arrange
        var item = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "monitor-movie",
            Name = "Monitor Movie",
            Monitoring = true,
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
                Monitoring = new() { Value = true }
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
            Monitoring = true,
            EffectiveMonitoring = true,
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
            EffectiveMonitoring = true,
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
                Monitoring = new() { Value = false }
            },
            _userId).ConfigureAwait(false);

        // Assert
        var stored = await _context.ContentHierarchyItems.AsNoTracking()
            .Where(item => item.Id == parent.Id || item.Id == child.Id)
            .ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(stored.Single(item => item.Id == parent.Id).EffectiveMonitoring, Is.False);
            Assert.That(stored.Single(item => item.Id == child.Id).Monitoring, Is.Null);
            Assert.That(stored.Single(item => item.Id == child.Id).EffectiveMonitoring, Is.False);
        }
    }

    [Test]
    public async Task RecomputeAllEffectiveValuesAsync_RepairsFullStaleTree_AndPublishesAfterCommit()
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
            DefaultMonitoring = true,
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
            EffectiveMonitoring = false,
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
            EffectiveMonitoring = false,
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
                    magpieEvent.Name == MagpieEvents.Names.ContentHierarchyItemUpdated),
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
            Assert.That(stored, Has.All.Property(nameof(ContentHierarchyItem.EffectiveMonitoring)).True);
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
                    magpieEvent.Name == MagpieEvents.Names.ContentHierarchyItemUpdated),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Test]
    public async Task PatchAsync_SetsAndClearsQualityTierList_AndAudits()
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
            Monitoring = true,
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
    public async Task UpdateAsync_ReturnsFalse_WhenNotExists()
    {
        // Arrange
        var item = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "nonexistent-movie",
            Name = "Nonexistent Movie",
            Monitoring = true
        };

        // Act
        var result = await _subject.UpdateAsync(item, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task UpdateAsync_CreatesAuditRecord_WhenItemIsUpdated()
    {
        // Arrange
        var item = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "to-update-movie",
            Name = "To Update Movie",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(item);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        item.Name = "Updated";

        // Act
        await _subject.UpdateAsync(item, _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Update"));
            Assert.That(
                auditRecords,
                Has.One.With.Property(nameof(AuditRecord.EntityType))
                    .EqualTo("ContentHierarchyItem"));
        }
    }

    [Test]
    public async Task DeleteAsync_DeletesContentHierarchyItem_WhenExists()
    {
        // Arrange
        var item = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "to-delete-movie",
            Name = "To Delete Movie",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(item);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.DeleteAsync(item.Id, _userId).ConfigureAwait(false);

        // Assert
        var deleted = await _subject.GetByIdAsync(item.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(deleted, Is.Null);
        }
    }

    [Test]
    public async Task DeleteAsync_ReturnsFalse_WhenNotExists()
    {
        // Act
        var result = await _subject.DeleteAsync(Guid.NewGuid(), _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task DeleteAsync_CreatesAuditRecord_WhenItemIsDeleted()
    {
        // Arrange
        var item = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "delete-audit-movie",
            Name = "Delete Audit Movie",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(item);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject.DeleteAsync(item.Id, _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Delete"));
            Assert.That(
                auditRecords,
                Has.One.With.Property(nameof(AuditRecord.EntityType))
                    .EqualTo("ContentHierarchyItem"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task DeleteAsync_RemovesSubtreeAndContentItems_WhenItemHasChildrenAndContentItems()
    {
        // Arrange
        var childContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(childContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var parent = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-f",
            Level = "level-b",
            Slug = "to-delete-series",
            Name = "To Delete Series",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(parent);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var child = new ContentHierarchyItem
        {
            MetadataContainerId = childContainer.Id,
            ParentId = parent.Id,
            ContentType = "type-f",
            Level = "level-c",
            Slug = "to-delete-season",
            Name = "To Delete Season",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(child);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var parentContentItem = new ContentItem
        {
            ContentHierarchyItemId = parent.Id,
            Fulfillment = FulfillmentStatus.Missing,
            CreatedAt = DateTime.UtcNow
        };
        var childContentItem = new ContentItem
        {
            ContentHierarchyItemId = child.Id,
            Fulfillment = FulfillmentStatus.Missing,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentItems.AddRange(parentContentItem, childContentItem);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.DeleteAsync(parent.Id, _userId).ConfigureAwait(false);

        // Assert
        var remainingHierarchyItems = await _context.ContentHierarchyItems.CountAsync().ConfigureAwait(false);
        var remainingContentItems = await _context.ContentItems.CountAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(remainingHierarchyItems, Is.Zero);
            Assert.That(remainingContentItems, Is.Zero);
        }
    }

    [Test]
    public async Task DeleteAsync_RemovesReleaseScope_WhenSubtreeHasCoverage()
    {
        // Arrange
        var childContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(childContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var parent = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-f",
            Level = "level-b",
            Slug = "coverage-delete-series",
            Name = "Coverage Delete Series",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(parent);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var child = new ContentHierarchyItem
        {
            MetadataContainerId = childContainer.Id,
            ParentId = parent.Id,
            ContentType = "type-f",
            Level = "level-c",
            Slug = "coverage-delete-season",
            Name = "Coverage Delete Season",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(child);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var release = new Release
        {
            Protocol = "torrent",
            Title = "Coverage Delete Pack",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _context.Releases.Add(release);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        _context.ReleaseScopes.Add(new ReleaseScope { ReleaseId = release.Id, ContentHierarchyItemId = child.Id, });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.DeleteAsync(parent.Id, _userId).ConfigureAwait(false);

        // Assert
        var remainingHierarchyItems = await _context.ContentHierarchyItems.CountAsync().ConfigureAwait(false);
        var remainingScopeRows = await _context.ReleaseScopes.CountAsync().ConfigureAwait(false);
        var releaseStillExists = await _context.Releases.AnyAsync(r => r.Id == release.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(remainingHierarchyItems, Is.Zero);
            Assert.That(remainingScopeRows, Is.Zero);
            Assert.That(releaseStillExists, Is.True);
        }
    }

    [Test]
    public async Task DeleteAsync_RemovesMetadataGraph_LeavingSiblingMetadataIntact()
    {
        // Arrange
        var deleteContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var siblingContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.AddRange(deleteContainer, siblingContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var toDelete = new ContentHierarchyItem
        {
            MetadataContainerId = deleteContainer.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "graph-delete",
            Name = "Graph Delete",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        var sibling = new ContentHierarchyItem
        {
            MetadataContainerId = siblingContainer.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "graph-keep",
            Name = "Graph Keep",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(toDelete, sibling);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var deleteContribution = new MetadataContribution
        {
            MetadataContainerId = deleteContainer.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Graph Delete"
        };
        var siblingContribution = new MetadataContribution
        {
            MetadataContainerId = siblingContainer.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Graph Keep"
        };
        _context.MetadataContributions.AddRange(deleteContribution, siblingContribution);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        _context.Genres.AddRange(
            new Genre { MetadataContributionId = deleteContribution.Id, Name = "Drama", Ordinal = 0 },
            new Genre { MetadataContributionId = siblingContribution.Id, Name = "Comedy", Ordinal = 0 });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        // Act
        var result = await _subject.DeleteAsync(toDelete.Id, _userId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(
                await _context.MetadataContainers.AsNoTracking().AnyAsync(c => c.Id == deleteContainer.Id)
                    .ConfigureAwait(false),
                Is.False);
            Assert.That(
                await _context.MetadataContributions.AsNoTracking()
                    .AnyAsync(c => c.MetadataContainerId == deleteContainer.Id).ConfigureAwait(false),
                Is.False);
            Assert.That(
                await _context.Genres.AsNoTracking()
                    .AnyAsync(g => g.MetadataContributionId == deleteContribution.Id).ConfigureAwait(false),
                Is.False);

            Assert.That(
                await _context.MetadataContainers.AsNoTracking().AnyAsync(c => c.Id == siblingContainer.Id)
                    .ConfigureAwait(false),
                Is.True);
            Assert.That(
                await _context.Genres.AsNoTracking()
                    .AnyAsync(g => g.MetadataContributionId == siblingContribution.Id).ConfigureAwait(false),
                Is.True);
        }
    }

    [Test]
    public async Task BulkDeleteAsync_RemovesMetadataGraphForSubtree_LeavingSiblingMetadataIntact()
    {
        // Arrange
        var showContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var seasonContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var siblingContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.AddRange(showContainer, seasonContainer, siblingContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var show = new ContentHierarchyItem
        {
            MetadataContainerId = showContainer.Id,
            ContentType = "type-c",
            Level = "level-b",
            Slug = "bulk-show",
            Name = "Bulk Show",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(show);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var season = new ContentHierarchyItem
        {
            MetadataContainerId = seasonContainer.Id,
            ParentId = show.Id,
            ContentType = "type-c",
            Level = "level-c",
            Slug = "bulk-season",
            Name = "Bulk Season",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        var sibling = new ContentHierarchyItem
        {
            MetadataContainerId = siblingContainer.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "bulk-keep",
            Name = "Bulk Keep",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(season, sibling);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        _context.MetadataContributions.AddRange(
            new MetadataContribution { MetadataContainerId = showContainer.Id, Ordinal = 0, ProviderType = "test" },
            new MetadataContribution { MetadataContainerId = seasonContainer.Id, Ordinal = 0, ProviderType = "test" },
            new MetadataContribution { MetadataContainerId = siblingContainer.Id, Ordinal = 0, ProviderType = "test" });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        // Act
        var result = await _subject.BulkDeleteAsync(new() { Ids = [show.Id] }, _userId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(
                await _context.MetadataContainers.AsNoTracking().AnyAsync(c => c.Id == showContainer.Id)
                    .ConfigureAwait(false),
                Is.False);
            Assert.That(
                await _context.MetadataContainers.AsNoTracking().AnyAsync(c => c.Id == seasonContainer.Id)
                    .ConfigureAwait(false),
                Is.False);
            Assert.That(
                await _context.MetadataContributions.AsNoTracking()
                    .AnyAsync(c => c.MetadataContainerId == showContainer.Id ||
                        c.MetadataContainerId == seasonContainer.Id).ConfigureAwait(false),
                Is.False);
            Assert.That(
                await _context.MetadataContainers.AsNoTracking().AnyAsync(c => c.Id == siblingContainer.Id)
                    .ConfigureAwait(false),
                Is.True);
            Assert.That(
                await _context.MetadataContributions.AsNoTracking()
                    .AnyAsync(c => c.MetadataContainerId == siblingContainer.Id).ConfigureAwait(false),
                Is.True);
        }
    }

    [Test]
    public async Task DeleteAsync_RemovesMediaFiles_WhenContentItemHasFiles()
    {
        // Arrange
        var item = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-with-files",
            Name = "Movie With Files",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(item);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var contentItem = new ContentItem
        {
            ContentHierarchyItemId = item.Id,
            Fulfillment = FulfillmentStatus.Fulfilled,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentItems.Add(contentItem);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        _context.MediaFiles.AddRange(
            new MediaFile
            {
                ContentItemId = contentItem.Id,
                FileKind = "Video",
                FilePath = "/movies/movie-with-files/movie.mkv",
                FileSize = 1024,
                CreatedAt = DateTime.UtcNow
            },
            new MediaFile
            {
                ContentItemId = contentItem.Id,
                FileKind = "Subtitle",
                FilePath = "/movies/movie-with-files/movie.en.srt",
                FileSize = 16,
                CreatedAt = DateTime.UtcNow
            });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.DeleteAsync(item.Id, _userId).ConfigureAwait(false);

        // Assert
        var remainingHierarchyItems = await _context.ContentHierarchyItems.CountAsync().ConfigureAwait(false);
        var remainingContentItems = await _context.ContentItems.CountAsync().ConfigureAwait(false);
        var remainingMediaFiles = await _context.MediaFiles.CountAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(remainingHierarchyItems, Is.Zero);
            Assert.That(remainingContentItems, Is.Zero);
            Assert.That(remainingMediaFiles, Is.Zero);
        }
    }

    [Test]
    public async Task DeleteAsync_RemovesSubtreeMediaFiles_WhenChildContentItemHasFiles()
    {
        // Arrange
        var childContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(childContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var parent = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-f",
            Level = "level-b",
            Slug = "series-with-file-child",
            Name = "Series With File Child",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(parent);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var child = new ContentHierarchyItem
        {
            MetadataContainerId = childContainer.Id,
            ParentId = parent.Id,
            ContentType = "type-f",
            Level = "level-c",
            Slug = "season-with-files",
            Name = "Season With Files",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(child);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var childContentItem = new ContentItem
        {
            ContentHierarchyItemId = child.Id,
            Fulfillment = FulfillmentStatus.Fulfilled,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentItems.Add(childContentItem);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        _context.MediaFiles.Add(new MediaFile
        {
            ContentItemId = childContentItem.Id,
            FileKind = "Video",
            FilePath = "/episodes/season-with-files/s01e01.mkv",
            FileSize = 2048,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.DeleteAsync(parent.Id, _userId).ConfigureAwait(false);

        // Assert
        var remainingHierarchyItems = await _context.ContentHierarchyItems.CountAsync().ConfigureAwait(false);
        var remainingContentItems = await _context.ContentItems.CountAsync().ConfigureAwait(false);
        var remainingMediaFiles = await _context.MediaFiles.CountAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(remainingHierarchyItems, Is.Zero);
            Assert.That(remainingContentItems, Is.Zero);
            Assert.That(remainingMediaFiles, Is.Zero);
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsFilteredItems_ByContentType()
    {
        // Arrange
        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var item1 = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-1",
            Name = "Movie 1",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        var item2 = new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-c",
            Level = "level-b",
            Slug = "series-1",
            Name = "Series 1",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(item1, item2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            ContentType = new()
            {
                And = [new() { Value = "type-a" }]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Exactly(1).Items);
            Assert.That(items, Has.All.Matches<ContentHierarchyItem>(i => i.ContentType == "type-a"));
        }
    }

    [Test]
    public async Task QueryAsync_RootLevelOnly_ExcludesChildrenAndIncludesRoots()
    {
        // Arrange
        var seasonContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var movieContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.AddRange(seasonContainer, movieContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var series = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-f",
            Level = "level-b",
            Slug = "root-series",
            Name = "Root Series",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(series);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var season = new ContentHierarchyItem
        {
            MetadataContainerId = seasonContainer.Id,
            ParentId = series.Id,
            ContentType = "type-f",
            Level = "level-c",
            Slug = "child-season",
            Name = "Child Season",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        var movie = new ContentHierarchyItem
        {
            MetadataContainerId = movieContainer.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "root-movie",
            Name = "Root Movie",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(season, movie);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria { RootLevelOnly = true };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Exactly(2).Items);
            Assert.That(items, Has.All.Matches<ContentHierarchyItem>(i => i.ParentId == null));
            Assert.That(items.Select(i => i.Slug), Is.EquivalentTo(["root-series", "root-movie"]));
            Assert.That(result.TotalItems, Is.EqualTo(2));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsDescendants_WhenAncestorIdMatches()
    {
        // Arrange
        var rootContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var childContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var grandchildContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var unrelatedContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.AddRange(rootContainer, childContainer, grandchildContainer, unrelatedContainer);
        var root = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainer = rootContainer,
            ContentType = "type-a",
            Level = "root",
            Slug = "ancestor-root",
            Name = "Root",
            CreatedAt = DateTime.UtcNow
        };
        var child = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainer = childContainer,
            ParentId = root.Id,
            ContentType = "type-a",
            Level = "child",
            Slug = "ancestor-child",
            Name = "Child",
            CreatedAt = DateTime.UtcNow
        };
        var grandchild = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainer = grandchildContainer,
            ParentId = child.Id,
            ContentType = "type-a",
            Level = "leaf",
            Slug = "ancestor-grandchild",
            Name = "Grandchild",
            CreatedAt = DateTime.UtcNow
        };
        var unrelated = new ContentHierarchyItem
        {
            MetadataContainer = unrelatedContainer,
            ContentType = "type-a",
            Level = "root",
            Slug = "unrelated-root",
            Name = "Unrelated",
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(root, child, grandchild, unrelated);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new() { AncestorId = root.Id },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        Assert.That(result.Items.Select(item => item.Id), Is.EquivalentTo([child.Id, grandchild.Id]));
    }

    [Test]
    public async Task QueryAsync_FiltersByMetadataExternalIdPresence_WhenPresenceFilterIsSet()
    {
        // Arrange
        var withExternalIdContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var withoutExternalIdContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var contribution = new MetadataContribution
        {
            MetadataContainer = withExternalIdContainer,
            Ordinal = 0,
            ProviderType = "test",
            Title = "With id"
        };
        contribution.ExternalIds.Add(new ExternalId { Source = "test", Value = "external-1" });
        _context.MetadataContainers.AddRange(withExternalIdContainer, withoutExternalIdContainer);
        _context.MetadataContributions.Add(contribution);
        var withExternalId = new ContentHierarchyItem
        {
            MetadataContainer = withExternalIdContainer,
            ContentType = "type-a",
            Level = "root",
            Slug = "with-external-id",
            Name = "With id",
            CreatedAt = DateTime.UtcNow
        };
        var withoutExternalId = new ContentHierarchyItem
        {
            MetadataContainer = withoutExternalIdContainer,
            ContentType = "type-a",
            Level = "root",
            Slug = "without-external-id",
            Name = "Without id",
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(withExternalId, withoutExternalId);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new() { HasMetadataExternalIds = true },
            new() { PageNumber = 1, PageSize = 100 },
            null)
            .ConfigureAwait(false);

        // Assert
        Assert.That(result.Items.Select(item => item.Id), Is.EqualTo([withExternalId.Id]));
    }

    [Test]
    public async Task QueryAsync_FiltersBySearchableTitle_WhenTitleSearchIsSet()
    {
        // Arrange
        var pendingContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var titledContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var refreshedContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.AddRange(pendingContainer, titledContainer, refreshedContainer);
        _context.MetadataContributions.Add(new MetadataContribution
        {
            MetadataContainer = titledContainer,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Searchable"
        });
        var pending = new ContentHierarchyItem
        {
            MetadataContainer = pendingContainer,
            ContentType = "type-a",
            Level = "root",
            Slug = "pending-title",
            Name = "Pending",
            PendingMetadataRefresh = true,
            CreatedAt = DateTime.UtcNow
        };
        var titled = new ContentHierarchyItem
        {
            MetadataContainer = titledContainer,
            ContentType = "type-a",
            Level = "root",
            Slug = "metadata-title",
            Name = "Pending",
            PendingMetadataRefresh = true,
            CreatedAt = DateTime.UtcNow
        };
        var refreshed = new ContentHierarchyItem
        {
            MetadataContainer = refreshedContainer,
            ContentType = "type-a",
            Level = "root",
            Slug = "refreshed-title",
            Name = "Refreshed",
            PendingMetadataRefresh = false,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(pending, titled, refreshed);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new() { HasSearchableTitle = true },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        Assert.That(result.Items.Select(item => item.Id), Is.EquivalentTo([titled.Id, refreshed.Id]));
    }

    [Test]
    public async Task BulkPatchAsync_SetsMonitoringOnMatching_ExceptExcluded()
    {
        // Arrange
        var seededItems = new Dictionary<string, ContentHierarchyItem>();
        foreach (var slug in new[] { "movie-a", "movie-b", "movie-c" })
        {
            var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
            _context.MetadataContainers.Add(container);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            var item = new ContentHierarchyItem
            {
                MetadataContainerId = container.Id,
                ContentType = "type-a",
                Level = "level-a",
                Slug = slug,
                Name = slug,
                Monitoring = true,
                CreatedAt = DateTime.UtcNow
            };
            _context.ContentHierarchyItems.Add(item);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            seededItems.Add(slug, item);
        }

        var a = seededItems["movie-a"];
        var b = seededItems["movie-b"];
        var c = seededItems["movie-c"];

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
                Patch = new() { Monitoring = new() { Value = false } }
            },
            _userId).ConfigureAwait(false);

        // Assert
        var refreshed = await _context.ContentHierarchyItems.AsNoTracking().ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result.Error, Is.Null);
            Assert.That(refreshed.Single(i => i.Id == a.Id).Monitoring, Is.EqualTo(false));
            Assert.That(refreshed.Single(i => i.Id == b.Id).Monitoring, Is.EqualTo(false));
            Assert.That(refreshed.Single(i => i.Id == c.Id).Monitoring, Is.EqualTo(true));
        }
    }

    [Test]
    public async Task BulkPatchAsync_SetsAndClearsRootFolder_OnIds()
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
        var seededItems = new Dictionary<string, ContentHierarchyItem>();
        foreach (var slug in new[] { "movie-a", "movie-b" })
        {
            var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
            _context.MetadataContainers.Add(container);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            var item = new ContentHierarchyItem
            {
                MetadataContainerId = container.Id,
                ContentType = "type-a",
                Level = "level-a",
                Slug = slug,
                Name = slug,
                Monitoring = true,
                CreatedAt = DateTime.UtcNow
            };
            _context.ContentHierarchyItems.Add(item);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            seededItems.Add(slug, item);
        }

        var a = seededItems["movie-a"];
        var b = seededItems["movie-b"];

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
            Monitoring = true,
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
    public async Task BulkPatchAsync_SetsRootFolderOnMatching_ExceptExcluded()
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
        var seededItems = new Dictionary<string, ContentHierarchyItem>();
        foreach (var slug in new[] { "movie-a", "movie-b", "movie-c" })
        {
            var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
            _context.MetadataContainers.Add(container);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            var item = new ContentHierarchyItem
            {
                MetadataContainerId = container.Id,
                ContentType = "type-a",
                Level = "level-a",
                Slug = slug,
                Name = slug,
                Monitoring = true,
                CreatedAt = DateTime.UtcNow
            };
            _context.ContentHierarchyItems.Add(item);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            seededItems.Add(slug, item);
        }

        var a = seededItems["movie-a"];
        var b = seededItems["movie-b"];
        var c = seededItems["movie-c"];

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
    public async Task BulkPatchAsync_SetsAndClearsQualityTierList_OnIds()
    {
        // Arrange
        var tierList = new QualityTierList { Id = Guid.NewGuid(), Name = "HD Tiers", CreatedAt = DateTime.UtcNow };
        _context.QualityTierLists.Add(tierList);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var seededItems = new Dictionary<string, ContentHierarchyItem>();
        foreach (var slug in new[] { "movie-a", "movie-b" })
        {
            var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
            _context.MetadataContainers.Add(container);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            var item = new ContentHierarchyItem
            {
                MetadataContainerId = container.Id,
                ContentType = "type-a",
                Level = "level-a",
                Slug = slug,
                Name = slug,
                Monitoring = true,
                CreatedAt = DateTime.UtcNow
            };
            _context.ContentHierarchyItems.Add(item);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            seededItems.Add(slug, item);
        }

        var a = seededItems["movie-a"];
        var b = seededItems["movie-b"];

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
    public async Task BulkPatchAsync_SetsQualityTierListOnMatching_ExceptExcluded()
    {
        // Arrange
        var tierList = new QualityTierList { Id = Guid.NewGuid(), Name = "HD Tiers", CreatedAt = DateTime.UtcNow };
        _context.QualityTierLists.Add(tierList);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var seededItems = new Dictionary<string, ContentHierarchyItem>();
        foreach (var slug in new[] { "movie-a", "movie-b", "movie-c" })
        {
            var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
            _context.MetadataContainers.Add(container);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            var item = new ContentHierarchyItem
            {
                MetadataContainerId = container.Id,
                ContentType = "type-a",
                Level = "level-a",
                Slug = slug,
                Name = slug,
                Monitoring = true,
                CreatedAt = DateTime.UtcNow
            };
            _context.ContentHierarchyItems.Add(item);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            seededItems.Add(slug, item);
        }

        var a = seededItems["movie-a"];
        var b = seededItems["movie-b"];
        var c = seededItems["movie-c"];

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
    public async Task BulkDeleteAsync_DeletesMatching_ExceptExcluded()
    {
        // Arrange
        var seededItems = new Dictionary<string, ContentHierarchyItem>();
        foreach (var slug in new[] { "movie-a", "movie-b", "movie-c" })
        {
            var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
            _context.MetadataContainers.Add(container);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            var item = new ContentHierarchyItem
            {
                MetadataContainerId = container.Id,
                ContentType = "type-a",
                Level = "level-a",
                Slug = slug,
                Name = slug,
                Monitoring = true,
                CreatedAt = DateTime.UtcNow
            };
            _context.ContentHierarchyItems.Add(item);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            seededItems.Add(slug, item);
        }

        var a = seededItems["movie-a"];
        var b = seededItems["movie-b"];
        var c = seededItems["movie-c"];

        var criteria = new ContentHierarchyItemCriteria
        {
            ContentType = new() { And = [new() { Value = "type-a" }] },
            ExcludeIds = [c.Id]
        };

        // Act
        var result = await _subject.BulkDeleteAsync(new() { Filter = criteria }, _userId).ConfigureAwait(false);

        // Assert
        var remaining = await _context.ContentHierarchyItems.AsNoTracking().Select(i => i.Id).ToListAsync()
            .ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(remaining, Does.Not.Contain(a.Id));
            Assert.That(remaining, Does.Not.Contain(b.Id));
            Assert.That(remaining, Does.Contain(c.Id));
        }
    }

    [Test]
    public async Task BulkPatchAsync_TagsMatching_ExceptExcluded()
    {
        // Arrange
        var seededItems = new Dictionary<string, ContentHierarchyItem>();
        foreach (var slug in new[] { "movie-a", "movie-b", "movie-c" })
        {
            var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
            _context.MetadataContainers.Add(container);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            var item = new ContentHierarchyItem
            {
                MetadataContainerId = container.Id,
                ContentType = "type-a",
                Level = "level-a",
                Slug = slug,
                Name = slug,
                Monitoring = true,
                CreatedAt = DateTime.UtcNow
            };
            _context.ContentHierarchyItems.Add(item);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            seededItems.Add(slug, item);
        }

        var a = seededItems["movie-a"];
        var b = seededItems["movie-b"];
        var c = seededItems["movie-c"];

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
    public async Task QueryAsync_OrdersChildrenBySlugAscending_NotByName()
    {
        // Arrange
        var parentContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(parentContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var series = new ContentHierarchyItem
        {
            MetadataContainerId = parentContainer.Id,
            ContentType = "type-f",
            Level = "level-b",
            Slug = "ordering-series",
            Name = "Ordering Series",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(series);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var episodes = new[]
        {
            ("s01e02", "Cancer Man"),
            ("s01e03", "Cat's in the Bag"),
            ("s01e01", "And the Bag's in the River"),
        };
        foreach (var (slug, name) in episodes)
        {
            var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
            _context.MetadataContainers.Add(container);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            _context.ContentHierarchyItems.Add(new ContentHierarchyItem
            {
                MetadataContainerId = container.Id,
                ParentId = series.Id,
                ContentType = "type-f",
                Level = "level-d",
                Slug = slug,
                Name = name,
                Monitoring = true,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            ParentId = new() { And = [new() { Value = series.Id }] }
        };
        var sort = new SortParameter
        {
            PropertyName = nameof(ContentHierarchyItem.Slug),
            Direction = SortDirection.Ascending
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            sort).ConfigureAwait(false);

        // Assert
        var slugs = result.Items.Select(i => i.Slug).ToList();
        Assert.That(slugs, Is.EqualTo(["s01e01", "s01e02", "s01e03"]));
    }

    [Test]
    public async Task QueryAsync_FiltersByMetadataTitle_WhenNameIsPlaceholder()
    {
        // Arrange
        var seededItems = new Dictionary<string, ContentHierarchyItem>();
        foreach (var seed in new[]
        {
            (Slug: "movie-jump", Title: "22 Jump Street"),
            (Slug: "movie-fast", Title: "2 Fast 2 Furious")
        })
        {
            var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
            _context.MetadataContainers.Add(container);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            _context.MetadataContributions.Add(new MetadataContribution
            {
                MetadataContainerId = container.Id,
                Ordinal = 0,
                ProviderType = "test",
                Title = seed.Title
            });
            var item = new ContentHierarchyItem
            {
                MetadataContainerId = container.Id,
                ContentType = "type-a",
                Level = "level-a",
                Slug = seed.Slug,
                Name = "Pending refresh",
                Monitoring = true,
                CreatedAt = DateTime.UtcNow
            };
            _context.ContentHierarchyItems.Add(item);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            seededItems.Add(seed.Slug, item);
        }

        var match = seededItems["movie-jump"];

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new()
                    {
                        Title = new()
                        {
                            And = [new() { Operator = CriteriaValueOperator.ContainsString, Value = "Jump" }]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Exactly(1).Items);
            Assert.That(items[0].Id, Is.EqualTo(match.Id));
        }
    }

    [Test]
    public async Task QueryAsync_SearchTitle_MatchesMetadataTitle()
    {
        // Arrange
        var seededItems = new Dictionary<string, ContentHierarchyItem>();
        foreach (var seed in new[]
        {
            (Slug: "movie-cure", Title: "A Cure for Wellness"),
            (Slug: "movie-jump", Title: "22 Jump Street")
        })
        {
            var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
            _context.MetadataContainers.Add(container);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            _context.MetadataContributions.Add(new MetadataContribution
            {
                MetadataContainerId = container.Id,
                Ordinal = 0,
                ProviderType = "test",
                Title = seed.Title
            });
            var item = new ContentHierarchyItem
            {
                MetadataContainerId = container.Id,
                ContentType = "type-a",
                Level = "level-a",
                Slug = seed.Slug,
                Name = "Pending refresh",
                Monitoring = true,
                CreatedAt = DateTime.UtcNow
            };
            _context.ContentHierarchyItems.Add(item);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            seededItems.Add(seed.Slug, item);
        }

        var match = seededItems["movie-cure"];

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new()
                    {
                        Title = new()
                        {
                            And = [new() { Operator = CriteriaValueOperator.ContainsString, Value = "Cure" }]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Exactly(1).Items);
            Assert.That(items[0].Id, Is.EqualTo(match.Id));
        }
    }

    [Test]
    public async Task QueryAsync_SearchTitle_MatchesMetadataTitleCaseInsensitively()
    {
        // Arrange
        var seededItems = new Dictionary<string, ContentHierarchyItem>();
        foreach (var seed in new[]
        {
            (Slug: "movie-marriage", Title: "The Secret To A Happy Marriage"),
            (Slug: "movie-jump", Title: "22 Jump Street")
        })
        {
            var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
            _context.MetadataContainers.Add(container);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            _context.MetadataContributions.Add(new MetadataContribution
            {
                MetadataContainerId = container.Id,
                Ordinal = 0,
                ProviderType = "test",
                Title = seed.Title
            });
            var item = new ContentHierarchyItem
            {
                MetadataContainerId = container.Id,
                ContentType = "type-a",
                Level = "level-a",
                Slug = seed.Slug,
                Name = "Pending refresh",
                Monitoring = true,
                CreatedAt = DateTime.UtcNow
            };
            _context.ContentHierarchyItems.Add(item);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            seededItems.Add(seed.Slug, item);
        }

        var match = seededItems["movie-marriage"];

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new()
                    {
                        Title = new()
                        {
                            And = [new() { Operator = CriteriaValueOperator.ContainsString, Value = "marriage" }]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Exactly(1).Items);
            Assert.That(items[0].Id, Is.EqualTo(match.Id));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByMetadataPublishDate_WindowsToInRangeItems()
    {
        // Arrange
        var start = new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2024, 3, 31, 23, 59, 59, DateTimeKind.Utc);

        var seededItems = new Dictionary<string, ContentHierarchyItem>();
        foreach (var seed in new[]
        {
            (Slug: "movie-before", Date: new DateTime(2024, 2, 15, 0, 0, 0, DateTimeKind.Utc)),
            (Slug: "movie-mid", Date: new DateTime(2024, 3, 10, 0, 0, 0, DateTimeKind.Utc)),
            (Slug: "movie-late", Date: new DateTime(2024, 3, 28, 0, 0, 0, DateTimeKind.Utc)),
            (Slug: "movie-after", Date: new DateTime(2024, 4, 5, 0, 0, 0, DateTimeKind.Utc))
        })
        {
            var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
            _context.MetadataContainers.Add(container);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            _context.MetadataContributions.Add(new MetadataContribution
            {
                MetadataContainerId = container.Id,
                Ordinal = 0,
                ProviderType = "test",
                Title = seed.Slug,
                PublishDate = seed.Date
            });
            var item = new ContentHierarchyItem
            {
                MetadataContainerId = container.Id,
                ContentType = "type-a",
                Level = "level-a",
                Slug = seed.Slug,
                Name = "Pending refresh",
                Monitoring = true,
                CreatedAt = DateTime.UtcNow
            };
            _context.ContentHierarchyItems.Add(item);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            seededItems.Add(seed.Slug, item);
        }

        var before = seededItems["movie-before"];
        var inWindow1 = seededItems["movie-mid"];
        var inWindow2 = seededItems["movie-late"];
        var after = seededItems["movie-after"];

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new()
                    {
                        PublishDate = new()
                        {
                            And =
                            [
                                new()
                                {
                                    Operator = CriteriaValueOperator.GreaterThanOrEqual,
                                    Value = new TemporalValue { Value = start, Precision = TemporalPrecision.Instant }
                                },
                                new()
                                {
                                    Operator = CriteriaValueOperator.LessThanOrEqual,
                                    Value = new TemporalValue { Value = end, Precision = TemporalPrecision.Instant }
                                }
                            ]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var ids = result.Items.Select(i => i.Id).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Has.Count.EqualTo(2));
            Assert.That(ids, Does.Contain(inWindow1.Id));
            Assert.That(ids, Does.Contain(inWindow2.Id));
            Assert.That(ids, Does.Not.Contain(before.Id));
            Assert.That(ids, Does.Not.Contain(after.Id));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByMetadataEstablished_RangeExcludesOutOfRange()
    {
        // Arrange
        var seededItems = new Dictionary<string, ContentHierarchyItem>();
        foreach (var seed in new[]
        {
            (Slug: "studio-early", Date: new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            (Slug: "studio-mid", Date: new DateTime(2005, 6, 1, 0, 0, 0, DateTimeKind.Utc)),
            (Slug: "studio-late", Date: new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc))
        })
        {
            var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
            _context.MetadataContainers.Add(container);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            _context.MetadataContributions.Add(new MetadataContribution
            {
                MetadataContainerId = container.Id,
                Ordinal = 0,
                ProviderType = "test",
                Title = seed.Slug,
                Established = seed.Date
            });
            var item = new ContentHierarchyItem
            {
                MetadataContainerId = container.Id,
                ContentType = "type-a",
                Level = "level-a",
                Slug = seed.Slug,
                Name = "Pending refresh",
                Monitoring = true,
                CreatedAt = DateTime.UtcNow
            };
            _context.ContentHierarchyItems.Add(item);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            seededItems.Add(seed.Slug, item);
        }

        var early = seededItems["studio-early"];
        var mid = seededItems["studio-mid"];
        var late = seededItems["studio-late"];

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new()
                    {
                        Established = new()
                        {
                            And =
                            [
                                new()
                                {
                                    Operator = CriteriaValueOperator.GreaterThanOrEqual,
                                    Value = new TemporalValue
                                    {
                                        Value = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                                        Precision = TemporalPrecision.Instant
                                    }
                                },
                                new()
                                {
                                    Operator = CriteriaValueOperator.LessThanOrEqual,
                                    Value = new TemporalValue
                                    {
                                        Value = new DateTime(2010, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                                        Precision = TemporalPrecision.Instant
                                    }
                                }
                            ]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var ids = result.Items.Select(i => i.Id).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Has.Count.EqualTo(1));
            Assert.That(ids, Does.Contain(mid.Id));
            Assert.That(ids, Does.Not.Contain(early.Id));
            Assert.That(ids, Does.Not.Contain(late.Id));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByMetadataPublishDate_TemporalPrecisionSelectsMatchingPeriod()
    {
        // Arrange
        var seededItems = new Dictionary<string, ContentHierarchyItem>();
        foreach (var seed in new[]
        {
            (Slug: "late-2019", Date: new DateTime(2019, 12, 31, 23, 0, 0, DateTimeKind.Utc)),
            (Slug: "jan-2020", Date: new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            (Slug: "may-2020", Date: new DateTime(2020, 5, 17, 10, 0, 0, DateTimeKind.Utc)),
            (Slug: "dec-2020", Date: new DateTime(2020, 12, 31, 23, 59, 59, DateTimeKind.Utc)),
            (Slug: "jan-2021", Date: new DateTime(2021, 1, 1, 0, 0, 30, DateTimeKind.Utc))
        })
        {
            var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
            _context.MetadataContainers.Add(container);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            _context.MetadataContributions.Add(new MetadataContribution
            {
                MetadataContainerId = container.Id,
                Ordinal = 0,
                ProviderType = "test",
                Title = seed.Slug,
                PublishDate = seed.Date
            });
            var item = new ContentHierarchyItem
            {
                MetadataContainerId = container.Id,
                ContentType = "type-a",
                Level = "level-a",
                Slug = seed.Slug,
                Name = "Item",
                Monitoring = true,
                CreatedAt = DateTime.UtcNow
            };
            _context.ContentHierarchyItems.Add(item);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            seededItems.Add(seed.Slug, item);
        }

        async Task<List<string>> MatchingSlugs(TemporalValue requested)
        {
            var criteria = new ContentHierarchyItemCriteria
            {
                Metadata = new()
                {
                    And =
                    [
                        new()
                        {
                            PublishDate = new()
                            {
                                And = [new() { Operator = CriteriaValueOperator.Equals, Value = requested }]
                            }
                        }
                    ]
                }
            };
            var result = await _subject.QueryAsync(criteria,
                new() { PageNumber = 1, PageSize = 100 },
                null).ConfigureAwait(false);
            return [.. result.Items.Select(i => i.Slug).OrderBy(s => s)];
        }

        // Act
        var yearMatches = await MatchingSlugs(new TemporalValue
        {
            Value = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Precision = TemporalPrecision.Year
        }).ConfigureAwait(false);
        var monthMatches = await MatchingSlugs(new TemporalValue
        {
            Value = new DateTime(2020, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            Precision = TemporalPrecision.Month
        }).ConfigureAwait(false);
        var dayMatches = await MatchingSlugs(new TemporalValue
        {
            Value = new DateTime(2020, 5, 17, 0, 0, 0, DateTimeKind.Utc),
            Precision = TemporalPrecision.Day
        }).ConfigureAwait(false);
        var instantMatches = await MatchingSlugs(new TemporalValue
        {
            Value = new DateTime(2020, 5, 17, 10, 0, 0, DateTimeKind.Utc),
            Precision = TemporalPrecision.Instant
        }).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(yearMatches, Is.EqualTo(["dec-2020", "jan-2020", "may-2020"]));
            Assert.That(monthMatches, Is.EqualTo(["may-2020"]));
            Assert.That(dayMatches, Is.EqualTo(["may-2020"]));
            Assert.That(instantMatches, Is.EqualTo(["may-2020"]));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByGenre_ReturnsOnlyMatchingItems()
    {
        // Arrange
        var seededItems = new Dictionary<string, ContentHierarchyItem>();
        foreach (var seed in new[]
        {
            (Slug: "movie-action", Genres: new[] { "Action", "Thriller" }),
            (Slug: "movie-comedy", Genres: ["Comedy"])
        })
        {
            var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
            _context.MetadataContainers.Add(container);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            var contribution = new MetadataContribution
            {
                MetadataContainerId = container.Id,
                Ordinal = 0,
                ProviderType = "test",
                Title = seed.Slug
            };
            _context.MetadataContributions.Add(contribution);
            _context.ContentHierarchyItems.Add(new ContentHierarchyItem
            {
                MetadataContainerId = container.Id,
                ContentType = "type-a",
                Level = "level-a",
                Slug = seed.Slug,
                Name = "Pending refresh",
                Monitoring = true,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync().ConfigureAwait(false);
            for (var ordinal = 0; ordinal < seed.Genres.Length; ordinal++)
            {
                _context.Genres.Add(new Genre
                {
                    MetadataContributionId = contribution.Id,
                    Ordinal = ordinal,
                    Name = seed.Genres[ordinal]
                });
            }

            await _context.SaveChangesAsync().ConfigureAwait(false);
            var item = await _context.ContentHierarchyItems.AsNoTracking()
                .FirstAsync(i => i.MetadataContainerId == contribution.MetadataContainerId).ConfigureAwait(false);
            seededItems.Add(seed.Slug, item);
        }

        var match = seededItems["movie-action"];
        var nonMatch = seededItems["movie-comedy"];

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new MetadataCriteria
                    {
                        Genres = new()
                        {
                            Some = [[new GenreCriteria
                            {
                                Name = new()
                                {
                                    And = [new() { Operator = CriteriaValueOperator.Equals, Value = "Action" }]
                                }
                            }]]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var ids = result.Items.Select(i => i.Id).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Does.Contain(match.Id));
            Assert.That(ids, Does.Not.Contain(nonMatch.Id));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByMetadataTag_ReturnsOnlyMatchingItems()
    {
        // Arrange
        var seededItems = new Dictionary<string, ContentHierarchyItem>();
        foreach (var seed in new[]
        {
            (Slug: "movie-favorite", Tags: new[] { "Favorite" }),
            (Slug: "movie-archive", Tags: ["Archive"])
        })
        {
            var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
            _context.MetadataContainers.Add(container);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            var contribution = new MetadataContribution
            {
                MetadataContainerId = container.Id,
                Ordinal = 0,
                ProviderType = "test",
                Title = seed.Slug
            };
            _context.MetadataContributions.Add(contribution);
            _context.ContentHierarchyItems.Add(new ContentHierarchyItem
            {
                MetadataContainerId = container.Id,
                ContentType = "type-a",
                Level = "level-a",
                Slug = seed.Slug,
                Name = "Pending refresh",
                Monitoring = true,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync().ConfigureAwait(false);
            var trackedContainer = await _context.MetadataContainers
                .FirstAsync(c => c.Id == contribution.MetadataContainerId).ConfigureAwait(false);
            foreach (var name in seed.Tags)
            {
                var tag = await _context.Tags.FirstOrDefaultAsync(t => t.Name == name).ConfigureAwait(false);
                if (tag is null)
                {
                    tag = new Tag { Name = name };
                    _context.Tags.Add(tag);
                }

                tag.MetadataContainers.Add(trackedContainer);
            }

            await _context.SaveChangesAsync().ConfigureAwait(false);
            var item = await _context.ContentHierarchyItems.AsNoTracking()
                .FirstAsync(i => i.MetadataContainerId == contribution.MetadataContainerId).ConfigureAwait(false);
            seededItems.Add(seed.Slug, item);
        }

        var match = seededItems["movie-favorite"];
        var nonMatch = seededItems["movie-archive"];

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new MetadataCriteria
                    {
                        Tags = new()
                        {
                            Some = [[new TagCriteria
                            {
                                Name = new()
                                {
                                    And = [new() { Operator = CriteriaValueOperator.Equals, Value = "Favorite" }]
                                }
                            }]]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var ids = result.Items.Select(i => i.Id).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Does.Contain(match.Id));
            Assert.That(ids, Does.Not.Contain(nonMatch.Id));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByAward_ReturnsOnlyMatchingItems()
    {
        // Arrange
        var seededItems = new Dictionary<string, ContentHierarchyItem>();
        foreach (var seed in new[]
        {
            (Slug: "movie-oscar", Body: "Academy Awards", Name: "Best Picture"),
            (Slug: "movie-razzie", Body: "Razzies", Name: "Worst Picture")
        })
        {
            var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
            _context.MetadataContainers.Add(container);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            var contribution = new MetadataContribution
            {
                MetadataContainerId = container.Id,
                Ordinal = 0,
                ProviderType = "test",
                Title = seed.Slug
            };
            _context.MetadataContributions.Add(contribution);
            _context.ContentHierarchyItems.Add(new ContentHierarchyItem
            {
                MetadataContainerId = container.Id,
                ContentType = "type-a",
                Level = "level-a",
                Slug = seed.Slug,
                Name = "Pending refresh",
                Monitoring = true,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync().ConfigureAwait(false);
            _context.Awards.Add(new Award
            {
                MetadataContributionId = contribution.Id,
                Ordinal = 0,
                Body = seed.Body,
                Name = seed.Name
            });
            await _context.SaveChangesAsync().ConfigureAwait(false);
            var item = await _context.ContentHierarchyItems.AsNoTracking()
                .FirstAsync(i => i.MetadataContainerId == contribution.MetadataContainerId).ConfigureAwait(false);
            seededItems.Add(seed.Slug, item);
        }

        var match = seededItems["movie-oscar"];
        var nonMatch = seededItems["movie-razzie"];

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new MetadataCriteria
                    {
                        Awards = new()
                        {
                            Some = [[new AwardCriteria
                            {
                                Name = new()
                                {
                                    And = [new() { Operator = CriteriaValueOperator.Equals, Value = "Best Picture" }]
                                }
                            }]]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var ids = result.Items.Select(i => i.Id).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Does.Contain(match.Id));
            Assert.That(ids, Does.Not.Contain(nonMatch.Id));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByExternalId_ReturnsOnlyMatchingItems()
    {
        // Arrange
        var seededItems = new Dictionary<string, ContentHierarchyItem>();
        foreach (var seed in new[]
        {
            (Slug: "provider-a-type-a", Source: "provider-a", Value: "603"),
            (Slug: "provider-c-type-a", Source: "provider-c", Value: "tt0133093")
        })
        {
            var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
            _context.MetadataContainers.Add(container);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            var contribution = new MetadataContribution
            {
                MetadataContainerId = container.Id,
                Ordinal = 0,
                ProviderType = "test",
                Title = seed.Slug
            };
            _context.MetadataContributions.Add(contribution);
            _context.ContentHierarchyItems.Add(new ContentHierarchyItem
            {
                MetadataContainerId = container.Id,
                ContentType = "type-a",
                Level = "level-a",
                Slug = seed.Slug,
                Name = "Pending refresh",
                Monitoring = true,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync().ConfigureAwait(false);
            _context.ExternalIds.Add(new ExternalId
            {
                MetadataContributionId = contribution.Id,
                Ordinal = 0,
                Source = seed.Source,
                Value = seed.Value
            });
            await _context.SaveChangesAsync().ConfigureAwait(false);
            var item = await _context.ContentHierarchyItems.AsNoTracking()
                .FirstAsync(i => i.MetadataContainerId == contribution.MetadataContainerId).ConfigureAwait(false);
            seededItems.Add(seed.Slug, item);
        }

        var match = seededItems["provider-a-type-a"];
        var nonMatch = seededItems["provider-c-type-a"];

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new MetadataCriteria
                    {
                        ExternalIds = new()
                        {
                            Some = [[new ExternalIdCriteria
                            {
                                Value = new()
                                {
                                    And = [new() { Operator = CriteriaValueOperator.Equals, Value = "603" }]
                                }
                            }]]
                        }
                    }
                ]
            }
        };
        var sourceCriteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new MetadataCriteria
                    {
                        ExternalIds = new()
                        {
                            Some = [[new ExternalIdCriteria
                            {
                                Source = new()
                                {
                                    And = [new() { Operator = CriteriaValueOperator.Equals, Value = "provider-c" }]
                                }
                            }]]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);
        var sourceResult = await _subject.QueryAsync(sourceCriteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var ids = result.Items.Select(i => i.Id).ToList();
        var sourceIds = sourceResult.Items.Select(i => i.Id).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Does.Contain(match.Id));
            Assert.That(ids, Does.Not.Contain(nonMatch.Id));
            Assert.That(sourceIds, Does.Not.Contain(match.Id));
            Assert.That(sourceIds, Does.Contain(nonMatch.Id));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByContentRating_ReturnsOnlyMatchingItems()
    {
        // Arrange
        var seededItems = new Dictionary<string, ContentHierarchyItem>();
        foreach (var seed in new[]
        {
            (Slug: "movie-pg13", Body: "MPAA", Rating: "PG-13"),
            (Slug: "movie-r", Body: "MPAA", Rating: "R")
        })
        {
            var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
            _context.MetadataContainers.Add(container);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            var contribution = new MetadataContribution
            {
                MetadataContainerId = container.Id,
                Ordinal = 0,
                ProviderType = "test",
                Title = seed.Slug
            };
            _context.MetadataContributions.Add(contribution);
            _context.ContentHierarchyItems.Add(new ContentHierarchyItem
            {
                MetadataContainerId = container.Id,
                ContentType = "type-a",
                Level = "level-a",
                Slug = seed.Slug,
                Name = "Pending refresh",
                Monitoring = true,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync().ConfigureAwait(false);
            _context.ContentRatings.Add(new ContentRating
            {
                MetadataContributionId = contribution.Id,
                Ordinal = 0,
                Body = seed.Body,
                Rating = seed.Rating
            });
            await _context.SaveChangesAsync().ConfigureAwait(false);
            var item = await _context.ContentHierarchyItems.AsNoTracking()
                .FirstAsync(i => i.MetadataContainerId == contribution.MetadataContainerId).ConfigureAwait(false);
            seededItems.Add(seed.Slug, item);
        }

        var match = seededItems["movie-pg13"];
        var nonMatch = seededItems["movie-r"];

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new MetadataCriteria
                    {
                        ContentRatings = new()
                        {
                            Some = [[new ContentRatingCriteria
                            {
                                Rating = new()
                                {
                                    And = [new() { Operator = CriteriaValueOperator.Equals, Value = "PG-13" }]
                                }
                            }]]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var ids = result.Items.Select(i => i.Id).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Does.Contain(match.Id));
            Assert.That(ids, Does.Not.Contain(nonMatch.Id));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByImage_ReturnsOnlyMatchingItems()
    {
        // Arrange
        var seededItems = new Dictionary<string, ContentHierarchyItem>();
        foreach (var seed in new[]
        {
            (Slug: "movie-poster", Type: "poster"),
            (Slug: "movie-backdrop", Type: "backdrop")
        })
        {
            var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
            _context.MetadataContainers.Add(container);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            var contribution = new MetadataContribution
            {
                MetadataContainerId = container.Id,
                Ordinal = 0,
                ProviderType = "test",
                Title = seed.Slug
            };
            _context.MetadataContributions.Add(contribution);
            _context.ContentHierarchyItems.Add(new ContentHierarchyItem
            {
                MetadataContainerId = container.Id,
                ContentType = "type-a",
                Level = "level-a",
                Slug = seed.Slug,
                Name = "Pending refresh",
                Monitoring = true,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync().ConfigureAwait(false);
            _context.Images.Add(new Image
            {
                MetadataContributionId = contribution.Id,
                Ordinal = 0,
                Type = seed.Type,
                Url = new Uri($"https://images.test/{seed.Slug}.jpg")
            });
            await _context.SaveChangesAsync().ConfigureAwait(false);
            var item = await _context.ContentHierarchyItems.AsNoTracking()
                .FirstAsync(i => i.MetadataContainerId == contribution.MetadataContainerId).ConfigureAwait(false);
            seededItems.Add(seed.Slug, item);
        }

        var match = seededItems["movie-poster"];
        var nonMatch = seededItems["movie-backdrop"];

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new MetadataCriteria
                    {
                        Images = new()
                        {
                            Some = [[new ImageCriteria
                            {
                                Type = new()
                                {
                                    And = [new() { Operator = CriteriaValueOperator.Equals, Value = "poster" }]
                                }
                            }]]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var ids = result.Items.Select(i => i.Id).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Does.Contain(match.Id));
            Assert.That(ids, Does.Not.Contain(nonMatch.Id));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsOnlyMatchingItems_WhenImageUrlEqualsCriterionIsSet()
    {
        // Arrange
        var seededItems = new Dictionary<string, ContentHierarchyItem>();
        foreach (var seed in new[]
        {
            (Slug: "movie-a", Url: "https://images.test/poster-a.jpg"),
            (Slug: "movie-b", Url: "https://images.test/poster-b.jpg")
        })
        {
            var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
            _context.MetadataContainers.Add(container);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            var contribution = new MetadataContribution
            {
                MetadataContainerId = container.Id,
                Ordinal = 0,
                ProviderType = "test",
                Title = seed.Slug
            };
            _context.MetadataContributions.Add(contribution);
            _context.ContentHierarchyItems.Add(new ContentHierarchyItem
            {
                MetadataContainerId = container.Id,
                ContentType = "type-a",
                Level = "level-a",
                Slug = seed.Slug,
                Name = "Pending refresh",
                Monitoring = true,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync().ConfigureAwait(false);
            _context.Images.Add(new Image
            {
                MetadataContributionId = contribution.Id,
                Ordinal = 0,
                Type = "poster",
                Url = new Uri(seed.Url)
            });
            await _context.SaveChangesAsync().ConfigureAwait(false);
            var item = await _context.ContentHierarchyItems.AsNoTracking()
                .FirstAsync(i => i.MetadataContainerId == contribution.MetadataContainerId).ConfigureAwait(false);
            seededItems.Add(seed.Slug, item);
        }

        var match = seededItems["movie-a"];
        var nonMatch = seededItems["movie-b"];

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                        [
                            new MetadataCriteria
                            {
                                Images = new()
                                {
                                    Some = [[new ImageCriteria
                                    {
                                        Url = new()
                                        {
                                            And = [new()
                                            {
                                                Operator = CriteriaValueOperator.Equals,
                                                Value = "https://images.test/poster-a.jpg"
                                            }]
                                        }
                                    }]]
                                }
                            }
                        ]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var ids = result.Items.Select(i => i.Id).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Does.Contain(match.Id));
            Assert.That(ids, Does.Not.Contain(nonMatch.Id));
        }
    }

    [Test]
    public async Task QueryAsync_ExcludesMatchingItems_WhenImageUrlNotEqualsCriterionIsSet()
    {
        // Arrange
        var seededItems = new Dictionary<string, ContentHierarchyItem>();
        foreach (var seed in new[]
        {
            (Slug: "movie-a", Url: "https://images.test/poster-a.jpg"),
            (Slug: "movie-b", Url: "https://images.test/poster-b.jpg")
        })
        {
            var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
            _context.MetadataContainers.Add(container);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            var contribution = new MetadataContribution
            {
                MetadataContainerId = container.Id,
                Ordinal = 0,
                ProviderType = "test",
                Title = seed.Slug
            };
            _context.MetadataContributions.Add(contribution);
            _context.ContentHierarchyItems.Add(new ContentHierarchyItem
            {
                MetadataContainerId = container.Id,
                ContentType = "type-a",
                Level = "level-a",
                Slug = seed.Slug,
                Name = "Pending refresh",
                Monitoring = true,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync().ConfigureAwait(false);
            _context.Images.Add(new Image
            {
                MetadataContributionId = contribution.Id,
                Ordinal = 0,
                Type = "poster",
                Url = new Uri(seed.Url)
            });
            await _context.SaveChangesAsync().ConfigureAwait(false);
            var item = await _context.ContentHierarchyItems.AsNoTracking()
                .FirstAsync(i => i.MetadataContainerId == contribution.MetadataContainerId).ConfigureAwait(false);
            seededItems.Add(seed.Slug, item);
        }

        var match = seededItems["movie-a"];
        var excluded = seededItems["movie-b"];

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                        [
                            new MetadataCriteria
                            {
                                Images = new()
                                {
                                    Some = [[new ImageCriteria
                                    {
                                        Url = new()
                                        {
                                            And = [new()
                                            {
                                                Operator = CriteriaValueOperator.NotEquals,
                                                Value = "https://images.test/poster-b.jpg"
                                            }]
                                        }
                                    }]]
                                }
                            }
                        ]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var ids = result.Items.Select(i => i.Id).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Does.Contain(match.Id));
            Assert.That(ids, Does.Not.Contain(excluded.Id));
        }
    }

    [TestCase(CriteriaValueOperator.ContainsString)]
    [TestCase(CriteriaValueOperator.NotContainsString)]
    [TestCase(CriteriaValueOperator.StartsWithString)]
    [TestCase(CriteriaValueOperator.NotStartsWithString)]
    [TestCase(CriteriaValueOperator.EndsWithString)]
    [TestCase(CriteriaValueOperator.NotEndsWithString)]
    public async Task QueryAsync_ThrowsTypedRejection_WhenImageUrlSubstringOperatorIsSet(CriteriaValueOperator op)
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var contribution = new MetadataContribution
        {
            MetadataContainerId = container.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "movie-a"
        };
        _context.MetadataContributions.Add(contribution);
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            MetadataContainerId = container.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-a",
            Name = "Pending refresh",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Images.Add(new Image
        {
            MetadataContributionId = contribution.Id,
            Ordinal = 0,
            Type = "poster",
            Url = new Uri("https://images.test/poster-a.jpg")
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _ = await _context.ContentHierarchyItems.AsNoTracking()
            .FirstAsync(i => i.MetadataContainerId == contribution.MetadataContainerId).ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                        [
                            new MetadataCriteria
                            {
                                Images = new()
                                {
                                    Some = [[new ImageCriteria
                                    {
                                        Url = new() { And = [new() { Operator = op, Value = "images.test" }] }
                                    }]]
                                }
                            }
                        ]
            }
        };

        // Assert
        Assert.That(
            async () => await _subject.QueryAsync(criteria,
                new() { PageNumber = 1, PageSize = 100 },
                null).ConfigureAwait(false),
            Throws.ArgumentException);
    }

    [TestCase("images.test")]
    [TestCase("not a url")]
    public async Task QueryAsync_ThrowsTypedRejection_WhenImageUrlValueIsNotAbsolute(string value)
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var contribution = new MetadataContribution
        {
            MetadataContainerId = container.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "movie-a"
        };
        _context.MetadataContributions.Add(contribution);
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            MetadataContainerId = container.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-a",
            Name = "Pending refresh",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Images.Add(new Image
        {
            MetadataContributionId = contribution.Id,
            Ordinal = 0,
            Type = "poster",
            Url = new Uri("https://images.test/poster-a.jpg")
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _ = await _context.ContentHierarchyItems.AsNoTracking()
            .FirstAsync(i => i.MetadataContainerId == contribution.MetadataContainerId).ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                        [
                            new MetadataCriteria
                            {
                                Images = new()
                                {
                                    Some = [[new ImageCriteria
                                    {
                                        Url = new()
                                        {
                                            And = [new() { Operator = CriteriaValueOperator.Equals, Value = value }]
                                        }
                                    }]]
                                }
                            }
                        ]
            }
        };

        // Assert
        Assert.That(
            async () => await _subject.QueryAsync(criteria,
                new() { PageNumber = 1, PageSize = 100 },
                null).ConfigureAwait(false),
            Throws.ArgumentException);
    }

    [Test]
    public async Task QueryAsync_ReturnsOnlyMatchingItems_WhenImageUrlInCriterionIsSet()
    {
        // Arrange
        var seededItems = new Dictionary<string, ContentHierarchyItem>();
        foreach (var seed in new[]
        {
            (Slug: "movie-a", Url: "https://images.test/poster-a.jpg"),
            (Slug: "movie-b", Url: "https://images.test/poster-b.jpg")
        })
        {
            var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
            _context.MetadataContainers.Add(container);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            var contribution = new MetadataContribution
            {
                MetadataContainerId = container.Id,
                Ordinal = 0,
                ProviderType = "test",
                Title = seed.Slug
            };
            _context.MetadataContributions.Add(contribution);
            _context.ContentHierarchyItems.Add(new ContentHierarchyItem
            {
                MetadataContainerId = container.Id,
                ContentType = "type-a",
                Level = "level-a",
                Slug = seed.Slug,
                Name = "Pending refresh",
                Monitoring = true,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync().ConfigureAwait(false);
            _context.Images.Add(new Image
            {
                MetadataContributionId = contribution.Id,
                Ordinal = 0,
                Type = "poster",
                Url = new Uri(seed.Url)
            });
            await _context.SaveChangesAsync().ConfigureAwait(false);
            var item = await _context.ContentHierarchyItems.AsNoTracking()
                .FirstAsync(i => i.MetadataContainerId == contribution.MetadataContainerId).ConfigureAwait(false);
            seededItems.Add(seed.Slug, item);
        }

        var match = seededItems["movie-a"];
        var nonMatch = seededItems["movie-b"];

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new MetadataCriteria
                    {
                        Images = new()
                        {
                            Some = [[new ImageCriteria
                            {
                                Url = new()
                                {
                                    And =
                                    [
                                        new()
                                        {
                                            Operator = CriteriaValueOperator.In,
                                            Values =
                                            [
                                                "https://images.test/poster-a.jpg",
                                                "https://images.test/poster-c.jpg"
                                            ]
                                        }
                                    ]
                                }
                            }]]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var ids = result.Items.Select(i => i.Id).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Does.Contain(match.Id));
            Assert.That(ids, Does.Not.Contain(nonMatch.Id));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsItemsWithImages_WhenImageUrlIsNotNull()
    {
        // Arrange
        var imageContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(imageContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var imageContribution = new MetadataContribution
        {
            MetadataContainerId = imageContainer.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "movie-a"
        };
        _context.MetadataContributions.Add(imageContribution);
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            MetadataContainerId = imageContainer.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-a",
            Name = "Pending refresh",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Images.Add(new Image
        {
            MetadataContributionId = imageContribution.Id,
            Ordinal = 0,
            Type = "poster",
            Url = new Uri("https://images.test/poster-a.jpg")
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var match = await _context.ContentHierarchyItems.AsNoTracking()
            .FirstAsync(i => i.MetadataContainerId == imageContribution.MetadataContainerId).ConfigureAwait(false);

        var noImageContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(noImageContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var noImage = new MetadataContribution
        {
            MetadataContainerId = noImageContainer.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "movie-no-image"
        };
        _context.MetadataContributions.Add(noImage);
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            MetadataContainerId = noImageContainer.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-no-image",
            Name = "Pending refresh",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                        [
                            new MetadataCriteria
                            {
                                Images = new()
                                {
                                    Some = [[new ImageCriteria
                                    {
                                        Url = new()
                                        {
                                            And = [new() { Operator = CriteriaValueOperator.IsNotNull, Value = null }]
                                        }
                                    }]]
                                }
                            }
                        ]
            }
        };

        var excluded = await _context.ContentHierarchyItems.AsNoTracking()
            .FirstAsync(i => i.MetadataContainerId == noImage.MetadataContainerId).ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var ids = result.Items.Select(i => i.Id).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Does.Contain(match.Id));
            Assert.That(ids, Does.Not.Contain(excluded.Id));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByGenreNone_ExcludesMatchingItems()
    {
        // Arrange
        var seededItems = new Dictionary<string, ContentHierarchyItem>();
        foreach (var seed in new[]
        {
            (Slug: "movie-horror", Genres: new[] { "Horror" }),
            (Slug: "movie-drama", Genres: ["Drama"])
        })
        {
            var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
            _context.MetadataContainers.Add(container);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            var contribution = new MetadataContribution
            {
                MetadataContainerId = container.Id,
                Ordinal = 0,
                ProviderType = "test",
                Title = seed.Slug
            };
            _context.MetadataContributions.Add(contribution);
            _context.ContentHierarchyItems.Add(new ContentHierarchyItem
            {
                MetadataContainerId = container.Id,
                ContentType = "type-a",
                Level = "level-a",
                Slug = seed.Slug,
                Name = "Pending refresh",
                Monitoring = true,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync().ConfigureAwait(false);
            for (var ordinal = 0; ordinal < seed.Genres.Length; ordinal++)
            {
                _context.Genres.Add(new Genre
                {
                    MetadataContributionId = contribution.Id,
                    Ordinal = ordinal,
                    Name = seed.Genres[ordinal]
                });
            }

            await _context.SaveChangesAsync().ConfigureAwait(false);
            var item = await _context.ContentHierarchyItems.AsNoTracking()
                .FirstAsync(i => i.MetadataContainerId == contribution.MetadataContainerId).ConfigureAwait(false);
            seededItems.Add(seed.Slug, item);
        }

        var horror = seededItems["movie-horror"];
        var drama = seededItems["movie-drama"];

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new MetadataCriteria
                    {
                        Genres = new()
                        {
                            None = [new GenreCriteria
                            {
                                Name = new()
                                {
                                    And = [new() { Operator = CriteriaValueOperator.Equals, Value = "Horror" }]
                                }
                            }]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var ids = result.Items.Select(i => i.Id).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Does.Contain(drama.Id));
            Assert.That(ids, Does.Not.Contain(horror.Id));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsFilteredItems_ByLevel()
    {
        // Arrange
        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var item1 = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-c",
            Level = "level-b",
            Slug = "series-1",
            Name = "Series 1",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        var item2 = new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-c",
            Level = "level-c",
            Slug = "season-1",
            Name = "Season 1",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(item1, item2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            Level = new()
            {
                And = [new() { Value = "level-b" }]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Exactly(1).Items);
            Assert.That(items, Has.All.Matches<ContentHierarchyItem>(i => i.Level == "level-b"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsCanonicalLevelAItem_WhenContentTypeAndLevelMatch()
    {
        // Arrange
        var movieContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(movieContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var scene = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "scene",
            Name = "Scene",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        var movie = new ContentHierarchyItem
        {
            MetadataContainerId = movieContainer.Id,
            ContentType = "type-a",
            Level = "level-b",
            Slug = "movie",
            Name = "Movie",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(scene, movie);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var criteria = new ContentHierarchyItemCriteria
        {
            ContentType = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Value = "type-a" }]
            },
            Level = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Value = "level-a" }]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Items.Select(item => item.Id), Is.EqualTo([scene.Id]));
            Assert.That(result.TotalItems, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsFilteredItems_ByMonitoring()
    {
        // Arrange
        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var item1 = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "active-movie",
            Name = "Active Movie",
            Monitoring = true,
            EffectiveMonitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        var item2 = new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "excluded-movie",
            Name = "Excluded Movie",
            Monitoring = false,
            EffectiveMonitoring = false,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(item1, item2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria { EffectiveMonitoring = true };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Exactly(1).Items);
            Assert.That(items, Has.All.Matches<ContentHierarchyItem>(i => i.EffectiveMonitoring));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsFilteredItems_ByFulfillment()
    {
        // Arrange
        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var container3 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.AddRange(container2, container3);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var missingWithoutContentItem = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "missing-without-content-item",
            Name = "Missing Without Content Item",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        var missingWithContentItem = new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "missing-with-content-item",
            Name = "Missing With Content Item",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        var fulfilled = new ContentHierarchyItem
        {
            MetadataContainerId = container3.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "fulfilled",
            Name = "Fulfilled",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(missingWithoutContentItem, missingWithContentItem, fulfilled);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        _context.ContentItems.AddRange(
            new ContentItem
            {
                ContentHierarchyItemId = missingWithContentItem.Id,
                Fulfillment = FulfillmentStatus.Missing,
                CreatedAt = DateTime.UtcNow
            },
            new ContentItem
            {
                ContentHierarchyItemId = fulfilled.Id,
                Fulfillment = FulfillmentStatus.Fulfilled,
                CreatedAt = DateTime.UtcNow
            });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            Fulfillment = new()
            {
                And = [new() { Value = FulfillmentStatus.Missing }]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Exactly(2).Items);
            Assert.That(
                items,
                Has.One.With.Property(nameof(ContentHierarchyItem.Name))
                    .EqualTo("Missing Without Content Item"));
            Assert.That(
                items,
                Has.One.With.Property(nameof(ContentHierarchyItem.Name))
                    .EqualTo("Missing With Content Item"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsFilteredItems_ByTag()
    {
        // Arrange
        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var container3 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.AddRange(container2, container3);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var taggedA = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "tagged-a",
            Name = "Tagged A",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        var taggedB = new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "tagged-b",
            Name = "Tagged B",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        var untagged = new ContentHierarchyItem
        {
            MetadataContainerId = container3.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "untagged",
            Name = "Untagged",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(taggedA, taggedB, untagged);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var tag = new Tag { Name = "Favorites" };
        var firstContainer = await _context.MetadataContainers.SingleAsync(c => c.Id == _metadataContainerId)
            .ConfigureAwait(false);
        tag.MetadataContainers.Add(firstContainer);
        tag.MetadataContainers.Add(container2);
        _context.Tags.Add(tag);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            Tags = new()
            {
                And = [new() { Value = "Favorites" }]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Exactly(2).Items);
            Assert.That(items, Has.One.With.Property(nameof(ContentHierarchyItem.Name)).EqualTo("Tagged A"));
            Assert.That(items, Has.One.With.Property(nameof(ContentHierarchyItem.Name)).EqualTo("Tagged B"));
            Assert.That(items, Has.None.With.Property(nameof(ContentHierarchyItem.Name)).EqualTo("Untagged"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsFilteredItems_ByRelationshipKind()
    {
        // Arrange
        var relationshipSubjectContainer1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipSubjectContainer1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContribution1 = new MetadataContribution
        {
            MetadataContainerId = relationshipSubjectContainer1.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Delphine Ashworth"
        };
        _context.MetadataContributions.Add(relationshipSubjectContribution1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = relationshipSubjectContainer1.Id,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        });
        var relationshipContentContainer1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipContentContainer1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipContribution1 = new MetadataContribution
        {
            MetadataContainerId = relationshipContentContainer1.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Scene A"
        };
        relationshipContribution1.Relationships.Add(new Relationship
        {
            Kind = "performer",
            RelatedMetadataContainerId = relationshipSubjectContainer1.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(relationshipContribution1);
        var relationshipItem1 = new ContentHierarchyItem
        {
            MetadataContainerId = relationshipContentContainer1.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "perf-content",
            Name = "Scene A",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(relationshipItem1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var performerItem = relationshipItem1;
        var relationshipSubjectContainer2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipSubjectContainer2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContribution2 = new MetadataContribution
        {
            MetadataContainerId = relationshipSubjectContainer2.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Big Studio"
        };
        _context.MetadataContributions.Add(relationshipSubjectContribution2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = relationshipSubjectContainer2.Id,
            Kind = "studio",
            CreatedAt = DateTime.UtcNow
        });
        var relationshipContentContainer2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipContentContainer2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipContribution2 = new MetadataContribution
        {
            MetadataContainerId = relationshipContentContainer2.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Scene B"
        };
        relationshipContribution2.Relationships.Add(new Relationship
        {
            Kind = "studio",
            RelatedMetadataContainerId = relationshipSubjectContainer2.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(relationshipContribution2);
        var relationshipItem2 = new ContentHierarchyItem
        {
            MetadataContainerId = relationshipContentContainer2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "studio-content",
            Name = "Scene B",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(relationshipItem2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new()
                    {
                        Relationships = new()
                        {
                            Some =
[
[
                                new()
                                {
                                    Kind = new()
                                    {
                                        And = [new() { Operator = CriteriaValueOperator.Equals, Value = "performer" }]
                                    }
                                }
]
]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Exactly(1).Items);
            Assert.That(items[0].Id, Is.EqualTo(performerItem.Id));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsOnlyLinkedItems_ByRelationshipRelatedMetadataContainerId()
    {
        // Arrange
        var scopedSubjectContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(scopedSubjectContainer);
        var otherSubjectContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(otherSubjectContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.MetadataContributions.Add(new MetadataContribution
        {
            MetadataContainerId = scopedSubjectContainer.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Kevin Heffernan"
        });
        _context.MetadataContributions.Add(new MetadataContribution
        {
            MetadataContainerId = otherSubjectContainer.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Kevin Heffernan"
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = scopedSubjectContainer.Id,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        });
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = otherSubjectContainer.Id,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        });
        var scopedContentContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(scopedContentContainer);
        var otherContentContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(otherContentContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var scopedContribution = new MetadataContribution
        {
            MetadataContainerId = scopedContentContainer.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Super Troopers"
        };
        scopedContribution.Relationships.Add(new Relationship
        {
            Kind = "performer",
            RelatedMetadataContainerId = scopedSubjectContainer.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(scopedContribution);
        var otherContribution = new MetadataContribution
        {
            MetadataContainerId = otherContentContainer.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Night Shift"
        };
        otherContribution.Relationships.Add(new Relationship
        {
            Kind = "performer",
            RelatedMetadataContainerId = otherSubjectContainer.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(otherContribution);
        var scopedItem = new ContentHierarchyItem
        {
            MetadataContainerId = scopedContentContainer.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "super-troopers",
            Name = "Super Troopers",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(scopedItem);
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            MetadataContainerId = otherContentContainer.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "night-shift",
            Name = "Night Shift",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new()
                    {
                        Relationships = new()
                        {
                            Some =
                            [
                                [
                                    new()
                                    {
                                        RelatedMetadataContainerId = new()
                                        {
                                            And =
                                            [
                                                new()
                                                {
                                                    Operator = CriteriaValueOperator.Equals,
                                                    Value = scopedSubjectContainer.Id
                                                }
                                            ]
                                        }
                                    }
                                ]
                            ]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Exactly(1).Items);
            Assert.That(items[0].Id, Is.EqualTo(scopedItem.Id));
            Assert.That(items[0].Name, Is.EqualTo("Super Troopers"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsFilteredItems_ByRelationshipKindCaseInsensitively()
    {
        // Arrange
        var relationshipSubjectContainer3 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipSubjectContainer3);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContribution3 = new MetadataContribution
        {
            MetadataContainerId = relationshipSubjectContainer3.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Delphine Ashworth"
        };
        _context.MetadataContributions.Add(relationshipSubjectContribution3);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = relationshipSubjectContainer3.Id,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        });
        var relationshipContentContainer3 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipContentContainer3);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipContribution3 = new MetadataContribution
        {
            MetadataContainerId = relationshipContentContainer3.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Scene A"
        };
        relationshipContribution3.Relationships.Add(new Relationship
        {
            Kind = "performer",
            RelatedMetadataContainerId = relationshipSubjectContainer3.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(relationshipContribution3);
        var relationshipItem3 = new ContentHierarchyItem
        {
            MetadataContainerId = relationshipContentContainer3.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "perf-content",
            Name = "Scene A",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(relationshipItem3);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var performerItem = relationshipItem3;
        var relationshipSubjectContainer4 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipSubjectContainer4);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContribution4 = new MetadataContribution
        {
            MetadataContainerId = relationshipSubjectContainer4.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Big Studio"
        };
        _context.MetadataContributions.Add(relationshipSubjectContribution4);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = relationshipSubjectContainer4.Id,
            Kind = "studio",
            CreatedAt = DateTime.UtcNow
        });
        var relationshipContentContainer4 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipContentContainer4);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipContribution4 = new MetadataContribution
        {
            MetadataContainerId = relationshipContentContainer4.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Scene B"
        };
        relationshipContribution4.Relationships.Add(new Relationship
        {
            Kind = "studio",
            RelatedMetadataContainerId = relationshipSubjectContainer4.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(relationshipContribution4);
        var relationshipItem4 = new ContentHierarchyItem
        {
            MetadataContainerId = relationshipContentContainer4.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "studio-content",
            Name = "Scene B",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(relationshipItem4);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new()
                    {
                        Relationships = new()
                        {
                            Some =
[
[
                                new()
                                {
                                    Kind = new()
                                    {
                                        And = [new() { Operator = CriteriaValueOperator.Equals, Value = "Performer" }]
                                    }
                                }
]
]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Exactly(1).Items);
            Assert.That(items[0].Id, Is.EqualTo(performerItem.Id));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsFilteredItems_ByRelationshipSubjectName()
    {
        // Arrange
        var relationshipSubjectContainer5 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipSubjectContainer5);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContribution5 = new MetadataContribution
        {
            MetadataContainerId = relationshipSubjectContainer5.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Delphine Ashworth"
        };
        _context.MetadataContributions.Add(relationshipSubjectContribution5);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = relationshipSubjectContainer5.Id,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        });
        var relationshipContentContainer5 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipContentContainer5);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipContribution5 = new MetadataContribution
        {
            MetadataContainerId = relationshipContentContainer5.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Scene A"
        };
        relationshipContribution5.Relationships.Add(new Relationship
        {
            Kind = "performer",
            RelatedMetadataContainerId = relationshipSubjectContainer5.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(relationshipContribution5);
        var relationshipItem5 = new ContentHierarchyItem
        {
            MetadataContainerId = relationshipContentContainer5.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "delphine-content",
            Name = "Scene A",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(relationshipItem5);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var match = relationshipItem5;
        var relationshipSubjectContainer6 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipSubjectContainer6);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContribution6 = new MetadataContribution
        {
            MetadataContainerId = relationshipSubjectContainer6.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Marlowe Quinn"
        };
        _context.MetadataContributions.Add(relationshipSubjectContribution6);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = relationshipSubjectContainer6.Id,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        });
        var relationshipContentContainer6 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipContentContainer6);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipContribution6 = new MetadataContribution
        {
            MetadataContainerId = relationshipContentContainer6.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Scene B"
        };
        relationshipContribution6.Relationships.Add(new Relationship
        {
            Kind = "performer",
            RelatedMetadataContainerId = relationshipSubjectContainer6.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(relationshipContribution6);
        var relationshipItem6 = new ContentHierarchyItem
        {
            MetadataContainerId = relationshipContentContainer6.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "other-content",
            Name = "Scene B",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(relationshipItem6);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new()
                    {
                        Relationships = new()
                        {
                            Some =
[
[
                                new()
                                {
                                    Subject = new()
                                    {
                                        And =
                                        [
                                            new()
                                            {
                                                Operator = CriteriaValueOperator.ContainsString,
                                                Value = "Delphine"
                                            }
                                        ]
                                    }
                                }
]
]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Exactly(1).Items);
            Assert.That(items[0].Id, Is.EqualTo(match.Id));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsFilteredItems_ByRelationshipSubjectNameCaseInsensitively()
    {
        // Arrange
        var relationshipSubjectContainer7 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipSubjectContainer7);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContribution7 = new MetadataContribution
        {
            MetadataContainerId = relationshipSubjectContainer7.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Sable Vance"
        };
        _context.MetadataContributions.Add(relationshipSubjectContribution7);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = relationshipSubjectContainer7.Id,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        });
        var relationshipContentContainer7 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipContentContainer7);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipContribution7 = new MetadataContribution
        {
            MetadataContainerId = relationshipContentContainer7.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Scene A"
        };
        relationshipContribution7.Relationships.Add(new Relationship
        {
            Kind = "performer",
            RelatedMetadataContainerId = relationshipSubjectContainer7.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(relationshipContribution7);
        var relationshipItem7 = new ContentHierarchyItem
        {
            MetadataContainerId = relationshipContentContainer7.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "sable-content",
            Name = "Scene A",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(relationshipItem7);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var match = relationshipItem7;
        var relationshipSubjectContainer8 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipSubjectContainer8);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContribution8 = new MetadataContribution
        {
            MetadataContainerId = relationshipSubjectContainer8.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Marlowe Quinn"
        };
        _context.MetadataContributions.Add(relationshipSubjectContribution8);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = relationshipSubjectContainer8.Id,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        });
        var relationshipContentContainer8 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipContentContainer8);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipContribution8 = new MetadataContribution
        {
            MetadataContainerId = relationshipContentContainer8.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Scene B"
        };
        relationshipContribution8.Relationships.Add(new Relationship
        {
            Kind = "performer",
            RelatedMetadataContainerId = relationshipSubjectContainer8.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(relationshipContribution8);
        var relationshipItem8 = new ContentHierarchyItem
        {
            MetadataContainerId = relationshipContentContainer8.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "other-content",
            Name = "Scene B",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(relationshipItem8);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new()
                    {
                        Relationships = new()
                        {
                            Some =
[
[
                                new()
                                {
                                    Subject = new()
                                    {
                                        And =
                                        [
                                            new() { Operator = CriteriaValueOperator.Equals, Value = "sable vance" }
                                        ]
                                    }
                                }
]
]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Exactly(1).Items);
            Assert.That(items[0].Id, Is.EqualTo(match.Id));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsFilteredItems_ByRelationshipSubjectNameAlias()
    {
        // Arrange
        var relationshipSubjectContainer9 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipSubjectContainer9);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContribution9 = new MetadataContribution
        {
            MetadataContainerId = relationshipSubjectContainer9.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Odessandra Kerr"
        };
        var relationshipAliases9 = new[] { "Odessa Kerr" };
        for (var i = 0; i < relationshipAliases9.Length; i++)
        {
            relationshipSubjectContribution9.AlternateTexts.Add(new AlternateText
            {
                Ordinal = i,
                Kind = "Name",
                Text = relationshipAliases9[i]
            });
        }

        _context.MetadataContributions.Add(relationshipSubjectContribution9);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = relationshipSubjectContainer9.Id,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        });
        var relationshipContentContainer9 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipContentContainer9);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipContribution9 = new MetadataContribution
        {
            MetadataContainerId = relationshipContentContainer9.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Scene A"
        };
        relationshipContribution9.Relationships.Add(new Relationship
        {
            Kind = "performer",
            RelatedMetadataContainerId = relationshipSubjectContainer9.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(relationshipContribution9);
        var relationshipItem9 = new ContentHierarchyItem
        {
            MetadataContainerId = relationshipContentContainer9.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "kerr-content",
            Name = "Scene A",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(relationshipItem9);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var match = relationshipItem9;
        var relationshipSubjectContainer10 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipSubjectContainer10);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContribution10 = new MetadataContribution
        {
            MetadataContainerId = relationshipSubjectContainer10.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Marlowe Quinn"
        };
        _context.MetadataContributions.Add(relationshipSubjectContribution10);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = relationshipSubjectContainer10.Id,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        });
        var relationshipContentContainer10 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipContentContainer10);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipContribution10 = new MetadataContribution
        {
            MetadataContainerId = relationshipContentContainer10.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Scene B"
        };
        relationshipContribution10.Relationships.Add(new Relationship
        {
            Kind = "performer",
            RelatedMetadataContainerId = relationshipSubjectContainer10.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(relationshipContribution10);
        var relationshipItem10 = new ContentHierarchyItem
        {
            MetadataContainerId = relationshipContentContainer10.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "other-content",
            Name = "Scene B",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(relationshipItem10);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var queryCriteria = new[]
        {
            (Operator: CriteriaValueOperator.ContainsString, Value: "odessa kerr"),
            (Operator: CriteriaValueOperator.Equals, Value: "Odessa Kerr"),
            (Operator: CriteriaValueOperator.ContainsString, Value: "Odessandra")
        };

        // Act
        var queryResults = new List<List<ContentHierarchyItem>>();
        foreach (var criterion in queryCriteria)
        {
            var criteria = new ContentHierarchyItemCriteria
            {
                Metadata = new()
                {
                    And =
                    [
                        new()
                        {
                            Relationships = new()
                            {
                                Some =
                                [
                                    [
                                        new()
                                        {
                                            Subject = new()
                                            {
                                                And = [new()
                                                {
                                                    Operator = criterion.Operator,
                                                    Value = criterion.Value
                                                }]
                                            }
                                        }
                                    ]
                                ]
                            }
                        }
                    ]
                }
            };
            var result = await _subject.QueryAsync(criteria,
                new() { PageNumber = 1, PageSize = 100 },
                null).ConfigureAwait(false);
            queryResults.Add([.. result.Items]);
        }

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(queryResults[0], Has.Exactly(1).Items);
            Assert.That(queryResults[0][0].Id, Is.EqualTo(match.Id));
            Assert.That(queryResults[1], Has.Exactly(1).Items);
            Assert.That(queryResults[1][0].Id, Is.EqualTo(match.Id));
            Assert.That(queryResults[2], Has.Exactly(1).Items);
            Assert.That(queryResults[2][0].Id, Is.EqualTo(match.Id));
        }
    }

    [Test]
    public async Task QueryAsync_NegativeSubjectOperatorExcludesAliasMatch_WhenNegativeSubjectOperatorIsSet()
    {
        // Arrange
        var relationshipSubjectContainer11 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipSubjectContainer11);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContribution11 = new MetadataContribution
        {
            MetadataContainerId = relationshipSubjectContainer11.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Bob Smith"
        };
        var relationshipAliases11 = new[] { "Nova" };
        for (var i = 0; i < relationshipAliases11.Length; i++)
        {
            relationshipSubjectContribution11.AlternateTexts.Add(new AlternateText
            {
                Ordinal = i,
                Kind = "Name",
                Text = relationshipAliases11[i]
            });
        }

        _context.MetadataContributions.Add(relationshipSubjectContribution11);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = relationshipSubjectContainer11.Id,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        });
        var relationshipContentContainer11 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipContentContainer11);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipContribution11 = new MetadataContribution
        {
            MetadataContainerId = relationshipContentContainer11.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Scene A"
        };
        relationshipContribution11.Relationships.Add(new Relationship
        {
            Kind = "performer",
            RelatedMetadataContainerId = relationshipSubjectContainer11.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(relationshipContribution11);
        var relationshipItem11 = new ContentHierarchyItem
        {
            MetadataContainerId = relationshipContentContainer11.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "bob-content",
            Name = "Scene A",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(relationshipItem11);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContainer12 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipSubjectContainer12);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContribution12 = new MetadataContribution
        {
            MetadataContainerId = relationshipSubjectContainer12.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Marlowe Quinn"
        };
        _context.MetadataContributions.Add(relationshipSubjectContribution12);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = relationshipSubjectContainer12.Id,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        });
        var relationshipContentContainer12 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipContentContainer12);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipContribution12 = new MetadataContribution
        {
            MetadataContainerId = relationshipContentContainer12.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Scene B"
        };
        relationshipContribution12.Relationships.Add(new Relationship
        {
            Kind = "performer",
            RelatedMetadataContainerId = relationshipSubjectContainer12.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(relationshipContribution12);
        var relationshipItem12 = new ContentHierarchyItem
        {
            MetadataContainerId = relationshipContentContainer12.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "marlowe-content",
            Name = "Scene B",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(relationshipItem12);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var kept = relationshipItem12;

        var queryCriteria = new[]
        {
            (Operator: CriteriaValueOperator.NotContainsString, Value: "nova"),
            (Operator: CriteriaValueOperator.NotEquals, Value: "Nova")
        };

        // Act
        var queryResults = new List<List<Guid>>();
        foreach (var criterion in queryCriteria)
        {
            var criteria = new ContentHierarchyItemCriteria
            {
                Metadata = new()
                {
                    And =
                    [
                        new()
                        {
                            Relationships = new()
                            {
                                Some =
                                [
                                    [
                                        new()
                                        {
                                            Subject = new()
                                            {
                                                And = [new()
                                                {
                                                    Operator = criterion.Operator,
                                                    Value = criterion.Value
                                                }]
                                            }
                                        }
                                    ]
                                ]
                            }
                        }
                    ]
                }
            };
            var result = await _subject.QueryAsync(criteria,
                new() { PageNumber = 1, PageSize = 100 },
                null).ConfigureAwait(false);
            queryResults.Add([.. result.Items.Select(i => i.Id)]);
        }

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(queryResults[0], Is.EquivalentTo([kept.Id]));
            Assert.That(queryResults[1], Is.EquivalentTo([kept.Id]));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsFilteredItems_ByRelationshipSubjectExternalId()
    {
        // Arrange
        var relationshipSubjectContainer13 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipSubjectContainer13);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContribution13 = new MetadataContribution
        {
            MetadataContainerId = relationshipSubjectContainer13.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Delphine Ashworth"
        };
        relationshipSubjectContribution13.ExternalIds.Add(new ExternalId
        {
            Ordinal = 0,
            Source = "provider-b.performer",
            Value = "12345"
        });
        _context.MetadataContributions.Add(relationshipSubjectContribution13);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = relationshipSubjectContainer13.Id,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        });
        var relationshipContentContainer13 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipContentContainer13);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipContribution13 = new MetadataContribution
        {
            MetadataContainerId = relationshipContentContainer13.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Scene A"
        };
        relationshipContribution13.Relationships.Add(new Relationship
        {
            Kind = "performer",
            RelatedMetadataContainerId = relationshipSubjectContainer13.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(relationshipContribution13);
        var relationshipItem13 = new ContentHierarchyItem
        {
            MetadataContainerId = relationshipContentContainer13.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "pinned-content",
            Name = "Scene A",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(relationshipItem13);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var match = relationshipItem13;
        var relationshipSubjectContainer14 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipSubjectContainer14);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContribution14 = new MetadataContribution
        {
            MetadataContainerId = relationshipSubjectContainer14.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Delphine Ashworth"
        };
        relationshipSubjectContribution14.ExternalIds.Add(new ExternalId
        {
            Ordinal = 0,
            Source = "provider-b.performer",
            Value = "99999"
        });
        _context.MetadataContributions.Add(relationshipSubjectContribution14);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = relationshipSubjectContainer14.Id,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        });
        var relationshipContentContainer14 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipContentContainer14);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipContribution14 = new MetadataContribution
        {
            MetadataContainerId = relationshipContentContainer14.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Scene B"
        };
        relationshipContribution14.Relationships.Add(new Relationship
        {
            Kind = "performer",
            RelatedMetadataContainerId = relationshipSubjectContainer14.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(relationshipContribution14);
        var relationshipItem14 = new ContentHierarchyItem
        {
            MetadataContainerId = relationshipContentContainer14.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "other-content",
            Name = "Scene B",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(relationshipItem14);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new()
                    {
                        Relationships = new()
                        {
                            Some =
[
[
                                new()
                                {
                                    ExternalIds = new()
                                    {
                                        Some =
                                        [
                                            [
                                                new()
                                                {
                                                    Value = new()
                                                    {
                                                        And =
                                                        [
                                                            new()
                                                            {
                                                                Operator = CriteriaValueOperator.Equals,
                                                                Value = "12345"
                                                            }
                                                        ]
                                                    },
                                                    Source = new()
                                                    {
                                                        And =
                                                        [
                                                            new()
                                                            {
                                                                Operator = CriteriaValueOperator.Equals,
                                                                Value = "provider-b.performer"
                                                            }
                                                        ]
                                                    }
                                                }
                                            ]
                                        ]
                                    }
                                }
]
]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Exactly(1).Items);
            Assert.That(items[0].Id, Is.EqualTo(match.Id));
        }
    }

    [Test]
    public async Task QueryAsync_AndedRelationshipHasClauses_IntersectAcrossRows()
    {
        // Arrange
        var subjects = new Dictionary<string, Guid>();
        foreach (var title in new[] { "Subject A", "Subject B" })
        {
            var subjectContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
            _context.MetadataContainers.Add(subjectContainer);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            _context.MetadataContributions.Add(new MetadataContribution
            {
                MetadataContainerId = subjectContainer.Id,
                Ordinal = 0,
                ProviderType = "test",
                Title = title
            });
            _context.Subjects.Add(new Subject
            {
                MetadataContainerId = subjectContainer.Id,
                Kind = "person",
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync().ConfigureAwait(false);
            subjects.Add(title, subjectContainer.Id);
        }

        var subjectA = subjects["Subject A"];
        var subjectB = subjects["Subject B"];

        var contentSeeds = new[]
        {
            (
                Slug: "only-a",
                Title: "Only A",
                Relationships: new (string Kind, Guid SubjectContainerId)[]
                    {
                        ("performer", subjectA)
                    }),
            (Slug: "only-b", Title: "Only B", Relationships: [("performer", subjectB)]),
            (Slug: "both", Title: "Both", Relationships: [("performer", subjectA), ("performer", subjectB)])
        };
        var seededItems = new Dictionary<string, ContentHierarchyItem>();
        foreach (var seed in contentSeeds)
        {
            var contentContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
            _context.MetadataContainers.Add(contentContainer);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            var contribution = new MetadataContribution
            {
                MetadataContainerId = contentContainer.Id,
                Ordinal = 0,
                ProviderType = "test",
                Title = seed.Title
            };
            for (var i = 0; i < seed.Relationships.Length; i++)
            {
                contribution.Relationships.Add(new Relationship
                {
                    Kind = seed.Relationships[i].Kind,
                    RelatedMetadataContainerId = seed.Relationships[i].SubjectContainerId,
                    Ordinal = i
                });
            }

            _context.MetadataContributions.Add(contribution);
            var item = new ContentHierarchyItem
            {
                MetadataContainerId = contentContainer.Id,
                ContentType = "type-a",
                Level = "level-a",
                Slug = seed.Slug,
                Name = seed.Title,
                Monitoring = true,
                CreatedAt = DateTime.UtcNow
            };
            _context.ContentHierarchyItems.Add(item);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            seededItems.Add(seed.Slug, item);
        }

        var onlyA = seededItems["only-a"];
        var onlyB = seededItems["only-b"];
        var both = seededItems["both"];

        var andCriteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new()
                    {
                        Relationships = new()
                        {
                            Some =
                            [
                                [new RelationshipCriteria
                                {
                                    Subject = new()
                                    {
                                        And = [new()
                                        {
                                            Operator = CriteriaValueOperator.ContainsString,
                                            Value = "Subject A"
                                        }]
                                    }
                                }],
                                [new RelationshipCriteria
                                {
                                    Subject = new()
                                    {
                                        And = [new()
                                        {
                                            Operator = CriteriaValueOperator.ContainsString,
                                            Value = "Subject B"
                                        }]
                                    }
                                }]
                            ]
                        }
                    }
                ]
            }
        };

        var orCriteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new()
                    {
                        Relationships = new()
                        {
                            Some =
                            [
                                [
                                    new RelationshipCriteria
                                    {
                                        Subject = new()
                                        {
                                            And = [new()
                                            {
                                                Operator = CriteriaValueOperator.ContainsString,
                                                Value = "Subject A"
                                            }]
                                        }
                                    },
                                    new RelationshipCriteria
                                    {
                                        Subject = new()
                                        {
                                            And = [new()
                                            {
                                                Operator = CriteriaValueOperator.ContainsString,
                                                Value = "Subject B"
                                            }]
                                        }
                                    }
                                ]
                            ]
                        }
                    }
                ]
            }
        };

        // Act
        var andResult = await _subject.QueryAsync(andCriteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);
        var orResult = await _subject.QueryAsync(orCriteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(andResult.Items.Select(i => i.Id), Is.EquivalentTo([both.Id]));
            Assert.That(orResult.Items.Select(i => i.Id), Is.EquivalentTo([onlyA.Id, onlyB.Id, both.Id]));
        }
    }

    [Test]
    public async Task QueryAsync_ExcludesItems_ByRelationshipNone()
    {
        // Arrange
        var relationshipSubjectContainer15 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipSubjectContainer15);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContribution15 = new MetadataContribution
        {
            MetadataContainerId = relationshipSubjectContainer15.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Big Studio"
        };
        _context.MetadataContributions.Add(relationshipSubjectContribution15);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = relationshipSubjectContainer15.Id,
            Kind = "studio",
            CreatedAt = DateTime.UtcNow
        });
        var relationshipContentContainer15 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipContentContainer15);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipContribution15 = new MetadataContribution
        {
            MetadataContainerId = relationshipContentContainer15.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Scene A"
        };
        relationshipContribution15.Relationships.Add(new Relationship
        {
            Kind = "studio",
            RelatedMetadataContainerId = relationshipSubjectContainer15.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(relationshipContribution15);
        var relationshipItem15 = new ContentHierarchyItem
        {
            MetadataContainerId = relationshipContentContainer15.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "studio-content",
            Name = "Scene A",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(relationshipItem15);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContainer16 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipSubjectContainer16);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContribution16 = new MetadataContribution
        {
            MetadataContainerId = relationshipSubjectContainer16.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Delphine Ashworth"
        };
        _context.MetadataContributions.Add(relationshipSubjectContribution16);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = relationshipSubjectContainer16.Id,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        });
        var relationshipContentContainer16 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipContentContainer16);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipContribution16 = new MetadataContribution
        {
            MetadataContainerId = relationshipContentContainer16.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Scene B"
        };
        relationshipContribution16.Relationships.Add(new Relationship
        {
            Kind = "performer",
            RelatedMetadataContainerId = relationshipSubjectContainer16.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(relationshipContribution16);
        var relationshipItem16 = new ContentHierarchyItem
        {
            MetadataContainerId = relationshipContentContainer16.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "performer-content",
            Name = "Scene B",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(relationshipItem16);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var kept = relationshipItem16;

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new()
                    {
                        Relationships = new()
                        {
                            None =
                            [
                                new()
                                {
                                    Kind = new()
                                    {
                                        And = [new() { Operator = CriteriaValueOperator.Equals, Value = "studio" }]
                                    }
                                }
                            ]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Exactly(1).Items);
            Assert.That(items[0].Id, Is.EqualTo(kept.Id));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsFilteredItems_ByRelationshipTypePersonSubject()
    {
        // Arrange
        var relationshipSubjectContainer17 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipSubjectContainer17);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContribution17 = new MetadataContribution
        {
            MetadataContainerId = relationshipSubjectContainer17.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Bella Hadid"
        };
        _context.MetadataContributions.Add(relationshipSubjectContribution17);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = relationshipSubjectContainer17.Id,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        });
        var relationshipContentContainer17 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipContentContainer17);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipContribution17 = new MetadataContribution
        {
            MetadataContainerId = relationshipContentContainer17.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Scene A"
        };
        relationshipContribution17.Relationships.Add(new Relationship
        {
            Kind = "performer",
            RelatedMetadataContainerId = relationshipSubjectContainer17.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(relationshipContribution17);
        var relationshipItem17 = new ContentHierarchyItem
        {
            MetadataContainerId = relationshipContentContainer17.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "perf-content",
            Name = "Scene A",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(relationshipItem17);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var personItem = relationshipItem17;
        var relationshipSubjectContainer18 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipSubjectContainer18);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContribution18 = new MetadataContribution
        {
            MetadataContainerId = relationshipSubjectContainer18.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Big Studio"
        };
        _context.MetadataContributions.Add(relationshipSubjectContribution18);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = relationshipSubjectContainer18.Id,
            Kind = "studio",
            CreatedAt = DateTime.UtcNow
        });
        var relationshipContentContainer18 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipContentContainer18);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipContribution18 = new MetadataContribution
        {
            MetadataContainerId = relationshipContentContainer18.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Scene B"
        };
        relationshipContribution18.Relationships.Add(new Relationship
        {
            Kind = "studio",
            RelatedMetadataContainerId = relationshipSubjectContainer18.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(relationshipContribution18);
        var relationshipItem18 = new ContentHierarchyItem
        {
            MetadataContainerId = relationshipContentContainer18.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "studio-content",
            Name = "Scene B",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(relationshipItem18);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new()
                    {
                        Relationships = new()
                        {
                            Some =
[
[
                                new()
                                {
                                    Type = new()
                                    {
                                        And = [new() { Operator = CriteriaValueOperator.Equals, Value = "person" }]
                                    }
                                }
]
]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Exactly(1).Items);
            Assert.That(items[0].Id, Is.EqualTo(personItem.Id));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsFilteredItems_ByRelationshipTypeCaseInsensitively()
    {
        // Arrange
        var relationshipSubjectContainer19 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipSubjectContainer19);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContribution19 = new MetadataContribution
        {
            MetadataContainerId = relationshipSubjectContainer19.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Bella Hadid"
        };
        _context.MetadataContributions.Add(relationshipSubjectContribution19);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = relationshipSubjectContainer19.Id,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        });
        var relationshipContentContainer19 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipContentContainer19);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipContribution19 = new MetadataContribution
        {
            MetadataContainerId = relationshipContentContainer19.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Scene A"
        };
        relationshipContribution19.Relationships.Add(new Relationship
        {
            Kind = "performer",
            RelatedMetadataContainerId = relationshipSubjectContainer19.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(relationshipContribution19);
        var relationshipItem19 = new ContentHierarchyItem
        {
            MetadataContainerId = relationshipContentContainer19.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "perf-content",
            Name = "Scene A",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(relationshipItem19);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var personItem = relationshipItem19;
        var relationshipSubjectContainer20 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipSubjectContainer20);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContribution20 = new MetadataContribution
        {
            MetadataContainerId = relationshipSubjectContainer20.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Big Studio"
        };
        _context.MetadataContributions.Add(relationshipSubjectContribution20);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = relationshipSubjectContainer20.Id,
            Kind = "studio",
            CreatedAt = DateTime.UtcNow
        });
        var relationshipContentContainer20 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipContentContainer20);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipContribution20 = new MetadataContribution
        {
            MetadataContainerId = relationshipContentContainer20.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Scene B"
        };
        relationshipContribution20.Relationships.Add(new Relationship
        {
            Kind = "studio",
            RelatedMetadataContainerId = relationshipSubjectContainer20.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(relationshipContribution20);
        var relationshipItem20 = new ContentHierarchyItem
        {
            MetadataContainerId = relationshipContentContainer20.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "studio-content",
            Name = "Scene B",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(relationshipItem20);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new()
                    {
                        Relationships = new()
                        {
                            Some =
[
[
                                new()
                                {
                                    Type = new()
                                    {
                                        And = [new() { Operator = CriteriaValueOperator.Equals, Value = "Person" }]
                                    }
                                }
]
]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Exactly(1).Items);
            Assert.That(items[0].Id, Is.EqualTo(personItem.Id));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsFilteredItems_ByRelationshipTypeContentLevel()
    {
        // Arrange
        var targetContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(targetContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var targetItem = new ContentHierarchyItem
        {
            MetadataContainerId = targetContainer.Id,
            ContentType = "type-e",
            Level = "level-e",
            Slug = "movie-content-target",
            Name = "Movie A Target",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(targetItem);
        var contentContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(contentContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var contribution = new MetadataContribution
        {
            MetadataContainerId = contentContainer.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Movie A"
        };
        contribution.Relationships.Add(new Relationship
        {
            Kind = "related",
            RelatedMetadataContainerId = targetContainer.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(contribution);
        var match = new ContentHierarchyItem
        {
            MetadataContainerId = contentContainer.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-content",
            Name = "Movie A",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(match);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContainer21 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipSubjectContainer21);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContribution21 = new MetadataContribution
        {
            MetadataContainerId = relationshipSubjectContainer21.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Bella Hadid"
        };
        _context.MetadataContributions.Add(relationshipSubjectContribution21);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = relationshipSubjectContainer21.Id,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        });
        var relationshipContentContainer21 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipContentContainer21);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipContribution21 = new MetadataContribution
        {
            MetadataContainerId = relationshipContentContainer21.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Scene B"
        };
        relationshipContribution21.Relationships.Add(new Relationship
        {
            Kind = "performer",
            RelatedMetadataContainerId = relationshipSubjectContainer21.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(relationshipContribution21);
        var relationshipItem21 = new ContentHierarchyItem
        {
            MetadataContainerId = relationshipContentContainer21.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "perf-content",
            Name = "Scene B",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(relationshipItem21);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new()
                    {
                        Relationships = new()
                        {
                            Some =
[
[
                                new()
                                {
                                    Type = new()
                                    {
                                        And = [new() { Operator = CriteriaValueOperator.Equals, Value = "level-e" }]
                                    }
                                }
]
]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Exactly(1).Items);
            Assert.That(items[0].Id, Is.EqualTo(match.Id));
        }
    }

    [Test]
    public async Task QueryAsync_ExcludesItems_ByRelationshipTypeNone()
    {
        // Arrange
        var relationshipSubjectContainer22 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipSubjectContainer22);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContribution22 = new MetadataContribution
        {
            MetadataContainerId = relationshipSubjectContainer22.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Bella Hadid"
        };
        _context.MetadataContributions.Add(relationshipSubjectContribution22);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = relationshipSubjectContainer22.Id,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        });
        var relationshipContentContainer22 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipContentContainer22);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipContribution22 = new MetadataContribution
        {
            MetadataContainerId = relationshipContentContainer22.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Scene A"
        };
        relationshipContribution22.Relationships.Add(new Relationship
        {
            Kind = "performer",
            RelatedMetadataContainerId = relationshipSubjectContainer22.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(relationshipContribution22);
        var relationshipItem22 = new ContentHierarchyItem
        {
            MetadataContainerId = relationshipContentContainer22.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "perf-content",
            Name = "Scene A",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(relationshipItem22);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContainer23 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipSubjectContainer23);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContribution23 = new MetadataContribution
        {
            MetadataContainerId = relationshipSubjectContainer23.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Big Studio"
        };
        _context.MetadataContributions.Add(relationshipSubjectContribution23);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = relationshipSubjectContainer23.Id,
            Kind = "studio",
            CreatedAt = DateTime.UtcNow
        });
        var relationshipContentContainer23 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipContentContainer23);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipContribution23 = new MetadataContribution
        {
            MetadataContainerId = relationshipContentContainer23.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Scene B"
        };
        relationshipContribution23.Relationships.Add(new Relationship
        {
            Kind = "studio",
            RelatedMetadataContainerId = relationshipSubjectContainer23.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(relationshipContribution23);
        var relationshipItem23 = new ContentHierarchyItem
        {
            MetadataContainerId = relationshipContentContainer23.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "studio-content",
            Name = "Scene B",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(relationshipItem23);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var kept = relationshipItem23;

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new()
                    {
                        Relationships = new()
                        {
                            None =
                            [
                                new()
                                {
                                    Type = new()
                                    {
                                        And = [new() { Operator = CriteriaValueOperator.Equals, Value = "person" }]
                                    }
                                }
                            ]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Exactly(1).Items);
            Assert.That(items[0].Id, Is.EqualTo(kept.Id));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsFilteredItems_ByRelationshipTypeNotEquals()
    {
        // Arrange
        var relationshipSubjectContainer24 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipSubjectContainer24);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContribution24 = new MetadataContribution
        {
            MetadataContainerId = relationshipSubjectContainer24.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Bella Hadid"
        };
        _context.MetadataContributions.Add(relationshipSubjectContribution24);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = relationshipSubjectContainer24.Id,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        });
        var relationshipContentContainer24 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipContentContainer24);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipContribution24 = new MetadataContribution
        {
            MetadataContainerId = relationshipContentContainer24.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Scene A"
        };
        relationshipContribution24.Relationships.Add(new Relationship
        {
            Kind = "performer",
            RelatedMetadataContainerId = relationshipSubjectContainer24.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(relationshipContribution24);
        var relationshipItem24 = new ContentHierarchyItem
        {
            MetadataContainerId = relationshipContentContainer24.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "perf-content",
            Name = "Scene A",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(relationshipItem24);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContainer25 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipSubjectContainer25);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContribution25 = new MetadataContribution
        {
            MetadataContainerId = relationshipSubjectContainer25.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Big Studio"
        };
        _context.MetadataContributions.Add(relationshipSubjectContribution25);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = relationshipSubjectContainer25.Id,
            Kind = "studio",
            CreatedAt = DateTime.UtcNow
        });
        var relationshipContentContainer25 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipContentContainer25);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipContribution25 = new MetadataContribution
        {
            MetadataContainerId = relationshipContentContainer25.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Scene B"
        };
        relationshipContribution25.Relationships.Add(new Relationship
        {
            Kind = "studio",
            RelatedMetadataContainerId = relationshipSubjectContainer25.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(relationshipContribution25);
        var relationshipItem25 = new ContentHierarchyItem
        {
            MetadataContainerId = relationshipContentContainer25.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "studio-content",
            Name = "Scene B",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(relationshipItem25);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var studioItem = relationshipItem25;

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new()
                    {
                        Relationships = new()
                        {
                            Some =
[
[
                                new()
                                {
                                    Type = new()
                                    {
                                        And = [new() { Operator = CriteriaValueOperator.NotEquals, Value = "person" }]
                                    }
                                }
]
]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Exactly(1).Items);
            Assert.That(items[0].Id, Is.EqualTo(studioItem.Id));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersItems_ByRelationshipTypeIsNullAndIsNotNull()
    {
        // Arrange
        var relationshipSubjectContainer26 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipSubjectContainer26);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContribution26 = new MetadataContribution
        {
            MetadataContainerId = relationshipSubjectContainer26.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Bella Hadid"
        };
        _context.MetadataContributions.Add(relationshipSubjectContribution26);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = relationshipSubjectContainer26.Id,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        });
        var relationshipContentContainer26 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipContentContainer26);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipContribution26 = new MetadataContribution
        {
            MetadataContainerId = relationshipContentContainer26.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Scene A"
        };
        relationshipContribution26.Relationships.Add(new Relationship
        {
            Kind = "performer",
            RelatedMetadataContainerId = relationshipSubjectContainer26.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(relationshipContribution26);
        var relationshipItem26 = new ContentHierarchyItem
        {
            MetadataContainerId = relationshipContentContainer26.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "materialized-content",
            Name = "Scene A",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(relationshipItem26);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var materialized = relationshipItem26;
        var bareTargetContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        bareTargetContainer.Contributions.Add(new MetadataContribution
        {
            Ordinal = 0,
            ProviderType = "test",
            Title = "Scene B Bare"
        });
        _context.MetadataContainers.Add(bareTargetContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var bareContentContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(bareContentContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var bareContribution = new MetadataContribution
        {
            MetadataContainerId = bareContentContainer.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Scene B"
        };
        bareContribution.Relationships.Add(new Relationship
        {
            Kind = "related",
            RelatedMetadataContainerId = bareTargetContainer.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(bareContribution);
        var bare = new ContentHierarchyItem
        {
            MetadataContainerId = bareContentContainer.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "bare-content",
            Name = "Scene B",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(bare);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var notNullCriteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new()
                    {
                        Relationships = new()
                        {
                            Some =
[
[
                                new()
                                {
                                    Type = new()
                                    {
                                        And = [new() { Operator = CriteriaValueOperator.IsNotNull }]
                                    }
                                }
]
]
                        }
                    }
                ]
            }
        };

        var nullCriteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new()
                    {
                        Relationships = new()
                        {
                            Some =
[
[
                                new()
                                {
                                    Type = new()
                                    {
                                        And = [new() { Operator = CriteriaValueOperator.IsNull }]
                                    }
                                }
]
]
                        }
                    }
                ]
            }
        };

        // Act
        var notNullResult = await _subject.QueryAsync(notNullCriteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);
        var nullResult = await _subject.QueryAsync(nullCriteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var notNullItems = notNullResult.Items.ToList();
        var nullItems = nullResult.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(notNullItems, Has.Exactly(1).Items);
            Assert.That(notNullItems[0].Id, Is.EqualTo(materialized.Id));
            Assert.That(nullItems, Has.Exactly(1).Items);
            Assert.That(nullItems[0].Id, Is.EqualTo(bare.Id));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsPagedItems_WhenPagingIsSet()
    {
        // Arrange
        for (var i = 0; i < 5; i++)
        {
            var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
            _context.MetadataContainers.Add(container);
            await _context.SaveChangesAsync().ConfigureAwait(false);

            var item = new ContentHierarchyItem
            {
                MetadataContainerId = container.Id,
                ContentType = "type-a",
                Level = "level-a",
                Slug = $"movie-{i}",
                Name = $"Movie {i}",
                Monitoring = true,
                CreatedAt = DateTime.UtcNow
            };
            _context.ContentHierarchyItems.Add(item);
        }

        await _context.SaveChangesAsync().ConfigureAwait(false);

        var paging = new PagingParameter { PageNumber = 1, PageSize = 2 };

        // Act
        var result = await _subject.QueryAsync(new(), paging, null).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Exactly(2).Items);
            Assert.That(result, Has.Property(nameof(result.TotalItems)).EqualTo(5));
            Assert.That(result, Has.Property(nameof(result.PageNumber)).EqualTo(1));
            Assert.That(result, Has.Property(nameof(result.PageSize)).EqualTo(2));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsSortedItems_WhenSortIsSet()
    {
        // Arrange
        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var container3 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.AddRange(container2, container3);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var item1 = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-b",
            Name = "B Movie",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        var item2 = new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-a",
            Name = "A Movie",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        var item3 = new ContentHierarchyItem
        {
            MetadataContainerId = container3.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-c",
            Name = "C Movie",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(item1, item2, item3);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var sort = new SortParameter { PropertyName = "Name", Direction = SortDirection.Ascending };

        // Act
        var result = await _subject.QueryAsync(new(),
            new() { PageNumber = 1, PageSize = 100 },
            sort).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(3));
            Assert.That(items, Has.ItemAt(0).Property(nameof(ContentHierarchyItem.Name)).EqualTo("A Movie"));
            Assert.That(items, Has.ItemAt(1).Property(nameof(ContentHierarchyItem.Name)).EqualTo("B Movie"));
            Assert.That(items, Has.ItemAt(2).Property(nameof(ContentHierarchyItem.Name)).EqualTo("C Movie"));
        }
    }

    [Test]
    public async Task QueryAsync_SortsByCamelCasePropertyName_WhenCamelCaseSortPropertyIsSet()
    {
        // Arrange
        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var container3 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.AddRange(container2, container3);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var item1 = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "first",
            Name = "First",
            Monitoring = true,
            CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        var item2 = new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "third",
            Name = "Third",
            Monitoring = true,
            CreatedAt = new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        var item3 = new ContentHierarchyItem
        {
            MetadataContainerId = container3.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "second",
            Name = "Second",
            Monitoring = true,
            CreatedAt = new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        _context.ContentHierarchyItems.AddRange(item1, item2, item3);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var sort = new SortParameter { PropertyName = "createdAt", Direction = SortDirection.Descending };

        // Act
        var result = await _subject.QueryAsync(new(),
            new() { PageNumber = 1, PageSize = 100 },
            sort).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(3));
            Assert.That(items, Has.ItemAt(0).Property(nameof(ContentHierarchyItem.Name)).EqualTo("Third"));
            Assert.That(items, Has.ItemAt(1).Property(nameof(ContentHierarchyItem.Name)).EqualTo("Second"));
            Assert.That(items, Has.ItemAt(2).Property(nameof(ContentHierarchyItem.Name)).EqualTo("First"));
        }
    }

    [Test]
    public async Task QueryAsync_FallsBackToDefaultOrder_ForUnknownSortProperty()
    {
        // Arrange
        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var item1 = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "beta",
            Name = "Beta",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        var item2 = new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "alpha",
            Name = "Alpha",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(item1, item2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var sort = new SortParameter
        {
            PropertyName = "doesNotExist",
            Direction = SortDirection.Ascending
        };

        // Act
        var result = await _subject.QueryAsync(new(),
            new() { PageNumber = 1, PageSize = 100 },
            sort).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(2));
            Assert.That(items, Has.ItemAt(0).Property(nameof(ContentHierarchyItem.Name)).EqualTo("Alpha"));
            Assert.That(items, Has.ItemAt(1).Property(nameof(ContentHierarchyItem.Name)).EqualTo("Beta"));
        }
    }

    [Test]
    public async Task QueryAsync_SortsByMaterializedMetadataPublishDate_WhenPublishDateSortIsSet()
    {
        // Arrange
        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container2.Contributions.Add(new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            PublishDate = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow
        });

        var container3 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container3.Contributions.Add(new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            PublishDate = new DateTime(2024, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow
        });

        _context.MetadataContainers.AddRange(container2, container3);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var item1 = new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "later",
            Name = "Later",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        var item2 = new ContentHierarchyItem
        {
            MetadataContainerId = container3.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "earlier",
            Name = "Earlier",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(item1, item2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var sort = new SortParameter { PropertyName = "publishDate", Direction = SortDirection.Ascending };

        // Act
        var result = await _subject.QueryAsync(new(),
            new() { PageNumber = 1, PageSize = 100 },
            sort).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(2));
            Assert.That(items, Has.ItemAt(0).Property(nameof(ContentHierarchyItem.Name)).EqualTo("Earlier"));
            Assert.That(items, Has.ItemAt(1).Property(nameof(ContentHierarchyItem.Name)).EqualTo("Later"));
        }
    }

    [Test]
    public async Task QueryAsync_SortsByMaterializedMetadataSequenceNumber_NullsLast()
    {
        // Arrange
        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container2.Contributions.Add(new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            SequenceNumber = 10,
            CreatedAt = DateTime.UtcNow
        });
        var container3 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container3.Contributions.Add(new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            SequenceNumber = 2,
            CreatedAt = DateTime.UtcNow
        });
        var container4 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container4.Contributions.Add(new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Unnumbered",
            CreatedAt = DateTime.UtcNow
        });
        _context.MetadataContainers.AddRange(container2, container3, container4);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var seasonTen = new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-c",
            Level = "level-c",
            Slug = "season-10",
            Name = "Season 10",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        var seasonTwo = new ContentHierarchyItem
        {
            MetadataContainerId = container3.Id,
            ContentType = "type-c",
            Level = "level-c",
            Slug = "season-2",
            Name = "Season 2",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        var unnumbered = new ContentHierarchyItem
        {
            MetadataContainerId = container4.Id,
            ContentType = "type-c",
            Level = "level-c",
            Slug = "special",
            Name = "Special",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(seasonTen, seasonTwo, unnumbered);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var sort = new SortParameter
        {
            PropertyName = nameof(MaterializedMetadata.SequenceNumber),
            Direction = SortDirection.Ascending
        };

        // Act
        var result = await _subject.QueryAsync(new(),
            new() { PageNumber = 1, PageSize = 100 },
            sort).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(3));
            Assert.That(items, Has.ItemAt(0).Property(nameof(ContentHierarchyItem.Name)).EqualTo("Season 2"));
            Assert.That(items, Has.ItemAt(1).Property(nameof(ContentHierarchyItem.Name)).EqualTo("Season 10"));
            Assert.That(items, Has.ItemAt(2).Property(nameof(ContentHierarchyItem.Name)).EqualTo("Special"));
        }
    }

    [Test]
    public async Task QueryAsync_SortsByContentItemDateLastSearched_WhenLastSearchedSortIsSet()
    {
        // Arrange
        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var container3 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var container4 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var container5 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.AddRange(container2, container3, container4, container5);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var item1 = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "middle",
            Name = "Middle",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        var item2 = new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "latest",
            Name = "Latest",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        var item3 = new ContentHierarchyItem
        {
            MetadataContainerId = container3.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "earliest",
            Name = "Earliest",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        var item4 = new ContentHierarchyItem
        {
            MetadataContainerId = container4.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "no-content-item",
            Name = "No Content Item",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        var item5 = new ContentHierarchyItem
        {
            MetadataContainerId = container5.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "unsearched",
            Name = "Unsearched",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(item1, item2, item3, item4, item5);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        _context.ContentItems.AddRange(
            new ContentItem
            {
                ContentHierarchyItemId = item1.Id,
                Fulfillment = FulfillmentStatus.Missing,
                DateLastSearched = new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                CreatedAt = DateTime.UtcNow
            },
            new ContentItem
            {
                ContentHierarchyItemId = item2.Id,
                Fulfillment = FulfillmentStatus.Missing,
                DateLastSearched = new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                CreatedAt = DateTime.UtcNow
            },
            new ContentItem
            {
                ContentHierarchyItemId = item3.Id,
                Fulfillment = FulfillmentStatus.Missing,
                DateLastSearched = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                CreatedAt = DateTime.UtcNow
            },
            new ContentItem
            {
                ContentHierarchyItemId = item5.Id,
                Fulfillment = FulfillmentStatus.Missing,
                DateLastSearched = null,
                CreatedAt = DateTime.UtcNow
            });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var sort = new SortParameter
        {
            PropertyName = "dateLastSearched",
            Direction = SortDirection.Descending
        };

        // Act
        var result = await _subject.QueryAsync(new(),
            new() { PageNumber = 1, PageSize = 100 },
            sort).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(5));
            Assert.That(items, Has.ItemAt(0).Property(nameof(ContentHierarchyItem.Name)).EqualTo("Latest"));
            Assert.That(items, Has.ItemAt(1).Property(nameof(ContentHierarchyItem.Name)).EqualTo("Middle"));
            Assert.That(items, Has.ItemAt(2).Property(nameof(ContentHierarchyItem.Name)).EqualTo("Earliest"));
            Assert.That(items, Has.ItemAt(3).Property(nameof(ContentHierarchyItem.Name)).EqualTo("No Content Item"));
            Assert.That(items, Has.ItemAt(4).Property(nameof(ContentHierarchyItem.Name)).EqualTo("Unsearched"));
        }
    }

    [TestCase(SortDirection.Ascending)]
    [TestCase(SortDirection.Descending)]
    public async Task QueryAsync_SortsByContentItemDateLastSearched_UsesIdAsFinalTieBreaker(SortDirection direction)
    {
        // Arrange
        var searchedLowerId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var searchedHigherId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var unsearchedLowerId = Guid.Parse("00000000-0000-0000-0000-000000000003");
        var unsearchedHigherId = Guid.Parse("00000000-0000-0000-0000-000000000004");

        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var container3 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var container4 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.AddRange(container2, container3, container4);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var searchedHigher = new ContentHierarchyItem
        {
            Id = searchedHigherId,
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "searched-higher",
            Name = "Duplicate",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        var searchedLower = new ContentHierarchyItem
        {
            Id = searchedLowerId,
            MetadataContainerId = container2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "searched-lower",
            Name = "Duplicate",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        var unsearchedHigher = new ContentHierarchyItem
        {
            Id = unsearchedHigherId,
            MetadataContainerId = container3.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "unsearched-higher",
            Name = "Duplicate",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        var unsearchedLower = new ContentHierarchyItem
        {
            Id = unsearchedLowerId,
            MetadataContainerId = container4.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "unsearched-lower",
            Name = "Duplicate",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(searchedHigher, searchedLower, unsearchedHigher, unsearchedLower);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var searchedAt = new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        _context.ContentItems.AddRange(
            new ContentItem
            {
                ContentHierarchyItemId = searchedHigher.Id,
                Fulfillment = FulfillmentStatus.Missing,
                DateLastSearched = searchedAt,
                CreatedAt = DateTime.UtcNow
            },
            new ContentItem
            {
                ContentHierarchyItemId = searchedLower.Id,
                Fulfillment = FulfillmentStatus.Missing,
                DateLastSearched = searchedAt,
                CreatedAt = DateTime.UtcNow
            },
            new ContentItem
            {
                ContentHierarchyItemId = unsearchedHigher.Id,
                Fulfillment = FulfillmentStatus.Missing,
                DateLastSearched = null,
                CreatedAt = DateTime.UtcNow
            },
            new ContentItem
            {
                ContentHierarchyItemId = unsearchedLower.Id,
                Fulfillment = FulfillmentStatus.Missing,
                DateLastSearched = null,
                CreatedAt = DateTime.UtcNow
            });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var sort = new SortParameter
        {
            PropertyName = "dateLastSearched",
            Direction = direction
        };

        // Act
        var result = await _subject.QueryAsync(new(),
            new() { PageNumber = 1, PageSize = 100 },
            sort).ConfigureAwait(false);

        // Assert
        var ids = result.Items.Select(i => i.Id).ToList();
        Assert.That(ids, Is.EqualTo([searchedLowerId, searchedHigherId, unsearchedLowerId, unsearchedHigherId]));
    }

    [Test]
    public async Task QueryAsync_ReturnsFilteredItems_ByName()
    {
        // Arrange
        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var item1 = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "action-movie",
            Name = "Action Movie",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        var item2 = new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "comedy-movie",
            Name = "Comedy Movie",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(item1, item2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            Name = new()
            {
                And = [new() { Value = "Action", Operator = CriteriaValueOperator.ContainsString }]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Exactly(1).Items);
            Assert.That(items, Has.One.With.Property(nameof(ContentHierarchyItem.Name)).EqualTo("Action Movie"));
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
            Monitoring = true,
            CreatedAt = DateTime.UtcNow,
            MetadataContainer = await _context.MetadataContainers.FindAsync(_metadataContainerId).ConfigureAwait(false)
        };

        _context.ContentHierarchyItems.Add(item);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new ContentHierarchyItemPatch
        {
            Id = item.Id,
            Monitoring = new() { Value = false }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);

        var updated = await _subject.GetByIdAsync(item.Id).ConfigureAwait(false);
        Assert.That(updated, Has.Property(nameof(updated.Monitoring)).EqualTo(false));
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
            Monitoring = true,
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
            Monitoring = true,
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
            Monitoring = true,
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
            Monitoring = true,
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
    public async Task GetByIdAsync_ReturnsOrderedFiles_WhenAudiobookHasSeveralFiles()
    {
        // Arrange
        var item = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainerId = _metadataContainerId,
            ContentType = "book",
            Level = "audiobook",
            Slug = "multi-file-audiobook",
            Name = "Multi-file Audiobook",
            CreatedAt = DateTime.UtcNow
        };
        var contentItem = new ContentItem
        {
            ContentHierarchyItemId = item.Id,
            CreatedAt = DateTime.UtcNow
        };
        contentItem.MediaFiles.Add(new MediaFile
        {
            FileKind = "audio",
            MimeType = "audio/mpeg",
            FilePath = "/library/audiobooks/book/10 - Chapter 10.mp3",
            CreatedAt = DateTime.UtcNow
        });
        contentItem.MediaFiles.Add(new MediaFile
        {
            FileKind = "audio",
            MimeType = "audio/mpeg",
            FilePath = "/library/audiobooks/book/2 - Chapter 2.mp3",
            CreatedAt = DateTime.UtcNow
        });
        contentItem.MediaFiles.Add(new MediaFile
        {
            FileKind = "audio",
            MimeType = "audio/mpeg",
            FilePath = "/library/audiobooks/book/1 - Chapter 1.mp3",
            CreatedAt = DateTime.UtcNow
        });
        _context.ContentHierarchyItems.Add(item);
        _context.ContentItems.Add(contentItem);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.GetByIdAsync(item.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.ContentItem, Is.Not.Null);
            Assert.That(
                result.ContentItem!.MediaFiles.Select(file => file.FilePath),
                Is.EqualTo(
                [
                    "/library/audiobooks/book/1 - Chapter 1.mp3",
                    "/library/audiobooks/book/2 - Chapter 2.mp3",
                    "/library/audiobooks/book/10 - Chapter 10.mp3"
                ]));
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
            Monitoring = true,
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
            Monitoring = new() { Value = false }
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
            Monitoring = true,
            CreatedAt = DateTime.UtcNow,
            MetadataContainer = await _context.MetadataContainers.FindAsync(_metadataContainerId).ConfigureAwait(false)
        };

        _context.ContentHierarchyItems.Add(item);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new ContentHierarchyItemPatch
        {
            Id = item.Id,
            Monitoring = new() { Value = false }
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
            Monitoring = true,
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
            Monitoring = true,
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
            Monitoring = true,
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
    public async Task QueryAsync_ReturnsFilteredItems_BySlug()
    {
        // Arrange
        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var item1 = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "specific-slug",
            Name = "Specific Slug Movie",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        var item2 = new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "other-slug",
            Name = "Other Slug Movie",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(item1, item2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            Slug = new()
            {
                And = [new() { Value = "specific-slug" }]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Exactly(1).Items);
            Assert.That(items, Has.One.With.Property(nameof(ContentHierarchyItem.Slug)).EqualTo("specific-slug"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsFilteredItems_ByPath()
    {
        // Arrange
        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var item1 = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "path-movie-1",
            Name = "Path Movie 1",
            Monitoring = true,
            Path = "/movies/action/test",
            CreatedAt = DateTime.UtcNow
        };
        var item2 = new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "path-movie-2",
            Name = "Path Movie 2",
            Monitoring = true,
            Path = "/movies/comedy/test",
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(item1, item2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            Path = new()
            {
                And = [new() { Value = "action", Operator = CriteriaValueOperator.ContainsString }]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Exactly(1).Items);
            Assert.That(items, Has.One.With.Property(nameof(ContentHierarchyItem.Path)).Contains("action"));
        }
    }

    [Test]
    public async Task GetStatsAsync_ReturnsZeroes_WhenLibraryIsEmpty()
    {
        // Act
        var stats = await _subject.GetStatsAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stats, Has.Property(nameof(stats.Total)).EqualTo(0));
            Assert.That(stats, Has.Property(nameof(stats.Missing)).EqualTo(0));
            Assert.That(stats, Has.Property(nameof(stats.Upgradable)).EqualTo(0));
            Assert.That(stats, Has.Property(nameof(stats.Fulfilled)).EqualTo(0));
            Assert.That(stats, Has.Property(nameof(stats.FulfilledExternally)).EqualTo(0));
            Assert.That(stats, Has.Property(nameof(stats.Partial)).EqualTo(0));
        }
    }

    [Test]
    public async Task GetStatsAsync_AggregatesAcrossEntireLibrary_WhenLibraryHasItems()
    {
        // Arrange
        var fulfillmentByIndex = new[]
        {
            null,
            (FulfillmentStatus?)null,
            FulfillmentStatus.Missing,
            FulfillmentStatus.Upgradable,
            FulfillmentStatus.Upgradable,
            FulfillmentStatus.Upgradable,
            FulfillmentStatus.Fulfilled,
            FulfillmentStatus.Fulfilled,
            FulfillmentStatus.Fulfilled,
            FulfillmentStatus.Fulfilled,
            FulfillmentStatus.FulfilledExternally,
            FulfillmentStatus.Partial,
            FulfillmentStatus.Partial
        };

        for (var i = 0; i < fulfillmentByIndex.Length; i++)
        {
            var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
            _context.MetadataContainers.Add(container);
            await _context.SaveChangesAsync().ConfigureAwait(false);

            var hierarchy = new ContentHierarchyItem
            {
                MetadataContainerId = container.Id,
                ContentType = "type-a",
                Level = "level-a",
                Slug = $"movie-{i}",
                Name = $"Movie {i}",
                Monitoring = true,
                CreatedAt = DateTime.UtcNow
            };
            _context.ContentHierarchyItems.Add(hierarchy);
            await _context.SaveChangesAsync().ConfigureAwait(false);

            if (fulfillmentByIndex[i] is { } status)
            {
                _context.ContentItems.Add(new ContentItem
                {
                    ContentHierarchyItemId = hierarchy.Id,
                    Fulfillment = status,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var stats = await _subject.GetStatsAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stats, Has.Property(nameof(stats.Total)).EqualTo(13));
            Assert.That(stats, Has.Property(nameof(stats.Missing)).EqualTo(3));
            Assert.That(stats, Has.Property(nameof(stats.Upgradable)).EqualTo(3));
            Assert.That(stats, Has.Property(nameof(stats.Fulfilled)).EqualTo(4));
            Assert.That(stats, Has.Property(nameof(stats.FulfilledExternally)).EqualTo(1));
            Assert.That(stats, Has.Property(nameof(stats.Partial)).EqualTo(2));
        }
    }

    [Test]
    public async Task GetLineageAsync_ReturnsLeafToRoot_WhenLineageExists()
    {
        // Arrange
        var rootContainer = new MetadataContainer { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow };
        var parentContainer = new MetadataContainer { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow };
        var leafContainer = new MetadataContainer { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow };
        var root = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainerId = rootContainer.Id,
            ContentType = "test",
            Level = "level",
            Slug = "root",
            Name = "root",
            Monitoring = null,
            CreatedAt = DateTime.UtcNow
        };
        var parent = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainerId = parentContainer.Id,
            ParentId = root.Id,
            ContentType = "test",
            Level = "level",
            Slug = "parent",
            Name = "parent",
            Monitoring = null,
            CreatedAt = DateTime.UtcNow
        };
        var leaf = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainerId = leafContainer.Id,
            ParentId = parent.Id,
            ContentType = "test",
            Level = "level",
            Slug = "leaf",
            Name = "leaf",
            Monitoring = null,
            CreatedAt = DateTime.UtcNow
        };
        _context.MetadataContainers.AddRange(rootContainer, parentContainer, leafContainer);
        _context.ContentHierarchyItems.AddRange(root, parent, leaf);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        // Act
        var nodes = await _subject.GetLineageAsync(leaf.Id).ConfigureAwait(false);

        // Assert
        Assert.That(nodes.Select(n => n.Id), Is.EqualTo([leaf.Id, parent.Id, root.Id]));
    }

    [Test]
    public async Task GetLineageAsync_StopsBeforeRepeat_WhenCycleExists()
    {
        // Arrange
        var rootContainer = new MetadataContainer { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow };
        var leafContainer = new MetadataContainer { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow };
        var root = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainerId = rootContainer.Id,
            ContentType = "test",
            Level = "level",
            Slug = "root",
            Name = "root",
            Monitoring = null,
            CreatedAt = DateTime.UtcNow
        };
        var leaf = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainerId = leafContainer.Id,
            ParentId = root.Id,
            ContentType = "test",
            Level = "level",
            Slug = "leaf",
            Name = "leaf",
            Monitoring = null,
            CreatedAt = DateTime.UtcNow
        };
        _context.MetadataContainers.AddRange(rootContainer, leafContainer);
        _context.ContentHierarchyItems.AddRange(root, leaf);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        root.ParentId = leaf.Id;
        _context.ContentHierarchyItems.Update(root);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        // Act
        var nodes = await _subject.GetLineageAsync(leaf.Id).ConfigureAwait(false);

        // Assert
        Assert.That(nodes.Select(n => n.Id), Is.EqualTo([leaf.Id, root.Id]));
    }

    [Test]
    public async Task GetLineageAsync_StopsBeforeMissingAncestor_WhenAncestorMissing()
    {
        // Arrange
        var container = new MetadataContainer { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow };
        var leaf = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainerId = container.Id,
            ContentType = "test",
            Level = "level",
            Slug = "leaf",
            Name = "leaf",
            Monitoring = null,
            CreatedAt = DateTime.UtcNow
        };
        _context.MetadataContainers.Add(container);
        _context.ContentHierarchyItems.Add(leaf);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var missingParentId = Guid.NewGuid();
        await _context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF").ConfigureAwait(false);
        await _context.Database
            .ExecuteSqlInterpolatedAsync(
                $"UPDATE content_hierarchyItem SET ParentId = {missingParentId} WHERE Id = {leaf.Id}")
            .ConfigureAwait(false);
        await _context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON").ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        // Act
        var nodes = await _subject.GetLineageAsync(leaf.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(nodes.Select(n => n.Id), Is.EqualTo([leaf.Id]));
            Assert.That(nodes[0].ParentId, Is.EqualTo(missingParentId));
        }
    }

    [Test]
    public async Task DeleteAsync_ClearsDownloadScopes_WhenSubtreeIsScoped()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var seasonContainer = new MetadataContainer { CreatedAt = now };
        var episodeContainer = new MetadataContainer { CreatedAt = now };
        _context.MetadataContainers.AddRange(seasonContainer, episodeContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var season = new ContentHierarchyItem
        {
            MetadataContainerId = seasonContainer.Id,
            ContentType = "tv",
            Level = "season",
            Slug = "s1",
            Name = "Season 1",
            CreatedAt = now
        };
        _context.ContentHierarchyItems.Add(season);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var episode = new ContentHierarchyItem
        {
            MetadataContainerId = episodeContainer.Id,
            ParentId = season.Id,
            ContentType = "tv",
            Level = "episode",
            Slug = "s1e1",
            Name = "Episode 1",
            CreatedAt = now
        };
        _context.ContentHierarchyItems.Add(episode);
        var client = new DownloadClient
        {
            Name = "client",
            ProviderType = "downloadclient-a",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            CreatedAt = now
        };
        _context.DownloadClients.Add(client);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var download = new Download
        {
            DownloadClientId = client.Id,
            ExternalId = string.Empty,
            Title = "season pack",
            Status = DownloadStatus.Downloading,
            CreatedAt = now
        };
        _context.Downloads.Add(download);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.DownloadScopes.Add(new DownloadScope
        {
            DownloadId = download.Id,
            ContentHierarchyItemId = episode.Id
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        // Act
        var deleted = await _subject.DeleteAsync(season.Id, _userId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleted, Is.True);
            Assert.That(
                await _context.DownloadScopes.AnyAsync(c => c.ContentHierarchyItemId == episode.Id)
                    .ConfigureAwait(false),
                Is.False);
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsOnlyTopmostMonitoredNode_ByHasMonitoredParent()
    {
        // Arrange
        var showContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var seasonContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.AddRange(showContainer, seasonContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var show = new ContentHierarchyItem
        {
            MetadataContainerId = showContainer.Id,
            ContentType = "tv",
            Level = "show",
            Slug = "show",
            Name = "Show",
            EffectiveMonitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(show);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var season = new ContentHierarchyItem
        {
            MetadataContainerId = seasonContainer.Id,
            ParentId = show.Id,
            ContentType = "tv",
            Level = "season",
            Slug = "s1",
            Name = "Season 1",
            EffectiveMonitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(season);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject
            .QueryAsync(
                new ContentHierarchyItemCriteria { EffectiveMonitoring = true, HasMonitoredParent = false },
                new() { PageNumber = 1, PageSize = 100 },
                null)
            .ConfigureAwait(false);

        // Assert
        Assert.That(result.Items.Select(i => i.Id), Is.EquivalentTo([show.Id]));
    }

    [Test]
    public async Task QueryAsync_ReturnsOverrideBoundary_ByHasMonitoredParent()
    {
        // Arrange
        var showContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var seasonContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.AddRange(showContainer, seasonContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var unmonitoredShow = new ContentHierarchyItem
        {
            MetadataContainerId = showContainer.Id,
            ContentType = "tv",
            Level = "show",
            Slug = "show",
            Name = "Show",
            EffectiveMonitoring = false,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(unmonitoredShow);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var monitoredSeason = new ContentHierarchyItem
        {
            MetadataContainerId = seasonContainer.Id,
            ParentId = unmonitoredShow.Id,
            ContentType = "tv",
            Level = "season",
            Slug = "s1",
            Name = "Season 1",
            EffectiveMonitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(monitoredSeason);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject
            .QueryAsync(
                new ContentHierarchyItemCriteria { EffectiveMonitoring = true, HasMonitoredParent = false },
                new() { PageNumber = 1, PageSize = 100 },
                null)
            .ConfigureAwait(false);

        // Assert
        Assert.That(result.Items.Select(i => i.Id), Is.EquivalentTo([monitoredSeason.Id]));
    }

    [Test]
    public async Task QueryAsync_ReturnsMonitoredDescendant_ByHasMonitoredParent()
    {
        // Arrange
        var showContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var seasonContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.AddRange(showContainer, seasonContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var show = new ContentHierarchyItem
        {
            MetadataContainerId = showContainer.Id,
            ContentType = "tv",
            Level = "show",
            Slug = "show",
            Name = "Show",
            EffectiveMonitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(show);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var season = new ContentHierarchyItem
        {
            MetadataContainerId = seasonContainer.Id,
            ParentId = show.Id,
            ContentType = "tv",
            Level = "season",
            Slug = "s1",
            Name = "Season 1",
            EffectiveMonitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(season);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject
            .QueryAsync(new ContentHierarchyItemCriteria { HasMonitoredParent = true },
                new() { PageNumber = 1, PageSize = 100 },
                null)
            .ConfigureAwait(false);

        // Assert
        Assert.That(result.Items.Select(i => i.Id), Is.EquivalentTo([season.Id]));
    }

    [Test]
    public async Task QueryAsync_ReturnsEpisodeRowsOnly_WhenLevelInCriteriaAccompaniesAncestorAndLeafOnly()
    {
        // Arrange
        var showContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var childlessSeasonContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var seasonContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var firstEpisodeContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var secondEpisodeContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.AddRange(
            showContainer,
            childlessSeasonContainer,
            seasonContainer,
            firstEpisodeContainer,
            secondEpisodeContainer);
        var show = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainer = showContainer,
            ContentType = "tv",
            Level = "show",
            Slug = "provider-a-1438",
            Name = "The Wire",
            CreatedAt = DateTime.UtcNow
        };
        var childlessSeason = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainer = childlessSeasonContainer,
            ParentId = show.Id,
            ContentType = "tv",
            Level = "season",
            Slug = "season-1",
            Name = "Season 1",
            Ordinal = 1,
            CreatedAt = DateTime.UtcNow
        };
        var season = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainer = seasonContainer,
            ParentId = show.Id,
            ContentType = "tv",
            Level = "season",
            Slug = "season-2",
            Name = "Season 2",
            Ordinal = 2,
            CreatedAt = DateTime.UtcNow
        };
        var firstEpisode = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainer = firstEpisodeContainer,
            ParentId = season.Id,
            ContentType = "tv",
            Level = "episode",
            Slug = "s02e01",
            Name = "Ebb Tide",
            Ordinal = 1,
            CreatedAt = DateTime.UtcNow
        };
        var secondEpisode = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainer = secondEpisodeContainer,
            ParentId = season.Id,
            ContentType = "tv",
            Level = "episode",
            Slug = "s02e02",
            Name = "Collateral Damage",
            Ordinal = 2,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(show, childlessSeason, season, firstEpisode, secondEpisode);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(
            new()
            {
                AncestorId = show.Id,
                LeafOnly = true,
                Level = new CriteriaSet<PropertyCriterion<string>>
                {
                    And =
                    [
                        new PropertyCriterion<string>
                        {
                            Operator = CriteriaValueOperator.In,
                            Values = ["episode"]
                        }
                    ]
                }
            },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Items.Select(item => item.Id), Is.EquivalentTo([firstEpisode.Id, secondEpisode.Id]));
            Assert.That(result.Items.Select(item => item.Slug), Is.EquivalentTo(["s02e01", "s02e02"]));
            Assert.That(result.TotalItems, Is.EqualTo(2));
        }
    }

    [Test]
    public async Task GetByIdAsync_IncludesDetailMetadataCollections_WhenReadingOneItem()
    {
        // Arrange
        var contribution = new MetadataContribution
        {
            MetadataContainerId = _metadataContainerId,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Detailed Movie",
            CreatedAt = DateTime.UtcNow
        };
        _context.MetadataContributions.Add(contribution);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        _context.Genres.Add(new Genre
        {
            MetadataContributionId = contribution.Id,
            Ordinal = 0,
            Name = "Drama"
        });
        _context.ExternalIds.Add(new ExternalId
        {
            MetadataContributionId = contribution.Id,
            Ordinal = 0,
            Source = "tmdb",
            Value = "42"
        });

        var item = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "detailed-movie",
            Name = "Detailed Movie",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(item);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var contentItem = new ContentItem
        {
            ContentHierarchyItemId = item.Id,
            Fulfillment = FulfillmentStatus.Fulfilled,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentItems.Add(contentItem);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        _context.MediaFiles.Add(new MediaFile
        {
            ContentItemId = contentItem.Id,
            FileKind = "Video",
            FilePath = "/movies/detailed-movie/detailed.mkv",
            FileSize = 1024,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.GetByIdAsync(item.Id).ConfigureAwait(false);

        // Assert
        var detailedContribution = result!.MetadataContainer!.Contributions.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(detailedContribution.Genres, Has.Exactly(1).Items);
            Assert.That(detailedContribution.ExternalIds, Has.Exactly(1).Items);
            Assert.That(result.ContentItem!.MediaFiles, Has.Exactly(1).Items);
        }
    }
}
