using System;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Auth;
using Magpie.Abstractions.Security;
using Magpie.Auth;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Test.Shared;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Auth;

[TestFixture]
public class MagpieAuthHandlerTests
{
    private Mock<IApiKeyManager> _apiKeyManager = null!;
    private Mock<IUserManager> _userManager = null!;
    private Mock<IAuthenticationProvider> _provider1 = null!;
    private Mock<IAuthenticationProvider> _provider2 = null!;
    private MagpieAuthenticationHandler _subject = null!;
    private DefaultHttpContext _httpContext = null!;
    private ILoggerFactory _loggerFactory = null!;

    [SetUp]
    public async Task SetUp()
    {
        _apiKeyManager = new Mock<IApiKeyManager>();
        _userManager = new Mock<IUserManager>();

        _provider1 = new Mock<IAuthenticationProvider>();
        _provider1.SetupGet(h => h.ProviderType).Returns("bearer");

        _provider2 = new Mock<IAuthenticationProvider>();
        _provider2.SetupGet(h => h.ProviderType).Returns("oidc");

        var options = new MagpieSchemeOptions();
        var optionsMonitor = new Mock<IOptionsMonitor<MagpieSchemeOptions>>();
        optionsMonitor
            .Setup(m => m.Get(MagpieAuthenticationHandler.SchemeName))
            .Returns(options);

        _loggerFactory = LoggerFactory.Create(b => b.AddTestLogger());

        _subject = new MagpieAuthenticationHandler(
            optionsMonitor.Object,
            _loggerFactory,
            UrlEncoder.Default,
            _apiKeyManager.Object,
            _userManager.Object,
            [_provider1.Object, _provider2.Object]);

        _httpContext = new DefaultHttpContext();

        var scheme = new AuthenticationScheme(
            MagpieAuthenticationHandler.SchemeName,
            MagpieAuthenticationHandler.SchemeName,
            typeof(MagpieAuthenticationHandler));

        await _subject.InitializeAsync(scheme, _httpContext);
    }

    [TearDown]
    public void TearDown()
    {
        _loggerFactory?.Dispose();
    }

    [Test]
    public async Task AuthenticateAsync_ReturnsFail_WhenNoCredentials()
    {
        // Arrange
        _provider1
            .Setup(m => m.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(ClaimsPrincipal?));
        _provider2
            .Setup(m => m.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(ClaimsPrincipal?));

        // Act
        var result = await _subject.AuthenticateAsync();

        // Assert
        Assert.That(result, Has.Property(nameof(result.Succeeded)).False);
    }

    [Test]
    public async Task AuthenticateAsync_ReturnsSuccess_WhenApiKeyValid()
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
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.AuthenticationMethod, "apikey")
        };
        var identity = new ClaimsIdentity(claims, MagpieAuthenticationHandler.SchemeName);
        var principal = new ClaimsPrincipal(identity);

        _httpContext.Request.Headers["X-Api-Key"] = "valid-api-key";

        _apiKeyManager
            .Setup(m => m.TryValidateApiKeyAsync("valid-api-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(apiKey);

        _userManager
            .Setup(m => m.GetUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _userManager
            .Setup(m => m.CreatePrincipalAsync(user, It.IsAny<CancellationToken>()))
            .ReturnsAsync(principal);

        // Act
        var result = await _subject.AuthenticateAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Succeeded)).True);
            Assert.That(
                result.Principal!.FindFirst(ClaimTypes.AuthenticationMethod)?.Value,
                Is.EqualTo("apikey"));
        }
    }

    [Test]
    public async Task AuthenticateAsync_ReturnsFailure_WhenApiKeyInvalid()
    {
        // Arrange
        _httpContext.Request.Headers["X-Api-Key"] = "invalid-api-key";

        _apiKeyManager
            .Setup(m => m.TryValidateApiKeyAsync("invalid-api-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(ApiKey?));

        _provider1
            .Setup(m => m.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(ClaimsPrincipal?));
        _provider2
            .Setup(m => m.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(ClaimsPrincipal?));

        // Act
        var result = await _subject.AuthenticateAsync();

        // Assert
        Assert.That(result, Has.Property(nameof(result.Succeeded)).False);
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
        var identity = new ClaimsIdentity([], MagpieAuthenticationHandler.SchemeName);
        var principal = new ClaimsPrincipal(identity);

        _httpContext.Request.Headers["X-Api-Key"] = "api-key";

        _apiKeyManager
            .Setup(m => m.TryValidateApiKeyAsync("api-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(apiKey);

        _userManager
            .Setup(m => m.GetUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _userManager
            .Setup(m => m.CreatePrincipalAsync(user, It.IsAny<CancellationToken>()))
            .ReturnsAsync(principal);

        // Act
        await _subject.AuthenticateAsync();

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
            .ReturnsAsync(principal);

        _userManager
            .Setup(m => m.GetUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        // Act
        var result = await _subject.AuthenticateAsync();

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
            .ReturnsAsync(principal);

        _userManager
            .Setup(m => m.GetUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _provider2
            .Setup(m => m.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(ClaimsPrincipal?));

        // Act
        var result = await _subject.AuthenticateAsync();

        // Assert
        Assert.That(result, Has.Property(nameof(result.Succeeded)).False);
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
            .ReturnsAsync(principal);

        _userManager
            .Setup(m => m.GetUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(User?));

        _provider2
            .Setup(m => m.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(ClaimsPrincipal?));

        // Act
        var result = await _subject.AuthenticateAsync();

        // Assert
        Assert.That(result, Has.Property(nameof(result.Succeeded)).False);
    }

    [Test]
    public async Task AuthenticateAsync_TriesNextProvider_WhenFirstReturnsNull()
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
            .ReturnsAsync(default(ClaimsPrincipal?));

        _provider2
            .Setup(m => m.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(principal);

        _userManager
            .Setup(m => m.GetUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        // Act
        var result = await _subject.AuthenticateAsync();

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

        _apiKeyManager
            .Setup(m => m.TryValidateApiKeyAsync("valid-api-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(apiKey);

        _userManager
            .Setup(m => m.GetUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _provider1
            .Setup(m => m.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(ClaimsPrincipal?));
        _provider2
            .Setup(m => m.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(ClaimsPrincipal?));

        // Act
        var result = await _subject.AuthenticateAsync();

        // Assert
        Assert.That(result, Has.Property(nameof(result.Succeeded)).False);
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

        _apiKeyManager
            .Setup(m => m.TryValidateApiKeyAsync("valid-api-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(apiKey);

        _userManager
            .Setup(m => m.GetUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(User?));

        _provider1
            .Setup(m => m.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(ClaimsPrincipal?));
        _provider2
            .Setup(m => m.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(ClaimsPrincipal?));

        // Act
        var result = await _subject.AuthenticateAsync();

        // Assert
        Assert.That(result, Has.Property(nameof(result.Succeeded)).False);
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
            .ReturnsAsync(principal1);

        _provider2
            .Setup(m => m.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(principal2);

        _userManager
            .Setup(m => m.GetUserAsync(user1Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user1);

        _userManager
            .Setup(m => m.GetUserAsync(user2Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user2);

        // Act
        var result = await _subject.AuthenticateAsync();

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
}
