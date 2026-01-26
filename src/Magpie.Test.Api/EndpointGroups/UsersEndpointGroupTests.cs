using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Security;
using Magpie.Api.EndpointGroups;
using Magpie.Core;
using Magpie.Models.Result;
using Magpie.Models.Users;
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
    private Mock<IUserManager> _userManagerMock = null!;

    [SetUp]
    public void SetUp()
    {
        _userManagerMock = new Mock<IUserManager>();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders().AddTestLogger();
        var authServiceMock = new Mock<IAuthenticationService>();
        authServiceMock
            .Setup(x => x.AuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<string>()))
            .ReturnsAsync(AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity("Test")), "Test")));
        builder.Services.AddSingleton(authServiceMock.Object);
        builder.Services.AddAuthorizationBuilder()
            .SetFallbackPolicy(null)
            .SetDefaultPolicy(new AuthorizationPolicyBuilder()
                .RequireAssertion(_ => true)
                .Build());

        builder.Services.AddSingleton(_userManagerMock.Object);
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
        _userManagerMock
            .Setup(m => m.GetUsersAsync(null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IEnumerable<User>>([]));

        // Act
        var response = await _client.GetAsync("/api/users").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<User[]>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Is.Empty);
        }
    }

    [Test]
    public async Task GetAllUsers_ReturnsUsers()
    {
        // Arrange
        var user = new User(
            Id: Guid.NewGuid(),
            Username: "TestUser",
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

        _userManagerMock
            .Setup(m => m.GetUsersAsync(null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IEnumerable<User>>([user]));

        // Act
        var response = await _client.GetAsync("/api/users").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<User[]>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content![0], Has.Property(nameof(User.Username)).EqualTo("TestUser"));
            Assert.That(content[0], Has.Property(nameof(User.Email)).EqualTo("test@example.com"));
        }
    }

    [Test]
    public async Task GetAllUsers_FiltersByEnabled()
    {
        // Arrange
        var user = new User(
            Id: Guid.NewGuid(),
            Username: "EnabledUser",
            Email: "enabled@example.com",
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

        _userManagerMock
            .Setup(m => m.GetUsersAsync(null, null, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IEnumerable<User>>([user]));

        // Act
        var response = await _client.GetAsync("/api/users?enabled=true").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<User[]>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
        }
    }

    [Test]
    public async Task GetUserById_ReturnsUser()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new User(
            Id: userId,
            Username: "TestUser",
            Email: "test@example.com",
            AuthProviderId: null,
            ExternalId: null,
            Enabled: true,
            Role: Role.Manager,
            ProviderData: null,
            LastLoginAt: null,
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _userManagerMock
            .Setup(m => m.GetUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<User?>(user));

        // Act
        var response = await _client.GetAsync($"/api/users/{userId}").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<User>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(User.Id)).EqualTo(userId));
            Assert.That(content, Has.Property(nameof(User.Username)).EqualTo("TestUser"));
            Assert.That(content, Has.Property(nameof(User.Role)).EqualTo(Role.Manager));
        }
    }

    [Test]
    public async Task GetUserById_ReturnsNotFound_WhenUserDoesNotExist()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _userManagerMock
            .Setup(m => m.GetUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<User?>(null));

        // Act
        var response = await _client.GetAsync($"/api/users/{userId}").ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task EnableUser_ReturnsNotFound_WhenUserDoesNotExist()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _userManagerMock
            .Setup(m => m.UpdateUserAsync(
                It.Is<UserPatch>(p => p.Id == userId && p.Enabled!.Value == true),
                Guid.Empty,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Fail<User>("User not found", ErrorCodes.UserNotFound));

        // Act
        var response = await _client.PostAsync($"/api/users/{userId}/enable", null).ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DisableUser_DisablesUser()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var disabledUser = new User(
            Id: userId,
            Username: "TestUser",
            Email: "test@example.com",
            AuthProviderId: null,
            ExternalId: null,
            Enabled: false,
            Role: Role.Viewer,
            ProviderData: null,
            LastLoginAt: null,
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: DateTimeOffset.UtcNow,
            ModifiedByUserId: null);

        _userManagerMock
            .Setup(m => m.UpdateUserAsync(
                It.Is<UserPatch>(p => p.Id == userId && p.Enabled!.Value == false),
                Guid.Empty,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(disabledUser));

        // Act
        var response = await _client.PostAsync($"/api/users/{userId}/disable", null).ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<User>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(User.Enabled)).False);
        }
    }

    [Test]
    public async Task DisableUser_ReturnsNotFound_WhenUserDoesNotExist()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _userManagerMock
            .Setup(m => m.UpdateUserAsync(
                It.Is<UserPatch>(p => p.Id == userId && p.Enabled!.Value == false),
                Guid.Empty,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Fail<User>("User not found", ErrorCodes.UserNotFound));

        // Act
        var response = await _client.PostAsync($"/api/users/{userId}/disable", null).ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task ChangeRole_ReturnsNotFound_WhenUserDoesNotExist()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _userManagerMock
            .Setup(m => m.UpdateUserAsync(
                It.Is<UserPatch>(p => p.Id == userId),
                Guid.Empty,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Fail<User>("User not found", ErrorCodes.UserNotFound));

        // Act
        var request = new { Role = Role.Admin };
        var response = await _client.PostAsJsonAsync($"/api/users/{userId}/role", request).ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DeleteUser_DeletesUser()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _userManagerMock
            .Setup(m => m.DeleteUserAsync(userId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        // Act
        var response = await _client.DeleteAsync($"/api/users/{userId}").ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task DeleteUser_ReturnsNotFound_WhenUserDoesNotExist()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _userManagerMock
            .Setup(m => m.DeleteUserAsync(userId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Fail<bool>("User not found", ErrorCodes.UserNotFound));

        // Act
        var response = await _client.DeleteAsync($"/api/users/{userId}").ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
