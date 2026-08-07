using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Auth;
using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
using Magpie.Auth;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Security;

using Microsoft.IdentityModel.Tokens;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Auth;

[TestFixture]
public class TokenServiceTests
{
    private Mock<IRefreshTokenRepository> _refreshTokenRepository = null!;
    private Mock<IKeyStore> _keyStore = null!;
    private ITokenService _subject = null!;
    private TokenServiceOptions _options = null!;
    private byte[] _jwtHmacKey = null!;

    [SetUp]
    public void SetUp()
    {
        _refreshTokenRepository = new Mock<IRefreshTokenRepository>();
        _jwtHmacKey = "this-is-a-test-hmac-key-32-bytes"u8.ToArray();
        _keyStore = new Mock<IKeyStore>(MockBehavior.Strict);
        _keyStore
            .Setup(m => m.KeyExistsAsync("jwt-hmac", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _keyStore
            .Setup(m => m.ComputeHmacAsync("jwt-hmac", It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, byte[] data, CancellationToken _) =>
            {
                using var hmac = new HMACSHA256(_jwtHmacKey);
                return hmac.ComputeHash(data);
            });
        _keyStore
            .Setup(m => m.VerifyHmacAsync(
                "jwt-hmac",
                It.IsAny<byte[]>(),
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, byte[] data, byte[] signature, CancellationToken _) =>
            {
                using var hmac = new HMACSHA256(_jwtHmacKey);
                return CryptographicOperations.FixedTimeEquals(hmac.ComputeHash(data), signature);
            });

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
            _keyStore.Object,
            new KeyStoreHmacCryptoProviderFactory(),
            TimeProvider.System);
    }

    [Test]
    public async Task GenerateAccessTokenAsync_ReturnsValidJwt_WhenUserIsValid()
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
            Assert.That(
                jwt,
                Has.Property(nameof(jwt.Claims)).With.One.With.Property(nameof(Claim.Type))
                    .EqualTo(JwtRegisteredClaimNames.NameId));
            Assert.That(
                jwt,
                Has.Property(nameof(jwt.Claims)).With.One.With.Property(nameof(Claim.Type))
                    .EqualTo(JwtRegisteredClaimNames.UniqueName));
            Assert.That(
                jwt,
                Has.Property(nameof(jwt.Claims)).With.One.With.Property(nameof(Claim.Type))
                    .EqualTo("role"));
        }
    }

    [Test]
    public async Task GenerateAccessTokenAsync_ReturnsHmacSignedJwt_WhenUserIsValid()
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
        var handler = new JwtSecurityTokenHandler();
        var result = await handler.ValidateTokenAsync(
            accessToken.Token,
            new()
            {
                ValidateIssuer = true,
                ValidIssuer = _options.Issuer,
                ValidateAudience = true,
                ValidAudience = _options.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(_jwtHmacKey),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            }).ConfigureAwait(false);

        // Assert
        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public async Task GenerateAccessTokenAsync_CreatesHmacKey_WhenMissing()
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
        using var hmac = new HMACSHA256(_jwtHmacKey);
        var keyStore = new Mock<IKeyStore>(MockBehavior.Strict);
        keyStore
            .Setup(m => m.KeyExistsAsync("jwt-hmac", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        keyStore
            .Setup(m => m.CreateHmacKeyAsync("jwt-hmac", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        keyStore
            .Setup(m => m.ComputeHmacAsync("jwt-hmac", It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, byte[] data, CancellationToken _) => hmac.ComputeHash(data));
        var subject = new TokenService(
            _options,
            _refreshTokenRepository.Object,
            keyStore.Object,
            new KeyStoreHmacCryptoProviderFactory(),
            TimeProvider.System);

        // Act
        var accessToken = await subject.GenerateAccessTokenAsync(user, default).ConfigureAwait(false);

        // Assert
        Assert.That(accessToken.Token, Is.Not.Empty);
        keyStore.Verify(m => m.CreateHmacKeyAsync("jwt-hmac", It.IsAny<CancellationToken>()), Times.Once);
        keyStore.Verify(m => m.CreateSigningKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task TryValidateAccessTokenAsync_UsesHmacBoundary_WhenTokenIsGeneratedByService()
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
        using var hmac = new HMACSHA256(_jwtHmacKey);
        var keyStore = new Mock<IKeyStore>(MockBehavior.Strict);
        keyStore
            .Setup(m => m.KeyExistsAsync("jwt-hmac", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        keyStore
            .Setup(m => m.ComputeHmacAsync("jwt-hmac", It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, byte[] data, CancellationToken _) => hmac.ComputeHash(data));
        keyStore
            .Setup(m => m.VerifyHmacAsync(
                "jwt-hmac",
                It.IsAny<byte[]>(),
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, byte[] data, byte[] signature, CancellationToken _) =>
                CryptographicOperations.FixedTimeEquals(hmac.ComputeHash(data), signature));
        var subject = new TokenService(
            _options,
            _refreshTokenRepository.Object,
            keyStore.Object,
            new KeyStoreHmacCryptoProviderFactory(),
            TimeProvider.System);
        var accessToken = await subject.GenerateAccessTokenAsync(user, default).ConfigureAwait(false);

        // Act
        var result = await subject.TryValidateAccessTokenAsync(accessToken.Token, default)
            .ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Not.Null);
        keyStore.Verify(
            m => m.ComputeHmacAsync("jwt-hmac", It.IsAny<byte[]>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
        keyStore.Verify(
            m => m.VerifyHmacAsync(
                "jwt-hmac",
                It.IsAny<byte[]>(),
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        keyStore.Verify(m => m.GetPublicKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        keyStore.Verify(
            m => m.SignAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        keyStore.Verify(
            m => m.VerifySignatureAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
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

        Assert.That(
            jwt,
            Has.Property(nameof(jwt.Claims)).With.One
                .With.Property("Type").EqualTo(MagpieClaims.AuthProviderType)
                .And.Property(nameof(Claim.Value)).EqualTo("local"));
    }

    [Test]
    public async Task GenerateAccessTokenAsync_SetsConfiguredIssuerAndAudience_WhenUserIsValid()
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
    public async Task GenerateRefreshTokenAsync_CreatesRepositoryRecord_WhenUserIdIsProvided()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _refreshTokenRepository
            .Setup(m => m.CreateAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RefreshToken t, CancellationToken _) => t);

        // Act
        var result = await _subject.GenerateRefreshTokenAsync(userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Token, Is.Not.Null.And.Not.Empty);
            Assert.That(result.ExpiresAt, Is.GreaterThan(DateTime.UtcNow));
        }

        _refreshTokenRepository.Verify(
            r => r.CreateAsync(It.Is<RefreshToken>(t => t.UserId == userId), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task GenerateRefreshTokenAsync_SetsConfiguredExpiration_WhenUserIdIsProvided()
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
            Assert.That(
                capturedToken,
                Has.Property(nameof(capturedToken.ExpiresAt))
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
    public async Task TryConsumeRefreshTokenAsync_ReturnsUserId_WhenRepositoryConsumesToken()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _refreshTokenRepository
            .Setup(m => m.ConsumeAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefreshToken
            {
                UserId = userId,
                TokenHash = "hash",
                ExpiresAt = DateTime.UtcNow.AddDays(1),
                CreatedAt = DateTime.UtcNow
            });

        // Act
        var result = await _subject.TryConsumeRefreshTokenAsync("test-token").ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.EqualTo(userId));
        _refreshTokenRepository.Verify(
            r => r.ConsumeAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task TryConsumeRefreshTokenAsync_ReturnsNull_WhenRepositoryDoesNotConsumeToken()
    {
        // Arrange
        _refreshTokenRepository
            .Setup(m => m.ConsumeAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(RefreshToken?));

        // Act
        var result = await _subject.TryConsumeRefreshTokenAsync("test-token").ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task RevokeRefreshTokenAsync_DeletesRepositoryToken_WhenTokenIsProvided()
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
        Assert.That(
            result,
            Is.Not.Null
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
