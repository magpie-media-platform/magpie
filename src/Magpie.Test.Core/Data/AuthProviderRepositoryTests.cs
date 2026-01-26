using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions.Data;
using Magpie.Core.Data;
using Magpie.Data.Database;
using Magpie.Data.Database.Entities;
using Magpie.Models.Result;
using Magpie.Models.Users;
using Magpie.Test.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NUnit.Framework;

using AuthProvider = Magpie.Models.Users.AuthProvider;
using AuthProviderEntity = Magpie.Data.Database.Entities.AuthProvider;
using HistoryEntryEntity = Magpie.Data.Database.Entities.HistoryEntry;
using UserEntity = Magpie.Data.Database.Entities.User;

namespace Magpie.Test.Core.Data;

[TestFixture]
public class AuthProviderRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IAuthProviderRepository _subject = null!;
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

        _subject = new AuthProviderRepository(
            _context,
            historyRepo,
            loggerFactory.CreateLogger<AuthProviderRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context?.Dispose();
    }

    [Test]
    public async Task GetByIdAsync_ReturnsAuthProvider()
    {
        // Arrange
        var entity = new AuthProviderEntity
        {
            Name = "TestProvider",
            Provider = "oidc",
            Enabled = true,
            IsDefault = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.AuthProviders.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByIdAsync(entity.Id);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<AuthProvider?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(AuthProvider.Name)).EqualTo("TestProvider")
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(AuthProvider.Provider)).EqualTo("oidc")
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(AuthProvider.Enabled)).True
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(AuthProvider.IsDefault)).False);
    }

    [Test]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        // Act
        var result = await _subject.GetByIdAsync(Guid.NewGuid());

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<AuthProvider?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task GetByIdAsync_IncludesSettings()
    {
        // Arrange
        var entity = new AuthProviderEntity
        {
            Name = "ProviderWithSettings",
            Provider = "oidc",
            Enabled = true,
            IsDefault = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Settings.Add(new() { Key = "clientId", Value = "my-client" });
        entity.Settings.Add(new() { Key = "clientSecret", Value = "secret" });
        _context.AuthProviders.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByIdAsync(entity.Id);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<AuthProvider?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(AuthProvider.Settings))
            .With.Count.EqualTo(2));
    }

    [Test]
    public async Task GetAllAsync_ReturnsAllAuthProviders()
    {
        // Arrange
        var entity1 = new AuthProviderEntity
        {
            Name = "Provider1",
            Provider = "oidc",
            Enabled = true,
            IsDefault = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new AuthProviderEntity
        {
            Name = "Provider2",
            Provider = "ldap",
            Enabled = false,
            IsDefault = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.AuthProviders.AddRange(entity1, entity2);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetAllAsync();

        // Assert (2 added + 2 seeded = 4)
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<AuthProvider>>>()
            .With.Property(nameof(SuccessResult<>.Value)).With.Count.EqualTo(4));
    }

    [Test]
    public async Task GetAllAsync_ReturnsSeededProviders_WhenNoAdditionalProviders()
    {
        // Act
        var result = await _subject.GetAllAsync();

        // Assert (2 seeded providers)
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<AuthProvider>>>()
            .With.Property(nameof(SuccessResult<>.Value)).With.Count.EqualTo(2));
    }

    [Test]
    public async Task GetAllAsync_IncludesSettings()
    {
        // Arrange
        var entity = new AuthProviderEntity
        {
            Name = "ProviderWithSettings",
            Provider = "oidc",
            Enabled = true,
            IsDefault = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Settings.Add(new() { Key = "setting1", Value = "value1" });
        _context.AuthProviders.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetAllAsync();

        // Assert
        var success = result as SuccessResult<IReadOnlyList<AuthProvider>>;
        Assert.That(success, Is.Not.Null);
        var providerWithSettings = success!.Value.FirstOrDefault(p => p.Name == "ProviderWithSettings");
        Assert.That(providerWithSettings, Is.Not.Null);
        Assert.That(providerWithSettings!.Settings, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task CreateAsync_CreatesAuthProvider()
    {
        // Arrange
        var model = new AuthProvider(
            Guid.Empty,
            "NewProvider",
            "oidc",
            true,
            false,
            ImmutableDictionary<string, string?>.Empty,
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.CreateAsync(model, _userId);

        // Assert
        var entity = await (
            from p in _context.AuthProviders.AsNoTracking()
            where p.Name == "NewProvider"
            select p).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<AuthProvider>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(AuthProvider.Name)).EqualTo("NewProvider")
                .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(AuthProvider.Provider)).EqualTo("oidc")
                .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(AuthProvider.Enabled)).True
                .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(AuthProvider.IsDefault)).False);
            Assert.That(entity, Is.InstanceOf<AuthProviderEntity>()
                .With.Property(nameof(AuthProviderEntity.Name)).EqualTo("NewProvider")
                .And.Property(nameof(AuthProviderEntity.Provider)).EqualTo("oidc")
                .And.Property(nameof(AuthProviderEntity.CreatedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Create")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("AuthProvider")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesAuthProviderWithSettings()
    {
        // Arrange
        var settings = new Dictionary<string, string?>
        {
            ["clientId"] = "my-client-id",
            ["clientSecret"] = "my-secret",
            ["authority"] = "https://auth.example.com"
        }.ToImmutableDictionary();

        var model = new AuthProvider(
            Guid.Empty,
            "OidcProvider",
            "oidc",
            true,
            true,
            settings,
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.CreateAsync(model, _userId);

        // Assert
        var entity = await (
            from p in _context.AuthProviders.AsNoTracking().Include(p => p.Settings)
            where p.Name == "OidcProvider"
            select p).SingleOrDefaultAsync();
        var historyEntities = await _context.HistoryEntries.ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<AuthProvider>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(AuthProvider.Settings)).Count.EqualTo(3));
            Assert.That(entity, Is.Not.Null);
            Assert.That(entity!.Settings, Has.Count.EqualTo(3));
            Assert.That(entity.Settings, Has.One
                .With.Property(nameof(AuthProviderSetting.Key)).EqualTo("clientId")
                .And.Property(nameof(AuthProviderSetting.Value)).EqualTo("my-client-id"));
            Assert.That(entity.Settings, Has.One
                .With.Property(nameof(AuthProviderSetting.Key)).EqualTo("clientSecret")
                .And.Property(nameof(AuthProviderSetting.Value)).EqualTo("my-secret"));
            Assert.That(entity.Settings, Has.One
                .With.Property(nameof(AuthProviderSetting.Key)).EqualTo("authority")
                .And.Property(nameof(AuthProviderSetting.Value)).EqualTo("https://auth.example.com"));
            Assert.That(historyEntities, Has.Count.EqualTo(4));
            Assert.That(historyEntities, Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Create")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("AuthProvider"));
            Assert.That(historyEntities, Has.Exactly(3)
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Create")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo(nameof(AuthProviderSetting)));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesAuthProvider()
    {
        // Arrange
        var entity = new AuthProviderEntity
        {
            Name = "OriginalName",
            Provider = "oidc",
            Enabled = true,
            IsDefault = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.AuthProviders.Add(entity);
        _context.SaveChanges();

        var model = new AuthProvider(
            entity.Id,
            "UpdatedName",
            "ldap",
            false,
            true,
            ImmutableDictionary<string, string?>.Empty,
            entity.CreatedAt,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        var updatedEntity = await (
            from p in _context.AuthProviders.AsNoTracking()
            where p.Id == entity.Id
            select p).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<AuthProviderEntity>()
                .With.Property(nameof(AuthProviderEntity.Name)).EqualTo("UpdatedName")
                .And.Property(nameof(AuthProviderEntity.Provider)).EqualTo("ldap")
                .And.Property(nameof(AuthProviderEntity.Enabled)).False
                .And.Property(nameof(AuthProviderEntity.IsDefault)).True
                .And.Property(nameof(AuthProviderEntity.ModifiedAt)).Not.Null
                .And.Property(nameof(AuthProviderEntity.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Update")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("AuthProvider")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateAsync_ReturnsFalse_WhenNotFound()
    {
        // Arrange
        var model = new AuthProvider(
            Guid.NewGuid(),
            "NonExistent",
            "oidc",
            true,
            false,
            ImmutableDictionary<string, string?>.Empty,
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
        var entity = new AuthProviderEntity
        {
            Name = "ProviderToUpdate",
            Provider = "oidc",
            Enabled = true,
            IsDefault = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.AuthProviders.Add(entity);
        _context.SaveChanges();

        var newSettings = new Dictionary<string, string?>
        {
            ["newSetting1"] = "value1",
            ["newSetting2"] = "value2"
        }.ToImmutableDictionary();

        var model = new AuthProvider(
            entity.Id,
            entity.Name,
            entity.Provider,
            entity.Enabled,
            entity.IsDefault,
            newSettings,
            entity.CreatedAt,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        var settingEntities = await _context.AuthProviderSettings
            .AsNoTracking()
            .Where(s => s.AuthProviderId == entity.Id)
            .ToListAsync();
        var historyEntities = await _context.HistoryEntries.ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(settingEntities, Has.Count.EqualTo(2));
            Assert.That(settingEntities, Has.One
                .With.Property(nameof(AuthProviderSetting.Key)).EqualTo("newSetting1")
                .And.Property(nameof(AuthProviderSetting.Value)).EqualTo("value1"));
            Assert.That(settingEntities, Has.One
                .With.Property(nameof(AuthProviderSetting.Key)).EqualTo("newSetting2")
                .And.Property(nameof(AuthProviderSetting.Value)).EqualTo("value2"));
            Assert.That(historyEntities, Has.Count.EqualTo(3));
            Assert.That(historyEntities, Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Update")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("AuthProvider"));
            Assert.That(historyEntities, Has.Exactly(2)
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Create")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo(nameof(AuthProviderSetting)));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesExistingSettings()
    {
        // Arrange
        var entity = new AuthProviderEntity
        {
            Name = "ProviderWithSettings",
            Provider = "oidc",
            Enabled = true,
            IsDefault = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Settings.Add(new() { Key = "existingKey", Value = "oldValue" });
        _context.AuthProviders.Add(entity);
        _context.SaveChanges();

        var updatedSettings = new Dictionary<string, string?>
        {
            ["existingKey"] = "newValue"
        }.ToImmutableDictionary();

        var model = new AuthProvider(
            entity.Id,
            entity.Name,
            entity.Provider,
            entity.Enabled,
            entity.IsDefault,
            updatedSettings,
            entity.CreatedAt,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        var settingEntity = await _context.AuthProviderSettings
            .AsNoTracking()
            .Where(s => s.AuthProviderId == entity.Id)
            .Where(s => s.Key == "existingKey")
            .SingleOrDefaultAsync();
        var historyEntities = await _context.HistoryEntries.ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(settingEntity, Is.InstanceOf<AuthProviderSetting>()
                .With.Property(nameof(AuthProviderSetting.Value)).EqualTo("newValue"));
            Assert.That(historyEntities, Has.Count.EqualTo(2));
            Assert.That(historyEntities, Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Update")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("AuthProvider"));
            Assert.That(historyEntities, Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Update")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo(nameof(AuthProviderSetting)));
        }
    }

    [Test]
    public async Task UpdateAsync_RemovesSettings()
    {
        // Arrange
        var entity = new AuthProviderEntity
        {
            Name = "ProviderWithSettings",
            Provider = "oidc",
            Enabled = true,
            IsDefault = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Settings.Add(new() { Key = "keepMe", Value = "keep" });
        entity.Settings.Add(new() { Key = "removeMe", Value = "remove" });
        _context.AuthProviders.Add(entity);
        _context.SaveChanges();

        var updatedSettings = new Dictionary<string, string?>
        {
            ["keepMe"] = "keep"
        }.ToImmutableDictionary();

        var model = new AuthProvider(
            entity.Id,
            entity.Name,
            entity.Provider,
            entity.Enabled,
            entity.IsDefault,
            updatedSettings,
            entity.CreatedAt,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        var settingEntities = await _context.AuthProviderSettings
            .AsNoTracking()
            .Where(s => s.AuthProviderId == entity.Id)
            .ToListAsync();
        var historyEntities = await _context.HistoryEntries.ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(settingEntities, Has.Count.EqualTo(1));
            Assert.That(settingEntities, Has.One
                .With.Property(nameof(AuthProviderSetting.Key)).EqualTo("keepMe"));
            Assert.That(settingEntities, Has.None
                .With.Property(nameof(AuthProviderSetting.Key)).EqualTo("removeMe"));
            Assert.That(historyEntities, Has.Count.EqualTo(2));
            Assert.That(historyEntities, Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Update")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo(nameof(AuthProvider)));
            Assert.That(historyEntities, Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Delete")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo(nameof(AuthProviderSetting)));
        }
    }

    [Test]
    public async Task UpdateAsync_AddsUpdatesAndRemovesSettings()
    {
        // Arrange
        var entity = new AuthProviderEntity
        {
            Name = "ComplexProvider",
            Provider = "oidc",
            Enabled = true,
            IsDefault = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Settings.Add(new() { Key = "toUpdate", Value = "oldValue" });
        entity.Settings.Add(new() { Key = "toRemove", Value = "removeMe" });
        _context.AuthProviders.Add(entity);
        _context.SaveChanges();

        var updatedSettings = new Dictionary<string, string?>
        {
            ["toUpdate"] = "newValue",
            ["toAdd"] = "addedValue"
        }.ToImmutableDictionary();

        var model = new AuthProvider(
            entity.Id,
            entity.Name,
            entity.Provider,
            entity.Enabled,
            entity.IsDefault,
            updatedSettings,
            entity.CreatedAt,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        var settingEntities = await _context.AuthProviderSettings
            .AsNoTracking()
            .Where(s => s.AuthProviderId == entity.Id)
            .ToListAsync();
        var historyEntities = await _context.HistoryEntries.ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(settingEntities, Has.Count.EqualTo(2));
            Assert.That(settingEntities, Has.One
                .With.Property(nameof(AuthProviderSetting.Key)).EqualTo("toUpdate")
                .And.Property(nameof(AuthProviderSetting.Value)).EqualTo("newValue"));
            Assert.That(settingEntities, Has.One
                .With.Property(nameof(AuthProviderSetting.Key)).EqualTo("toAdd")
                .And.Property(nameof(AuthProviderSetting.Value)).EqualTo("addedValue"));
            Assert.That(settingEntities, Has.None
                .With.Property(nameof(AuthProviderSetting.Key)).EqualTo("toRemove"));
            Assert.That(historyEntities, Has.Count.EqualTo(4));
            Assert.That(historyEntities, Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Update")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("AuthProvider"));
            Assert.That(historyEntities, Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Delete")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo(nameof(AuthProviderSetting)));
            Assert.That(historyEntities, Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Update")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo(nameof(AuthProviderSetting)));
            Assert.That(historyEntities, Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Create")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo(nameof(AuthProviderSetting)));
        }
    }

    [Test]
    public async Task DeleteAsync_DeletesAuthProvider()
    {
        // Arrange
        var entity = new AuthProviderEntity
        {
            Name = "ToDelete",
            Provider = "oidc",
            Enabled = true,
            IsDefault = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.AuthProviders.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.DeleteAsync(entity.Id, _userId);

        // Assert
        var entityExists = await _context.AuthProviders.AnyAsync(p => p.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(entityExists, Is.False);
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Delete")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("AuthProvider")
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
        var entity = new AuthProviderEntity
        {
            Name = "ProviderWithSettings",
            Provider = "oidc",
            Enabled = true,
            IsDefault = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Settings.Add(new() { Key = "setting1", Value = "value1" });
        entity.Settings.Add(new() { Key = "setting2", Value = "value2" });
        _context.AuthProviders.Add(entity);
        _context.SaveChanges();

        var providerId = entity.Id;

        // Act
        var result = await _subject.DeleteAsync(providerId, _userId);

        // Assert
        var settingsExist = await _context.AuthProviderSettings
            .AnyAsync(s => s.AuthProviderId == providerId);
        var historyEntities = await _context.HistoryEntries.ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(settingsExist, Is.False);
            Assert.That(historyEntities, Has.Count.EqualTo(3));
            Assert.That(historyEntities, Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Delete")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("AuthProvider"));
            Assert.That(historyEntities, Has.Exactly(2)
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Delete")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo(nameof(AuthProviderSetting)));
        }
    }

    [Test]
    public async Task GetByNameAsync_ReturnsAuthProvider()
    {
        // Arrange
        var entity = new AuthProviderEntity
        {
            Name = "UniqueProviderName",
            Provider = "ldap",
            Enabled = true,
            IsDefault = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.AuthProviders.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByNameAsync("UniqueProviderName");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<AuthProvider?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(AuthProvider.Name))
                .EqualTo("UniqueProviderName")
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(AuthProvider.Provider))
                .EqualTo("ldap")
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(AuthProvider.IsDefault))
                .True);
    }

    [Test]
    public async Task GetByNameAsync_ReturnsNull_WhenNotFound()
    {
        // Act
        var result = await _subject.GetByNameAsync("NonExistentProvider");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<AuthProvider?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task GetByNameAsync_IncludesSettings()
    {
        // Arrange
        var entity = new AuthProviderEntity
        {
            Name = "ProviderWithSettings",
            Provider = "oidc",
            Enabled = true,
            IsDefault = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Settings.Add(new() { Key = "authority", Value = "https://auth.example.com" });
        _context.AuthProviders.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByNameAsync("ProviderWithSettings");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<AuthProvider?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(AuthProvider.Settings)).Count.EqualTo(1));
    }

    [Test]
    public async Task GetByProviderTypeAsync_ReturnsEnabledProviders()
    {
        // Arrange
        var enabledEntity = new AuthProviderEntity
        {
            Name = "EnabledLdapProvider",
            Provider = "ldap",
            Enabled = true,
            IsDefault = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var disabledEntity = new AuthProviderEntity
        {
            Name = "DisabledLdapProvider",
            Provider = "ldap",
            Enabled = false,
            IsDefault = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var differentTypeEntity = new AuthProviderEntity
        {
            Name = "OidcProvider",
            Provider = "oidc",
            Enabled = true,
            IsDefault = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.AuthProviders.AddRange(enabledEntity, disabledEntity, differentTypeEntity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByProviderTypeAsync("ldap");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<AuthProvider>>>()
                .With.Property(nameof(SuccessResult<>.Value)).With.Count.EqualTo(1));
            var success = result as SuccessResult<IReadOnlyList<AuthProvider>>;
            Assert.That(success!.Value, Has.One
                .With.Property(nameof(AuthProvider.Name)).EqualTo("EnabledLdapProvider"));
        }
    }

    [Test]
    public async Task GetByProviderTypeAsync_ReturnsEmptyList_WhenNoMatches()
    {
        // Arrange
        var entity = new AuthProviderEntity
        {
            Name = "OidcProvider",
            Provider = "oidc",
            Enabled = true,
            IsDefault = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.AuthProviders.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByProviderTypeAsync("ldap");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<AuthProvider>>>()
            .With.Property(nameof(SuccessResult<>.Value)).With.Count.EqualTo(0));
    }

    [Test]
    public async Task GetByProviderTypeAsync_ReturnsMultipleEnabledProviders()
    {
        // Arrange
        var entity1 = new AuthProviderEntity
        {
            Name = "LdapProvider1",
            Provider = "ldap",
            Enabled = true,
            IsDefault = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new AuthProviderEntity
        {
            Name = "LdapProvider2",
            Provider = "ldap",
            Enabled = true,
            IsDefault = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.AuthProviders.AddRange(entity1, entity2);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByProviderTypeAsync("ldap");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<AuthProvider>>>()
            .With.Property(nameof(SuccessResult<>.Value)).With.Count.EqualTo(2));
    }

    [Test]
    public async Task GetByProviderTypeAsync_IncludesSettings()
    {
        // Arrange
        var entity = new AuthProviderEntity
        {
            Name = "LdapWithSettings",
            Provider = "ldap",
            Enabled = true,
            IsDefault = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Settings.Add(new() { Key = "server", Value = "ldap.example.com" });
        entity.Settings.Add(new() { Key = "port", Value = "389" });
        _context.AuthProviders.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByProviderTypeAsync("ldap");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<AuthProvider>>>()
            .With.Property(nameof(SuccessResult<>.Value)).With.One
                .With.Property(nameof(AuthProvider.Settings)).Count.EqualTo(2));
    }
}
