using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Core.Data;
using Magpie.Data.Database;
using Magpie.Models;
using Magpie.Models.Result;
using Magpie.Models.Users;
using Magpie.Test.Shared;

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
    private UserRepository _subject = null!;
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

        var loggerFactory = LoggerFactory.Create(b => b.AddTestLogger());
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
        var result = await _subject.GetByIdAsync(userEntity.Id, default);

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
        var result = await _subject.GetByIdAsync(Guid.NewGuid(), default);

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
        var result = await _subject.GetAllAsync(default);

        // Assert (2 added + 1 seeded admin + 1 setup admin = 4)
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<User>>>()
            .With.Property(nameof(SuccessResult<>.Value)).With.Count.EqualTo(4));
    }

    [Test]
    public async Task GetAllAsync_ReturnsSeededAndSetupUsers_WhenNoOtherUsers()
    {
        // Act
        var result = await _subject.GetAllAsync(default);

        // Assert (1 seeded admin + 1 setup admin = 2)
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<User>>>()
            .With.Property(nameof(SuccessResult<>.Value)).With.Count.EqualTo(2));
    }

    [Test]
    public async Task CreateAsync_CreatesUser()
    {
        // Act
        var result = await _subject.CreateAsync(
            username: "NewUser",
            email: "newuser@example.com",
            authProviderId: Guid.Empty,
            externalId: null,
            enabled: true,
            role: Role.Requester,
            passwordHash: null,
            passwordSalt: null,
            providerData: ImmutableDictionary<string, string>.Empty,
            createdAt: DateTimeOffset.UtcNow,
            createdByUserId: _adminUserId,
            cancellationToken: default);

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
        var providerData = new Dictionary<string, string>
        {
            ["key1"] = "value1",
            ["key2"] = "value2"
        }.ToImmutableDictionary();

        // Act
        var result = await _subject.CreateAsync(
            username: "UserWithData",
            email: string.Empty,
            authProviderId: Guid.Empty,
            externalId: null,
            enabled: true,
            role: Role.Viewer,
            passwordHash: null,
            passwordSalt: null,
            providerData: providerData,
            createdAt: DateTimeOffset.UtcNow,
            createdByUserId: _adminUserId,
            cancellationToken: default);

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
        var result = await _subject.UpdateAsync(model, _adminUserId, default);

        // Assert
        var entity = await (
            from u in _context.Users.AsNoTracking()
            where u.Id == userEntity.Id
            select u).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>()
                .With.Property(nameof(SuccessResult<>.Value)).True);
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
        var result = await _subject.UpdateAsync(model, _adminUserId, default);

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
        var result = await _subject.UpdateAsync(model, _adminUserId, default);

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
            Assert.That(providerDataEntities, Has.None.With.Property("Key").EqualTo("toRemove"));
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
        var result = await _subject.DeleteAsync(userEntity.Id, _adminUserId, default);

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
        var result = await _subject.DeleteAsync(Guid.NewGuid(), _adminUserId, default);

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
        var result = await _subject.DeleteAsync(userEntity.Id, _adminUserId, default);

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
        var result = await _subject.PatchAsync(patch, _adminUserId, default);

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
        var result = await _subject.PatchAsync(patch, _adminUserId, default);

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
        var result = await _subject.PatchAsync(patch, _adminUserId, default);

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
        var result = await _subject.GetByUsernameAsync("UniqueUsername", default);

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
        var result = await _subject.GetByUsernameAsync("NonExistentUser", default);

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
        var result = await _subject.GetByExternalIdAsync(authProviderId, "external-123", default);

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
        var result = await _subject.GetByExternalIdAsync(Guid.NewGuid(), "nonexistent", default);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<User?>>().With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task GetUserCredentialsAsync_ReturnsCredentials_WhenUserExists()
    {
        // Arrange
        var userEntity = new UserEntity
        {
            Username = "CredUser",
            Enabled = true,
            Role = Role.Viewer,
            PasswordHash = "testhash",
            PasswordSalt = "testsalt",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Users.Add(userEntity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetUserCredentialsAsync("CredUser", default);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<UserCredentials?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(UserCredentials.UserId))
                .EqualTo(userEntity.Id)
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(UserCredentials.PasswordHash))
                .EqualTo("testhash")
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(UserCredentials.PasswordSalt))
                .EqualTo("testsalt")
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(UserCredentials.Enabled))
                .True);
    }

    [Test]
    public async Task GetUserCredentialsAsync_ReturnsNull_WhenUserDoesNotExist()
    {
        // Act
        var result = await _subject.GetUserCredentialsAsync("nonexistent", default);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<UserCredentials?>>().With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task GetUserCredentialsAsync_ReturnsLockedUntil_WhenUserIsLocked()
    {
        // Arrange
        var lockedUntil = DateTimeOffset.UtcNow.AddHours(1);
        var userEntity = new UserEntity
        {
            Username = "LockedUser",
            Enabled = true,
            Role = Role.Viewer,
            PasswordHash = "testhash",
            PasswordSalt = "testsalt",
            LockedUntil = lockedUntil,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Users.Add(userEntity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetUserCredentialsAsync("LockedUser", default);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<UserCredentials?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(UserCredentials.LockedUntil))
                .EqualTo(lockedUntil));
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
        var result = await _subject.UpdatePasswordAsync(
            userEntity.Id,
            "newHash",
            "newSalt",
            unlock: null,
            resetFailedLoginAttempts: null,
            _adminUserId,
            default);

        // Assert
        var entity = await (
            from u in _context.Users.AsNoTracking()
            where u.Id == userEntity.Id
            select u).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<Unit>>());
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
    public async Task UpdatePasswordAsync_UnlocksUser_WhenUnlockIsTrue()
    {
        // Arrange
        var userEntity = new UserEntity
        {
            Username = "LockedUser",
            Enabled = true,
            Role = Role.Viewer,
            PasswordHash = "oldHash",
            PasswordSalt = "oldSalt",
            LockedUntil = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Users.Add(userEntity);
        _context.SaveChanges();

        // Act
        var result = await _subject.UpdatePasswordAsync(
            userEntity.Id,
            "newHash",
            "newSalt",
            unlock: true,
            resetFailedLoginAttempts: null,
            _adminUserId,
            default);

        // Assert
        var entity = await (
            from u in _context.Users.AsNoTracking()
            where u.Id == userEntity.Id
            select u).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<Unit>>());
            Assert.That(entity, Is.InstanceOf<UserEntity>()
                .With.Property(nameof(UserEntity.LockedUntil)).Null);
        }
    }

    [Test]
    public async Task UpdatePasswordAsync_ResetsFailedAttempts_WhenResetIsTrue()
    {
        // Arrange
        var userEntity = new UserEntity
        {
            Username = "FailedUser",
            Enabled = true,
            Role = Role.Viewer,
            PasswordHash = "oldHash",
            PasswordSalt = "oldSalt",
            FailedLoginAttempts = 5,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Users.Add(userEntity);
        _context.SaveChanges();

        // Act
        var result = await _subject.UpdatePasswordAsync(
            userEntity.Id,
            "newHash",
            "newSalt",
            unlock: null,
            resetFailedLoginAttempts: true,
            _adminUserId,
            default);

        // Assert
        var entity = await (
            from u in _context.Users.AsNoTracking()
            where u.Id == userEntity.Id
            select u).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<Unit>>());
            Assert.That(entity, Is.InstanceOf<UserEntity>()
                .With.Property(nameof(UserEntity.FailedLoginAttempts)).EqualTo(0));
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
        var result = await _subject.RecordFailedLoginAsync(userEntity.Id, null, default);

        // Assert
        var entity = await (
            from u in _context.Users.AsNoTracking()
            where u.Id == userEntity.Id
            select u).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<Unit>>());
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
        var result = await _subject.RecordFailedLoginAsync(userEntity.Id, lockoutDuration, default);

        // Assert
        var entity = await (
            from u in _context.Users.AsNoTracking()
            where u.Id == userEntity.Id
            select u).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<Unit>>());
            Assert.That(entity, Is.InstanceOf<UserEntity>()
                .With.Property(nameof(UserEntity.FailedLoginAttempts)).EqualTo(5));
            Assert.That(entity!.LockedUntil, Is.Not.Null
                .And.GreaterThanOrEqualTo(beforeLockout.Add(lockoutDuration)));
        }
    }

    [Test]
    public async Task ResetFailedLoginAttemptsAsync_ResetsFailedAttemptsAndUpdatesLastLogin()
    {
        // Arrange
        var userEntity = new UserEntity
        {
            Username = "SuccessfulLoginUser",
            Enabled = true,
            Role = Role.Viewer,
            FailedLoginAttempts = 3,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Users.Add(userEntity);
        _context.SaveChanges();

        var beforeLogin = DateTimeOffset.UtcNow;

        // Act
        var result = await _subject.ResetFailedLoginAttemptsAsync(userEntity.Id, default);

        // Assert
        var entity = await (
            from u in _context.Users.AsNoTracking()
            where u.Id == userEntity.Id
            select u).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<Unit>>());
            Assert.That(entity, Is.InstanceOf<UserEntity>()
                .With.Property(nameof(UserEntity.FailedLoginAttempts)).EqualTo(0));
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
        var result = await _subject.GetByIdAsync(userEntity.Id, default);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<User?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(User.ProviderData)).With.Count.EqualTo(1));
    }
}
