using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Api.EndpointGroups;
using Magpie.Models.Data;
using Magpie.Models.Enums;
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
public class ContentEndpointGroupTests
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private Mock<IContentHierarchyItemRepository> _contentHierarchyItemRepository = null!;
    private Mock<IJobManager> _jobManager = null!;
    private Guid _userId;

    [SetUp]
    public void SetUp()
    {
        _userId = Guid.NewGuid();
        _contentHierarchyItemRepository = new Mock<IContentHierarchyItemRepository>();
        _jobManager = new Mock<IJobManager>();

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
            .AddPolicy(nameof(Role.Requester), policy => policy.RequireAssertion(_ => true))
            .AddPolicy(nameof(Role.Manager), policy => policy.RequireAssertion(_ => true))
            .AddPolicy(nameof(Role.Admin), policy => policy.RequireAssertion(_ => true));

        builder.Services.AddSingleton(_contentHierarchyItemRepository.Object);
        builder.Services.AddSingleton(_jobManager.Object);
        builder.Services.AddSingleton<IApiEndpointGroup, ContentEndpointGroup>();

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
    public async Task GetContentStats_ReturnsAggregateCounts()
    {
        // Arrange
        var stats = new ContentStats
        {
            Total = 42,
            Missing = 7,
            Upgradable = 3,
            Fulfilled = 30,
            FulfilledExternally = 2
        };

        _contentHierarchyItemRepository
            .Setup(m => m.GetStatsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(stats);

        // Act
        var response = await _client.GetAsync("/api/content/stats").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<ContentStats>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Has.Property(nameof(content.Total)).EqualTo(42));
            Assert.That(content, Has.Property(nameof(content.Missing)).EqualTo(7));
            Assert.That(content, Has.Property(nameof(content.Upgradable)).EqualTo(3));
            Assert.That(content, Has.Property(nameof(content.Fulfilled)).EqualTo(30));
            Assert.That(content, Has.Property(nameof(content.FulfilledExternally)).EqualTo(2));
        }
    }

    [Test]
    public async Task GetContentById_ReturnsContent()
    {
        // Arrange
        var id = Guid.NewGuid();
        var item = new ContentHierarchyItem
        {
            Id = id,
            ContentType = "Episodes",
            Level = "Series",
            Slug = "test-series",
            Name = "Test Series"
        };

        _contentHierarchyItemRepository
            .Setup(m => m.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);

        // Act
        var response = await _client.GetAsync($"/api/content/{id}").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<ContentHierarchyItem>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Has.Property(nameof(content.Id)).EqualTo(id));
            Assert.That(content, Has.Property(nameof(content.Name)).EqualTo("Test Series"));
        }
    }

    [Test]
    public async Task GetContentById_ReturnsNotFound_WhenContentDoesNotExist()
    {
        // Arrange
        var id = Guid.NewGuid();

        _contentHierarchyItemRepository
            .Setup(m => m.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(ContentHierarchyItem?));

        // Act
        var response = await _client.GetAsync($"/api/content/{id}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task RefreshContent_ReturnsAccepted_AndDispatchesJob()
    {
        // Arrange
        var id = Guid.NewGuid();
        var executionId = Guid.NewGuid();
        var item = new ContentHierarchyItem
        {
            Id = id,
            ContentType = "Episodes",
            Level = "Series",
            Slug = "test-series",
            Name = "Test Series"
        };

        _contentHierarchyItemRepository
            .Setup(m => m.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);

        _jobManager
            .Setup(m => m.ExecuteAsync(
                "RefreshMetadata",
                null,
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                JobExecutionMode.Queue,
                _userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(executionId);

        // Act
        var response = await _client.PostAsync($"/api/content/{id}/refresh", null).ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<Guid>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.Accepted));
            Assert.That(content, Is.EqualTo(executionId));
        }

        _jobManager.Verify(
            m => m.ExecuteAsync(
                "RefreshMetadata",
                null,
                It.Is<IReadOnlyDictionary<string, string>>(p =>
                    p.ContainsKey("contentHierarchyItemId") &&
                    p["contentHierarchyItemId"] == id.ToString()),
                JobExecutionMode.Queue,
                _userId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task RefreshContent_ReturnsNotFound_WhenContentDoesNotExist()
    {
        // Arrange
        var id = Guid.NewGuid();

        _contentHierarchyItemRepository
            .Setup(m => m.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(ContentHierarchyItem?));

        // Act
        var response = await _client.PostAsync($"/api/content/{id}/refresh", null).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task SearchContent_ReturnsAccepted_AndDispatchesJob()
    {
        // Arrange
        var id = Guid.NewGuid();
        var executionId = Guid.NewGuid();
        var item = new ContentHierarchyItem
        {
            Id = id,
            ContentType = "Episodes",
            Level = "Series",
            Slug = "test-series",
            Name = "Test Series"
        };

        _contentHierarchyItemRepository
            .Setup(m => m.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);

        _jobManager
            .Setup(m => m.ExecuteAsync(
                "ReleaseSearch",
                null,
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                JobExecutionMode.Queue,
                _userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(executionId);

        // Act
        var response = await _client.PostAsync($"/api/content/{id}/search", null).ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<Guid>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.Accepted));
            Assert.That(content, Is.EqualTo(executionId));
        }

        _jobManager.Verify(
            m => m.ExecuteAsync(
                "ReleaseSearch",
                null,
                It.Is<IReadOnlyDictionary<string, string>>(p =>
                    p.ContainsKey("contentHierarchyItemId") &&
                    p["contentHierarchyItemId"] == id.ToString()),
                JobExecutionMode.Queue,
                _userId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task SearchContent_ReturnsNotFound_WhenContentDoesNotExist()
    {
        // Arrange
        var id = Guid.NewGuid();

        _contentHierarchyItemRepository
            .Setup(m => m.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(ContentHierarchyItem?));

        // Act
        var response = await _client.PostAsync($"/api/content/{id}/search", null).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }
}
