using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Data;
using Magpie.Api.EndpointGroups;
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
public class AuthProvidersEndpointGroupTests
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private Mock<IAuthProviderRepository> _authProviderRepositoryMock = null!;

    [SetUp]
    public void SetUp()
    {
        _authProviderRepositoryMock = new Mock<IAuthProviderRepository>();

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

        builder.Services.AddSingleton(_authProviderRepositoryMock.Object);
        builder.Services.AddSingleton<IApiEndpointGroup, AuthProvidersEndpointGroup>();

        var app = builder.Build();

        var endpointGroups = app.Services.GetServices<IApiEndpointGroup>();
        var apiGroup = app.MapGroup("/api");
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
    public async Task GetAllAuthProviders_ReturnsEmptyArray_WhenNoProviders()
    {
        // Arrange
        _authProviderRepositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<AuthProvider>>([]));

        // Act
        var response = await _client.GetAsync("/api/auth-providers").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<AuthProvider[]>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Is.Empty);
        }
    }

    [Test]
    public async Task GetAllAuthProviders_ReturnsProviders()
    {
        // Arrange
        var provider = new AuthProvider(
            Id: Guid.NewGuid(),
            Name: "Local",
            Provider: "local",
            Enabled: true,
            IsDefault: true,
            Settings: new Dictionary<string, string?>(),
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _authProviderRepositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<AuthProvider>>([provider]));

        // Act
        var response = await _client.GetAsync("/api/auth-providers").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<AuthProvider[]>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(AuthProvider.Name)).EqualTo("Local"));
            Assert.That(content, Has.One.With.Property(nameof(AuthProvider.Provider)).EqualTo("local"));
            Assert.That(content, Has.One.With.Property(nameof(AuthProvider.IsDefault)).True);
        }
    }

    [Test]
    public async Task GetAuthProviderById_ReturnsProvider()
    {
        // Arrange
        var providerId = Guid.NewGuid();
        var provider = new AuthProvider(
            Id: providerId,
            Name: "OIDC Provider",
            Provider: "oidc",
            Enabled: true,
            IsDefault: false,
            Settings: new Dictionary<string, string?>(),
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _authProviderRepositoryMock
            .Setup(r => r.GetByIdAsync(providerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<AuthProvider?>(provider));

        // Act
        var response = await _client.GetAsync($"/api/auth-providers/{providerId}").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<AuthProvider>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(AuthProvider.Id)).EqualTo(providerId));
            Assert.That(content, Has.Property(nameof(AuthProvider.Name)).EqualTo("OIDC Provider"));
            Assert.That(content, Has.Property(nameof(AuthProvider.Provider)).EqualTo("oidc"));
        }
    }

    [Test]
    public async Task GetAuthProviderById_ReturnsNotFound_WhenProviderDoesNotExist()
    {
        // Arrange
        var providerId = Guid.NewGuid();
        _authProviderRepositoryMock
            .Setup(r => r.GetByIdAsync(providerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<AuthProvider?>(null));

        // Act
        var response = await _client.GetAsync($"/api/auth-providers/{providerId}").ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
