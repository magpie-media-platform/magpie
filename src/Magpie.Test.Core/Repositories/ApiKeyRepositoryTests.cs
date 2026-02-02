using System;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Test.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public class ApiKeyRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IApiKeyRepository _subject = null!;
    private Guid _userId;

    [SetUp]
    public void SetUp()
    {
        _context = new MagpieDbContext(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options);

        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();

        var user = new User
        {
            Username = "TestUser",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Admin,
            CreatedAt = DateTime.UtcNow
        };

        _context.Users.Add(user);
        _context.SaveChanges();

        _userId = user.Id;

        var loggerFactory = LoggerFactory.Create(builder => builder.AddTestLogger());

        var auditRecordRepository = new AuditRecordRepository(
            _context,
            loggerFactory.CreateLogger<AuditRecordRepository>());

        var transactionManager = new TransactionManager(_context);

        _subject = new ApiKeyRepository(
            _context,
            auditRecordRepository,
            transactionManager,
            loggerFactory.CreateLogger<ApiKeyRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [Test]
    public async Task GetByIdAsync_ReturnsApiKey_WhenExists()
    {
        // Arrange
        var apiKey = new ApiKey
        {
            UserId = _userId,
            KeyPrefix = "test",
            Name = "TestKey",
            KeyHash = "hash123",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ApiKeys.Add(apiKey);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.GetByIdAsync(apiKey.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("TestKey"));
            Assert.That(result, Has.Property(nameof(result.KeyHash)).EqualTo("hash123"));
            Assert.That(result, Has.Property(nameof(result.Enabled)).True);
        }
    }

    [Test]
    public async Task GetByIdAsync_ReturnsNull_WhenNotExists()
    {
        // Act
        var result = await _subject.GetByIdAsync(Guid.NewGuid());

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ListAllAsync_ReturnsAllApiKeys()
    {
        // Arrange
        var apiKey1 = new ApiKey
        {
            UserId = _userId,
            KeyPrefix = "test",
            Name = "Key1",
            KeyHash = "hash1",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        var apiKey2 = new ApiKey
        {
            UserId = _userId,
            KeyPrefix = "test",
            Name = "Key2",
            KeyHash = "hash2",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ApiKeys.AddRange(apiKey1, apiKey2);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.ListAllAsync();

        // Assert
        Assert.That(result, Has.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_ReturnsEmptyList_WhenNoApiKeys()
    {
        // Act
        var result = await _subject.ListAllAsync();

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task CreateAsync_CreatesApiKey()
    {
        // Arrange
        var apiKey = new ApiKey
        {
            UserId = _userId,
            KeyPrefix = "test",
            Name = "NewKey",
            KeyHash = "newhash",
            Enabled = true
        };

        // Act
        var result = await _subject.CreateAsync(apiKey, _userId);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Id)).Not.EqualTo(Guid.Empty));
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("NewKey"));
            Assert.That(result, Has.Property(nameof(result.KeyHash)).EqualTo("newhash"));
            Assert.That(result, Has.Property(nameof(result.CreatedByUserId)).EqualTo(_userId));
            Assert.That(result, Has.Property(nameof(result.CreatedAt)).Not.EqualTo(default(DateTime)));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesAuditRecord()
    {
        // Arrange
        var apiKey = new ApiKey
        {
            UserId = _userId,
            KeyPrefix = "test",
            Name = "AuditedKey",
            KeyHash = "audithash",
            Enabled = true
        };

        // Act
        await _subject.CreateAsync(apiKey, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Create"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("ApiKey"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesApiKey_WhenExists()
    {
        // Arrange
        var apiKey = new ApiKey
        {
            UserId = _userId,
            KeyPrefix = "test",
            Name = "OriginalKey",
            KeyHash = "originalhash",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ApiKeys.Add(apiKey);
        await _context.SaveChangesAsync();

        apiKey.Name = "UpdatedKey";
        apiKey.Enabled = false;

        // Act
        var result = await _subject.UpdateAsync(apiKey, _userId);

        // Assert
        Assert.That(result, Is.True);

        var updated = await _subject.GetByIdAsync(apiKey.Id);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated, Has.Property(nameof(updated.Name)).EqualTo("UpdatedKey"));
            Assert.That(updated, Has.Property(nameof(updated.Enabled)).False);
            Assert.That(updated, Has.Property(nameof(updated.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(updated, Has.Property(nameof(updated.ModifiedAt)).Not.Null);
        }
    }

    [Test]
    public async Task UpdateAsync_ReturnsFalse_WhenNotExists()
    {
        // Arrange
        var apiKey = new ApiKey
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            KeyPrefix = "test",
            Name = "NonExistent",
            KeyHash = "hash",
            Enabled = true
        };

        // Act
        var result = await _subject.UpdateAsync(apiKey, _userId);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task UpdateAsync_CreatesAuditRecord()
    {
        // Arrange
        var apiKey = new ApiKey
        {
            UserId = _userId,
            KeyPrefix = "test",
            Name = "ToUpdate",
            KeyHash = "updatehash",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ApiKeys.Add(apiKey);
        await _context.SaveChangesAsync();

        apiKey.Name = "Updated";

        // Act
        await _subject.UpdateAsync(apiKey, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Update"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("ApiKey"));
        }
    }

    [Test]
    public async Task DeleteAsync_DeletesApiKey_WhenExists()
    {
        // Arrange
        var apiKey = new ApiKey
        {
            UserId = _userId,
            KeyPrefix = "test",
            Name = "ToDelete",
            KeyHash = "deletehash",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ApiKeys.Add(apiKey);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.DeleteAsync(apiKey.Id, _userId);

        // Assert
        Assert.That(result, Is.True);

        var deleted = await _subject.GetByIdAsync(apiKey.Id);
        Assert.That(deleted, Is.Null);
    }

    [Test]
    public async Task DeleteAsync_ReturnsFalse_WhenNotExists()
    {
        // Act
        var result = await _subject.DeleteAsync(Guid.NewGuid(), _userId);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task DeleteAsync_CreatesAuditRecord()
    {
        // Arrange
        var apiKey = new ApiKey
        {
            UserId = _userId,
            KeyPrefix = "test",
            Name = "DeleteAudit",
            KeyHash = "deleteaudithash",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ApiKeys.Add(apiKey);
        await _context.SaveChangesAsync();

        // Act
        await _subject.DeleteAsync(apiKey.Id, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Delete"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("ApiKey"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task GetByUserIdAsync_ReturnsUserApiKeys()
    {
        // Arrange
        var otherUser = new User
        {
            Username = "OtherUser",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Viewer,
            CreatedAt = DateTime.UtcNow
        };
        _context.Users.Add(otherUser);
        await _context.SaveChangesAsync();

        var apiKey1 = new ApiKey
        {
            UserId = _userId,
            KeyPrefix = "test",
            Name = "Key1",
            KeyHash = "hash1",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        var apiKey2 = new ApiKey
        {
            UserId = _userId,
            KeyPrefix = "test",
            Name = "Key2",
            KeyHash = "hash2",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        var apiKey3 = new ApiKey
        {
            UserId = otherUser.Id,
            KeyPrefix = "test",
            Name = "OtherKey",
            KeyHash = "hash3",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ApiKeys.AddRange(apiKey1, apiKey2, apiKey3);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.GetByUserIdAsync(_userId);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Exactly(2).Items);
            Assert.That(result, Has.All.Matches<ApiKey>(k => k.UserId == _userId));
        }
    }

    [Test]
    public async Task GetByKeyHashAsync_ReturnsApiKey_WhenExists()
    {
        // Arrange
        var apiKey = new ApiKey
        {
            UserId = _userId,
            KeyPrefix = "test",
            Name = "HashKey",
            KeyHash = "uniquehash123",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ApiKeys.Add(apiKey);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.GetByKeyHashAsync("uniquehash123");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("HashKey"));
            Assert.That(result, Has.Property(nameof(result.KeyHash)).EqualTo("uniquehash123"));
        }
    }

    [Test]
    public async Task GetByKeyHashAsync_ReturnsNull_WhenNotExists()
    {
        // Act
        var result = await _subject.GetByKeyHashAsync("nonexistenthash");

        // Assert
        Assert.That(result, Is.Null);
    }
}
