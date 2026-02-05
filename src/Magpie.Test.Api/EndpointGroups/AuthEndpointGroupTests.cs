using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Auth;
using Magpie.Abstractions.Repositories;
using Magpie.Api.EndpointGroups;
using Magpie.Models.Api;
using Magpie.Models.Data;
using Magpie.Test.Shared;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Moq;

using NUnit.Framework;

using static Magpie.Abstractions.Auth.ITokenService;

namespace Magpie.Test.Api.EndpointGroups;

[TestFixture]
public class AuthEndpointGroupTests
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private Mock<ICredentialAuthenticationProvider> _authProvider = null!;
    private Mock<ITokenService> _tokenService = null!;
    private Mock<IUserRepository> _userRepository = null!;
    private Guid _userId;

    [SetUp]
    public void SetUp()
    {
        _userId = Guid.NewGuid();
        _authProvider = new Mock<ICredentialAuthenticationProvider>();
        _tokenService = new Mock<ITokenService>();
        _userRepository = new Mock<IUserRepository>();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders().AddTestLogger();
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, _userId.ToString()) };
        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var authServiceMock = new Mock<IAuthenticationService>();
        authServiceMock
            .Setup(m => m.AuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<string>()))
            .ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(principal, "Test")));
        builder.Services.AddSingleton(authServiceMock.Object);
        builder.Services.AddAuthorizationBuilder()
            .SetFallbackPolicy(null)
            .SetDefaultPolicy(new AuthorizationPolicyBuilder()
                .RequireAssertion(_ => true)
                .Build());

        builder.Services.AddSingleton(_authProvider.Object);
        builder.Services.AddSingleton(_tokenService.Object);
        builder.Services.AddSingleton(_userRepository.Object);
        builder.Services.AddSingleton<IApiEndpointGroup, AuthEndpointGroup>();

        var app = builder.Build();

        app.Use(async (context, next) =>
        {
            context.User = principal;
            await next(context).ConfigureAwait(false);
        });

        var apiGroup = app.MapGroup("/api");
        var endpointGroups = app.Services.GetServices<IApiEndpointGroup>();
        foreach (var group in endpointGroups)
        {
            var groupRoute = apiGroup.MapGroup(group.Name);
            group.MapEndpoints(groupRoute);
        }

        app.UseAuthorization();

        _host = app;
        _host.Start();
        _client = _host.GetTestClient();
    }

    [TearDown]
    public void TearDown()
    {
        _client?.Dispose();
        _host?.Dispose();
    }

    [Test]
    public async Task Login_ReturnsTokens_WhenCredentialsValid()
    {
        // Arrange
        var user = new User
        {
            Id = _userId,
            Username = "testuser",
            ProviderType = "local",
            Enabled = true,
            Role = Models.Enums.Role.Viewer
        };
        var accessToken = new AccessToken("access_token_value", DateTime.UtcNow.AddHours(1));
        var refreshToken = "refresh_token_value";

        _authProvider
            .Setup(m => m.VerifyCredentialsAsync("testuser", "password123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _tokenService
            .Setup(m => m.GenerateAccessTokenAsync(user, It.IsAny<CancellationToken>()))
            .ReturnsAsync(accessToken);

        _tokenService
            .Setup(m => m.GenerateRefreshTokenAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(refreshToken);

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Username = "testuser",
            Password = "password123"
        }).ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<AuthTokenResponse>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Has.Property(nameof(content.AccessToken)).EqualTo("access_token_value"));
            Assert.That(content, Has.Property(nameof(content.RefreshToken)).EqualTo("refresh_token_value"));
        }
    }

    [Test]
    public async Task Login_ReturnsUnauthorized_WhenCredentialsInvalid()
    {
        // Arrange
        _authProvider
            .Setup(m => m.VerifyCredentialsAsync("testuser", "wrongpassword", It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(User?));

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Username = "testuser",
            Password = "wrongpassword"
        }).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Login_ReturnsBadRequest_WhenUsernameEmpty()
    {
        // Act
        var request = new LoginRequest { Username = "", Password = "password123" };
        var response = await _client.PostAsJsonAsync("/api/auth/login", request).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Login_ReturnsBadRequest_WhenPasswordEmpty()
    {
        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Username = "testuser",
            Password = ""
        }).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Logout_ReturnsNoContent_WhenRefreshTokenProvided()
    {
        // Arrange
        _tokenService
            .Setup(m => m.RevokeRefreshTokenAsync("valid_refresh_token", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/logout", new LogoutRequest
        {
            RefreshToken = "valid_refresh_token"
        }).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
        _tokenService.Verify(m => m.RevokeRefreshTokenAsync("valid_refresh_token", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task Logout_ReturnsBadRequest_WhenRefreshTokenEmpty()
    {
        // Act
        var request = new LogoutRequest { RefreshToken = "" };
        var response = await _client.PostAsJsonAsync("/api/auth/logout", request).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Refresh_ReturnsNewTokens_WhenRefreshTokenValid()
    {
        // Arrange
        var user = new User
        {
            Id = _userId,
            Username = "testuser",
            ProviderType = "local",
            Enabled = true,
            Role = Models.Enums.Role.Viewer
        };
        var accessToken = new AccessToken("new_access_token", DateTime.UtcNow.AddHours(1));
        var newRefreshToken = "new_refresh_token";

        _tokenService
            .Setup(m => m.TryValidateRefreshTokenAsync("valid_refresh_token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(_userId);

        _userRepository
            .Setup(m => m.GetByIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _tokenService
            .Setup(m => m.RevokeRefreshTokenAsync("valid_refresh_token", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _tokenService
            .Setup(m => m.GenerateAccessTokenAsync(user, It.IsAny<CancellationToken>()))
            .ReturnsAsync(accessToken);

        _tokenService
            .Setup(m => m.GenerateRefreshTokenAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(newRefreshToken);

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest
        {
            RefreshToken = "valid_refresh_token"
        }).ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<AuthTokenResponse>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Has.Property(nameof(content.AccessToken)).EqualTo("new_access_token"));
            Assert.That(content, Has.Property(nameof(content.RefreshToken)).EqualTo("new_refresh_token"));
        }
    }

    [Test]
    public async Task Refresh_ReturnsUnauthorized_WhenRefreshTokenInvalid()
    {
        // Arrange
        _tokenService
            .Setup(m => m.TryValidateRefreshTokenAsync("invalid_refresh_token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(Guid?));

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest
        {
            RefreshToken = "invalid_refresh_token"
        }).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Refresh_ReturnsUnauthorized_WhenUserNotFound()
    {
        // Arrange
        _tokenService
            .Setup(m => m.TryValidateRefreshTokenAsync("valid_refresh_token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(_userId);

        _userRepository
            .Setup(m => m.GetByIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(User?));

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest
        {
            RefreshToken = "valid_refresh_token"
        }).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Refresh_ReturnsBadRequest_WhenRefreshTokenEmpty()
    {
        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest
        {
            RefreshToken = ""
        }).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Refresh_RevokesOldToken_WhenSuccessful()
    {
        // Arrange
        var user = new User
        {
            Id = _userId,
            Username = "testuser",
            ProviderType = "local",
            Enabled = true,
            Role = Models.Enums.Role.Viewer
        };
        var accessToken = new AccessToken("new_access_token", DateTime.UtcNow.AddHours(1));

        _tokenService
            .Setup(m => m.TryValidateRefreshTokenAsync("old_refresh_token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(_userId);

        _userRepository
            .Setup(m => m.GetByIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _tokenService
            .Setup(m => m.RevokeRefreshTokenAsync("old_refresh_token", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _tokenService
            .Setup(m => m.GenerateAccessTokenAsync(user, It.IsAny<CancellationToken>()))
            .ReturnsAsync(accessToken);

        _tokenService
            .Setup(m => m.GenerateRefreshTokenAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("new_refresh_token");

        // Act
        await _client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest
        {
            RefreshToken = "old_refresh_token"
        }).ConfigureAwait(false);

        // Assert
        _tokenService.Verify(m => m
            .RevokeRefreshTokenAsync("old_refresh_token", It.IsAny<CancellationToken>()), Times.Once);
    }
}
