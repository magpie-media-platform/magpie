using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions.Data;
using Magpie.Core.Data;
using Magpie.Data.Database;
using Magpie.Models.Indexers;
using Magpie.Models.Result;
using Magpie.Models.Users;
using Magpie.Test.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NUnit.Framework;

using HistoryEntryEntity = Magpie.Data.Database.Entities.HistoryEntry;
using IndexerDefinitionCategoryEntity = Magpie.Data.Database.Entities.IndexerDefinitionCategory;
using IndexerDefinitionEntity = Magpie.Data.Database.Entities.IndexerDefinition;
using IndexerDefinitionLanguageEntity = Magpie.Data.Database.Entities.IndexerDefinitionLanguage;
using IndexerDefinitionTagEntity = Magpie.Data.Database.Entities.IndexerDefinitionTag;
using IndexerFeedEntity = Magpie.Data.Database.Entities.IndexerFeed;
using UserEntity = Magpie.Data.Database.Entities.User;

namespace Magpie.Test.Core.Data;

[TestFixture]
public class IndexerDefinitionRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IIndexerDefinitionRepository _subject = null!;
    private Guid _userId;
    private Guid _feedId;

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

        var feed = new IndexerFeedEntity
        {
            Name = "TestFeed",
            Provider = "torznab",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _context.Users.Add(user);
        _context.IndexerFeeds.Add(feed);
        _context.SaveChanges();

        _userId = user.Id;
        _feedId = feed.Id;

        var loggerFactory = LoggerFactory.Create(b => b.AddTestLogger());
        var historyRepo = new HistoryRepository(_context, loggerFactory.CreateLogger<HistoryRepository>());

        _subject = new IndexerDefinitionRepository(
            _context,
            historyRepo,
            loggerFactory.CreateLogger<IndexerDefinitionRepository>());
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
    public async Task GetByIdAsync_ReturnsDefinition()
    {
        // Arrange
        var entity = new IndexerDefinitionEntity
        {
            Provider = "torznab",
            Name = "TestDefinition",
            Access = IndexerAccess.Public,
            Description = "Test description",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.IndexerDefinitions.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByIdAsync(entity.Id);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IndexerDefinition?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(IndexerDefinition.Name))
                .EqualTo("TestDefinition")
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(IndexerDefinition.Provider))
                .EqualTo("torznab")
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(IndexerDefinition.Access))
                .EqualTo(IndexerAccess.Public)
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(IndexerDefinition.Description))
                .EqualTo("Test description"));
    }

    [Test]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        // Act
        var result = await _subject.GetByIdAsync(Guid.NewGuid());

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IndexerDefinition?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task GetByIdAsync_IncludesLanguagesCategoriesAndTags()
    {
        // Arrange
        EnsureTagsExist("movies", "tv");
        var entity = new IndexerDefinitionEntity
        {
            Provider = "torznab",
            Name = "DefinitionWithChildren",
            Access = IndexerAccess.Public,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Languages.Add(new() { Language = "en" });
        entity.Languages.Add(new() { Language = "de" });
        entity.Categories.Add(new() { Category = "Movies" });
        entity.Categories.Add(new() { Category = "TV" });
        entity.Tags.Add(new() { TagName = "movies" });
        entity.Tags.Add(new() { TagName = "tv" });
        _context.IndexerDefinitions.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByIdAsync(entity.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<IndexerDefinition?>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(IndexerDefinition.Languages))
                    .With.Count.EqualTo(2));
            Assert.That(result, Is.InstanceOf<SuccessResult<IndexerDefinition?>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(IndexerDefinition.Categories))
                    .With.Count.EqualTo(2));
            Assert.That(result, Is.InstanceOf<SuccessResult<IndexerDefinition?>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(IndexerDefinition.Tags))
                    .With.Count.EqualTo(2));
        }
    }

    [Test]
    public async Task GetAllAsync_ReturnsAllDefinitions()
    {
        // Arrange
        var entity1 = new IndexerDefinitionEntity
        {
            Provider = "torznab",
            Name = "Definition1",
            Access = IndexerAccess.Public,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new IndexerDefinitionEntity
        {
            Provider = "newznab",
            Name = "Definition2",
            Access = IndexerAccess.Private,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.IndexerDefinitions.AddRange(entity1, entity2);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetAllAsync();

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<IndexerDefinition>>>()
            .With.Property(nameof(SuccessResult<>.Value)).With.Count.EqualTo(2));
    }

    [Test]
    public async Task GetAllAsync_ReturnsEmptyList_WhenNoDefinitions()
    {
        // Act
        var result = await _subject.GetAllAsync();

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<IndexerDefinition>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task GetAllAsync_IncludesLanguagesCategoriesAndTags()
    {
        // Arrange
        EnsureTagsExist("books");
        var entity = new IndexerDefinitionEntity
        {
            Provider = "torznab",
            Name = "DefinitionWithChildren",
            Access = IndexerAccess.Public,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Languages.Add(new() { Language = "en" });
        entity.Categories.Add(new() { Category = "Books" });
        entity.Tags.Add(new() { TagName = "books" });
        _context.IndexerDefinitions.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetAllAsync();

        // Assert
        var success = result as SuccessResult<IReadOnlyList<IndexerDefinition>>;
        Assert.That(success, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(success!.Value[0].Languages, Has.Count.EqualTo(1));
            Assert.That(success.Value[0].Categories, Has.Count.EqualTo(1));
            Assert.That(success.Value[0].Tags, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesDefinition()
    {
        // Arrange
        var model = new IndexerDefinition(
            Guid.Empty,
            null,
            "torznab",
            "NewDefinition",
            IndexerAccess.Public,
            "A new definition",
            [],
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
            from d in _context.IndexerDefinitions.AsNoTracking()
            where d.Name == "NewDefinition"
            select d).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<IndexerDefinition>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(IndexerDefinition.Name))
                    .EqualTo("NewDefinition")
                .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(IndexerDefinition.Provider))
                    .EqualTo("torznab")
                .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(IndexerDefinition.Access))
                    .EqualTo(IndexerAccess.Public));
            Assert.That(entity, Is.InstanceOf<IndexerDefinitionEntity>()
                .With.Property(nameof(IndexerDefinitionEntity.Name)).EqualTo("NewDefinition")
                .And.Property(nameof(IndexerDefinitionEntity.CreatedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Create")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("IndexerDefinition")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesDefinitionWithLanguages()
    {
        // Arrange
        var languages = ImmutableList.Create("en", "de", "fr");

        var model = new IndexerDefinition(
            Guid.Empty,
            null,
            "torznab",
            "DefinitionWithLanguages",
            IndexerAccess.Public,
            null,
            languages,
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
            from d in _context.IndexerDefinitions.AsNoTracking().Include(d => d.Languages)
            where d.Name == "DefinitionWithLanguages"
            select d).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<IndexerDefinition>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(IndexerDefinition.Languages))
                    .With.Count.EqualTo(3));
            Assert.That(entity, Is.Not.Null);
            Assert.That(entity!.Languages, Has.Count.EqualTo(3));
            Assert.That(entity.Languages, Has.One
                .With.Property(nameof(IndexerDefinitionLanguageEntity.Language)).EqualTo("en"));
            Assert.That(entity.Languages, Has.One
                .With.Property(nameof(IndexerDefinitionLanguageEntity.Language)).EqualTo("de"));
            Assert.That(entity.Languages, Has.One
                .With.Property(nameof(IndexerDefinitionLanguageEntity.Language)).EqualTo("fr"));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesDefinitionWithCategories()
    {
        // Arrange
        var categories = ImmutableList.Create("Movies", "TV", "Music");

        var model = new IndexerDefinition(
            Guid.Empty,
            null,
            "torznab",
            "DefinitionWithCategories",
            IndexerAccess.Public,
            null,
            [],
            categories,
            [],
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.CreateAsync(model, _userId);

        // Assert
        var entity = await (
            from d in _context.IndexerDefinitions.AsNoTracking().Include(d => d.Categories)
            where d.Name == "DefinitionWithCategories"
            select d).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<IndexerDefinition>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(IndexerDefinition.Categories))
                    .With.Count.EqualTo(3));
            Assert.That(entity, Is.Not.Null);
            Assert.That(entity!.Categories, Has.Count.EqualTo(3));
            Assert.That(entity.Categories, Has.One
                .With.Property(nameof(IndexerDefinitionCategoryEntity.Category)).EqualTo("Movies"));
            Assert.That(entity.Categories, Has.One
                .With.Property(nameof(IndexerDefinitionCategoryEntity.Category)).EqualTo("TV"));
            Assert.That(entity.Categories, Has.One
                .With.Property(nameof(IndexerDefinitionCategoryEntity.Category)).EqualTo("Music"));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesDefinitionWithTags()
    {
        // Arrange
        EnsureTagsExist("movies", "tv", "anime");
        var tags = ImmutableList.Create("movies", "tv", "anime");

        var model = new IndexerDefinition(
            Guid.Empty,
            null,
            "torznab",
            "DefinitionWithTags",
            IndexerAccess.Public,
            null,
            [],
            [],
            tags,
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.CreateAsync(model, _userId);

        // Assert
        var entity = await (
            from d in _context.IndexerDefinitions.AsNoTracking().Include(d => d.Tags)
            where d.Name == "DefinitionWithTags"
            select d).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<IndexerDefinition>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(IndexerDefinition.Tags))
                    .With.Count.EqualTo(3));
            Assert.That(entity, Is.Not.Null);
            Assert.That(entity!.Tags, Has.Count.EqualTo(3));
            Assert.That(entity.Tags, Has.One
                .With.Property(nameof(IndexerDefinitionTagEntity.TagName)).EqualTo("movies"));
            Assert.That(entity.Tags, Has.One
                .With.Property(nameof(IndexerDefinitionTagEntity.TagName)).EqualTo("tv"));
            Assert.That(entity.Tags, Has.One
                .With.Property(nameof(IndexerDefinitionTagEntity.TagName)).EqualTo("anime"));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesDefinitionWithFeed()
    {
        // Arrange
        var model = new IndexerDefinition(
            Guid.Empty,
            _feedId,
            "torznab",
            "DefinitionWithFeed",
            IndexerAccess.Public,
            null,
            [],
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
            from d in _context.IndexerDefinitions.AsNoTracking()
            where d.Name == "DefinitionWithFeed"
            select d).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<IndexerDefinition>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(IndexerDefinition.FeedId))
                .EqualTo(_feedId));
            Assert.That(entity, Is.Not.Null);
            Assert.That(entity!.FeedId, Is.EqualTo(_feedId));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesDefinition()
    {
        // Arrange
        var entity = new IndexerDefinitionEntity
        {
            Provider = "torznab",
            Name = "OriginalName",
            Access = IndexerAccess.Public,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.IndexerDefinitions.Add(entity);
        _context.SaveChanges();

        var model = new IndexerDefinition(
            entity.Id,
            _feedId,
            "newznab",
            "UpdatedName",
            IndexerAccess.Private,
            "Updated description",
            [],
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
            from d in _context.IndexerDefinitions.AsNoTracking()
            where d.Id == entity.Id
            select d).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<IndexerDefinitionEntity>()
                .With.Property(nameof(IndexerDefinitionEntity.Name)).EqualTo("UpdatedName")
                .And.Property(nameof(IndexerDefinitionEntity.Provider)).EqualTo("newznab")
                .And.Property(nameof(IndexerDefinitionEntity.Access)).EqualTo(IndexerAccess.Private)
                .And.Property(nameof(IndexerDefinitionEntity.FeedId)).EqualTo(_feedId)
                .And.Property(nameof(IndexerDefinitionEntity.Description)).EqualTo("Updated description")
                .And.Property(nameof(IndexerDefinitionEntity.ModifiedAt)).Not.Null
                .And.Property(nameof(IndexerDefinitionEntity.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Update")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("IndexerDefinition")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateAsync_ReturnsFalse_WhenNotFound()
    {
        // Arrange
        var model = new IndexerDefinition(
            Guid.NewGuid(),
            null,
            "torznab",
            "NonExistent",
            IndexerAccess.Public,
            null,
            [],
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
    public async Task UpdateAsync_AddsAndRemovesLanguages()
    {
        // Arrange
        var entity = new IndexerDefinitionEntity
        {
            Provider = "torznab",
            Name = "DefinitionToUpdate",
            Access = IndexerAccess.Public,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Languages.Add(new() { Language = "en" });
        entity.Languages.Add(new() { Language = "de" });
        _context.IndexerDefinitions.Add(entity);
        _context.SaveChanges();

        var updatedLanguages = ImmutableList.Create("en", "fr");

        var model = new IndexerDefinition(
            entity.Id,
            null,
            entity.Provider,
            entity.Name,
            entity.Access,
            null,
            updatedLanguages,
            [],
            [],
            entity.CreatedAt,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        var languageEntities = await _context.IndexerDefinitionLanguages
            .AsNoTracking()
            .Where(l => l.IndexerDefinitionId == entity.Id)
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(languageEntities, Has.Count.EqualTo(2));
            Assert.That(languageEntities, Has.One
                .With.Property(nameof(IndexerDefinitionLanguageEntity.Language)).EqualTo("en"));
            Assert.That(languageEntities, Has.One
                .With.Property(nameof(IndexerDefinitionLanguageEntity.Language)).EqualTo("fr"));
            Assert.That(languageEntities, Has.None
                .With.Property(nameof(IndexerDefinitionLanguageEntity.Language)).EqualTo("de"));
        }
    }

    [Test]
    public async Task UpdateAsync_AddsAndRemovesCategories()
    {
        // Arrange
        var entity = new IndexerDefinitionEntity
        {
            Provider = "torznab",
            Name = "DefinitionToUpdate",
            Access = IndexerAccess.Public,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Categories.Add(new() { Category = "Movies" });
        entity.Categories.Add(new() { Category = "TV" });
        _context.IndexerDefinitions.Add(entity);
        _context.SaveChanges();

        var updatedCategories = ImmutableList.Create("Movies", "Music");

        var model = new IndexerDefinition(
            entity.Id,
            null,
            entity.Provider,
            entity.Name,
            entity.Access,
            null,
            [],
            updatedCategories,
            [],
            entity.CreatedAt,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        var categoryEntities = await _context.IndexerDefinitionCategories
            .AsNoTracking()
            .Where(c => c.IndexerDefinitionId == entity.Id)
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(categoryEntities, Has.Count.EqualTo(2));
            Assert.That(categoryEntities, Has.One
                .With.Property(nameof(IndexerDefinitionCategoryEntity.Category)).EqualTo("Movies"));
            Assert.That(categoryEntities, Has.One
                .With.Property(nameof(IndexerDefinitionCategoryEntity.Category)).EqualTo("Music"));
            Assert.That(categoryEntities, Has.None
                .With.Property(nameof(IndexerDefinitionCategoryEntity.Category)).EqualTo("TV"));
        }
    }

    [Test]
    public async Task UpdateAsync_AddsAndRemovesTags()
    {
        // Arrange
        EnsureTagsExist("movies", "tv", "anime");
        var entity = new IndexerDefinitionEntity
        {
            Provider = "torznab",
            Name = "DefinitionToUpdate",
            Access = IndexerAccess.Public,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Tags.Add(new() { TagName = "movies" });
        entity.Tags.Add(new() { TagName = "tv" });
        _context.IndexerDefinitions.Add(entity);
        _context.SaveChanges();

        var updatedTags = ImmutableList.Create("movies", "anime");

        var model = new IndexerDefinition(
            entity.Id,
            null,
            entity.Provider,
            entity.Name,
            entity.Access,
            null,
            [],
            [],
            updatedTags,
            entity.CreatedAt,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        var tagEntities = await _context.IndexerDefinitionTags
            .AsNoTracking()
            .Where(t => t.IndexerDefinitionId == entity.Id)
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(tagEntities, Has.Count.EqualTo(2));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(IndexerDefinitionTagEntity.TagName)).EqualTo("movies"));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(IndexerDefinitionTagEntity.TagName)).EqualTo("anime"));
            Assert.That(tagEntities, Has.None
                .With.Property(nameof(IndexerDefinitionTagEntity.TagName)).EqualTo("tv"));
        }
    }

    [Test]
    public async Task DeleteAsync_DeletesDefinition()
    {
        // Arrange
        var entity = new IndexerDefinitionEntity
        {
            Provider = "torznab",
            Name = "ToDelete",
            Access = IndexerAccess.Public,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.IndexerDefinitions.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.DeleteAsync(entity.Id, _userId);

        // Assert
        var entityExists = await _context.IndexerDefinitions.AnyAsync(d => d.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(entityExists, Is.False);
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Delete")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("IndexerDefinition")
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
    public async Task DeleteAsync_CascadesToLanguagesCategoriesAndTags()
    {
        // Arrange
        EnsureTagsExist("tag1");
        var entity = new IndexerDefinitionEntity
        {
            Provider = "torznab",
            Name = "DefinitionWithChildren",
            Access = IndexerAccess.Public,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Languages.Add(new() { Language = "en" });
        entity.Categories.Add(new() { Category = "Movies" });
        entity.Tags.Add(new() { TagName = "tag1" });
        _context.IndexerDefinitions.Add(entity);
        _context.SaveChanges();

        var definitionId = entity.Id;

        // Act
        var result = await _subject.DeleteAsync(definitionId, _userId);

        // Assert
        var languagesExist = await _context.IndexerDefinitionLanguages
            .AnyAsync(l => l.IndexerDefinitionId == definitionId);
        var categoriesExist = await _context.IndexerDefinitionCategories
            .AnyAsync(c => c.IndexerDefinitionId == definitionId);
        var tagsExist = await _context.IndexerDefinitionTags
            .AnyAsync(t => t.IndexerDefinitionId == definitionId);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(languagesExist, Is.False);
            Assert.That(categoriesExist, Is.False);
            Assert.That(tagsExist, Is.False);
        }
    }

    [Test]
    public async Task GetByNameAsync_ReturnsDefinition()
    {
        // Arrange
        var entity = new IndexerDefinitionEntity
        {
            Provider = "torznab",
            Name = "UniqueDefinitionName",
            Access = IndexerAccess.Public,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.IndexerDefinitions.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByNameAsync("UniqueDefinitionName");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IndexerDefinition?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(IndexerDefinition.Name))
                .EqualTo("UniqueDefinitionName")
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(IndexerDefinition.Provider))
                .EqualTo("torznab"));
    }

    [Test]
    public async Task GetByNameAsync_ReturnsNull_WhenNotFound()
    {
        // Act
        var result = await _subject.GetByNameAsync("NonExistentDefinition");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IndexerDefinition?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task GetByNameAsync_IncludesLanguagesCategoriesAndTags()
    {
        // Arrange
        EnsureTagsExist("primary");
        var entity = new IndexerDefinitionEntity
        {
            Provider = "torznab",
            Name = "DefinitionWithChildren",
            Access = IndexerAccess.Public,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Languages.Add(new() { Language = "en" });
        entity.Categories.Add(new() { Category = "Movies" });
        entity.Tags.Add(new() { TagName = "primary" });
        _context.IndexerDefinitions.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByNameAsync("DefinitionWithChildren");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<IndexerDefinition?>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(IndexerDefinition.Languages))
                    .With.Count.EqualTo(1));
            Assert.That(result, Is.InstanceOf<SuccessResult<IndexerDefinition?>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(IndexerDefinition.Categories))
                    .With.Count.EqualTo(1));
            Assert.That(result, Is.InstanceOf<SuccessResult<IndexerDefinition?>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(IndexerDefinition.Tags))
                    .With.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task GetByFeedIdAsync_ReturnsDefinitions()
    {
        // Arrange
        var entity1 = new IndexerDefinitionEntity
        {
            Provider = "torznab",
            Name = "Definition1",
            Access = IndexerAccess.Public,
            FeedId = _feedId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new IndexerDefinitionEntity
        {
            Provider = "torznab",
            Name = "Definition2",
            Access = IndexerAccess.Public,
            FeedId = _feedId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity3 = new IndexerDefinitionEntity
        {
            Provider = "newznab",
            Name = "Definition3",
            Access = IndexerAccess.Private,
            FeedId = null,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.IndexerDefinitions.AddRange(entity1, entity2, entity3);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByFeedIdAsync(_feedId);

        // Assert
        var success = result as SuccessResult<IReadOnlyList<IndexerDefinition>>;
        Assert.That(success, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(success!.Value, Has.Count.EqualTo(2));
            Assert.That(success.Value, Has.All
                .With.Property(nameof(IndexerDefinition.FeedId)).EqualTo(_feedId));
        }
    }

    [Test]
    public async Task GetByFeedIdAsync_ReturnsEmptyList_WhenNoMatches()
    {
        // Act
        var result = await _subject.GetByFeedIdAsync(Guid.NewGuid());

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<IndexerDefinition>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task GetByProviderAsync_ReturnsDefinitions()
    {
        // Arrange
        var entity1 = new IndexerDefinitionEntity
        {
            Provider = "torznab",
            Name = "Definition1",
            Access = IndexerAccess.Public,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new IndexerDefinitionEntity
        {
            Provider = "torznab",
            Name = "Definition2",
            Access = IndexerAccess.Private,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity3 = new IndexerDefinitionEntity
        {
            Provider = "newznab",
            Name = "Definition3",
            Access = IndexerAccess.Public,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.IndexerDefinitions.AddRange(entity1, entity2, entity3);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByProviderAsync("torznab");

        // Assert
        var success = result as SuccessResult<IReadOnlyList<IndexerDefinition>>;
        Assert.That(success, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(success!.Value, Has.Count.EqualTo(2));
            Assert.That(success.Value, Has.All
                .With.Property(nameof(IndexerDefinition.Provider)).EqualTo("torznab"));
        }
    }

    [Test]
    public async Task GetByProviderAsync_ReturnsEmptyList_WhenNoMatches()
    {
        // Act
        var result = await _subject.GetByProviderAsync("nonexistent");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<IndexerDefinition>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task GetByAccessAsync_ReturnsDefinitions()
    {
        // Arrange
        var entity1 = new IndexerDefinitionEntity
        {
            Provider = "torznab",
            Name = "PublicDefinition1",
            Access = IndexerAccess.Public,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new IndexerDefinitionEntity
        {
            Provider = "torznab",
            Name = "PublicDefinition2",
            Access = IndexerAccess.Public,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity3 = new IndexerDefinitionEntity
        {
            Provider = "newznab",
            Name = "PrivateDefinition",
            Access = IndexerAccess.Private,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.IndexerDefinitions.AddRange(entity1, entity2, entity3);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByAccessAsync(IndexerAccess.Public);

        // Assert
        var success = result as SuccessResult<IReadOnlyList<IndexerDefinition>>;
        Assert.That(success, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(success!.Value, Has.Count.EqualTo(2));
            Assert.That(success.Value, Has.All
                .With.Property(nameof(IndexerDefinition.Access)).EqualTo(IndexerAccess.Public));
        }
    }

    [Test]
    public async Task GetByAccessAsync_ReturnsEmptyList_WhenNoMatches()
    {
        // Arrange
        var entity = new IndexerDefinitionEntity
        {
            Provider = "torznab",
            Name = "PublicDefinition",
            Access = IndexerAccess.Public,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.IndexerDefinitions.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByAccessAsync(IndexerAccess.SemiPrivate);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<IndexerDefinition>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }
}
