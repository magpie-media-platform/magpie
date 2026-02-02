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
public class RefreshTokenRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IRefreshTokenRepository _subject = null!;
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

        _subject = new RefreshTokenRepository(
            _context,
            auditRecordRepository,
            transactionManager,
            loggerFactory.CreateLogger<RefreshTokenRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [Test]
    public async Task CreateAsync_CreatesRefreshToken()
    {
        // Arrange
        var token = new RefreshToken
        {
            UserId = _userId,
            TokenHash = "hash123",
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };

        // Act
        var result = await _subject.CreateAsync(token);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Id)).Not.EqualTo(Guid.Empty));
            Assert.That(result, Has.Property(nameof(result.UserId)).EqualTo(_userId));
            Assert.That(result, Has.Property(nameof(result.TokenHash)).EqualTo("hash123"));
            Assert.That(result, Has.Property(nameof(result.CreatedAt)).Not.EqualTo(default(DateTime)));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesAuditRecord()
    {
        // Arrange
        var token = new RefreshToken
        {
            UserId = _userId,
            TokenHash = "audithash",
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };

        // Act
        await _subject.CreateAsync(token);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Create"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("RefreshToken"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task GetByHashAsync_ReturnsRefreshToken_WhenExists()
    {
        // Arrange
        var token = new RefreshToken
        {
            UserId = _userId,
            TokenHash = "uniquehash123",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow
        };
        _context.RefreshTokens.Add(token);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.GetByHashAsync("uniquehash123");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.UserId)).EqualTo(_userId));
            Assert.That(result, Has.Property(nameof(result.TokenHash)).EqualTo("uniquehash123"));
        }
    }

    [Test]
    public async Task GetByHashAsync_ReturnsNull_WhenNotExists()
    {
        // Act
        var result = await _subject.GetByHashAsync("nonexistenthash");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task DeleteAsync_DeletesRefreshToken_WhenExists()
    {
        // Arrange
        var token = new RefreshToken
        {
            UserId = _userId,
            TokenHash = "deletehash",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow
        };
        _context.RefreshTokens.Add(token);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.DeleteAsync("deletehash");

        // Assert
        Assert.That(result, Is.True);

        var deleted = await _subject.GetByHashAsync("deletehash");
        Assert.That(deleted, Is.Null);
    }

    [Test]
    public async Task DeleteAsync_ReturnsFalse_WhenNotExists()
    {
        // Act
        var result = await _subject.DeleteAsync("nonexistenthash");

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task DeleteAsync_CreatesAuditRecord()
    {
        // Arrange
        var token = new RefreshToken
        {
            UserId = _userId,
            TokenHash = "deleteaudithash",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow
        };
        _context.RefreshTokens.Add(token);
        await _context.SaveChangesAsync();

        // Act
        await _subject.DeleteAsync("deleteaudithash");

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Delete"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("RefreshToken"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task DeleteByUserIdAsync_DeletesAllUserTokens()
    {
        // Arrange
        var token1 = new RefreshToken
        {
            UserId = _userId,
            TokenHash = "hash1",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow
        };
        var token2 = new RefreshToken
        {
            UserId = _userId,
            TokenHash = "hash2",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow
        };
        _context.RefreshTokens.AddRange(token1, token2);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.DeleteByUserIdAsync(_userId);

        // Assert
        Assert.That(result, Is.EqualTo(2));

        var remaining = await _context.RefreshTokens.Where(rt => rt.UserId == _userId).ToListAsync();
        Assert.That(remaining, Is.Empty);
    }

    [Test]
    public async Task DeleteByUserIdAsync_ReturnsZero_WhenNoTokens()
    {
        // Act
        var result = await _subject.DeleteByUserIdAsync(_userId);

        // Assert
        Assert.That(result, Is.EqualTo(0));
    }

    [Test]
    public async Task DeleteByUserIdAsync_DoesNotDeleteOtherUserTokens()
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

        var token1 = new RefreshToken
        {
            UserId = _userId,
            TokenHash = "hash1",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow
        };
        var token2 = new RefreshToken
        {
            UserId = otherUser.Id,
            TokenHash = "hash2",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow
        };
        _context.RefreshTokens.AddRange(token1, token2);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.DeleteByUserIdAsync(_userId);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo(1));

            var remaining = await _context.RefreshTokens.ToListAsync();
            Assert.That(remaining, Has.Exactly(1).Items);
            Assert.That(remaining, Has.One.With.Property(nameof(RefreshToken.UserId)).EqualTo(otherUser.Id));
        }
    }

    [Test]
    public async Task DeleteByUserIdAsync_CreatesAuditRecord()
    {
        // Arrange
        var token = new RefreshToken
        {
            UserId = _userId,
            TokenHash = "hash1",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow
        };
        _context.RefreshTokens.Add(token);
        await _context.SaveChangesAsync();

        // Act
        await _subject.DeleteByUserIdAsync(_userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("DeleteAll"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("RefreshToken"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task DeleteExpiredAsync_DeletesExpiredTokens()
    {
        // Arrange
        var expiredToken = new RefreshToken
        {
            UserId = _userId,
            TokenHash = "expiredhash",
            ExpiresAt = DateTime.UtcNow.AddDays(-1),
            CreatedAt = DateTime.UtcNow.AddDays(-8)
        };
        var validToken = new RefreshToken
        {
            UserId = _userId,
            TokenHash = "validhash",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow
        };
        _context.RefreshTokens.AddRange(expiredToken, validToken);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.DeleteExpiredAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo(1));

            var remaining = await _context.RefreshTokens.ToListAsync();
            Assert.That(remaining, Has.Exactly(1).Items);
            Assert.That(remaining, Has.One.With.Property(nameof(RefreshToken.TokenHash)).EqualTo("validhash"));
        }
    }

    [Test]
    public async Task DeleteExpiredAsync_ReturnsZero_WhenNoExpiredTokens()
    {
        // Arrange
        var validToken = new RefreshToken
        {
            UserId = _userId,
            TokenHash = "validhash",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow
        };
        _context.RefreshTokens.Add(validToken);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.DeleteExpiredAsync();

        // Assert
        Assert.That(result, Is.EqualTo(0));
    }

    [Test]
    public async Task DeleteExpiredAsync_CreatesAuditRecord_WhenTokensDeleted()
    {
        // Arrange
        var expiredToken = new RefreshToken
        {
            UserId = _userId,
            TokenHash = "expiredhash",
            ExpiresAt = DateTime.UtcNow.AddDays(-1),
            CreatedAt = DateTime.UtcNow.AddDays(-8)
        };
        _context.RefreshTokens.Add(expiredToken);
        await _context.SaveChangesAsync();

        // Act
        await _subject.DeleteExpiredAsync();

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("DeleteExpired"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("RefreshToken"));
        }
    }

    [Test]
    public async Task DeleteExpiredAsync_DoesNotCreateAuditRecord_WhenNoTokensDeleted()
    {
        // Act
        await _subject.DeleteExpiredAsync();

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        Assert.That(auditRecords, Is.Empty);
    }
}
