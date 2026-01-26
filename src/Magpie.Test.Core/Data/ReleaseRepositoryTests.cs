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
using IndexerDefinitionEntity = Magpie.Data.Database.Entities.IndexerDefinition;
using IndexerEntity = Magpie.Data.Database.Entities.Indexer;
using ReleaseCategoryEntity = Magpie.Data.Database.Entities.ReleaseCategory;
using ReleaseEntity = Magpie.Data.Database.Entities.Release;
using ReleasePropertyEntity = Magpie.Data.Database.Entities.ReleaseProperty;
using ReleaseTagEntity = Magpie.Data.Database.Entities.ReleaseTag;
using UserEntity = Magpie.Data.Database.Entities.User;

namespace Magpie.Test.Core.Data;

[TestFixture]
public class ReleaseRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IReleaseRepository _subject = null!;
    private Guid _userId;
    private Guid _indexerId;

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

        var definition = new IndexerDefinitionEntity
        {
            Provider = "torznab",
            Name = "TestDefinition",
            Access = IndexerAccess.Public,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _context.Users.Add(user);
        _context.IndexerDefinitions.Add(definition);
        _context.SaveChanges();

        var indexer = new IndexerEntity
        {
            Name = "TestIndexer",
            DefinitionId = definition.Id,
            Enabled = true,
            Status = IndexerStatus.Healthy,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _context.Indexers.Add(indexer);
        _context.SaveChanges();

        _userId = user.Id;
        _indexerId = indexer.Id;

        var loggerFactory = LoggerFactory.Create(b => b.AddTestLogger());
        var historyRepo = new HistoryRepository(_context, loggerFactory.CreateLogger<HistoryRepository>());

        _subject = new ReleaseRepository(_context, historyRepo, loggerFactory.CreateLogger<ReleaseRepository>());
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
    public async Task GetByIdAsync_ReturnsRelease()
    {
        // Arrange
        var entity = new ReleaseEntity
        {
            IndexerId = _indexerId,
            Provider = "torznab",
            Title = "TestRelease",
            PublishDate = DateTimeOffset.UtcNow,
            DownloadUrl = new("https://example.com/download"),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Releases.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByIdAsync(entity.Id);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<Release?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(Release.Title)).EqualTo("TestRelease")
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(Release.Provider)).EqualTo("torznab"));
    }

    [Test]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        // Act
        var result = await _subject.GetByIdAsync(Guid.NewGuid());

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<Release?>>().With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task GetByIdAsync_IncludesCollections()
    {
        // Arrange
        EnsureTagsExist("movies", "hd");
        var entity = new ReleaseEntity
        {
            IndexerId = _indexerId,
            Provider = "torznab",
            Title = "ReleaseWithCollections",
            PublishDate = DateTimeOffset.UtcNow,
            DownloadUrl = new("https://example.com/download"),
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Categories.Add(new() { Category = "Movies" });
        entity.Categories.Add(new() { Category = "HD" });
        entity.Languages.Add(new() { Language = "en" });
        entity.Properties.Add(new() { Key = "quality", Value = "1080p" });
        entity.Tags.Add(new() { TagName = "movies" });
        entity.Tags.Add(new() { TagName = "hd" });
        _context.Releases.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByIdAsync(entity.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<Release?>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(Release.Categories))
                    .With.Count.EqualTo(2));
            Assert.That(result, Is.InstanceOf<SuccessResult<Release?>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(Release.Languages))
                    .With.Count.EqualTo(1));
            Assert.That(result, Is.InstanceOf<SuccessResult<Release?>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(Release.Properties))
                    .With.Count.EqualTo(1));
            Assert.That(result, Is.InstanceOf<SuccessResult<Release?>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(Release.Tags)).With.Count.EqualTo(2));
        }
    }

    [Test]
    public async Task GetAllAsync_ReturnsAllReleases()
    {
        // Arrange
        var entity1 = new ReleaseEntity
        {
            IndexerId = _indexerId,
            Provider = "torznab",
            Title = "Release1",
            PublishDate = DateTimeOffset.UtcNow,
            DownloadUrl = new("https://example.com/download1"),
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new ReleaseEntity
        {
            IndexerId = _indexerId,
            Provider = "newznab",
            Title = "Release2",
            PublishDate = DateTimeOffset.UtcNow,
            DownloadUrl = new("https://example.com/download2"),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Releases.AddRange(entity1, entity2);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetAllAsync();

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<Release>>>()
            .With.Property(nameof(SuccessResult<>.Value)).With.Count.EqualTo(2));
    }

    [Test]
    public async Task GetAllAsync_ReturnsEmptyList_WhenNoReleases()
    {
        // Act
        var result = await _subject.GetAllAsync();

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<Release>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task GetByIndexerIdAsync_ReturnsReleases()
    {
        // Arrange
        var entity1 = new ReleaseEntity
        {
            IndexerId = _indexerId,
            Provider = "torznab",
            Title = "Release1",
            PublishDate = DateTimeOffset.UtcNow,
            DownloadUrl = new("https://example.com/download1"),
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new ReleaseEntity
        {
            IndexerId = _indexerId,
            Provider = "torznab",
            Title = "Release2",
            PublishDate = DateTimeOffset.UtcNow,
            DownloadUrl = new("https://example.com/download2"),
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity3 = new ReleaseEntity
        {
            IndexerId = null,
            Provider = "manual",
            Title = "Release3",
            PublishDate = DateTimeOffset.UtcNow,
            DownloadUrl = new("https://example.com/download3"),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Releases.AddRange(entity1, entity2, entity3);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByIndexerIdAsync(_indexerId);

        // Assert
        var success = result as SuccessResult<IReadOnlyList<Release>>;
        Assert.That(success, Is.Not.Null);
        Assert.That(success!.Value, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task GetByIndexerIdAsync_ReturnsEmptyList_WhenNoMatches()
    {
        // Act
        var result = await _subject.GetByIndexerIdAsync(Guid.NewGuid());

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<Release>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task GetByExternalIdAsync_ReturnsRelease()
    {
        // Arrange
        var entity = new ReleaseEntity
        {
            IndexerId = _indexerId,
            ExternalId = "ext-123",
            Provider = "torznab",
            Title = "TestRelease",
            PublishDate = DateTimeOffset.UtcNow,
            DownloadUrl = new("https://example.com/download"),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Releases.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByExternalIdAsync(_indexerId, "ext-123");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<Release?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(Release.Title)).EqualTo("TestRelease")
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(Release.ExternalId)).EqualTo("ext-123"));
    }

    [Test]
    public async Task GetByExternalIdAsync_ReturnsNull_WhenNotFound()
    {
        // Act
        var result = await _subject.GetByExternalIdAsync(_indexerId, "nonexistent");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<Release?>>().With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task GetByProviderAsync_ReturnsReleases()
    {
        // Arrange
        var entity1 = new ReleaseEntity
        {
            IndexerId = _indexerId,
            Provider = "torznab",
            Title = "Release1",
            PublishDate = DateTimeOffset.UtcNow,
            DownloadUrl = new("https://example.com/download1"),
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new ReleaseEntity
        {
            IndexerId = _indexerId,
            Provider = "torznab",
            Title = "Release2",
            PublishDate = DateTimeOffset.UtcNow,
            DownloadUrl = new("https://example.com/download2"),
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity3 = new ReleaseEntity
        {
            IndexerId = _indexerId,
            Provider = "newznab",
            Title = "Release3",
            PublishDate = DateTimeOffset.UtcNow,
            DownloadUrl = new("https://example.com/download3"),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Releases.AddRange(entity1, entity2, entity3);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByProviderAsync("torznab");

        // Assert
        var success = result as SuccessResult<IReadOnlyList<Release>>;
        Assert.That(success, Is.Not.Null);
        Assert.That(success!.Value, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task GetByProviderAsync_ReturnsEmptyList_WhenNoMatches()
    {
        // Act
        var result = await _subject.GetByProviderAsync("nonexistent");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<Release>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task GetByInfoHashAsync_ReturnsReleases()
    {
        // Arrange
        var entity1 = new ReleaseEntity
        {
            IndexerId = _indexerId,
            Provider = "torznab",
            Title = "Release1",
            InfoHash = "abc123",
            PublishDate = DateTimeOffset.UtcNow,
            DownloadUrl = new("https://example.com/download1"),
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new ReleaseEntity
        {
            IndexerId = _indexerId,
            Provider = "torznab",
            Title = "Release2",
            InfoHash = "abc123",
            PublishDate = DateTimeOffset.UtcNow,
            DownloadUrl = new("https://example.com/download2"),
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity3 = new ReleaseEntity
        {
            IndexerId = _indexerId,
            Provider = "torznab",
            Title = "Release3",
            InfoHash = "xyz789",
            PublishDate = DateTimeOffset.UtcNow,
            DownloadUrl = new("https://example.com/download3"),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Releases.AddRange(entity1, entity2, entity3);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByInfoHashAsync("abc123");

        // Assert
        var success = result as SuccessResult<IReadOnlyList<Release>>;
        Assert.That(success, Is.Not.Null);
        Assert.That(success!.Value, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task GetByInfoHashAsync_ReturnsEmptyList_WhenNoMatches()
    {
        // Act
        var result = await _subject.GetByInfoHashAsync("nonexistent");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<Release>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task GetByTagAsync_ReturnsReleases()
    {
        // Arrange
        EnsureTagsExist("shared", "unique");
        var entity1 = new ReleaseEntity
        {
            IndexerId = _indexerId,
            Provider = "torznab",
            Title = "Release1",
            PublishDate = DateTimeOffset.UtcNow,
            DownloadUrl = new("https://example.com/download1"),
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity1.Tags.Add(new() { TagName = "shared" });
        var entity2 = new ReleaseEntity
        {
            IndexerId = _indexerId,
            Provider = "torznab",
            Title = "Release2",
            PublishDate = DateTimeOffset.UtcNow,
            DownloadUrl = new("https://example.com/download2"),
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity2.Tags.Add(new() { TagName = "shared" });
        var entity3 = new ReleaseEntity
        {
            IndexerId = _indexerId,
            Provider = "torznab",
            Title = "Release3",
            PublishDate = DateTimeOffset.UtcNow,
            DownloadUrl = new("https://example.com/download3"),
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity3.Tags.Add(new() { TagName = "unique" });
        _context.Releases.AddRange(entity1, entity2, entity3);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByTagAsync("shared");

        // Assert
        var success = result as SuccessResult<IReadOnlyList<Release>>;
        Assert.That(success, Is.Not.Null);
        Assert.That(success!.Value, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task GetByTagAsync_ReturnsEmptyList_WhenNoMatches()
    {
        // Act
        var result = await _subject.GetByTagAsync("nonexistent");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<Release>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task CreateAsync_CreatesRelease()
    {
        // Arrange
        var model = new Release(
            Guid.Empty,
            _indexerId,
            "ext-456",
            "torznab",
            "NewRelease",
            DateTimeOffset.UtcNow,
            1024 * 1024 * 100,
            "hash123",
            [],
            new("https://example.com/download"),
            new("https://example.com/info"),
            ImmutableDictionary<string, string?>.Empty,
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
            from r in _context.Releases.AsNoTracking()
            where r.Title == "NewRelease"
            select r).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<Release>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(Release.Title)).EqualTo("NewRelease")
                .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(Release.Provider)).EqualTo("torznab"));
            Assert.That(entity, Is.InstanceOf<ReleaseEntity>()
                .With.Property(nameof(ReleaseEntity.Title)).EqualTo("NewRelease")
                .And.Property(nameof(ReleaseEntity.CreatedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Create")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("Release")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesReleaseWithCollections()
    {
        // Arrange
        EnsureTagsExist("movies", "hd");
        var categories = ImmutableList.Create("Movies", "HD");
        var languages = ImmutableList.Create("en", "es");
        var properties = new Dictionary<string, string?>
        {
            ["quality"] = "1080p",
            ["codec"] = "x264"
        }.ToImmutableDictionary();
        var tags = ImmutableList.Create("movies", "hd");

        var model = new Release(
            Guid.Empty,
            _indexerId,
            null,
            "torznab",
            "ReleaseWithCollections",
            DateTimeOffset.UtcNow,
            null,
            null,
            categories,
            new("https://example.com/download"),
            null,
            properties,
            languages,
            tags,
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.CreateAsync(model, _userId);

        // Assert
        var entity = await (
            from r in _context.Releases
                .AsNoTracking()
                .Include(r => r.Categories)
                .Include(r => r.Languages)
                .Include(r => r.Properties)
                .Include(r => r.Tags)
            where r.Title == "ReleaseWithCollections"
            select r).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<Release>>());
            Assert.That(entity, Is.Not.Null);
            Assert.That(entity!.Categories, Has.Count.EqualTo(2));
            Assert.That(entity.Languages, Has.Count.EqualTo(2));
            Assert.That(entity.Properties, Has.Count.EqualTo(2));
            Assert.That(entity.Tags, Has.Count.EqualTo(2));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesRelease()
    {
        // Arrange
        var entity = new ReleaseEntity
        {
            IndexerId = _indexerId,
            Provider = "torznab",
            Title = "OriginalTitle",
            PublishDate = DateTimeOffset.UtcNow,
            DownloadUrl = new("https://example.com/download"),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Releases.Add(entity);
        _context.SaveChanges();

        var model = new Release(
            entity.Id,
            _indexerId,
            "updated-ext",
            "newznab",
            "UpdatedTitle",
            DateTimeOffset.UtcNow,
            2048,
            "newhash",
            [],
            new("https://example.com/updated"),
            new("https://example.com/info"),
            ImmutableDictionary<string, string?>.Empty,
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
            from r in _context.Releases.AsNoTracking()
            where r.Id == entity.Id
            select r).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<ReleaseEntity>()
                .With.Property(nameof(ReleaseEntity.Title)).EqualTo("UpdatedTitle")
                .And.Property(nameof(ReleaseEntity.Provider)).EqualTo("newznab")
                .And.Property(nameof(ReleaseEntity.ModifiedAt)).Not.Null
                .And.Property(nameof(ReleaseEntity.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Update")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("Release")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateAsync_ReturnsFalse_WhenNotFound()
    {
        // Arrange
        var model = new Release(
            Guid.NewGuid(),
            _indexerId,
            null,
            "torznab",
            "NonExistent",
            DateTimeOffset.UtcNow,
            null,
            null,
            [],
            new("https://example.com/download"),
            null,
            ImmutableDictionary<string, string?>.Empty,
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
    public async Task UpdateAsync_UpdatesCollections()
    {
        // Arrange
        EnsureTagsExist("original", "newTag");
        var entity = new ReleaseEntity
        {
            IndexerId = _indexerId,
            Provider = "torznab",
            Title = "ReleaseToUpdate",
            PublishDate = DateTimeOffset.UtcNow,
            DownloadUrl = new("https://example.com/download"),
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Categories.Add(new() { Category = "OldCategory" });
        entity.Languages.Add(new() { Language = "de" });
        entity.Properties.Add(new() { Key = "old", Value = "value" });
        entity.Tags.Add(new() { TagName = "original" });
        _context.Releases.Add(entity);
        _context.SaveChanges();

        var model = new Release(
            entity.Id,
            _indexerId,
            null,
            "torznab",
            "ReleaseToUpdate",
            DateTimeOffset.UtcNow,
            null,
            null,
            ImmutableList.Create("NewCategory1", "NewCategory2"),
            new("https://example.com/download"),
            null,
            new Dictionary<string, string?> { ["new"] = "value" },
            ImmutableList.Create("en", "fr"),
            ["newTag"],
            entity.CreatedAt,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        var updatedEntity = await _context.Releases
            .AsNoTracking()
            .Include(r => r.Categories)
            .Include(r => r.Languages)
            .Include(r => r.Properties)
            .Include(r => r.Tags)
            .FirstOrDefaultAsync(r => r.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.Not.Null);
            Assert.That(updatedEntity!.Categories, Has.Count.EqualTo(2));
            Assert.That(updatedEntity.Categories, Has.One
                .With.Property(nameof(ReleaseCategoryEntity.Category)).EqualTo("NewCategory1"));
            Assert.That(updatedEntity.Languages, Has.Count.EqualTo(2));
            Assert.That(updatedEntity.Properties, Has.Count.EqualTo(1));
            Assert.That(updatedEntity.Properties, Has.One
                .With.Property(nameof(ReleasePropertyEntity.Key)).EqualTo("new"));
            Assert.That(updatedEntity.Tags, Has.Count.EqualTo(1));
            Assert.That(updatedEntity.Tags, Has.One
                .With.Property(nameof(ReleaseTagEntity.TagName)).EqualTo("newTag"));
        }
    }

    [Test]
    public async Task DeleteAsync_DeletesRelease()
    {
        // Arrange
        var entity = new ReleaseEntity
        {
            IndexerId = _indexerId,
            Provider = "torznab",
            Title = "ToDelete",
            PublishDate = DateTimeOffset.UtcNow,
            DownloadUrl = new("https://example.com/download"),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Releases.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.DeleteAsync(entity.Id, _userId);

        // Assert
        var entityExists = await _context.Releases.AnyAsync(r => r.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(entityExists, Is.False);
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Delete")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("Release")
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
    public async Task DeleteAsync_CascadesToCollections()
    {
        // Arrange
        EnsureTagsExist("tag1", "tag2");
        var entity = new ReleaseEntity
        {
            IndexerId = _indexerId,
            Provider = "torznab",
            Title = "ReleaseWithCollections",
            PublishDate = DateTimeOffset.UtcNow,
            DownloadUrl = new("https://example.com/download"),
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Categories.Add(new() { Category = "Cat1" });
        entity.Categories.Add(new() { Category = "Cat2" });
        entity.Languages.Add(new() { Language = "en" });
        entity.Properties.Add(new() { Key = "key1", Value = "val1" });
        entity.Tags.Add(new() { TagName = "tag1" });
        entity.Tags.Add(new() { TagName = "tag2" });
        _context.Releases.Add(entity);
        _context.SaveChanges();

        var releaseId = entity.Id;

        // Act
        var result = await _subject.DeleteAsync(releaseId, _userId);

        // Assert
        var categoriesExist = await _context.ReleaseCategories.AnyAsync(c => c.ReleaseId == releaseId);
        var languagesExist = await _context.ReleaseLanguages.AnyAsync(l => l.ReleaseId == releaseId);
        var propertiesExist = await _context.ReleaseProperties.AnyAsync(p => p.ReleaseId == releaseId);
        var tagsExist = await _context.ReleaseTags.AnyAsync(t => t.ReleaseId == releaseId);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(categoriesExist, Is.False);
            Assert.That(languagesExist, Is.False);
            Assert.That(propertiesExist, Is.False);
            Assert.That(tagsExist, Is.False);
        }
    }

    [Test]
    public async Task DeleteByIndexerIdAsync_DeletesReleasesForIndexer()
    {
        // Arrange
        var entity1 = new ReleaseEntity
        {
            IndexerId = _indexerId,
            Provider = "torznab",
            Title = "Release1",
            PublishDate = DateTimeOffset.UtcNow,
            DownloadUrl = new("https://example.com/download1"),
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new ReleaseEntity
        {
            IndexerId = _indexerId,
            Provider = "torznab",
            Title = "Release2",
            PublishDate = DateTimeOffset.UtcNow,
            DownloadUrl = new("https://example.com/download2"),
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity3 = new ReleaseEntity
        {
            IndexerId = null,
            Provider = "manual",
            Title = "Release3",
            PublishDate = DateTimeOffset.UtcNow,
            DownloadUrl = new("https://example.com/download3"),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Releases.AddRange(entity1, entity2, entity3);
        _context.SaveChanges();

        // Act
        var result = await _subject.DeleteByIndexerIdAsync(_indexerId, _userId);

        // Assert
        var indexerReleasesExist = await _context.Releases.AnyAsync(r => r.IndexerId == _indexerId);
        var manualReleaseExists = await _context.Releases.AnyAsync(r => r.Title == "Release3");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<int>>()
                .With.Property(nameof(SuccessResult<>.Value)).EqualTo(2));
            Assert.That(indexerReleasesExist, Is.False);
            Assert.That(manualReleaseExists, Is.True);
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Delete")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("Release"));
        }
    }

    [Test]
    public async Task DeleteByIndexerIdAsync_ReturnsZero_WhenNoMatches()
    {
        // Act
        var result = await _subject.DeleteByIndexerIdAsync(Guid.NewGuid(), _userId);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<int>>()
            .With.Property(nameof(SuccessResult<>.Value)).EqualTo(0));
    }

    [Test]
    public async Task DeleteByIndexerIdAsync_CascadesToCollections()
    {
        // Arrange
        EnsureTagsExist("tag1");
        var entity = new ReleaseEntity
        {
            IndexerId = _indexerId,
            Provider = "torznab",
            Title = "ReleaseWithCollections",
            PublishDate = DateTimeOffset.UtcNow,
            DownloadUrl = new("https://example.com/download"),
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Categories.Add(new() { Category = "Cat1" });
        entity.Languages.Add(new() { Language = "en" });
        entity.Properties.Add(new() { Key = "key1", Value = "val1" });
        entity.Tags.Add(new() { TagName = "tag1" });
        _context.Releases.Add(entity);
        _context.SaveChanges();

        var releaseId = entity.Id;

        // Act
        var result = await _subject.DeleteByIndexerIdAsync(_indexerId, _userId);

        // Assert
        var categoriesExist = await _context.ReleaseCategories.AnyAsync(c => c.ReleaseId == releaseId);
        var languagesExist = await _context.ReleaseLanguages.AnyAsync(l => l.ReleaseId == releaseId);
        var propertiesExist = await _context.ReleaseProperties.AnyAsync(p => p.ReleaseId == releaseId);
        var tagsExist = await _context.ReleaseTags.AnyAsync(t => t.ReleaseId == releaseId);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<int>>()
                .With.Property(nameof(SuccessResult<>.Value)).EqualTo(1));
            Assert.That(categoriesExist, Is.False);
            Assert.That(languagesExist, Is.False);
            Assert.That(propertiesExist, Is.False);
            Assert.That(tagsExist, Is.False);
        }
    }
}
