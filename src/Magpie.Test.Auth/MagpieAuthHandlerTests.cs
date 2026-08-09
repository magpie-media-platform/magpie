using System;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Auth;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Auth;
using Magpie.Sdk.Security;
using Magpie.Sdk.Services;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Auth;

[TestFixture]
public class MagpieAuthHandlerTests
{
    private Mock<IApiKeyService> _apiKeyService = null!;
    private Mock<IUserService> _userService = null!;
    private Mock<IAuthenticationProvider> _provider1 = null!;
    private Mock<IAuthenticationProvider> _provider2 = null!;
    private MagpieAuthenticationHandler _subject = null!;
    private DefaultHttpContext _httpContext = null!;
    private ILoggerFactory _loggerFactory = null!;

    [SetUp]
    public async Task SetUp()
    {
        _apiKeyService = new Mock<IApiKeyService>();
        _userService = new Mock<IUserService>();

        _provider1 = new Mock<IAuthenticationProvider>();
        _provider1.SetupGet(h => h.ProviderType).Returns("bearer");

        _provider2 = new Mock<IAuthenticationProvider>();
        _provider2.SetupGet(h => h.ProviderType).Returns("oidc");

        var options = new MagpieSchemeOptions();
        var optionsMonitor = new Mock<IOptionsMonitor<MagpieSchemeOptions>>();
        optionsMonitor
            .Setup(m => m.Get(MagpieAuthenticationHandler.SchemeName))
            .Returns(options);

        _loggerFactory = NullLoggerFactory.Instance;

        _subject = new MagpieAuthenticationHandler(
            optionsMonitor.Object,
            _loggerFactory,
            UrlEncoder.Default,
            _apiKeyService.Object,
            _userService.Object,
            [_provider1.Object, _provider2.Object]);

        _httpContext = new DefaultHttpContext();

        var scheme = new AuthenticationScheme(
            MagpieAuthenticationHandler.SchemeName,
            MagpieAuthenticationHandler.SchemeName,
            typeof(MagpieAuthenticationHandler));

        await _subject.InitializeAsync(scheme, _httpContext).ConfigureAwait(false);
    }

    [TearDown]
    public void TearDown()
    {
        _loggerFactory?.Dispose();
    }

