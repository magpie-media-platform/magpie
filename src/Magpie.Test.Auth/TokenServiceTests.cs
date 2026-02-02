using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Auth;
using Magpie.Abstractions.Repositories;
using Magpie.Auth;
using Magpie.Core.Security;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Test.Shared;

using Microsoft.Extensions.Logging;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Auth;

[TestFixture]
public class TokenServiceTests
{
    private Mock<IRefreshTokenRepository> _refreshTokenRepository = null!;
    private InMemoryKeyStore _keyStore = null!;
    private ITokenService _subject = null!;
    private TokenServiceOptions _options = null!;
    private ILoggerFactory _loggerFactory = null!;

    [SetUp]
    public void SetUp()
    {
        _refreshTokenRepository = new Mock<IRefreshTokenRepository>();
        _loggerFactory = LoggerFactory.Create(b => b.AddTestLogger());

        _keyStore = new InMemoryKeyStore(_loggerFactory.CreateLogger<InMemoryKeyStore>());
        _keyStore.LoadSigningKey("jwt-signing", "this-is-a-test-signing-key-32byt"u8.ToArray());

        _options = new TokenServiceOptions
        {
            Issuer = "magpie-test",
            Audience = "magpie-test-audience",
            AccessTokenLifetime = TimeSpan.FromMinutes(15),
            RefreshTokenLifetime = TimeSpan.FromDays(7)
        };

        _subject = new TokenService(
            _options,
            _refreshTokenRepository.Object,
            _keyStore);
    }

    [TearDown]
    public void TearDown()
    {
        _loggerFactory?.Dispose();
    }

