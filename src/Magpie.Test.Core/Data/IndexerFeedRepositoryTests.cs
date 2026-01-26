using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions.Data;
using Magpie.Core.Data;
using Magpie.Data.Database;
using Magpie.Models;
using Magpie.Models.Indexers;
using Magpie.Models.Result;
using Magpie.Models.Users;
using Magpie.Test.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NUnit.Framework;

using HistoryEntryEntity = Magpie.Data.Database.Entities.HistoryEntry;
using IndexerFeedEntity = Magpie.Data.Database.Entities.IndexerFeed;
using IndexerFeedSettingEntity = Magpie.Data.Database.Entities.IndexerFeedSetting;
using UserEntity = Magpie.Data.Database.Entities.User;

namespace Magpie.Test.Core.Data;

[TestFixture]
public class IndexerFeedRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IIndexerFeedRepository _subject = null!;
    private Guid _userId;

    [SetUp]
    public void SetUp()
    {
        _context = new MagpieDbContext(new DbContextOptionsBuilder<MagpieDbContext>()
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

        _subject = new IndexerFeedRepository(
            _context,
            historyRepo,
            loggerFactory.CreateLogger<IndexerFeedRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context?.Dispose();
    }

    [Test]
    public async Task GetByIdAsync_ReturnsIndexerFeed()
    {
        // Arrange
        var entity = new IndexerFeedEntity
        {
            Name = "TestFeed",
            Provider = "newznab",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.IndexerFeeds.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByIdAsync(entity.Id, default);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IndexerFeed?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(IndexerFeed.Name)).EqualTo("TestFeed")
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(IndexerFeed.Provider)).EqualTo("newznab")
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(IndexerFeed.Enabled)).True);
    }

    [Test]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        // Act
        var result = await _subject.GetByIdAsync(Guid.NewGuid(), default);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IndexerFeed?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task GetByIdAsync_IncludesSettings()
    {
        // Arrange
        var entity = new IndexerFeedEntity
        {
            Name = "FeedWithSettings",
            Provider = "newznab",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Settings.Add(new() { Key = "url", Value = "https://example.com" });
        entity.Settings.Add(new() { Key = "apiKey", Value = "secret123" });
        _context.IndexerFeeds.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByIdAsync(entity.Id, default);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IndexerFeed?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(IndexerFeed.Settings)).With.Count.EqualTo(2));
    }

    [Test]
    public async Task GetAllAsync_ReturnsAllIndexerFeeds()
    {
        // Arrange
        var entity1 = new IndexerFeedEntity
        {
            Name = "Feed1",
            Provider = "newznab",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new IndexerFeedEntity
        {
            Name = "Feed2",
            Provider = "torznab",
            Enabled = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.IndexerFeeds.AddRange(entity1, entity2);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetAllAsync(default);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<IndexerFeed>>>()
            .With.Property(nameof(SuccessResult<>.Value)).With.Count.EqualTo(2));
    }

    [Test]
    public async Task GetAllAsync_ReturnsEmptyList_WhenNoFeeds()
    {
        // Act
        var result = await _subject.GetAllAsync(default);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<IndexerFeed>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task GetAllAsync_IncludesSettings()
    {
        // Arrange
        var entity = new IndexerFeedEntity
        {
            Name = "FeedWithSettings",
            Provider = "newznab",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Settings.Add(new() { Key = "apiKey", Value = "test-key" });
        _context.IndexerFeeds.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetAllAsync(default);

        // Assert
        var success = result as SuccessResult<IReadOnlyList<IndexerFeed>>;
        Assert.That(success, Is.Not.Null);
        Assert.That(success!.Value[0].Settings, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task CreateAsync_CreatesIndexerFeed()
    {
        // Arrange
        var model = new IndexerFeed(
            Guid.Empty,
            "NewFeed",
            "Test description",
            "newznab",
            true,
            ImmutableDictionary<string, string?>.Empty,
            null,
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.CreateAsync(model, _userId, default);

        // Assert
        var entity = await (
            from f in _context.IndexerFeeds.AsNoTracking()
            where f.Name == "NewFeed"
            select f).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<IndexerFeed>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(IndexerFeed.Name))
                    .EqualTo("NewFeed")
                .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(IndexerFeed.Provider))
                    .EqualTo("newznab")
                .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(IndexerFeed.Description))
                    .EqualTo("Test description")
                .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(IndexerFeed.Enabled))
                    .True);
            Assert.That(entity, Is.InstanceOf<IndexerFeedEntity>()
                .With.Property(nameof(IndexerFeedEntity.Name)).EqualTo("NewFeed")
                .And.Property(nameof(IndexerFeedEntity.Provider)).EqualTo("newznab")
                .And.Property(nameof(IndexerFeedEntity.CreatedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Create")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("IndexerFeed")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesIndexerFeedWithSettings()
    {
        // Arrange
        var settings = new Dictionary<string, string?>
        {
            ["url"] = "https://indexer.example.com",
            ["apiKey"] = "abc123",
            ["categories"] = "5000,5040"
        }.ToImmutableDictionary();

        var model = new IndexerFeed(
            Guid.Empty,
            "FeedWithSettings",
            null,
            "newznab",
            true,
            settings,
            null,
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.CreateAsync(model, _userId, default);

        // Assert
        var entity = await (
            from f in _context.IndexerFeeds.AsNoTracking().Include(f => f.Settings)
            where f.Name == "FeedWithSettings"
            select f).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<IndexerFeed>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(IndexerFeed.Settings)).Count.EqualTo(3));
            Assert.That(entity, Is.Not.Null);
            Assert.That(entity!.Settings, Has.Count.EqualTo(3));
            Assert.That(entity.Settings, Has.One
                .With.Property(nameof(IndexerFeedSettingEntity.Key)).EqualTo("url")
                .And.Property(nameof(IndexerFeedSettingEntity.Value)).EqualTo("https://indexer.example.com"));
            Assert.That(entity.Settings, Has.One
                .With.Property(nameof(IndexerFeedSettingEntity.Key)).EqualTo("apiKey")
                .And.Property(nameof(IndexerFeedSettingEntity.Value)).EqualTo("abc123"));
            Assert.That(entity.Settings, Has.One
                .With.Property(nameof(IndexerFeedSettingEntity.Key)).EqualTo("categories")
                .And.Property(nameof(IndexerFeedSettingEntity.Value)).EqualTo("5000,5040"));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesIndexerFeed()
    {
        // Arrange
        var entity = new IndexerFeedEntity
        {
            Name = "OriginalName",
            Provider = "newznab",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.IndexerFeeds.Add(entity);
        _context.SaveChanges();

        var model = new IndexerFeed(
            entity.Id,
            "UpdatedName",
            "Updated description",
            "torznab",
            false,
            ImmutableDictionary<string, string?>.Empty,
            DateTimeOffset.UtcNow,
            entity.CreatedAt,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _userId, default);

        // Assert
        var updatedEntity = await (
            from f in _context.IndexerFeeds.AsNoTracking()
            where f.Id == entity.Id
            select f).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<IndexerFeedEntity>()
                .With.Property(nameof(IndexerFeedEntity.Name)).EqualTo("UpdatedName")
                .And.Property(nameof(IndexerFeedEntity.Description)).EqualTo("Updated description")
                .And.Property(nameof(IndexerFeedEntity.Provider)).EqualTo("torznab")
                .And.Property(nameof(IndexerFeedEntity.Enabled)).False
                .And.Property(nameof(IndexerFeedEntity.ModifiedAt)).Not.Null
                .And.Property(nameof(IndexerFeedEntity.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Update")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("IndexerFeed")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateAsync_ReturnsFalse_WhenNotFound()
    {
        // Arrange
        var model = new IndexerFeed(
            Guid.NewGuid(),
            "NonExistent",
            null,
            "newznab",
            true,
            ImmutableDictionary<string, string?>.Empty,
            null,
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _userId, default);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).False);
    }

    [Test]
    public async Task UpdateAsync_AddsNewSettings()
    {
        // Arrange
        var entity = new IndexerFeedEntity
        {
            Name = "FeedToUpdate",
            Provider = "newznab",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.IndexerFeeds.Add(entity);
        _context.SaveChanges();

        var newSettings = new Dictionary<string, string?>
        {
            ["newSetting1"] = "value1",
            ["newSetting2"] = "value2"
        }.ToImmutableDictionary();

        var model = new IndexerFeed(
            entity.Id,
            entity.Name,
            entity.Description,
            entity.Provider,
            entity.Enabled,
            newSettings,
            entity.LastSync,
            entity.CreatedAt,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _userId, default);

        // Assert
        var settingEntities = await _context.IndexerFeedSettings
            .AsNoTracking()
            .Where(s => s.IndexerFeedId == entity.Id)
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(settingEntities, Has.Count.EqualTo(2));
            Assert.That(settingEntities, Has.One
                .With.Property(nameof(IndexerFeedSettingEntity.Key)).EqualTo("newSetting1")
                .And.Property(nameof(IndexerFeedSettingEntity.Value)).EqualTo("value1"));
            Assert.That(settingEntities, Has.One
                .With.Property(nameof(IndexerFeedSettingEntity.Key)).EqualTo("newSetting2")
                .And.Property(nameof(IndexerFeedSettingEntity.Value)).EqualTo("value2"));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesExistingSettings()
    {
        // Arrange
        var entity = new IndexerFeedEntity
        {
            Name = "FeedWithSettings",
            Provider = "newznab",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Settings.Add(new() { Key = "existingKey", Value = "oldValue" });
        _context.IndexerFeeds.Add(entity);
        _context.SaveChanges();

        var updatedSettings = new Dictionary<string, string?>
        {
            ["existingKey"] = "newValue"
        }.ToImmutableDictionary();

        var model = new IndexerFeed(
            entity.Id,
            entity.Name,
            entity.Description,
            entity.Provider,
            entity.Enabled,
            updatedSettings,
            entity.LastSync,
            entity.CreatedAt,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _userId, default);

        // Assert
        var settingEntity = await _context.IndexerFeedSettings
            .AsNoTracking()
            .Where(s => s.IndexerFeedId == entity.Id)
            .Where(s => s.Key == "existingKey")
            .SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(settingEntity, Is.InstanceOf<IndexerFeedSettingEntity>()
                .With.Property(nameof(IndexerFeedSettingEntity.Value)).EqualTo("newValue"));
        }
    }

    [Test]
    public async Task UpdateAsync_RemovesSettings()
    {
        // Arrange
        var entity = new IndexerFeedEntity
        {
            Name = "FeedWithSettings",
            Provider = "newznab",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Settings.Add(new() { Key = "keepMe", Value = "keep" });
        entity.Settings.Add(new() { Key = "removeMe", Value = "remove" });
        _context.IndexerFeeds.Add(entity);
        _context.SaveChanges();

        var updatedSettings = new Dictionary<string, string?>
        {
            ["keepMe"] = "keep"
        }.ToImmutableDictionary();

        var model = new IndexerFeed(
            entity.Id,
            entity.Name,
            entity.Description,
            entity.Provider,
            entity.Enabled,
            updatedSettings,
            entity.LastSync,
            entity.CreatedAt,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _userId, default);

        // Assert
        var settingEntities = await _context.IndexerFeedSettings
            .AsNoTracking()
            .Where(s => s.IndexerFeedId == entity.Id)
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(settingEntities, Has.Count.EqualTo(1));
            Assert.That(settingEntities, Has.One
                .With.Property(nameof(IndexerFeedSettingEntity.Key)).EqualTo("keepMe"));
            Assert.That(settingEntities, Has.None
                .With.Property(nameof(IndexerFeedSettingEntity.Key)).EqualTo("removeMe"));
        }
    }

    [Test]
    public async Task UpdateAsync_AddsUpdatesAndRemovesSettings()
    {
        // Arrange
        var entity = new IndexerFeedEntity
        {
            Name = "ComplexFeed",
            Provider = "newznab",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Settings.Add(new() { Key = "toUpdate", Value = "oldValue" });
        entity.Settings.Add(new() { Key = "toRemove", Value = "removeMe" });
        _context.IndexerFeeds.Add(entity);
        _context.SaveChanges();

        var updatedSettings = new Dictionary<string, string?>
        {
            ["toUpdate"] = "newValue",
            ["toAdd"] = "addedValue"
        }.ToImmutableDictionary();

        var model = new IndexerFeed(
            entity.Id,
            entity.Name,
            entity.Description,
            entity.Provider,
            entity.Enabled,
            updatedSettings,
            entity.LastSync,
            entity.CreatedAt,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _userId, default);

        // Assert
        var settingEntities = await _context.IndexerFeedSettings
            .AsNoTracking()
            .Where(s => s.IndexerFeedId == entity.Id)
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(settingEntities, Has.Count.EqualTo(2));
            Assert.That(settingEntities, Has.One
                .With.Property(nameof(IndexerFeedSettingEntity.Key)).EqualTo("toUpdate")
                .And.Property(nameof(IndexerFeedSettingEntity.Value)).EqualTo("newValue"));
            Assert.That(settingEntities, Has.One
                .With.Property(nameof(IndexerFeedSettingEntity.Key)).EqualTo("toAdd")
                .And.Property(nameof(IndexerFeedSettingEntity.Value)).EqualTo("addedValue"));
            Assert.That(settingEntities, Has.None
                .With.Property(nameof(IndexerFeedSettingEntity.Key)).EqualTo("toRemove"));
        }
    }

    [Test]
    public async Task DeleteAsync_DeletesIndexerFeed()
    {
        // Arrange
        var entity = new IndexerFeedEntity
        {
            Name = "ToDelete",
            Provider = "newznab",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.IndexerFeeds.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.DeleteAsync(entity.Id, _userId, default);

        // Assert
        var entityExists = await _context.IndexerFeeds.AnyAsync(f => f.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(entityExists, Is.False);
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Delete")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("IndexerFeed")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task DeleteAsync_ReturnsFalse_WhenNotFound()
    {
        // Act
        var result = await _subject.DeleteAsync(Guid.NewGuid(), _userId, default);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).False);
    }

    [Test]
    public async Task DeleteAsync_CascadesToSettings()
    {
        // Arrange
        var entity = new IndexerFeedEntity
        {
            Name = "FeedWithSettings",
            Provider = "newznab",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Settings.Add(new() { Key = "setting1", Value = "value1" });
        entity.Settings.Add(new() { Key = "setting2", Value = "value2" });
        _context.IndexerFeeds.Add(entity);
        _context.SaveChanges();

        var feedId = entity.Id;

        // Act
        var result = await _subject.DeleteAsync(feedId, _userId, default);

        // Assert
        var settingsExist = await _context.IndexerFeedSettings
            .AnyAsync(s => s.IndexerFeedId == feedId);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(settingsExist, Is.False);
        }
    }

    [Test]
    public async Task PatchAsync_PatchesName()
    {
        // Arrange
        var entity = new IndexerFeedEntity
        {
            Name = "OriginalName",
            Provider = "newznab",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.IndexerFeeds.Add(entity);
        _context.SaveChanges();

        var patch = new IndexerFeedPatch
        {
            Id = entity.Id,
            Name = new FieldPatch<string>("PatchedName")
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId, default);

        // Assert
        var updatedEntity = await _context.IndexerFeeds
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<IndexerFeedEntity>()
                .With.Property(nameof(IndexerFeedEntity.Name)).EqualTo("PatchedName")
                .And.Property(nameof(IndexerFeedEntity.ModifiedAt)).Not.Null
                .And.Property(nameof(IndexerFeedEntity.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Update")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("IndexerFeed")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesDescription()
    {
        // Arrange
        var entity = new IndexerFeedEntity
        {
            Name = "TestFeed",
            Description = "Original description",
            Provider = "newznab",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.IndexerFeeds.Add(entity);
        _context.SaveChanges();

        var patch = new IndexerFeedPatch
        {
            Id = entity.Id,
            Description = new FieldPatch<string?>("Patched description")
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId, default);

        // Assert
        var updatedEntity = await _context.IndexerFeeds
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<IndexerFeedEntity>()
                .With.Property(nameof(IndexerFeedEntity.Description)).EqualTo("Patched description"));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesEnabled()
    {
        // Arrange
        var entity = new IndexerFeedEntity
        {
            Name = "TestFeed",
            Provider = "newznab",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.IndexerFeeds.Add(entity);
        _context.SaveChanges();

        var patch = new IndexerFeedPatch
        {
            Id = entity.Id,
            Enabled = new FieldPatch<bool>(false)
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId, default);

        // Assert
        var updatedEntity = await _context.IndexerFeeds
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<IndexerFeedEntity>()
                .With.Property(nameof(IndexerFeedEntity.Enabled)).False);
        }
    }

    [Test]
    public async Task PatchAsync_PatchesSettings()
    {
        // Arrange
        var entity = new IndexerFeedEntity
        {
            Name = "TestFeed",
            Provider = "newznab",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Settings.Add(new() { Key = "toUpdate", Value = "oldValue" });
        entity.Settings.Add(new() { Key = "toRemove", Value = "removeMe" });
        _context.IndexerFeeds.Add(entity);
        _context.SaveChanges();

        var patch = new IndexerFeedPatch
        {
            Id = entity.Id,
            Settings = new(
                new Dictionary<string, string?>
                {
                    ["toAdd"] = "addedValue"
                },
                ["toRemove"],
                new Dictionary<string, string?>
                {
                    ["toUpdate"] = "newValue"
                })
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId, default);

        // Assert
        var settingEntities = await _context.IndexerFeedSettings
            .AsNoTracking()
            .Where(s => s.IndexerFeedId == entity.Id)
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(settingEntities, Has.Count.EqualTo(2));
            Assert.That(settingEntities, Has.One
                .With.Property(nameof(IndexerFeedSettingEntity.Key)).EqualTo("toUpdate")
                .And.Property(nameof(IndexerFeedSettingEntity.Value)).EqualTo("newValue"));
            Assert.That(settingEntities, Has.One
                .With.Property(nameof(IndexerFeedSettingEntity.Key)).EqualTo("toAdd")
                .And.Property(nameof(IndexerFeedSettingEntity.Value)).EqualTo("addedValue"));
            Assert.That(settingEntities, Has.None
                .With.Property(nameof(IndexerFeedSettingEntity.Key)).EqualTo("toRemove"));
        }
    }

    [Test]
    public async Task PatchAsync_ReturnsFalse_WhenNotFound()
    {
        // Arrange
        var patch = new IndexerFeedPatch
        {
            Id = Guid.NewGuid(),
            Name = new FieldPatch<string>("NewName")
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId, default);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).False);
    }

    [Test]
    public async Task GetByNameAsync_ReturnsIndexerFeed()
    {
        // Arrange
        var entity = new IndexerFeedEntity
        {
            Name = "UniqueFeedName",
            Provider = "torznab",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.IndexerFeeds.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByNameAsync("UniqueFeedName", default);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IndexerFeed?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(IndexerFeed.Name)).EqualTo("UniqueFeedName")
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(IndexerFeed.Provider)).EqualTo("torznab"));
    }

    [Test]
    public async Task GetByNameAsync_ReturnsNull_WhenNotFound()
    {
        // Act
        var result = await _subject.GetByNameAsync("NonExistentFeed", default);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IndexerFeed?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task GetByNameAsync_IncludesSettings()
    {
        // Arrange
        var entity = new IndexerFeedEntity
        {
            Name = "FeedWithSettings",
            Provider = "newznab",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Settings.Add(new() { Key = "url", Value = "https://example.com" });
        _context.IndexerFeeds.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByNameAsync("FeedWithSettings", default);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IndexerFeed?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(IndexerFeed.Settings)).With.Count.EqualTo(1));
    }

    [Test]
    public async Task GetByProviderAsync_ReturnsIndexerFeeds()
    {
        // Arrange
        var entity1 = new IndexerFeedEntity
        {
            Name = "NewznabFeed1",
            Provider = "newznab",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new IndexerFeedEntity
        {
            Name = "NewznabFeed2",
            Provider = "newznab",
            Enabled = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity3 = new IndexerFeedEntity
        {
            Name = "TorznabFeed",
            Provider = "torznab",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.IndexerFeeds.AddRange(entity1, entity2, entity3);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByProviderAsync("newznab", default);

        // Assert
        var success = result as SuccessResult<IReadOnlyList<IndexerFeed>>;
        Assert.That(success, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(success!.Value, Has.Count.EqualTo(2));
            Assert.That(success.Value, Has.All
                .With.Property(nameof(IndexerFeed.Provider)).EqualTo("newznab"));
        }
    }

    [Test]
    public async Task GetByProviderAsync_ReturnsEmptyList_WhenNoMatches()
    {
        // Arrange
        var entity = new IndexerFeedEntity
        {
            Name = "NewznabFeed",
            Provider = "newznab",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.IndexerFeeds.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByProviderAsync("rss", default);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<IndexerFeed>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task GetByProviderAsync_IncludesSettings()
    {
        // Arrange
        var entity = new IndexerFeedEntity
        {
            Name = "NewznabFeed",
            Provider = "newznab",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Settings.Add(new() { Key = "apiKey", Value = "abc" });
        _context.IndexerFeeds.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByProviderAsync("newznab", default);

        // Assert
        var success = result as SuccessResult<IReadOnlyList<IndexerFeed>>;
        Assert.That(success, Is.Not.Null);
        Assert.That(success!.Value[0].Settings, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task GetEnabledAsync_ReturnsEnabledIndexerFeeds()
    {
        // Arrange
        var entity1 = new IndexerFeedEntity
        {
            Name = "EnabledFeed1",
            Provider = "newznab",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new IndexerFeedEntity
        {
            Name = "EnabledFeed2",
            Provider = "torznab",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity3 = new IndexerFeedEntity
        {
            Name = "DisabledFeed",
            Provider = "newznab",
            Enabled = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.IndexerFeeds.AddRange(entity1, entity2, entity3);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetEnabledAsync(default);

        // Assert
        var success = result as SuccessResult<IReadOnlyList<IndexerFeed>>;
        Assert.That(success, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(success!.Value, Has.Count.EqualTo(2));
            Assert.That(success.Value, Has.All
                .With.Property(nameof(IndexerFeed.Enabled)).True);
        }
    }

    [Test]
    public async Task GetEnabledAsync_ReturnsEmptyList_WhenNoEnabledFeeds()
    {
        // Arrange
        var entity = new IndexerFeedEntity
        {
            Name = "DisabledFeed",
            Provider = "newznab",
            Enabled = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.IndexerFeeds.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetEnabledAsync(default);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<IndexerFeed>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task GetEnabledAsync_IncludesSettings()
    {
        // Arrange
        var entity = new IndexerFeedEntity
        {
            Name = "EnabledFeed",
            Provider = "newznab",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Settings.Add(new() { Key = "url", Value = "https://example.com" });
        _context.IndexerFeeds.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetEnabledAsync(default);

        // Assert
        var success = result as SuccessResult<IReadOnlyList<IndexerFeed>>;
        Assert.That(success, Is.Not.Null);
        Assert.That(success!.Value[0].Settings, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task UpdateLastSyncAsync_UpdatesLastSync()
    {
        // Arrange
        var entity = new IndexerFeedEntity
        {
            Name = "TestFeed",
            Provider = "newznab",
            Enabled = true,
            LastSync = null,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.IndexerFeeds.Add(entity);
        _context.SaveChanges();

        var newLastSync = DateTimeOffset.UtcNow;

        // Act
        var result = await _subject.UpdateLastSyncAsync(entity.Id, newLastSync, default);

        // Assert
        var updatedEntity = await _context.IndexerFeeds
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<IndexerFeedEntity>()
                .With.Property(nameof(IndexerFeedEntity.LastSync)).EqualTo(newLastSync));
        }
    }

    [Test]
    public async Task UpdateLastSyncAsync_ReturnsFalse_WhenNotFound()
    {
        // Act
        var result = await _subject.UpdateLastSyncAsync(Guid.NewGuid(), DateTimeOffset.UtcNow, default);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).False);
    }

    [Test]
    public async Task UpdateLastSyncAsync_DoesNotCreateHistoryEntry()
    {
        // Arrange
        var entity = new IndexerFeedEntity
        {
            Name = "TestFeed",
            Provider = "newznab",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.IndexerFeeds.Add(entity);
        _context.SaveChanges();

        // Act
        await _subject.UpdateLastSyncAsync(entity.Id, DateTimeOffset.UtcNow, default);

        // Assert
        var historyEntries = await _context.HistoryEntries.ToListAsync();
        Assert.That(historyEntries, Is.Empty);
    }
}