    [Test]
    public async Task AuthenticateAsync_ReturnsNoResult_WhenNoCredentials()
    {
        // Arrange
        _provider1
            .Setup(m => m.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthenticationAttempt.None);
        _provider2
            .Setup(m => m.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthenticationAttempt.None);

        // Act
        var result = await _subject.AuthenticateAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Succeeded)).False);
            Assert.That(result, Has.Property(nameof(result.None)).True);
            Assert.That(result.Failure, Is.Null);
        }
    }

    [Test]
    public async Task AuthenticateAsync_ReturnsSuccessWithoutSessionLookup_WhenApiKeyValid()
    {
        // Arrange
        var refreshTokenRepository = new Mock<IRefreshTokenRepository>();
        var tokenService = new TokenService(
            new TokenServiceOptions(),
            refreshTokenRepository.Object,
            Mock.Of<IKeyStore>(),
            new KeyStoreHmacCryptoProviderFactory(),
            TimeProvider.System);
        var bearerProvider = new JwtBearerAuthenticationProvider(tokenService);
        var optionsMonitor = new Mock<IOptionsMonitor<MagpieSchemeOptions>>();
        optionsMonitor
            .Setup(m => m.Get(MagpieAuthenticationHandler.SchemeName))
            .Returns(new MagpieSchemeOptions());
        _subject = new MagpieAuthenticationHandler(
            optionsMonitor.Object,
            _loggerFactory,
            UrlEncoder.Default,
            _apiKeyService.Object,
            _userService.Object,
            [bearerProvider]);
        var scheme = new AuthenticationScheme(
            MagpieAuthenticationHandler.SchemeName,
            MagpieAuthenticationHandler.SchemeName,
            typeof(MagpieAuthenticationHandler));
        await _subject.InitializeAsync(scheme, _httpContext).ConfigureAwait(false);

        var userId = Guid.NewGuid();
        var apiKey = new ApiKey
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "Test API Key",
            KeyPrefix = "mgp_12345678",
            Enabled = true
        };
        var user = new User
        {
            Id = userId,
            Username = "testuser",
            Email = "test@example.com",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Viewer,
            CreatedAt = DateTime.UtcNow
        };

        _httpContext.Request.Headers["X-Api-Key"] = "valid-api-key";

        _apiKeyService
            .Setup(m => m.TryValidateAsync("valid-api-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(apiKey);

        _userService
            .Setup(m => m.GetAuthenticationUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        // Act
        var result = await _subject.AuthenticateAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Succeeded)).True);
            Assert.That(
                result.Principal!.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                Is.EqualTo(userId.ToString()));
        }

        refreshTokenRepository.Verify(
            repository => repository.GetByAccessTokenJtiAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task AuthenticateAsync_ReturnsFailure_WhenApiKeyInvalid()
    {
        // Arrange
        _httpContext.Request.Headers["X-Api-Key"] = "invalid-api-key";

        _apiKeyService
            .Setup(m => m.TryValidateAsync("invalid-api-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(ApiKey?));

        _provider1
            .Setup(m => m.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthenticationAttempt.None);
        _provider2
            .Setup(m => m.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthenticationAttempt.None);

        // Act
        var result = await _subject.AuthenticateAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Succeeded)).False);
            Assert.That(result, Has.Property(nameof(result.None)).False);
            Assert.That(
                result.Failure?.Message,
                Is.EqualTo("the api key credential was rejected: the key is not valid or its user cannot sign in"));
        }
    }

    [Test]
    public async Task AuthenticateAsync_ApiKeyTakesPrecedence_OverProviders()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var apiKey = new ApiKey
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "Test API Key",
            KeyPrefix = "mgp_12345678",
            Enabled = true
        };
        var user = new User
        {
            Id = userId,
            Username = "testuser",
            Email = "test@example.com",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Viewer,
            CreatedAt = DateTime.UtcNow
        };

        _httpContext.Request.Headers["X-Api-Key"] = "api-key";

        _apiKeyService
            .Setup(m => m.TryValidateAsync("api-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(apiKey);

        _userService
            .Setup(m => m.GetAuthenticationUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        // Act
        await _subject.AuthenticateAsync().ConfigureAwait(false);

        // Assert
        _provider1.Verify(
            h => h.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _provider2.Verify(
            h => h.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task AuthenticateAsync_ReturnsSuccess_WhenProviderSucceedsAndUserEnabled()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        var identity = new ClaimsIdentity(claims, MagpieAuthenticationHandler.SchemeName);
        var principal = new ClaimsPrincipal(identity);

        var user = new User
        {
            Id = userId,
            Username = "testuser",
            Email = "test@example.com",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Viewer,
            CreatedAt = DateTime.UtcNow
        };

        _provider1
            .Setup(m => m.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthenticationAttempt.Authenticated(principal));

        _userService
            .Setup(m => m.GetAuthenticationUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        // Act
        var result = await _subject.AuthenticateAsync().ConfigureAwait(false);

        // Assert
        Assert.That(result, Has.Property(nameof(result.Succeeded)).True);
    }

    [Test]
    public async Task AuthenticateAsync_ReturnsFailure_WhenUserDisabled()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        var identity = new ClaimsIdentity(claims, MagpieAuthenticationHandler.SchemeName);
        var principal = new ClaimsPrincipal(identity);

        var user = new User
        {
            Id = userId,
            Username = "testuser",
            Email = "test@example.com",
            ProviderType = "local",
            Enabled = false,
            Role = Role.Viewer,
            CreatedAt = DateTime.UtcNow
        };

        _provider1
            .Setup(m => m.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthenticationAttempt.Authenticated(principal));

        _userService
            .Setup(m => m.GetAuthenticationUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _provider2
            .Setup(m => m.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthenticationAttempt.None);

        // Act
        var result = await _subject.AuthenticateAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Succeeded)).False);
            Assert.That(result, Has.Property(nameof(result.None)).False);
            Assert.That(
                result.Failure?.Message,
                Is.EqualTo("the bearer credential was rejected: the authenticated user cannot sign in"));
        }
    }

    [Test]
    public async Task AuthenticateAsync_ReturnsFailure_WhenUserNotFound()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        var identity = new ClaimsIdentity(claims, MagpieAuthenticationHandler.SchemeName);
        var principal = new ClaimsPrincipal(identity);

        _provider1
            .Setup(m => m.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthenticationAttempt.Authenticated(principal));

        _userService
            .Setup(m => m.GetAuthenticationUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(User?));

        _provider2
            .Setup(m => m.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthenticationAttempt.None);

        // Act
        var result = await _subject.AuthenticateAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Succeeded)).False);
            Assert.That(result, Has.Property(nameof(result.None)).False);
            Assert.That(
                result.Failure?.Message,
                Is.EqualTo("the bearer credential was rejected: the authenticated user cannot sign in"));
        }
    }

    [Test]
    public async Task AuthenticateAsync_TriesNextProvider_WhenFirstPresentsNoCredentials()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        var identity = new ClaimsIdentity(claims, MagpieAuthenticationHandler.SchemeName);
        var principal = new ClaimsPrincipal(identity);

        var user = new User
        {
            Id = userId,
            Username = "testuser",
            Email = "test@example.com",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Viewer,
            CreatedAt = DateTime.UtcNow
        };

        _provider1
            .Setup(m => m.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthenticationAttempt.None);

        _provider2
            .Setup(m => m.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthenticationAttempt.Authenticated(principal));

        _userService
            .Setup(m => m.GetAuthenticationUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        // Act
        var result = await _subject.AuthenticateAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _provider1.Verify(
                h => h.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()),
                Times.Once);
            _provider2.Verify(
                h => h.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()),
                Times.Once);
            Assert.That(result, Has.Property(nameof(result.Succeeded)).True);
        }
    }

    [Test]
    public async Task AuthenticateAsync_ReturnsFailure_WhenApiKeyUserDisabled()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var apiKey = new ApiKey
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "Test API Key",
            KeyPrefix = "mgp_12345678",
            Enabled = true
        };
        var user = new User
        {
            Id = userId,
            Username = "testuser",
            Email = "test@example.com",
            ProviderType = "local",
            Enabled = false,
            Role = Role.Viewer,
            CreatedAt = DateTime.UtcNow
        };

        _httpContext.Request.Headers["X-Api-Key"] = "valid-api-key";

        _apiKeyService
            .Setup(m => m.TryValidateAsync("valid-api-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(apiKey);

        _userService
            .Setup(m => m.GetAuthenticationUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _provider1
            .Setup(m => m.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthenticationAttempt.None);
        _provider2
            .Setup(m => m.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthenticationAttempt.None);

        // Act
        var result = await _subject.AuthenticateAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Succeeded)).False);
            Assert.That(result, Has.Property(nameof(result.None)).False);
            Assert.That(
                result.Failure?.Message,
                Is.EqualTo("the api key credential was rejected: the key is not valid or its user cannot sign in"));
        }
    }

    [Test]
    public async Task AuthenticateAsync_ReturnsFailure_WhenApiKeyUserNotFound()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var apiKey = new ApiKey
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "Test API Key",
            KeyPrefix = "mgp_12345678",
            Enabled = true
        };

        _httpContext.Request.Headers["X-Api-Key"] = "valid-api-key";

        _apiKeyService
            .Setup(m => m.TryValidateAsync("valid-api-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(apiKey);

        _userService
            .Setup(m => m.GetAuthenticationUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(User?));

        _provider1
            .Setup(m => m.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthenticationAttempt.None);
        _provider2
            .Setup(m => m.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthenticationAttempt.None);

        // Act
        var result = await _subject.AuthenticateAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Succeeded)).False);
            Assert.That(result, Has.Property(nameof(result.None)).False);
            Assert.That(
                result.Failure?.Message,
                Is.EqualTo("the api key credential was rejected: the key is not valid or its user cannot sign in"));
        }
    }

    [Test]
    public async Task AuthenticateAsync_TriesNextProvider_WhenUserValidationFails()
    {
        // Arrange
        var user1Id = Guid.NewGuid();
        var user2Id = Guid.NewGuid();

        var claims1 = new[] { new Claim(ClaimTypes.NameIdentifier, user1Id.ToString()) };
        var identity1 = new ClaimsIdentity(claims1, MagpieAuthenticationHandler.SchemeName);
        var principal1 = new ClaimsPrincipal(identity1);

        var claims2 = new[] { new Claim(ClaimTypes.NameIdentifier, user2Id.ToString()) };
        var identity2 = new ClaimsIdentity(claims2, MagpieAuthenticationHandler.SchemeName);
        var principal2 = new ClaimsPrincipal(identity2);

        var user1 = new User
        {
            Id = user1Id,
            Username = "testuser",
            Email = "test@example.com",
            ProviderType = "local",
            Enabled = false,
            Role = Role.Viewer,
            CreatedAt = DateTime.UtcNow
        };
        var user2 = new User
        {
            Id = user2Id,
            Username = "testuser",
            Email = "test@example.com",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Viewer,
            CreatedAt = DateTime.UtcNow
        };

        _provider1
            .Setup(m => m.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthenticationAttempt.Authenticated(principal1));

        _provider2
            .Setup(m => m.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthenticationAttempt.Authenticated(principal2));

        _userService
            .Setup(m => m.GetAuthenticationUserAsync(user1Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user1);

        _userService
            .Setup(m => m.GetAuthenticationUserAsync(user2Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user2);

        // Act
        var result = await _subject.AuthenticateAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Succeeded)).True);
            _provider1.Verify(
                h => h.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()),
                Times.Once);
            _provider2.Verify(
                h => h.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task AuthenticateAsync_ReturnsFailure_WhenProviderRejectsCredential()
    {
        // Arrange
        _provider1
            .Setup(m => m.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthenticationAttempt.Rejected("the access token is invalid or expired"));

        _provider2
            .Setup(m => m.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthenticationAttempt.None);

        // Act
        var result = await _subject.AuthenticateAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Succeeded)).False);
            Assert.That(result, Has.Property(nameof(result.None)).False);
            Assert.That(
                result.Failure?.Message,
                Is.EqualTo("the bearer credential was rejected: the access token is invalid or expired"));
        }
    }

    [Test]
    public async Task AuthenticateAsync_ReportsApiKeyRejection_WhenApiKeyAndBearerBothRejected()
    {
        // Arrange
        _httpContext.Request.Headers["X-Api-Key"] = "invalid-api-key";

        _apiKeyService
            .Setup(m => m.TryValidateAsync("invalid-api-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(ApiKey?));

        _provider1
            .Setup(m => m.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthenticationAttempt.Rejected("the access token is invalid or expired"));

        _provider2
            .Setup(m => m.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthenticationAttempt.None);

        // Act
        var result = await _subject.AuthenticateAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.None)).False);
            Assert.That(
                result.Failure?.Message,
                Is.EqualTo("the api key credential was rejected: the key is not valid or its user cannot sign in"));
        }
    }

    [Test]
    public async Task AuthenticateAsync_ReturnsNoResult_WhenProviderReturnsNullAttempt()
    {
        // Arrange
        _provider1
            .Setup(m => m.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(AuthenticationAttempt?)!);

        _provider2
            .Setup(m => m.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthenticationAttempt.None);

        // Act
        var result = await _subject.AuthenticateAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.None)).True);
            Assert.That(result.Failure, Is.Null);
        }
    }

    [Test]
    public async Task AuthenticateAsync_ReturnsSuccess_WhenLaterProviderAuthenticatesAfterRejection()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        var identity = new ClaimsIdentity(claims, MagpieAuthenticationHandler.SchemeName);
        var principal = new ClaimsPrincipal(identity);

        var user = new User
        {
            Id = userId,
            Username = "testuser",
            Email = "test@example.com",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Viewer,
            CreatedAt = DateTime.UtcNow
        };

        _provider1
            .Setup(m => m.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthenticationAttempt.Rejected("the access token is invalid or expired"));

        _provider2
            .Setup(m => m.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthenticationAttempt.Authenticated(principal));

        _userService
            .Setup(m => m.GetAuthenticationUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        // Act
        var result = await _subject.AuthenticateAsync().ConfigureAwait(false);

        // Assert
        Assert.That(result, Has.Property(nameof(result.Succeeded)).True);
    }
}
