using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Auth;
using Magpie.Abstractions.Data;
using Magpie.Abstractions.Security;
using Magpie.Auth;
using Magpie.Auth.Providers;
using Magpie.Models.Result;
using Magpie.Models.Users;
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
    private Mock<IApiKeyManager> _apiKeyManagerMock = null!;
    private Mock<IUserManager> _userManagerMock = null!;
    private Mock<IUserRepository> _userRepositoryMock = null!;
    private Mock<IAuthProviderRepository> _authProviderRepositoryMock = null!;
    private Mock<IAuthenticationProvider> _handler1Mock = null!;
    private Mock<IAuthenticationProvider> _handler2Mock = null!;
    private Mock<IAuthenticator> _authenticatorMock = null!;
    private MagpieAuthenticationHandler _subject = null!;
    private DefaultHttpContext _httpContext = null!;

    [SetUp]
    public async Task SetUp()
    {
        _apiKeyManagerMock = new Mock<IApiKeyManager>();
        _userManagerMock = new Mock<IUserManager>();
        _userRepositoryMock = new Mock<IUserRepository>();
        _authProviderRepositoryMock = new Mock<IAuthProviderRepository>();

        _handler1Mock = new Mock<IAuthenticationProvider>();
        _handler1Mock.SetupGet(h => h.ProviderType).Returns("bearer");

        _handler2Mock = new Mock<IAuthenticationProvider>();
        _handler2Mock.SetupGet(h => h.ProviderType).Returns("oidc");

        _authenticatorMock = new Mock<IAuthenticator>();
        _authenticatorMock.SetupGet(a => a.ProviderType).Returns("ldap");
        _authenticatorMock.SetupGet(a => a.SupportsJitProvisioning).Returns(false);

        var options = new MagpieSchemeOptions();
        var optionsMonitor = new Mock<IOptionsMonitor<MagpieSchemeOptions>>();
        optionsMonitor
            .Setup(x => x.Get(MagpieAuthenticationHandler.SchemeName))
            .Returns(options);

        var loggerFactory = LoggerFactory.Create(b => b.AddTestLogger());

        _subject = new MagpieAuthenticationHandler(
            optionsMonitor.Object,
            loggerFactory,
            UrlEncoder.Default,
            _apiKeyManagerMock.Object,
            _userManagerMock.Object,
            _userRepositoryMock.Object,
            _authProviderRepositoryMock.Object,
            [_handler1Mock.Object, _handler2Mock.Object],
            [_authenticatorMock.Object]);

        _httpContext = new DefaultHttpContext();

        var scheme = new AuthenticationScheme(
            MagpieAuthenticationHandler.SchemeName,
            MagpieAuthenticationHandler.SchemeName,
            typeof(MagpieAuthenticationHandler));

        await _subject.InitializeAsync(scheme, _httpContext);
    }

    [Test]
    public async Task AuthenticateAsync_ReturnsFail_WhenNoCredentials()
    {
        // Arrange
        _handler1Mock
            .Setup(h => h.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<ClaimsPrincipal?>(null));
        _handler2Mock
            .Setup(h => h.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<ClaimsPrincipal?>(null));

        // Act
        var result = await _subject.AuthenticateAsync();

        // Assert
        Assert.That(result.Succeeded, Is.False);
    }

    [Test]
    public async Task AuthenticateAsync_ReturnsSuccess_WhenApiKeyValid()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var apiKey = CreateTestApiKey(userId: userId);
        var user = CreateTestUser(userId);
        var claims = new[]
        {
        new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
        new Claim(ClaimTypes.AuthenticationMethod, "apikey")
    };
        var identity = new ClaimsIdentity(claims, MagpieAuthenticationHandler.SchemeName);
        var principal = new ClaimsPrincipal(identity);
        _httpContext.Request.Headers["X-Api-Key"] = "valid-api-key";
        _apiKeyManagerMock
            .Setup(m => m.ValidateApiKeyAsync("valid-api-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(apiKey));
        _userManagerMock
            .Setup(m => m.GetUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<User?>(user));
        _userManagerMock
            .Setup(m => m.CreatePrincipalAsync(user, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(principal));

        // Act
        var result = await _subject.AuthenticateAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Succeeded, Is.True);
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
        _apiKeyManagerMock
            .Setup(m => m.ValidateApiKeyAsync("invalid-api-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Fail<ApiKey>("API key not found", "ERR002"));
        _handler1Mock
            .Setup(h => h.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<ClaimsPrincipal?>(null));
        _handler2Mock
            .Setup(h => h.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<ClaimsPrincipal?>(null));

        // Act
        var result = await _subject.AuthenticateAsync();

        // Assert
        Assert.That(result.Succeeded, Is.False);
    }

    [Test]
    public async Task AuthenticateAsync_ApiKeyTakesPrecedence_OverHandlers()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var apiKey = CreateTestApiKey(userId: userId);
        var user = CreateTestUser(userId);
        var identity = new ClaimsIdentity([], MagpieAuthenticationHandler.SchemeName);
        var principal = new ClaimsPrincipal(identity);
        _httpContext.Request.Headers["X-Api-Key"] = "api-key";
        _apiKeyManagerMock
            .Setup(m => m.ValidateApiKeyAsync("api-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(apiKey));
        _userManagerMock
            .Setup(m => m.GetUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<User?>(user));
        _userManagerMock
            .Setup(m => m.CreatePrincipalAsync(user, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(principal));

        // Act
        await _subject.AuthenticateAsync();

        // Assert
        _handler1Mock.Verify(
            h => h.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _handler2Mock.Verify(
            h => h.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task AuthenticateAsync_ReturnsSuccess_WhenHandlerSucceedsAndUserEnabled()
    {
        var userId = Guid.NewGuid();
        var authProviderId = Guid.NewGuid();
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        var identity = new ClaimsIdentity(claims, MagpieAuthenticationHandler.SchemeName);
        var principal = new ClaimsPrincipal(identity);

        var user = new User(
            userId, "testuser", null, authProviderId, null, true, Role.Viewer,
            null, null, DateTimeOffset.UtcNow, null, null, null);

        var authProvider = new AuthProvider(
            authProviderId, "Local", "local", true, true,
            new Dictionary<string, string?>(), DateTimeOffset.UtcNow, null, null, null);

        _handler1Mock
            .Setup(h => h.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<ClaimsPrincipal?>(principal));

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<User?>(user));

        _authProviderRepositoryMock
            .Setup(r => r.GetByIdAsync(authProviderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<AuthProvider?>(authProvider));

        var result = await _subject.AuthenticateAsync();

        Assert.That(result.Succeeded, Is.True);
    }

    [Test]
    public async Task AuthenticateAsync_ReturnsFailure_WhenUserDisabled()
    {
        var userId = Guid.NewGuid();
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        var identity = new ClaimsIdentity(claims, MagpieAuthenticationHandler.SchemeName);
        var principal = new ClaimsPrincipal(identity);

        var user = new User(
            userId, "testuser", null, Guid.NewGuid(), null, false, Role.Viewer,
            null, null, DateTimeOffset.UtcNow, null, null, null);

        _handler1Mock
            .Setup(h => h.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<ClaimsPrincipal?>(principal));

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<User?>(user));

        _handler2Mock
            .Setup(h => h.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<ClaimsPrincipal?>(null));

        var result = await _subject.AuthenticateAsync();

        Assert.That(result.Succeeded, Is.False);
    }

    [Test]
    public async Task AuthenticateAsync_ReturnsFailure_WhenUserNotFound()
    {
        var userId = Guid.NewGuid();
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        var identity = new ClaimsIdentity(claims, MagpieAuthenticationHandler.SchemeName);
        var principal = new ClaimsPrincipal(identity);

        _handler1Mock
            .Setup(h => h.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<ClaimsPrincipal?>(principal));

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<User?>(null));

        _handler2Mock
            .Setup(h => h.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<ClaimsPrincipal?>(null));

        var result = await _subject.AuthenticateAsync();

        Assert.That(result.Succeeded, Is.False);
    }

    [Test]
    public async Task AuthenticateAsync_TriesNextHandler_WhenFirstFails()
    {
        var userId = Guid.NewGuid();
        var authProviderId = Guid.NewGuid();
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        var identity = new ClaimsIdentity(claims, MagpieAuthenticationHandler.SchemeName);
        var principal = new ClaimsPrincipal(identity);

        var user = new User(
            userId, "testuser", null, authProviderId, null, true, Role.Viewer,
            null, null, DateTimeOffset.UtcNow, null, null, null);

        var authProvider = new AuthProvider(
            authProviderId, "Local", "local", true, true,
            new Dictionary<string, string?>(), DateTimeOffset.UtcNow, null, null, null);

        _handler1Mock
            .Setup(h => h.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Fail<ClaimsPrincipal?>("Invalid credentials"));

        _handler2Mock
            .Setup(h => h.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<ClaimsPrincipal?>(principal));

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<User?>(user));

        _authProviderRepositoryMock
            .Setup(r => r.GetByIdAsync(authProviderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<AuthProvider?>(authProvider));

        var result = await _subject.AuthenticateAsync();

        using (Assert.EnterMultipleScope())
        {
            _handler1Mock.Verify(
                h => h.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()),
                Times.Once);
            _handler2Mock.Verify(
                h => h.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()),
                Times.Once);
            Assert.That(result.Succeeded, Is.True);
        }
    }

    [Test]
    public async Task AuthenticateAsync_TriesNextHandler_WhenFirstReturnsNotApplicable()
    {
        var userId = Guid.NewGuid();
        var authProviderId = Guid.NewGuid();
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        var identity = new ClaimsIdentity(claims, MagpieAuthenticationHandler.SchemeName);
        var principal = new ClaimsPrincipal(identity);

        var user = new User(
            userId, "testuser", null, authProviderId, null, true, Role.Viewer,
            null, null, DateTimeOffset.UtcNow, null, null, null);

        var authProvider = new AuthProvider(
            authProviderId, "Local", "local", true, true,
            new Dictionary<string, string?>(), DateTimeOffset.UtcNow, null, null, null);

        _handler1Mock
            .Setup(h => h.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<ClaimsPrincipal?>(null));

        _handler2Mock
            .Setup(h => h.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<ClaimsPrincipal?>(principal));

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<User?>(user));

        _authProviderRepositoryMock
            .Setup(r => r.GetByIdAsync(authProviderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<AuthProvider?>(authProvider));

        var result = await _subject.AuthenticateAsync();

        using (Assert.EnterMultipleScope())
        {
            _handler1Mock.Verify(
                h => h.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()),
                Times.Once);
            _handler2Mock.Verify(
                h => h.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()),
                Times.Once);
            Assert.That(result.Succeeded, Is.True);
        }
    }

    [Test]
    public async Task AuthenticateAsync_SkipsUserValidation_ForNoAuthProvider()
    {
        var noAuthProviderMock = new Mock<IAuthenticationProvider>();
        noAuthProviderMock.SetupGet(h => h.ProviderType).Returns(NoAuthProvider.ProviderTypeName);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, NoAuthProvider.AdminUserId.ToString()),
            new Claim(ClaimTypes.Name, "admin")
        };
        var identity = new ClaimsIdentity(claims, MagpieAuthenticationHandler.SchemeName);
        var principal = new ClaimsPrincipal(identity);

        noAuthProviderMock
            .Setup(h => h.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<ClaimsPrincipal?>(principal));

        var options = new MagpieSchemeOptions();
        var optionsMonitor = new Mock<IOptionsMonitor<MagpieSchemeOptions>>();
        optionsMonitor
            .Setup(x => x.Get(MagpieAuthenticationHandler.SchemeName))
            .Returns(options);

        var loggerFactory = LoggerFactory.Create(b => b.AddTestLogger());

        var subject = new MagpieAuthenticationHandler(
            optionsMonitor.Object,
            loggerFactory,
            UrlEncoder.Default,
            _apiKeyManagerMock.Object,
            _userManagerMock.Object,
            _userRepositoryMock.Object,
            _authProviderRepositoryMock.Object,
            [noAuthProviderMock.Object],
            []);

        var httpContext = new DefaultHttpContext();
        var scheme = new AuthenticationScheme(
            MagpieAuthenticationHandler.SchemeName,
            MagpieAuthenticationHandler.SchemeName,
            typeof(MagpieAuthenticationHandler));

        await subject.InitializeAsync(scheme, httpContext);

        var result = await subject.AuthenticateAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Succeeded, Is.True);
            _userRepositoryMock.Verify(
                r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task AuthenticateAsync_ReturnsFailure_WhenUserHasNoAuthProvider()
    {
        var userId = Guid.NewGuid();
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        var identity = new ClaimsIdentity(claims, MagpieAuthenticationHandler.SchemeName);
        var principal = new ClaimsPrincipal(identity);

        var user = new User(
            userId, "testuser", null, null, null, true, Role.Viewer,
            null, null, DateTimeOffset.UtcNow, null, null, null);

        _handler1Mock
            .Setup(h => h.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<ClaimsPrincipal?>(principal));

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<User?>(user));

        _handler2Mock
            .Setup(h => h.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<ClaimsPrincipal?>(null));

        var result = await _subject.AuthenticateAsync();

        Assert.That(result.Succeeded, Is.False);
    }

    [Test]
    public async Task AuthenticateAsync_ReturnsFailure_WhenAuthProviderNotFound()
    {
        var userId = Guid.NewGuid();
        var authProviderId = Guid.NewGuid();
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        var identity = new ClaimsIdentity(claims, MagpieAuthenticationHandler.SchemeName);
        var principal = new ClaimsPrincipal(identity);

        var user = new User(
            userId, "testuser", null, authProviderId, null, true, Role.Viewer,
            null, null, DateTimeOffset.UtcNow, null, null, null);

        _handler1Mock
            .Setup(h => h.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<ClaimsPrincipal?>(principal));

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<User?>(user));

        _authProviderRepositoryMock
            .Setup(r => r.GetByIdAsync(authProviderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<AuthProvider?>(null));

        _handler2Mock
            .Setup(h => h.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<ClaimsPrincipal?>(null));

        var result = await _subject.AuthenticateAsync();

        Assert.That(result.Succeeded, Is.False);
    }

    [Test]
    public async Task AuthenticateAsync_ReturnsFailure_WhenAuthProviderDisabled()
    {
        var userId = Guid.NewGuid();
        var authProviderId = Guid.NewGuid();
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        var identity = new ClaimsIdentity(claims, MagpieAuthenticationHandler.SchemeName);
        var principal = new ClaimsPrincipal(identity);

        var user = new User(
            userId, "testuser", null, authProviderId, null, true, Role.Viewer,
            null, null, DateTimeOffset.UtcNow, null, null, null);

        var authProvider = new AuthProvider(
            authProviderId, "Local", "local", false, true,
            new Dictionary<string, string?>(), DateTimeOffset.UtcNow, null, null, null);

        _handler1Mock
            .Setup(h => h.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<ClaimsPrincipal?>(principal));

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<User?>(user));

        _authProviderRepositoryMock
            .Setup(r => r.GetByIdAsync(authProviderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<AuthProvider?>(authProvider));

        _handler2Mock
            .Setup(h => h.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<ClaimsPrincipal?>(null));

        var result = await _subject.AuthenticateAsync();

        Assert.That(result.Succeeded, Is.False);
    }

    private static ApiKey CreateTestApiKey(Guid? id = null, Guid? userId = null)
    {
        return new(
            Id: id ?? Guid.NewGuid(),
            UserId: userId ?? Guid.NewGuid(),
            Name: "Test API Key",
            KeyPrefix: "mgp_12345678",
            Enabled: true,
            ExpiresAt: null,
            LastUsedAt: null,
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);
    }

    private static User CreateTestUser(Guid? id = null)
    {
        return new(
            Id: id ?? Guid.NewGuid(),
            Username: "testuser",
            Email: "test@example.com",
            AuthProviderId: null,
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

    [Test]
    public async Task AuthenticateAsync_JitProvisions_WhenUserNotFoundAndAuthenticatorSucceeds()
    {
        var authProviderId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var jitAuthenticatorMock = new Mock<IAuthenticator>();
        jitAuthenticatorMock.SetupGet(a => a.ProviderType).Returns("ldap");
        jitAuthenticatorMock.SetupGet(a => a.SupportsJitProvisioning).Returns(true);

        var transientUser = new User(
            Guid.Empty, "ldapuser", "ldap@example.com", null, "ldap-ext-id", true, Role.Viewer,
            new Dictionary<string, string?> { ["department"] = "IT" },
            null, DateTimeOffset.UtcNow, null, null, null);

        var persistedUser = transientUser with { Id = userId, AuthProviderId = authProviderId };

        var authProvider = new AuthProvider(
            authProviderId, "LDAP", "ldap", true, false,
            new Dictionary<string, string?>(), DateTimeOffset.UtcNow, null, null, null);

        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("ldapuser:ldappass"));

        var options = new MagpieSchemeOptions();
        var optionsMonitor = new Mock<IOptionsMonitor<MagpieSchemeOptions>>();
        optionsMonitor.Setup(x => x.Get(MagpieAuthenticationHandler.SchemeName)).Returns(options);
        var loggerFactory = LoggerFactory.Create(b => b.AddTestLogger());

        var subject = new MagpieAuthenticationHandler(
            optionsMonitor.Object,
            loggerFactory,
            UrlEncoder.Default,
            _apiKeyManagerMock.Object,
            _userManagerMock.Object,
            _userRepositoryMock.Object,
            _authProviderRepositoryMock.Object,
            [_handler1Mock.Object],
            [jitAuthenticatorMock.Object]);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = $"Basic {encoded}";

        var scheme = new AuthenticationScheme(
            MagpieAuthenticationHandler.SchemeName,
            MagpieAuthenticationHandler.SchemeName,
            typeof(MagpieAuthenticationHandler));
        await subject.InitializeAsync(scheme, httpContext);

        _handler1Mock
            .Setup(h => h.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<ClaimsPrincipal?>(null));

        _authProviderRepositoryMock
            .Setup(r => r.GetByProviderTypeAsync("ldap", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<AuthProvider>>([authProvider]));

        jitAuthenticatorMock
            .Setup(a => a.AuthenticateAsync("ldapuser", "ldappass", authProvider, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(transientUser));

        _userManagerMock
            .Setup(m => m.CreateExternalUserAsync(
                "ldapuser",
                "ldap@example.com",
                authProviderId,
                "ldap-ext-id",
                Role.Viewer,
                true,
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(persistedUser));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
            MagpieAuthenticationHandler.SchemeName));

        _userManagerMock
            .Setup(m => m.CreatePrincipalAsync(persistedUser, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(principal));

        var result = await subject.AuthenticateAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Succeeded, Is.True);
            _userManagerMock.Verify(
                m => m.CreateExternalUserAsync(
                    "ldapuser",
                    "ldap@example.com",
                    authProviderId,
                    "ldap-ext-id",
                    Role.Viewer,
                    true,
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task AuthenticateAsync_DoesNotJitProvision_WhenAuthenticatorDoesNotSupportIt()
    {
        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("newuser:password"));
        _httpContext.Request.Headers.Authorization = $"Basic {encoded}";

        _handler1Mock
            .Setup(h => h.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<ClaimsPrincipal?>(null));

        _handler2Mock
            .Setup(h => h.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<ClaimsPrincipal?>(null));

        var result = await _subject.AuthenticateAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Succeeded, Is.False);
            _userManagerMock.Verify(
                m => m.CreateExternalUserAsync(
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<Guid>(),
                    It.IsAny<string?>(),
                    It.IsAny<Role>(),
                    It.IsAny<bool>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task AuthenticateAsync_DoesNotJitProvision_WhenNoBasicCredentials()
    {
        var jitAuthenticatorMock = new Mock<IAuthenticator>();
        jitAuthenticatorMock.SetupGet(a => a.ProviderType).Returns("ldap");
        jitAuthenticatorMock.SetupGet(a => a.SupportsJitProvisioning).Returns(true);

        var options = new MagpieSchemeOptions();
        var optionsMonitor = new Mock<IOptionsMonitor<MagpieSchemeOptions>>();
        optionsMonitor.Setup(x => x.Get(MagpieAuthenticationHandler.SchemeName)).Returns(options);
        var loggerFactory = LoggerFactory.Create(b => b.AddTestLogger());

        var subject = new MagpieAuthenticationHandler(
            optionsMonitor.Object,
            loggerFactory,
            UrlEncoder.Default,
            _apiKeyManagerMock.Object,
            _userManagerMock.Object,
            _userRepositoryMock.Object,
            _authProviderRepositoryMock.Object,
            [_handler1Mock.Object],
            [jitAuthenticatorMock.Object]);

        var httpContext = new DefaultHttpContext();

        var scheme = new AuthenticationScheme(
            MagpieAuthenticationHandler.SchemeName,
            MagpieAuthenticationHandler.SchemeName,
            typeof(MagpieAuthenticationHandler));
        await subject.InitializeAsync(scheme, httpContext);

        _handler1Mock
            .Setup(h => h.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<ClaimsPrincipal?>(null));

        var result = await subject.AuthenticateAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Succeeded, Is.False);
            jitAuthenticatorMock.Verify(
                a => a.AuthenticateAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<AuthProvider>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task AuthenticateAsync_SkipsDisabledProviders_ForJitProvisioning()
    {
        var enabledProviderId = Guid.NewGuid();
        var disabledProviderId = Guid.NewGuid();

        var jitAuthenticatorMock = new Mock<IAuthenticator>();
        jitAuthenticatorMock.SetupGet(a => a.ProviderType).Returns("ldap");
        jitAuthenticatorMock.SetupGet(a => a.SupportsJitProvisioning).Returns(true);

        var disabledProvider = new AuthProvider(
            disabledProviderId, "LDAP Disabled", "ldap", false, false,
            new Dictionary<string, string?>(), DateTimeOffset.UtcNow, null, null, null);

        var enabledProvider = new AuthProvider(
            enabledProviderId, "LDAP Enabled", "ldap", true, false,
            new Dictionary<string, string?>(), DateTimeOffset.UtcNow, null, null, null);

        var transientUser = new User(
            Guid.Empty, "ldapuser", null, null, null, true, Role.Viewer,
            null, null, DateTimeOffset.UtcNow, null, null, null);

        var persistedUser = transientUser with { Id = Guid.NewGuid(), AuthProviderId = enabledProviderId };

        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("ldapuser:pass"));

        var options = new MagpieSchemeOptions();
        var optionsMonitor = new Mock<IOptionsMonitor<MagpieSchemeOptions>>();
        optionsMonitor.Setup(x => x.Get(MagpieAuthenticationHandler.SchemeName)).Returns(options);
        var loggerFactory = LoggerFactory.Create(b => b.AddTestLogger());

        var subject = new MagpieAuthenticationHandler(
            optionsMonitor.Object,
            loggerFactory,
            UrlEncoder.Default,
            _apiKeyManagerMock.Object,
            _userManagerMock.Object,
            _userRepositoryMock.Object,
            _authProviderRepositoryMock.Object,
            [_handler1Mock.Object],
            [jitAuthenticatorMock.Object]);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = $"Basic {encoded}";

        var scheme = new AuthenticationScheme(
            MagpieAuthenticationHandler.SchemeName,
            MagpieAuthenticationHandler.SchemeName,
            typeof(MagpieAuthenticationHandler));
        await subject.InitializeAsync(scheme, httpContext);

        _handler1Mock
            .Setup(h => h.TryAuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<ClaimsPrincipal?>(null));

        _authProviderRepositoryMock
            .Setup(r => r.GetByProviderTypeAsync("ldap", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<AuthProvider>>([disabledProvider, enabledProvider]));

        jitAuthenticatorMock
            .Setup(a => a.AuthenticateAsync("ldapuser", "pass", enabledProvider, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(transientUser));

        _userManagerMock
            .Setup(m => m.CreateExternalUserAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Guid>(), It.IsAny<string?>(),
                It.IsAny<Role>(), It.IsAny<bool>(), It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(persistedUser));

        var principal = new ClaimsPrincipal(new ClaimsIdentity([], MagpieAuthenticationHandler.SchemeName));
        _userManagerMock
            .Setup(m => m.CreatePrincipalAsync(persistedUser, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(principal));

        await subject.AuthenticateAsync();

        jitAuthenticatorMock.Verify(
            a => a.AuthenticateAsync("ldapuser", "pass", disabledProvider, It.IsAny<CancellationToken>()),
            Times.Never);
        jitAuthenticatorMock.Verify(
            a => a.AuthenticateAsync("ldapuser", "pass", enabledProvider, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
