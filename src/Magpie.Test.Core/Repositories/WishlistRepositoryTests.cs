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
using Magpie.Test.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public class WishlistRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IWishlistRepository _subject = null!;
    private Guid _userId;
    private Guid _contentFilterId;

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

        var contentFilter = new ContentFilter
        {
            Name = "TestFilter",
            Criteria = new ContentFilterCriteria(),
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentFilters.Add(contentFilter);

        _context.SaveChanges();

        _userId = user.Id;
        _contentFilterId = contentFilter.Id;

        var loggerFactory = LoggerFactory.Create(builder => builder.AddTestLogger());

        var auditRecordRepository = new AuditRecordRepository(
            _context,
            loggerFactory.CreateLogger<AuditRecordRepository>());

        var transactionManager = new TransactionManager(_context);

        _subject = new WishlistRepository(
            _context,
            auditRecordRepository,
            transactionManager,
            loggerFactory.CreateLogger<WishlistRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [Test]
    public async Task GetByIdAsync_ReturnsWishlist_WhenExists()
    {
        // Arrange
        var wishlist = new Wishlist
        {
            Name = "TestWishlist",
            ContentFilterId = _contentFilterId,
            OwnerUserId = _userId,
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.Wishlists.Add(wishlist);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.GetByIdAsync(wishlist.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("TestWishlist"));
            Assert.That(result, Has.Property(nameof(result.OwnerUserId)).EqualTo(_userId));
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
    public async Task ListAllAsync_ReturnsAllWishlists()
    {
        // Arrange
        var wishlist1 = new Wishlist
        {
            Name = "Wishlist1",
            ContentFilterId = _contentFilterId,
            OwnerUserId = _userId,
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        var wishlist2 = new Wishlist
        {
            Name = "Wishlist2",
            ContentFilterId = _contentFilterId,
            OwnerUserId = _userId,
            Enabled = false,
            CreatedAt = DateTime.UtcNow
        };
        _context.Wishlists.AddRange(wishlist1, wishlist2);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.ListAllAsync(default);

        // Assert
        Assert.That(result, Has.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_ReturnsEmptyList_WhenNoWishlists()
    {
        // Act
        var result = await _subject.ListAllAsync(default);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetByUserIdAsync_ReturnsUserWishlists()
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

        var wishlist1 = new Wishlist
        {
            Name = "UserWishlist1",
            ContentFilterId = _contentFilterId,
            OwnerUserId = _userId,
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        var wishlist2 = new Wishlist
        {
            Name = "UserWishlist2",
            ContentFilterId = _contentFilterId,
            OwnerUserId = _userId,
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        var wishlist3 = new Wishlist
        {
            Name = "OtherWishlist",
            ContentFilterId = _contentFilterId,
            OwnerUserId = otherUser.Id,
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.Wishlists.AddRange(wishlist1, wishlist2, wishlist3);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.GetByUserIdAsync(_userId);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Exactly(2).Items);
            Assert.That(result, Has.All.Matches<Wishlist>(w => w.OwnerUserId == _userId));
        }
    }

    [Test]
    public async Task GetMonitoredAsync_ReturnsEnabledWishlists()
    {
        // Arrange
        var enabled1 = new Wishlist
        {
            Name = "EnabledWishlist1",
            ContentFilterId = _contentFilterId,
            OwnerUserId = _userId,
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        var enabled2 = new Wishlist
        {
            Name = "EnabledWishlist2",
            ContentFilterId = _contentFilterId,
            OwnerUserId = _userId,
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        var disabled = new Wishlist
        {
            Name = "DisabledWishlist",
            ContentFilterId = _contentFilterId,
            OwnerUserId = _userId,
            Enabled = false,
            CreatedAt = DateTime.UtcNow
        };
        _context.Wishlists.AddRange(enabled1, enabled2, disabled);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.GetMonitoredAsync(default);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Exactly(2).Items);
            Assert.That(result, Has.All.Matches<Wishlist>(w => w.Enabled));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesWishlist()
    {
        // Arrange
        var wishlist = new Wishlist
        {
            Name = "NewWishlist",
            ContentFilterId = _contentFilterId,
            OwnerUserId = _userId,
            Enabled = true
        };

        // Act
        var result = await _subject.CreateAsync(wishlist, _userId);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Id)).Not.EqualTo(Guid.Empty));
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("NewWishlist"));
            Assert.That(result, Has.Property(nameof(result.CreatedByUserId)).EqualTo(_userId));
            Assert.That(result, Has.Property(nameof(result.CreatedAt)).Not.EqualTo(default(DateTime)));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesAuditRecord()
    {
        // Arrange
        var wishlist = new Wishlist
        {
            Name = "AuditedWishlist",
            ContentFilterId = _contentFilterId,
            OwnerUserId = _userId,
            Enabled = true
        };

        // Act
        await _subject.CreateAsync(wishlist, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Create"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("Wishlist"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesWishlist_WhenExists()
    {
        // Arrange
        var wishlist = new Wishlist
        {
            Name = "OriginalWishlist",
            ContentFilterId = _contentFilterId,
            OwnerUserId = _userId,
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.Wishlists.Add(wishlist);
        await _context.SaveChangesAsync();

        wishlist.Name = "UpdatedWishlist";
        wishlist.Enabled = false;

        // Act
        var result = await _subject.UpdateAsync(wishlist, _userId);

        // Assert
        Assert.That(result, Is.True);

        var updated = await _subject.GetByIdAsync(wishlist.Id);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated, Has.Property(nameof(updated.Name)).EqualTo("UpdatedWishlist"));
            Assert.That(updated, Has.Property(nameof(updated.Enabled)).False);
            Assert.That(updated, Has.Property(nameof(updated.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(updated, Has.Property(nameof(updated.ModifiedAt)).Not.Null);
        }
    }

    [Test]
    public async Task UpdateAsync_ReturnsFalse_WhenNotExists()
    {
        // Arrange
        var wishlist = new Wishlist
        {
            Id = Guid.NewGuid(),
            Name = "NonExistent",
            ContentFilterId = _contentFilterId,
            OwnerUserId = _userId,
            Enabled = true
        };

        // Act
        var result = await _subject.UpdateAsync(wishlist, _userId);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task UpdateAsync_CreatesAuditRecord()
    {
        // Arrange
        var wishlist = new Wishlist
        {
            Name = "ToUpdate",
            ContentFilterId = _contentFilterId,
            OwnerUserId = _userId,
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.Wishlists.Add(wishlist);
        await _context.SaveChangesAsync();

        wishlist.Name = "Updated";

        // Act
        await _subject.UpdateAsync(wishlist, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Update"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("Wishlist"));
        }
    }

    [Test]
    public async Task DeleteAsync_DeletesWishlist_WhenExists()
    {
        // Arrange
        var wishlist = new Wishlist
        {
            Name = "ToDelete",
            ContentFilterId = _contentFilterId,
            OwnerUserId = _userId,
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.Wishlists.Add(wishlist);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.DeleteAsync(wishlist.Id, _userId);

        // Assert
        Assert.That(result, Is.True);

        var deleted = await _subject.GetByIdAsync(wishlist.Id);
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
        var wishlist = new Wishlist
        {
            Name = "DeleteAudit",
            ContentFilterId = _contentFilterId,
            OwnerUserId = _userId,
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.Wishlists.Add(wishlist);
        await _context.SaveChangesAsync();

        // Act
        await _subject.DeleteAsync(wishlist.Id, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Delete"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("Wishlist"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesNameField()
    {
        // Arrange
        var wishlist = new Wishlist
        {
            Name = "ToPatch",
            ContentFilterId = _contentFilterId,
            OwnerUserId = _userId,
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.Wishlists.Add(wishlist);
        await _context.SaveChangesAsync();

        var patch = new WishlistPatch { Id = wishlist.Id, Name = new() { Value = "PatchedName" } };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.True);

        var patched = await _subject.GetByIdAsync(wishlist.Id);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(patched, Has.Property(nameof(patched.Name)).EqualTo("PatchedName"));
            Assert.That(patched, Has.Property(nameof(patched.ModifiedByUserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task PatchAsync_ReturnsFalse_WhenNotExists()
    {
        // Arrange
        var patch = new WishlistPatch { Id = Guid.NewGuid(), Name = new() { Value = "Test" } };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task PatchAsync_CreatesAuditRecord()
    {
        // Arrange
        var wishlist = new Wishlist
        {
            Name = "ToPatchAudit",
            ContentFilterId = _contentFilterId,
            OwnerUserId = _userId,
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.Wishlists.Add(wishlist);
        await _context.SaveChangesAsync();

        var patch = new WishlistPatch { Id = wishlist.Id, Name = new() { Value = "Patched" } };

        // Act
        await _subject.PatchAsync(patch, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Patch"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("Wishlist"));
        }
    }
}
