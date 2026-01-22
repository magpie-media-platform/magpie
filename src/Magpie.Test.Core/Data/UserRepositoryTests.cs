using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions.Data;
using Magpie.Core.Data;
using Magpie.Data.Database;
using Magpie.Models;
using Magpie.Models.Result;
using Magpie.Models.Users;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NUnit.Framework;

using HistoryEntryEntity = Magpie.Data.Database.Entities.HistoryEntry;
using UserEntity = Magpie.Data.Database.Entities.User;

namespace Magpie.Test.Core.Data;

[TestFixture]
public class UserRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IUserRepository _subject = null!;
    private Guid _adminUserId;

    [SetUp]
    public void SetUp()
    {
        _context = new MagpieDbContext(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options);

        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();

        var adminUser = new UserEntity
        {
            Username = "AdminUser",
            Enabled = true,
            Role = Role.Admin,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _context.Users.Add(adminUser);
        _context.SaveChanges();

        _adminUserId = adminUser.Id;

        var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
        var historyRepo = new HistoryRepository(_context, loggerFactory.CreateLogger<HistoryRepository>());

        _subject = new UserRepository(_context, historyRepo, loggerFactory.CreateLogger<UserRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context?.Dispose();
    }

    [Test]
    public async Task GetByIdAsync_ReturnsUser()
    {
        // Arrange
        var userEntity = new UserEntity
        {
            Username = "TestUser",
            Email = "test@example.com",
            Enabled = true,
            Role = Role.Viewer,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Users.Add(userEntity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByIdAsync(userEntity.Id);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<User?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(User.Username)).EqualTo("TestUser")
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(User.Email)).EqualTo("test@example.com")
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(User.Role)).EqualTo(Role.Viewer));
    }

    [Test]
    public async Task GetByIdAsync_ReturnsNull_WhenUserDoesNotExist()
    {
        // Act
        var result = await _subject.GetByIdAsync(Guid.NewGuid());

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<User?>>().With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task GetAllAsync_ReturnsAllUsers()
    {
        // Arrange
        var userEntity1 = new UserEntity
        {
            Username = "User1",
            Enabled = true,
            Role = Role.Viewer,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var userEntity2 = new UserEntity
        {
            Username = "User2",
            Enabled = true,
            Role = Role.Manager,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Users.AddRange(userEntity1, userEntity2);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetAllAsync();

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<User>>>()
            .With.Property(nameof(SuccessResult<>.Value)).With.Count.EqualTo(3));
    }

    [Test]
    public async Task GetAllAsync_ReturnsOnlyAdminUser_WhenNoOtherUsers()
    {
        // Act
        var result = await _subject.GetAllAsync();

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<User>>>()
            .With.Property(nameof(SuccessResult<>.Value)).With.Count.EqualTo(1));
    }

    [Test]
    public async Task CreateAsync_CreatesUser()
    {
        // Arrange
        var model = new User(
            Guid.Empty,
            "NewUser",
            "newuser@example.com",
            null,
            null,
            true,
            Role.Requester,
            null,
            null,
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.CreateAsync(model, _adminUserId);

        // Assert
        var entity = await (
            from u in _context.Users.AsNoTracking()
            where u.Username == "NewUser"
            select u).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<User>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(User.Username)).EqualTo("NewUser")
                .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(User.Email)).EqualTo("newuser@example.com")
                .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(User.Role)).EqualTo(Role.Requester));
            Assert.That(entity, Is.InstanceOf<UserEntity>()
                .With.Property(nameof(UserEntity.Username)).EqualTo("NewUser")
                .And.Property(nameof(UserEntity.Email)).EqualTo("newuser@example.com")
                .And.Property(nameof(UserEntity.CreatedByUserId)).EqualTo(_adminUserId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Create")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("User")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_adminUserId));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesUserWithProviderData()
    {
        // Arrange
        var providerData = new Dictionary<string, string?>
        {
            ["key1"] = "value1",
            ["key2"] = "value2"
        }.ToImmutableDictionary();

        var model = new User(
            Guid.Empty,
            "UserWithData",
            null,
            null,
            null,
            true,
            Role.Viewer,
            providerData,
            null,
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.CreateAsync(model, _adminUserId);

        // Assert
        var entity = await (
            from u in _context.Users.AsNoTracking().Include(u => u.ProviderData)
            where u.Username == "UserWithData"
            select u).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<User>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(User.ProviderData)).Count.EqualTo(2));
            Assert.That(entity, Is.Not.Null);
            Assert.That(entity!.ProviderData, Has.Count.EqualTo(2));
            Assert.That(entity.ProviderData, Has.One
                .With.Property("Key").EqualTo("key1")
                .And.Property(nameof(SuccessResult<>.Value)).EqualTo("value1"));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesUser()
    {
        // Arrange
        var userEntity = new UserEntity
        {
            Username = "OriginalUser",
            Email = "original@example.com",
            Enabled = true,
            Role = Role.Viewer,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Users.Add(userEntity);
        _context.SaveChanges();

        var model = new User(
            userEntity.Id,
            "UpdatedUser",
            "updated@example.com",
            null,
            null,
            false,
            Role.Manager,
            null,
            null,
            userEntity.CreatedAt,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _adminUserId);

        // Assert
        var entity = await (
            from u in _context.Users.AsNoTracking()
            where u.Id == userEntity.Id
            select u).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(entity, Is.InstanceOf<UserEntity>()
                .With.Property(nameof(UserEntity.Username)).EqualTo("UpdatedUser")
                .And.Property(nameof(UserEntity.Email)).EqualTo("updated@example.com")
                .And.Property(nameof(UserEntity.Enabled)).False
                .And.Property(nameof(UserEntity.Role)).EqualTo(Role.Manager)
                .And.Property(nameof(UserEntity.ModifiedAt)).Not.Null
                .And.Property(nameof(UserEntity.ModifiedByUserId)).EqualTo(_adminUserId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Update")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("User")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_adminUserId));
        }
    }

    [Test]
    public async Task UpdateAsync_ReturnsFalse_WhenUserDoesNotExist()
    {
        // Arrange
        var model = new User(
            Guid.NewGuid(),
            "NonExistent",
            null,
            null,
            null,
            true,
            Role.Viewer,
            null,
            null,
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _adminUserId);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).False);
    }

    [Test]
    public async Task UpdateAsync_UpdatesProviderData()
    {
        // Arrange
        var userEntity = new UserEntity
        {
            Username = "UserWithData",
            Enabled = true,
            Role = Role.Viewer,
            CreatedAt = DateTimeOffset.UtcNow
        };
        userEntity.ProviderData.Add(new()
        {
            Key = "existingKey",
            Value = "oldValue"
        });
        userEntity.ProviderData.Add(new()
        {
            Key = "toRemove",
            Value = "removeMe"
        });
        _context.Users.Add(userEntity);
        _context.SaveChanges();

        var newProviderData = new Dictionary<string, string?>
        {
            ["existingKey"] = "newValue",
            ["newKey"] = "brandNew"
        }.ToImmutableDictionary();

        var model = new User(
            userEntity.Id,
            "UserWithData",
            null,
            null,
            null,
            true,
            Role.Viewer,
            newProviderData,
            null,
            userEntity.CreatedAt,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _adminUserId);

        // Assert
        var providerDataEntities = await _context.UserProviderData
            .AsNoTracking()
            .Where(pd => pd.UserId == userEntity.Id)
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(providerDataEntities, Has.Count.EqualTo(2));
            Assert.That(providerDataEntities, Has.One
                .With.Property("Key").EqualTo("existingKey")
                .And.Property(nameof(SuccessResult<>.Value)).EqualTo("newValue"));
            Assert.That(providerDataEntities, Has.One
                .With.Property("Key").EqualTo("newKey")
                .And.Property(nameof(SuccessResult<>.Value)).EqualTo("brandNew"));
            Assert.That(providerDataEntities, Has.None
                .With.Property("Key").EqualTo("toRemove"));
        }
    }

    [Test]
    public async Task DeleteAsync_DeletesUser()
    {
        // Arrange
        var userEntity = new UserEntity
        {
            Username = "ToDelete",
            Enabled = true,
            Role = Role.Viewer,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Users.Add(userEntity);
        _context.SaveChanges();

        // Act
        var result = await _subject.DeleteAsync(userEntity.Id, _adminUserId);

        // Assert
        var entityExists = await _context.Users.AnyAsync(u => u.Id == userEntity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(entityExists, Is.False);
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Delete")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("User")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_adminUserId));
        }
    }

    [Test]
    public async Task DeleteAsync_ReturnsFalse_WhenUserDoesNotExist()
    {
        // Act
        var result = await _subject.DeleteAsync(Guid.NewGuid(), _adminUserId);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).False);
    }

    [Test]
    public async Task DeleteAsync_DeletesProviderData()
    {
        // Arrange
        var userEntity = new UserEntity
        {
            Username = "UserWithData",
            Enabled = true,
            Role = Role.Viewer,
            CreatedAt = DateTimeOffset.UtcNow
        };
        userEntity.ProviderData.Add(new()
        {
            Key = "key1",
            Value = "value1"
        });
        userEntity.ProviderData.Add(new()
        {
            Key = "key2",
            Value = "value2"
        });
        _context.Users.Add(userEntity);
        _context.SaveChanges();

        // Act
        var result = await _subject.DeleteAsync(userEntity.Id, _adminUserId);

        // Assert
        var providerDataExists = await _context.UserProviderData
            .AnyAsync(pd => pd.UserId == userEntity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(providerDataExists, Is.False);
        }
    }

    [Test]
    public async Task PatchAsync_PatchesEnabledField()
    {
        // Arrange
        var userEntity = new UserEntity
        {
            Username = "ToPatch",
            Enabled = true,
            Role = Role.Viewer,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Users.Add(userEntity);
        _context.SaveChanges();

        var patch = new UserPatch
        {
            Id = userEntity.Id,
            Enabled = new FieldPatch<bool>(false)
        };

        // Act
        var result = await _subject.PatchAsync(patch, _adminUserId);

        // Assert
        var entity = await (
            from u in _context.Users.AsNoTracking()
            where u.Id == userEntity.Id
            select u).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(entity, Is.InstanceOf<UserEntity>()
                .With.Property(nameof(UserEntity.Enabled)).False
                .And.Property(nameof(UserEntity.ModifiedAt)).Not.Null
                .And.Property(nameof(UserEntity.ModifiedByUserId)).EqualTo(_adminUserId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Patch")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("User")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_adminUserId));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesRoleField()
    {
        // Arrange
        var userEntity = new UserEntity
        {
            Username = "ToPatch",
            Enabled = true,
            Role = Role.Viewer,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Users.Add(userEntity);
        _context.SaveChanges();

        var patch = new UserPatch
        {
            Id = userEntity.Id,
            Role = new FieldPatch<Role>(Role.Admin)
        };

        // Act
        var result = await _subject.PatchAsync(patch, _adminUserId);

        // Assert
        var entity = await (
            from u in _context.Users.AsNoTracking()
            where u.Id == userEntity.Id
            select u).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(entity, Is.InstanceOf<UserEntity>()
                .With.Property(nameof(UserEntity.Role)).EqualTo(Role.Admin));
        }
    }

    [Test]
    public async Task PatchAsync_ReturnsFalse_WhenUserDoesNotExist()
    {
        // Arrange
        var patch = new UserPatch
        {
            Id = Guid.NewGuid(),
            Enabled = new FieldPatch<bool>(false)
        };

        // Act
        var result = await _subject.PatchAsync(patch, _adminUserId);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).False);
    }

    [Test]
    public async Task GetByUsernameAsync_ReturnsUser()
    {
        // Arrange
        var userEntity = new UserEntity
        {
            Username = "UniqueUsername",
            Email = "unique@example.com",
            Enabled = true,
            Role = Role.Requester,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Users.Add(userEntity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByUsernameAsync("UniqueUsername");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<User?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(User.Username)).EqualTo("UniqueUsername")
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(User.Email)).EqualTo("unique@example.com")
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(User.Role)).EqualTo(Role.Requester));
    }

    [Test]
    public async Task GetByUsernameAsync_ReturnsNull_WhenUserDoesNotExist()
    {
        // Act
        var result = await _subject.GetByUsernameAsync("NonExistentUser");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<User?>>().With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task GetByExternalIdAsync_ReturnsUser()
    {
        // Arrange
        var authProviderId = Guid.NewGuid();
        var authProvider = new Magpie.Data.Database.Entities.AuthProvider
        {
            Id = authProviderId,
            Name = "TestProvider",
            Provider = "test",
            Enabled = true,
            IsDefault = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.AuthProviders.Add(authProvider);

        var userEntity = new UserEntity
        {
            Username = "ExternalUser",
            AuthProviderId = authProviderId,
            ExternalId = "external-123",
            Enabled = true,
            Role = Role.Viewer,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Users.Add(userEntity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByExternalIdAsync(authProviderId, "external-123");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<User?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(User.Username)).EqualTo("ExternalUser")
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(User.ExternalId)).EqualTo("external-123")
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(User.AuthProviderId)).EqualTo(authProviderId));
    }

    [Test]
    public async Task GetByExternalIdAsync_ReturnsNull_WhenUserDoesNotExist()
    {
        // Act
        var result = await _subject.GetByExternalIdAsync(Guid.NewGuid(), "nonexistent");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<User?>>().With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task UpdatePasswordAsync_UpdatesPassword()
    {
        // Arrange
        var userEntity = new UserEntity
        {
            Username = "PasswordUser",
            Enabled = true,
            Role = Role.Viewer,
            PasswordHash = "oldHash",
            PasswordSalt = "oldSalt",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Users.Add(userEntity);
        _context.SaveChanges();

        // Act
        var result = await _subject.UpdatePasswordAsync(userEntity.Id, "newHash", "newSalt", _adminUserId);

        // Assert
        var entity = await (
            from u in _context.Users.AsNoTracking()
            where u.Id == userEntity.Id
            select u).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult>());
            Assert.That(entity, Is.InstanceOf<UserEntity>()
                .With.Property(nameof(UserEntity.PasswordHash)).EqualTo("newHash")
                .And.Property(nameof(UserEntity.PasswordSalt)).EqualTo("newSalt")
                .And.Property(nameof(UserEntity.ModifiedAt)).Not.Null
                .And.Property(nameof(UserEntity.ModifiedByUserId)).EqualTo(_adminUserId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("UpdatePassword")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("User")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_adminUserId));
        }
    }

    [Test]
    public async Task RecordFailedLoginAsync_IncrementsFailedAttempts()
    {
        // Arrange
        var userEntity = new UserEntity
        {
            Username = "FailedLoginUser",
            Enabled = true,
            Role = Role.Viewer,
            FailedLoginAttempts = 0,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Users.Add(userEntity);
        _context.SaveChanges();

        // Act
        var result = await _subject.RecordFailedLoginAsync(userEntity.Id, null);

        // Assert
        var entity = await (
            from u in _context.Users.AsNoTracking()
            where u.Id == userEntity.Id
            select u).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult>());
            Assert.That(entity, Is.InstanceOf<UserEntity>()
                .With.Property(nameof(UserEntity.FailedLoginAttempts)).EqualTo(1)
                .And.Property(nameof(UserEntity.LockedUntil)).Null);
        }
    }

    [Test]
    public async Task RecordFailedLoginAsync_SetsLockout()
    {
        // Arrange
        var userEntity = new UserEntity
        {
            Username = "LockoutUser",
            Enabled = true,
            Role = Role.Viewer,
            FailedLoginAttempts = 4,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Users.Add(userEntity);
        _context.SaveChanges();

        var lockoutDuration = TimeSpan.FromMinutes(15);
        var beforeLockout = DateTimeOffset.UtcNow;

        // Act
        var result = await _subject.RecordFailedLoginAsync(userEntity.Id, lockoutDuration);

        // Assert
        var entity = await (
            from u in _context.Users.AsNoTracking()
            where u.Id == userEntity.Id
            select u).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult>());
            Assert.That(entity, Is.InstanceOf<UserEntity>()
                .With.Property(nameof(UserEntity.FailedLoginAttempts)).EqualTo(5));
            Assert.That(entity!.LockedUntil, Is.Not.Null
                .And.GreaterThanOrEqualTo(beforeLockout.Add(lockoutDuration)));
        }
    }

    [Test]
    public async Task RecordSuccessfulLoginAsync_ResetsFailedAttemptsAndLockout()
    {
        // Arrange
        var userEntity = new UserEntity
        {
            Username = "SuccessfulLoginUser",
            Enabled = true,
            Role = Role.Viewer,
            FailedLoginAttempts = 3,
            LockedUntil = DateTimeOffset.UtcNow.AddMinutes(10),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Users.Add(userEntity);
        _context.SaveChanges();

        var beforeLogin = DateTimeOffset.UtcNow;

        // Act
        var result = await _subject.RecordSuccessfulLoginAsync(userEntity.Id);

        // Assert
        var entity = await (
            from u in _context.Users.AsNoTracking()
            where u.Id == userEntity.Id
            select u).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult>());
            Assert.That(entity, Is.InstanceOf<UserEntity>()
                .With.Property(nameof(UserEntity.FailedLoginAttempts)).EqualTo(0)
                .And.Property(nameof(UserEntity.LockedUntil)).Null);
            Assert.That(entity!.LastLoginAt, Is.Not.Null
                .And.GreaterThanOrEqualTo(beforeLogin));
        }
    }

    [Test]
    public async Task GetByIdAsync_IncludesProviderData()
    {
        // Arrange
        var userEntity = new UserEntity
        {
            Username = "UserWithData",
            Enabled = true,
            Role = Role.Viewer,
            CreatedAt = DateTimeOffset.UtcNow
        };
        userEntity.ProviderData.Add(new()
        {
            Key = "testKey",
            Value = "testValue"
        });
        _context.Users.Add(userEntity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByIdAsync(userEntity.Id);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<User?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(User.ProviderData)).With.Count.EqualTo(1));
    }
}