    [Test]
    public async Task GenerateAccessTokenAsync_ReturnsValidJwt()
    {
        // Arrange
        var user = new User
        {
            Id = Guid.NewGuid(),
            ProviderType = "local",
            Username = "testuser",
            Email = "test@example.com",
            Enabled = true,
            Role = Role.Viewer,
            CreatedAt = DateTime.UtcNow,
        };

        // Act
        var accessToken = await _subject.GenerateAccessTokenAsync(user).ConfigureAwait(false);

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(accessToken.Token);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(accessToken, Has.Property(nameof(accessToken.ExpiresAt)).GreaterThan(DateTime.UtcNow));
            Assert.That(jwt, Has.Property(nameof(jwt.Claims)).With.One.With.Property(nameof(Claim.Type))
                .EqualTo(JwtRegisteredClaimNames.NameId));
            Assert.That(jwt, Has.Property(nameof(jwt.Claims)).With.One.With.Property(nameof(Claim.Type))
                .EqualTo(JwtRegisteredClaimNames.UniqueName));
            Assert.That(jwt, Has.Property(nameof(jwt.Claims)).With.One.With.Property(nameof(Claim.Type))
                .EqualTo("role"));
        }
    }

    [Test]
    public async Task GenerateAccessTokenAsync_IncludesAuthProviderTypeClaim_WhenPresent()
    {
        // Arrange
        var user = new User
        {
            Id = Guid.NewGuid(),
            ProviderType = "local",
            Username = "testuser",
            Email = "test@example.com",
            Enabled = true,
            Role = Role.Viewer,
            CreatedAt = DateTime.UtcNow,
        };

        // Act
        var accessToken = await _subject.GenerateAccessTokenAsync(user).ConfigureAwait(false);

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(accessToken.Token);

        Assert.That(jwt, Has.Property(nameof(jwt.Claims)).With.One
            .With.Property("Type").EqualTo(MagpieClaims.AuthProviderType)
            .And.Property(nameof(Claim.Value)).EqualTo("local"));
    }

    [Test]
    public async Task GenerateAccessTokenAsync_SetsCorrectIssuerAndAudience()
    {
        // Arrange
        var user = new User
        {
            Id = Guid.NewGuid(),
            ProviderType = "local",
            Username = "testuser",
            Email = "test@example.com",
            Enabled = true,
            Role = Role.Viewer,
            CreatedAt = DateTime.UtcNow,
        };

        // Act
        var accessToken = await _subject.GenerateAccessTokenAsync(user).ConfigureAwait(false);

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(accessToken.Token);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(jwt, Has.Property(nameof(jwt.Issuer)).EqualTo(_options.Issuer));
            Assert.That(jwt, Has.Property(nameof(jwt.Audiences)).With.One.EqualTo(_options.Audience));
        }
    }

    [Test]
    public async Task GenerateRefreshTokenAsync_CallsRepository()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _refreshTokenRepository
            .Setup(m => m.CreateAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RefreshToken t, CancellationToken _) => t);

        // Act
        var result = await _subject.GenerateRefreshTokenAsync(userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Not.Null.And.Not.Empty);
        _refreshTokenRepository.Verify(
            r => r.CreateAsync(It.Is<RefreshToken>(t => t.UserId == userId), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task GenerateRefreshTokenAsync_SetsCorrectExpiration()
    {
        // Arrange
        var userId = Guid.NewGuid();
        RefreshToken? capturedToken = null;

        _refreshTokenRepository
            .Setup(m => m.CreateAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()))
            .Callback<RefreshToken, CancellationToken>((t, _) => capturedToken = t)
            .ReturnsAsync((RefreshToken t, CancellationToken _) => t);

        // Act
        await _subject.GenerateRefreshTokenAsync(userId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(capturedToken, Is.Not.Null);
            Assert.That(capturedToken, Has.Property(nameof(capturedToken.ExpiresAt))
                .EqualTo(DateTime.UtcNow.Add(_options.RefreshTokenLifetime))
                .Within(TimeSpan.FromSeconds(1)));
        }
    }

    [Test]
    public async Task TryValidateRefreshTokenAsync_ReturnsUserId_ForValidToken()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var token = "test-token";

        _refreshTokenRepository
            .Setup(m => m.GetByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefreshToken
            {
                UserId = userId,
                TokenHash = "hash",
                ExpiresAt = DateTime.UtcNow.AddDays(1),
                CreatedAt = DateTime.UtcNow
            });

        // Act
        var result = await _subject.TryValidateRefreshTokenAsync(token).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.EqualTo(userId));
    }

    [Test]
    public async Task TryValidateRefreshTokenAsync_ReturnsNull_ForExpiredToken()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var token = "test-token";

        _refreshTokenRepository
            .Setup(m => m.GetByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefreshToken
            {
                UserId = userId,
                TokenHash = "hash",
                ExpiresAt = DateTime.UtcNow.AddMinutes(-1),
                CreatedAt = DateTime.UtcNow.AddDays(-1)
            });

        // Act
        var result = await _subject.TryValidateRefreshTokenAsync(token).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task TryValidateRefreshTokenAsync_ReturnsNull_ForNonexistentToken()
    {
        // Arrange
        _refreshTokenRepository
            .Setup(m => m.GetByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(RefreshToken?));

        // Act
        var result = await _subject.TryValidateRefreshTokenAsync("nonexistent-token").ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task RevokeRefreshTokenAsync_DeletesToken()
    {
        // Arrange
        var token = "test-token";
        _refreshTokenRepository
            .Setup(m => m.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _subject.RevokeRefreshTokenAsync(token).ConfigureAwait(false);

        // Assert
        _refreshTokenRepository.Verify(
            r => r.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task TryValidateAccessTokenAsync_ReturnsClaimsPrincipal_ForValidToken()
    {
        // Arrange
        var user = new User
        {
            Id = Guid.NewGuid(),
            ProviderType = "local",
            Username = "testuser",
            Email = "test@example.com",
            Enabled = true,
            Role = Role.Viewer,
            CreatedAt = DateTime.UtcNow,
        };
        var accessToken = await _subject.GenerateAccessTokenAsync(user).ConfigureAwait(false);

        // Act
        var result = await _subject.TryValidateAccessTokenAsync(accessToken.Token).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Not.Null
            .With.Property(nameof(ClaimsIdentity.Claims))
            .With.One.Property(nameof(Claim.Type)).EqualTo(ClaimTypes.NameIdentifier));
    }

    [Test]
    public async Task TryValidateAccessTokenAsync_ReturnsNull_ForInvalidToken()
    {
        // Act
        var result = await _subject.TryValidateAccessTokenAsync("invalid-token").ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Null);
    }
}
