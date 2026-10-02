using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Auth;
using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
using Magpie.Core;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models.Data;
using Magpie.Models.Security;
using Magpie.Sdk.Auth;
using Magpie.Sdk.Security;
using Magpie.Sdk.Services;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core;

[TestFixture]
public sealed class AuthRenewalRecoveryTests
{
    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private ServiceProvider _services = null!;
    private IAuthService _auth = null!;
    private IRefreshTokenRepository _repository = null!;
    private Mock<IDataProtectionService> _protector = null!;
    private FakeTimeProvider _time = null!;
    private User _user = null!;
    private IssuedAccessToken _access = null!;
    private IssuedToken _refresh = null!;

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
    public async Task SetUp()
    {
        _time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero));
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync().ConfigureAwait(false);
        _template.BackupDatabase(connection);
        _context = new MagpieDbContext(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite(connection, contextOwnsConnection: true).Options);
        _user = new User { Username = "renewal-test", ProviderType = "local", Enabled = true };
        _context.Users.Add(_user);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var transactions = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);
        _repository = new RefreshTokenRepository(_context, Mock.Of<IAuditRecordRepository>(), transactions,
            NullLogger<RefreshTokenRepository>.Instance, _time);
        var users = new Mock<IUserService>();
        users.Setup(service => service.GetAuthenticationUserAsync(_user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_user);
        var keys = new Mock<IKeyStore>();
        keys.Setup(service => service.KeyExistsAsync("jwt-hmac", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        keys.Setup(service => service.ComputeHmacAsync("jwt-hmac", It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, byte[] value, CancellationToken _) =>
                HMACSHA256.HashData("isolated-renewal-test-key-32-bytes"u8, value));
        _protector = new Mock<IDataProtectionService>();
        _protector.Setup(service => service.Protect(It.IsAny<string>()))
            .Returns((string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value)));
        _protector.Setup(service => service.Unprotect(It.IsAny<string>()))
            .Returns((string value) => Encoding.UTF8.GetString(Convert.FromBase64String(value)));
        var services = new ServiceCollection();
        services.AddMagpieAuth(new ConfigurationBuilder().Build());
        services.RemoveAll<ICredentialAuthenticationProvider>();
        services.AddSingleton(users.Object);
        services.AddSingleton(keys.Object);
        services.AddSingleton(_protector.Object);
        services.AddSingleton(Mock.Of<IEventBus>());
        services.AddSingleton<ITransactionManager>(transactions);
        services.AddSingleton(_repository);
        services.AddSingleton<TimeProvider>(_time);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        _services = services.BuildServiceProvider();
        _auth = _services.GetRequiredService<IAuthService>();
        var tokens = _services.GetRequiredService<ITokenService>();
        _access = await tokens.GenerateAccessTokenAsync(_user).ConfigureAwait(false);
        _refresh = await tokens.GenerateRefreshTokenAsync(_user.Id, _access.Jti).ConfigureAwait(false);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _services.DisposeAsync().ConfigureAwait(false);
        await _context.DisposeAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task RefreshAsync_ReturnsTheExactCommittedTokens_WhenTheResponseIsRetriedWithTheDeliveredCookie()
    {
        // Arrange
        var committed = await _auth.RefreshAsync(_refresh.Token, _access.Token).ConfigureAwait(false);
        _time.Advance(TimeSpan.FromMinutes(2));

        // Act
        var recovered = await _auth.RefreshAsync(committed!.RefreshToken, _access.Token).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(recovered, Is.EqualTo(committed));
            Assert.That(await _context.RefreshTokens.CountAsync().ConfigureAwait(false), Is.EqualTo(1));
            Assert.That(recovered!.AccessTokenExpiresAt,
                Is.EqualTo(new DateTime(2026, 9, 21, 12, 15, 0, DateTimeKind.Utc)));
            Assert.That(recovered.RefreshTokenExpiresAt,
                Is.EqualTo(new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc)));
        }
    }

    [Test]
    public async Task RefreshAsync_ReturnsTheExactCommittedTokens_WhenTheResponseIsRetriedWithTheOriginalCookie()
    {
        // Arrange
        var committed = await _auth.RefreshAsync(_refresh.Token, _access.Token).ConfigureAwait(false);
        _time.Advance(TimeSpan.FromMinutes(2));

        // Act
        var recovered = await _auth.RefreshAsync(_refresh.Token, _access.Token).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(recovered, Is.EqualTo(committed));
            Assert.That(await _context.RefreshTokens.CountAsync().ConfigureAwait(false), Is.EqualTo(1));
            Assert.That(recovered!.AccessTokenExpiresAt,
                Is.EqualTo(new DateTime(2026, 9, 21, 12, 15, 0, DateTimeKind.Utc)));
            Assert.That(recovered.RefreshTokenExpiresAt,
                Is.EqualTo(new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc)));
        }
    }

    [Test]
    public async Task RefreshAsync_RejectsTheConsumedCookie_WhenTheProofIsMissingOrDifferent()
    {
        // Arrange
        await _auth.RefreshAsync(_refresh.Token, _access.Token).ConfigureAwait(false);

        // Act
        var legacy = await _auth.RefreshAsync(_refresh.Token).ConfigureAwait(false);
        var different = await _auth.RefreshAsync(_refresh.Token, "different-proof").ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(legacy, Is.Null);
            Assert.That(different, Is.Null);
            Assert.That(await _context.RefreshTokens.CountAsync().ConfigureAwait(false), Is.EqualTo(1));
        }
    }

    [Test]
    public async Task LogoutAsync_RevokesTheCommittedReplacement_WhenItsResponseWasLost()
    {
        // Arrange
        var committed = await _auth.RefreshAsync(_refresh.Token, _access.Token).ConfigureAwait(false);

        // Act
        await _auth.LogoutAsync(_refresh.Token, _access.Token).ConfigureAwait(false);
        var recovered = await _auth.RefreshAsync(_refresh.Token, _access.Token).ConfigureAwait(false);
        var rotated = await _auth.RefreshAsync(committed!.RefreshToken, committed.AccessToken).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(recovered, Is.Null);
            Assert.That(rotated, Is.Null);
            Assert.That(await _context.RefreshTokens.AnyAsync().ConfigureAwait(false), Is.False);
        }
    }

    [Test]
    public async Task LogoutAsync_KeepsTheOtherSession_WhenItsCookieAndTheOldProofDoNotMatch()
    {
        // Arrange
        var tokens = _services.GetRequiredService<ITokenService>();
        var otherAccess = await tokens.GenerateAccessTokenAsync(_user).ConfigureAwait(false);
        var otherRefresh = await tokens.GenerateRefreshTokenAsync(_user.Id, otherAccess.Jti).ConfigureAwait(false);

        // Act
        var revoked = await _auth.LogoutAsync(otherRefresh.Token, _access.Token).ConfigureAwait(false);

        // Assert
        var otherHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(otherRefresh.Token)));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(revoked, Is.False);
            Assert.That(
                await _context.RefreshTokens.AnyAsync(token => token.TokenHash == otherHash).ConfigureAwait(false),
                Is.True);
            Assert.That(await _context.RefreshTokens.CountAsync().ConfigureAwait(false), Is.EqualTo(2));
        }
    }

    [Test]
    public async Task RefreshAsync_RejectsRecovery_WhenTheUserSessionsAreRevoked()
    {
        // Arrange
        await _auth.RefreshAsync(_refresh.Token, _access.Token).ConfigureAwait(false);
        await _repository.DeleteByUserAsync(_user.Id).ConfigureAwait(false);

        // Act
        var result = await _auth.RefreshAsync(_refresh.Token, _access.Token).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task RefreshAsync_RejectsRecovery_WhenTheAccountIsDisabled()
    {
        // Arrange
        await _auth.RefreshAsync(_refresh.Token, _access.Token).ConfigureAwait(false);
        _user.Enabled = false;

        // Act
        var result = await _auth.RefreshAsync(_refresh.Token, _access.Token).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task RefreshAsync_RejectsRecovery_WhenTheAccountIsLocked()
    {
        // Arrange
        await _auth.RefreshAsync(_refresh.Token, _access.Token).ConfigureAwait(false);
        _user.Enabled = true;
        _user.LockedUntil = new DateTime(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);

        // Act
        var result = await _auth.RefreshAsync(_refresh.Token, _access.Token).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task RefreshAsync_RejectsRecovery_WhenTheOriginalCredentialHasExpired()
    {
        // Arrange
        _time.Advance(TimeSpan.FromDays(6));
        await _auth.RefreshAsync(_refresh.Token, _access.Token).ConfigureAwait(false);
        _time.Advance(TimeSpan.FromDays(1));

        // Act
        var result = await _auth.RefreshAsync(_refresh.Token, _access.Token).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Null);
            Assert.That(await _context.RefreshTokens.CountAsync().ConfigureAwait(false), Is.EqualTo(1));
        }
    }

    [Test]
    public async Task RefreshAsync_RejectsTheOldReceipt_WhenTheReplacementHasRotated()
    {
        // Arrange
        var committed = await _auth.RefreshAsync(_refresh.Token, _access.Token).ConfigureAwait(false);
        await _auth.RefreshAsync(committed!.RefreshToken, committed.AccessToken).ConfigureAwait(false);

        // Act
        var result = await _auth.RefreshAsync(_refresh.Token, _access.Token).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Null);
            Assert.That(await _context.RefreshTokens.CountAsync().ConfigureAwait(false), Is.EqualTo(1));
        }
    }

    [Test]
    public async Task RefreshAsync_RollsBackRotation_WhenTheReceiptCannotBeProtected()
    {
        // Arrange
        _protector.Setup(service => service.Protect(It.IsAny<string>()))
            .Throws(new InvalidOperationException("Temporary protection failure"));

        // Act
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _auth.RefreshAsync(_refresh.Token, _access.Token).ConfigureAwait(false));

        // Assert
        var refreshHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(_refresh.Token)));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                await _context.RefreshTokens.AnyAsync(token => token.TokenHash == refreshHash).ConfigureAwait(false),
                Is.True);
            Assert.That(await _context.RefreshTokens.CountAsync().ConfigureAwait(false), Is.EqualTo(1));
        }
    }
}
