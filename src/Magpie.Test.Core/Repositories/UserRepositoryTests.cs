using System;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

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

        var loggerFactory = NullLoggerFactory.Instance;

        var auditRecordRepository = new AuditRecordRepository(
            _context,
            Moq.Mock.Of<Sdk.Services.IEventBus>(),
            loggerFactory.CreateLogger<AuditRecordRepository>(),
            new TransactionManager(_context, NullLogger<TransactionManager>.Instance));

        var transactionManager = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);

        _subject = new UserRepository(
            _context,
            auditRecordRepository,
            transactionManager,
            Moq.Mock.Of<Sdk.Services.IEventBus>(),
            loggerFactory.CreateLogger<UserRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
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
}
