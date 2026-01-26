using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using Magpie.Abstractions.Data;
using Magpie.Core.Data;
using Magpie.Data.Database;
using Magpie.Models.Filters;
using Magpie.Models.Filters.Metadata;
using Magpie.Models.Filters.Subject;
using Magpie.Models.Result;
using Magpie.Models.Users;
using Magpie.Test.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NUnit.Framework;

using HistoryEntryEntity = Magpie.Data.Database.Entities.HistoryEntry;
using SubjectFilterEntity = Magpie.Data.Database.Entities.SubjectFilter;
using SubjectFilterTagEntity = Magpie.Data.Database.Entities.SubjectFilterTag;
using UserEntity = Magpie.Data.Database.Entities.User;

namespace Magpie.Test.Core.Data;

[TestFixture]
public class SubjectFilterRepositoryTests
{
    private MagpieDbContext _context = null!;
    private ISubjectFilterRepository _subject = null!;
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

        _subject = new SubjectFilterRepository(
            _context,
            historyRepo,
            loggerFactory.CreateLogger<SubjectFilterRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context?.Dispose();
    }

    private void EnsureTagsExist(params string[] tagNames)
    {
        foreach (var tagName in tagNames)
        {
            if (!_context.Tags.Any(t => t.Name == tagName))
            {
                _context.Tags.Add(new() { Name = tagName });
            }
        }
        _context.SaveChanges();
    }

    private static SubjectCriteria CreateTestCriteria()
    {
        return new();
    }


