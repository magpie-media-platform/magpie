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
using Magpie.Models;
using Magpie.Models.Indexers;
using Magpie.Models.Result;
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
    private Mock<IIndexerFeedRepository> _indexerFeedRepositoryMock = null!;

    [SetUp]
    public void SetUp()
    {
        _indexerFeedRepositoryMock = new Mock<IIndexerFeedRepository>();

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

        builder.Services.AddSingleton(_indexerFeedRepositoryMock.Object);
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
        _indexerFeedRepositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<IndexerFeed>>([]));

        // Act
        var response = await _client.GetAsync("/api/indexer-feeds").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<IndexerFeed[]>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Is.Empty);
        }
    }

    [Test]
    public async Task GetAllIndexerFeeds_ReturnsIndexerFeeds()
    {
        // Arrange
        var indexerFeed = new IndexerFeed(
            Id: Guid.NewGuid(),
            Name: "NZBgeek RSS",
            Description: "RSS feed for NZBgeek",
            Provider: "newznab",
            Enabled: true,
            Settings: new Dictionary<string, string?>(),
            LastSync: null,
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _indexerFeedRepositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<IndexerFeed>>([indexerFeed]));

        // Act
        var response = await _client.GetAsync("/api/indexer-feeds").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<IndexerFeed[]>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One
                .With.Property(nameof(IndexerFeed.Name)).EqualTo("NZBgeek RSS"));
            Assert.That(content, Has.One
                .With.Property(nameof(IndexerFeed.Description)).EqualTo("RSS feed for NZBgeek"));
            Assert.That(content, Has.One
                .With.Property(nameof(IndexerFeed.Provider)).EqualTo("newznab"));
            Assert.That(content, Has.One
                .With.Property(nameof(IndexerFeed.Enabled)).True);
        }
    }

    [Test]
    public async Task GetIndexerFeedById_ReturnsIndexerFeed()
    {
        // Arrange
        var feedId = Guid.NewGuid();
        var indexerFeed = new IndexerFeed(
            Id: feedId,
            Name: "Drunken Slug RSS",
            Description: "RSS feed for Drunken Slug",
            Provider: "newznab",
            Enabled: true,
            Settings: new Dictionary<string, string?>(),
            LastSync: null,
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _indexerFeedRepositoryMock
            .Setup(r => r.GetByIdAsync(feedId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IndexerFeed?>(indexerFeed));

        // Act
        var response = await _client.GetAsync($"/api/indexer-feeds/{feedId}").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<IndexerFeed>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(IndexerFeed.Id)).EqualTo(feedId));
            Assert.That(content, Has.Property(nameof(IndexerFeed.Name)).EqualTo("Drunken Slug RSS"));
            Assert.That(content, Has.Property(nameof(IndexerFeed.Provider)).EqualTo("newznab"));
        }
    }

    [Test]
    public async Task GetIndexerFeedById_ReturnsNotFound_WhenIndexerFeedDoesNotExist()
    {
        // Arrange
        var feedId = Guid.NewGuid();
        _indexerFeedRepositoryMock
            .Setup(r => r.GetByIdAsync(feedId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IndexerFeed?>(null));

        // Act
        var response = await _client.GetAsync($"/api/indexer-feeds/{feedId}").ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetIndexerFeedByName_ReturnsIndexerFeed()
    {
        // Arrange
        var indexerFeed = new IndexerFeed(
            Id: Guid.NewGuid(),
            Name: "IPTorrents RSS",
            Description: "RSS feed for IPTorrents",
            Provider: "torznab",
            Enabled: true,
            Settings: new Dictionary<string, string?>(),
            LastSync: null,
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _indexerFeedRepositoryMock
            .Setup(r => r.GetByNameAsync("IPTorrents RSS", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IndexerFeed?>(indexerFeed));

        // Act
        var response = await _client.GetAsync("/api/indexer-feeds/by-name/IPTorrents%20RSS").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<IndexerFeed>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(IndexerFeed.Name)).EqualTo("IPTorrents RSS"));
            Assert.That(content, Has.Property(nameof(IndexerFeed.Provider)).EqualTo("torznab"));
        }
    }

    [Test]
    public async Task GetIndexerFeedByName_ReturnsNotFound_WhenIndexerFeedDoesNotExist()
    {
        // Arrange
        _indexerFeedRepositoryMock
            .Setup(r => r.GetByNameAsync("NonExistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IndexerFeed?>(null));

        // Act
        var response = await _client.GetAsync("/api/indexer-feeds/by-name/NonExistent").ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetIndexerFeedsByProvider_ReturnsFilteredIndexerFeeds()
    {
        // Arrange
        var feed1 = new IndexerFeed(
            Id: Guid.NewGuid(),
            Name: "Newznab Feed 1",
            Description: null,
            Provider: "newznab",
            Enabled: true,
            Settings: new Dictionary<string, string?>(),
            LastSync: null,
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        var feed2 = new IndexerFeed(
            Id: Guid.NewGuid(),
            Name: "Newznab Feed 2",
            Description: null,
            Provider: "newznab",
            Enabled: true,
            Settings: new Dictionary<string, string?>(),
            LastSync: null,
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _indexerFeedRepositoryMock
            .Setup(r => r.GetByProviderAsync("newznab", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<IndexerFeed>>([feed1, feed2]));

        // Act
        var response = await _client.GetAsync("/api/indexer-feeds/by-provider/newznab").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<IndexerFeed[]>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(2));
            Assert.That(content, Has.All.With.Property(nameof(IndexerFeed.Provider)).EqualTo("newznab"));
        }
    }

    [Test]
    public async Task GetEnabledIndexerFeeds_ReturnsOnlyEnabledFeeds()
    {
        // Arrange
        var indexerFeed = new IndexerFeed(
            Id: Guid.NewGuid(),
            Name: "Enabled Feed",
            Description: null,
            Provider: "newznab",
            Enabled: true,
            Settings: new Dictionary<string, string?>(),
            LastSync: null,
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _indexerFeedRepositoryMock
            .Setup(r => r.GetEnabledAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<IndexerFeed>>([indexerFeed]));

        // Act
        var response = await _client.GetAsync("/api/indexer-feeds/enabled").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<IndexerFeed[]>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(IndexerFeed.Name)).EqualTo("Enabled Feed"));
            Assert.That(content, Has.One.With.Property(nameof(IndexerFeed.Enabled)).True);
        }
    }

    [Test]
    public async Task CreateIndexerFeed_CreatesAndReturnsIndexerFeed()
    {
        // Arrange
        var request = new IndexerFeed(
            Id: Guid.Empty,
            Name: "New Feed",
            Description: "A new RSS feed",
            Provider: "newznab",
            Enabled: true,
            Settings: new Dictionary<string, string?> { ["apiKey"] = "abc123", ["baseUrl"] = "https://example.com" },
            LastSync: null,
            CreatedAt: default,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _indexerFeedRepositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<IndexerFeed>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IndexerFeed f, Guid? _, CancellationToken _) => Result.Success(f));

        // Act
        var response = await _client.PostAsJsonAsync("/api/indexer-feeds", request).ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<IndexerFeed>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(content, Has.Property(nameof(IndexerFeed.Name)).EqualTo("New Feed"));
            Assert.That(content, Has.Property(nameof(IndexerFeed.Description)).EqualTo("A new RSS feed"));
            Assert.That(content, Has.Property(nameof(IndexerFeed.Provider)).EqualTo("newznab"));
            Assert.That(content, Has.Property(nameof(IndexerFeed.Enabled)).True);
            Assert.That(content!.Settings, Does.ContainKey("apiKey"));
            Assert.That(content.Settings["apiKey"], Is.EqualTo("abc123"));
            Assert.That(response.Headers.Location?.ToString(), Does.Contain($"/api/indexer-feeds/{content.Id}"));
        }
    }

    [Test]
    public async Task PatchIndexerFeed_PatchesNameField()
    {
        // Arrange
        var feedId = Guid.NewGuid();
        var request = new IndexerFeedPatch { Name = new FieldPatch<string>("Patched") };

        _indexerFeedRepositoryMock
            .Setup(r => r.PatchAsync(
                It.Is<IndexerFeedPatch>(p => p.Id == feedId && p.Name!.Value == "Patched"),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/indexer-feeds/{feedId}", request).ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task PatchIndexerFeed_PatchesEnabledField()
    {
        // Arrange
        var feedId = Guid.NewGuid();
        var request = new IndexerFeedPatch { Enabled = new FieldPatch<bool>(false) };

        _indexerFeedRepositoryMock
            .Setup(r => r.PatchAsync(
                It.Is<IndexerFeedPatch>(p => p.Id == feedId && p.Enabled!.Value == false),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/indexer-feeds/{feedId}", request).ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task PatchIndexerFeed_ReturnsNotFound_WhenIndexerFeedDoesNotExist()
    {
        // Arrange
        var feedId = Guid.NewGuid();
        var request = new IndexerFeedPatch { Name = new FieldPatch<string>("Patched") };

        _indexerFeedRepositoryMock
            .Setup(r => r.PatchAsync(It.IsAny<IndexerFeedPatch>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(false));

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/indexer-feeds/{feedId}", request).ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DeleteIndexerFeed_DeletesIndexerFeed()
    {
        // Arrange
        var feedId = Guid.NewGuid();

        _indexerFeedRepositoryMock
            .Setup(r => r.DeleteAsync(feedId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        // Act
        var response = await _client.DeleteAsync($"/api/indexer-feeds/{feedId}").ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task DeleteIndexerFeed_ReturnsNotFound_WhenIndexerFeedDoesNotExist()
    {
        // Arrange
        var feedId = Guid.NewGuid();

        _indexerFeedRepositoryMock
            .Setup(r => r.DeleteAsync(feedId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(false));

        // Act
        var response = await _client.DeleteAsync($"/api/indexer-feeds/{feedId}").ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
