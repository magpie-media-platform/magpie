using System;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models.Data;
using Magpie.Models.Enums;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

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

        var loggerFactory = NullLoggerFactory.Instance;

        var auditRecordRepository = new AuditRecordRepository(
            _context,
            Mock.Of<Sdk.Services.IEventBus>(),
            loggerFactory.CreateLogger<AuditRecordRepository>(),
            new TransactionManager(_context, NullLogger<TransactionManager>.Instance));

        var transactionManager = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);

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
    public async Task CreateAsync_CreatesRefreshToken_WhenRequestIsValid()
    {
        // Arrange
        var token = new RefreshToken
        {
            UserId = _userId,
            TokenHash = "hash123",
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };

        // Act
        var result = await _subject.CreateAsync(token).ConfigureAwait(false);

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
    public async Task CreateAsync_CreatesAuditRecord_WhenTokenIsCreated()
    {
        // Arrange
        var token = new RefreshToken
        {
            UserId = _userId,
            TokenHash = "audithash",
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };

        // Act
        await _subject.CreateAsync(token).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.GetByHashAsync("uniquehash123").ConfigureAwait(false);

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
        var result = await _subject.GetByHashAsync("nonexistenthash").ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetByHashAsync_ReturnsRefreshToken_WhenHashLooksLikeGuid()
    {
        // Arrange
        var token = new RefreshToken
        {
            UserId = _userId,
            TokenHash = "11111111-1111-1111-1111-111111111111",
            AccessTokenJti = "22222222-2222-2222-2222-222222222222",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow
        };
        _context.RefreshTokens.Add(token);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.GetByHashAsync("11111111-1111-1111-1111-111111111111")
            .ConfigureAwait(false);

        // Assert
        Assert.That(
            result,
            Has.Property(nameof(RefreshToken.TokenHash)).EqualTo("11111111-1111-1111-1111-111111111111"));
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.DeleteAsync("deletehash").ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);

        var deleted = await _subject.GetByHashAsync("deletehash").ConfigureAwait(false);
        Assert.That(deleted, Is.Null);
    }

    [Test]
    public async Task DeleteAsync_ReturnsFalse_WhenNotExists()
    {
        // Act
        var result = await _subject.DeleteAsync("nonexistenthash").ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task DeleteAsync_CreatesAuditRecord_WhenTokenExists()
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject.DeleteAsync("deleteaudithash").ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Delete"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("RefreshToken"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task ConsumeAsync_ReturnsAndDeletesToken_WhenTokenIsValid()
    {
        // Arrange
        var token = new RefreshToken
        {
            UserId = _userId,
            TokenHash = "consumehash",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow
        };
        _context.RefreshTokens.Add(token);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.ConsumeAsync("consumehash", DateTime.UtcNow).ConfigureAwait(false);

        // Assert
        Assert.That(result, Has.Property(nameof(result.UserId)).EqualTo(_userId));
        var deleted = await _subject.GetByHashAsync("consumehash").ConfigureAwait(false);
        Assert.That(deleted, Is.Null);
    }

    [Test]
    public async Task ConsumeAsync_ReturnsNull_WhenTokenExpired()
    {
        // Arrange
        var token = new RefreshToken
        {
            UserId = _userId,
            TokenHash = "expiredconsumehash",
            ExpiresAt = DateTime.UtcNow.AddMinutes(-1),
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        };
        _context.RefreshTokens.Add(token);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.ConsumeAsync("expiredconsumehash", DateTime.UtcNow).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Null);
        var stored = await _subject.GetByHashAsync("expiredconsumehash").ConfigureAwait(false);
        Assert.That(stored, Is.Not.Null);
    }

    [Test]
    public async Task ConsumeAsync_ReturnsNull_WhenTokenAlreadyConsumed()
    {
        // Arrange
        var token = new RefreshToken
        {
            UserId = _userId,
            TokenHash = "consumeoncehash",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow
        };
        _context.RefreshTokens.Add(token);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var first = await _subject.ConsumeAsync("consumeoncehash", DateTime.UtcNow).ConfigureAwait(false);
        var second = await _subject.ConsumeAsync("consumeoncehash", DateTime.UtcNow).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.Null);
        }
    }

    [Test]
    public async Task ConsumeAsync_LeavesSecondSession_WhenFirstSessionIsConsumed()
    {
        // Arrange
        _context.RefreshTokens.AddRange(
            new RefreshToken
            {
                UserId = _userId,
                TokenHash = "first-session-hash",
                AccessTokenJti = "11111111-1111-1111-1111-111111111111",
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                CreatedAt = DateTime.UtcNow
            },
            new RefreshToken
            {
                UserId = _userId,
                TokenHash = "second-session-hash",
                AccessTokenJti = "22222222-2222-2222-2222-222222222222",
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                CreatedAt = DateTime.UtcNow
            });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject.ConsumeAsync("first-session-hash", DateTime.UtcNow).ConfigureAwait(false);

        // Assert
        var first = await _subject.GetByAccessTokenJtiAsync("11111111-1111-1111-1111-111111111111")
            .ConfigureAwait(false);
        var second = await _subject.GetByAccessTokenJtiAsync("22222222-2222-2222-2222-222222222222")
            .ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.Null);
            Assert.That(second, Is.Not.Null);
        }
    }
}
