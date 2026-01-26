using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Auth;
using Magpie.Abstractions.Data;
using Magpie.Auth.Providers;
using Magpie.Models.Result;
using Magpie.Models.Users;

using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Auth;

[TestFixture]
public class BasicAuthProviderTests
{
    private Mock<IUserRepository> _userRepository = null!;
    private Mock<IAuthProviderRepository> _authProviderRepository = null!;
    private Mock<IAuthenticator> _authenticator = null!;
    private BasicAuthProvider _subject = null!;
    private DefaultHttpContext _httpContext = null!;

    [SetUp]
    public void SetUp()
    {
        _userRepository = new Mock<IUserRepository>();
        _authProviderRepository = new Mock<IAuthProviderRepository>();
        _authenticator = new Mock<IAuthenticator>();
        _authenticator.Setup(a => a.ProviderType).Returns("local");

        _subject = new BasicAuthProvider(
            _userRepository.Object,
            _authProviderRepository.Object,
            [_authenticator.Object]);
        _httpContext = new DefaultHttpContext();
    }

    [Test]
    public void ProviderType_ReturnsBasic()
    {
        // Assert
        Assert.That(_subject.ProviderType, Is.EqualTo("basic"));
    }

    [Test]
    public async Task TryAuthenticateAsync_NoAuthHeader_ReturnsNotApplicable()
    {
        // Act
        var result = await _subject.TryAuthenticateAsync(_httpContext);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<ClaimsPrincipal?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task TryAuthenticateAsync_NonBasicScheme_ReturnsNotApplicable()
    {
        // Arrange
        _httpContext.Request.Headers[HeaderNames.Authorization] = "Bearer token123";

        // Act
        var result = await _subject.TryAuthenticateAsync(_httpContext);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<ClaimsPrincipal?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task TryAuthenticateAsync_InvalidBase64_ReturnsFailure()
    {
        // Arrange
        _httpContext.Request.Headers[HeaderNames.Authorization] = "Basic !!!invalid!!!";

        // Act
        var result = await _subject.TryAuthenticateAsync(_httpContext);

        // Assert
        Assert.That(result, Is.InstanceOf<FailureResult<ClaimsPrincipal?>>()
            .With.Property(nameof(FailureResult<>.Reason)).EqualTo("Invalid base64 encoding"));
    }

    [Test]
    public async Task TryAuthenticateAsync_NoColon_ReturnsFailure()
    {
        // Arrange
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("usernameonly"));
        _httpContext.Request.Headers[HeaderNames.Authorization] = $"Basic {encoded}";

        // Act
        var result = await _subject.TryAuthenticateAsync(_httpContext);

        // Assert
        Assert.That(result, Is.InstanceOf<FailureResult<ClaimsPrincipal?>>()
            .With.Property(nameof(FailureResult<>.Reason)).EqualTo("Invalid credentials format"));
    }

    [Test]
    public async Task TryAuthenticateAsync_EmptyUsername_ReturnsFailure()
    {
        // Arrange
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(":password"));
        _httpContext.Request.Headers[HeaderNames.Authorization] = $"Basic {encoded}";

        // Act
        var result = await _subject.TryAuthenticateAsync(_httpContext);

        // Assert
        Assert.That(result, Is.InstanceOf<FailureResult<ClaimsPrincipal?>>()
            .With.Property(nameof(FailureResult<>.Reason)).EqualTo("Username cannot be empty"));
    }

    [Test]
    public async Task TryAuthenticateAsync_UserNotFound_ReturnsFailure()
    {
        // Arrange
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("testuser:wrongpassword"));
        _httpContext.Request.Headers[HeaderNames.Authorization] = $"Basic {encoded}";
        _userRepository.Setup(r => r.GetByUsernameAsync("testuser", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<User?>(null));

        // Act
        var result = await _subject.TryAuthenticateAsync(_httpContext);

        // Assert
        Assert.That(result, Is.InstanceOf<FailureResult<ClaimsPrincipal?>>()
            .With.Property(nameof(FailureResult<>.Reason)).EqualTo("Invalid username or password"));
    }

    [Test]
    public async Task TryAuthenticateAsync_UserWithoutAuthProvider_ReturnsFailure()
    {
        // Arrange
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("testuser:password"));
        _httpContext.Request.Headers[HeaderNames.Authorization] = $"Basic {encoded}";
        var user = CreateTestUser() with { AuthProviderId = null };
        _userRepository.Setup(r => r.GetByUsernameAsync("testuser", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<User?>(user));

        // Act
        var result = await _subject.TryAuthenticateAsync(_httpContext);

        // Assert
        Assert.That(result, Is.InstanceOf<FailureResult<ClaimsPrincipal?>>()
            .With.Property(nameof(FailureResult<>.Reason)).EqualTo("User has no auth provider configured"));
    }