    [Test]
    public async Task GetByIdAsync_ReturnsSubjectFilter()
    {
        // Arrange
        var entity = new SubjectFilterEntity
        {
            Name = "TestFilter",
            Criteria = JsonSerializer.Serialize(new MetadataContributionCriteria()),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.SubjectFilters.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByIdAsync(entity.Id);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<SubjectFilter?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(SubjectFilter.Name)).EqualTo("TestFilter"));
    }

    [Test]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        // Act
        var result = await _subject.GetByIdAsync(Guid.NewGuid());

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<SubjectFilter?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task GetByIdAsync_IncludesTags()
    {
        // Arrange
        EnsureTagsExist("actors", "directors");
        var entity = new SubjectFilterEntity
        {
            Name = "FilterWithTags",
            Criteria = JsonSerializer.Serialize(new MetadataContributionCriteria()),
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Tags.Add(new() { TagName = "actors" });
        entity.Tags.Add(new() { TagName = "directors" });
        _context.SubjectFilters.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByIdAsync(entity.Id);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<SubjectFilter?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(SubjectFilter.Tags)).With.Count.EqualTo(2));
    }

    [Test]
    public async Task GetAllAsync_ReturnsAllSubjectFilters()
    {
        // Arrange
        var entity1 = new SubjectFilterEntity
        {
            Name = "Filter1",
            Criteria = JsonSerializer.Serialize(new MetadataContributionCriteria()),
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new SubjectFilterEntity
        {
            Name = "Filter2",
            Criteria = JsonSerializer.Serialize(new MetadataContributionCriteria()),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.SubjectFilters.AddRange(entity1, entity2);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetAllAsync();

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<SubjectFilter>>>()
            .With.Property(nameof(SuccessResult<>.Value)).With.Count.EqualTo(2));
    }

    [Test]
    public async Task GetAllAsync_ReturnsEmptyList_WhenNoSubjectFilters()
    {
        // Act
        var result = await _subject.GetAllAsync();

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<SubjectFilter>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task GetAllAsync_IncludesTags()
    {
        // Arrange
        EnsureTagsExist("studios");
        var entity = new SubjectFilterEntity
        {
            Name = "FilterWithTags",
            Criteria = JsonSerializer.Serialize(new MetadataContributionCriteria()),
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Tags.Add(new() { TagName = "studios" });
        _context.SubjectFilters.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetAllAsync();

        // Assert
        var success = result as SuccessResult<IReadOnlyList<SubjectFilter>>;
        Assert.That(success, Is.Not.Null);
        Assert.That(success!.Value[0].Tags, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task GetByNameAsync_ReturnsSubjectFilter()
    {
        // Arrange
        var entity = new SubjectFilterEntity
        {
            Name = "UniqueFilterName",
            Criteria = JsonSerializer.Serialize(new MetadataContributionCriteria()),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.SubjectFilters.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByNameAsync("UniqueFilterName");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<SubjectFilter?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(SubjectFilter.Name))
                .EqualTo("UniqueFilterName"));
    }

    [Test]
    public async Task GetByNameAsync_ReturnsNull_WhenNotFound()
    {
        // Act
        var result = await _subject.GetByNameAsync("NonExistentFilter");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<SubjectFilter?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task GetByTagAsync_ReturnsSubjectFilters()
    {
        // Arrange
        EnsureTagsExist("shared", "unique");
        var entity1 = new SubjectFilterEntity
        {
            Name = "Filter1",
            Criteria = JsonSerializer.Serialize(new MetadataContributionCriteria()),
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity1.Tags.Add(new() { TagName = "shared" });
        var entity2 = new SubjectFilterEntity
        {
            Name = "Filter2",
            Criteria = JsonSerializer.Serialize(new MetadataContributionCriteria()),
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity2.Tags.Add(new() { TagName = "shared" });
        var entity3 = new SubjectFilterEntity
        {
            Name = "Filter3",
            Criteria = JsonSerializer.Serialize(new MetadataContributionCriteria()),
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity3.Tags.Add(new() { TagName = "unique" });
        _context.SubjectFilters.AddRange(entity1, entity2, entity3);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByTagAsync("shared");

        // Assert
        var success = result as SuccessResult<IReadOnlyList<SubjectFilter>>;
        Assert.That(success, Is.Not.Null);
        Assert.That(success!.Value, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task GetByTagAsync_ReturnsEmptyList_WhenNoMatches()
    {
        // Act
        var result = await _subject.GetByTagAsync("nonexistent");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<SubjectFilter>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task CreateAsync_CreatesSubjectFilter()
    {
        // Arrange
        var model = new SubjectFilter(
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
            from f in _context.SubjectFilters.AsNoTracking()
            where f.Name == "NewFilter"
            select f).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<SubjectFilter>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(SubjectFilter.Name))
                    .EqualTo("NewFilter"));
            Assert.That(entity, Is.InstanceOf<SubjectFilterEntity>()
                .With.Property(nameof(SubjectFilterEntity.Name)).EqualTo("NewFilter")
                .And.Property(nameof(SubjectFilterEntity.CreatedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Create")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("SubjectFilter")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesSubjectFilterWithTags()
    {
        // Arrange
        EnsureTagsExist("actors", "directors", "writers");
        var model = new SubjectFilter(
            Guid.Empty,
            "FilterWithTags",
            CreateTestCriteria(),
            ["actors", "directors", "writers"],
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.CreateAsync(model, _userId);

        // Assert
        var entity = await (
            from f in _context.SubjectFilters.AsNoTracking().Include(f => f.Tags)
            where f.Name == "FilterWithTags"
            select f).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<SubjectFilter>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(SubjectFilter.Tags))
                    .With.Count.EqualTo(3));
            Assert.That(entity, Is.Not.Null);
            Assert.That(entity!.Tags, Has.Count.EqualTo(3));
            Assert.That(entity.Tags, Has.One
                .With.Property(nameof(SubjectFilterTagEntity.TagName)).EqualTo("actors"));
            Assert.That(entity.Tags, Has.One
                .With.Property(nameof(SubjectFilterTagEntity.TagName)).EqualTo("directors"));
            Assert.That(entity.Tags, Has.One
                .With.Property(nameof(SubjectFilterTagEntity.TagName)).EqualTo("writers"));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesSubjectFilter()
    {
        // Arrange
        var entity = new SubjectFilterEntity
        {
            Name = "OriginalName",
            Criteria = JsonSerializer.Serialize(new MetadataContributionCriteria()),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.SubjectFilters.Add(entity);
        _context.SaveChanges();

        var model = new SubjectFilter(
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
            from f in _context.SubjectFilters.AsNoTracking()
            where f.Id == entity.Id
            select f).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<SubjectFilterEntity>()
                .With.Property(nameof(SubjectFilterEntity.Name)).EqualTo("UpdatedName")
                .And.Property(nameof(SubjectFilterEntity.ModifiedAt)).Not.Null
                .And.Property(nameof(SubjectFilterEntity.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Update")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("SubjectFilter")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateAsync_ReturnsFalse_WhenNotFound()
    {
        // Arrange
        var model = new SubjectFilter(
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
        EnsureTagsExist("newTag1", "newTag2");
        var entity = new SubjectFilterEntity
        {
            Name = "FilterToUpdate",
            Criteria = JsonSerializer.Serialize(new MetadataContributionCriteria()),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.SubjectFilters.Add(entity);
        _context.SaveChanges();

        var model = new SubjectFilter(
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
        var tagEntities = await _context.SubjectFilterTags
            .AsNoTracking()
            .Where(t => t.SubjectFilterId == entity.Id)
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(tagEntities, Has.Count.EqualTo(2));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(SubjectFilterTagEntity.TagName)).EqualTo("newTag1"));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(SubjectFilterTagEntity.TagName)).EqualTo("newTag2"));
        }
    }

    [Test]
    public async Task UpdateAsync_RemovesTags()
    {
        // Arrange
        EnsureTagsExist("keepMe", "removeMe");
        var entity = new SubjectFilterEntity
        {
            Name = "FilterWithTags",
            Criteria = JsonSerializer.Serialize(new MetadataContributionCriteria()),
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Tags.Add(new() { TagName = "keepMe" });
        entity.Tags.Add(new() { TagName = "removeMe" });
        _context.SubjectFilters.Add(entity);
        _context.SaveChanges();

        var model = new SubjectFilter(
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
        var tagEntities = await _context.SubjectFilterTags
            .AsNoTracking()
            .Where(t => t.SubjectFilterId == entity.Id)
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(tagEntities, Has.Count.EqualTo(1));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(SubjectFilterTagEntity.TagName)).EqualTo("keepMe"));
            Assert.That(tagEntities, Has.None
                .With.Property(nameof(SubjectFilterTagEntity.TagName)).EqualTo("removeMe"));
        }
    }

    [Test]
    public async Task UpdateAsync_AddsAndRemovesTags()
    {
        // Arrange
        EnsureTagsExist("existing", "toRemove", "toAdd");
        var entity = new SubjectFilterEntity
        {
            Name = "ComplexFilter",
            Criteria = JsonSerializer.Serialize(new MetadataContributionCriteria()),
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Tags.Add(new() { TagName = "existing" });
        entity.Tags.Add(new() { TagName = "toRemove" });
        _context.SubjectFilters.Add(entity);
        _context.SaveChanges();

        var model = new SubjectFilter(
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
        var tagEntities = await _context.SubjectFilterTags
            .AsNoTracking()
            .Where(t => t.SubjectFilterId == entity.Id)
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(tagEntities, Has.Count.EqualTo(2));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(SubjectFilterTagEntity.TagName)).EqualTo("existing"));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(SubjectFilterTagEntity.TagName)).EqualTo("toAdd"));
            Assert.That(tagEntities, Has.None
                .With.Property(nameof(SubjectFilterTagEntity.TagName)).EqualTo("toRemove"));
        }
    }

    [Test]
    public async Task DeleteAsync_DeletesSubjectFilter()
    {
        // Arrange
        var entity = new SubjectFilterEntity
        {
            Name = "ToDelete",
            Criteria = JsonSerializer.Serialize(new MetadataContributionCriteria()),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.SubjectFilters.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.DeleteAsync(entity.Id, _userId);

        // Assert
        var entityExists = await _context.SubjectFilters.AnyAsync(f => f.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(entityExists, Is.False);
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Delete")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("SubjectFilter")
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
        EnsureTagsExist("tag1", "tag2");
        var entity = new SubjectFilterEntity
        {
            Name = "FilterWithTags",
            Criteria = JsonSerializer.Serialize(new MetadataContributionCriteria()),
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Tags.Add(new() { TagName = "tag1" });
        entity.Tags.Add(new() { TagName = "tag2" });
        _context.SubjectFilters.Add(entity);
        _context.SaveChanges();

        var filterId = entity.Id;

        // Act
        var result = await _subject.DeleteAsync(filterId, _userId);

        // Assert
        var tagsExist = await _context.SubjectFilterTags
            .AnyAsync(t => t.SubjectFilterId == filterId);

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
        var entity = new SubjectFilterEntity
        {
            Name = "OriginalName",
            Criteria = JsonSerializer.Serialize(new MetadataContributionCriteria()),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.SubjectFilters.Add(entity);
        _context.SaveChanges();

        var patch = new SubjectFilterPatch
        {
            Id = entity.Id,
            Name = new("PatchedName")
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var updatedEntity = await _context.SubjectFilters
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<SubjectFilterEntity>()
                .With.Property(nameof(SubjectFilterEntity.Name)).EqualTo("PatchedName")
                .And.Property(nameof(SubjectFilterEntity.ModifiedAt)).Not.Null
                .And.Property(nameof(SubjectFilterEntity.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Patch")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("SubjectFilter")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesCriteria()
    {
        // Arrange
        var entity = new SubjectFilterEntity
        {
            Name = "TestFilter",
            Criteria = JsonSerializer.Serialize(new MetadataContributionCriteria()),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.SubjectFilters.Add(entity);
        _context.SaveChanges();

        var newCriteria = CreateTestCriteria();
        var patch = new SubjectFilterPatch
        {
            Id = entity.Id,
            Criteria = new(newCriteria)
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var updatedEntity = await _context.SubjectFilters
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.Not.Null);
            Assert.That(updatedEntity!.Criteria, Does.Contain("Kind"));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesTags_AddOnly()
    {
        // Arrange
        EnsureTagsExist("existing", "newTag1", "newTag2");
        var entity = new SubjectFilterEntity
        {
            Name = "TestFilter",
            Criteria = JsonSerializer.Serialize(new MetadataContributionCriteria()),
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Tags.Add(new() { TagName = "existing" });
        _context.SubjectFilters.Add(entity);
        _context.SaveChanges();

        var patch = new SubjectFilterPatch
        {
            Id = entity.Id,
            Tags = new(Add: ["newTag1", "newTag2"], Remove: null)
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var tagEntities = await _context.SubjectFilterTags
            .AsNoTracking()
            .Where(t => t.SubjectFilterId == entity.Id)
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(tagEntities, Has.Count.EqualTo(3));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(SubjectFilterTagEntity.TagName)).EqualTo("existing"));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(SubjectFilterTagEntity.TagName)).EqualTo("newTag1"));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(SubjectFilterTagEntity.TagName)).EqualTo("newTag2"));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesTags_RemoveOnly()
    {
        // Arrange
        EnsureTagsExist("keepMe", "removeMe");
        var entity = new SubjectFilterEntity
        {
            Name = "TestFilter",
            Criteria = JsonSerializer.Serialize(new MetadataContributionCriteria()),
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Tags.Add(new() { TagName = "keepMe" });
        entity.Tags.Add(new() { TagName = "removeMe" });
        _context.SubjectFilters.Add(entity);
        _context.SaveChanges();

        var patch = new SubjectFilterPatch
        {
            Id = entity.Id,
            Tags = new(Add: null, Remove: ["removeMe"])
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var tagEntities = await _context.SubjectFilterTags
            .AsNoTracking()
            .Where(t => t.SubjectFilterId == entity.Id)
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(tagEntities, Has.Count.EqualTo(1));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(SubjectFilterTagEntity.TagName)).EqualTo("keepMe"));
            Assert.That(tagEntities, Has.None
                .With.Property(nameof(SubjectFilterTagEntity.TagName)).EqualTo("removeMe"));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesTags_AddAndRemove()
    {
        // Arrange
        EnsureTagsExist("existing", "toRemove", "toAdd");
        var entity = new SubjectFilterEntity
        {
            Name = "TestFilter",
            Criteria = JsonSerializer.Serialize(new MetadataContributionCriteria()),
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Tags.Add(new() { TagName = "existing" });
        entity.Tags.Add(new() { TagName = "toRemove" });
        _context.SubjectFilters.Add(entity);
        _context.SaveChanges();

        var patch = new SubjectFilterPatch
        {
            Id = entity.Id,
            Tags = new(Add: ["toAdd"], Remove: ["toRemove"])
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var tagEntities = await _context.SubjectFilterTags
            .AsNoTracking()
            .Where(t => t.SubjectFilterId == entity.Id)
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(tagEntities, Has.Count.EqualTo(2));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(SubjectFilterTagEntity.TagName)).EqualTo("existing"));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(SubjectFilterTagEntity.TagName)).EqualTo("toAdd"));
            Assert.That(tagEntities, Has.None
                .With.Property(nameof(SubjectFilterTagEntity.TagName)).EqualTo("toRemove"));
        }
    }

    [Test]
    public async Task PatchAsync_ReturnsFalse_WhenNotFound()
    {
        // Arrange
        var patch = new SubjectFilterPatch
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
