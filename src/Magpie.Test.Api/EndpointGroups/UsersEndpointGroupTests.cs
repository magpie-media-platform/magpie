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
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;
using Magpie.Models.Patch;
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

    [SetUp]
    public void SetUp()
    {
        _userRepository = new Mock<IUserRepository>();
        _userManager = new Mock<IUserManager>();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders().AddTestLogger();
        var authServiceMock = new Mock<IAuthenticationService>();
        authServiceMock
            .Setup(m => m.AuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<string>()))
            .ReturnsAsync(AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity("Test")), "Test")));
        builder.Services.AddSingleton(authServiceMock.Object);
        builder.Services.AddAuthorizationBuilder()
            .SetFallbackPolicy(null)
            .SetDefaultPolicy(new AuthorizationPolicyBuilder()
                .RequireAssertion(_ => true)
                .Build());

        builder.Services.AddSingleton(_userRepository.Object);
        builder.Services.AddSingleton(_userManager.Object);
        builder.Services.AddSingleton<IApiEndpointGroup, UsersEndpointGroup>();

        var app = builder.Build();

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
    public async Task GetAllUsers_ReturnsEmptyArray_WhenNoUsers()
    {
        // Arrange
        _userRepository
            .Setup(m => m
                .QueryAsync(It.IsAny<UserQuery>(), It.IsAny<PagingParameter?>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User>([], null, null, 0));

        // Act
        var response = await _client.GetAsync("/api/users").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<PagedList<User>>().ConfigureAwait(false);

        // Assert
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
            .ReturnsAsync(new PagedList<User>([user], 1, 25, 1));

        // Act
        var response = await _client.GetAsync("/api/users").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<PagedList<User>>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(content.Items)).With.Count.EqualTo(1));
            Assert.That(content, Has.Property(nameof(content.Items)).With.One.With.Property(nameof(User.Username))
                .EqualTo("TestUser"));
            Assert.That(content, Has.Property(nameof(content.Items)).With.One.With.Property(nameof(User.Email))
                .EqualTo("test@example.com"));
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
        var content = await response.Content.ReadFromJsonAsync<User>().ConfigureAwait(false);

        // Assert
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
        var patch = new UserPatch(userId, Enabled: new ScalarPatch<bool>(false));

        _userRepository
            .Setup(m => m.PatchAsync(It.Is<UserPatch>(p => p.Id == userId), null, It.IsAny<CancellationToken>()))
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
        var patch = new UserPatch(userId, Enabled: new ScalarPatch<bool>(false));

        _userRepository
            .Setup(m => m.PatchAsync(It.IsAny<UserPatch>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/users/{userId}", patch).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DeleteUser_DeletesUser()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _userRepository
            .Setup(m => m.DeleteAsync(userId, null, It.IsAny<CancellationToken>()))
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
            .Setup(m => m.DeleteAsync(userId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var response = await _client.DeleteAsync($"/api/users/{userId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }
}
