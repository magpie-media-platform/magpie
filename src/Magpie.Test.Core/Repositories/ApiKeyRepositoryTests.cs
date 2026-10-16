using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Services;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public sealed class ApiKeyRepositoryTests
{
    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private IApiKeyRepository _subject = null!;
    private List<MagpieEvent> _publishedEvents = null!;
    private Guid _userId;

    public static IEnumerable<TestCaseData> SuppliedExpiries()
    {
        yield return new TestCaseData(new DateTime(2026, 9, 8, 12, 30, 0, DateTimeKind.Utc))
            .SetArgDisplayNames("Z");
        yield return new TestCaseData(new DateTimeOffset(2026, 9, 8, 12, 30, 0, TimeSpan.Zero).LocalDateTime)
            .SetArgDisplayNames("+00:00");
        yield return new TestCaseData(new DateTimeOffset(2026, 9, 8, 5, 30, 0, TimeSpan.FromHours(-7)).LocalDateTime)
            .SetArgDisplayNames("-07:00");
    }

    public static IEnumerable<TestCaseData> ExpiryFilterCases()
    {
        yield return new TestCaseData(new DateTime(2026, 9, 8, 12, 30, 0, DateTimeKind.Utc), false)
            .SetArgDisplayNames("12:30Z", "false");
        yield return new TestCaseData(new DateTimeOffset(2026, 9, 8, 12, 30, 0, TimeSpan.Zero).LocalDateTime, false)
            .SetArgDisplayNames("12:30+00:00", "false");
        yield return new TestCaseData(
                new DateTimeOffset(2026, 9, 8, 5, 30, 0, TimeSpan.FromHours(-7)).LocalDateTime,
                false)
            .SetArgDisplayNames("05:30-07:00", "false");
        yield return new TestCaseData(new DateTime(2026, 9, 8, 11, 30, 0, DateTimeKind.Utc), true)
            .SetArgDisplayNames("11:30Z", "true");
        yield return new TestCaseData(new DateTimeOffset(2026, 9, 8, 11, 30, 0, TimeSpan.Zero).LocalDateTime, true)
            .SetArgDisplayNames("11:30+00:00", "true");
        yield return new TestCaseData(
                new DateTimeOffset(2026, 9, 8, 13, 30, 0, TimeSpan.FromHours(2)).LocalDateTime,
                true)
            .SetArgDisplayNames("13:30+02:00", "true");
    }

    [OneTimeSetUp]
    public void CreateTemplate()
    {
        _template = new SqliteConnection("Data Source=:memory:");
        _template.Open();
        using var context = new MagpieDbContext(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite(_template)
            .Options);
        context.Database.EnsureCreated();
    }

    [OneTimeTearDown]
    public void DisposeTemplate()
    {
        _template.Dispose();
    }

    [SetUp]
    public void SetUp()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        _template.BackupDatabase(connection);
        _context = new MagpieDbContext(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite(connection, contextOwnsConnection: true)
            .Options);

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

        var auditRecordRepository = new AuditRecordRepository(
            _context,
            Mock.Of<IEventBus>(),
            NullLogger<AuditRecordRepository>.Instance,
            new TransactionManager(_context, NullLogger<TransactionManager>.Instance),
            Mock.Of<IJobExecutionScopeFactory>(),
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));

        var transactionManager = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);
        _publishedEvents = [];
        var eventBus = new Mock<IEventBus>();
        eventBus
            .Setup(bus => bus.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()))
            .Callback((MagpieEvent magpieEvent, CancellationToken _) => _publishedEvents.Add(magpieEvent))
            .Returns(Task.CompletedTask);

        _subject = new ApiKeyRepository(
            _context,
            auditRecordRepository,
            transactionManager,
            eventBus.Object,
            NullLogger<ApiKeyRepository>.Instance,
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));
    }

    [TearDown]
    public void TearDown()
    {
        _context.Dispose();
    }

    [Test]
    public async Task UpdateLastUsedAsync_PreservesRevocationAndExpiry_WhenAnEarlierValidationFinishes()
    {
        // Arrange
        var key = new ApiKey
        {
            UserId = _userId,
            Name = "Revoked",
            KeyPrefix = "mgp_test",
            KeyHash = "hash",
            Enabled = false,
            ExpiresAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        _context.ApiKeys.Add(key);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject.UpdateLastUsedAsync(key.Id, new DateTime(2026, 9, 4, 0, 0, 0, DateTimeKind.Utc))
            .ConfigureAwait(false);
        await _subject.UpdateLastUsedAsync(key.Id, new DateTime(2026, 9, 3, 0, 0, 0, DateTimeKind.Utc))
            .ConfigureAwait(false);
        var stored = await _subject.GetByIdAsync(key.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stored!.Enabled, Is.False);
            Assert.That(stored.ExpiresAt, Is.EqualTo(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(stored.LastUsedAt, Is.EqualTo(new DateTime(2026, 9, 4, 0, 0, 0, DateTimeKind.Utc)));
        }
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.GetByIdAsync(apiKey.Id).ConfigureAwait(false);

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
        var result = await _subject.GetByIdAsync(Guid.NewGuid()).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ListAllAsync_ReturnsAllApiKeys_WhenApiKeysExist()
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.ListAllAsync().ConfigureAwait(false);

        // Assert
        Assert.That(result, Has.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_ReturnsEmptyList_WhenNoApiKeys()
    {
        // Act
        var result = await _subject.ListAllAsync().ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task CreateAsync_CreatesApiKey_WhenRequestIsValid()
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
        var result = await _subject.CreateAsync(apiKey, _userId).ConfigureAwait(false);

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
    public async Task CreateAsync_CreatesAuditRecord_WhenApiKeyIsCreated()
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
        await _subject.CreateAsync(apiKey, _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        apiKey.Name = "UpdatedKey";
        apiKey.Enabled = false;

        // Act
        var result = await _subject.UpdateAsync(apiKey, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);

        var updated = await _subject.GetByIdAsync(apiKey.Id).ConfigureAwait(false);
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
        var result = await _subject.UpdateAsync(apiKey, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task UpdateAsync_CreatesAuditRecord_WhenApiKeyIsUpdated()
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        apiKey.Name = "Updated";

        // Act
        await _subject.UpdateAsync(apiKey, _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Update"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("ApiKey"));
        }
    }

    [TestCase(true, false)]
    [TestCase(false, true)]
    public async Task PatchAsync_PersistsEnabled_WhenApiKeyExists(bool initialEnabled, bool patchedEnabled)
    {
        // Arrange
        var apiKey = new ApiKey
        {
            UserId = _userId,
            KeyPrefix = "test",
            Name = "ToPatch",
            KeyHash = "patchhash",
            Enabled = initialEnabled,
            CreatedAt = DateTime.UtcNow
        };
        _context.ApiKeys.Add(apiKey);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        // Act
        var result = await _subject
            .PatchAsync(new ApiKeyPatch { Id = apiKey.Id, Enabled = new() { Value = patchedEnabled } }, _userId)
            .ConfigureAwait(false);

        // Assert
        _context.ChangeTracker.Clear();
        var patched = await _subject.GetByIdAsync(apiKey.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(patched, Has.Property(nameof(ApiKey.Enabled)).EqualTo(patchedEnabled));
            Assert.That(patched, Has.Property(nameof(ApiKey.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(patched, Has.Property(nameof(ApiKey.Name)).EqualTo("ToPatch"));
        }
    }

    [Test]
    public async Task PatchAsync_ReturnsFalse_WhenApiKeyNotExists()
    {
        // Act
        var result = await _subject
            .PatchAsync(new ApiKeyPatch { Id = Guid.NewGuid(), Enabled = new() { Value = false } }, _userId)
            .ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task PatchAsync_PublishesUpdatedEvent_WhenEnabledChanges()
    {
        // Arrange
        var apiKey = new ApiKey
        {
            UserId = _userId,
            KeyPrefix = "test",
            Name = "PatchEvent",
            KeyHash = "patcheventhash",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ApiKeys.Add(apiKey);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        // Act
        await _subject
            .PatchAsync(new ApiKeyPatch { Id = apiKey.Id, Enabled = new() { Value = false } }, _userId)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                _publishedEvents.Select(magpieEvent => magpieEvent.Name),
                Is.EqualTo(["apiKey.updated"]));
            Assert.That(_publishedEvents[0].Context.Values, Does.Contain("Enabled"));
            Assert.That(_publishedEvents[0].Context["user.id"], Is.EqualTo(_userId.ToString("D")));
        }
    }

    [Test]
    public async Task PatchAsync_CreatesAuditRecord_WhenApiKeyIsPatched()
    {
        // Arrange
        var apiKey = new ApiKey
        {
            UserId = _userId,
            KeyPrefix = "test",
            Name = "PatchAudit",
            KeyHash = "patchaudithash",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ApiKeys.Add(apiKey);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        // Act
        await _subject
            .PatchAsync(new ApiKeyPatch { Id = apiKey.Id, Enabled = new() { Value = false } }, _userId)
            .ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Patch"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("ApiKey"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.DeleteAsync(apiKey.Id, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);

        var deleted = await _subject.GetByIdAsync(apiKey.Id).ConfigureAwait(false);
        Assert.That(deleted, Is.Null);
    }

    [Test]
    public async Task DeleteAsync_ReturnsFalse_WhenNotExists()
    {
        // Act
        var result = await _subject.DeleteAsync(Guid.NewGuid(), _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task DeleteAsync_CreatesAuditRecord_WhenApiKeyIsDeleted()
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject.DeleteAsync(apiKey.Id, _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Delete"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("ApiKey"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task CreateUpdateDeleteAsync_PublishesLifecycleEvents_WhenWritesSucceed()
    {
        // Arrange
        var apiKey = new ApiKey
        {
            UserId = _userId,
            KeyPrefix = "test",
            Name = "Lifecycle",
            KeyHash = "lifecyclehash",
            Enabled = true
        };

        // Act
        var created = await _subject.CreateAsync(apiKey, _userId).ConfigureAwait(false);
        _context.ChangeTracker.Clear();
        var updated = created with
        {
            UserId = Guid.NewGuid(),
            Name = "Lifecycle updated",
            Enabled = false
        };
        await _subject.UpdateAsync(updated, _userId).ConfigureAwait(false);
        await _subject.DeleteAsync(created.Id, _userId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                _publishedEvents.Select(magpieEvent => magpieEvent.Name),
                Is.EqualTo(
                [
                    "apiKey.created",
                    "apiKey.updated",
                    "apiKey.deleted"
                ]));
            Assert.That(_publishedEvents[0].Context.Values, Does.Contain(created.Id.ToString("D")));
            Assert.That(_publishedEvents[0].Context.Values, Does.Contain(_userId.ToString("D")));
            Assert.That(_publishedEvents[1].Context.Values, Does.Contain("Name,Enabled"));
            Assert.That(_publishedEvents[1].Context["user.id"], Is.EqualTo(_userId.ToString("D")));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsAllApiKeys_WhenQueryEmpty()
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
            Enabled = false,
            CreatedAt = DateTime.UtcNow
        };
        _context.ApiKeys.AddRange(apiKey1, apiKey2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new ApiKeyQuery(),
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Items, Has.Exactly(2).Items);
            Assert.That(result.TotalItems, Is.EqualTo(2));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsUserApiKeys_WhenUserIdSet()
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new ApiKeyQuery { UserId = _userId },
            new() { PageNumber = 1, PageSize = 100 },
            null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Items, Has.Exactly(2).Items);
            Assert.That(result.Items, Has.All.Matches<ApiKey>(k => k.UserId == _userId));
        }
    }

    [TestCase(true, "EnabledKey")]
    [TestCase(false, "DisabledKey")]
    public async Task QueryAsync_ReturnsMatchingApiKeys_WhenEnabledIsSet(bool enabled, string expectedName)
    {
        // Arrange
        var enabledKey = new ApiKey
        {
            UserId = _userId,
            KeyPrefix = "test",
            Name = "EnabledKey",
            KeyHash = "hash1",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        var disabledKey = new ApiKey
        {
            UserId = _userId,
            KeyPrefix = "test",
            Name = "DisabledKey",
            KeyHash = "hash2",
            Enabled = false,
            CreatedAt = DateTime.UtcNow
        };
        _context.ApiKeys.AddRange(enabledKey, disabledKey);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new ApiKeyQuery { Enabled = enabled },
            new() { PageNumber = 1, PageSize = 100 },
            null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Items, Has.Exactly(1).Items);
            Assert.That(result.Items, Has.One.With.Property(nameof(ApiKey.Name)).EqualTo(expectedName));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsExpiredApiKeys_WhenExpiredTrue()
    {
        // Arrange
        var expiredKey = new ApiKey
        {
            UserId = _userId,
            KeyPrefix = "test",
            Name = "ExpiredKey",
            KeyHash = "hash1",
            Enabled = true,
            ExpiresAt = new DateTime(2026, 9, 8, 11, 0, 0, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow
        };
        var futureKey = new ApiKey
        {
            UserId = _userId,
            KeyPrefix = "test",
            Name = "FutureKey",
            KeyHash = "hash2",
            Enabled = true,
            ExpiresAt = new DateTime(2026, 9, 8, 13, 0, 0, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow
        };
        var noExpiryKey = new ApiKey
        {
            UserId = _userId,
            KeyPrefix = "test",
            Name = "NoExpiryKey",
            KeyHash = "hash3",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ApiKeys.AddRange(expiredKey, futureKey, noExpiryKey);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new ApiKeyQuery { Expired = true },
            new() { PageNumber = 1, PageSize = 100 },
            null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Items, Has.Exactly(1).Items);
            Assert.That(result.Items, Has.One.With.Property(nameof(ApiKey.Name)).EqualTo("ExpiredKey"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsUnexpiredApiKeys_WhenExpiredFalse()
    {
        // Arrange
        var expiredKey = new ApiKey
        {
            UserId = _userId,
            KeyPrefix = "test",
            Name = "ExpiredKey",
            KeyHash = "hash1",
            Enabled = true,
            ExpiresAt = new DateTime(2026, 9, 8, 11, 0, 0, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow
        };
        var futureKey = new ApiKey
        {
            UserId = _userId,
            KeyPrefix = "test",
            Name = "FutureKey",
            KeyHash = "hash2",
            Enabled = true,
            ExpiresAt = new DateTime(2026, 9, 8, 13, 0, 0, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow
        };
        var noExpiryKey = new ApiKey
        {
            UserId = _userId,
            KeyPrefix = "test",
            Name = "NoExpiryKey",
            KeyHash = "hash3",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ApiKeys.AddRange(expiredKey, futureKey, noExpiryKey);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new ApiKeyQuery { Expired = false },
            new() { PageNumber = 1, PageSize = 100 },
            null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Items, Has.Exactly(2).Items);
            Assert.That(result.Items, Has.One.With.Property(nameof(ApiKey.Name)).EqualTo("FutureKey"));
            Assert.That(result.Items, Has.One.With.Property(nameof(ApiKey.Name)).EqualTo("NoExpiryKey"));
        }
    }

    [TestCaseSource(nameof(SuppliedExpiries))]
    public async Task GetByIdAsync_ReturnsSuppliedExpiryInstant_WhenExpiresAtHasOffset(DateTime expiresAt)
    {
        // Arrange
        var apiKey = new ApiKey
        {
            UserId = _userId,
            KeyPrefix = "test",
            Name = "OffsetKey",
            KeyHash = "hash1",
            Enabled = true,
            ExpiresAt = expiresAt,
            CreatedAt = DateTime.UtcNow
        };
        _context.ApiKeys.Add(apiKey);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.GetByIdAsync(apiKey.Id).ConfigureAwait(false);

        // Assert
        Assert.That(result?.ExpiresAt, Is.EqualTo(new DateTime(2026, 9, 8, 12, 30, 0, DateTimeKind.Utc)));
    }

    [TestCaseSource(nameof(ExpiryFilterCases))]
    public async Task QueryAsync_MatchesExpiryInstant_WhenExpiresAtHasOffset(DateTime expiresAt, bool expired)
    {
        // Arrange
        _context.ApiKeys.Add(new ApiKey
        {
            UserId = _userId,
            KeyPrefix = "test",
            Name = "OffsetKey",
            KeyHash = "hash1",
            Enabled = true,
            ExpiresAt = expiresAt,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject
            .QueryAsync(new ApiKeyQuery { Expired = expired }, new() { PageNumber = 1, PageSize = 100 }, null)
            .ConfigureAwait(false);

        // Assert
        Assert.That(result.Items, Has.One.With.Property(nameof(ApiKey.Name)).EqualTo("OffsetKey"));
    }

    [Test]
    public async Task QueryAsync_CombinesPredicates_WhenAllSet()
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var matchingKey = new ApiKey
        {
            UserId = _userId,
            KeyPrefix = "test",
            Name = "MatchingKey",
            KeyHash = "hash1",
            Enabled = true,
            ExpiresAt = new DateTime(2026, 9, 8, 13, 0, 0, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow
        };
        var disabledKey = new ApiKey
        {
            UserId = _userId,
            KeyPrefix = "test",
            Name = "DisabledKey",
            KeyHash = "hash2",
            Enabled = false,
            CreatedAt = DateTime.UtcNow
        };
        var expiredKey = new ApiKey
        {
            UserId = _userId,
            KeyPrefix = "test",
            Name = "ExpiredKey",
            KeyHash = "hash3",
            Enabled = true,
            ExpiresAt = new DateTime(2026, 9, 8, 11, 0, 0, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow
        };
        var otherUserKey = new ApiKey
        {
            UserId = otherUser.Id,
            KeyPrefix = "test",
            Name = "OtherKey",
            KeyHash = "hash4",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ApiKeys.AddRange(matchingKey, disabledKey, expiredKey, otherUserKey);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject
            .QueryAsync(new ApiKeyQuery { UserId = _userId, Enabled = true, Expired = false },
                new() { PageNumber = 1, PageSize = 100 },
                null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Items, Has.Exactly(1).Items);
            Assert.That(result.Items, Has.One.With.Property(nameof(ApiKey.Name)).EqualTo("MatchingKey"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsApiKey_WhenKeyHashMatches()
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.QueryAsync(
            new ApiKeyQuery { KeyHash = "uniquehash123" },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false)).Items.SingleOrDefault();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("HashKey"));
            Assert.That(result, Has.Property(nameof(result.KeyHash)).EqualTo("uniquehash123"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsNull_WhenKeyHashDoesNotMatch()
    {
        // Act
        var result = (await _subject.QueryAsync(
            new ApiKeyQuery { KeyHash = "nonexistenthash" },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false)).Items.SingleOrDefault();

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task QueryAsync_UsesDefaultSort_WhenSecretColumnIsRequested()
    {
        // Arrange
        _context.ApiKeys.AddRange(
            new ApiKey { Name = "Alpha", UserId = _userId, KeyPrefix = "alpha", KeyHash = "z" },
            new ApiKey { Name = "Zulu", UserId = _userId, KeyPrefix = "zulu", KeyHash = "a" });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var page = await _subject.QueryAsync(new ApiKeyQuery(),
            new() { PageNumber = 1, PageSize = 10 },
            new() { PropertyName = "keyHash", Direction = SortDirection.Ascending }).ConfigureAwait(false);

        // Assert
        Assert.That(page.Items.Select(item => item.Name), Is.EqualTo(["Alpha", "Zulu"]));
    }
}
