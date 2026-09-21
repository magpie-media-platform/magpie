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

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public sealed class ContentHierarchyItemRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IContentHierarchyItemRepository _subject = null!;
    private Guid _metadataContainerId;
    private Guid _userId;
    private ITransactionManager _transactionManager = null!;
    private Mock<IEventBus> _eventBus = null!;
    private List<MagpieEvent> _publishedEvents = null!;
    private Mock<IContentTypeRegistry> _contentTypeRegistry = null!;

    [SetUp]
    public void SetUp()
    {
        _context = MagpieDbContextFactory.Create("Data Source=:memory:", [], []);
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
        var metadataContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(metadataContainer);
        _context.SaveChanges();
        _metadataContainerId = metadataContainer.Id;
        _eventBus = new Mock<IEventBus>();
        _publishedEvents = [];
        _eventBus
            .Setup(bus => bus.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()))
            .Callback((MagpieEvent magpieEvent, CancellationToken _) => _publishedEvents.Add(magpieEvent))
            .Returns(Task.CompletedTask);
        _contentTypeRegistry = new Mock<IContentTypeRegistry>();
        _transactionManager = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);
        var auditRecords = new AuditRecordRepository(
            _context, _eventBus.Object, NullLogger<AuditRecordRepository>.Instance,
            _transactionManager, Mock.Of<IJobExecutionScopeFactory>(), TimeProvider.System);
        _subject = new ContentHierarchyItemRepository(
            _context, auditRecords, _transactionManager,
            Mock.Of<IMetadataContainerRepository>(),
            _contentTypeRegistry.Object, _eventBus.Object,
            NullLogger<ContentHierarchyItemRepository>.Instance, TimeProvider.System);
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
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
    public async Task TryCreateAsync_CreatesRootWithMetadataContainer_WhenTheTransactionSucceeds()
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
            Slug = "item-a",
            Name = "Item A",
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
    public async Task TryCreateAsync_ReturnsFalseAndWritesNothing_WhenUniqueKeyExists()
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
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var rowsAfterSave = await _context.ContentHierarchyItems.AsNoTracking().CountAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.False);
            Assert.That(keyRows, Is.EqualTo(1));
            Assert.That(rowsAfterSave, Is.EqualTo(1));
            Assert.That(loserAuditExists, Is.False);
            Assert.That(_publishedEvents, Is.Empty);
        }
    }

    [Test]
    public async Task TryCreateAsync_DefersCreatedEvent_WhenTheOuterTransactionIsOpen()
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
        var createdDuringTransaction = -1;
        await _transactionManager.ExecuteWithTransactionAsync(
            async cancellationToken =>
            {
                await _subject.TryCreateAsync(item, _userId, cancellationToken).ConfigureAwait(false);
                createdDuringTransaction = _publishedEvents
                    .Count(magpieEvent => magpieEvent.Name == "content.hierarchyItem.created");
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(createdDuringTransaction, Is.Zero);
            Assert.That(
                _publishedEvents.Count(magpieEvent => magpieEvent.Name == "content.hierarchyItem.created"),
                Is.EqualTo(1));
        }
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
            Slug = "item-a",
            Name = "Item A",
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
            NullLoggerFactory.Instance.CreateLogger<ContentHierarchyItemRepository>(),
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));

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
}
