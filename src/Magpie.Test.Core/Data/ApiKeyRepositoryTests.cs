using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions.Data;
using Magpie.Core.Data;
using Magpie.Data.Database;
using Magpie.Models;
using Magpie.Models.Result;
using Magpie.Models.Users;

using Magpie.Test.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NUnit.Framework;

using ApiKeyEntity = Magpie.Data.Database.Entities.ApiKey;
using HistoryEntryEntity = Magpie.Data.Database.Entities.HistoryEntry;
using UserEntity = Magpie.Data.Database.Entities.User;

namespace Magpie.Test.Core.Data;

[TestFixture]
public class ApiKeyRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IApiKeyRepository _subject = null!;
    private Guid _userId;
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
        var testUser = new UserEntity
        {
            Username = "TestUser",
            Enabled = true,
            Role = Role.Viewer,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _context.Users.AddRange(adminUser, testUser);
        _context.SaveChanges();

        _adminUserId = adminUser.Id;
        _userId = testUser.Id;

        var loggerFactory = LoggerFactory.Create(b => b.AddTestLogger());
        var historyRepo = new HistoryRepository(_context, loggerFactory.CreateLogger<HistoryRepository>());
        _subject = new ApiKeyRepository(_context, historyRepo, loggerFactory.CreateLogger<ApiKeyRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context?.Dispose();
    }

    [Test]
    public async Task GetByIdAsync_ReturnsApiKey()
    {
        // Arrange
        var apiKeyEntity = new ApiKeyEntity
        {
            UserId = _userId,
            Name = "TestKey",
            KeyPrefix = "mgp_",
            KeyHash = "testhash123",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.ApiKeys.Add(apiKeyEntity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByIdAsync(apiKeyEntity.Id);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<ApiKey?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(ApiKey.Name)).EqualTo("TestKey")
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(ApiKey.KeyPrefix)).EqualTo("mgp_")
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(ApiKey.UserId)).EqualTo(_userId)
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(ApiKey.Enabled)).True);
    }

    [Test]
    public async Task GetByIdAsync_ReturnsNull_WhenApiKeyDoesNotExist()
    {
        // Act
        var result = await _subject.GetByIdAsync(Guid.NewGuid());

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<ApiKey?>>().With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task GetAllAsync_ReturnsAllApiKeys()
    {
        // Arrange
        var apiKeyEntity1 = new ApiKeyEntity
        {
            UserId = _userId,
            Name = "Key1",
            KeyPrefix = "mgp_",
            KeyHash = "hash1",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var apiKeyEntity2 = new ApiKeyEntity
        {
            UserId = _adminUserId,
            Name = "Key2",
            KeyPrefix = "mgp_",
            KeyHash = "hash2",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.ApiKeys.AddRange(apiKeyEntity1, apiKeyEntity2);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetAllAsync();

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<ApiKey>>>()
            .With.Property(nameof(SuccessResult<>.Value)).With.Count.EqualTo(2));
    }

    [Test]
    public async Task GetAllAsync_ReturnsEmptyList_WhenNoApiKeys()
    {
        // Act
        var result = await _subject.GetAllAsync();

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<ApiKey>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task CreateAsync_CreatesApiKey()
    {
        // Arrange
        const string name = "NewApiKey";

        // Act
        var result = await _subject.CreateAsync(
            _userId,
            name,
            "mgp_",
            "somehash",
            DateTimeOffset.UtcNow.AddDays(30),
            _adminUserId);

        // Assert
        var entity = await (
            from k in _context.ApiKeys.AsNoTracking()
            where k.Name == "NewApiKey"
            select k).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<ApiKey>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(ApiKey.Name)).EqualTo("NewApiKey")
                .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(ApiKey.UserId)).EqualTo(_userId)
                .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(ApiKey.Enabled)).True);
            Assert.That(entity, Is.InstanceOf<ApiKeyEntity>()
                .With.Property(nameof(ApiKeyEntity.Name)).EqualTo("NewApiKey")
                .And.Property(nameof(ApiKeyEntity.UserId)).EqualTo(_userId)
                .And.Property(nameof(ApiKeyEntity.CreatedByUserId)).EqualTo(_adminUserId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Create")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("ApiKey")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_adminUserId));
        }
    }

    [Test]
    public async Task DeleteAsync_DeletesApiKey()
    {
        // Arrange
        var apiKeyEntity = new ApiKeyEntity
        {
            UserId = _userId,
            Name = "ToDelete",
            KeyPrefix = "mgp_",
            KeyHash = "deletehash",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.ApiKeys.Add(apiKeyEntity);
        _context.SaveChanges();

        // Act
        var result = await _subject.DeleteAsync(apiKeyEntity.Id, _adminUserId);

        // Assert
        var entityExists = await _context.ApiKeys.AnyAsync(k => k.Id == apiKeyEntity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>());
            Assert.That(entityExists, Is.False);
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Delete")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("ApiKey")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_adminUserId));
        }
    }

    [Test]
    public async Task DeleteAsync_ReturnsFalse_WhenApiKeyDoesNotExist()
    {
        // Act
        var result = await _subject.DeleteAsync(Guid.NewGuid(), _adminUserId);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<bool>>()
            .With.Property(nameof(SuccessResult<>.Value)).False);
    }

    [Test]
    public async Task PatchAsync_PatchesEnabledField()
    {
        // Arrange
        var apiKeyEntity = new ApiKeyEntity
        {
            UserId = _userId,
            Name = "ToPatch",
            KeyPrefix = "mgp_",
            KeyHash = "patchhash",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.ApiKeys.Add(apiKeyEntity);
        _context.SaveChanges();

        var patch = new ApiKeyPatch
        {
            Id = apiKeyEntity.Id,
            Enabled = new FieldPatch<bool>(false)
        };

        // Act
        var result = await _subject.PatchAsync(patch, _adminUserId);

        // Assert
        var entity = await (
            from k in _context.ApiKeys.AsNoTracking()
            where k.Id == apiKeyEntity.Id
            select k).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(entity, Is.InstanceOf<ApiKeyEntity>()
                .With.Property(nameof(ApiKeyEntity.Enabled)).False
                .And.Property(nameof(ApiKeyEntity.ModifiedAt)).Not.Null
                .And.Property(nameof(ApiKeyEntity.ModifiedByUserId)).EqualTo(_adminUserId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Patch")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("ApiKey")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_adminUserId));
        }
    }


    [Test]
    public async Task PatchAsync_PatchesExpiresAtField()
    {
        // Arrange
        var apiKeyEntity = new ApiKeyEntity
        {
            UserId = _userId,
            Name = "ToPatch",
            KeyPrefix = "mgp_",
            KeyHash = "patchhash",
            Enabled = true,
            ExpiresAt = null,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.ApiKeys.Add(apiKeyEntity);
        _context.SaveChanges();

        var newExpiry = DateTimeOffset.UtcNow.AddDays(90);
        var patch = new ApiKeyPatch
        {
            Id = apiKeyEntity.Id,
            ExpiresAt = new FieldPatch<DateTimeOffset?>(newExpiry)
        };

        // Act
        var result = await _subject.PatchAsync(patch, _adminUserId);

        // Assert
        var entity = await (
            from k in _context.ApiKeys.AsNoTracking()
            where k.Id == apiKeyEntity.Id
            select k).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(entity, Is.InstanceOf<ApiKeyEntity>()
                .With.Property(nameof(ApiKeyEntity.ExpiresAt)).EqualTo(newExpiry));
        }
    }

    [Test]
    public async Task PatchAsync_ReturnsFalse_WhenApiKeyDoesNotExist()
    {
        // Arrange
        var patch = new ApiKeyPatch
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
    public async Task GetByKeyHashAsync_ReturnsApiKey()
    {
        // Arrange
        var apiKeyEntity = new ApiKeyEntity
        {
            UserId = _userId,
            Name = "KeyByHash",
            KeyPrefix = "mgp_test",
            KeyHash = "uniquehash123",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.ApiKeys.Add(apiKeyEntity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByKeyHashAsync("uniquehash123");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<ApiKey>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(ApiKey.Name)).EqualTo("KeyByHash")
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(ApiKey.KeyPrefix)).EqualTo("mgp_test")
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(ApiKey.UserId)).EqualTo(_userId));
    }

    [Test]
    public async Task GetByKeyHashAsync_ReturnsFailure_WhenApiKeyDoesNotExist()
    {
        // Act
        var result = await _subject.GetByKeyHashAsync("invalidhash");

        // Assert
        Assert.That(result, Is.InstanceOf<FailureResult<ApiKey>>());
    }

    [Test]
    public async Task GetByUserIdAsync_ReturnsApiKeysForUser()
    {
        // Arrange
        var apiKeyEntity1 = new ApiKeyEntity
        {
            UserId = _userId,
            Name = "UserKey1",
            KeyPrefix = "mgp_",
            KeyHash = "userhash1",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var apiKeyEntity2 = new ApiKeyEntity
        {
            UserId = _userId,
            Name = "UserKey2",
            KeyPrefix = "mgp_",
            KeyHash = "userhash2",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var apiKeyEntity3 = new ApiKeyEntity
        {
            UserId = _adminUserId,
            Name = "AdminKey",
            KeyPrefix = "mgp_",
            KeyHash = "adminhash",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.ApiKeys.AddRange(apiKeyEntity1, apiKeyEntity2, apiKeyEntity3);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByUserIdAsync(_userId);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<ApiKey>>>()
            .With.Property(nameof(SuccessResult<>.Value)).With.Count.EqualTo(2));
    }

    [Test]
    public async Task GetByUserIdAsync_ReturnsEmptyList_WhenUserHasNoApiKeys()
    {
        // Act
        var result = await _subject.GetByUserIdAsync(_userId);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<ApiKey>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }


    [Test]
    public async Task CreateAsync_SetsParentEntityIdToUserId()
    {
        // Arrange
        const string name = "ParentTrackingKey";

        // Act
        await _subject.CreateAsync(_userId, name, "mgp_", "somehash", null, _adminUserId);

        // Assert
        var historyEntry = await _context.HistoryEntries.SingleOrDefaultAsync();
        Assert.That(historyEntry, Is.InstanceOf<HistoryEntryEntity>()
            .With.Property(nameof(HistoryEntryEntity.ParentEntityId)).EqualTo(_userId.ToString()));
    }

    [Test]
    public async Task DeleteAsync_SetsParentEntityIdToUserId()
    {
        // Arrange
        var apiKeyEntity = new ApiKeyEntity
        {
            UserId = _userId,
            Name = "ParentTrackingDelete",
            KeyPrefix = "mgp_",
            KeyHash = "parenthash",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.ApiKeys.Add(apiKeyEntity);
        _context.SaveChanges();

        // Act
        await _subject.DeleteAsync(apiKeyEntity.Id, _adminUserId);

        // Assert
        var historyEntry = await _context.HistoryEntries.SingleOrDefaultAsync();
        Assert.That(historyEntry, Is.InstanceOf<HistoryEntryEntity>()
            .With.Property(nameof(HistoryEntryEntity.ParentEntityId)).EqualTo(_userId.ToString()));
    }

    [Test]
    public async Task PatchAsync_SetsParentEntityIdToUserId()
    {
        // Arrange
        var apiKeyEntity = new ApiKeyEntity
        {
            UserId = _userId,
            Name = "ParentTrackingPatch",
            KeyPrefix = "mgp_",
            KeyHash = "parentpatchhash",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.ApiKeys.Add(apiKeyEntity);
        _context.SaveChanges();

        var patch = new ApiKeyPatch
        {
            Id = apiKeyEntity.Id,
            Enabled = new FieldPatch<bool>(false)
        };

        // Act
        await _subject.PatchAsync(patch, _adminUserId);

        // Assert
        var historyEntry = await _context.HistoryEntries.SingleOrDefaultAsync();
        Assert.That(historyEntry, Is.InstanceOf<HistoryEntryEntity>()
            .With.Property(nameof(HistoryEntryEntity.ParentEntityId)).EqualTo(_userId.ToString()));
    }
}
