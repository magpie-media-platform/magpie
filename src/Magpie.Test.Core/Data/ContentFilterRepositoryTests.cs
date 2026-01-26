using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using Magpie.Abstractions.Data;
using Magpie.Core.Data;
using Magpie.Data.Database;
using Magpie.Models.Filters;
using Magpie.Models.Filters.Content;
using Magpie.Models.Result;
using Magpie.Models.Users;
using Magpie.Test.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NUnit.Framework;

using ContentFilterEntity = Magpie.Data.Database.Entities.ContentFilter;
using ContentFilterTagEntity = Magpie.Data.Database.Entities.ContentFilterTag;
using HistoryEntryEntity = Magpie.Data.Database.Entities.HistoryEntry;
using UserEntity = Magpie.Data.Database.Entities.User;

namespace Magpie.Test.Core.Data;

[TestFixture]
public class ContentFilterRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IContentFilterRepository _subject = null!;
    private Guid _userId;

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
        _context.SaveChanges();

        _userId = user.Id;

        var loggerFactory = LoggerFactory.Create(b => b.AddTestLogger());
        var historyRepo = new HistoryRepository(_context, loggerFactory.CreateLogger<HistoryRepository>());

        _subject = new ContentFilterRepository(
            _context,
            historyRepo,
            loggerFactory.CreateLogger<ContentFilterRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context?.Dispose();
    }

    private static ContentItemCriteria CreateTestCriteria()
    {
        return new ContentItemCriteria
        {
            TargetLevel = new()
        };
    }

    [Test]
    public async Task GetByIdAsync_ReturnsContentFilter()
    {
        // Arrange
        var entity = new ContentFilterEntity
        {
            Name = "TestFilter",
            Criteria = JsonSerializer.Serialize(CreateTestCriteria()),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.ContentFilters.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByIdAsync(entity.Id);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<ContentFilter?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(ContentFilter.Name)).EqualTo("TestFilter"));
    }

    [Test]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        // Act
        var result = await _subject.GetByIdAsync(Guid.NewGuid());

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<ContentFilter?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task GetByIdAsync_IncludesTags()
    {
        // Arrange
        _context.Tags.Add(new() { Name = "movies" });
        _context.Tags.Add(new() { Name = "tv" });
        var entity = new ContentFilterEntity
        {
            Name = "FilterWithTags",
            Criteria = JsonSerializer.Serialize(CreateTestCriteria()),
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Tags.Add(new() { TagName = "movies" });
        entity.Tags.Add(new() { TagName = "tv" });
        _context.ContentFilters.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByIdAsync(entity.Id);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<ContentFilter?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(ContentFilter.Tags)).With.Count.EqualTo(2));
    }

    [Test]
    public async Task GetAllAsync_ReturnsAllContentFilters()
    {
        // Arrange
        var entity1 = new ContentFilterEntity
        {
            Name = "Filter1",
            Criteria = JsonSerializer.Serialize(CreateTestCriteria()),
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new ContentFilterEntity
        {
            Name = "Filter2",
            Criteria = JsonSerializer.Serialize(CreateTestCriteria()),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.ContentFilters.AddRange(entity1, entity2);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetAllAsync();

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<ContentFilter>>>()
            .With.Property(nameof(SuccessResult<>.Value)).With.Count.EqualTo(2));
    }

    [Test]
    public async Task GetAllAsync_ReturnsEmptyList_WhenNoContentFilters()
    {
        // Act
        var result = await _subject.GetAllAsync();

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<ContentFilter>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task GetAllAsync_IncludesTags()
    {
        // Arrange
        _context.Tags.Add(new() { Name = "books" });
        var entity = new ContentFilterEntity
        {
            Name = "FilterWithTags",
            Criteria = JsonSerializer.Serialize(CreateTestCriteria()),
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Tags.Add(new() { TagName = "books" });
        _context.ContentFilters.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetAllAsync();

        // Assert
        var success = result as SuccessResult<IReadOnlyList<ContentFilter>>;
        Assert.That(success, Is.Not.Null);
        Assert.That(success!.Value[0].Tags, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task GetByNameAsync_ReturnsContentFilter()
    {
        // Arrange
        var entity = new ContentFilterEntity
        {
            Name = "UniqueFilterName",
            Criteria = JsonSerializer.Serialize(CreateTestCriteria()),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.ContentFilters.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByNameAsync("UniqueFilterName");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<ContentFilter?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(ContentFilter.Name))
                .EqualTo("UniqueFilterName"));
    }

    [Test]
    public async Task GetByNameAsync_ReturnsNull_WhenNotFound()
    {
        // Act
        var result = await _subject.GetByNameAsync("NonExistentFilter");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<ContentFilter?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task GetByTagAsync_ReturnsContentFilters()
    {
        // Arrange
        _context.Tags.Add(new() { Name = "shared" });
        _context.Tags.Add(new() { Name = "unique" });
        var entity1 = new ContentFilterEntity
        {
            Name = "Filter1",
            Criteria = JsonSerializer.Serialize(CreateTestCriteria()),
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity1.Tags.Add(new() { TagName = "shared" });
        var entity2 = new ContentFilterEntity
        {
            Name = "Filter2",
            Criteria = JsonSerializer.Serialize(CreateTestCriteria()),
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity2.Tags.Add(new() { TagName = "shared" });
        var entity3 = new ContentFilterEntity
        {
            Name = "Filter3",
            Criteria = JsonSerializer.Serialize(CreateTestCriteria()),
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity3.Tags.Add(new() { TagName = "unique" });
        _context.ContentFilters.AddRange(entity1, entity2, entity3);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByTagAsync("shared");

        // Assert
        var success = result as SuccessResult<IReadOnlyList<ContentFilter>>;
        Assert.That(success, Is.Not.Null);
        Assert.That(success!.Value, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task GetByTagAsync_ReturnsEmptyList_WhenNoMatches()
    {
        // Act
        var result = await _subject.GetByTagAsync("nonexistent");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<ContentFilter>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task CreateAsync_CreatesContentFilter()
    {
        // Arrange
        var model = new ContentFilter(
            Guid.Empty,
            "NewFilter",
            CreateTestCriteria(),
            [],
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.CreateAsync(model, _userId);

        // Assert
        var entity = await (
            from f in _context.ContentFilters.AsNoTracking()
            where f.Name == "NewFilter"
            select f).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<ContentFilter>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(ContentFilter.Name))
                    .EqualTo("NewFilter"));
            Assert.That(entity, Is.InstanceOf<ContentFilterEntity>()
                .With.Property(nameof(ContentFilterEntity.Name)).EqualTo("NewFilter")
                .And.Property(nameof(ContentFilterEntity.CreatedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Create")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("ContentFilter")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesContentFilterWithTags()
    {
        // Arrange
        _context.Tags.Add(new() { Name = "movies" });
        _context.Tags.Add(new() { Name = "tv" });
        _context.Tags.Add(new() { Name = "anime" });
        var model = new ContentFilter(
            Guid.Empty,
            "FilterWithTags",
            CreateTestCriteria(),
            ["movies", "tv", "anime"],
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.CreateAsync(model, _userId);

        // Assert
        var entity = await (
            from f in _context.ContentFilters.AsNoTracking().Include(f => f.Tags)
            where f.Name == "FilterWithTags"
            select f).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<ContentFilter>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(ContentFilter.Tags)).Count.EqualTo(3));
            Assert.That(entity, Is.Not.Null);
            Assert.That(entity!.Tags, Has.Count.EqualTo(3));
            Assert.That(entity.Tags, Has.One
                .With.Property(nameof(ContentFilterTagEntity.TagName)).EqualTo("movies"));
            Assert.That(entity.Tags, Has.One
                .With.Property(nameof(ContentFilterTagEntity.TagName)).EqualTo("tv"));
            Assert.That(entity.Tags, Has.One
                .With.Property(nameof(ContentFilterTagEntity.TagName)).EqualTo("anime"));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesContentFilter()
    {
        // Arrange
        var entity = new ContentFilterEntity
        {
            Name = "OriginalName",
            Criteria = JsonSerializer.Serialize(CreateTestCriteria()),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.ContentFilters.Add(entity);
        _context.SaveChanges();

        var model = new ContentFilter(
            entity.Id,
            "UpdatedName",
            CreateTestCriteria(),
            [],
            entity.CreatedAt,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        var updatedEntity = await (
            from f in _context.ContentFilters.AsNoTracking()
            where f.Id == entity.Id
            select f).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<ContentFilterEntity>()
                .With.Property(nameof(ContentFilterEntity.Name)).EqualTo("UpdatedName")
                .And.Property(nameof(ContentFilterEntity.ModifiedAt)).Not.Null
                .And.Property(nameof(ContentFilterEntity.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Update")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("ContentFilter")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateAsync_ReturnsFalse_WhenNotFound()
    {
        // Arrange
        var model = new ContentFilter(
            Guid.NewGuid(),
            "NonExistent",
            CreateTestCriteria(),
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
    public async Task UpdateAsync_AddsNewTags()
    {
        // Arrange
        _context.Tags.Add(new() { Name = "newTag1" });
        _context.Tags.Add(new() { Name = "newTag2" });
        var entity = new ContentFilterEntity
        {
            Name = "FilterToUpdate",
            Criteria = JsonSerializer.Serialize(CreateTestCriteria()),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.ContentFilters.Add(entity);
        _context.SaveChanges();

        var model = new ContentFilter(
            entity.Id,
            entity.Name,
            CreateTestCriteria(),
            ["newTag1", "newTag2"],
            entity.CreatedAt,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        var tagEntities = await _context.ContentFilterTags
            .AsNoTracking()
            .Where(t => t.ContentFilterId == entity.Id)
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(tagEntities, Has.Count.EqualTo(2));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(ContentFilterTagEntity.TagName)).EqualTo("newTag1"));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(ContentFilterTagEntity.TagName)).EqualTo("newTag2"));
        }
    }

    [Test]
    public async Task UpdateAsync_RemovesTags()
    {
        // Arrange
        _context.Tags.Add(new() { Name = "keepMe" });
        _context.Tags.Add(new() { Name = "removeMe" });
        var entity = new ContentFilterEntity
        {
            Name = "FilterWithTags",
            Criteria = JsonSerializer.Serialize(CreateTestCriteria()),
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Tags.Add(new() { TagName = "keepMe" });
        entity.Tags.Add(new() { TagName = "removeMe" });
        _context.ContentFilters.Add(entity);
        _context.SaveChanges();

        var model = new ContentFilter(
            entity.Id,
            entity.Name,
            CreateTestCriteria(),
            ["keepMe"],
            entity.CreatedAt,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        var tagEntities = await _context.ContentFilterTags
            .AsNoTracking()
            .Where(t => t.ContentFilterId == entity.Id)
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(tagEntities, Has.Count.EqualTo(1));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(ContentFilterTagEntity.TagName)).EqualTo("keepMe"));
            Assert.That(tagEntities, Has.None
                .With.Property(nameof(ContentFilterTagEntity.TagName)).EqualTo("removeMe"));
        }
    }

    [Test]
    public async Task UpdateAsync_AddsAndRemovesTags()
    {
        // Arrange
        _context.Tags.Add(new() { Name = "existing" });
        _context.Tags.Add(new() { Name = "toRemove" });
        _context.Tags.Add(new() { Name = "toAdd" });
        var entity = new ContentFilterEntity
        {
            Name = "ComplexFilter",
            Criteria = JsonSerializer.Serialize(CreateTestCriteria()),
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Tags.Add(new() { TagName = "existing" });
        entity.Tags.Add(new() { TagName = "toRemove" });
        _context.ContentFilters.Add(entity);
        _context.SaveChanges();

        var model = new ContentFilter(
            entity.Id,
            entity.Name,
            CreateTestCriteria(),
            ["existing", "toAdd"],
            entity.CreatedAt,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        var tagEntities = await _context.ContentFilterTags
            .AsNoTracking()
            .Where(t => t.ContentFilterId == entity.Id)
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(tagEntities, Has.Count.EqualTo(2));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(ContentFilterTagEntity.TagName)).EqualTo("existing"));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(ContentFilterTagEntity.TagName)).EqualTo("toAdd"));
            Assert.That(tagEntities, Has.None
                .With.Property(nameof(ContentFilterTagEntity.TagName)).EqualTo("toRemove"));
        }
    }

    [Test]
    public async Task DeleteAsync_DeletesContentFilter()
    {
        // Arrange
        var entity = new ContentFilterEntity
        {
            Name = "ToDelete",
            Criteria = JsonSerializer.Serialize(CreateTestCriteria()),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.ContentFilters.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.DeleteAsync(entity.Id, _userId);

        // Assert
        var entityExists = await _context.ContentFilters.AnyAsync(f => f.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(entityExists, Is.False);
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Delete")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("ContentFilter")
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
    public async Task DeleteAsync_CascadesToTags()
    {
        // Arrange
        _context.Tags.Add(new() { Name = "tag1" });
        _context.Tags.Add(new() { Name = "tag2" });
        var entity = new ContentFilterEntity
        {
            Name = "FilterWithTags",
            Criteria = JsonSerializer.Serialize(CreateTestCriteria()),
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Tags.Add(new() { TagName = "tag1" });
        entity.Tags.Add(new() { TagName = "tag2" });
        _context.ContentFilters.Add(entity);
        _context.SaveChanges();

        var filterId = entity.Id;

        // Act
        var result = await _subject.DeleteAsync(filterId, _userId);

        // Assert
        var tagsExist = await _context.ContentFilterTags
            .AnyAsync(t => t.ContentFilterId == filterId);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(tagsExist, Is.False);
        }
    }

    [Test]
    public async Task PatchAsync_PatchesName()
    {
        // Arrange
        var entity = new ContentFilterEntity
        {
            Name = "OriginalName",
            Criteria = JsonSerializer.Serialize(CreateTestCriteria()),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.ContentFilters.Add(entity);
        _context.SaveChanges();

        var patch = new ContentFilterPatch
        {
            Id = entity.Id,
            Name = new("PatchedName")
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var updatedEntity = await _context.ContentFilters
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<ContentFilterEntity>()
                .With.Property(nameof(ContentFilterEntity.Name)).EqualTo("PatchedName")
                .And.Property(nameof(ContentFilterEntity.ModifiedAt)).Not.Null
                .And.Property(nameof(ContentFilterEntity.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Patch")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("ContentFilter")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesCriteria()
    {
        // Arrange
        var entity = new ContentFilterEntity
        {
            Name = "TestFilter",
            Criteria = JsonSerializer.Serialize(CreateTestCriteria()),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.ContentFilters.Add(entity);
        _context.SaveChanges();

        var newCriteria = CreateTestCriteria();
        var patch = new ContentFilterPatch
        {
            Id = entity.Id,
            Criteria = new(newCriteria)
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var updatedEntity = await _context.ContentFilters
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.Not.Null);
            Assert.That(updatedEntity!.Criteria, Does.Contain("TargetLevel"));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesTags_AddOnly()
    {
        // Arrange
        _context.Tags.Add(new() { Name = "existing" });
        _context.Tags.Add(new() { Name = "newTag1" });
        _context.Tags.Add(new() { Name = "newTag2" });
        var entity = new ContentFilterEntity
        {
            Name = "TestFilter",
            Criteria = JsonSerializer.Serialize(CreateTestCriteria()),
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Tags.Add(new() { TagName = "existing" });
        _context.ContentFilters.Add(entity);
        _context.SaveChanges();

        var patch = new ContentFilterPatch
        {
            Id = entity.Id,
            Tags = new(Add: ["newTag1", "newTag2"], Remove: null)
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var tagEntities = await _context.ContentFilterTags
            .AsNoTracking()
            .Where(t => t.ContentFilterId == entity.Id)
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(tagEntities, Has.Count.EqualTo(3));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(ContentFilterTagEntity.TagName)).EqualTo("existing"));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(ContentFilterTagEntity.TagName)).EqualTo("newTag1"));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(ContentFilterTagEntity.TagName)).EqualTo("newTag2"));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesTags_RemoveOnly()
    {
        // Arrange
        _context.Tags.Add(new() { Name = "keepMe" });
        _context.Tags.Add(new() { Name = "removeMe" });
        var entity = new ContentFilterEntity
        {
            Name = "TestFilter",
            Criteria = JsonSerializer.Serialize(CreateTestCriteria()),
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Tags.Add(new() { TagName = "keepMe" });
        entity.Tags.Add(new() { TagName = "removeMe" });
        _context.ContentFilters.Add(entity);
        _context.SaveChanges();

        var patch = new ContentFilterPatch
        {
            Id = entity.Id,
            Tags = new(Add: null, Remove: ["removeMe"])
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var tagEntities = await _context.ContentFilterTags
            .AsNoTracking()
            .Where(t => t.ContentFilterId == entity.Id)
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(tagEntities, Has.Count.EqualTo(1));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(ContentFilterTagEntity.TagName)).EqualTo("keepMe"));
            Assert.That(tagEntities, Has.None
                .With.Property(nameof(ContentFilterTagEntity.TagName)).EqualTo("removeMe"));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesTags_AddAndRemove()
    {
        // Arrange
        _context.Tags.Add(new() { Name = "existing" });
        _context.Tags.Add(new() { Name = "toRemove" });
        _context.Tags.Add(new() { Name = "toAdd" });
        var entity = new ContentFilterEntity
        {
            Name = "TestFilter",
            Criteria = JsonSerializer.Serialize(CreateTestCriteria()),
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Tags.Add(new() { TagName = "existing" });
        entity.Tags.Add(new() { TagName = "toRemove" });
        _context.ContentFilters.Add(entity);
        _context.SaveChanges();

        var patch = new ContentFilterPatch
        {
            Id = entity.Id,
            Tags = new(Add: ["toAdd"], Remove: ["toRemove"])
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var tagEntities = await _context.ContentFilterTags
            .AsNoTracking()
            .Where(t => t.ContentFilterId == entity.Id)
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(tagEntities, Has.Count.EqualTo(2));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(ContentFilterTagEntity.TagName)).EqualTo("existing"));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(ContentFilterTagEntity.TagName)).EqualTo("toAdd"));
            Assert.That(tagEntities, Has.None
                .With.Property(nameof(ContentFilterTagEntity.TagName)).EqualTo("toRemove"));
        }
    }

    [Test]
    public async Task PatchAsync_ReturnsFalse_WhenNotFound()
    {
        // Arrange
        var patch = new ContentFilterPatch
        {
            Id = Guid.NewGuid(),
            Name = new("NewName")
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).False);
    }
}
