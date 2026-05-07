using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Integrations;
using Magpie.Abstractions.Repositories;
using Magpie.Api.EndpointGroups;
using Magpie.Models;
using Magpie.Models.Arguments;
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
    private Mock<IIndexerConnectionRepository> _indexerConnectionRepository = null!;
    private FakeIndexerIntegration _integration = null!;

    [SetUp]
    public void SetUp()
    {
        _indexerRepository = new Mock<IIndexerRepository>();
        _indexerConnectionRepository = new Mock<IIndexerConnectionRepository>();
        _integration = new FakeIndexerIntegration();

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
            .AddPolicy(nameof(Role.Viewer), policy => policy.RequireAssertion(_ => true))
            .AddPolicy(nameof(Role.Manager), policy => policy.RequireAssertion(_ => true));

        builder.Services.AddSingleton(_indexerRepository.Object);
        builder.Services.AddSingleton(_indexerConnectionRepository.Object);
        builder.Services.AddSingleton<IIndexerIntegration>(_integration);
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
    public async Task PatchIndexer_PatchesIndexer()
    {
        // Arrange
        var indexerId = Guid.NewGuid();
        var existingIndexer = new Indexer
        {
            Id = indexerId,
            Name = "Original Indexer",
            ProviderType = "newznab",
            Availability = IndexerAvailability.Public,
            Categories = ["Movies"],
            Languages = ["en"]
        };

        var request = new IndexerPatch { Name = "Updated Indexer" };

        _indexerRepository
            .Setup(m => m.GetByIdAsync(indexerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingIndexer);

        _indexerRepository
            .Setup(m => m.UpdateAsync(It.IsAny<Indexer>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/indexers/{indexerId}", request).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
        _indexerRepository.Verify(
            r => r.UpdateAsync(
                It.Is<Indexer>(i =>
                    i.Id == indexerId &&
                    i.Name == "Updated Indexer" &&
                    i.ProviderType == "newznab" &&
                    i.Availability == IndexerAvailability.Public &&
                    i.Categories.SequenceEqual(new[] { "Movies" }) &&
                    i.Languages.SequenceEqual(new[] { "en" })),
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task PatchIndexer_ReturnsNotFound_WhenIndexerDoesNotExist()
    {
        // Arrange
        var indexerId = Guid.NewGuid();
        var request = new IndexerPatch
        {
            Name = "Updated Indexer",
            ProviderType = "newznab"
        };

        _indexerRepository
            .Setup(m => m.GetByIdAsync(indexerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(Indexer?));

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/indexers/{indexerId}", request).ConfigureAwait(false);

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

    [Test]
    public async Task TestIndexer_DispatchesToMatchingIntegration_WithSoleConnectionSettings()
    {
        // Arrange
        var indexerId = Guid.NewGuid();
        var indexer = new Indexer { Id = indexerId, Name = "Drunken Slug", ProviderType = "fake" };
        var connection = new IndexerConnection
        {
            Id = Guid.NewGuid(),
            IndexerId = indexerId,
            Name = "primary",
            Settings = new Dictionary<string, string>
            {
                ["url"] = "https://example.test",
                ["apiKey"] = "secret-1"
            }
        };

        _indexerRepository
            .Setup(m => m.GetByIdAsync(indexerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(indexer);
        _indexerConnectionRepository
            .Setup(m => m.ListByIndexerIdAsync(indexerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([connection]);

        // Act
        var response = await _client.PostAsync($"/api/indexers/{indexerId}/test", null)
            .ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<ConnectionTestResult>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(content.Success)).True);
            Assert.That(content, Has.Property(nameof(content.Message)).EqualTo("ok"));
            Assert.That(_integration.WasCalled, Is.True);
            Assert.That(_integration.LastSettings, Is.Not.Null);
            Assert.That(_integration.LastSettings, Is.Not.Empty);
            Assert.That(_integration.LastSettings!["url"], Is.EqualTo("https://example.test"));
            Assert.That(_integration.LastSettings!["apiKey"], Is.EqualTo("secret-1"));
        }
    }

    [Test]
    public async Task TestIndexer_UsesExplicitConnectionId_WhenMultipleConnectionsExist()
    {
        // Arrange
        var indexerId = Guid.NewGuid();
        var indexer = new Indexer { Id = indexerId, Name = "Multi", ProviderType = "fake" };
        var connectionA = new IndexerConnection
        {
            Id = Guid.NewGuid(),
            IndexerId = indexerId,
            Name = "A",
            Settings = new Dictionary<string, string> { ["apiKey"] = "key-a" }
        };
        var connectionB = new IndexerConnection
        {
            Id = Guid.NewGuid(),
            IndexerId = indexerId,
            Name = "B",
            Settings = new Dictionary<string, string> { ["apiKey"] = "key-b" }
        };

        _indexerRepository
            .Setup(m => m.GetByIdAsync(indexerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(indexer);
        _indexerConnectionRepository
            .Setup(m => m.ListByIndexerIdAsync(indexerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([connectionA, connectionB]);

        // Act
        var response = await _client
            .PostAsync($"/api/indexers/{indexerId}/test?connectionId={connectionB.Id}", null)
            .ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<ConnectionTestResult>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(content.Success)).True);
            Assert.That(_integration.LastSettings!["apiKey"], Is.EqualTo("key-b"));
        }
    }

    [Test]
    public async Task TestIndexer_ReturnsFailureResult_WhenNoConnectionsConfigured()
    {
        // Arrange
        var indexerId = Guid.NewGuid();
        var indexer = new Indexer { Id = indexerId, Name = "NoConn", ProviderType = "fake" };

        _indexerRepository
            .Setup(m => m.GetByIdAsync(indexerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(indexer);
        _indexerConnectionRepository
            .Setup(m => m.ListByIndexerIdAsync(indexerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var response = await _client.PostAsync($"/api/indexers/{indexerId}/test", null)
            .ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<ConnectionTestResult>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(content.Success)).False);
            Assert.That(content!.Message, Does.Contain("No connections"));
            Assert.That(_integration.WasCalled, Is.False);
        }
    }

    [Test]
    public async Task TestIndexer_ReturnsFailureResult_WhenMultipleConnectionsAndNoConnectionId()
    {
        // Arrange
        var indexerId = Guid.NewGuid();
        var indexer = new Indexer { Id = indexerId, Name = "Multi", ProviderType = "fake" };
        var connections = new[]
        {
            new IndexerConnection { Id = Guid.NewGuid(), IndexerId = indexerId, Name = "A" },
            new IndexerConnection { Id = Guid.NewGuid(), IndexerId = indexerId, Name = "B" }
        };

        _indexerRepository
            .Setup(m => m.GetByIdAsync(indexerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(indexer);
        _indexerConnectionRepository
            .Setup(m => m.ListByIndexerIdAsync(indexerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connections);

        // Act
        var response = await _client.PostAsync($"/api/indexers/{indexerId}/test", null)
            .ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<ConnectionTestResult>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(content.Success)).False);
            Assert.That(content!.Message, Does.Contain("Multiple connections"));
            Assert.That(_integration.WasCalled, Is.False);
        }
    }

    [Test]
    public async Task TestIndexer_ReturnsFailureResult_WhenConnectionIdNotFound()
    {
        // Arrange
        var indexerId = Guid.NewGuid();
        var indexer = new Indexer { Id = indexerId, Name = "NoMatch", ProviderType = "fake" };
        var connection = new IndexerConnection { Id = Guid.NewGuid(), IndexerId = indexerId, Name = "A" };
        var bogusConnectionId = Guid.NewGuid();

        _indexerRepository
            .Setup(m => m.GetByIdAsync(indexerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(indexer);
        _indexerConnectionRepository
            .Setup(m => m.ListByIndexerIdAsync(indexerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([connection]);

        // Act
        var response = await _client
            .PostAsync($"/api/indexers/{indexerId}/test?connectionId={bogusConnectionId}", null)
            .ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<ConnectionTestResult>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(content.Success)).False);
            Assert.That(content!.Message, Does.Contain("not found"));
            Assert.That(_integration.WasCalled, Is.False);
        }
    }

    [Test]
    public async Task TestIndexer_ReturnsNotFound_WhenIndexerDoesNotExist()
    {
        // Arrange
        var indexerId = Guid.NewGuid();

        _indexerRepository
            .Setup(m => m.GetByIdAsync(indexerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(Indexer?));

        // Act
        var response = await _client.PostAsync($"/api/indexers/{indexerId}/test", null)
            .ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task TestIndexer_ReturnsFailureResult_WhenNoMatchingIntegrationRegistered()
    {
        // Arrange
        var indexerId = Guid.NewGuid();
        var indexer = new Indexer { Id = indexerId, Name = "Other", ProviderType = "unknown-provider" };

        _indexerRepository
            .Setup(m => m.GetByIdAsync(indexerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(indexer);

        // Act
        var response = await _client.PostAsync($"/api/indexers/{indexerId}/test", null)
            .ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<ConnectionTestResult>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(content.Success)).False);
            Assert.That(content!.Message, Does.Contain("unknown-provider"));
        }
    }

    private sealed class FakeIndexerIntegration : IIndexerIntegration
    {
        public string IndexerType => "fake";

        public bool WasCalled { get; private set; }

        public IReadOnlyDictionary<string, string>? LastSettings { get; private set; }

        public Task<IEnumerable<Release>> SearchAsync(
            string query,
            IEnumerable<string>? categories,
            CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<ConnectionTestResult> TestConnectionAsync(
            IReadOnlyDictionary<string, string> settings,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            LastSettings = settings;
            return Task.FromResult(new ConnectionTestResult { Success = true, Message = "ok" });
        }
    }
}
