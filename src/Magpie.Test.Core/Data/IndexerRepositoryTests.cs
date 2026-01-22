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

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NUnit.Framework;

using HistoryEntryEntity = Magpie.Data.Database.Entities.HistoryEntry;
using IndexerDefinitionEntity = Magpie.Data.Database.Entities.IndexerDefinition;
using IndexerEntity = Magpie.Data.Database.Entities.Indexer;
using IndexerSettingEntity = Magpie.Data.Database.Entities.IndexerSetting;
using IndexerTagEntity = Magpie.Data.Database.Entities.IndexerTag;
using UserEntity = Magpie.Data.Database.Entities.User;

namespace Magpie.Test.Core.Data;

[TestFixture]
public class IndexerRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IIndexerRepository _subject = null!;
    private Guid _userId;
    private Guid _definitionId;

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

        _userId = user.Id;
        _definitionId = definition.Id;

        var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
        var historyRepo = new HistoryRepository(_context, loggerFactory.CreateLogger<HistoryRepository>());

        _subject = new IndexerRepository(_context, historyRepo, loggerFactory.CreateLogger<IndexerRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context?.Dispose();
    }

    [Test]
    public async Task GetByIdAsync_ReturnsIndexer()
    {
        // Arrange
        var entity = new IndexerEntity
        {
            Name = "TestIndexer",
            DefinitionId = _definitionId,
            Enabled = true,
            Status = IndexerStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Indexers.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByIdAsync(entity.Id);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<Indexer?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(Indexer.Name))
                .EqualTo("TestIndexer")
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(Indexer.DefinitionId))
                .EqualTo(_definitionId)
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(Indexer.Enabled))
                .True
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(Indexer.Status))
                .EqualTo(IndexerStatus.Unknown));
    }

    [Test]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        // Act
        var result = await _subject.GetByIdAsync(Guid.NewGuid());

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<Indexer?>>().With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task GetByIdAsync_IncludesSettingsAndTags()
    {
        // Arrange
        _context.Tags.AddRange(
            new() { Name = "movies" },
            new() { Name = "tv" });
        _context.SaveChanges();

        var entity = new IndexerEntity
        {
            Name = "IndexerWithChildren",
            DefinitionId = _definitionId,
            Enabled = true,
            Status = IndexerStatus.Healthy,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Settings.Add(new() { Key = "apiKey", Value = "abc123" });
        entity.Settings.Add(new() { Key = "baseUrl", Value = "http://localhost" });
        entity.Tags.Add(new() { TagName = "movies" });
        entity.Tags.Add(new() { TagName = "tv" });
        _context.Indexers.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByIdAsync(entity.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<Indexer?>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(Indexer.Settings)).With.Count.EqualTo(2));
            Assert.That(result, Is.InstanceOf<SuccessResult<Indexer?>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(Indexer.Tags)).With.Count.EqualTo(2));
        }
    }

    [Test]
    public async Task GetAllAsync_ReturnsAllIndexers()
    {
        // Arrange
        var entity1 = new IndexerEntity
        {
            Name = "Indexer1",
            DefinitionId = _definitionId,
            Enabled = true,
            Status = IndexerStatus.Healthy,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new IndexerEntity
        {
            Name = "Indexer2",
            DefinitionId = _definitionId,
            Enabled = false,
            Status = IndexerStatus.Unhealthy,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Indexers.AddRange(entity1, entity2);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetAllAsync();

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<Indexer>>>()
            .With.Property(nameof(SuccessResult<>.Value)).With.Count.EqualTo(2));
    }

    [Test]
    public async Task GetAllAsync_ReturnsEmptyList_WhenNoIndexers()
    {
        // Act
        var result = await _subject.GetAllAsync();

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<Indexer>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task GetAllAsync_IncludesSettingsAndTags()
    {
        // Arrange
        _context.Tags.Add(new() { Name = "books" });
        _context.SaveChanges();

        var entity = new IndexerEntity
        {
            Name = "IndexerWithChildren",
            DefinitionId = _definitionId,
            Enabled = true,
            Status = IndexerStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Settings.Add(new() { Key = "apiKey", Value = "test-key" });
        entity.Tags.Add(new() { TagName = "books" });
        _context.Indexers.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetAllAsync();

        // Assert
        var success = result as SuccessResult<IReadOnlyList<Indexer>>;
        Assert.That(success, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(success!.Value[0].Settings, Has.Count.EqualTo(1));
            Assert.That(success.Value[0].Tags, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesIndexer()
    {
        // Arrange
        var model = new Indexer(
            Guid.Empty,
            _definitionId,
            "NewIndexer",
            true,
            ImmutableList<string>.Empty,
            ImmutableDictionary<string, string?>.Empty,
            IndexerStatus.Unknown,
            null,
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.CreateAsync(model, _userId);

        // Assert
        var entity = await (
            from i in _context.Indexers.AsNoTracking()
            where i.Name == "NewIndexer"
            select i).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<Indexer>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(Indexer.Name))
                    .EqualTo("NewIndexer")
                .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(Indexer.DefinitionId))
                    .EqualTo(_definitionId)
                .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(Indexer.Enabled)).True
                .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(Indexer.Status))
                    .EqualTo(IndexerStatus.Unknown));
            Assert.That(entity, Is.InstanceOf<IndexerEntity>()
                .With.Property(nameof(IndexerEntity.Name)).EqualTo("NewIndexer")
                .And.Property(nameof(IndexerEntity.DefinitionId)).EqualTo(_definitionId)
                .And.Property(nameof(IndexerEntity.CreatedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Create")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("Indexer")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesIndexerWithSettings()
    {
        // Arrange
        var settings = new Dictionary<string, string?>
        {
            ["apiKey"] = "secret123",
            ["baseUrl"] = "http://example.com",
            ["username"] = "admin"
        }.ToImmutableDictionary();

        var model = new Indexer(
            Guid.Empty,
            _definitionId,
            "IndexerWithSettings",
            true,
            ImmutableList<string>.Empty,
            settings,
            IndexerStatus.Unknown,
            null,
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.CreateAsync(model, _userId);

        // Assert
        var entity = await (
            from i in _context.Indexers.AsNoTracking().Include(i => i.Settings)
            where i.Name == "IndexerWithSettings"
            select i).SingleOrDefaultAsync();

        var historyEntities = await _context.HistoryEntries.ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<Indexer>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(Indexer.Settings)).With.Count.EqualTo(3));
            Assert.That(entity, Is.Not.Null);
            Assert.That(entity!.Settings, Has.Count.EqualTo(3));
            Assert.That(entity.Settings, Has.One
                .With.Property(nameof(IndexerSettingEntity.Key)).EqualTo("apiKey")
                .And.Property(nameof(IndexerSettingEntity.Value)).EqualTo("secret123"));
            Assert.That(entity.Settings, Has.One
                .With.Property(nameof(IndexerSettingEntity.Key)).EqualTo("baseUrl")
                .And.Property(nameof(IndexerSettingEntity.Value)).EqualTo("http://example.com"));
            Assert.That(entity.Settings, Has.One
                .With.Property(nameof(IndexerSettingEntity.Key)).EqualTo("username")
                .And.Property(nameof(IndexerSettingEntity.Value)).EqualTo("admin"));
            Assert.That(historyEntities, Has.Count.EqualTo(4));
            Assert.That(historyEntities, Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Create")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("Indexer"));
            Assert.That(historyEntities, Has.Exactly(3)
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Create")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("IndexerSetting"));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesIndexerWithTags()
    {
        // Arrange
        _context.Tags.AddRange(
            new() { Name = "movies" },
            new() { Name = "tv" },
            new() { Name = "anime" });
        _context.SaveChanges();

        var tags = ImmutableList.Create("movies", "tv", "anime");

        var model = new Indexer(
            Guid.Empty,
            _definitionId,
            "IndexerWithTags",
            true,
            tags,
            ImmutableDictionary<string, string?>.Empty,
            IndexerStatus.Unknown,
            null,
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.CreateAsync(model, _userId);

        // Assert
        var entity = await (
            from i in _context.Indexers.AsNoTracking().Include(i => i.Tags)
            where i.Name == "IndexerWithTags"
            select i).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<Indexer>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(Indexer.Tags)).With.Count.EqualTo(3));
            Assert.That(entity, Is.Not.Null);
            Assert.That(entity!.Tags, Has.Count.EqualTo(3));
            Assert.That(entity.Tags, Has.One
                .With.Property(nameof(IndexerTagEntity.TagName)).EqualTo("movies"));
            Assert.That(entity.Tags, Has.One
                .With.Property(nameof(IndexerTagEntity.TagName)).EqualTo("tv"));
            Assert.That(entity.Tags, Has.One
                .With.Property(nameof(IndexerTagEntity.TagName)).EqualTo("anime"));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesIndexerWithSettingsAndTags()
    {
        // Arrange
        _context.Tags.AddRange(
            new() { Name = "primary" },
            new() { Name = "fast" });
        _context.SaveChanges();

        var settings = new Dictionary<string, string?>
        {
            ["apiKey"] = "key123",
            ["rateLimit"] = "10"
        }.ToImmutableDictionary();

        var tags = ImmutableList.Create("primary", "fast");

        var model = new Indexer(
            Guid.Empty,
            _definitionId,
            "CompleteIndexer",
            true,
            tags,
            settings,
            IndexerStatus.Unknown,
            null,
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.CreateAsync(model, _userId);

        // Assert
        var entity = await (
            from i in _context.Indexers.AsNoTracking().Include(i => i.Settings).Include(i => i.Tags)
            where i.Name == "CompleteIndexer"
            select i).SingleOrDefaultAsync();
        var historyEntities = await _context.HistoryEntries.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<Indexer>>());
            Assert.That(entity, Is.Not.Null);
            Assert.That(entity!.Settings, Has.Count.EqualTo(2));
            Assert.That(entity.Tags, Has.Count.EqualTo(2));
            // Now expect 5: 1 Indexer, 2 IndexerSetting, 2 IndexerTag
            Assert.That(historyEntities, Has.Count.EqualTo(5));
            Assert.That(historyEntities, Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Create")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("Indexer"));
            Assert.That(historyEntities, Has.Exactly(2)
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Create")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("IndexerSetting"));
            Assert.That(historyEntities, Has.Exactly(2)
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Create")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("IndexerTag"));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesIndexer()
    {
        // Arrange
        var entity = new IndexerEntity
        {
            Name = "OriginalName",
            DefinitionId = _definitionId,
            Enabled = true,
            Status = IndexerStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Indexers.Add(entity);
        _context.SaveChanges();

        var model = new Indexer(
            entity.Id,
            _definitionId,
            "UpdatedName",
            false,
            ImmutableList<string>.Empty,
            ImmutableDictionary<string, string?>.Empty,
            IndexerStatus.Healthy,
            DateTimeOffset.UtcNow,
            entity.CreatedAt,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        var updatedEntity = await (
            from i in _context.Indexers.AsNoTracking()
            where i.Id == entity.Id
            select i).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<IndexerEntity>()
                .With.Property(nameof(IndexerEntity.Name)).EqualTo("UpdatedName")
                .And.Property(nameof(IndexerEntity.Enabled)).False
                .And.Property(nameof(IndexerEntity.Status)).EqualTo(IndexerStatus.Healthy)
                .And.Property(nameof(IndexerEntity.ModifiedAt)).Not.Null
                .And.Property(nameof(IndexerEntity.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Update")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("Indexer")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateAsync_ReturnsFalse_WhenNotFound()
    {
        // Arrange
        var model = new Indexer(
            Guid.NewGuid(),
            _definitionId,
            "NonExistent",
            true,
            ImmutableList<string>.Empty,
            ImmutableDictionary<string, string?>.Empty,
            IndexerStatus.Unknown,
            null,
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
    public async Task UpdateAsync_AddsNewSettings()
    {
        // Arrange
        var entity = new IndexerEntity
        {
            Name = "IndexerToUpdate",
            DefinitionId = _definitionId,
            Enabled = true,
            Status = IndexerStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Indexers.Add(entity);
        _context.SaveChanges();

        var newSettings = new Dictionary<string, string?>
        {
            ["newSetting1"] = "value1",
            ["newSetting2"] = "value2"
        }.ToImmutableDictionary();

        var model = new Indexer(
            entity.Id,
            _definitionId,
            entity.Name,
            entity.Enabled,
            ImmutableList<string>.Empty,
            newSettings,
            entity.Status,
            entity.LastChecked,
            entity.CreatedAt,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        var settingEntities = await _context.IndexerSettings
            .AsNoTracking()
            .Where(s => s.IndexerId == entity.Id)
            .ToListAsync();
        var historyEntities = await _context.HistoryEntries.ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(settingEntities, Has.Count.EqualTo(2));
            Assert.That(settingEntities, Has.One
                .With.Property(nameof(IndexerSettingEntity.Key)).EqualTo("newSetting1")
                .And.Property(nameof(IndexerSettingEntity.Value)).EqualTo("value1"));
            Assert.That(settingEntities, Has.One
                .With.Property(nameof(IndexerSettingEntity.Key)).EqualTo("newSetting2")
                .And.Property(nameof(IndexerSettingEntity.Value)).EqualTo("value2"));
            Assert.That(historyEntities, Has.Count.EqualTo(3));
            Assert.That(historyEntities, Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Update")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("Indexer"));
            Assert.That(historyEntities, Has.Exactly(2)
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Create")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("IndexerSetting"));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesExistingSettings()
    {
        // Arrange
        var entity = new IndexerEntity
        {
            Name = "IndexerWithSettings",
            DefinitionId = _definitionId,
            Enabled = true,
            Status = IndexerStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Settings.Add(new() { Key = "existingKey", Value = "oldValue" });
        _context.Indexers.Add(entity);
        _context.SaveChanges();

        var updatedSettings = new Dictionary<string, string?>
        {
            ["existingKey"] = "newValue"
        }.ToImmutableDictionary();

        var model = new Indexer(
            entity.Id,
            _definitionId,
            entity.Name,
            entity.Enabled,
            ImmutableList<string>.Empty,
            updatedSettings,
            entity.Status,
            entity.LastChecked,
            entity.CreatedAt,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        var settingEntity = await _context.IndexerSettings
            .AsNoTracking()
            .Where(s => s.IndexerId == entity.Id)
            .Where(s => s.Key == "existingKey")
            .SingleOrDefaultAsync();
        var historyEntities = await _context.HistoryEntries.ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(settingEntity, Is.InstanceOf<IndexerSettingEntity>()
                .With.Property(nameof(IndexerSettingEntity.Value)).EqualTo("newValue"));
            Assert.That(historyEntities, Has.Count.EqualTo(2));
            Assert.That(historyEntities, Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Update")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("Indexer"));
            Assert.That(historyEntities, Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Update")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("IndexerSetting"));
        }
    }

    [Test]
    public async Task UpdateAsync_RemovesSettings()
    {
        // Arrange
        var entity = new IndexerEntity
        {
            Name = "IndexerWithSettings",
            DefinitionId = _definitionId,
            Enabled = true,
            Status = IndexerStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Settings.Add(new() { Key = "keepMe", Value = "keep" });
        entity.Settings.Add(new() { Key = "removeMe", Value = "remove" });
        _context.Indexers.Add(entity);
        _context.SaveChanges();

        var updatedSettings = new Dictionary<string, string?>
        {
            ["keepMe"] = "keep"
        }.ToImmutableDictionary();

        var model = new Indexer(
            entity.Id,
            _definitionId,
            entity.Name,
            entity.Enabled,
            ImmutableList<string>.Empty,
            updatedSettings,
            entity.Status,
            entity.LastChecked,
            entity.CreatedAt,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        var settingEntities = await _context.IndexerSettings
            .AsNoTracking()
            .Where(s => s.IndexerId == entity.Id)
            .ToListAsync();
        var historyEntities = await _context.HistoryEntries.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(settingEntities, Has.Count.EqualTo(1));
            Assert.That(settingEntities, Has.One
                .With.Property(nameof(IndexerSettingEntity.Key)).EqualTo("keepMe"));
            Assert.That(settingEntities, Has.None
                .With.Property(nameof(IndexerSettingEntity.Key)).EqualTo("removeMe"));
            // Now expect 2: 1 Indexer (Update), 1 IndexerSetting (Delete)
            Assert.That(historyEntities, Has.Count.EqualTo(2));
            Assert.That(historyEntities, Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Update")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("Indexer"));
            Assert.That(historyEntities, Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Delete")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("IndexerSetting"));
        }
    }

    [Test]
    public async Task UpdateAsync_AddsUpdatesAndRemovesSettings()
    {
        // Arrange
        var entity = new IndexerEntity
        {
            Name = "ComplexIndexer",
            DefinitionId = _definitionId,
            Enabled = true,
            Status = IndexerStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Settings.Add(new() { Key = "toUpdate", Value = "oldValue" });
        entity.Settings.Add(new() { Key = "toRemove", Value = "removeMe" });
        _context.Indexers.Add(entity);
        _context.SaveChanges();

        var updatedSettings = new Dictionary<string, string?>
        {
            ["toUpdate"] = "newValue",
            ["toAdd"] = "addedValue"
        }.ToImmutableDictionary();

        var model = new Indexer(
            entity.Id,
            _definitionId,
            entity.Name,
            entity.Enabled,
            ImmutableList<string>.Empty,
            updatedSettings,
            entity.Status,
            entity.LastChecked,
            entity.CreatedAt,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        var settingEntities = await _context.IndexerSettings
            .AsNoTracking()
            .Where(s => s.IndexerId == entity.Id)
            .ToListAsync();
        var historyEntities = await _context.HistoryEntries.ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(settingEntities, Has.Count.EqualTo(2));
            Assert.That(settingEntities, Has.One
                .With.Property(nameof(IndexerSettingEntity.Key)).EqualTo("toUpdate")
                .And.Property(nameof(IndexerSettingEntity.Value)).EqualTo("newValue"));
            Assert.That(settingEntities, Has.One
                .With.Property(nameof(IndexerSettingEntity.Key)).EqualTo("toAdd")
                .And.Property(nameof(IndexerSettingEntity.Value)).EqualTo("addedValue"));
            Assert.That(settingEntities, Has.None
                .With.Property(nameof(IndexerSettingEntity.Key)).EqualTo("toRemove"));
            Assert.That(historyEntities, Has.Count.EqualTo(4));
            Assert.That(historyEntities, Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Create")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("IndexerSetting"));
            Assert.That(historyEntities, Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Update")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("IndexerSetting"));
            Assert.That(historyEntities, Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Delete")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("IndexerSetting"));
            Assert.That(historyEntities, Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Delete")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("IndexerSetting"));
        }
    }

    [Test]
    public async Task UpdateAsync_AddsNewTags()
    {
        // Arrange
        _context.Tags.AddRange(
            new() { Name = "newTag1" },
            new() { Name = "newTag2" });
        _context.SaveChanges();

        var entity = new IndexerEntity
        {
            Name = "IndexerToUpdate",
            DefinitionId = _definitionId,
            Enabled = true,
            Status = IndexerStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Indexers.Add(entity);
        _context.SaveChanges();

        var newTags = ImmutableList.Create("newTag1", "newTag2");

        var model = new Indexer(
            entity.Id,
            _definitionId,
            entity.Name,
            entity.Enabled,
            newTags,
            ImmutableDictionary<string, string?>.Empty,
            entity.Status,
            entity.LastChecked,
            entity.CreatedAt,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        var tagEntities = await _context.IndexerTags
            .AsNoTracking()
            .Where(t => t.IndexerId == entity.Id)
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(tagEntities, Has.Count.EqualTo(2));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(IndexerTagEntity.TagName)).EqualTo("newTag1"));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(IndexerTagEntity.TagName)).EqualTo("newTag2"));
        }
    }

    [Test]
    public async Task UpdateAsync_RemovesTags()
    {
        // Arrange
        _context.Tags.AddRange(
            new() { Name = "keepMe" },
            new() { Name = "removeMe" });
        _context.SaveChanges();

        var entity = new IndexerEntity
        {
            Name = "IndexerWithTags",
            DefinitionId = _definitionId,
            Enabled = true,
            Status = IndexerStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Tags.Add(new() { TagName = "keepMe" });
        entity.Tags.Add(new() { TagName = "removeMe" });
        _context.Indexers.Add(entity);
        _context.SaveChanges();

        var updatedTags = ImmutableList.Create("keepMe");

        var model = new Indexer(
            entity.Id,
            _definitionId,
            entity.Name,
            entity.Enabled,
            updatedTags,
            ImmutableDictionary<string, string?>.Empty,
            entity.Status,
            entity.LastChecked,
            entity.CreatedAt,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        var tagEntities = await _context.IndexerTags
            .AsNoTracking()
            .Where(t => t.IndexerId == entity.Id)
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(tagEntities, Has.Count.EqualTo(1));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(IndexerTagEntity.TagName)).EqualTo("keepMe"));
            Assert.That(tagEntities, Has.None
                .With.Property(nameof(IndexerTagEntity.TagName)).EqualTo("removeMe"));
        }
    }

    [Test]
    public async Task UpdateAsync_AddsAndRemovesTags()
    {
        // Arrange
        _context.Tags.AddRange(
            new() { Name = "existing" },
            new() { Name = "toRemove" },
            new() { Name = "toAdd" });
        _context.SaveChanges();

        var entity = new IndexerEntity
        {
            Name = "ComplexIndexer",
            DefinitionId = _definitionId,
            Enabled = true,
            Status = IndexerStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Tags.Add(new() { TagName = "existing" });
        entity.Tags.Add(new() { TagName = "toRemove" });
        _context.Indexers.Add(entity);
        _context.SaveChanges();

        var updatedTags = ImmutableList.Create("existing", "toAdd");

        var model = new Indexer(
            entity.Id,
            _definitionId,
            entity.Name,
            entity.Enabled,
            updatedTags,
            ImmutableDictionary<string, string?>.Empty,
            entity.Status,
            entity.LastChecked,
            entity.CreatedAt,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        var tagEntities = await _context.IndexerTags
            .AsNoTracking()
            .Where(t => t.IndexerId == entity.Id)
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(tagEntities, Has.Count.EqualTo(2));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(IndexerTagEntity.TagName)).EqualTo("existing"));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(IndexerTagEntity.TagName)).EqualTo("toAdd"));
            Assert.That(tagEntities, Has.None
                .With.Property(nameof(IndexerTagEntity.TagName)).EqualTo("toRemove"));
        }
    }

    [Test]
    public async Task DeleteAsync_DeletesIndexer()
    {
        // Arrange
        var entity = new IndexerEntity
        {
            Name = "ToDelete",
            DefinitionId = _definitionId,
            Enabled = true,
            Status = IndexerStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Indexers.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.DeleteAsync(entity.Id, _userId);

        // Assert
        var entityExists = await _context.Indexers.AnyAsync(i => i.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(entityExists, Is.False);
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Delete")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("Indexer")
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
    public async Task DeleteAsync_CascadesToSettings()
    {
        // Arrange
        var entity = new IndexerEntity
        {
            Name = "IndexerWithSettings",
            DefinitionId = _definitionId,
            Enabled = true,
            Status = IndexerStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Settings.Add(new() { Key = "setting1", Value = "value1" });
        entity.Settings.Add(new() { Key = "setting2", Value = "value2" });
        _context.Indexers.Add(entity);
        _context.SaveChanges();

        var indexerId = entity.Id;

        // Act
        var result = await _subject.DeleteAsync(indexerId, _userId);

        // Assert
        var settingsExist = await _context.IndexerSettings
            .AnyAsync(s => s.IndexerId == indexerId);
        var historyEntities = await _context.HistoryEntries.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(settingsExist, Is.False);
            // Now expect 3: 1 Indexer, 2 IndexerSetting
            Assert.That(historyEntities, Has.Count.EqualTo(3));
            Assert.That(historyEntities, Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Delete")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("Indexer"));
            Assert.That(historyEntities, Has.Exactly(2)
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Delete")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("IndexerSetting"));
        }
    }

    [Test]
    public async Task DeleteAsync_CascadesToTags()
    {
        // Arrange
        _context.Tags.AddRange(
            new() { Name = "tag1" },
            new() { Name = "tag2" });
        _context.SaveChanges();

        var entity = new IndexerEntity
        {
            Name = "IndexerWithTags",
            DefinitionId = _definitionId,
            Enabled = true,
            Status = IndexerStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Tags.Add(new() { TagName = "tag1" });
        entity.Tags.Add(new() { TagName = "tag2" });
        _context.Indexers.Add(entity);
        _context.SaveChanges();

        var indexerId = entity.Id;

        // Act
        var result = await _subject.DeleteAsync(indexerId, _userId);

        // Assert
        var tagsExist = await _context.IndexerTags
            .AnyAsync(t => t.IndexerId == indexerId);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(tagsExist, Is.False);
        }
    }

    [Test]
    public async Task DeleteAsync_CascadesToSettingsAndTags()
    {
        // Arrange
        _context.Tags.Add(new() { Name = "tag1" });
        _context.SaveChanges();

        var entity = new IndexerEntity
        {
            Name = "CompleteIndexer",
            DefinitionId = _definitionId,
            Enabled = true,
            Status = IndexerStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Settings.Add(new() { Key = "setting1", Value = "value1" });
        entity.Tags.Add(new() { TagName = "tag1" });
        _context.Indexers.Add(entity);
        _context.SaveChanges();

        var indexerId = entity.Id;

        // Act
        var result = await _subject.DeleteAsync(indexerId, _userId);

        // Assert
        var settingsExist = await _context.IndexerSettings
            .AnyAsync(s => s.IndexerId == indexerId);
        var tagsExist = await _context.IndexerTags
            .AnyAsync(t => t.IndexerId == indexerId);
        var historyEntities = await _context.HistoryEntries.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(settingsExist, Is.False);
            Assert.That(tagsExist, Is.False);
            // Now expect 3: 1 Indexer, 1 IndexerSetting, 1 IndexerTag
            Assert.That(historyEntities, Has.Count.EqualTo(3));
            Assert.That(historyEntities, Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Delete")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("Indexer"));
            Assert.That(historyEntities, Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Delete")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("IndexerSetting"));
            Assert.That(historyEntities, Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Delete")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("IndexerTag"));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesName()
    {
        // Arrange
        var entity = new IndexerEntity
        {
            Name = "OriginalName",
            DefinitionId = _definitionId,
            Enabled = true,
            Status = IndexerStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Indexers.Add(entity);
        _context.SaveChanges();

        var patch = new IndexerPatch
        {
            Id = entity.Id,
            Name = new FieldPatch<string>("PatchedName")
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var updatedEntity = await _context.Indexers
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<IndexerEntity>()
                .With.Property(nameof(IndexerEntity.Name)).EqualTo("PatchedName")
                .And.Property(nameof(IndexerEntity.ModifiedAt)).Not.Null
                .And.Property(nameof(IndexerEntity.ModifiedByUserId)).EqualTo(_userId));
            // Now expect Update instead of Patch
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Update")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("Indexer")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesEnabled()
    {
        // Arrange
        var entity = new IndexerEntity
        {
            Name = "TestIndexer",
            DefinitionId = _definitionId,
            Enabled = true,
            Status = IndexerStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Indexers.Add(entity);
        _context.SaveChanges();

        var patch = new IndexerPatch
        {
            Id = entity.Id,
            Enabled = new FieldPatch<bool>(false)
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var updatedEntity = await _context.Indexers
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<IndexerEntity>()
                .With.Property(nameof(IndexerEntity.Enabled)).False);
        }
    }

    [Test]
    public async Task PatchAsync_PatchesSettings()
    {
        // Arrange
        var entity = new IndexerEntity
        {
            Name = "TestIndexer",
            DefinitionId = _definitionId,
            Enabled = true,
            Status = IndexerStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Settings.Add(new() { Key = "toUpdate", Value = "oldValue" });
        entity.Settings.Add(new() { Key = "toRemove", Value = "removeMe" });
        _context.Indexers.Add(entity);
        _context.SaveChanges();

        var patch = new IndexerPatch
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
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var settingEntities = await _context.IndexerSettings
            .AsNoTracking()
            .Where(s => s.IndexerId == entity.Id)
            .ToListAsync();

        var historyEntities = await _context.HistoryEntries.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(settingEntities, Has.Count.EqualTo(2));
            Assert.That(settingEntities, Has.One
                .With.Property(nameof(IndexerSettingEntity.Key)).EqualTo("toUpdate")
                .And.Property(nameof(IndexerSettingEntity.Value)).EqualTo("newValue"));
            Assert.That(settingEntities, Has.One
                .With.Property(nameof(IndexerSettingEntity.Key)).EqualTo("toAdd")
                .And.Property(nameof(IndexerSettingEntity.Value)).EqualTo("addedValue"));
            Assert.That(settingEntities, Has.None
                .With.Property(nameof(IndexerSettingEntity.Key)).EqualTo("toRemove"));
            Assert.That(historyEntities, Has.Count.EqualTo(4));
            Assert.That(historyEntities, Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Update")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("Indexer"));
            Assert.That(historyEntities, Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Create")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("IndexerSetting"));
            Assert.That(historyEntities, Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Update")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("IndexerSetting"));
            Assert.That(historyEntities, Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Delete")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("IndexerSetting"));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesTags_AddOnly()
    {
        // Arrange
        _context.Tags.AddRange(
            new() { Name = "existing" },
            new() { Name = "newTag1" },
            new() { Name = "newTag2" });
        _context.SaveChanges();

        var entity = new IndexerEntity
        {
            Name = "TestIndexer",
            DefinitionId = _definitionId,
            Enabled = true,
            Status = IndexerStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Tags.Add(new() { TagName = "existing" });
        _context.Indexers.Add(entity);
        _context.SaveChanges();

        var patch = new IndexerPatch
        {
            Id = entity.Id,
            Tags = new SetPatch<string, string>(Add: ["newTag1", "newTag2"], Remove: null)
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var tagEntities = await _context.IndexerTags
            .AsNoTracking()
            .Where(t => t.IndexerId == entity.Id)
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(tagEntities, Has.Count.EqualTo(3));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(IndexerTagEntity.TagName)).EqualTo("existing"));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(IndexerTagEntity.TagName)).EqualTo("newTag1"));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(IndexerTagEntity.TagName)).EqualTo("newTag2"));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesTags_RemoveOnly()
    {
        // Arrange
        _context.Tags.AddRange(
            new() { Name = "keepMe" },
            new() { Name = "removeMe" });
        _context.SaveChanges();

        var entity = new IndexerEntity
        {
            Name = "TestIndexer",
            DefinitionId = _definitionId,
            Enabled = true,
            Status = IndexerStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Tags.Add(new() { TagName = "keepMe" });
        entity.Tags.Add(new() { TagName = "removeMe" });
        _context.Indexers.Add(entity);
        _context.SaveChanges();

        var patch = new IndexerPatch
        {
            Id = entity.Id,
            Tags = new SetPatch<string, string>(Add: null, Remove: ["removeMe"])
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var tagEntities = await _context.IndexerTags
            .AsNoTracking()
            .Where(t => t.IndexerId == entity.Id)
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(tagEntities, Has.Count.EqualTo(1));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(IndexerTagEntity.TagName)).EqualTo("keepMe"));
            Assert.That(tagEntities, Has.None
                .With.Property(nameof(IndexerTagEntity.TagName)).EqualTo("removeMe"));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesTags_AddAndRemove()
    {
        // Arrange
        _context.Tags.AddRange(
            new() { Name = "existing" },
            new() { Name = "toRemove" },
            new() { Name = "toAdd" });
        _context.SaveChanges();

        var entity = new IndexerEntity
        {
            Name = "TestIndexer",
            DefinitionId = _definitionId,
            Enabled = true,
            Status = IndexerStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Tags.Add(new() { TagName = "existing" });
        entity.Tags.Add(new() { TagName = "toRemove" });
        _context.Indexers.Add(entity);
        _context.SaveChanges();

        var patch = new IndexerPatch
        {
            Id = entity.Id,
            Tags = new SetPatch<string, string>(Add: ["toAdd"], Remove: ["toRemove"])
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var tagEntities = await _context.IndexerTags
            .AsNoTracking()
            .Where(t => t.IndexerId == entity.Id)
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(tagEntities, Has.Count.EqualTo(2));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(IndexerTagEntity.TagName)).EqualTo("existing"));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(IndexerTagEntity.TagName)).EqualTo("toAdd"));
            Assert.That(tagEntities, Has.None
                .With.Property(nameof(IndexerTagEntity.TagName)).EqualTo("toRemove"));
        }
    }

    [Test]
    public async Task PatchAsync_ReturnsFalse_WhenNotFound()
    {
        // Arrange
        var patch = new IndexerPatch
        {
            Id = Guid.NewGuid(),
            Name = new FieldPatch<string>("NewName")
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).False);
    }

    [Test]
    public async Task GetByNameAsync_ReturnsIndexer()
    {
        // Arrange
        var entity = new IndexerEntity
        {
            Name = "UniqueIndexerName",
            DefinitionId = _definitionId,
            Enabled = true,
            Status = IndexerStatus.Healthy,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Indexers.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByNameAsync("UniqueIndexerName");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<Indexer?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(Indexer.Name))
                .EqualTo("UniqueIndexerName")
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(Indexer.DefinitionId))
                .EqualTo(_definitionId)
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(Indexer.Status))
                .EqualTo(IndexerStatus.Healthy));
    }

    [Test]
    public async Task GetByNameAsync_ReturnsNull_WhenNotFound()
    {
        // Act
        var result = await _subject.GetByNameAsync("NonExistentIndexer");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<Indexer?>>().With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task GetByNameAsync_IncludesSettingsAndTags()
    {
        // Arrange
        _context.Tags.Add(new() { Name = "primary" });
        _context.SaveChanges();

        var entity = new IndexerEntity
        {
            Name = "IndexerWithChildren",
            DefinitionId = _definitionId,
            Enabled = true,
            Status = IndexerStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Settings.Add(new() { Key = "apiKey", Value = "abc123" });
        entity.Tags.Add(new() { TagName = "primary" });
        _context.Indexers.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByNameAsync("IndexerWithChildren");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<Indexer?>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(Indexer.Settings)).With.Count.EqualTo(1));
            Assert.That(result, Is.InstanceOf<SuccessResult<Indexer?>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(Indexer.Tags)).With.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task GetByDefinitionIdAsync_ReturnsIndexers()
    {
        // Arrange
        var entity1 = new IndexerEntity
        {
            Name = "Indexer1",
            DefinitionId = _definitionId,
            Enabled = true,
            Status = IndexerStatus.Healthy,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new IndexerEntity
        {
            Name = "Indexer2",
            DefinitionId = _definitionId,
            Enabled = false,
            Status = IndexerStatus.Unhealthy,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var otherDefinition = new IndexerDefinitionEntity
        {
            Provider = "newznab",
            Name = "OtherDefinition",
            Access = IndexerAccess.Private,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.IndexerDefinitions.Add(otherDefinition);
        _context.SaveChanges();

        var entity3 = new IndexerEntity
        {
            Name = "OtherIndexer",
            DefinitionId = otherDefinition.Id,
            Enabled = true,
            Status = IndexerStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Indexers.AddRange(entity1, entity2, entity3);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByDefinitionIdAsync(_definitionId);

        // Assert
        var success = result as SuccessResult<IReadOnlyList<Indexer>>;
        Assert.That(success, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(success!.Value, Has.Count.EqualTo(2));
            Assert.That(success.Value, Has.All
                .With.Property(nameof(Indexer.DefinitionId)).EqualTo(_definitionId));
        }
    }

    [Test]
    public async Task GetByDefinitionIdAsync_ReturnsEmptyList_WhenNoMatches()
    {
        // Act
        var result = await _subject.GetByDefinitionIdAsync(Guid.NewGuid());

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<Indexer>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task GetByDefinitionIdAsync_IncludesSettingsAndTags()
    {
        // Arrange
        _context.Tags.Add(new() { Name = "movies" });
        _context.SaveChanges();

        var entity = new IndexerEntity
        {
            Name = "IndexerWithChildren",
            DefinitionId = _definitionId,
            Enabled = true,
            Status = IndexerStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Settings.Add(new() { Key = "token", Value = "xyz" });
        entity.Tags.Add(new() { TagName = "movies" });
        _context.Indexers.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByDefinitionIdAsync(_definitionId);

        // Assert
        var success = result as SuccessResult<IReadOnlyList<Indexer>>;
        Assert.That(success, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(success!.Value[0].Settings, Has.Count.EqualTo(1));
            Assert.That(success.Value[0].Tags, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task GetByStatusAsync_ReturnsIndexers()
    {
        // Arrange
        var entity1 = new IndexerEntity
        {
            Name = "HealthyIndexer1",
            DefinitionId = _definitionId,
            Enabled = true,
            Status = IndexerStatus.Healthy,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new IndexerEntity
        {
            Name = "HealthyIndexer2",
            DefinitionId = _definitionId,
            Enabled = true,
            Status = IndexerStatus.Healthy,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity3 = new IndexerEntity
        {
            Name = "UnhealthyIndexer",
            DefinitionId = _definitionId,
            Enabled = true,
            Status = IndexerStatus.Unhealthy,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Indexers.AddRange(entity1, entity2, entity3);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByStatusAsync(IndexerStatus.Healthy);

        // Assert
        var success = result as SuccessResult<IReadOnlyList<Indexer>>;
        Assert.That(success, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(success!.Value, Has.Count.EqualTo(2));
            Assert.That(success.Value, Has.All
                .With.Property(nameof(Indexer.Status)).EqualTo(IndexerStatus.Healthy));
        }
    }

    [Test]
    public async Task GetByStatusAsync_ReturnsEmptyList_WhenNoMatches()
    {
        // Arrange
        var entity = new IndexerEntity
        {
            Name = "HealthyIndexer",
            DefinitionId = _definitionId,
            Enabled = true,
            Status = IndexerStatus.Healthy,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Indexers.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByStatusAsync(IndexerStatus.Stale);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<Indexer>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task GetByStatusAsync_IncludesSettingsAndTags()
    {
        // Arrange
        _context.Tags.Add(new() { Name = "primary" });
        _context.SaveChanges();

        var entity = new IndexerEntity
        {
            Name = "HealthyIndexer",
            DefinitionId = _definitionId,
            Enabled = true,
            Status = IndexerStatus.Healthy,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Settings.Add(new() { Key = "apiKey", Value = "secret" });
        entity.Tags.Add(new() { TagName = "primary" });
        _context.Indexers.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByStatusAsync(IndexerStatus.Healthy);

        // Assert
        var success = result as SuccessResult<IReadOnlyList<Indexer>>;
        Assert.That(success, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(success!.Value[0].Settings, Has.Count.EqualTo(1));
            Assert.That(success.Value[0].Tags, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task GetEnabledAsync_ReturnsEnabledIndexers()
    {
        // Arrange
        var entity1 = new IndexerEntity
        {
            Name = "EnabledIndexer1",
            DefinitionId = _definitionId,
            Enabled = true,
            Status = IndexerStatus.Healthy,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new IndexerEntity
        {
            Name = "EnabledIndexer2",
            DefinitionId = _definitionId,
            Enabled = true,
            Status = IndexerStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity3 = new IndexerEntity
        {
            Name = "DisabledIndexer",
            DefinitionId = _definitionId,
            Enabled = false,
            Status = IndexerStatus.Unhealthy,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Indexers.AddRange(entity1, entity2, entity3);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetEnabledAsync();

        // Assert
        var success = result as SuccessResult<IReadOnlyList<Indexer>>;
        Assert.That(success, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(success!.Value, Has.Count.EqualTo(2));
            Assert.That(success.Value, Has.All
                .With.Property(nameof(Indexer.Enabled)).True);
        }
    }

    [Test]
    public async Task GetEnabledAsync_ReturnsEmptyList_WhenNoEnabledIndexers()
    {
        // Arrange
        var entity = new IndexerEntity
        {
            Name = "DisabledIndexer",
            DefinitionId = _definitionId,
            Enabled = false,
            Status = IndexerStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Indexers.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetEnabledAsync();

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<Indexer>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task GetEnabledAsync_IncludesSettingsAndTags()
    {
        // Arrange
        _context.Tags.Add(new() { Name = "primary" });
        _context.SaveChanges();

        var entity = new IndexerEntity
        {
            Name = "EnabledIndexer",
            DefinitionId = _definitionId,
            Enabled = true,
            Status = IndexerStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Settings.Add(new() { Key = "apiKey", Value = "abc" });
        entity.Tags.Add(new() { TagName = "primary" });
        _context.Indexers.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetEnabledAsync();

        // Assert
        var success = result as SuccessResult<IReadOnlyList<Indexer>>;
        Assert.That(success, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(success!.Value[0].Settings, Has.Count.EqualTo(1));
            Assert.That(success.Value[0].Tags, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task UpdateStatusAsync_UpdatesStatusAndLastChecked()
    {
        // Arrange
        var entity = new IndexerEntity
        {
            Name = "TestIndexer",
            DefinitionId = _definitionId,
            Enabled = true,
            Status = IndexerStatus.Unknown,
            LastChecked = null,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Indexers.Add(entity);
        _context.SaveChanges();

        var newLastChecked = DateTimeOffset.UtcNow;

        // Act
        var result = await _subject.UpdateStatusAsync(entity.Id, IndexerStatus.Healthy, newLastChecked);

        // Assert
        var updatedEntity = await _context.Indexers
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<IndexerEntity>()
                .With.Property(nameof(IndexerEntity.Status)).EqualTo(IndexerStatus.Healthy)
                .And.Property(nameof(IndexerEntity.LastChecked)).EqualTo(newLastChecked));
        }
    }

    [Test]
    public async Task UpdateStatusAsync_ReturnsFalse_WhenNotFound()
    {
        // Act
        var result = await _subject.UpdateStatusAsync(Guid.NewGuid(), IndexerStatus.Healthy, DateTimeOffset.UtcNow);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).False);
    }

    [Test]
    public async Task UpdateStatusAsync_DoesNotCreateHistoryEntry()
    {
        // Arrange
        var entity = new IndexerEntity
        {
            Name = "TestIndexer",
            DefinitionId = _definitionId,
            Enabled = true,
            Status = IndexerStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Indexers.Add(entity);
        _context.SaveChanges();

        // Act
        await _subject.UpdateStatusAsync(entity.Id, IndexerStatus.Healthy, DateTimeOffset.UtcNow);

        // Assert
        var historyEntries = await _context.HistoryEntries.ToListAsync();
        Assert.That(historyEntries, Is.Empty);
    }
}
