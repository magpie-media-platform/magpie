using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions.Data;
using Magpie.Core.Data;
using Magpie.Data.Database;
using Magpie.Models;
using Magpie.Models.MediaServers;
using Magpie.Models.Result;
using Magpie.Models.Users;
using Magpie.Test.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NUnit.Framework;

using HistoryEntryEntity = Magpie.Data.Database.Entities.HistoryEntry;
using MediaServerEntity = Magpie.Data.Database.Entities.MediaServer;
using MediaServerSettingEntity = Magpie.Data.Database.Entities.MediaServerSetting;
using UserEntity = Magpie.Data.Database.Entities.User;

namespace Magpie.Test.Core.Data;

[TestFixture]
public class MediaServerRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IMediaServerRepository _subject = null!;
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

        _subject = new MediaServerRepository(
            _context,
            historyRepo,
            loggerFactory.CreateLogger<MediaServerRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context?.Dispose();
    }

    [Test]
    public async Task GetByIdAsync_ReturnsMediaServer()
    {
        // Arrange
        var entity = new MediaServerEntity
        {
            Name = "TestServer",
            Provider = "plex",
            Enabled = true,
            Status = MediaServerStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.MediaServers.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByIdAsync(entity.Id);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<MediaServer?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(MediaServer.Name))
                .EqualTo("TestServer")
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(MediaServer.Provider))
                .EqualTo("plex")
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(MediaServer.Enabled))
                .True
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(MediaServer.Status))
                .EqualTo(MediaServerStatus.Unknown));
    }

    [Test]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        // Act
        var result = await _subject.GetByIdAsync(Guid.NewGuid());

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<MediaServer?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task GetByIdAsync_IncludesSettings()
    {
        // Arrange
        var entity = new MediaServerEntity
        {
            Name = "ServerWithSettings",
            Provider = "plex",
            Enabled = true,
            Status = MediaServerStatus.Healthy,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Settings.Add(new() { Key = "host", Value = "localhost" });
        entity.Settings.Add(new() { Key = "port", Value = "32400" });
        _context.MediaServers.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByIdAsync(entity.Id);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<MediaServer?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(MediaServer.Settings)).With.Count.EqualTo(2));
    }

    [Test]
    public async Task GetAllAsync_ReturnsAllMediaServers()
    {
        // Arrange
        var entity1 = new MediaServerEntity
        {
            Name = "Server1",
            Provider = "plex",
            Enabled = true,
            Status = MediaServerStatus.Healthy,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new MediaServerEntity
        {
            Name = "Server2",
            Provider = "jellyfin",
            Enabled = false,
            Status = MediaServerStatus.Unhealthy,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.MediaServers.AddRange(entity1, entity2);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetAllAsync();

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<MediaServer>>>()
            .With.Property(nameof(SuccessResult<>.Value)).With.Count.EqualTo(2));
    }

    [Test]
    public async Task GetAllAsync_ReturnsEmptyList_WhenNoServers()
    {
        // Act
        var result = await _subject.GetAllAsync();

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<MediaServer>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task GetAllAsync_IncludesSettings()
    {
        // Arrange
        var entity = new MediaServerEntity
        {
            Name = "ServerWithSettings",
            Provider = "plex",
            Enabled = true,
            Status = MediaServerStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Settings.Add(new() { Key = "apiKey", Value = "test-key" });
        _context.MediaServers.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetAllAsync();

        // Assert
        var success = result as SuccessResult<IReadOnlyList<MediaServer>>;
        Assert.That(success, Is.Not.Null);
        Assert.That(success!.Value[0].Settings, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task CreateAsync_CreatesMediaServer()
    {
        // Arrange
        var model = new MediaServer(
            Guid.Empty,
            "NewServer",
            "plex",
            true,
            ImmutableDictionary<string, string?>.Empty,
            MediaServerStatus.Unknown,
            null,
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.CreateAsync(model, _userId);

        // Assert
        var entity = await (
            from s in _context.MediaServers.AsNoTracking()
            where s.Name == "NewServer"
            select s).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<MediaServer>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(MediaServer.Name))
                    .EqualTo("NewServer")
                .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(MediaServer.Provider))
                    .EqualTo("plex")
                .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(MediaServer.Enabled))
                    .True
                .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(MediaServer.Status))
                    .EqualTo(MediaServerStatus.Unknown));
            Assert.That(entity, Is.InstanceOf<MediaServerEntity>()
                .With.Property(nameof(MediaServerEntity.Name)).EqualTo("NewServer")
                .And.Property(nameof(MediaServerEntity.Provider)).EqualTo("plex")
                .And.Property(nameof(MediaServerEntity.CreatedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Create")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("MediaServer")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesMediaServerWithSettings()
    {
        // Arrange
        var settings = new Dictionary<string, string?>
        {
            ["host"] = "192.168.1.100",
            ["port"] = "32400",
            ["token"] = "abc123"
        }.ToImmutableDictionary();

        var model = new MediaServer(
            Guid.Empty,
            "PlexServer",
            "plex",
            true,
            settings,
            MediaServerStatus.Unknown,
            null,
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.CreateAsync(model, _userId);

        // Assert
        var entity = await (
            from s in _context.MediaServers.AsNoTracking().Include(s => s.Settings)
            where s.Name == "PlexServer"
            select s).SingleOrDefaultAsync();
        var historyEntities = await _context.HistoryEntries.ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<MediaServer>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(MediaServer.Settings))
                .With.Count.EqualTo(3));
            Assert.That(entity, Is.Not.Null);
            Assert.That(entity!.Settings, Has.Count.EqualTo(3));
            Assert.That(entity.Settings, Has.One
                .With.Property(nameof(MediaServerSettingEntity.Key)).EqualTo("host")
                .And.Property(nameof(MediaServerSettingEntity.Value)).EqualTo("192.168.1.100"));
            Assert.That(entity.Settings, Has.One
                .With.Property(nameof(MediaServerSettingEntity.Key)).EqualTo("port")
                .And.Property(nameof(MediaServerSettingEntity.Value)).EqualTo("32400"));
            Assert.That(entity.Settings, Has.One
                .With.Property(nameof(MediaServerSettingEntity.Key)).EqualTo("token")
                .And.Property(nameof(MediaServerSettingEntity.Value)).EqualTo("abc123"));
            Assert.That(historyEntities, Has.Count.EqualTo(4));
            Assert.That(historyEntities, Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Create")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("MediaServer"));
            Assert.That(historyEntities, Has.Exactly(3)
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Create")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("MediaServerSetting"));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesMediaServer()
    {
        // Arrange
        var entity = new MediaServerEntity
        {
            Name = "OriginalName",
            Provider = "plex",
            Enabled = true,
            Status = MediaServerStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.MediaServers.Add(entity);
        _context.SaveChanges();

        var model = new MediaServer(
            entity.Id,
            "UpdatedName",
            "jellyfin",
            false,
            ImmutableDictionary<string, string?>.Empty,
            MediaServerStatus.Healthy,
            DateTimeOffset.UtcNow,
            entity.CreatedAt,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        var updatedEntity = await (
            from s in _context.MediaServers.AsNoTracking()
            where s.Id == entity.Id
            select s).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<MediaServerEntity>()
                .With.Property(nameof(MediaServerEntity.Name)).EqualTo("UpdatedName")
                .And.Property(nameof(MediaServerEntity.Provider)).EqualTo("jellyfin")
                .And.Property(nameof(MediaServerEntity.Enabled)).False
                .And.Property(nameof(MediaServerEntity.Status)).EqualTo(MediaServerStatus.Healthy)
                .And.Property(nameof(MediaServerEntity.ModifiedAt)).Not.Null
                .And.Property(nameof(MediaServerEntity.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Update")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("MediaServer")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateAsync_ReturnsFalse_WhenNotFound()
    {
        // Arrange
        var model = new MediaServer(
            Guid.NewGuid(),
            "NonExistent",
            "plex",
            true,
            ImmutableDictionary<string, string?>.Empty,
            MediaServerStatus.Unknown,
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
        var entity = new MediaServerEntity
        {
            Name = "ServerToUpdate",
            Provider = "plex",
            Enabled = true,
            Status = MediaServerStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.MediaServers.Add(entity);
        _context.SaveChanges();

        var newSettings = new Dictionary<string, string?>
        {
            ["newSetting1"] = "value1",
            ["newSetting2"] = "value2"
        }.ToImmutableDictionary();

        var model = new MediaServer(
            entity.Id,
            entity.Name,
            entity.Provider,
            entity.Enabled,
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
        var settingEntities = await _context.MediaServerSettings
            .AsNoTracking()
            .Where(s => s.MediaServerId == entity.Id)
            .ToListAsync();
        var historyEntities = await _context.HistoryEntries.ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(settingEntities, Has.Count.EqualTo(2));
            Assert.That(settingEntities, Has.One
                .With.Property(nameof(MediaServerSettingEntity.Key)).EqualTo("newSetting1")
                .And.Property(nameof(MediaServerSettingEntity.Value)).EqualTo("value1"));
            Assert.That(settingEntities, Has.One
                .With.Property(nameof(MediaServerSettingEntity.Key)).EqualTo("newSetting2")
                .And.Property(nameof(MediaServerSettingEntity.Value)).EqualTo("value2"));
            Assert.That(historyEntities, Has.Count.EqualTo(3));
            Assert.That(historyEntities, Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Update")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("MediaServer"));
            Assert.That(historyEntities, Has.Exactly(2)
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Create")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("MediaServerSetting"));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesExistingSettings()
    {
        // Arrange
        var entity = new MediaServerEntity
        {
            Name = "ServerWithSettings",
            Provider = "plex",
            Enabled = true,
            Status = MediaServerStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Settings.Add(new() { Key = "existingKey", Value = "oldValue" });
        _context.MediaServers.Add(entity);
        _context.SaveChanges();

        var updatedSettings = new Dictionary<string, string?>
        {
            ["existingKey"] = "newValue"
        }.ToImmutableDictionary();

        var model = new MediaServer(
            entity.Id,
            entity.Name,
            entity.Provider,
            entity.Enabled,
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
        var settingEntity = await _context.MediaServerSettings
            .AsNoTracking()
            .Where(s => s.MediaServerId == entity.Id)
            .Where(s => s.Key == "existingKey")
            .SingleOrDefaultAsync();
        var historyEntities = await _context.HistoryEntries.ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(settingEntity, Is.InstanceOf<MediaServerSettingEntity>()
                .With.Property(nameof(MediaServerSettingEntity.Value)).EqualTo("newValue"));
            Assert.That(historyEntities, Has.Count.EqualTo(2));
            Assert.That(historyEntities, Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Update")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("MediaServer"));
            Assert.That(historyEntities, Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Update")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("MediaServerSetting"));
        }
    }

    [Test]
    public async Task UpdateAsync_RemovesSettings()
    {
        // Arrange
        var entity = new MediaServerEntity
        {
            Name = "ServerWithSettings",
            Provider = "plex",
            Enabled = true,
            Status = MediaServerStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Settings.Add(new() { Key = "keepMe", Value = "keep" });
        entity.Settings.Add(new() { Key = "removeMe", Value = "remove" });
        _context.MediaServers.Add(entity);
        _context.SaveChanges();

        var updatedSettings = new Dictionary<string, string?>
        {
            ["keepMe"] = "keep"
        }.ToImmutableDictionary();

        var model = new MediaServer(
            entity.Id,
            entity.Name,
            entity.Provider,
            entity.Enabled,
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
        var settingEntities = await _context.MediaServerSettings
            .AsNoTracking()
            .Where(s => s.MediaServerId == entity.Id)
            .ToListAsync();
        var historyEntities = await _context.HistoryEntries.ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(settingEntities, Has.Count.EqualTo(1));
            Assert.That(settingEntities, Has.One
                .With.Property(nameof(MediaServerSettingEntity.Key)).EqualTo("keepMe"));
            Assert.That(settingEntities, Has.None
                .With.Property(nameof(MediaServerSettingEntity.Key)).EqualTo("removeMe"));
            Assert.That(historyEntities, Has.Count.EqualTo(2));
            Assert.That(historyEntities, Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Update")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("MediaServer"));
            Assert.That(historyEntities, Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Delete")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("MediaServerSetting"));
        }
    }

    [Test]
    public async Task UpdateAsync_AddsUpdatesAndRemovesSettings()
    {
        // Arrange
        var entity = new MediaServerEntity
        {
            Name = "ComplexServer",
            Provider = "plex",
            Enabled = true,
            Status = MediaServerStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Settings.Add(new() { Key = "toUpdate", Value = "oldValue" });
        entity.Settings.Add(new() { Key = "toRemove", Value = "removeMe" });
        _context.MediaServers.Add(entity);
        _context.SaveChanges();

        var updatedSettings = new Dictionary<string, string?>
        {
            ["toUpdate"] = "newValue",
            ["toAdd"] = "addedValue"
        }.ToImmutableDictionary();

        var model = new MediaServer(
            entity.Id,
            entity.Name,
            entity.Provider,
            entity.Enabled,
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
        var settingEntities = await _context.MediaServerSettings
            .AsNoTracking()
            .Where(s => s.MediaServerId == entity.Id)
            .ToListAsync();
        var historyEntities = await _context.HistoryEntries.ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(settingEntities, Has.Count.EqualTo(2));
            Assert.That(settingEntities, Has.One
                .With.Property(nameof(MediaServerSettingEntity.Key)).EqualTo("toUpdate")
                .And.Property(nameof(MediaServerSettingEntity.Value)).EqualTo("newValue"));
            Assert.That(settingEntities, Has.One
                .With.Property(nameof(MediaServerSettingEntity.Key)).EqualTo("toAdd")
                .And.Property(nameof(MediaServerSettingEntity.Value)).EqualTo("addedValue"));
            Assert.That(settingEntities, Has.None
                .With.Property(nameof(MediaServerSettingEntity.Key)).EqualTo("toRemove"));
            Assert.That(historyEntities, Has.Count.EqualTo(4));
            Assert.That(historyEntities, Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Update")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("MediaServer"));
            Assert.That(historyEntities, Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Create")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("MediaServerSetting"));
            Assert.That(historyEntities, Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Update")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("MediaServerSetting"));
            Assert.That(historyEntities, Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Delete")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("MediaServerSetting"));
        }
    }

    [Test]
    public async Task DeleteAsync_DeletesMediaServer()
    {
        // Arrange
        var entity = new MediaServerEntity
        {
            Name = "ToDelete",
            Provider = "plex",
            Enabled = true,
            Status = MediaServerStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.MediaServers.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.DeleteAsync(entity.Id, _userId);

        // Assert
        var entityExists = await _context.MediaServers.AnyAsync(s => s.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(entityExists, Is.False);
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Delete")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("MediaServer")
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
        var entity = new MediaServerEntity
        {
            Name = "ServerWithSettings",
            Provider = "plex",
            Enabled = true,
            Status = MediaServerStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Settings.Add(new() { Key = "setting1", Value = "value1" });
        entity.Settings.Add(new() { Key = "setting2", Value = "value2" });
        _context.MediaServers.Add(entity);
        _context.SaveChanges();

        var serverId = entity.Id;

        // Act
        var result = await _subject.DeleteAsync(serverId, _userId);

        // Assert
        var settingsExist = await _context.MediaServerSettings
            .AnyAsync(s => s.MediaServerId == serverId);
        var historyEntities = await _context.HistoryEntries.ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(settingsExist, Is.False);
            Assert.That(historyEntities, Has.Count.EqualTo(3));
            Assert.That(historyEntities, Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Delete")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("MediaServer"));
            Assert.That(historyEntities, Has.Exactly(2)
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Delete")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("MediaServerSetting"));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesName()
    {
        // Arrange
        var entity = new MediaServerEntity
        {
            Name = "OriginalName",
            Provider = "plex",
            Enabled = true,
            Status = MediaServerStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.MediaServers.Add(entity);
        _context.SaveChanges();

        var patch = new MediaServerPatch
        {
            Id = entity.Id,
            Name = new FieldPatch<string>("PatchedName")
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var updatedEntity = await _context.MediaServers
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<MediaServerEntity>()
                .With.Property(nameof(MediaServerEntity.Name)).EqualTo("PatchedName")
                .And.Property(nameof(MediaServerEntity.ModifiedAt)).Not.Null
                .And.Property(nameof(MediaServerEntity.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Update")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("MediaServer")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesEnabled()
    {
        // Arrange
        var entity = new MediaServerEntity
        {
            Name = "TestServer",
            Provider = "plex",
            Enabled = true,
            Status = MediaServerStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.MediaServers.Add(entity);
        _context.SaveChanges();

        var patch = new MediaServerPatch
        {
            Id = entity.Id,
            Enabled = new FieldPatch<bool>(false)
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var updatedEntity = await _context.MediaServers
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<MediaServerEntity>()
                .With.Property(nameof(MediaServerEntity.Enabled)).False);
        }
    }

    [Test]
    public async Task PatchAsync_PatchesSettings()
    {
        // Arrange
        var entity = new MediaServerEntity
        {
            Name = "TestServer",
            Provider = "plex",
            Enabled = true,
            Status = MediaServerStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Settings.Add(new() { Key = "toUpdate", Value = "oldValue" });
        entity.Settings.Add(new() { Key = "toRemove", Value = "removeMe" });
        _context.MediaServers.Add(entity);
        _context.SaveChanges();

        var patch = new MediaServerPatch
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
        var settingEntities = await _context.MediaServerSettings
            .AsNoTracking()
            .Where(s => s.MediaServerId == entity.Id)
            .ToListAsync();

        var historyEntities = await _context.HistoryEntries.ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(settingEntities, Has.Count.EqualTo(2));
            Assert.That(settingEntities, Has.One
                .With.Property(nameof(MediaServerSettingEntity.Key)).EqualTo("toUpdate")
                .And.Property(nameof(MediaServerSettingEntity.Value)).EqualTo("newValue"));
            Assert.That(settingEntities, Has.One
                .With.Property(nameof(MediaServerSettingEntity.Key)).EqualTo("toAdd")
                .And.Property(nameof(MediaServerSettingEntity.Value)).EqualTo("addedValue"));
            Assert.That(settingEntities, Has.None
                .With.Property(nameof(MediaServerSettingEntity.Key)).EqualTo("toRemove"));
            Assert.That(historyEntities, Has.Count.EqualTo(4));
            Assert.That(historyEntities, Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Update")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("MediaServer"));
            Assert.That(historyEntities, Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Create")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("MediaServerSetting"));
            Assert.That(historyEntities, Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Update")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("MediaServerSetting"));
            Assert.That(historyEntities, Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Delete")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("MediaServerSetting"));
        }
    }

    [Test]
    public async Task PatchAsync_ReturnsFalse_WhenNotFound()
    {
        // Arrange
        var patch = new MediaServerPatch
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
    public async Task GetByNameAsync_ReturnsMediaServer()
    {
        // Arrange
        var entity = new MediaServerEntity
        {
            Name = "UniqueServerName",
            Provider = "jellyfin",
            Enabled = true,
            Status = MediaServerStatus.Healthy,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.MediaServers.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByNameAsync("UniqueServerName");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<MediaServer?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(MediaServer.Name))
                .EqualTo("UniqueServerName")
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(MediaServer.Provider))
                .EqualTo("jellyfin")
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(MediaServer.Status))
                .EqualTo(MediaServerStatus.Healthy));
    }

    [Test]
    public async Task GetByNameAsync_ReturnsNull_WhenNotFound()
    {
        // Act
        var result = await _subject.GetByNameAsync("NonExistentServer");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<MediaServer?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task GetByNameAsync_IncludesSettings()
    {
        // Arrange
        var entity = new MediaServerEntity
        {
            Name = "ServerWithSettings",
            Provider = "plex",
            Enabled = true,
            Status = MediaServerStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Settings.Add(new() { Key = "host", Value = "localhost" });
        _context.MediaServers.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByNameAsync("ServerWithSettings");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<MediaServer?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(MediaServer.Settings)).With.Count.EqualTo(1));
    }

    [Test]
    public async Task GetByProviderAsync_ReturnsMediaServers()
    {
        // Arrange
        var entity1 = new MediaServerEntity
        {
            Name = "PlexServer1",
            Provider = "plex",
            Enabled = true,
            Status = MediaServerStatus.Healthy,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new MediaServerEntity
        {
            Name = "PlexServer2",
            Provider = "plex",
            Enabled = false,
            Status = MediaServerStatus.Unhealthy,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity3 = new MediaServerEntity
        {
            Name = "JellyfinServer",
            Provider = "jellyfin",
            Enabled = true,
            Status = MediaServerStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.MediaServers.AddRange(entity1, entity2, entity3);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByProviderAsync("plex");

        // Assert
        var success = result as SuccessResult<IReadOnlyList<MediaServer>>;
        Assert.That(success, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(success!.Value, Has.Count.EqualTo(2));
            Assert.That(success.Value, Has.All
                .With.Property(nameof(MediaServer.Provider)).EqualTo("plex"));
        }
    }

    [Test]
    public async Task GetByProviderAsync_ReturnsEmptyList_WhenNoMatches()
    {
        // Arrange
        var entity = new MediaServerEntity
        {
            Name = "PlexServer",
            Provider = "plex",
            Enabled = true,
            Status = MediaServerStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.MediaServers.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByProviderAsync("emby");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<MediaServer>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task GetByProviderAsync_IncludesSettings()
    {
        // Arrange
        var entity = new MediaServerEntity
        {
            Name = "PlexServer",
            Provider = "plex",
            Enabled = true,
            Status = MediaServerStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Settings.Add(new() { Key = "token", Value = "abc" });
        _context.MediaServers.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByProviderAsync("plex");

        // Assert
        var success = result as SuccessResult<IReadOnlyList<MediaServer>>;
        Assert.That(success, Is.Not.Null);
        Assert.That(success!.Value[0].Settings, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task GetByStatusAsync_ReturnsMediaServers()
    {
        // Arrange
        var entity1 = new MediaServerEntity
        {
            Name = "HealthyServer1",
            Provider = "plex",
            Enabled = true,
            Status = MediaServerStatus.Healthy,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new MediaServerEntity
        {
            Name = "HealthyServer2",
            Provider = "jellyfin",
            Enabled = true,
            Status = MediaServerStatus.Healthy,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity3 = new MediaServerEntity
        {
            Name = "UnhealthyServer",
            Provider = "plex",
            Enabled = true,
            Status = MediaServerStatus.Unhealthy,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.MediaServers.AddRange(entity1, entity2, entity3);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByStatusAsync(MediaServerStatus.Healthy);

        // Assert
        var success = result as SuccessResult<IReadOnlyList<MediaServer>>;
        Assert.That(success, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(success!.Value, Has.Count.EqualTo(2));
            Assert.That(success.Value, Has.All
                .With.Property(nameof(MediaServer.Status)).EqualTo(MediaServerStatus.Healthy));
        }
    }

    [Test]
    public async Task GetByStatusAsync_ReturnsEmptyList_WhenNoMatches()
    {
        // Arrange
        var entity = new MediaServerEntity
        {
            Name = "HealthyServer",
            Provider = "plex",
            Enabled = true,
            Status = MediaServerStatus.Healthy,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.MediaServers.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByStatusAsync(MediaServerStatus.Stale);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<MediaServer>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task GetByStatusAsync_IncludesSettings()
    {
        // Arrange
        var entity = new MediaServerEntity
        {
            Name = "HealthyServer",
            Provider = "plex",
            Enabled = true,
            Status = MediaServerStatus.Healthy,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Settings.Add(new() { Key = "host", Value = "localhost" });
        _context.MediaServers.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByStatusAsync(MediaServerStatus.Healthy);

        // Assert
        var success = result as SuccessResult<IReadOnlyList<MediaServer>>;
        Assert.That(success, Is.Not.Null);
        Assert.That(success!.Value[0].Settings, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task GetEnabledAsync_ReturnsEnabledMediaServers()
    {
        // Arrange
        var entity1 = new MediaServerEntity
        {
            Name = "EnabledServer1",
            Provider = "plex",
            Enabled = true,
            Status = MediaServerStatus.Healthy,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new MediaServerEntity
        {
            Name = "EnabledServer2",
            Provider = "jellyfin",
            Enabled = true,
            Status = MediaServerStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity3 = new MediaServerEntity
        {
            Name = "DisabledServer",
            Provider = "plex",
            Enabled = false,
            Status = MediaServerStatus.Unhealthy,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.MediaServers.AddRange(entity1, entity2, entity3);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetEnabledAsync();

        // Assert
        var success = result as SuccessResult<IReadOnlyList<MediaServer>>;
        Assert.That(success, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(success!.Value, Has.Count.EqualTo(2));
            Assert.That(success.Value, Has.All
                .With.Property(nameof(MediaServer.Enabled)).True);
        }
    }

    [Test]
    public async Task GetEnabledAsync_ReturnsEmptyList_WhenNoEnabledServers()
    {
        // Arrange
        var entity = new MediaServerEntity
        {
            Name = "DisabledServer",
            Provider = "plex",
            Enabled = false,
            Status = MediaServerStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.MediaServers.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetEnabledAsync();

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<MediaServer>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task GetEnabledAsync_IncludesSettings()
    {
        // Arrange
        var entity = new MediaServerEntity
        {
            Name = "EnabledServer",
            Provider = "plex",
            Enabled = true,
            Status = MediaServerStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Settings.Add(new() { Key = "host", Value = "localhost" });
        _context.MediaServers.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetEnabledAsync();

        // Assert
        var success = result as SuccessResult<IReadOnlyList<MediaServer>>;
        Assert.That(success, Is.Not.Null);
        Assert.That(success!.Value[0].Settings, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task UpdateStatusAsync_UpdatesStatusAndLastChecked()
    {
        // Arrange
        var entity = new MediaServerEntity
        {
            Name = "TestServer",
            Provider = "plex",
            Enabled = true,
            Status = MediaServerStatus.Unknown,
            LastChecked = null,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.MediaServers.Add(entity);
        _context.SaveChanges();

        var newLastChecked = DateTimeOffset.UtcNow;

        // Act
        var result = await _subject.UpdateStatusAsync(entity.Id, MediaServerStatus.Healthy, newLastChecked);

        // Assert
        var updatedEntity = await _context.MediaServers
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<MediaServerEntity>()
                .With.Property(nameof(MediaServerEntity.Status)).EqualTo(MediaServerStatus.Healthy)
                .And.Property(nameof(MediaServerEntity.LastChecked)).EqualTo(newLastChecked));
        }
    }

    [Test]
    public async Task UpdateStatusAsync_ReturnsFalse_WhenNotFound()
    {
        // Act
        var result = await _subject.UpdateStatusAsync(Guid.NewGuid(), MediaServerStatus.Healthy, DateTimeOffset.UtcNow);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).False);
    }

    [Test]
    public async Task UpdateStatusAsync_DoesNotCreateHistoryEntry()
    {
        // Arrange
        var entity = new MediaServerEntity
        {
            Name = "TestServer",
            Provider = "plex",
            Enabled = true,
            Status = MediaServerStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.MediaServers.Add(entity);
        _context.SaveChanges();

        // Act
        await _subject.UpdateStatusAsync(entity.Id, MediaServerStatus.Healthy, DateTimeOffset.UtcNow);

        // Assert
        var historyEntries = await _context.HistoryEntries.ToListAsync();
        Assert.That(historyEntries, Is.Empty);
    }
}
