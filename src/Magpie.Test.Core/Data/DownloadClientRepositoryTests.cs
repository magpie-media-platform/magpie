using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions.Data;
using Magpie.Core.Data;
using Magpie.Data.Database;
using Magpie.Models;
using Magpie.Models.Downloads;
using Magpie.Models.Result;
using Magpie.Models.Users;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NUnit.Framework;

using DownloadClientEntity = Magpie.Data.Database.Entities.DownloadClient;
using DownloadClientSettingEntity = Magpie.Data.Database.Entities.DownloadClientSetting;
using DownloadClientTagEntity = Magpie.Data.Database.Entities.DownloadClientTag;
using HistoryEntryEntity = Magpie.Data.Database.Entities.HistoryEntry;
using UserEntity = Magpie.Data.Database.Entities.User;

namespace Magpie.Test.Core.Data;

[TestFixture]
public class DownloadClientRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IDownloadClientRepository _subject = null!;
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

        var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
        var historyRepo = new HistoryRepository(_context, loggerFactory.CreateLogger<HistoryRepository>());

        _subject = new DownloadClientRepository(
            _context,
            historyRepo,
            loggerFactory.CreateLogger<DownloadClientRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context?.Dispose();
    }

    [Test]
    public async Task GetByIdAsync_ReturnsDownloadClient()
    {
        // Arrange
        var entity = new DownloadClientEntity
        {
            Name = "TestClient",
            Provider = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.DownloadClients.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByIdAsync(entity.Id);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<DownloadClient?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(DownloadClient.Name))
                .EqualTo("TestClient")
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(DownloadClient.Provider))
                .EqualTo("qbittorrent")
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(DownloadClient.Enabled))
                .True
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(DownloadClient.Status))
                .EqualTo(DownloadClientStatus.Unknown));
    }

    [Test]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        // Act
        var result = await _subject.GetByIdAsync(Guid.NewGuid());

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<DownloadClient?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task GetByIdAsync_IncludesSettingsAndTags()
    {
        // Arrange
        _context.Tags.Add(new() { Name = "movies" });
        _context.Tags.Add(new() { Name = "tv" });
        var entity = new DownloadClientEntity
        {
            Name = "ClientWithChildren",
            Provider = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Healthy,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Settings.Add(new() { Key = "host", Value = "localhost" });
        entity.Settings.Add(new() { Key = "port", Value = "8080" });
        entity.Tags.Add(new() { TagName = "movies" });
        entity.Tags.Add(new() { TagName = "tv" });
        _context.DownloadClients.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByIdAsync(entity.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<DownloadClient?>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(DownloadClient.Settings))
                .Count.EqualTo(2));
            Assert.That(result, Is.InstanceOf<SuccessResult<DownloadClient?>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(DownloadClient.Tags))
                    .Count.EqualTo(2));
        }
    }

    [Test]
    public async Task GetAllAsync_ReturnsAllDownloadClients()
    {
        // Arrange
        var entity1 = new DownloadClientEntity
        {
            Name = "Client1",
            Provider = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Healthy,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new DownloadClientEntity
        {
            Name = "Client2",
            Provider = "sabnzbd",
            Enabled = false,
            Status = DownloadClientStatus.Unhealthy,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.DownloadClients.AddRange(entity1, entity2);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetAllAsync(default);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<DownloadClient>>>()
            .With.Property(nameof(SuccessResult<>.Value)).With.Count.EqualTo(2));
    }

    [Test]
    public async Task GetAllAsync_ReturnsEmptyList_WhenNoClients()
    {
        // Act
        var result = await _subject.GetAllAsync(default);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<DownloadClient>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task GetAllAsync_IncludesSettingsAndTags()
    {
        // Arrange
        _context.Tags.Add(new() { Name = "books" });
        var entity = new DownloadClientEntity
        {
            Name = "ClientWithChildren",
            Provider = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Settings.Add(new() { Key = "apiKey", Value = "test-key" });
        entity.Tags.Add(new() { TagName = "books" });
        _context.DownloadClients.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetAllAsync(default);

        // Assert
        var success = result as SuccessResult<IReadOnlyList<DownloadClient>>;
        Assert.That(success, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(success!.Value[0].Settings, Has.Count.EqualTo(1));
            Assert.That(success.Value[0].Tags, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesDownloadClient()
    {
        // Arrange
        var model = new DownloadClient(
            Guid.Empty,
            "NewClient",
            "qbittorrent",
            true,
            ImmutableList<string>.Empty,
            ImmutableDictionary<string, string?>.Empty,
            DownloadClientStatus.Unknown,
            null,
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.CreateAsync(model, _userId);

        // Assert
        var entity = await (
            from c in _context.DownloadClients.AsNoTracking()
            where c.Name == "NewClient"
            select c).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<DownloadClient>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(DownloadClient.Name))
                    .EqualTo("NewClient")
                .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(DownloadClient.Provider))
                    .EqualTo("qbittorrent")
                .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(DownloadClient.Enabled))
                    .True
                .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(DownloadClient.Status))
                    .EqualTo(DownloadClientStatus.Unknown));
            Assert.That(entity, Is.InstanceOf<DownloadClientEntity>()
                .With.Property(nameof(DownloadClientEntity.Name)).EqualTo("NewClient")
                .And.Property(nameof(DownloadClientEntity.Provider)).EqualTo("qbittorrent")
                .And.Property(nameof(DownloadClientEntity.CreatedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Create")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("DownloadClient")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesDownloadClientWithSettings()
    {
        // Arrange
        var settings = new Dictionary<string, string?>
        {
            ["host"] = "192.168.1.100",
            ["port"] = "8080",
            ["username"] = "admin"
        }.ToImmutableDictionary();

        var model = new DownloadClient(
            Guid.Empty,
            "ClientWithSettings",
            "qbittorrent",
            true,
            ImmutableList<string>.Empty,
            settings,
            DownloadClientStatus.Unknown,
            null,
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.CreateAsync(model, _userId);

        // Assert
        var entity = await (
            from c in _context.DownloadClients.AsNoTracking().Include(c => c.Settings)
            where c.Name == "ClientWithSettings"
            select c).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<DownloadClient>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(DownloadClient.Settings))
                .Count.EqualTo(3));
            Assert.That(entity, Is.Not.Null);
            Assert.That(entity!.Settings, Has.Count.EqualTo(3));
            Assert.That(entity.Settings, Has.One
                .With.Property(nameof(DownloadClientSettingEntity.Key)).EqualTo("host")
                .And.Property(nameof(DownloadClientSettingEntity.Value)).EqualTo("192.168.1.100"));
            Assert.That(entity.Settings, Has.One
                .With.Property(nameof(DownloadClientSettingEntity.Key)).EqualTo("port")
                .And.Property(nameof(DownloadClientSettingEntity.Value)).EqualTo("8080"));
            Assert.That(entity.Settings, Has.One
                .With.Property(nameof(DownloadClientSettingEntity.Key)).EqualTo("username")
                .And.Property(nameof(DownloadClientSettingEntity.Value)).EqualTo("admin"));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesDownloadClientWithTags()
    {
        // Arrange
        _context.Tags.Add(new() { Name = "movies" });
        _context.Tags.Add(new() { Name = "tv" });
        _context.Tags.Add(new() { Name = "anime" });
        _context.SaveChanges();
        var tags = ImmutableList.Create("movies", "tv", "anime");

        var model = new DownloadClient(
            Guid.Empty,
            "ClientWithTags",
            "qbittorrent",
            true,
            tags,
            ImmutableDictionary<string, string?>.Empty,
            DownloadClientStatus.Unknown,
            null,
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.CreateAsync(model, _userId);

        // Assert
        var entity = await (
            from c in _context.DownloadClients.AsNoTracking().Include(c => c.Tags)
            where c.Name == "ClientWithTags"
            select c).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<DownloadClient>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(DownloadClient.Tags))
                .Count.EqualTo(3));
            Assert.That(entity, Is.Not.Null);
            Assert.That(entity!.Tags, Has.Count.EqualTo(3));
            Assert.That(entity.Tags, Has.One
                .With.Property(nameof(DownloadClientTagEntity.TagName)).EqualTo("movies"));
            Assert.That(entity.Tags, Has.One
                .With.Property(nameof(DownloadClientTagEntity.TagName)).EqualTo("tv"));
            Assert.That(entity.Tags, Has.One
                .With.Property(nameof(DownloadClientTagEntity.TagName)).EqualTo("anime"));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesDownloadClientWithSettingsAndTags()
    {
        // Arrange
        _context.Tags.Add(new() { Name = "primary" });
        _context.Tags.Add(new() { Name = "fast" });
        _context.SaveChanges();
        var settings = new Dictionary<string, string?>
        {
            ["host"] = "localhost",
            ["port"] = "8080"
        }.ToImmutableDictionary();

        var tags = ImmutableList.Create("primary", "fast");

        var model = new DownloadClient(
            Guid.Empty,
            "CompleteClient",
            "sabnzbd",
            true,
            tags,
            settings,
            DownloadClientStatus.Unknown,
            null,
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.CreateAsync(model, _userId);

        // Assert
        var entity = await (
            from c in _context.DownloadClients.AsNoTracking().Include(c => c.Settings).Include(c => c.Tags)
            where c.Name == "CompleteClient"
            select c).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<DownloadClient>>());
            Assert.That(entity, Is.Not.Null);
            Assert.That(entity!.Settings, Has.Count.EqualTo(2));
            Assert.That(entity.Tags, Has.Count.EqualTo(2));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesDownloadClient()
    {
        // Arrange
        var entity = new DownloadClientEntity
        {
            Name = "OriginalName",
            Provider = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.DownloadClients.Add(entity);
        _context.SaveChanges();

        var model = new DownloadClient(
            entity.Id,
            "UpdatedName",
            "sabnzbd",
            false,
            ImmutableList<string>.Empty,
            ImmutableDictionary<string, string?>.Empty,
            DownloadClientStatus.Healthy,
            DateTimeOffset.UtcNow,
            entity.CreatedAt,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        var updatedEntity = await (
            from c in _context.DownloadClients.AsNoTracking()
            where c.Id == entity.Id
            select c).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<DownloadClientEntity>()
                .With.Property(nameof(DownloadClientEntity.Name)).EqualTo("UpdatedName")
                .And.Property(nameof(DownloadClientEntity.Provider)).EqualTo("sabnzbd")
                .And.Property(nameof(DownloadClientEntity.Enabled)).False
                .And.Property(nameof(DownloadClientEntity.Status)).EqualTo(DownloadClientStatus.Healthy)
                .And.Property(nameof(DownloadClientEntity.ModifiedAt)).Not.Null
                .And.Property(nameof(DownloadClientEntity.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Update")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("DownloadClient")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateAsync_ReturnsFalse_WhenNotFound()
    {
        // Arrange
        var model = new DownloadClient(
            Guid.NewGuid(),
            "NonExistent",
            "qbittorrent",
            true,
            ImmutableList<string>.Empty,
            ImmutableDictionary<string, string?>.Empty,
            DownloadClientStatus.Unknown,
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
        var entity = new DownloadClientEntity
        {
            Name = "ClientToUpdate",
            Provider = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.DownloadClients.Add(entity);
        _context.SaveChanges();

        var newSettings = new Dictionary<string, string?>
        {
            ["newSetting1"] = "value1",
            ["newSetting2"] = "value2"
        }.ToImmutableDictionary();

        var model = new DownloadClient(
            entity.Id,
            entity.Name,
            entity.Provider,
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
        var settingEntities = await _context.DownloadClientSettings
            .AsNoTracking()
            .Where(s => s.DownloadClientId == entity.Id)
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(settingEntities, Has.Count.EqualTo(2));
            Assert.That(settingEntities, Has.One
                .With.Property(nameof(DownloadClientSettingEntity.Key)).EqualTo("newSetting1")
                .And.Property(nameof(DownloadClientSettingEntity.Value)).EqualTo("value1"));
            Assert.That(settingEntities, Has.One
                .With.Property(nameof(DownloadClientSettingEntity.Key)).EqualTo("newSetting2")
                .And.Property(nameof(DownloadClientSettingEntity.Value)).EqualTo("value2"));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesExistingSettings()
    {
        // Arrange
        var entity = new DownloadClientEntity
        {
            Name = "ClientWithSettings",
            Provider = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Settings.Add(new() { Key = "existingKey", Value = "oldValue" });
        _context.DownloadClients.Add(entity);
        _context.SaveChanges();

        var updatedSettings = new Dictionary<string, string?>
        {
            ["existingKey"] = "newValue"
        }.ToImmutableDictionary();

        var model = new DownloadClient(
            entity.Id,
            entity.Name,
            entity.Provider,
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
        var settingEntity = await _context.DownloadClientSettings
            .AsNoTracking()
            .Where(s => s.DownloadClientId == entity.Id)
            .Where(s => s.Key == "existingKey")
            .SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(settingEntity, Is.InstanceOf<DownloadClientSettingEntity>()
                .With.Property(nameof(DownloadClientSettingEntity.Value)).EqualTo("newValue"));
        }
    }

    [Test]
    public async Task UpdateAsync_RemovesSettings()
    {
        // Arrange
        var entity = new DownloadClientEntity
        {
            Name = "ClientWithSettings",
            Provider = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Settings.Add(new() { Key = "keepMe", Value = "keep" });
        entity.Settings.Add(new() { Key = "removeMe", Value = "remove" });
        _context.DownloadClients.Add(entity);
        _context.SaveChanges();

        var updatedSettings = new Dictionary<string, string?>
        {
            ["keepMe"] = "keep"
        }.ToImmutableDictionary();

        var model = new DownloadClient(
            entity.Id,
            entity.Name,
            entity.Provider,
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
        var settingEntities = await _context.DownloadClientSettings
            .AsNoTracking()
            .Where(s => s.DownloadClientId == entity.Id)
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(settingEntities, Has.Count.EqualTo(1));
            Assert.That(settingEntities, Has.One
                .With.Property(nameof(DownloadClientSettingEntity.Key)).EqualTo("keepMe"));
            Assert.That(settingEntities, Has.None
                .With.Property(nameof(DownloadClientSettingEntity.Key)).EqualTo("removeMe"));
        }
    }

    [Test]
    public async Task UpdateAsync_AddsUpdatesAndRemovesSettings()
    {
        // Arrange
        var entity = new DownloadClientEntity
        {
            Name = "ComplexClient",
            Provider = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Settings.Add(new() { Key = "toUpdate", Value = "oldValue" });
        entity.Settings.Add(new() { Key = "toRemove", Value = "removeMe" });
        _context.DownloadClients.Add(entity);
        _context.SaveChanges();

        var updatedSettings = new Dictionary<string, string?>
        {
            ["toUpdate"] = "newValue",
            ["toAdd"] = "addedValue"
        }.ToImmutableDictionary();

        var model = new DownloadClient(
            entity.Id,
            entity.Name,
            entity.Provider,
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
        var settingEntities = await _context.DownloadClientSettings
            .AsNoTracking()
            .Where(s => s.DownloadClientId == entity.Id)
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(settingEntities, Has.Count.EqualTo(2));
            Assert.That(settingEntities, Has.One
                .With.Property(nameof(DownloadClientSettingEntity.Key)).EqualTo("toUpdate")
                .And.Property(nameof(DownloadClientSettingEntity.Value)).EqualTo("newValue"));
            Assert.That(settingEntities, Has.One
                .With.Property(nameof(DownloadClientSettingEntity.Key)).EqualTo("toAdd")
                .And.Property(nameof(DownloadClientSettingEntity.Value)).EqualTo("addedValue"));
            Assert.That(settingEntities, Has.None
                .With.Property(nameof(DownloadClientSettingEntity.Key)).EqualTo("toRemove"));
        }
    }

    [Test]
    public async Task UpdateAsync_AddsNewTags()
    {
        // Arrange
        _context.Tags.Add(new() { Name = "newTag1" });
        _context.Tags.Add(new() { Name = "newTag2" });
        _context.SaveChanges();
        var entity = new DownloadClientEntity
        {
            Name = "ClientToUpdate",
            Provider = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.DownloadClients.Add(entity);
        _context.SaveChanges();

        var newTags = ImmutableList.Create("newTag1", "newTag2");

        var model = new DownloadClient(
            entity.Id,
            entity.Name,
            entity.Provider,
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
        var tagEntities = await _context.DownloadClientTags
            .AsNoTracking()
            .Where(t => t.DownloadClientId == entity.Id)
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(tagEntities, Has.Count.EqualTo(2));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(DownloadClientTagEntity.TagName)).EqualTo("newTag1"));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(DownloadClientTagEntity.TagName)).EqualTo("newTag2"));
        }
    }

    [Test]
    public async Task UpdateAsync_RemovesTags()
    {
        // Arrange
        _context.Tags.Add(new() { Name = "keepMe" });
        _context.Tags.Add(new() { Name = "removeMe" });
        var entity = new DownloadClientEntity
        {
            Name = "ClientWithTags",
            Provider = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Tags.Add(new() { TagName = "keepMe" });
        entity.Tags.Add(new() { TagName = "removeMe" });
        _context.DownloadClients.Add(entity);
        _context.SaveChanges();

        var updatedTags = ImmutableList.Create("keepMe");

        var model = new DownloadClient(
            entity.Id,
            entity.Name,
            entity.Provider,
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
        var tagEntities = await _context.DownloadClientTags
            .AsNoTracking()
            .Where(t => t.DownloadClientId == entity.Id)
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(tagEntities, Has.Count.EqualTo(1));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(DownloadClientTagEntity.TagName)).EqualTo("keepMe"));
            Assert.That(tagEntities, Has.None
                .With.Property(nameof(DownloadClientTagEntity.TagName)).EqualTo("removeMe"));
        }
    }

    [Test]
    public async Task UpdateAsync_AddsAndRemovesTags()
    {
        // Arrange
        _context.Tags.Add(new() { Name = "existing" });
        _context.Tags.Add(new() { Name = "toRemove" });
        _context.Tags.Add(new() { Name = "toAdd" });
        var entity = new DownloadClientEntity
        {
            Name = "ComplexClient",
            Provider = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Tags.Add(new() { TagName = "existing" });
        entity.Tags.Add(new() { TagName = "toRemove" });
        _context.DownloadClients.Add(entity);
        _context.SaveChanges();

        var updatedTags = ImmutableList.Create("existing", "toAdd");

        var model = new DownloadClient(
            entity.Id,
            entity.Name,
            entity.Provider,
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
        var tagEntities = await _context.DownloadClientTags
            .AsNoTracking()
            .Where(t => t.DownloadClientId == entity.Id)
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(tagEntities, Has.Count.EqualTo(2));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(DownloadClientTagEntity.TagName)).EqualTo("existing"));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(DownloadClientTagEntity.TagName)).EqualTo("toAdd"));
            Assert.That(tagEntities, Has.None
                .With.Property(nameof(DownloadClientTagEntity.TagName)).EqualTo("toRemove"));
        }
    }

    [Test]
    public async Task DeleteAsync_DeletesDownloadClient()
    {
        // Arrange
        var entity = new DownloadClientEntity
        {
            Name = "ToDelete",
            Provider = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.DownloadClients.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.DeleteAsync(entity.Id, _userId);

        // Assert
        var entityExists = await _context.DownloadClients.AnyAsync(c => c.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(entityExists, Is.False);
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Delete")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("DownloadClient")
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
        var entity = new DownloadClientEntity
        {
            Name = "ClientWithSettings",
            Provider = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Settings.Add(new() { Key = "setting1", Value = "value1" });
        entity.Settings.Add(new() { Key = "setting2", Value = "value2" });
        _context.DownloadClients.Add(entity);
        _context.SaveChanges();

        var clientId = entity.Id;

        // Act
        var result = await _subject.DeleteAsync(clientId, _userId);

        // Assert
        var settingsExist = await _context.DownloadClientSettings
            .AnyAsync(s => s.DownloadClientId == clientId);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(settingsExist, Is.False);
        }
    }

    [Test]
    public async Task DeleteAsync_CascadesToTags()
    {
        // Arrange
        _context.Tags.Add(new() { Name = "tag1" });
        _context.Tags.Add(new() { Name = "tag2" });
        var entity = new DownloadClientEntity
        {
            Name = "ClientWithTags",
            Provider = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Tags.Add(new() { TagName = "tag1" });
        entity.Tags.Add(new() { TagName = "tag2" });
        _context.DownloadClients.Add(entity);
        _context.SaveChanges();

        var clientId = entity.Id;

        // Act
        var result = await _subject.DeleteAsync(clientId, _userId);

        // Assert
        var tagsExist = await _context.DownloadClientTags
            .AnyAsync(t => t.DownloadClientId == clientId);

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
        var entity = new DownloadClientEntity
        {
            Name = "CompleteClient",
            Provider = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Settings.Add(new() { Key = "setting1", Value = "value1" });
        entity.Tags.Add(new() { TagName = "tag1" });
        _context.DownloadClients.Add(entity);
        _context.SaveChanges();

        var clientId = entity.Id;

        // Act
        var result = await _subject.DeleteAsync(clientId, _userId);

        // Assert
        var settingsExist = await _context.DownloadClientSettings
            .AnyAsync(s => s.DownloadClientId == clientId);
        var tagsExist = await _context.DownloadClientTags
            .AnyAsync(t => t.DownloadClientId == clientId);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(settingsExist, Is.False);
            Assert.That(tagsExist, Is.False);
        }
    }

    [Test]
    public async Task PatchAsync_PatchesName()
    {
        // Arrange
        var entity = new DownloadClientEntity
        {
            Name = "OriginalName",
            Provider = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.DownloadClients.Add(entity);
        _context.SaveChanges();

        var patch = new DownloadClientPatch
        {
            Id = entity.Id,
            Name = new FieldPatch<string>("PatchedName")
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var updatedEntity = await _context.DownloadClients
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<DownloadClientEntity>()
                .With.Property(nameof(DownloadClientEntity.Name)).EqualTo("PatchedName")
                .And.Property(nameof(DownloadClientEntity.ModifiedAt)).Not.Null
                .And.Property(nameof(DownloadClientEntity.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Update")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("DownloadClient")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesEnabled()
    {
        // Arrange
        var entity = new DownloadClientEntity
        {
            Name = "TestClient",
            Provider = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.DownloadClients.Add(entity);
        _context.SaveChanges();

        var patch = new DownloadClientPatch
        {
            Id = entity.Id,
            Enabled = new FieldPatch<bool>(false)
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var updatedEntity = await _context.DownloadClients
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<DownloadClientEntity>()
                .With.Property(nameof(DownloadClientEntity.Enabled)).False);
        }
    }

    [Test]
    public async Task PatchAsync_PatchesSettings()
    {
        // Arrange
        var entity = new DownloadClientEntity
        {
            Name = "TestClient",
            Provider = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Settings.Add(new() { Key = "toUpdate", Value = "oldValue" });
        entity.Settings.Add(new() { Key = "toRemove", Value = "removeMe" });
        _context.DownloadClients.Add(entity);
        _context.SaveChanges();

        var patch = new DownloadClientPatch
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
        var settingEntities = await _context.DownloadClientSettings
            .AsNoTracking()
            .Where(s => s.DownloadClientId == entity.Id)
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(settingEntities, Has.Count.EqualTo(2));
            Assert.That(settingEntities, Has.One
                .With.Property(nameof(DownloadClientSettingEntity.Key)).EqualTo("toUpdate")
                .And.Property(nameof(DownloadClientSettingEntity.Value)).EqualTo("newValue"));
            Assert.That(settingEntities, Has.One
                .With.Property(nameof(DownloadClientSettingEntity.Key)).EqualTo("toAdd")
                .And.Property(nameof(DownloadClientSettingEntity.Value)).EqualTo("addedValue"));
            Assert.That(settingEntities, Has.None
                .With.Property(nameof(DownloadClientSettingEntity.Key)).EqualTo("toRemove"));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesTags_AddOnly()
    {
        // Arrange
        _context.Tags.Add(new() { Name = "existing" });
        _context.Tags.Add(new() { Name = "newTag1" });
        _context.Tags.Add(new() { Name = "newTag2" });
        var entity = new DownloadClientEntity
        {
            Name = "TestClient",
            Provider = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Tags.Add(new() { TagName = "existing" });
        _context.DownloadClients.Add(entity);
        _context.SaveChanges();

        var patch = new DownloadClientPatch
        {
            Id = entity.Id,
            Tags = new SetPatch<string, string>(Add: ["newTag1", "newTag2"], Remove: null)
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var tagEntities = await _context.DownloadClientTags
            .AsNoTracking()
            .Where(t => t.DownloadClientId == entity.Id)
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(tagEntities, Has.Count.EqualTo(3));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(DownloadClientTagEntity.TagName)).EqualTo("existing"));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(DownloadClientTagEntity.TagName)).EqualTo("newTag1"));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(DownloadClientTagEntity.TagName)).EqualTo("newTag2"));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesTags_RemoveOnly()
    {
        // Arrange
        _context.Tags.Add(new() { Name = "keepMe" });
        _context.Tags.Add(new() { Name = "removeMe" });
        var entity = new DownloadClientEntity
        {
            Name = "TestClient",
            Provider = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Tags.Add(new() { TagName = "keepMe" });
        entity.Tags.Add(new() { TagName = "removeMe" });
        _context.DownloadClients.Add(entity);
        _context.SaveChanges();

        var patch = new DownloadClientPatch
        {
            Id = entity.Id,
            Tags = new SetPatch<string, string>(Add: null, Remove: ["removeMe"])
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var tagEntities = await _context.DownloadClientTags
            .AsNoTracking()
            .Where(t => t.DownloadClientId == entity.Id)
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(tagEntities, Has.Count.EqualTo(1));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(DownloadClientTagEntity.TagName)).EqualTo("keepMe"));
            Assert.That(tagEntities, Has.None
                .With.Property(nameof(DownloadClientTagEntity.TagName)).EqualTo("removeMe"));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesTags_AddAndRemove()
    {
        // Arrange
        _context.Tags.Add(new() { Name = "existing" });
        _context.Tags.Add(new() { Name = "toRemove" });
        _context.Tags.Add(new() { Name = "toAdd" });
        var entity = new DownloadClientEntity
        {
            Name = "TestClient",
            Provider = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Tags.Add(new() { TagName = "existing" });
        entity.Tags.Add(new() { TagName = "toRemove" });
        _context.DownloadClients.Add(entity);
        _context.SaveChanges();

        var patch = new DownloadClientPatch
        {
            Id = entity.Id,
            Tags = new SetPatch<string, string>(Add: ["toAdd"], Remove: ["toRemove"])
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var tagEntities = await _context.DownloadClientTags
            .AsNoTracking()
            .Where(t => t.DownloadClientId == entity.Id)
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(tagEntities, Has.Count.EqualTo(2));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(DownloadClientTagEntity.TagName)).EqualTo("existing"));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(DownloadClientTagEntity.TagName)).EqualTo("toAdd"));
            Assert.That(tagEntities, Has.None
                .With.Property(nameof(DownloadClientTagEntity.TagName)).EqualTo("toRemove"));
        }
    }

    [Test]
    public async Task PatchAsync_ReturnsFalse_WhenNotFound()
    {
        // Arrange
        var patch = new DownloadClientPatch
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
    public async Task GetByNameAsync_ReturnsDownloadClient()
    {
        // Arrange
        var entity = new DownloadClientEntity
        {
            Name = "UniqueClientName",
            Provider = "sabnzbd",
            Enabled = true,
            Status = DownloadClientStatus.Healthy,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.DownloadClients.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByNameAsync("UniqueClientName");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<DownloadClient?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(DownloadClient.Name))
                .EqualTo("UniqueClientName")
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(DownloadClient.Provider))
                .EqualTo("sabnzbd")
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(DownloadClient.Status))
                .EqualTo(DownloadClientStatus.Healthy));
    }

    [Test]
    public async Task GetByNameAsync_ReturnsNull_WhenNotFound()
    {
        // Act
        var result = await _subject.GetByNameAsync("NonExistentClient");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<DownloadClient?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task GetByNameAsync_IncludesSettingsAndTags()
    {
        // Arrange
        _context.Tags.Add(new() { Name = "primary" });
        var entity = new DownloadClientEntity
        {
            Name = "ClientWithChildren",
            Provider = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Settings.Add(new() { Key = "host", Value = "localhost" });
        entity.Tags.Add(new() { TagName = "primary" });
        _context.DownloadClients.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByNameAsync("ClientWithChildren");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<DownloadClient?>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(DownloadClient.Settings))
                .Count.EqualTo(1));
            Assert.That(result, Is.InstanceOf<SuccessResult<DownloadClient?>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(DownloadClient.Tags))
                .Count.EqualTo(1));
        }
    }

    [Test]
    public async Task GetByProviderAsync_ReturnsDownloadClients()
    {
        // Arrange
        var entity1 = new DownloadClientEntity
        {
            Name = "QbitClient1",
            Provider = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Healthy,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new DownloadClientEntity
        {
            Name = "QbitClient2",
            Provider = "qbittorrent",
            Enabled = false,
            Status = DownloadClientStatus.Unhealthy,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity3 = new DownloadClientEntity
        {
            Name = "SabnzbdClient",
            Provider = "sabnzbd",
            Enabled = true,
            Status = DownloadClientStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.DownloadClients.AddRange(entity1, entity2, entity3);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByProviderAsync("qbittorrent");

        // Assert
        var success = result as SuccessResult<IReadOnlyList<DownloadClient>>;
        Assert.That(success, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(success!.Value, Has.Count.EqualTo(2));
            Assert.That(success.Value, Has.All
                .With.Property(nameof(DownloadClient.Provider)).EqualTo("qbittorrent"));
        }
    }

    [Test]
    public async Task GetByProviderAsync_ReturnsEmptyList_WhenNoMatches()
    {
        // Arrange
        var entity = new DownloadClientEntity
        {
            Name = "QbitClient",
            Provider = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.DownloadClients.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByProviderAsync("transmission");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<DownloadClient>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task GetByProviderAsync_IncludesSettingsAndTags()
    {
        // Arrange
        var entity = new DownloadClientEntity
        {
            Name = "QbitClient",
            Provider = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow,
            Settings =
            {
                new () { Key = "host", Value = "localhost" }
            },
            Tags =
            {
                new () { TagName = "movies" }
            }
        };
        _context.Tags.Add(new() { Name = "movies" });
        _context.DownloadClients.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByProviderAsync("qbittorrent");

        // Assert
        var success = result as SuccessResult<IReadOnlyList<DownloadClient>>;
        Assert.That(success, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(success!.Value[0].Settings, Has.Count.EqualTo(1));
            Assert.That(success.Value[0].Tags, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task GetByStatusAsync_ReturnsDownloadClients()
    {
        // Arrange
        var entity1 = new DownloadClientEntity
        {
            Name = "HealthyClient1",
            Provider = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Healthy,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new DownloadClientEntity
        {
            Name = "HealthyClient2",
            Provider = "sabnzbd",
            Enabled = true,
            Status = DownloadClientStatus.Healthy,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity3 = new DownloadClientEntity
        {
            Name = "UnhealthyClient",
            Provider = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Unhealthy,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.DownloadClients.AddRange(entity1, entity2, entity3);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByStatusAsync(DownloadClientStatus.Healthy);

        // Assert
        var success = result as SuccessResult<IReadOnlyList<DownloadClient>>;
        Assert.That(success, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(success!.Value, Has.Count.EqualTo(2));
            Assert.That(success.Value, Has.All
                .With.Property(nameof(DownloadClient.Status)).EqualTo(DownloadClientStatus.Healthy));
        }
    }

    [Test]
    public async Task GetByStatusAsync_ReturnsEmptyList_WhenNoMatches()
    {
        // Arrange
        var entity = new DownloadClientEntity
        {
            Name = "HealthyClient",
            Provider = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Healthy,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.DownloadClients.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByStatusAsync(DownloadClientStatus.Stale);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<DownloadClient>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task GetByStatusAsync_IncludesSettingsAndTags()
    {
        // Arrange
        var entity = new DownloadClientEntity
        {
            Name = "HealthyClient",
            Provider = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Healthy,
            CreatedAt = DateTimeOffset.UtcNow,
            Settings =
            {
                new () { Key = "host", Value = "localhost"}
            },
            Tags =
            {
                new () { TagName = "primary"}
            }
        };
        _context.Tags.Add(new() { Name = "primary" });
        _context.DownloadClients.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByStatusAsync(DownloadClientStatus.Healthy);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<DownloadClient>>>()
                .With.Property(nameof(SuccessResult<>.Value))
                .One.With.Property(nameof(DownloadClient.Settings)).Count.EqualTo(1));
            Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<DownloadClient>>>()
                .With.Property(nameof(SuccessResult<>.Value))
                .One.With.Property(nameof(DownloadClient.Tags)).Count.EqualTo(1));
        }
    }

    [Test]
    public async Task GetEnabledAsync_ReturnsEnabledDownloadClients()
    {
        // Arrange
        var entity1 = new DownloadClientEntity
        {
            Name = "EnabledClient1",
            Provider = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Healthy,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new DownloadClientEntity
        {
            Name = "EnabledClient2",
            Provider = "sabnzbd",
            Enabled = true,
            Status = DownloadClientStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity3 = new DownloadClientEntity
        {
            Name = "DisabledClient",
            Provider = "qbittorrent",
            Enabled = false,
            Status = DownloadClientStatus.Unhealthy,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.DownloadClients.AddRange(entity1, entity2, entity3);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetEnabledAsync(default);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<DownloadClient>>>()
                .With.Property(nameof(SuccessResult<>.Value)).With.Count.EqualTo(2));
            Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<DownloadClient>>>()
                .With.Property(nameof(SuccessResult<>.Value)).With.All
                .With.Property(nameof(DownloadClient.Enabled)).True);
        }
    }

    [Test]
    public async Task GetEnabledAsync_ReturnsEmptyList_WhenNoEnabledClients()
    {
        // Arrange
        var entity = new DownloadClientEntity
        {
            Name = "DisabledClient",
            Provider = "qbittorrent",
            Enabled = false,
            Status = DownloadClientStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.DownloadClients.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetEnabledAsync(default);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<DownloadClient>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task GetEnabledAsync_IncludesSettingsAndTags()
    {
        // Arrange
        _context.Tags.Add(new() { Name = "primary" });
        var entity = new DownloadClientEntity
        {
            Name = "EnabledClient",
            Provider = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Settings.Add(new() { Key = "host", Value = "localhost" });
        entity.Tags.Add(new() { TagName = "primary" });

        _context.DownloadClients.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetEnabledAsync(default);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<DownloadClient>>>()
                .With.Property(nameof(SuccessResult<>.Value))
                .One.With.Property(nameof(DownloadClient.Settings)).Count.EqualTo(1));
            Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<DownloadClient>>>()
                .With.Property(nameof(SuccessResult<>.Value))
                .One.With.Property(nameof(DownloadClient.Tags)).Count.EqualTo(1));
        }
    }

    [Test]
    public async Task UpdateStatusAsync_UpdatesStatusAndLastChecked()
    {
        // Arrange
        var entity = new DownloadClientEntity
        {
            Name = "TestClient",
            Provider = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Unknown,
            LastChecked = null,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.DownloadClients.Add(entity);
        _context.SaveChanges();

        var newLastChecked = DateTimeOffset.UtcNow;

        // Act
        var result = await _subject.UpdateStatusAsync(entity.Id, DownloadClientStatus.Healthy, newLastChecked);

        // Assert
        var updatedEntity = await _context.DownloadClients
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<DownloadClientEntity>()
                .With.Property(nameof(DownloadClientEntity.Status)).EqualTo(DownloadClientStatus.Healthy)
                .And.Property(nameof(DownloadClientEntity.LastChecked)).EqualTo(newLastChecked));
        }
    }

    [Test]
    public async Task UpdateStatusAsync_ReturnsFalse_WhenNotFound()
    {
        // Act
        var result = await _subject.UpdateStatusAsync(Guid.NewGuid(),
            DownloadClientStatus.Healthy,
            DateTimeOffset.UtcNow);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<bool>>()
            .With.Property(nameof(SuccessResult<>.Value)).False);
    }

    [Test]
    public async Task UpdateStatusAsync_DoesNotCreateHistoryEntry()
    {
        // Arrange
        var entity = new DownloadClientEntity
        {
            Name = "TestClient",
            Provider = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.DownloadClients.Add(entity);
        _context.SaveChanges();

        // Act
        await _subject.UpdateStatusAsync(entity.Id, DownloadClientStatus.Healthy, DateTimeOffset.UtcNow);

        // Assert
        var historyEntries = await _context.HistoryEntries.ToListAsync();

        Assert.That(historyEntries, Is.Empty);
    }
}
