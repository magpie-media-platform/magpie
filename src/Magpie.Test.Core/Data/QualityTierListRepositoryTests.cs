using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions.Data;
using Magpie.Core.Data;
using Magpie.Data.Database;
using Magpie.Models.QualityTiers;
using Magpie.Models.Result;
using Magpie.Models.Users;
using Magpie.Test.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NUnit.Framework;

using HistoryEntryEntity = Magpie.Data.Database.Entities.HistoryEntry;
using QualityCriterionEntity = Magpie.Data.Database.Entities.QualityCriterion;
using QualityTierEntity = Magpie.Data.Database.Entities.QualityTier;
using QualityTierListEntity = Magpie.Data.Database.Entities.QualityTierList;
using QualityTierListTagEntity = Magpie.Data.Database.Entities.QualityTierListTag;
using UserEntity = Magpie.Data.Database.Entities.User;

namespace Magpie.Test.Core.Data;

[TestFixture]
public class QualityTierListRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IQualityTierListRepository _subject = null!;
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

        _subject = new QualityTierListRepository(
            _context,
            historyRepo,
            loggerFactory.CreateLogger<QualityTierListRepository>());
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

    [Test]
    public async Task GetByIdAsync_ReturnsQualityTierList()
    {
        // Arrange
        var entity = new QualityTierListEntity
        {
            Name = "TestList",
            UpgradeAllowed = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.QualityTierLists.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByIdAsync(entity.Id);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<QualityTierList?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(QualityTierList.Name)).EqualTo("TestList")
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(QualityTierList.UpgradeAllowed)).True);
    }

    [Test]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        // Act
        var result = await _subject.GetByIdAsync(Guid.NewGuid());

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<QualityTierList?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task GetByIdAsync_IncludesTiersAndCriteria()
    {
        // Arrange
        var entity = new QualityTierListEntity
        {
            Name = "ListWithTiers",
            UpgradeAllowed = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var tier = new QualityTierEntity
        {
            Name = "HD",
            Ordinal = 1,
            IsCutoff = true
        };
        var criterion = new QualityCriterionEntity
        {
            Field = "resolution",
            Operator = QualityCriterionOperator.Equals,
            Value = "1080p"
        };
        tier.Criteria.Add(criterion);
        entity.Tiers.Add(tier);
        _context.QualityTierLists.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByIdAsync(entity.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<QualityTierList?>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(QualityTierList.Tiers))
                    .With.Count.EqualTo(1));
            var success = result as SuccessResult<QualityTierList?>;
            Assert.That(success!.Value!.Tiers[0].Name, Is.EqualTo("HD"));
            Assert.That(success.Value.Tiers[0].Criteria, Has.Count.EqualTo(1));
            Assert.That(success.Value.Tiers[0].Criteria[0].Field, Is.EqualTo("resolution"));
        }
    }

    [Test]
    public async Task GetByIdAsync_IncludesTags()
    {
        // Arrange
        EnsureTagsExist("movies", "tv");
        var entity = new QualityTierListEntity
        {
            Name = "ListWithTags",
            UpgradeAllowed = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Tags.Add(new() { TagName = "movies" });
        entity.Tags.Add(new() { TagName = "tv" });
        _context.QualityTierLists.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByIdAsync(entity.Id);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<QualityTierList?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(QualityTierList.Tags)).With.Count.EqualTo(2));
    }

    [Test]
    public async Task GetAllAsync_ReturnsAllQualityTierLists()
    {
        // Arrange
        var entity1 = new QualityTierListEntity
        {
            Name = "List1",
            UpgradeAllowed = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new QualityTierListEntity
        {
            Name = "List2",
            UpgradeAllowed = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.QualityTierLists.AddRange(entity1, entity2);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetAllAsync();

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<QualityTierList>>>()
            .With.Property(nameof(SuccessResult<>.Value)).With.Count.EqualTo(2));
    }

    [Test]
    public async Task GetAllAsync_ReturnsEmptyList_WhenNoLists()
    {
        // Act
        var result = await _subject.GetAllAsync();

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<QualityTierList>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task GetByNameAsync_ReturnsQualityTierList()
    {
        // Arrange
        var entity = new QualityTierListEntity
        {
            Name = "UniqueListName",
            UpgradeAllowed = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.QualityTierLists.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByNameAsync("UniqueListName");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<QualityTierList?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(QualityTierList.Name))
                .EqualTo("UniqueListName"));
    }

    [Test]
    public async Task GetByNameAsync_ReturnsNull_WhenNotFound()
    {
        // Act
        var result = await _subject.GetByNameAsync("NonExistentList");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<QualityTierList?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task CreateAsync_CreatesQualityTierList()
    {
        // Arrange
        var model = new QualityTierList(
            Guid.Empty,
            "NewList",
            ImmutableList<QualityTier>.Empty,
            true,
            ImmutableList<string>.Empty,
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.CreateAsync(model, _userId);

        // Assert
        var entity = await (
            from q in _context.QualityTierLists.AsNoTracking()
            where q.Name == "NewList"
            select q).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<QualityTierList>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(QualityTierList.Name)).EqualTo("NewList")
                .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(QualityTierList.UpgradeAllowed)).True);
            Assert.That(entity, Is.InstanceOf<QualityTierListEntity>()
                .With.Property(nameof(QualityTierListEntity.Name)).EqualTo("NewList")
                .And.Property(nameof(QualityTierListEntity.CreatedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Create")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("QualityTierList")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesQualityTierListWithTiers()
    {
        // Arrange
        var tiers = ImmutableList.Create(
            new QualityTier(
                Guid.Empty,
                1,
                "4K",
                true,
                [new(Guid.Empty, "resolution", QualityCriterionOperator.Equals, "2160p")]),
            new QualityTier(
                Guid.Empty,
                2,
                "HD",
                false,
                [new(Guid.Empty, "resolution", QualityCriterionOperator.Equals, "1080p")]));

        var model = new QualityTierList(
            Guid.Empty,
            "ListWithTiers",
            tiers,
            true,
            ImmutableList<string>.Empty,
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.CreateAsync(model, _userId);

        // Assert
        var entity = await (
            from q in _context.QualityTierLists
                .AsNoTracking()
                .Include(q => q.Tiers)
                    .ThenInclude(t => t.Criteria)
            where q.Name == "ListWithTiers"
            select q).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<QualityTierList>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(QualityTierList.Tiers)).Count.EqualTo(2));
            Assert.That(entity, Is.Not.Null);
            Assert.That(entity!.Tiers, Has.Count.EqualTo(2));
            Assert.That(entity.Tiers, Has.One
                .With.Property(nameof(QualityTierEntity.Name)).EqualTo("4K")
                .And.Property(nameof(QualityTierEntity.Ordinal)).EqualTo(1)
                .And.Property(nameof(QualityTierEntity.IsCutoff)).True);
            Assert.That(entity.Tiers.SelectMany(t => t.Criteria).ToList(), Has.Count.EqualTo(2));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesQualityTierListWithTags()
    {
        // Arrange
        EnsureTagsExist("movies", "tv", "anime");
        var tags = ImmutableList.Create("movies", "tv", "anime");

        var model = new QualityTierList(
            Guid.Empty,
            "ListWithTags",
            ImmutableList<QualityTier>.Empty,
            true,
            tags,
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.CreateAsync(model, _userId);

        // Assert
        var entity = await (
            from q in _context.QualityTierLists.AsNoTracking().Include(q => q.Tags)
            where q.Name == "ListWithTags"
            select q).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<QualityTierList>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(QualityTierList.Tags))
                    .With.Count.EqualTo(3));
            Assert.That(entity, Is.Not.Null);
            Assert.That(entity!.Tags, Has.Count.EqualTo(3));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesQualityTierList()
    {
        // Arrange
        var entity = new QualityTierListEntity
        {
            Name = "OriginalName",
            UpgradeAllowed = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.QualityTierLists.Add(entity);
        _context.SaveChanges();

        var model = new QualityTierList(
            entity.Id,
            "UpdatedName",
            ImmutableList<QualityTier>.Empty,
            false,
            ImmutableList<string>.Empty,
            entity.CreatedAt,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        var updatedEntity = await (
            from q in _context.QualityTierLists.AsNoTracking()
            where q.Id == entity.Id
            select q).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<QualityTierListEntity>()
                .With.Property(nameof(QualityTierListEntity.Name)).EqualTo("UpdatedName")
                .And.Property(nameof(QualityTierListEntity.UpgradeAllowed)).False
                .And.Property(nameof(QualityTierListEntity.ModifiedAt)).Not.Null
                .And.Property(nameof(QualityTierListEntity.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Update")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("QualityTierList")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateAsync_ReturnsFalse_WhenNotFound()
    {
        // Arrange
        var model = new QualityTierList(
            Guid.NewGuid(),
            "NonExistent",
            ImmutableList<QualityTier>.Empty,
            true,
            ImmutableList<string>.Empty,
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
    public async Task UpdateAsync_ReplacesTiers()
    {
        // Arrange
        var entity = new QualityTierListEntity
        {
            Name = "ListWithTiers",
            UpgradeAllowed = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var oldTier = new QualityTierEntity
        {
            Name = "OldTier",
            Ordinal = 1,
            IsCutoff = false
        };
        entity.Tiers.Add(oldTier);
        _context.QualityTierLists.Add(entity);
        _context.SaveChanges();

        var newTiers = ImmutableList.Create(
            new QualityTier(Guid.Empty, 1, "NewTier1", true, ImmutableList<QualityCriterion>.Empty),
            new QualityTier(Guid.Empty, 2, "NewTier2", false, ImmutableList<QualityCriterion>.Empty));

        var model = new QualityTierList(
            entity.Id,
            entity.Name,
            newTiers,
            entity.UpgradeAllowed,
            ImmutableList<string>.Empty,
            entity.CreatedAt,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        var tierEntities = await _context.QualityTiers
            .AsNoTracking()
            .Where(t => t.QualityTierListId == entity.Id)
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(tierEntities, Has.Count.EqualTo(2));
            Assert.That(tierEntities, Has.None
                .With.Property(nameof(QualityTierEntity.Name)).EqualTo("OldTier"));
            Assert.That(tierEntities, Has.One
                .With.Property(nameof(QualityTierEntity.Name)).EqualTo("NewTier1"));
            Assert.That(tierEntities, Has.One
                .With.Property(nameof(QualityTierEntity.Name)).EqualTo("NewTier2"));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesTags()
    {
        // Arrange
        EnsureTagsExist("existing", "toRemove", "toAdd");
        var entity = new QualityTierListEntity
        {
            Name = "ListWithTags",
            UpgradeAllowed = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Tags.Add(new() { TagName = "existing" });
        entity.Tags.Add(new() { TagName = "toRemove" });
        _context.QualityTierLists.Add(entity);
        _context.SaveChanges();

        var model = new QualityTierList(
            entity.Id,
            entity.Name,
            ImmutableList<QualityTier>.Empty,
            entity.UpgradeAllowed,
            ImmutableList.Create("existing", "toAdd"),
            entity.CreatedAt,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        var tagEntities = await _context.QualityTierListTags
            .AsNoTracking()
            .Where(t => t.QualityTierListId == entity.Id)
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(tagEntities, Has.Count.EqualTo(2));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(QualityTierListTagEntity.TagName)).EqualTo("existing"));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(QualityTierListTagEntity.TagName)).EqualTo("toAdd"));
            Assert.That(tagEntities, Has.None
                .With.Property(nameof(QualityTierListTagEntity.TagName)).EqualTo("toRemove"));
        }
    }

    [Test]
    public async Task DeleteAsync_DeletesQualityTierList()
    {
        // Arrange
        var entity = new QualityTierListEntity
        {
            Name = "ToDelete",
            UpgradeAllowed = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.QualityTierLists.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.DeleteAsync(entity.Id, _userId);

        // Assert
        var entityExists = await _context.QualityTierLists.AnyAsync(q => q.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(entityExists, Is.False);
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Delete")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("QualityTierList")
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
    public async Task DeleteAsync_CascadesToTiersAndCriteria()
    {
        // Arrange
        var entity = new QualityTierListEntity
        {
            Name = "ListWithTiers",
            UpgradeAllowed = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var tier = new QualityTierEntity
        {
            Name = "HD",
            Ordinal = 1,
            IsCutoff = true
        };
        tier.Criteria.Add(new()
        {
            Field = "resolution",
            Operator = QualityCriterionOperator.Equals,
            Value = "1080p"
        });
        entity.Tiers.Add(tier);
        _context.QualityTierLists.Add(entity);
        _context.SaveChanges();

        var listId = entity.Id;

        // Act
        var result = await _subject.DeleteAsync(listId, _userId);

        // Assert
        var tiersExist = await _context.QualityTiers.AnyAsync(t => t.QualityTierListId == listId);
        var criteriaExist = await _context.QualityCriteria.AnyAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(tiersExist, Is.False);
            Assert.That(criteriaExist, Is.False);
        }
    }

    [Test]
    public async Task DeleteAsync_CascadesToTags()
    {
        // Arrange
        EnsureTagsExist("tag1", "tag2");
        var entity = new QualityTierListEntity
        {
            Name = "ListWithTags",
            UpgradeAllowed = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Tags.Add(new() { TagName = "tag1" });
        entity.Tags.Add(new() { TagName = "tag2" });
        _context.QualityTierLists.Add(entity);
        _context.SaveChanges();

        var listId = entity.Id;

        // Act
        var result = await _subject.DeleteAsync(listId, _userId);

        // Assert
        var tagsExist = await _context.QualityTierListTags.AnyAsync(t => t.QualityTierListId == listId);

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
        var entity = new QualityTierListEntity
        {
            Name = "OriginalName",
            UpgradeAllowed = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.QualityTierLists.Add(entity);
        _context.SaveChanges();

        var patch = new QualityTierListPatch
        {
            Id = entity.Id,
            Name = new("PatchedName")
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var updatedEntity = await _context.QualityTierLists
            .AsNoTracking()
            .FirstOrDefaultAsync(q => q.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<QualityTierListEntity>()
                .With.Property(nameof(QualityTierListEntity.Name)).EqualTo("PatchedName")
                .And.Property(nameof(QualityTierListEntity.ModifiedAt)).Not.Null
                .And.Property(nameof(QualityTierListEntity.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Patch")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("QualityTierList")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesUpgradeAllowed()
    {
        // Arrange
        var entity = new QualityTierListEntity
        {
            Name = "TestList",
            UpgradeAllowed = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.QualityTierLists.Add(entity);
        _context.SaveChanges();

        var patch = new QualityTierListPatch
        {
            Id = entity.Id,
            UpgradeAllowed = new(false)
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var updatedEntity = await _context.QualityTierLists
            .AsNoTracking()
            .FirstOrDefaultAsync(q => q.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<QualityTierListEntity>()
                .With.Property(nameof(QualityTierListEntity.UpgradeAllowed)).False);
        }
    }

    [Test]
    public async Task PatchAsync_PatchesTags_AddOnly()
    {
        // Arrange
        EnsureTagsExist("existing", "newTag1", "newTag2");
        var entity = new QualityTierListEntity
        {
            Name = "TestList",
            UpgradeAllowed = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Tags.Add(new() { TagName = "existing" });
        _context.QualityTierLists.Add(entity);
        _context.SaveChanges();

        var patch = new QualityTierListPatch
        {
            Id = entity.Id,
            Tags = new(Add: ["newTag1", "newTag2"], Remove: null)
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var tagEntities = await _context.QualityTierListTags
            .AsNoTracking()
            .Where(t => t.QualityTierListId == entity.Id)
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(tagEntities, Has.Count.EqualTo(3));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesTags_RemoveOnly()
    {
        // Arrange
        EnsureTagsExist("keepMe", "removeMe");
        var entity = new QualityTierListEntity
        {
            Name = "TestList",
            UpgradeAllowed = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Tags.Add(new() { TagName = "keepMe" });
        entity.Tags.Add(new() { TagName = "removeMe" });
        _context.QualityTierLists.Add(entity);
        _context.SaveChanges();

        var patch = new QualityTierListPatch
        {
            Id = entity.Id,
            Tags = new(Add: null, Remove: ["removeMe"])
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var tagEntities = await _context.QualityTierListTags
            .AsNoTracking()
            .Where(t => t.QualityTierListId == entity.Id)
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(tagEntities, Has.Count.EqualTo(1));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(QualityTierListTagEntity.TagName)).EqualTo("keepMe"));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesTiers_AddTier()
    {
        // Arrange
        var entity = new QualityTierListEntity
        {
            Name = "TestList",
            UpgradeAllowed = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.QualityTierLists.Add(entity);
        _context.SaveChanges();

        var newTier = new QualityTier(
            Guid.Empty,
            1,
            "NewTier",
            true,
            [new QualityCriterion(Guid.Empty, "resolution", QualityCriterionOperator.Equals, "1080p")]);

        var patch = new QualityTierListPatch
        {
            Id = entity.Id,
            Tiers = new(
                Add: [newTier],
                Remove: null,
                Update: null)
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var tierEntities = await _context.QualityTiers
            .AsNoTracking()
            .Include(t => t.Criteria)
            .Where(t => t.QualityTierListId == entity.Id)
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(tierEntities, Has.Count.EqualTo(1));
            Assert.That(tierEntities, Has.One
                .With.Property(nameof(QualityTierEntity.Name)).EqualTo("NewTier")
                .And.Property(nameof(QualityTierEntity.Ordinal)).EqualTo(1)
                .And.Property(nameof(QualityTierEntity.IsCutoff)).True);
            Assert.That(tierEntities[0].Criteria, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesTiers_RemoveTier()
    {
        // Arrange
        var entity = new QualityTierListEntity
        {
            Name = "TestList",
            UpgradeAllowed = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var tier = new QualityTierEntity
        {
            Name = "ToRemove",
            Ordinal = 1,
            IsCutoff = false
        };
        tier.Criteria.Add(new()
        {
            Field = "resolution",
            Operator = QualityCriterionOperator.Equals,
            Value = "1080p"
        });
        entity.Tiers.Add(tier);
        _context.QualityTierLists.Add(entity);
        _context.SaveChanges();

        var tierId = tier.Id;

        var patch = new QualityTierListPatch
        {
            Id = entity.Id,
            Tiers = new(
                Add: null,
                Remove: [tierId],
                Update: null)
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var tiersExist = await _context.QualityTiers.AnyAsync(t => t.QualityTierListId == entity.Id);
        var criteriaExist = await _context.QualityCriteria.AnyAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(tiersExist, Is.False);
            Assert.That(criteriaExist, Is.False);
        }
    }

    [Test]
    public async Task PatchAsync_PatchesTiers_UpdateTier()
    {
        // Arrange
        var entity = new QualityTierListEntity
        {
            Name = "TestList",
            UpgradeAllowed = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var tier = new QualityTierEntity
        {
            Name = "OriginalTier",
            Ordinal = 1,
            IsCutoff = false
        };
        entity.Tiers.Add(tier);
        _context.QualityTierLists.Add(entity);
        _context.SaveChanges();

        var tierPatch = new QualityTierPatch
        {
            Id = tier.Id,
            Name = new("UpdatedTier"),
            IsCutoff = new(true)
        };

        var patch = new QualityTierListPatch
        {
            Id = entity.Id,
            Tiers = new(
                Add: null,
                Remove: null,
                Update: [tierPatch])
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var tierEntity = await _context.QualityTiers
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tier.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(tierEntity, Is.InstanceOf<QualityTierEntity>()
                .With.Property(nameof(QualityTierEntity.Name)).EqualTo("UpdatedTier")
                .And.Property(nameof(QualityTierEntity.IsCutoff)).True);
        }
    }

    [Test]
    public async Task PatchAsync_PatchesCriteria_AddCriterion()
    {
        // Arrange
        var entity = new QualityTierListEntity
        {
            Name = "TestList",
            UpgradeAllowed = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var tier = new QualityTierEntity
        {
            Name = "TestTier",
            Ordinal = 1,
            IsCutoff = false
        };
        entity.Tiers.Add(tier);
        _context.QualityTierLists.Add(entity);
        _context.SaveChanges();

        var newCriterion = new QualityCriterion(
            Guid.Empty,
            "resolution",
            QualityCriterionOperator.Equals,
            "1080p");

        var tierPatch = new QualityTierPatch
        {
            Id = tier.Id,
            Criteria = new(
                Add: [newCriterion],
                Remove: null,
                Update: null)
        };

        var patch = new QualityTierListPatch
        {
            Id = entity.Id,
            Tiers = new(
                Add: null,
                Remove: null,
                Update: [tierPatch])
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var criterionEntities = await _context.QualityCriteria
            .AsNoTracking()
            .Where(c => c.QualityTierId == tier.Id)
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(criterionEntities, Has.Count.EqualTo(1));
            Assert.That(criterionEntities, Has.One
                .With.Property(nameof(QualityCriterionEntity.Field)).EqualTo("resolution")
                .And.Property(nameof(QualityCriterionEntity.Value)).EqualTo("1080p"));
        }
    }

    [Test]
    public async Task PatchAsync_ReturnsFalse_WhenNotFound()
    {
        // Arrange
        var patch = new QualityTierListPatch
        {
            Id = Guid.NewGuid(),
            Name = new("NewName")
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).False);
    }

    [Test]
    public async Task GetAllAsync_ReturnsTiersInOrdinalOrder()
    {
        // Arrange
        var entity = new QualityTierListEntity
        {
            Name = "ListWithOrderedTiers",
            UpgradeAllowed = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Tiers.Add(new() { Name = "Third", Ordinal = 3, IsCutoff = false });
        entity.Tiers.Add(new() { Name = "First", Ordinal = 1, IsCutoff = true });
        entity.Tiers.Add(new() { Name = "Second", Ordinal = 2, IsCutoff = false });
        _context.QualityTierLists.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetAllAsync();

        // Assert
        var success = result as SuccessResult<IReadOnlyList<QualityTierList>>;
        Assert.That(success, Is.Not.Null);
        var tiers = success!.Value[0].Tiers;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(tiers[0].Name, Is.EqualTo("First"));
            Assert.That(tiers[1].Name, Is.EqualTo("Second"));
            Assert.That(tiers[2].Name, Is.EqualTo("Third"));
        }
    }
}
