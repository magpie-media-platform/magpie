using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
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
public class TagsEndpointGroupTests
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private Mock<ITagRepository> _tagRepository = null!;

    [SetUp]
    public void SetUp()
    {
        _tagRepository = new Mock<ITagRepository>();

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
                .Build())
            .AddPolicy(nameof(Role.Viewer), policy => policy.RequireAssertion(_ => true));

        builder.Services.AddSingleton(_tagRepository.Object);
        builder.Services.AddSingleton<IApiEndpointGroup, TagsEndpointGroup>();

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
    public async Task GetAllTags_ReturnsEmptyArray_WhenNoTags()
    {
        // Arrange
        _tagRepository
            .Setup(m => m.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var response = await _client.GetAsync("/api/tags").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<Tag[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Is.Empty);
        }
    }

    [Test]
    public async Task GetAllTags_ReturnsTags()
    {
        // Arrange
        var tags = new List<Tag>
        {
            new Tag { Name = "action" },
            new Tag { Name = "comedy" },
            new Tag { Name = "drama" }
        };

        _tagRepository
            .Setup(m => m.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(tags);

        // Act
        var response = await _client.GetAsync("/api/tags").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<Tag[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Has.Length.EqualTo(3));
            Assert.That(content, Has.One.With.Property(nameof(Tag.Name)).EqualTo("action"));
            Assert.That(content, Has.One.With.Property(nameof(Tag.Name)).EqualTo("comedy"));
            Assert.That(content, Has.One.With.Property(nameof(Tag.Name)).EqualTo("drama"));
        }
    }

    [Test]
    public async Task GetTags_ReturnsMatchingTags_WhenPrefixProvided()
    {
        // Arrange
        var matchingTags = new List<Tag>
        {
            new Tag { Name = "genre:action" },
            new Tag { Name = "genre:comedy" },
            new Tag { Name = "genre:drama" }
        };

        _tagRepository
            .Setup(m => m.GetByPrefixAsync("genre:", It.IsAny<CancellationToken>()))
            .ReturnsAsync(matchingTags);

        // Act
        var response = await _client.GetAsync("/api/tags?prefix=genre:").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<Tag[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Has.Length.EqualTo(3));
            Assert.That(content, Has.All.With.Property(nameof(Tag.Name)).StartsWith("genre:"));
        }
    }

    [Test]
    public async Task GetTags_ReturnsEmptyArray_WhenPrefixHasNoMatches()
    {
        // Arrange
        _tagRepository
            .Setup(m => m.GetByPrefixAsync("rating:", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var response = await _client.GetAsync("/api/tags?prefix=rating:").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<Tag[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Is.Empty);
        }
    }

    [Test]
    public async Task GetTags_ReturnsMatchingTags_WhenEntityIdProvided()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var matchingTags = new List<Tag>
        {
            new Tag { Name = "genre:action" },
            new Tag { Name = "quality:hd" }
        };

        _tagRepository
            .Setup(m => m.GetTagsByEntityAsync(entityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(matchingTags);

        // Act
        var response = await _client.GetAsync($"/api/tags?entityId={entityId}").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<Tag[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Has.Length.EqualTo(2));
        }
    }

    [Test]
    public async Task GetTags_ReturnsEmptyArray_WhenEntityIdHasNoTags()
    {
        // Arrange
        var entityId = Guid.NewGuid();

        _tagRepository
            .Setup(m => m.GetTagsByEntityAsync(entityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var response = await _client.GetAsync($"/api/tags?entityId={entityId}").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<Tag[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Is.Empty);
        }
    }
}
