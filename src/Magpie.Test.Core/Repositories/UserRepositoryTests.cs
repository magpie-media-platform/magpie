using System;
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
using Magpie.Models.Paging;
using Magpie.Models.Repositories;
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
public sealed class UserRepositoryTests
{
    private Mock<IEventBus> _updateEvents = null!;

    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private IUserRepository _subject = null!;
    private Guid _adminUserId;

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

        var adminUser = new User
        {
            Username = "AdminUser",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Admin,
            CreatedAt = DateTime.UtcNow
        };

        _context.Users.Add(adminUser);
        _context.SaveChanges();

        _adminUserId = adminUser.Id;

        var auditRecordRepository = new AuditRecordRepository(
            _context,
            Mock.Of<IEventBus>(),
            NullLogger<AuditRecordRepository>.Instance,
            new TransactionManager(_context, NullLogger<TransactionManager>.Instance),
            Mock.Of<IJobExecutionScopeFactory>(),
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));

        var transactionManager = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);

        _updateEvents = new Mock<IEventBus>();
        _subject = new UserRepository(
            _context,
            auditRecordRepository,
            transactionManager,
            _updateEvents.Object,
            NullLogger<UserRepository>.Instance,
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));
    }

    [TearDown]
    public void TearDown()
    {
        _context.Dispose();
    }

    [Test]
    public async Task CreateAsync_CreatesUser_WhenRequestIsValid()
    {
        // Arrange
        var user = new User
        {
            Username = "NewUser",
            ProviderType = "local",
            Email = "newuser@example.com",
            Enabled = true,
            Role = Role.Viewer
        };

        // Act
        var result = await _subject.CreateAsync(user, _adminUserId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Id)).Not.EqualTo(Guid.Empty));
            Assert.That(result, Has.Property(nameof(result.Username)).EqualTo("NewUser"));
            Assert.That(result, Has.Property(nameof(result.Email)).EqualTo("newuser@example.com"));
            Assert.That(result, Has.Property(nameof(result.CreatedByUserId)).EqualTo(_adminUserId));
            Assert.That(result, Has.Property(nameof(result.CreatedAt)).Not.EqualTo(default(DateTime)));
        }
    }

    [Test]
    public async Task GetByIdAsync_ReturnsUser_WhenExists()
    {
        // Arrange
        var user = new User
        {
            Username = "TestUser",
            ProviderType = "local",
            Email = "test@example.com",
            Enabled = true,
            Role = Role.Viewer
        };
        var created = await _subject.CreateAsync(user, _adminUserId).ConfigureAwait(false);

        // Act
        var result = await _subject.GetByIdAsync(created.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Id)).EqualTo(created.Id));
            Assert.That(result, Has.Property(nameof(result.Username)).EqualTo("TestUser"));
            Assert.That(result, Has.Property(nameof(result.Email)).EqualTo("test@example.com"));
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
    public async Task ListAllAsync_ReturnsAllUsers_WhenUsersExist()
    {
        // Arrange
        await _subject.CreateAsync(
            new User
            {
                Username = "User1",
                ProviderType = "local",
                Enabled = true,
                Role = Role.Viewer
            },
            _adminUserId).ConfigureAwait(false);
        await _subject.CreateAsync(
            new User
            {
                Username = "User2",
                ProviderType = "local",
                Enabled = true,
                Role = Role.Manager
            },
            _adminUserId).ConfigureAwait(false);

        // Act
        var result = await _subject.ListAllAsync().ConfigureAwait(false);

        // Assert
        Assert.That(result, Has.Exactly(3).Items);
    }

    [Test]
    public async Task QueryAsync_ReturnsUser_WhenUsernameMatches()
    {
        // Arrange
        await _subject.CreateAsync(
            new User
            {
                Username = "UniqueUser",
                ProviderType = "local",
                Email = "unique@example.com",
                Enabled = true,
                Role = Role.Viewer
            },
            _adminUserId).ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(
            new UserQuery { Username = "UniqueUser" },
                new() { PageNumber = 1, PageSize = 100 },
                null, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Items.Single(), Has.Property(nameof(User.Username)).EqualTo("UniqueUser"));
            Assert.That(result.Items.Single(), Has.Property(nameof(User.Email)).EqualTo("unique@example.com"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsEmpty_WhenUsernameDoesNotMatch()
    {
        // Act
        var result = await _subject.QueryAsync(
            new UserQuery { Username = "NonExistentUser" },
                new() { PageNumber = 1, PageSize = 100 },
                null, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Items, Is.Empty);
    }

    [Test]
    public async Task QueryAsync_ReturnsUser_WhenProviderIdentityMatches()
    {
        // Arrange
        await _subject.CreateAsync(
            new User
            {
                Username = "ExternalUser",
                ProviderType = "oauth",
                ExternalId = "external-123",
                Enabled = true,
                Role = Role.Viewer
            },
            _adminUserId).ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(
            new UserQuery { ProviderType = "oauth", ExternalId = "external-123" },
            new() { PageNumber = 1, PageSize = 100 },
            null,
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Items.Single(), Has.Property(nameof(User.Username)).EqualTo("ExternalUser"));
            Assert.That(result.Items.Single(), Has.Property(nameof(User.ProviderType)).EqualTo("oauth"));
            Assert.That(result.Items.Single(), Has.Property(nameof(User.ExternalId)).EqualTo("external-123"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsEmpty_WhenProviderIdentityDoesNotMatch()
    {
        // Act
        var result = await _subject.QueryAsync(
            new UserQuery { ProviderType = "oauth", ExternalId = "nonexistent" },
            new() { PageNumber = 1, PageSize = 100 },
            null,
            default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Items, Is.Empty);
    }

    [Test]
    public async Task UpdateAsync_UpdatesUser_WhenExists()
    {
        // Arrange
        var user = new User
        {
            Username = "OriginalUser",
            ProviderType = "local",
            Email = "original@example.com",
            Enabled = true,
            Role = Role.Viewer
        };
        var created = await _subject.CreateAsync(user, _adminUserId).ConfigureAwait(false);

        created.Username = "UpdatedUser";
        created.Email = "updated@example.com";
        created.Enabled = false;
        created.Role = Role.Manager;

        // Act
        var result = await _subject.UpdateAsync(created, _adminUserId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);

        var updated = await _subject.GetByIdAsync(created.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated, Has.Property(nameof(updated.Username)).EqualTo("UpdatedUser"));
            Assert.That(updated, Has.Property(nameof(updated.Email)).EqualTo("updated@example.com"));
            Assert.That(updated, Has.Property(nameof(updated.Enabled)).False);
            Assert.That(updated, Has.Property(nameof(updated.Role)).EqualTo(Role.Manager));
            Assert.That(updated, Has.Property(nameof(updated.ModifiedByUserId)).EqualTo(_adminUserId));
        }
    }

    [Test]
    public async Task UpdateAsync_PublishesUpdatedEventWithChangedFields_WhenExists()
    {
        // Arrange
        var created = await _subject.CreateAsync(new User
        {
            Username = "OriginalUser",
            ProviderType = "local",
            Email = "original@example.com",
            Enabled = true,
            Role = Role.Viewer
        }, _adminUserId).ConfigureAwait(false);

        created.Username = "UpdatedUser";
        created.Email = "updated@example.com";
        created.Enabled = false;
        created.Role = Role.Manager;

        // Act
        await _subject.UpdateAsync(created, _adminUserId).ConfigureAwait(false);

        // Assert
        _updateEvents.Verify(bus => bus.PublishAsync(
            It.Is<MagpieEvent>(e =>
                e.Name == "user.updated" &&
                e.Context.ContainsKey("user.changedFields") &&
                e.Context["user.changedFields"] == "username,email,enabled,role"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task UpdateAsync_ReturnsFalse_WhenNotExists()
    {
        // Arrange
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "NonExistent",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Viewer
        };

        // Act
        var result = await _subject.UpdateAsync(user, _adminUserId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task DeleteAsync_DeletesUser_WhenExists()
    {
        // Arrange
        var user = new User
        {
            Username = "ToDelete",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Viewer
        };
        var created = await _subject.CreateAsync(user, _adminUserId).ConfigureAwait(false);

        // Act
        var result = await _subject.DeleteAsync(created.Id, _adminUserId).ConfigureAwait(false);

        // Assert
        var deleted = await _subject.GetByIdAsync(created.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(deleted, Is.Null);
        }
    }

    [Test]
    public async Task DeleteAsync_DeletesRefreshTokens_WhenUserOwnsRefreshToken()
    {
        // Arrange
        var user = new User
        {
            Username = "RefreshTokenOwner",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Viewer
        };
        var created = await _subject.CreateAsync(user, _adminUserId).ConfigureAwait(false);
        _context.RefreshTokens.Add(new()
        {
            UserId = created.Id,
            TokenHash = "refresh-token-hash",
            ExpiresAt = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.DeleteAsync(created.Id, _adminUserId).ConfigureAwait(false);

        // Assert
        var deleted = await _subject.GetByIdAsync(created.Id).ConfigureAwait(false);
        var refreshTokens = await _context.RefreshTokens.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(deleted, Is.Null);
            Assert.That(refreshTokens, Is.Empty);
        }
    }

    [Test]
    public async Task DeleteAsync_DeletesApiKeys_WhenUserOwnsApiKey()
    {
        // Arrange
        var user = new User
        {
            Username = "ApiKeyOwner",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Viewer
        };
        var created = await _subject.CreateAsync(user, _adminUserId).ConfigureAwait(false);
        _context.ApiKeys.Add(new()
        {
            UserId = created.Id,
            Name = "Deletion test key",
            KeyHash = "api-key-hash",
            KeyPrefix = "delete",
            Enabled = true,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.DeleteAsync(created.Id, _adminUserId).ConfigureAwait(false);

        // Assert
        var deleted = await _subject.GetByIdAsync(created.Id).ConfigureAwait(false);
        var apiKeys = await _context.ApiKeys.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(deleted, Is.Null);
            Assert.That(apiKeys, Is.Empty);
        }
    }

    [Test]
    public async Task DeleteAsync_ReturnsFalse_WhenNotExists()
    {
        // Act
        var result = await _subject.DeleteAsync(Guid.NewGuid(), _adminUserId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task PatchAsync_UpdatesEnabledState_WhenEnabledStateIsProvided()
    {
        // Arrange
        var user = new User
        {
            Username = "ToPatch",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Viewer
        };
        var created = await _subject.CreateAsync(user, _adminUserId).ConfigureAwait(false);

        var patch = new UserPatch { Id = created.Id, Enabled = new() { Value = false } };

        // Act
        var result = await _subject.PatchAsync(patch, _adminUserId).ConfigureAwait(false);

        // Assert
        var patched = await _subject.GetByIdAsync(created.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(patched, Has.Property(nameof(patched.Enabled)).False);
            Assert.That(patched, Has.Property(nameof(patched.ModifiedByUserId)).EqualTo(_adminUserId));
        }
    }

    [Test]
    public async Task PatchAsync_UpdatesRole_WhenRoleIsProvided()
    {
        // Arrange
        var user = new User
        {
            Username = "ToPatchRole",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Viewer
        };
        var created = await _subject.CreateAsync(user, _adminUserId).ConfigureAwait(false);

        var patch = new UserPatch { Id = created.Id, Role = new() { Value = Role.Admin } };

        // Act
        var result = await _subject.PatchAsync(patch, _adminUserId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);

        var patched = await _subject.GetByIdAsync(created.Id).ConfigureAwait(false);
        Assert.That(patched, Has.Property(nameof(patched.Role)).EqualTo(Role.Admin));
    }

    [Test]
    public async Task PatchAsync_ReturnsFalse_WhenUserNotExists()
    {
        // Arrange
        var patch = new UserPatch { Id = Guid.NewGuid(), Enabled = new() { Value = false } };

        // Act
        var result = await _subject.PatchAsync(patch, _adminUserId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task PatchAsync_UsesStoredAuthorization_WhenTrackedUserIsStale()
    {
        // Arrange
        var created = await _subject.CreateAsync(new User
        {
            Username = "StaleTrackedUser",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Viewer
        }, _adminUserId).ConfigureAwait(false);
        await _context.Users.Where(user => user.Id == created.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.Role, Role.Manager)).ConfigureAwait(false);

        // Act
        var changed = await _subject.PatchAsync(new UserPatch
        {
            Id = created.Id,
            Role = new() { Value = Role.Viewer }
        }, _adminUserId).ConfigureAwait(false);

        // Assert
        var stored = await _subject.GetByIdAsync(created.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(changed, Is.True);
            Assert.That(stored!.Role, Is.EqualTo(Role.Viewer));
        }
    }

    [Test]
    public void LockAuthorizationAsync_RejectsRequest_WhenNoTransactionIsOpen()
    {
        // Act
        var error = Assert.ThrowsAsync<InvalidOperationException>(() => _subject.LockAuthorizationAsync(_adminUserId));

        // Assert
        Assert.That(error!.Message, Is.EqualTo("An account authorization lock requires a transaction."));
    }

    [Test]
    public async Task QueryAsync_ReturnsAllUsers_WhenNoFilter()
    {
        // Arrange
        await _subject.CreateAsync(
            new User
            {
                Username = "QueryUser1",
                ProviderType = "local",
                Enabled = true,
                Role = Role.Viewer
            },
            _adminUserId).ConfigureAwait(false);
        await _subject.CreateAsync(
            new User
            {
                Username = "QueryUser2",
                ProviderType = "local",
                Enabled = false,
                Role = Role.Manager
            },
            _adminUserId).ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new UserQuery(),
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        Assert.That(result, Has.Property(nameof(result.Items)).With.Exactly(3).Items);
    }

    [Test]
    public async Task QueryAsync_ReturnsMatchingUsers_WhenEnabledFilterIsSet()
    {
        // Arrange
        await _subject.CreateAsync(
            new User
            {
                Username = "EnabledUser",
                ProviderType = "local",
                Enabled = true,
                Role = Role.Viewer
            },
            _adminUserId).ConfigureAwait(false);
        await _subject.CreateAsync(
            new User
            {
                Username = "DisabledUser",
                ProviderType = "local",
                Enabled = false,
                Role = Role.Viewer
            },
            _adminUserId).ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new() { Enabled = false },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Items.Select(u => u.Username), Does.Contain("DisabledUser"));
            Assert.That(result.Items, Has.All.Property(nameof(User.Enabled)).False);
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsMatchingUsers_WhenRoleFilterIsSet()
    {
        // Arrange
        await _subject.CreateAsync(
            new User
            {
                Username = "ManagerUser",
                ProviderType = "local",
                Enabled = true,
                Role = Role.Manager
            },
            _adminUserId).ConfigureAwait(false);
        await _subject.CreateAsync(
            new User
            {
                Username = "ViewerUser",
                ProviderType = "local",
                Enabled = true,
                Role = Role.Viewer
            },
            _adminUserId).ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new() { Role = Role.Manager },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Count.EqualTo(1));
            Assert.That(
                result,
                Has.Property(nameof(result.Items))
                    .With.One.Property(nameof(User.Username)).EqualTo("ManagerUser"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsMatchingUsers_WhenProviderFilterIsSet()
    {
        // Arrange
        await _subject.CreateAsync(
            new User
            {
                Username = "LocalUser",
                ProviderType = "local",
                Enabled = true,
                Role = Role.Viewer
            },
            _adminUserId).ConfigureAwait(false);
        await _subject.CreateAsync(
            new User
            {
                Username = "OAuthUser",
                ProviderType = "oauth",
                Enabled = true,
                Role = Role.Viewer
            },
            _adminUserId).ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new() { ProviderType = "oauth" },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).With.Exactly(1).Items);
            Assert.That(
                result,
                Has.Property(nameof(result.Items))
                    .With.One.Property(nameof(User.Username)).EqualTo("OAuthUser"));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesAuditRecord_WhenUserIsCreated()
    {
        // Arrange
        var user = new User
        {
            Username = "AuditedUser",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Viewer
        };

        // Act
        await _subject.CreateAsync(user, _adminUserId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Create"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("User"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_adminUserId));
        }
    }

    [Test]
    public async Task UpdateAsync_CreatesAuditRecord_WhenUserExists()
    {
        // Arrange
        var user = new User
        {
            Username = "ToUpdateAudit",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Viewer
        };
        var created = await _subject.CreateAsync(user, _adminUserId).ConfigureAwait(false);

        created.Username = "Updated";

        // Act
        await _subject.UpdateAsync(created, _adminUserId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        Assert.That(auditRecords, Has.Count.EqualTo(2));
        Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Update"));
    }

    [Test]
    public async Task DeleteAsync_CreatesAuditRecord_WhenUserExists()
    {
        // Arrange
        var user = new User
        {
            Username = "ToDeleteAudit",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Viewer
        };
        var created = await _subject.CreateAsync(user, _adminUserId).ConfigureAwait(false);

        // Act
        await _subject.DeleteAsync(created.Id, _adminUserId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(2));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Delete"));
        }
    }

    [Test]
    public async Task PatchAsync_CreatesAuditRecord_WhenPatchSucceeds()
    {
        // Arrange
        var user = new User
        {
            Username = "ToPatchAudit",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Viewer
        };
        var created = await _subject.CreateAsync(user, _adminUserId).ConfigureAwait(false);

        var patch = new UserPatch { Id = created.Id, Enabled = new() { Value = false } };

        // Act
        await _subject.PatchAsync(patch, _adminUserId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(2));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Patch"));
        }
    }

    [Test]
    public async Task QueryAsync_SortsUsers_WhenCamelCasePropertyNameIsSet()
    {
        // Arrange
        await _subject.CreateAsync(
            new User
            {
                Username = "Charlie",
                ProviderType = "local",
                Enabled = true,
                Role = Role.Viewer
            },
            _adminUserId).ConfigureAwait(false);
        await _subject.CreateAsync(
            new User
            {
                Username = "Alpha",
                ProviderType = "local",
                Enabled = true,
                Role = Role.Viewer
            },
            _adminUserId).ConfigureAwait(false);

        var sort = new SortParameter { PropertyName = "username", Direction = SortDirection.Ascending };

        // Act
        var result = await _subject.QueryAsync(new UserQuery(),
            new() { PageNumber = 1, PageSize = 100 },
            sort).ConfigureAwait(false);

        // Assert
        var usernames = result.Items.Select(u => u.Username).ToList();
        Assert.That(usernames, Is.Ordered.Ascending);
    }

    [Test]
    public async Task SetPasswordAsync_ClearsLockout_WhenUserIsLockedOut()
    {
        // Arrange
        var user = new User
        {
            Username = "LockedUser",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Viewer,
            PasswordHash = "old-hash",
            PasswordSalt = "old-salt",
            FailedLoginAttempts = 5,
            LockedUntil = DateTime.UtcNow.AddMinutes(15)
        };
        var created = await _subject.CreateAsync(user, _adminUserId).ConfigureAwait(false);

        // Act
        var updated = await _subject
            .SetPasswordAsync(created.Id, "new-hash", "new-salt", _adminUserId)
            .ConfigureAwait(false);

        // Assert
        var reloaded = await _subject.GetByIdAsync(created.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated, Is.True);
            Assert.That(reloaded, Is.Not.Null);
            Assert.That(reloaded, Has.Property(nameof(User.PasswordHash)).EqualTo("new-hash"));
            Assert.That(reloaded, Has.Property(nameof(User.PasswordSalt)).EqualTo("new-salt"));
            Assert.That(reloaded, Has.Property(nameof(User.FailedLoginAttempts)).EqualTo(0));
            Assert.That(reloaded, Has.Property(nameof(User.LockedUntil)).Null);
            Assert.That(reloaded, Has.Property(nameof(User.ModifiedAt)).Not.Null);
            Assert.That(reloaded, Has.Property(nameof(User.ModifiedByUserId)).EqualTo(_adminUserId));
        }
    }

    [Test]
    public async Task SetPasswordAsync_SetsPassword_WhenUserIsNotLockedOut()
    {
        // Arrange
        var user = new User
        {
            Username = "UnlockedUser",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Viewer,
            PasswordHash = "old-hash",
            PasswordSalt = "old-salt",
            FailedLoginAttempts = 0,
            LockedUntil = null
        };
        var created = await _subject.CreateAsync(user, _adminUserId).ConfigureAwait(false);

        // Act
        var updated = await _subject
            .SetPasswordAsync(created.Id, "new-hash", "new-salt", _adminUserId)
            .ConfigureAwait(false);

        // Assert
        var reloaded = await _subject.GetByIdAsync(created.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated, Is.True);
            Assert.That(reloaded, Is.Not.Null);
            Assert.That(reloaded, Has.Property(nameof(User.Username)).EqualTo("UnlockedUser"));
            Assert.That(reloaded, Has.Property(nameof(User.Enabled)).True);
            Assert.That(reloaded, Has.Property(nameof(User.Role)).EqualTo(Role.Viewer));
            Assert.That(reloaded, Has.Property(nameof(User.PasswordHash)).EqualTo("new-hash"));
            Assert.That(reloaded, Has.Property(nameof(User.PasswordSalt)).EqualTo("new-salt"));
            Assert.That(reloaded, Has.Property(nameof(User.FailedLoginAttempts)).EqualTo(0));
            Assert.That(reloaded, Has.Property(nameof(User.LockedUntil)).Null);
            Assert.That(reloaded, Has.Property(nameof(User.ModifiedAt)).Not.Null);
            Assert.That(reloaded, Has.Property(nameof(User.ModifiedByUserId)).EqualTo(_adminUserId));
        }
    }

    [Test]
    public async Task SetPasswordAsync_CreatesAuditRecord_WhenPasswordChanges()
    {
        // Arrange
        var user = new User
        {
            Username = "PasswordAuditUser",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Viewer,
            PasswordHash = "old-hash",
            PasswordSalt = "old-salt"
        };
        var created = await _subject.CreateAsync(user, _adminUserId).ConfigureAwait(false);

        // Act
        await _subject
            .SetPasswordAsync(created.Id, "new-hash", "new-salt", _adminUserId)
            .ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(2));
            Assert.That(
                auditRecords,
                Has.One
                    .With.Property(nameof(AuditRecord.Operation)).EqualTo("SetPassword")
                    .And.Property(nameof(AuditRecord.Category)).EqualTo("Security")
                    .And.Property(nameof(AuditRecord.EntityType)).EqualTo("User")
                    .And.Property(nameof(AuditRecord.UserId)).EqualTo(_adminUserId));
        }
    }

    [Test]
    public async Task SetPasswordAsync_DeletesRefreshTokens_WhenPasswordChanges()
    {
        // Arrange
        var changedUser = new User
        {
            Username = "PasswordSessionUser",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Viewer,
            PasswordHash = "old-hash",
            PasswordSalt = "old-salt"
        };
        var otherUser = new User
        {
            Username = "OtherPasswordSessionUser",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Viewer,
            PasswordHash = "other-hash",
            PasswordSalt = "other-salt"
        };
        var changed = await _subject.CreateAsync(changedUser, _adminUserId).ConfigureAwait(false);
        var other = await _subject.CreateAsync(otherUser, _adminUserId).ConfigureAwait(false);
        _context.RefreshTokens.AddRange(
            new RefreshToken
            {
                UserId = changed.Id,
                TokenHash = "stale-session-hash",
                AccessTokenJti = "11111111-1111-1111-1111-111111111111",
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                CreatedAt = DateTime.UtcNow
            },
            new RefreshToken
            {
                UserId = other.Id,
                TokenHash = "other-session-hash",
                AccessTokenJti = "22222222-2222-2222-2222-222222222222",
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                CreatedAt = DateTime.UtcNow
            });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject
            .SetPasswordAsync(changed.Id, "new-hash", "new-salt", _adminUserId)
            .ConfigureAwait(false);

        // Assert
        var refreshTokens = await _context.RefreshTokens.AsNoTracking().ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(refreshTokens, Has.Count.EqualTo(1));
            Assert.That(refreshTokens, Has.None.With.Property(nameof(RefreshToken.UserId)).EqualTo(changed.Id));
            Assert.That(refreshTokens, Has.One.With.Property(nameof(RefreshToken.UserId)).EqualTo(other.Id));
            Assert.That(
                refreshTokens,
                Has.One.With.Property(nameof(RefreshToken.TokenHash)).EqualTo("other-session-hash"));
        }
    }

    [Test]
    public async Task SetPasswordAsync_ReturnsFalse_WhenNotExists()
    {
        // Act
        var result = await _subject
            .SetPasswordAsync(Guid.NewGuid(), "new-hash", "new-salt", _adminUserId)
            .ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task RecordFailedLoginAsync_LocksTheUser_WhenTheStoredCounterReachesTheLimit()
    {
        // Arrange
        var user = new User
        {
            Username = "ConcurrentFailuresUser",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Viewer,
            FailedLoginAttempts = 0
        };
        var created = await _subject.CreateAsync(user, _adminUserId).ConfigureAwait(false);
        var lockedUntil = new DateTime(2026, 9, 8, 12, 15, 0, DateTimeKind.Utc);

        // Act
        var first = await _subject.RecordFailedLoginAsync(created.Id, 3, lockedUntil).ConfigureAwait(false);
        var second = await _subject.RecordFailedLoginAsync(created.Id, 3, lockedUntil).ConfigureAwait(false);
        var third = await _subject.RecordFailedLoginAsync(created.Id, 3, lockedUntil).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.EqualTo(new FailedLoginResult(1, null)));
            Assert.That(second, Is.EqualTo(new FailedLoginResult(2, null)));
            Assert.That(
                third,
                Is.EqualTo(new FailedLoginResult(3, new DateTime(2026, 9, 8, 12, 15, 0, DateTimeKind.Utc))));
        }
    }

    [Test]
    public async Task ResetLockoutAsync_ClearsAttemptsAndExpiryAndAudits_WhenAnAdministratorActs()
    {
        // Arrange
        var user = new User
        {
            Username = "AdminUnlockedUser",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Viewer,
            FailedLoginAttempts = 5,
            LockedUntil = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        var created = await _subject.CreateAsync(user, _adminUserId).ConfigureAwait(false);

        // Act
        var result = await _subject.ResetLockoutAsync(created.Id, _adminUserId).ConfigureAwait(false);

        // Assert
        var reloaded = await _subject.GetByIdAsync(created.Id).ConfigureAwait(false);
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(reloaded, Has.Property(nameof(User.FailedLoginAttempts)).EqualTo(0));
            Assert.That(reloaded, Has.Property(nameof(User.LockedUntil)).Null);
            Assert.That(reloaded, Has.Property(nameof(User.Enabled)).True);
            Assert.That(auditRecords, Has.Count.EqualTo(2));
            Assert.That(
                auditRecords,
                Has.One
                    .With.Property(nameof(AuditRecord.Operation)).EqualTo("ResetLockout")
                    .And.Property(nameof(AuditRecord.Category)).EqualTo("Security")
                    .And.Property(nameof(AuditRecord.EntityType)).EqualTo("User")
                    .And.Property(nameof(AuditRecord.UserId)).EqualTo(_adminUserId));
        }
    }

    [Test]
    public async Task ResetLockoutAsync_ClearsAttemptsAndExpiryAndAudits_WhenTheLockoutLapsed()
    {
        // Arrange
        var user = new User
        {
            Username = "LapsedLockoutUser",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Viewer,
            FailedLoginAttempts = 5,
            LockedUntil = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        var created = await _subject.CreateAsync(user, _adminUserId).ConfigureAwait(false);

        // Act
        var result = await _subject.ResetLockoutAsync(created.Id).ConfigureAwait(false);

        // Assert
        var reloaded = await _subject.GetByIdAsync(created.Id).ConfigureAwait(false);
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(reloaded, Has.Property(nameof(User.FailedLoginAttempts)).EqualTo(0));
            Assert.That(reloaded, Has.Property(nameof(User.LockedUntil)).Null);
            Assert.That(auditRecords, Has.Count.EqualTo(2));
            Assert.That(
                auditRecords,
                Has.Exactly(1)
                    .With.Property(nameof(AuditRecord.Operation)).EqualTo("ResetLockout")
                    .And.Property(nameof(AuditRecord.UserId)).Null);
        }
    }

    [Test]
    public async Task ResetLockoutAsync_ReturnsFalse_WhenNotExists()
    {
        // Act
        var result = await _subject.ResetLockoutAsync(Guid.NewGuid(), _adminUserId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }

    [TestCase("passwordHash")]
    [TestCase("passwordSalt")]
    public async Task QueryAsync_UsesDefaultSort_WhenSecretColumnIsRequested(string propertyName)
    {
        // Arrange
        _context.Users.AddRange(
            new User
            {
                Username = "Alpha",
                ProviderType = "local",
                Role = Role.Viewer,
                PasswordHash = "z",
                PasswordSalt = "z"
            },
            new User
            {
                Username = "Zulu",
                ProviderType = "local",
                Role = Role.Viewer,
                PasswordHash = "a",
                PasswordSalt = "a"
            });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var page = await _subject.QueryAsync(new UserQuery { Role = Role.Viewer },
            new() { PageNumber = 1, PageSize = 10 },
            new() { PropertyName = propertyName, Direction = SortDirection.Ascending }).ConfigureAwait(false);

        // Assert
        Assert.That(page.Items.Select(item => item.Username), Is.EqualTo(["Alpha", "Zulu"]));
    }
}
