using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
using Magpie.Api.EndpointGroups;
using Magpie.Api.Request;
using Magpie.Models.Api;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;
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

namespace Magpie.Test.Api.EndpointGroups;

[TestFixture]
public class UsersEndpointGroupTests
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private Mock<IUserRepository> _userRepository = null!;
    private Mock<IUserManager> _userManager = null!;
    private Mock<IPasswordHasher> _passwordHasher = null!;
    private Guid _userId;

    [SetUp]
    public void SetUp()
    {
        _userId = Guid.NewGuid();
        _userRepository = new Mock<IUserRepository>();
        _userManager = new Mock<IUserManager>();
        _passwordHasher = new Mock<IPasswordHasher>();
        _passwordHasher
            .Setup(m => m.HashPasswordAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IPasswordHasher.HashedPassword("hashed", "salt"));

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
                .Build())
            .AddPolicy(nameof(Role.Viewer), policy => policy.RequireAssertion(_ => true))
            .AddPolicy(nameof(Role.Admin), policy => policy.RequireAssertion(_ => true));

        builder.Services.AddSingleton(_userRepository.Object);
        builder.Services.AddSingleton(_userManager.Object);
        builder.Services.AddSingleton(_passwordHasher.Object);
        builder.Services.AddSingleton<IApiEndpointGroup, UsersEndpointGroup>();

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
    public async Task GetCurrentUser_ReturnsUser()
    {
        // Arrange
        var user = new User
        {
            Id = _userId,
            Username = "TestUser",
            Email = "test@example.com",
            Role = Role.Viewer,
            ProviderType = "local"
        };

        _userRepository
            .Setup(m => m.GetByIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        // Act
        var response = await _client.GetAsync("/api/users/me").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<UserResponse>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(content.Id)).EqualTo(_userId));
            Assert.That(content, Has.Property(nameof(content.Username)).EqualTo("TestUser"));
        }
    }

    [Test]
    public async Task GetCurrentUser_ExcludesCredentialFields()
    {
        // Arrange
        var user = new User
        {
            Id = _userId,
            Username = "TestUser",
            Email = "test@example.com",
            Role = Role.Viewer,
            ProviderType = "local",
            PasswordHash = "secret-hash",
            PasswordSalt = "secret-salt"
        };

        _userRepository
            .Setup(m => m.GetByIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        // Act
        var response = await _client.GetAsync("/api/users/me").ConfigureAwait(false);

        // Assert
        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(json, Does.Not.Contain("secret-hash"));
            Assert.That(json, Does.Not.Contain("secret-salt"));
            Assert.That(json, Does.Not.Contain("PasswordHash"));
            Assert.That(json, Does.Not.Contain("PasswordSalt"));
        }
    }

    [Test]
    public async Task GetCurrentUser_ReturnsNotFound_WhenUserDoesNotExist()
    {
        // Arrange
        _userRepository
            .Setup(m => m.GetByIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(User?));

        // Act
        var response = await _client.GetAsync("/api/users/me").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task PatchCurrentUser_PatchesUser()
    {
        // Arrange
        var patch = new UserPatch { Id = _userId, Enabled = new() { Value = true } };

        _userRepository
            .Setup(m => m.PatchAsync(It.Is<UserPatch>(p => p.Id == _userId), _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.PatchAsJsonAsync("/api/users/me", patch).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task PatchCurrentUser_ReturnsNotFound_WhenUserDoesNotExist()
    {
        // Arrange
        var patch = new UserPatch { Id = _userId, Enabled = new() { Value = true } };

        _userRepository
            .Setup(m => m.PatchAsync(It.IsAny<UserPatch>(), _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var response = await _client.PatchAsJsonAsync("/api/users/me", patch).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetAllUsers_ReturnsEmptyArray_WhenNoUsers()
    {
        // Arrange
        _userRepository
            .Setup(m => m
                .QueryAsync(It.IsAny<UserQuery>(), It.IsAny<PagingParameter?>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User>
            {
                Items = [],
                PageNumber = null,
                PageSize = null,
                TotalItems = 0
            });

        // Act
        var response = await _client.GetAsync("/api/users").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<PagedList<UserResponse>>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Has.Property(nameof(content.Items)).Empty);
        }
    }

    [Test]
    public async Task GetAllUsers_ReturnsUsers()
    {
        // Arrange
        var user = new User
        {
            Username = "TestUser",
            Email = "test@example.com",
            Role = Role.Viewer,
            ProviderType = "local",
        };

        _userRepository
            .Setup(m => m
                .QueryAsync(It.IsAny<UserQuery>(), It.IsAny<PagingParameter?>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User>
            {
                Items = [user],
                PageNumber = 1,
                PageSize = 25,
                TotalItems = 1
            });

        // Act
        var response = await _client.GetAsync("/api/users").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<PagedList<UserResponse>>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(content.Items)).With.Count.EqualTo(1));
            Assert.That(content, Has.Property(nameof(content.Items)).With.One.With.Property(nameof(UserResponse.Username))
                .EqualTo("TestUser"));
            Assert.That(content, Has.Property(nameof(content.Items)).With.One.With.Property(nameof(UserResponse.Email))
                .EqualTo("test@example.com"));
        }
    }

    [Test]
    public async Task CreateUser_CreatesUser()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var request = new CreateUserRequest("NewUser", "plaintextpassword", Role.Viewer);

        var createdUser = new User
        {
            Id = userId,
            Username = "NewUser",
            Role = Role.Viewer,
            ProviderType = "local"
        };

        _userManager
            .Setup(m => m.CreateLocalUserAsync(
                It.Is<UserCreate>(c => c.Username == "NewUser" && c.PasswordHash == "hashed" && c.PasswordSalt == "salt"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdUser);

        // Act
        var response = await _client.PostAsJsonAsync("/api/users", request).ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<UserResponse>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.Created));
            Assert.That(content, Has.Property(nameof(content.Id)).EqualTo(userId));
            Assert.That(content, Has.Property(nameof(content.Username)).EqualTo("NewUser"));
        }
    }

    [Test]
    public async Task GetUserById_ReturnsUser()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            Username = "TestUser",
            Email = "test@example.com",
            Role = Role.Manager,
            ProviderType = "local"
        };

        _userRepository
            .Setup(m => m.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        // Act
        var response = await _client.GetAsync($"/api/users/{userId}").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<UserResponse>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(content.Id)).EqualTo(userId));
            Assert.That(content, Has.Property(nameof(content.Username)).EqualTo("TestUser"));
            Assert.That(content, Has.Property(nameof(content.Role)).EqualTo(Role.Manager));
        }
    }

    [Test]
    public async Task GetUserById_ReturnsNotFound_WhenUserDoesNotExist()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _userRepository
            .Setup(m => m.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(User?));

        // Act
        var response = await _client.GetAsync($"/api/users/{userId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task PatchUser_PatchesUser()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var patch = new UserPatch { Id = userId, Enabled = new() { Value = false } };

        _userRepository
            .Setup(m => m.PatchAsync(It.Is<UserPatch>(p => p.Id == userId), _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/users/{userId}", patch).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task PatchUser_ReturnsNotFound_WhenUserDoesNotExist()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var patch = new UserPatch { Id = userId, Enabled = new() { Value = false } };

        _userRepository
            .Setup(m => m.PatchAsync(It.IsAny<UserPatch>(), _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/users/{userId}", patch).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task EnableUser_EnablesUser()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            Username = "TestUser",
            Enabled = true,
            Role = Role.Viewer,
            ProviderType = "local"
        };

        _userRepository
            .Setup(m => m.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _userRepository
            .Setup(m => m.PatchAsync(
                It.Is<UserPatch>(p => p.Id == userId && p.Enabled!.Value),
                _userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.PostAsync($"/api/users/{userId}/enable", null).ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<UserResponse>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(content.Id)).EqualTo(userId));
        }
    }

    [Test]
    public async Task EnableUser_ReturnsNotFound_WhenUserDoesNotExist()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _userRepository
            .Setup(m => m.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(User?));

        // Act
        var response = await _client.PostAsync($"/api/users/{userId}/enable", null).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DisableUser_DisablesUser()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            Username = "TestUser",
            Enabled = false,
            Role = Role.Viewer,
            ProviderType = "local"
        };

        _userRepository
            .Setup(m => m.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _userRepository
            .Setup(m => m.PatchAsync(
                It.Is<UserPatch>(p => p.Id == userId && !p.Enabled!.Value),
                _userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.PostAsync($"/api/users/{userId}/disable", null).ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<UserResponse>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(content.Id)).EqualTo(userId));
        }
    }

    [Test]
    public async Task DisableUser_ReturnsNotFound_WhenUserDoesNotExist()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _userRepository
            .Setup(m => m.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(User?));

        // Act
        var response = await _client.PostAsync($"/api/users/{userId}/disable", null).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DeleteUser_DeletesUser()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _userRepository
            .Setup(m => m.DeleteAsync(userId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.DeleteAsync($"/api/users/{userId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task DeleteUser_ReturnsNotFound_WhenUserDoesNotExist()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _userRepository
            .Setup(m => m.DeleteAsync(userId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var response = await _client.DeleteAsync($"/api/users/{userId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }
}
