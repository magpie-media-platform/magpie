using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions.Data;
using Magpie.Core.Data;
using Magpie.Data.Database;
using Magpie.Models;
using Magpie.Models.Content;
using Magpie.Models.Filters;
using Magpie.Models.Filters.Content;
using Magpie.Models.Result;
using Magpie.Models.Users;

using Magpie.Test.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NUnit.Framework;

using ContentHierarchyLevelEntity = Magpie.Data.Database.Entities.ContentHierarchyLevel;
using ContentItemEntity = Magpie.Data.Database.Entities.ContentItem;
using HistoryEntryEntity = Magpie.Data.Database.Entities.HistoryEntry;
using RootFolderEntity = Magpie.Data.Database.Entities.RootFolder;
using UserEntity = Magpie.Data.Database.Entities.User;

namespace Magpie.Test.Core.Data;

[TestFixture]
public class ContentHierarchyLevelRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IContentHierarchyLevelRepository _subject = null!;
    private Guid _userId;
    private Guid _rootFolderId;
    private Guid _definitionId;

    [SetUp]
    public void SetUp()
    {
        _context = new(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options);

        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();

        var user = new UserEntity
        {
            Username = "TestUser",
            Enabled = true,
            Role = Role.Admin,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _context.Users.Add(user);

        var rootFolder = new RootFolderEntity
        {
            Name = "TestRootFolder",
            Path = "/media/test",
            ContentTypeId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        _context.RootFolders.Add(rootFolder);
        _context.SaveChanges();

        _userId = user.Id;
        _rootFolderId = rootFolder.Id;
        _definitionId = Guid.NewGuid();

        var loggerFactory = LoggerFactory.Create(b => b.AddTestLogger());
        var historyRepo = new HistoryRepository(_context, loggerFactory.CreateLogger<HistoryRepository>());
        var materializedMetadataRepo = new MaterializedMetadataRepository(
            _context,
            loggerFactory.CreateLogger<MaterializedMetadataRepository>());

        _subject = new ContentHierarchyLevelRepository(
            _context,
            historyRepo,
            materializedMetadataRepo,
            loggerFactory.CreateLogger<ContentHierarchyLevelRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context?.Dispose();
    }

    private ContentHierarchyLevelEntity CreateTestEntity(string name = "TestLevel", string slug = "test-level")
    {
        return new ContentHierarchyLevelEntity
        {
            ContentHierarchyLevelDefinitionId = _definitionId,
            RootFolderId = _rootFolderId,
            Name = name,
            Slug = slug,
            RelativePath = "/test/path",
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    [Test]
    public async Task GetByIdAsync_ReturnsContentHierarchyLevel()
    {
        // Arrange
        var entity = CreateTestEntity();
        _context.ContentHierarchyLevels.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByIdAsync(entity.Id);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<ContentHierarchyLevel?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(ContentHierarchyLevel.Name))
                .EqualTo("TestLevel"));
    }

    [Test]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        // Act
        var result = await _subject.GetByIdAsync(Guid.NewGuid());

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<ContentHierarchyLevel?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task GetByIdAsync_IncludesLanguagePreferences()
    {
        // Arrange
        var entity = CreateTestEntity();
        entity.LanguagePreferences.Add(new()
        {
            Ordinal = 0,
            Language = "en"
        });
        entity.LanguagePreferences.Add(new()
        {
            Ordinal = 1,
            Language = "ja"
        });
        _context.ContentHierarchyLevels.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByIdAsync(entity.Id);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<ContentHierarchyLevel?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(ContentHierarchyLevel.LanguagePreferences))
                .Count.EqualTo(2));
    }

    [Test]
    public async Task GetAllAsync_ReturnsAllContentHierarchyLevels()
    {
        // Arrange
        var entity1 = CreateTestEntity("Level1", "level-1");
        var entity2 = CreateTestEntity("Level2", "level-2");
        _context.ContentHierarchyLevels.AddRange(entity1, entity2);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetAllAsync();

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<ContentHierarchyLevel>>>()
            .With.Property(nameof(SuccessResult<>.Value)).With.Count.EqualTo(2));
    }

    [Test]
    public async Task GetAllAsync_ReturnsEmptyList_WhenNoLevels()
    {
        // Act
        var result = await _subject.GetAllAsync();

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<ContentHierarchyLevel>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task GetBySlugAsync_ReturnsContentHierarchyLevel()
    {
        // Arrange
        var entity = CreateTestEntity("UniqueLevel", "unique-slug");
        _context.ContentHierarchyLevels.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetBySlugAsync("unique-slug");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<ContentHierarchyLevel?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(ContentHierarchyLevel.Name))
                .EqualTo("UniqueLevel"));
    }

    [Test]
    public async Task GetBySlugAsync_ReturnsNull_WhenNotFound()
    {
        // Act
        var result = await _subject.GetBySlugAsync("nonexistent-slug");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<ContentHierarchyLevel?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task GetByRootFolderIdAsync_ReturnsContentHierarchyLevels()
    {
        // Arrange
        var entity1 = CreateTestEntity("Level1", "level-1");
        var entity2 = CreateTestEntity("Level2", "level-2");
        _context.ContentHierarchyLevels.AddRange(entity1, entity2);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByRootFolderIdAsync(_rootFolderId);

        // Assert
        var success = result as SuccessResult<IReadOnlyList<ContentHierarchyLevel>>;
        Assert.That(success, Is.Not.Null);
        Assert.That(success!.Value, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task GetByRootFolderIdAsync_ReturnsEmptyList_WhenNoMatches()
    {
        // Act
        var result = await _subject.GetByRootFolderIdAsync(Guid.NewGuid());

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<ContentHierarchyLevel>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task GetByParentIdAsync_ReturnsChildLevels()
    {
        // Arrange
        var parentEntity = CreateTestEntity("Parent", "parent");
        _context.ContentHierarchyLevels.Add(parentEntity);
        _context.SaveChanges();

        var childEntity1 = CreateTestEntity("Child1", "child-1");
        childEntity1.ParentId = parentEntity.Id;
        var childEntity2 = CreateTestEntity("Child2", "child-2");
        childEntity2.ParentId = parentEntity.Id;
        _context.ContentHierarchyLevels.AddRange(childEntity1, childEntity2);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByParentIdAsync(parentEntity.Id);

        // Assert
        var success = result as SuccessResult<IReadOnlyList<ContentHierarchyLevel>>;
        Assert.That(success, Is.Not.Null);
        Assert.That(success!.Value, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task GetByParentIdAsync_ReturnsRootLevels_WhenParentIdIsNull()
    {
        // Arrange
        var rootEntity = CreateTestEntity("Root", "root");
        _context.ContentHierarchyLevels.Add(rootEntity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByParentIdAsync(null);

        // Assert
        var success = result as SuccessResult<IReadOnlyList<ContentHierarchyLevel>>;
        Assert.That(success, Is.Not.Null);
        Assert.That(success!.Value, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task GetByDefinitionIdAsync_ReturnsContentHierarchyLevels()
    {
        // Arrange
        var entity1 = CreateTestEntity("Level1", "level-1");
        var entity2 = CreateTestEntity("Level2", "level-2");
        _context.ContentHierarchyLevels.AddRange(entity1, entity2);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByDefinitionIdAsync(_definitionId);

        // Assert
        var success = result as SuccessResult<IReadOnlyList<ContentHierarchyLevel>>;
        Assert.That(success, Is.Not.Null);
        Assert.That(success!.Value, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task CreateAsync_CreatesContentHierarchyLevel()
    {
        // Arrange
        var model = new ContentHierarchyLevel(
            Guid.Empty,
            _definitionId,
            _rootFolderId,
            "NewLevel",
            "new-level",
            "/new/path",
            null,
            null,
            null,
            new(Guid.Empty, DateTimeOffset.MinValue, []),
            [],
            [],
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.CreateAsync(model, _userId);

        // Assert
        var entity = await (
            from c in _context.ContentHierarchyLevels.AsNoTracking()
            where c.Name == "NewLevel"
            select c).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<ContentHierarchyLevel>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(ContentHierarchyLevel.Name))
                    .EqualTo("NewLevel"));
            Assert.That(entity, Is.InstanceOf<ContentHierarchyLevelEntity>()
                .With.Property(nameof(ContentHierarchyLevelEntity.Name)).EqualTo("NewLevel")
                .And.Property(nameof(ContentHierarchyLevelEntity.CreatedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Create")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("ContentHierarchyLevel")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesWithLanguagePreferences()
    {
        // Arrange
        var model = new ContentHierarchyLevel(
            Guid.Empty,
            _definitionId,
            _rootFolderId,
            "LevelWithLang",
            "level-with-lang",
            "/lang/path",
            null,
            null,
            null,
            new(Guid.Empty, DateTimeOffset.MinValue, []),
            [
                new(Guid.Empty, 0, "en"),
                new(Guid.Empty, 1, "ja")
            ],
            [],
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.CreateAsync(model, _userId);

        // Assert
        var entity = await (
            from c in _context.ContentHierarchyLevels.AsNoTracking().Include(c => c.LanguagePreferences)
            where c.Name == "LevelWithLang"
            select c).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<ContentHierarchyLevel>>()
                .With.Property(nameof(SuccessResult<>.Value))
                    .Property(nameof(ContentHierarchyLevel.LanguagePreferences)).Count.EqualTo(2));
            Assert.That(entity, Is.Not.Null);
            Assert.That(entity!.LanguagePreferences, Has.Count.EqualTo(2));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesContentHierarchyLevel()
    {
        // Arrange
        var entity = CreateTestEntity();
        _context.ContentHierarchyLevels.Add(entity);
        _context.SaveChanges();

        var model = new ContentHierarchyLevel(
            entity.Id,
            entity.ContentHierarchyLevelDefinitionId,
            entity.RootFolderId,
            "UpdatedName",
            "updated-slug",
            "/updated/path",
            null,
            null,
            DateTimeOffset.UtcNow,
            new(Guid.Empty, DateTimeOffset.MinValue, []),
            [],
            [],
            entity.CreatedAt,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        var updatedEntity = await (
            from c in _context.ContentHierarchyLevels.AsNoTracking()
            where c.Id == entity.Id
            select c).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<ContentHierarchyLevelEntity>()
                .With.Property(nameof(ContentHierarchyLevelEntity.Name)).EqualTo("UpdatedName")
                .And.Property(nameof(ContentHierarchyLevelEntity.Slug)).EqualTo("updated-slug")
                .And.Property(nameof(ContentHierarchyLevelEntity.RelativePath)).EqualTo("/updated/path")
                .And.Property(nameof(ContentHierarchyLevelEntity.ModifiedAt)).Not.Null
                .And.Property(nameof(ContentHierarchyLevelEntity.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Update")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("ContentHierarchyLevel")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateAsync_ReturnsFalse_WhenNotFound()
    {
        // Arrange
        var model = new ContentHierarchyLevel(
            Guid.NewGuid(),
            _definitionId,
            _rootFolderId,
            "NonExistent",
            "nonexistent",
            null,
            null,
            null,
            null,
            new(Guid.Empty, DateTimeOffset.MinValue, []),
            [],
            [],
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).False);
    }

    [Test]
    public async Task DeleteAsync_DeletesContentHierarchyLevel()
    {
        // Arrange
        var entity = CreateTestEntity();
        _context.ContentHierarchyLevels.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.DeleteAsync(entity.Id, _userId);

        // Assert
        var entityExists = await _context.ContentHierarchyLevels.AnyAsync(c => c.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(entityExists, Is.False);
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Delete")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("ContentHierarchyLevel")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task DeleteAsync_ReturnsFalse_WhenNotFound()
    {
        // Act
        var result = await _subject.DeleteAsync(Guid.NewGuid(), _userId);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).False);
    }

    [Test]
    public async Task DeleteAsync_CascadesToLanguagePreferences()
    {
        // Arrange
        var entity = CreateTestEntity();
        entity.LanguagePreferences.Add(new() { Ordinal = 0, Language = "en" });
        entity.LanguagePreferences.Add(new() { Ordinal = 1, Language = "ja" });
        _context.ContentHierarchyLevels.Add(entity);
        _context.SaveChanges();

        var levelId = entity.Id;

        // Act
        var result = await _subject.DeleteAsync(levelId, _userId);

        // Assert
        var preferencesExist = await _context.LanguagePreferences
            .AnyAsync(lp => lp.ContentHierarchyId == levelId);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(preferencesExist, Is.False);
        }
    }

    [Test]
    public async Task PatchAsync_PatchesRelativePath()
    {
        // Arrange
        var entity = CreateTestEntity();
        _context.ContentHierarchyLevels.Add(entity);
        _context.SaveChanges();

        var patch = new ContentHierarchyLevelPatch
        {
            Id = entity.Id,
            RelativePath = new("/patched/path")
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var updatedEntity = await _context.ContentHierarchyLevels
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<ContentHierarchyLevelEntity>()
                .With.Property(nameof(ContentHierarchyLevelEntity.RelativePath)).EqualTo("/patched/path")
                .And.Property(nameof(ContentHierarchyLevelEntity.ModifiedAt)).Not.Null
                .And.Property(nameof(ContentHierarchyLevelEntity.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Patch")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("ContentHierarchyLevel")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task PatchAsync_ReturnsFalse_WhenNotFound()
    {
        // Arrange
        var patch = new ContentHierarchyLevelPatch
        {
            Id = Guid.NewGuid(),
            RelativePath = new("/new/path")
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).False);
    }

    [Test]
    public async Task UpdateLastRefreshedAsync_UpdatesTimestamp()
    {
        // Arrange
        var entity = CreateTestEntity();
        _context.ContentHierarchyLevels.Add(entity);
        _context.SaveChanges();

        var newTimestamp = DateTimeOffset.UtcNow;

        // Act
        var result = await _subject.UpdateLastRefreshedAsync(entity.Id, newTimestamp);

        // Assert
        var updatedEntity = await _context.ContentHierarchyLevels
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.Not.Null);
            Assert.That(updatedEntity!.DateLastRefreshed, Is.EqualTo(newTimestamp).Within(TimeSpan.FromSeconds(1)));
        }
    }

    [Test]
    public async Task UpdateLastRefreshedAsync_ReturnsFalse_WhenNotFound()
    {
        // Act
        var result = await _subject.UpdateLastRefreshedAsync(Guid.NewGuid(), DateTimeOffset.UtcNow);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).False);
    }

    [Test]
    public async Task QueryAsync_ReturnsAllLevels_WhenNoCriteria()
    {
        // Arrange
        var entity1 = CreateTestEntity("Level1", "level-1");
        var entity2 = CreateTestEntity("Level2", "level-2");
        _context.ContentHierarchyLevels.AddRange(entity1, entity2);
        _context.SaveChanges();

        var criteria = new ContentItemCriteria { TargetLevel = new ContentHierarchyLevelCriteria() };

        // Act
        var result = await _subject.QueryAsync(criteria, null);

        // Assert
        var success = result as SuccessResult<PagedResult<ContentHierarchyLevel>>;
        Assert.That(success, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(success!.Value.Items, Has.Count.EqualTo(2));
            Assert.That(success.Value.TotalItems, Is.EqualTo(2));
        }
    }

    [Test]
    public async Task QueryAsync_AppliesPaging()
    {
        // Arrange
        for (var i = 0; i < 10; i++)
        {
            var entity = CreateTestEntity($"Level{i}", $"level-{i}");
            _context.ContentHierarchyLevels.Add(entity);
        }
        _context.SaveChanges();

        var criteria = new ContentItemCriteria { TargetLevel = new ContentHierarchyLevelCriteria() };
        var paging = new PagingRequest(2, 3);

        // Act
        var result = await _subject.QueryAsync(criteria, paging);

        // Assert
        var success = result as SuccessResult<PagedResult<ContentHierarchyLevel>>;
        Assert.That(success, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(success!.Value.Items, Has.Count.EqualTo(3));
            Assert.That(success.Value.TotalItems, Is.EqualTo(10));
            Assert.That(success.Value.PageNumber, Is.EqualTo(2));
            Assert.That(success.Value.PageSize, Is.EqualTo(3));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByName()
    {
        // Arrange
        var entity1 = CreateTestEntity("TestLevel", "test-level");
        var entity2 = CreateTestEntity("OtherLevel", "other-level");
        _context.ContentHierarchyLevels.AddRange(entity1, entity2);
        _context.SaveChanges();

        var criteria = new ContentItemCriteria
        {
            TargetLevel = new ContentHierarchyLevelCriteria
            {
                Name = new AnyOfCriteria<FieldCriterion<string>>
                {
                    Any = [new FieldCriterion<string>(FieldOperator.Equals, "TestLevel")]
                }
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria, null);

        // Assert
        var success = result as SuccessResult<PagedResult<ContentHierarchyLevel>>;
        Assert.That(success, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(success!.Value.Items, Has.Count.EqualTo(1));
            Assert.That(success.Value.Items[0].Name, Is.EqualTo("TestLevel"));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByNameContains()
    {
        // Arrange
        var entity1 = CreateTestEntity("TestLevel", "test-level");
        var entity2 = CreateTestEntity("AnotherTest", "another-test");
        var entity3 = CreateTestEntity("Something", "something");
        _context.ContentHierarchyLevels.AddRange(entity1, entity2, entity3);
        _context.SaveChanges();

        var criteria = new ContentItemCriteria
        {
            TargetLevel = new ContentHierarchyLevelCriteria
            {
                Name = new AnyOfCriteria<FieldCriterion<string>>
                {
                    Any = [new FieldCriterion<string>(FieldOperator.ContainsString, "Test")]
                }
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria, null);

        // Assert
        var success = result as SuccessResult<PagedResult<ContentHierarchyLevel>>;
        Assert.That(success, Is.Not.Null);
        Assert.That(success!.Value.Items, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task QueryAsync_FiltersBySlug()
    {
        // Arrange
        var entity1 = CreateTestEntity("Level1", "test-slug");
        var entity2 = CreateTestEntity("Level2", "other-slug");
        _context.ContentHierarchyLevels.AddRange(entity1, entity2);
        _context.SaveChanges();

        var criteria = new ContentItemCriteria
        {
            TargetLevel = new ContentHierarchyLevelCriteria
            {
                Slug = new AnyOfCriteria<FieldCriterion<string>>
                {
                    Any = [new FieldCriterion<string>(FieldOperator.Equals, "test-slug")]
                }
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria, null);

        // Assert
        var success = result as SuccessResult<PagedResult<ContentHierarchyLevel>>;
        Assert.That(success, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(success!.Value.Items, Has.Count.EqualTo(1));
            Assert.That(success.Value.Items[0].Slug, Is.EqualTo("test-slug"));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByDefinitionId()
    {
        // Arrange
        var otherDefinitionId = Guid.NewGuid();
        var entity1 = CreateTestEntity("Level1", "level-1");
        var entity2 = CreateTestEntity("Level2", "level-2");
        entity2.ContentHierarchyLevelDefinitionId = otherDefinitionId;
        _context.ContentHierarchyLevels.AddRange(entity1, entity2);
        _context.SaveChanges();

        var criteria = new ContentItemCriteria
        {
            TargetLevel = new ContentHierarchyLevelCriteria
            {
                ContentHierarchyLevelDefinitionId = new AnyOfCriteria<FieldCriterion<Guid>>
                {
                    Any = [new FieldCriterion<Guid>(FieldOperator.Equals, _definitionId)]
                }
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria, null);

        // Assert
        var success = result as SuccessResult<PagedResult<ContentHierarchyLevel>>;
        Assert.That(success, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(success!.Value.Items, Has.Count.EqualTo(1));
            Assert.That(success.Value.Items[0].Name, Is.EqualTo("Level1"));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByMonitoring()
    {
        // Arrange
        var entity1 = CreateTestEntity("Level1", "level-1");
        var entity2 = CreateTestEntity("Level2", "level-2");
        _context.ContentHierarchyLevels.AddRange(entity1, entity2);
        _context.SaveChanges();

        var contentItem1 = new ContentItemEntity
        {
            ContentHierarchyId = entity1.Id,
            Monitoring = MonitoringIntent.Active,
            Fulfillment = FulfillmentStatus.Missing,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var contentItem2 = new ContentItemEntity
        {
            ContentHierarchyId = entity2.Id,
            Monitoring = MonitoringIntent.Excluded,
            Fulfillment = FulfillmentStatus.Missing,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.ContentItems.AddRange(contentItem1, contentItem2);
        _context.SaveChanges();

        var criteria = new ContentItemCriteria
        {
            TargetLevel = new(),
            Monitoring = new AnyOfCriteria<FieldCriterion<MonitoringIntent>>
            {
                Any = [new FieldCriterion<MonitoringIntent>(FieldOperator.Equals, MonitoringIntent.Active)]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria, null);

        // Assert
        var success = result as SuccessResult<PagedResult<ContentHierarchyLevel>>;
        Assert.That(success, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(success!.Value.Items, Has.Count.EqualTo(1));
            Assert.That(success.Value.Items[0].Name, Is.EqualTo("Level1"));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByFulfillment()
    {
        // Arrange
        var entity1 = CreateTestEntity("Level1", "level-1");
        var entity2 = CreateTestEntity("Level2", "level-2");
        _context.ContentHierarchyLevels.AddRange(entity1, entity2);
        _context.SaveChanges();

        var contentItem1 = new ContentItemEntity
        {
            ContentHierarchyId = entity1.Id,
            Monitoring = MonitoringIntent.Active,
            Fulfillment = FulfillmentStatus.Missing,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var contentItem2 = new ContentItemEntity
        {
            ContentHierarchyId = entity2.Id,
            Monitoring = MonitoringIntent.Active,
            Fulfillment = FulfillmentStatus.Fulfilled,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.ContentItems.AddRange(contentItem1, contentItem2);
        _context.SaveChanges();

        var criteria = new ContentItemCriteria
        {
            TargetLevel = new(),
            Fulfillment = new AnyOfCriteria<FieldCriterion<FulfillmentStatus>>
            {
                Any = [new FieldCriterion<FulfillmentStatus>(FieldOperator.Equals, FulfillmentStatus.Fulfilled)]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria, null);

        // Assert
        var success = result as SuccessResult<PagedResult<ContentHierarchyLevel>>;
        Assert.That(success, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(success!.Value.Items, Has.Count.EqualTo(1));
            Assert.That(success.Value.Items[0].Name, Is.EqualTo("Level2"));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByTags()
    {
        // Arrange
        var entity1 = CreateTestEntity("Level1", "level-1");
        var entity2 = CreateTestEntity("Level2", "level-2");
        _context.ContentHierarchyLevels.AddRange(entity1, entity2);
        _context.SaveChanges();
        _context.Tags.Add(new() { Name = "action" });
        _context.Tags.Add(new() { Name = "comedy" });
        _context.SaveChanges();

        var contentItem1 = new ContentItemEntity
        {
            ContentHierarchyId = entity1.Id,
            Monitoring = MonitoringIntent.Active,
            Fulfillment = FulfillmentStatus.Missing,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var contentItem2 = new ContentItemEntity
        {
            ContentHierarchyId = entity2.Id,
            Monitoring = MonitoringIntent.Active,
            Fulfillment = FulfillmentStatus.Missing,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.ContentItems.AddRange(contentItem1, contentItem2);
        _context.SaveChanges();

        _context.ContentItemTags.Add(new()
        {
            ContentItemId = contentItem1.Id,
            TagName = "action"
        });
        _context.ContentItemTags.Add(new()
        {
            ContentItemId = contentItem2.Id,
            TagName = "comedy"
        });
        _context.SaveChanges();

        var criteria = new ContentItemCriteria
        {
            TargetLevel = new(),
            Tags = new CollectionCriteria<FieldCriterion<string>>
            {
                Criteria = [new FieldCriterion<string>(FieldOperator.Equals, "action")],
                Operator = CollectionOperator.Any
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria, null);

        // Assert
        var success = result as SuccessResult<PagedResult<ContentHierarchyLevel>>;
        Assert.That(success, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(success!.Value.Items, Has.Count.EqualTo(1));
            Assert.That(success.Value.Items[0].Name, Is.EqualTo("Level1"));
        }
    }

    [Test]
    public async Task QueryAsync_CombinesMultipleCriteria()
    {
        // Arrange
        var entity1 = CreateTestEntity("ActionMovie", "action-movie");
        var entity2 = CreateTestEntity("ComedyMovie", "comedy-movie");
        var entity3 = CreateTestEntity("ActionShow", "action-show");
        _context.ContentHierarchyLevels.AddRange(entity1, entity2, entity3);
        _context.SaveChanges();

        var contentItem1 = new ContentItemEntity
        {
            ContentHierarchyId = entity1.Id,
            Monitoring = MonitoringIntent.Active,
            Fulfillment = FulfillmentStatus.Missing,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var contentItem2 = new ContentItemEntity
        {
            ContentHierarchyId = entity2.Id,
            Monitoring = MonitoringIntent.Active,
            Fulfillment = FulfillmentStatus.Missing,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var contentItem3 = new ContentItemEntity
        {
            ContentHierarchyId = entity3.Id,
            Monitoring = MonitoringIntent.Excluded,
            Fulfillment = FulfillmentStatus.Missing,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.ContentItems.AddRange(contentItem1, contentItem2, contentItem3);
        _context.SaveChanges();

        var criteria = new ContentItemCriteria
        {
            TargetLevel = new ContentHierarchyLevelCriteria
            {
                Name = new AnyOfCriteria<FieldCriterion<string>>
                {
                    Any = [new FieldCriterion<string>(FieldOperator.ContainsString, "Action")]
                }
            },
            Monitoring = new AnyOfCriteria<FieldCriterion<MonitoringIntent>>
            {
                Any = [new FieldCriterion<MonitoringIntent>(FieldOperator.Equals, MonitoringIntent.Active)]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria, null);

        // Assert
        var success = result as SuccessResult<PagedResult<ContentHierarchyLevel>>;
        Assert.That(success, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(success!.Value.Items, Has.Count.EqualTo(1));
            Assert.That(success.Value.Items[0].Name, Is.EqualTo("ActionMovie"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsEmptyList_WhenNoMatches()
    {
        // Arrange
        var entity = CreateTestEntity("Level1", "level-1");
        _context.ContentHierarchyLevels.Add(entity);
        _context.SaveChanges();

        var criteria = new ContentItemCriteria
        {
            TargetLevel = new ContentHierarchyLevelCriteria
            {
                Name = new AnyOfCriteria<FieldCriterion<string>>
                {
                    Any = [new FieldCriterion<string>(FieldOperator.Equals, "NonExistent")]
                }
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria, null);

        // Assert
        var success = result as SuccessResult<PagedResult<ContentHierarchyLevel>>;
        Assert.That(success, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(success!.Value.Items, Is.Empty);
            Assert.That(success.Value.TotalItems, Is.EqualTo(0));
        }
    }
}
