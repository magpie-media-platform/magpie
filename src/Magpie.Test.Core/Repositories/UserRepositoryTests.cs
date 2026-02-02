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
using Magpie.Models.Patch;
using Magpie.Test.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

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

        var loggerFactory = LoggerFactory.Create(builder => builder.AddTestLogger());

        var auditRecordRepository = new AuditRecordRepository(
            _context,
            loggerFactory.CreateLogger<AuditRecordRepository>());

        var transactionManager = new TransactionManager(_context);

        _subject = new UserRepository(
            _context,
            auditRecordRepository,
            transactionManager,
            loggerFactory.CreateLogger<UserRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [Test]
    public async Task CreateAsync_CreatesUser()
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
        var result = await _subject.CreateAsync(user, _adminUserId);

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
        var created = await _subject.CreateAsync(user, _adminUserId);

        // Act
        var result = await _subject.GetByIdAsync(created.Id);

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
        var result = await _subject.GetByIdAsync(Guid.NewGuid());

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ListAllAsync_ReturnsAllUsers()
    {
        // Arrange
        await _subject.CreateAsync(new User
        {
            Username = "User1",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Viewer
        }, _adminUserId);
        await _subject.CreateAsync(new User
        {
            Username = "User2",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Manager
        }, _adminUserId);

        // Act
        var result = await _subject.ListAllAsync();

        // Assert
        Assert.That(result, Has.Exactly(4).Items);
    }

    [Test]
    public async Task GetUserAsync_ByUsername_ReturnsUser_WhenExists()
    {
        // Arrange
        await _subject.CreateAsync(new User
        {
            Username = "UniqueUser",
            ProviderType = "local",
            Email = "unique@example.com",
            Enabled = true,
            Role = Role.Viewer
        }, _adminUserId);

        // Act
        var result = await _subject.GetUserAsync("UniqueUser");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Username)).EqualTo("UniqueUser"));
            Assert.That(result, Has.Property(nameof(result.Email)).EqualTo("unique@example.com"));
        }
    }

    [Test]
    public async Task GetUserAsync_ByUsername_ReturnsNull_WhenNotExists()
    {
        // Act
        var result = await _subject.GetUserAsync("NonExistentUser");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetUserAsync_ByProviderTypeAndExternalId_ReturnsUser_WhenExists()
    {
        // Arrange
        await _subject.CreateAsync(new User
        {
            Username = "ExternalUser",
            ProviderType = "oauth",
            ExternalId = "external-123",
            Enabled = true,
            Role = Role.Viewer
        }, _adminUserId);

        // Act
        var result = await _subject.GetUserAsync("oauth", "external-123");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Username)).EqualTo("ExternalUser"));
            Assert.That(result, Has.Property(nameof(result.ProviderType)).EqualTo("oauth"));
            Assert.That(result, Has.Property(nameof(result.ExternalId)).EqualTo("external-123"));
        }
    }

    [Test]
    public async Task GetUserAsync_ByProviderTypeAndExternalId_ReturnsNull_WhenNotExists()
    {
        // Act
        var result = await _subject.GetUserAsync("oauth", "nonexistent");

        // Assert
        Assert.That(result, Is.Null);
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
        var created = await _subject.CreateAsync(user, _adminUserId);

        created.Username = "UpdatedUser";
        created.Email = "updated@example.com";
        created.Enabled = false;
        created.Role = Role.Manager;

        // Act
        var result = await _subject.UpdateAsync(created, _adminUserId);

        // Assert
        Assert.That(result, Is.True);

        var updated = await _subject.GetByIdAsync(created.Id);
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
        var result = await _subject.UpdateAsync(user, _adminUserId);

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
        var created = await _subject.CreateAsync(user, _adminUserId);

        // Act
        var result = await _subject.DeleteAsync(created.Id, _adminUserId);

        // Assert
        var deleted = await _subject.GetByIdAsync(created.Id);
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
        var result = await _subject.DeleteAsync(Guid.NewGuid(), _adminUserId);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task PatchAsync_PatchesEnabledField()
    {
        // Arrange
        var user = new User
        {
            Username = "ToPatch",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Viewer
        };
        var created = await _subject.CreateAsync(user, _adminUserId);

        var patch = new UserPatch(created.Id, Enabled: new ScalarPatch<bool>(false));

        // Act
        var result = await _subject.PatchAsync(patch, _adminUserId);

        // Assert
        var patched = await _subject.GetByIdAsync(created.Id);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(patched, Has.Property(nameof(patched.Enabled)).False);
            Assert.That(patched, Has.Property(nameof(patched.ModifiedByUserId)).EqualTo(_adminUserId));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesRoleField()
    {
        // Arrange
        var user = new User
        {
            Username = "ToPatchRole",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Viewer
        };
        var created = await _subject.CreateAsync(user, _adminUserId);

        var patch = new UserPatch(created.Id, Role: new ScalarPatch<Role>(Role.Admin));

        // Act
        var result = await _subject.PatchAsync(patch, _adminUserId);

        // Assert
        Assert.That(result, Is.True);

        var patched = await _subject.GetByIdAsync(created.Id);
        Assert.That(patched, Has.Property(nameof(patched.Role)).EqualTo(Role.Admin));
    }

    [Test]
    public async Task PatchAsync_ReturnsFalse_WhenUserNotExists()
    {
        // Arrange
        var patch = new UserPatch(Guid.NewGuid(), Enabled: new ScalarPatch<bool>(false));

        // Act
        var result = await _subject.PatchAsync(patch, _adminUserId);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task QueryAsync_ReturnsAllUsers_WhenNoFilter()
    {
        // Arrange
        await _subject.CreateAsync(new User
        {
            Username = "QueryUser1",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Viewer
        }, _adminUserId);
        await _subject.CreateAsync(new User
        {
            Username = "QueryUser2",
            ProviderType = "local",
            Enabled = false,
            Role = Role.Manager
        }, _adminUserId);

        // Act
        var result = await _subject.QueryAsync(new UserQuery(), null, null);

        // Assert
        Assert.That(result, Has.Property(nameof(result.Items)).With.Exactly(4).Items);
    }

    [Test]
    public async Task QueryAsync_FiltersByEnabled()
    {
        // Arrange
        await _subject.CreateAsync(new User
        {
            Username = "EnabledUser",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Viewer
        }, _adminUserId);
        await _subject.CreateAsync(new User
        {
            Username = "DisabledUser",
            ProviderType = "local",
            Enabled = false,
            Role = Role.Viewer
        }, _adminUserId);

        // Act
        var result = await _subject.QueryAsync(new UserQuery(Enabled: false), null, null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).With.Exactly(1).Items);
            Assert.That(result, Has.Property(nameof(result.Items))
                .With.One.Property(nameof(User.Username)).EqualTo("DisabledUser"));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByRole()
    {
        // Arrange
        await _subject.CreateAsync(new User
        {
            Username = "ManagerUser",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Manager
        }, _adminUserId);
        await _subject.CreateAsync(new User
        {
            Username = "ViewerUser",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Viewer
        }, _adminUserId);

        // Act
        var result = await _subject.QueryAsync(new UserQuery(Role: Role.Manager), null, null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Count.EqualTo(1));
            Assert.That(result, Has.Property(nameof(result.Items))
                .With.One.Property(nameof(User.Username)).EqualTo("ManagerUser"));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByProviderType()
    {
        // Arrange
        await _subject.CreateAsync(new User
        {
            Username = "LocalUser",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Viewer
        }, _adminUserId);
        await _subject.CreateAsync(new User
        {
            Username = "OAuthUser",
            ProviderType = "oauth",
            Enabled = true,
            Role = Role.Viewer
        }, _adminUserId);

        // Act
        var result = await _subject.QueryAsync(new UserQuery(ProviderType: "oauth"), null, null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).With.Exactly(1).Items);
            Assert.That(result, Has.Property(nameof(result.Items))
                .With.One.Property(nameof(User.Username)).EqualTo("OAuthUser"));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesAuditRecord()
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
        await _subject.CreateAsync(user, _adminUserId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Create"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("User"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_adminUserId));
        }
    }

    [Test]
    public async Task UpdateAsync_CreatesAuditRecord()
    {
        // Arrange
        var user = new User
        {
            Username = "ToUpdateAudit",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Viewer
        };
        var created = await _subject.CreateAsync(user, _adminUserId);

        created.Username = "Updated";

        // Act
        await _subject.UpdateAsync(created, _adminUserId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        Assert.That(auditRecords, Has.Count.EqualTo(2));
        Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Update"));
    }

    [Test]
    public async Task DeleteAsync_CreatesAuditRecord()
    {
        // Arrange
        var user = new User
        {
            Username = "ToDeleteAudit",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Viewer
        };
        var created = await _subject.CreateAsync(user, _adminUserId);

        // Act
        await _subject.DeleteAsync(created.Id, _adminUserId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(2));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Delete"));
        }
    }

    [Test]
    public async Task PatchAsync_CreatesAuditRecord()
    {
        // Arrange
        var user = new User
        {
            Username = "ToPatchAudit",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Viewer
        };
        var created = await _subject.CreateAsync(user, _adminUserId);

        var patch = new UserPatch(created.Id, Enabled: new ScalarPatch<bool>(false));

        // Act
        await _subject.PatchAsync(patch, _adminUserId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(2));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Patch"));
        }
    }

}
