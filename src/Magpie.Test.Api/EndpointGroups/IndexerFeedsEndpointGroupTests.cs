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
public class IndexerFeedsEndpointGroupTests
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private Mock<IIndexerFeedRepository> _indexerFeedRepository = null!;

    [SetUp]
    public void SetUp()
    {
        _indexerFeedRepository = new Mock<IIndexerFeedRepository>();

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

        builder.Services.AddSingleton(_indexerFeedRepository.Object);
        builder.Services.AddSingleton<IApiEndpointGroup, IndexerFeedsEndpointGroup>();

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
    public async Task GetAllIndexerFeeds_ReturnsEmptyArray_WhenNoIndexerFeeds()
    {
        // Arrange
        _indexerFeedRepository
            .Setup(m => m.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var response = await _client.GetAsync("/api/indexer-feeds").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<IndexerFeed[]>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Is.Empty);
        }
    }

    [Test]
    public async Task GetAllIndexerFeeds_ReturnsIndexerFeeds()
    {
        // Arrange
        var indexerFeed = new IndexerFeed
        {
            Name = "NZBgeek RSS",
            ProviderType = "newznab",
            Description = "RSS feed for NZBgeek",
            Enabled = true
        };

        _indexerFeedRepository
            .Setup(m => m.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([indexerFeed]);

        // Act
        var response = await _client.GetAsync("/api/indexer-feeds").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<IndexerFeed[]>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One
                .With.Property(nameof(IndexerFeed.Name)).EqualTo("NZBgeek RSS"));
            Assert.That(content, Has.One
                .With.Property(nameof(IndexerFeed.Description)).EqualTo("RSS feed for NZBgeek"));
            Assert.That(content, Has.One
                .With.Property(nameof(IndexerFeed.ProviderType)).EqualTo("newznab"));
            Assert.That(content, Has.One
                .With.Property(nameof(IndexerFeed.Enabled)).True);
        }
    }

    [Test]
    public async Task GetIndexerFeedById_ReturnsIndexerFeed()
    {
        // Arrange
        var feedId = Guid.NewGuid();
        var indexerFeed = new IndexerFeed
        {
            Id = feedId,
            Name = "Drunken Slug RSS",
            ProviderType = "newznab",
            Description = "RSS feed for Drunken Slug",
            Enabled = true
        };

        _indexerFeedRepository
            .Setup(m => m.GetByIdAsync(feedId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(indexerFeed);

        // Act
        var response = await _client.GetAsync($"/api/indexer-feeds/{feedId}").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<IndexerFeed>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(content.Id)).EqualTo(feedId));
            Assert.That(content, Has.Property(nameof(content.Name)).EqualTo("Drunken Slug RSS"));
            Assert.That(content, Has.Property(nameof(content.ProviderType)).EqualTo("newznab"));
        }
    }

    [Test]
    public async Task GetIndexerFeedById_ReturnsNotFound_WhenIndexerFeedDoesNotExist()
    {
        // Arrange
        var feedId = Guid.NewGuid();
        _indexerFeedRepository
            .Setup(m => m.GetByIdAsync(feedId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(IndexerFeed?));

        // Act
        var response = await _client.GetAsync($"/api/indexer-feeds/{feedId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task CreateIndexerFeed_CreatesAndReturnsIndexerFeed()
    {
        // Arrange
        var request = new IndexerFeed
        {
            Name = "New Feed",
            ProviderType = "newznab",
            Description = "A new RSS feed",
            Enabled = true,
            Settings = new Dictionary<string, string> { ["apiKey"] = "abc123", ["baseUrl"] = "https://example.com" }
        };

        _indexerFeedRepository
            .Setup(m => m.CreateAsync(It.IsAny<IndexerFeed>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IndexerFeed f, Guid? _, CancellationToken _) => f);

        // Act
        var response = await _client.PostAsJsonAsync("/api/indexer-feeds", request).ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<IndexerFeed>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.Created));
            Assert.That(content, Has.Property(nameof(content.Name)).EqualTo("New Feed"));
            Assert.That(content, Has.Property(nameof(content.Description)).EqualTo("A new RSS feed"));
            Assert.That(content, Has.Property(nameof(content.ProviderType)).EqualTo("newznab"));
            Assert.That(content, Has.Property(nameof(content.Enabled)).True);
            Assert.That(content, Has.Property(nameof(content.Settings)).ContainKey("apiKey"));
            Assert.That(content, Has.Property(nameof(content.Settings)).ContainValue("abc123"));
        }
    }

    [Test]
    public async Task UpdateIndexerFeed_UpdatesIndexerFeed()
    {
        // Arrange
        var feedId = Guid.NewGuid();
        var existingFeed = new IndexerFeed
        {
            Id = feedId,
            Name = "Original Feed",
            ProviderType = "newznab",
            Enabled = true
        };
        var request = new IndexerFeed
        {
            Name = "Updated Feed",
            ProviderType = "newznab",
            Enabled = false
        };

        _indexerFeedRepository
            .Setup(m => m.GetByIdAsync(feedId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingFeed);

        _indexerFeedRepository
            .Setup(m => m.UpdateAsync(It.IsAny<IndexerFeed>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.PutAsJsonAsync($"/api/indexer-feeds/{feedId}", request).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
        _indexerFeedRepository.Verify(
            r => r.UpdateAsync(
                It.Is<IndexerFeed>(f =>
                    f.Id == feedId &&
                    f.Name == "Updated Feed" &&
                    f.Enabled == false),
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task UpdateIndexerFeed_ReturnsNotFound_WhenIndexerFeedDoesNotExist()
    {
        // Arrange
        var feedId = Guid.NewGuid();
        var request = new IndexerFeed
        {
            Name = "Updated Feed",
            ProviderType = "newznab",
            Enabled = true
        };

        _indexerFeedRepository
            .Setup(m => m.GetByIdAsync(feedId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(IndexerFeed?));

        // Act
        var response = await _client.PutAsJsonAsync($"/api/indexer-feeds/{feedId}", request).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DeleteIndexerFeed_DeletesIndexerFeed()
    {
        // Arrange
        var feedId = Guid.NewGuid();

        _indexerFeedRepository
            .Setup(m => m.DeleteAsync(feedId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.DeleteAsync($"/api/indexer-feeds/{feedId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task DeleteIndexerFeed_ReturnsNotFound_WhenIndexerFeedDoesNotExist()
    {
        // Arrange
        var feedId = Guid.NewGuid();

        _indexerFeedRepository
            .Setup(m => m.DeleteAsync(feedId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var response = await _client.DeleteAsync($"/api/indexer-feeds/{feedId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }
}
