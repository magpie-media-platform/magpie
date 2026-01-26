using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Auth;
using Magpie.Abstractions.Data;
using Magpie.Abstractions.Security;
using Magpie.Auth;
using Magpie.Core;
using Magpie.Core.Security;
using Magpie.Models.Result;
using Magpie.Models.Users;
using Magpie.Test.Shared;

using Microsoft.Extensions.Logging;

using Moq;

using NUnit.Framework;

using static Magpie.Abstractions.Auth.ITokenService;
using static Magpie.Abstractions.Data.IRefreshTokenRepository;

namespace Magpie.Test.Auth;

[TestFixture]
public class TokenServiceTests
{
    private Mock<IRefreshTokenRepository> _refreshTokenRepository = null!;
    private Mock<IUserManager> _userManager = null!;
    private InMemoryKeyStore _keyStore = null!;
    private ITokenService _subject = null!;
    private TokenServiceOptions _options = null!;
    private ILoggerFactory _loggerFactory = null!;

    [SetUp]
    public void SetUp()
    {
        _refreshTokenRepository = new Mock<IRefreshTokenRepository>();
        _userManager = new Mock<IUserManager>();
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
            _userManager.Object,
            _refreshTokenRepository.Object,
            _keyStore,
            _loggerFactory.CreateLogger<TokenService>());
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
        var user = CreateTestUser();
        SetupUserManagerForUser(user);