    [Test]
    public async Task TryAuthenticateAsync_AuthProviderNotFound_ReturnsFailure()
    {
        // Arrange
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("testuser:password"));
        _httpContext.Request.Headers[HeaderNames.Authorization] = $"Basic {encoded}";
        var authProviderId = Guid.NewGuid();
        var user = CreateTestUser() with { AuthProviderId = authProviderId };
        _userRepository.Setup(r => r.GetByUsernameAsync("testuser", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<User?>(user));
        _authProviderRepository.Setup(r => r.GetByIdAsync(authProviderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<AuthProvider?>(null));

        // Act
        var result = await _subject.TryAuthenticateAsync(_httpContext);

        // Assert
        Assert.That(result, Is.InstanceOf<FailureResult<ClaimsPrincipal?>>()
            .With.Property(nameof(FailureResult<>.Reason)).EqualTo("Auth provider not found"));
    }

    [Test]
    public async Task TryAuthenticateAsync_InvalidCredentials_ReturnsFailure()
    {
        // Arrange
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("testuser:wrongpassword"));
        _httpContext.Request.Headers[HeaderNames.Authorization] = $"Basic {encoded}";
        var authProviderId = Guid.NewGuid();
        var user = CreateTestUser() with { AuthProviderId = authProviderId };
        var authProvider = CreateTestAuthProvider(authProviderId);
        _userRepository.Setup(r => r.GetByUsernameAsync("testuser", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<User?>(user));
        _authProviderRepository.Setup(r => r.GetByIdAsync(authProviderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<AuthProvider?>(authProvider));
        _authenticator.Setup(a => a.AuthenticateAsync(
            "testuser",
            "wrongpassword",
            authProvider,
            It.IsAny<CancellationToken>())).ReturnsAsync(Result.Fail<User>("Invalid username or password"));

        // Act
        var result = await _subject.TryAuthenticateAsync(_httpContext);

        // Assert
        Assert.That(result, Is.InstanceOf<FailureResult<ClaimsPrincipal?>>()
            .With.Property(nameof(FailureResult<>.Reason)).EqualTo("Invalid username or password"));
    }

    [Test]
    public async Task TryAuthenticateAsync_LockedOut_ReturnsFailure()
    {
        // Arrange
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("testuser:password"));
        _httpContext.Request.Headers[HeaderNames.Authorization] = $"Basic {encoded}";
        var authProviderId = Guid.NewGuid();
        var user = CreateTestUser() with { AuthProviderId = authProviderId };
        var authProvider = CreateTestAuthProvider(authProviderId);
        _userRepository.Setup(r => r.GetByUsernameAsync("testuser", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<User?>(user));
        _authProviderRepository.Setup(r => r.GetByIdAsync(authProviderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<AuthProvider?>(authProvider));
        _authenticator.Setup(a => a.AuthenticateAsync(
            "testuser",
            "password",
            authProvider,
            It.IsAny<CancellationToken>())).ReturnsAsync(Result.Fail<User>("account locked out"));

        // Act
        var result = await _subject.TryAuthenticateAsync(_httpContext);

        // Assert
        Assert.That(result, Is.InstanceOf<FailureResult<ClaimsPrincipal?>>()
            .With.Property(nameof(FailureResult<>.Reason)).Contains("locked"));
    }

    [Test]
    public async Task TryAuthenticateAsync_ValidCredentials_ReturnsSuccess()
    {
        // Arrange
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("testuser:correctpassword"));
        _httpContext.Request.Headers[HeaderNames.Authorization] = $"Basic {encoded}";
        var authProviderId = Guid.NewGuid();
        var user = CreateTestUser() with { AuthProviderId = authProviderId };
        var authProvider = CreateTestAuthProvider(authProviderId);
        _userRepository.Setup(r => r.GetByUsernameAsync("testuser", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<User?>(user));
        _authProviderRepository.Setup(r => r.GetByIdAsync(authProviderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<AuthProvider?>(authProvider));
        _authenticator.Setup(a => a.AuthenticateAsync(
            "testuser",
            "correctpassword",
            authProvider,
            It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(user));

        // Act
        var result = await _subject.TryAuthenticateAsync(_httpContext);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<ClaimsPrincipal?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Not.Null);
    }

    [Test]
    public async Task TryAuthenticateAsync_ValidCredentials_ReturnsCorrectClaims()
    {
        // Arrange
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("testuser:correctpassword"));
        _httpContext.Request.Headers[HeaderNames.Authorization] = $"Basic {encoded}";
        var userId = Guid.NewGuid();
        var authProviderId = Guid.NewGuid();
        var user = new User(
            Id: userId,
            Username: "testuser",
            Email: "test@example.com",
            AuthProviderId: authProviderId,
            ExternalId: null,
            Enabled: true,
            Role: Role.Admin,
            ProviderData: new Dictionary<string, string?>(),
            LastLoginAt: null,
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);
        var authProvider = CreateTestAuthProvider(authProviderId);
        _userRepository.Setup(r => r.GetByUsernameAsync("testuser", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<User?>(user));
        _authProviderRepository.Setup(r => r.GetByIdAsync(authProviderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<AuthProvider?>(authProvider));
        _authenticator.Setup(a =>
            a.AuthenticateAsync("testuser", "correctpassword", authProvider, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(user));

        // Act
        var result = await _subject.TryAuthenticateAsync(_httpContext);
        var principal = ((SuccessResult<ClaimsPrincipal?>)result).Value!;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                principal.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                Is.EqualTo(userId.ToString()));
            Assert.That(
                principal.FindFirst(ClaimTypes.Name)?.Value,
                Is.EqualTo("testuser"));
            Assert.That(
                principal.FindFirst(ClaimTypes.Role)?.Value,
                Is.EqualTo("Admin"));
            Assert.That(
                principal.FindFirst(MagpieClaims.AuthProviderId)?.Value,
                Is.EqualTo(authProviderId.ToString()));
            Assert.That(
                principal.FindFirst(ClaimTypes.AuthenticationMethod)?.Value,
                Is.EqualTo("basic"));
        }
    }

    [Test]
    public async Task TryAuthenticateAsync_PasswordWithColon_ParsesCorrectly()
    {
        // Arrange
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("testuser:pass:word:with:colons"));
        _httpContext.Request.Headers[HeaderNames.Authorization] = $"Basic {encoded}";
        var authProviderId = Guid.NewGuid();
        var user = CreateTestUser() with { AuthProviderId = authProviderId };
        var authProvider = CreateTestAuthProvider(authProviderId);
        _userRepository.Setup(r => r.GetByUsernameAsync("testuser", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<User?>(user));
        _authProviderRepository.Setup(r => r.GetByIdAsync(authProviderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<AuthProvider?>(authProvider));
        _authenticator.Setup(a => a.AuthenticateAsync(
            "testuser",
            "pass:word:with:colons",
            authProvider,
            It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(user));

        // Act
        var result = await _subject.TryAuthenticateAsync(_httpContext);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<ClaimsPrincipal?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Not.Null);
    }

    [Test]
    public async Task TryAuthenticateAsync_CaseInsensitiveScheme_AcceptsLowercaseBasic()
    {
        // Arrange
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("testuser:password"));
        _httpContext.Request.Headers[HeaderNames.Authorization] = $"basic {encoded}";
        var authProviderId = Guid.NewGuid();
        var user = CreateTestUser() with { AuthProviderId = authProviderId };
        var authProvider = CreateTestAuthProvider(authProviderId);
        _userRepository.Setup(r => r.GetByUsernameAsync("testuser", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<User?>(user));
        _authProviderRepository.Setup(r => r.GetByIdAsync(authProviderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<AuthProvider?>(authProvider));
        _authenticator.Setup(a => a.AuthenticateAsync(
            "testuser",
            "password",
            authProvider,
            It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(user));

        // Act
        var result = await _subject.TryAuthenticateAsync(_httpContext);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<ClaimsPrincipal?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Not.Null);
    }

    [Test]
    public void Challenge_AddsWWWAuthenticateHeader()
    {
        // Act
        _subject.Challenge(_httpContext);

        // Assert
        Assert.That(
            _httpContext.Response.Headers[HeaderNames.WWWAuthenticate].ToString(),
            Is.EqualTo("Basic realm=\"Magpie\""));
    }

    private static User CreateTestUser()
    {
        return new(
            Id: Guid.NewGuid(),
            Username: "testuser",
            Email: "test@example.com",
            AuthProviderId: Guid.NewGuid(),
            ExternalId: null,
            Enabled: true,
            Role: Role.Viewer,
            ProviderData: new Dictionary<string, string?>(),
            LastLoginAt: null,
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);
    }

    private static AuthProvider CreateTestAuthProvider(Guid id)
    {
        return new(
            Id: id,
            Name: "Local",
            Provider: "local",
            Enabled: true,
            IsDefault: true,
            Settings: new Dictionary<string, string?>(),
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);
    }
}
