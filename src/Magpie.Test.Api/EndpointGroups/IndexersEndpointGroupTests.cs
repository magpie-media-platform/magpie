using System;
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
public class IndexersEndpointGroupTests
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private Mock<IIndexerRepository> _indexerRepository = null!;

    [SetUp]
    public void SetUp()
    {
        _indexerRepository = new Mock<IIndexerRepository>();

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

        builder.Services.AddSingleton(_indexerRepository.Object);
        builder.Services.AddSingleton<IApiEndpointGroup, IndexersEndpointGroup>();

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
    public async Task GetAllIndexers_ReturnsEmptyArray_WhenNoIndexers()
    {
        // Arrange
        _indexerRepository
            .Setup(m => m.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var response = await _client.GetAsync("/api/indexers").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<Indexer[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Is.Empty);
        }
    }

    [Test]
    public async Task GetAllIndexers_ReturnsIndexers()
    {
        // Arrange
        var indexer = new Indexer { Name = "NZBgeek", ProviderType = "newznab" };

        _indexerRepository
            .Setup(m => m.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([indexer]);

        // Act
        var response = await _client.GetAsync("/api/indexers").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<Indexer[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(Indexer.Name)).EqualTo("NZBgeek"));
            Assert.That(content, Has.One.With.Property(nameof(Indexer.ProviderType)).EqualTo("newznab"));
        }
    }

    [Test]
    public async Task GetIndexerById_ReturnsIndexer()
    {
        // Arrange
        var indexerId = Guid.NewGuid();
        var indexer = new Indexer { Id = indexerId, Name = "Drunken Slug", ProviderType = "newznab" };

        _indexerRepository
            .Setup(m => m.GetByIdAsync(indexerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(indexer);

        // Act
        var response = await _client.GetAsync($"/api/indexers/{indexerId}").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<Indexer>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(content.Id)).EqualTo(indexerId));
            Assert.That(content, Has.Property(nameof(content.Name)).EqualTo("Drunken Slug"));
            Assert.That(content, Has.Property(nameof(content.ProviderType)).EqualTo("newznab"));
        }
    }

    [Test]
    public async Task GetIndexerById_ReturnsNotFound_WhenIndexerDoesNotExist()
    {
        // Arrange
        var indexerId = Guid.NewGuid();
        _indexerRepository
            .Setup(m => m.GetByIdAsync(indexerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(Indexer?));

        // Act
        var response = await _client.GetAsync($"/api/indexers/{indexerId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task CreateIndexer_CreatesAndReturnsIndexer()
    {
        // Arrange
        var request = new Indexer
        {
            Name = "New Indexer",
            ProviderType = "newznab",
            Availability = IndexerAvailability.Public,
            Categories = ["Movies", "TV"]
        };

        _indexerRepository
            .Setup(m => m.CreateAsync(It.IsAny<Indexer>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Indexer i, Guid? _, CancellationToken _) => i);

        // Act
        var response = await _client.PostAsJsonAsync("/api/indexers", request).ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<Indexer>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.Created));
            Assert.That(content, Has.Property(nameof(content.Name)).EqualTo("New Indexer"));
            Assert.That(content, Has.Property(nameof(content.ProviderType)).EqualTo("newznab"));
            Assert.That(content, Has.Property(nameof(content.Availability)).EqualTo(IndexerAvailability.Public));
        }
    }

    [Test]
    public async Task UpdateIndexer_UpdatesIndexer()
    {
        // Arrange
        var indexerId = Guid.NewGuid();
        var existingIndexer = new Indexer { Id = indexerId, Name = "Original Indexer", ProviderType = "newznab" };

        var request = new Indexer
        {
            Name = "Updated Indexer",
            ProviderType = "newznab",
            Availability = IndexerAvailability.Private
        };

        _indexerRepository
            .Setup(m => m.GetByIdAsync(indexerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingIndexer);

        _indexerRepository
            .Setup(m => m.UpdateAsync(It.IsAny<Indexer>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.PutAsJsonAsync($"/api/indexers/{indexerId}", request).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
        _indexerRepository.Verify(
            r => r.UpdateAsync(
                It.Is<Indexer>(i =>
                    i.Id == indexerId &&
                    i.Name == "Updated Indexer"),
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task UpdateIndexer_ReturnsNotFound_WhenIndexerDoesNotExist()
    {
        // Arrange
        var indexerId = Guid.NewGuid();
        var request = new Indexer
        {
            Name = "Updated Indexer",
            ProviderType = "newznab"
        };

        _indexerRepository
            .Setup(m => m.GetByIdAsync(indexerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(Indexer?));

        // Act
        var response = await _client.PutAsJsonAsync($"/api/indexers/{indexerId}", request).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DeleteIndexer_DeletesIndexer()
    {
        // Arrange
        var indexerId = Guid.NewGuid();

        _indexerRepository
            .Setup(m => m.DeleteAsync(indexerId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.DeleteAsync($"/api/indexers/{indexerId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task DeleteIndexer_ReturnsNotFound_WhenIndexerDoesNotExist()
    {
        // Arrange
        var indexerId = Guid.NewGuid();

        _indexerRepository
            .Setup(m => m.DeleteAsync(indexerId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var response = await _client.DeleteAsync($"/api/indexers/{indexerId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }
}