        // Act
        var result = await _subject.GenerateAccessTokenAsync(user).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<AccessToken>>());
        var accessToken = ((SuccessResult<AccessToken>)result).Value;

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(accessToken.Token);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(accessToken.Token, Is.Not.Null.And.Not.Empty);
            Assert.That(accessToken.ExpiresAt, Is.GreaterThan(DateTimeOffset.UtcNow));
            Assert.That(jwt.Claims, Has.One.With.Property(nameof(Claim.Type))
                .EqualTo(JwtRegisteredClaimNames.NameId));
            Assert.That(jwt.Claims, Has.One.With.Property(nameof(Claim.Type))
                .EqualTo(JwtRegisteredClaimNames.UniqueName));
            Assert.That(jwt.Claims, Has.One.With.Property(nameof(Claim.Type)).EqualTo("role"));
        }
    }

    [Test]
    public async Task GenerateAccessTokenAsync_IncludesAuthProviderIdClaim_WhenPresent()
    {
        // Arrange
        var authProviderId = Guid.NewGuid();
        var user = CreateTestUser(authProviderId: authProviderId);
        SetupUserManagerForUser(user);

        // Act
        var result = await _subject.GenerateAccessTokenAsync(user).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<AccessToken>>());
        var accessToken = ((SuccessResult<AccessToken>)result).Value;

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(accessToken.Token);

        Assert.That(jwt.Claims, Has.One
            .With.Property("Type").EqualTo(MagpieClaims.AuthProviderId)
            .And.Property(nameof(System.Security.Claims.Claim.Value)).EqualTo(authProviderId.ToString()));
    }

    [Test]
    public async Task GenerateAccessTokenAsync_SetsCorrectIssuerAndAudience()
    {
        // Arrange
        var user = CreateTestUser();
        SetupUserManagerForUser(user);

        // Act
        var result = await _subject.GenerateAccessTokenAsync(user).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<AccessToken>>());
        var accessToken = ((SuccessResult<AccessToken>)result).Value;

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(accessToken.Token);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(jwt.Issuer, Is.EqualTo(_options.Issuer));
            Assert.That(jwt.Audiences, Has.One.EqualTo(_options.Audience));
        }
    }

    [Test]
    public async Task GenerateRefreshTokenAsync_CallsRepository()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _refreshTokenRepository
            .Setup(r => r.CreateAsync(userId, It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new RefreshTokenInfo(userId, DateTimeOffset.UtcNow.AddDays(7))));

        // Act
        var result = await _subject.GenerateRefreshTokenAsync(userId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<string>>());
            Assert.That(((SuccessResult<string>)result).Value, Is.Not.Null.And.Not.Empty);
        }

        _refreshTokenRepository.Verify(
            r => r.CreateAsync(userId, It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task GenerateRefreshTokenAsync_SetsCorrectExpiration()
    {
        // Arrange
        var userId = Guid.NewGuid();
        DateTimeOffset capturedExpiration = default;

        _refreshTokenRepository
            .Setup(r => r.CreateAsync(userId, It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, DateTimeOffset, CancellationToken>((_, _, exp, _) => capturedExpiration = exp)
            .ReturnsAsync(Result.Success(new RefreshTokenInfo(userId, DateTimeOffset.UtcNow.AddDays(7))));

        // Act
        await _subject.GenerateRefreshTokenAsync(userId).ConfigureAwait(false);

        // Assert
        Assert.That(capturedExpiration, Is.EqualTo(DateTimeOffset.UtcNow.Add(_options.RefreshTokenLifetime))
            .Within(TimeSpan.FromSeconds(5)));
    }

    [Test]
    public async Task ValidateRefreshTokenAsync_ReturnsUserId_ForValidToken()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var token = "test-token";

        _refreshTokenRepository
            .Setup(r => r.GetByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<RefreshTokenInfo?>(
                new RefreshTokenInfo(userId, DateTimeOffset.UtcNow.AddDays(1))));

        // Act
        var result = await _subject.ValidateRefreshTokenAsync(token).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<Guid>>());
            Assert.That(((SuccessResult<Guid>)result).Value, Is.EqualTo(userId));
        }
    }

    [Test]
    public async Task ValidateRefreshTokenAsync_ReturnsFailure_ForExpiredToken()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var token = "test-token";

        _refreshTokenRepository
            .Setup(r => r.GetByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<RefreshTokenInfo?>(
                new RefreshTokenInfo(userId, DateTimeOffset.UtcNow.AddMinutes(-1))));

        // Act
        var result = await _subject.ValidateRefreshTokenAsync(token).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.InstanceOf<FailureResult<Guid>>()
            .With.Property(nameof(FailureResult<>.ErrorCode)).EqualTo(ErrorCodes.RefreshTokenExpired));
    }

    [Test]
    public async Task ValidateRefreshTokenAsync_ReturnsFailure_ForNonexistentToken()
    {
        // Arrange
        _refreshTokenRepository
            .Setup(r => r.GetByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<RefreshTokenInfo?>(null));

        // Act
        var result = await _subject.ValidateRefreshTokenAsync("nonexistent-token").ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.InstanceOf<FailureResult<Guid>>()
            .With.Property(nameof(FailureResult<>.ErrorCode)).EqualTo(ErrorCodes.RefreshTokenNotFound));
    }

    [Test]
    public async Task RevokeRefreshTokenAsync_DeletesToken()
    {
        // Arrange
        var token = "test-token";
        _refreshTokenRepository
            .Setup(r => r.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        // Act
        var result = await _subject.RevokeRefreshTokenAsync(token).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<Unit>>());
        _refreshTokenRepository.Verify(
            r => r.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ValidateAccessTokenAsync_ReturnsClaimsPrincipal_ForValidToken()
    {
        // Arrange
        var user = CreateTestUser();
        SetupUserManagerForUser(user);
        var generateResult = await _subject.GenerateAccessTokenAsync(user).ConfigureAwait(false);
        Assert.That(generateResult, Is.InstanceOf<SuccessResult<AccessToken>>());
        var accessToken = ((SuccessResult<AccessToken>)generateResult).Value;

        // Act
        var result = await _subject.ValidateAccessTokenAsync(accessToken.Token).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<ClaimsPrincipal>>());
            var principal = ((SuccessResult<ClaimsPrincipal>)result).Value;
            Assert.That(principal.Claims, Has.One.With.Property("Type").EqualTo(ClaimTypes.NameIdentifier));
        }
    }

    [Test]
    public async Task ValidateAccessTokenAsync_ReturnsFailure_ForInvalidToken()
    {
        // Act
        var result = await _subject.ValidateAccessTokenAsync("invalid-token").ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.InstanceOf<FailureResult<ClaimsPrincipal>>()
            .With.Property(nameof(FailureResult<>.ErrorCode)).EqualTo(ErrorCodes.TokenValidationError));
    }

    [Test]
    public async Task ValidateAccessTokenAsync_ReturnsFailure_ForExpiredToken()
    {
        // Arrange
        var shortLivedOptions = new TokenServiceOptions
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            AccessTokenLifetime = TimeSpan.FromMilliseconds(1),
            RefreshTokenLifetime = _options.RefreshTokenLifetime
        };

        var shortLivedService = new TokenService(
            shortLivedOptions,
            _userManager.Object,
            _refreshTokenRepository.Object,
            _keyStore,
            _loggerFactory.CreateLogger<TokenService>());

        var user = CreateTestUser();
        SetupUserManagerForUser(user);
        var generateResult = await shortLivedService.GenerateAccessTokenAsync(user).ConfigureAwait(false);
        Assert.That(generateResult, Is.InstanceOf<SuccessResult<AccessToken>>());
        var accessToken = ((SuccessResult<AccessToken>)generateResult).Value;

        await Task.Delay(100).ConfigureAwait(false);

        // Act
        var result = await shortLivedService.ValidateAccessTokenAsync(accessToken.Token).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.InstanceOf<FailureResult<ClaimsPrincipal>>()
            .With.Property(nameof(FailureResult<>.ErrorCode)).EqualTo(ErrorCodes.TokenValidationError));
    }

    private static User CreateTestUser(Guid? id = null, Guid? authProviderId = null)
    {
        return new(
            Id: id ?? Guid.NewGuid(),
            Username: "testuser",
            Email: "test@example.com",
            AuthProviderId: authProviderId,
            ExternalId: null,
            Enabled: true,
            Role: Role.Viewer,
            ProviderData: null,
            LastLoginAt: null,
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);
    }

    private void SetupUserManagerForUser(User user)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
        ]);

        if (user.AuthProviderId.HasValue)
        {
            identity.AddClaim(new Claim(MagpieClaims.AuthProviderId, user.AuthProviderId.Value.ToString()));
        }

        var principal = new ClaimsPrincipal(identity);

        _userManager
            .Setup(m => m.CreatePrincipalAsync(user, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(principal));
    }
}
