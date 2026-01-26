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
public class IndexersEndpointGroupTests
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private Mock<IIndexerRepository> _indexerRepositoryMock = null!;
    private Guid _definitionId;

    [SetUp]
    public void SetUp()
    {
        _indexerRepositoryMock = new Mock<IIndexerRepository>();
        _definitionId = Guid.NewGuid();

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

        builder.Services.AddSingleton(_indexerRepositoryMock.Object);
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
        _indexerRepositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<Indexer>>([]));

        // Act
        var response = await _client.GetAsync("/api/indexers").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Indexer[]>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Is.Empty);
        }
    }

    [Test]
    public async Task GetAllIndexers_ReturnsIndexers()
    {
        // Arrange
        var indexer = new Indexer(
            Id: Guid.NewGuid(),
            DefinitionId: _definitionId,
            Name: "NZBgeek",
            Enabled: true,
            Tags: [],
            Settings: new Dictionary<string, string?>(),
            Status: IndexerStatus.Healthy,
            LastChecked: null,
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _indexerRepositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<Indexer>>([indexer]));

        // Act
        var response = await _client.GetAsync("/api/indexers").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Indexer[]>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(Indexer.Name)).EqualTo("NZBgeek"));
            Assert.That(content, Has.One.With.Property(nameof(Indexer.DefinitionId)).EqualTo(_definitionId));
            Assert.That(content, Has.One.With.Property(nameof(Indexer.Enabled)).True);
        }
    }

    [Test]
    public async Task GetIndexerById_ReturnsIndexer()
    {
        // Arrange
        var indexerId = Guid.NewGuid();
        var indexer = new Indexer(
            Id: indexerId,
            DefinitionId: _definitionId,
            Name: "Drunken Slug",
            Enabled: true,
            Tags: [],
            Settings: new Dictionary<string, string?>(),
            Status: IndexerStatus.Unknown,
            LastChecked: null,
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _indexerRepositoryMock
            .Setup(r => r.GetByIdAsync(indexerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<Indexer?>(indexer));

        // Act
        var response = await _client.GetAsync($"/api/indexers/{indexerId}").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Indexer>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(Indexer.Id)).EqualTo(indexerId));
            Assert.That(content, Has.Property(nameof(Indexer.Name)).EqualTo("Drunken Slug"));
            Assert.That(content, Has.Property(nameof(Indexer.DefinitionId)).EqualTo(_definitionId));
        }
    }

    [Test]
    public async Task GetIndexerById_ReturnsNotFound_WhenIndexerDoesNotExist()
    {
        // Arrange
        var indexerId = Guid.NewGuid();
        _indexerRepositoryMock
            .Setup(r => r.GetByIdAsync(indexerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<Indexer?>(null));

        // Act
        var response = await _client.GetAsync($"/api/indexers/{indexerId}").ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetIndexerByName_ReturnsIndexer()
    {
        // Arrange
        var indexer = new Indexer(
            Id: Guid.NewGuid(),
            DefinitionId: _definitionId,
            Name: "IPTorrents",
            Enabled: true,
            Tags: [],
            Settings: new Dictionary<string, string?>(),
            Status: IndexerStatus.Healthy,
            LastChecked: null,
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _indexerRepositoryMock
            .Setup(r => r.GetByNameAsync("IPTorrents", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<Indexer?>(indexer));

        // Act
        var response = await _client.GetAsync("/api/indexers/by-name/IPTorrents").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Indexer>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(Indexer.Name)).EqualTo("IPTorrents"));
            Assert.That(content, Has.Property(nameof(Indexer.DefinitionId)).EqualTo(_definitionId));
        }
    }

    [Test]
    public async Task GetIndexerByName_ReturnsNotFound_WhenIndexerDoesNotExist()
    {
        // Arrange
        _indexerRepositoryMock
            .Setup(r => r.GetByNameAsync("NonExistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<Indexer?>(null));

        // Act
        var response = await _client.GetAsync("/api/indexers/by-name/NonExistent").ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetIndexersByDefinition_ReturnsFilteredIndexers()
    {
        // Arrange
        var indexer1 = new Indexer(
            Id: Guid.NewGuid(),
            DefinitionId: _definitionId,
            Name: "Newznab Indexer 1",
            Enabled: true,
            Tags: [],
            Settings: new Dictionary<string, string?>(),
            Status: IndexerStatus.Healthy,
            LastChecked: null,
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        var indexer2 = new Indexer(
            Id: Guid.NewGuid(),
            DefinitionId: _definitionId,
            Name: "Newznab Indexer 2",
            Enabled: true,
            Tags: [],
            Settings: new Dictionary<string, string?>(),
            Status: IndexerStatus.Healthy,
            LastChecked: null,
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _indexerRepositoryMock
            .Setup(r => r.GetByDefinitionIdAsync(_definitionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<Indexer>>([indexer1, indexer2]));

        // Act
        var response = await _client.GetAsync($"/api/indexers/by-definition/{_definitionId}").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Indexer[]>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(2));
            Assert.That(content, Has.All.With.Property(nameof(Indexer.DefinitionId)).EqualTo(_definitionId));
        }
    }

    [Test]
    public async Task GetIndexersByStatus_ReturnsFilteredIndexers()
    {
        // Arrange
        var indexer = new Indexer(
            Id: Guid.NewGuid(),
            DefinitionId: _definitionId,
            Name: "Healthy Indexer",
            Enabled: true,
            Tags: [],
            Settings: new Dictionary<string, string?>(),
            Status: IndexerStatus.Healthy,
            LastChecked: null,
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _indexerRepositoryMock
            .Setup(r => r.GetByStatusAsync(IndexerStatus.Healthy, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<Indexer>>([indexer]));

        // Act
        var response = await _client.GetAsync("/api/indexers/by-status/1").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Indexer[]>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(Indexer.Name)).EqualTo("Healthy Indexer"));
            Assert.That(content, Has.One.With.Property(nameof(Indexer.Status)).EqualTo(IndexerStatus.Healthy));
        }
    }

    [Test]
    public async Task GetEnabledIndexers_ReturnsOnlyEnabledIndexers()
    {
        // Arrange
        var indexer = new Indexer(
            Id: Guid.NewGuid(),
            DefinitionId: _definitionId,
            Name: "Enabled Indexer",
            Enabled: true,
            Tags: [],
            Settings: new Dictionary<string, string?>(),
            Status: IndexerStatus.Healthy,
            LastChecked: null,
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _indexerRepositoryMock
            .Setup(r => r.GetEnabledAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<Indexer>>([indexer]));

        // Act
        var response = await _client.GetAsync("/api/indexers/enabled").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Indexer[]>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(Indexer.Name)).EqualTo("Enabled Indexer"));
            Assert.That(content, Has.One.With.Property(nameof(Indexer.Enabled)).True);
        }
    }

    [Test]
    public async Task CreateIndexer_CreatesAndReturnsIndexer()
    {
        // Arrange
        var request = new Indexer(
            Id: Guid.Empty,
            DefinitionId: _definitionId,
            Name: "New Indexer",
            Enabled: true,
            Tags: [],
            Settings: new Dictionary<string, string?> { ["apiKey"] = "abc123", ["baseUrl"] = "https://example.com" },
            Status: default,
            LastChecked: null,
            CreatedAt: default,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _indexerRepositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<Indexer>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Indexer i, Guid? _, CancellationToken _) => Result.Success(i));

        // Act
        var response = await _client.PostAsJsonAsync("/api/indexers", request).ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Indexer>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(content, Has.Property(nameof(Indexer.Name)).EqualTo("New Indexer"));
            Assert.That(content, Has.Property(nameof(Indexer.DefinitionId)).EqualTo(_definitionId));
            Assert.That(content, Has.Property(nameof(Indexer.Enabled)).True);
            Assert.That(content, Has.Property(nameof(Indexer.Tags)).Empty);
            Assert.That(content!.Settings, Does.ContainKey("apiKey"));
            Assert.That(content.Settings["apiKey"], Is.EqualTo("abc123"));
            Assert.That(response.Headers.Location?.ToString(), Does.Contain($"/api/indexers/{content.Id}"));
        }
    }

    [Test]
    public async Task UpdateIndexer_UpdatesIndexer()
    {
        // Arrange
        var indexerId = Guid.NewGuid();
        var existingIndexer = new Indexer(
            Id: indexerId,
            DefinitionId: _definitionId,
            Name: "Original Indexer",
            Enabled: true,
            Tags: [],
            Settings: new Dictionary<string, string?>(),
            Status: IndexerStatus.Healthy,
            LastChecked: null,
            CreatedAt: DateTimeOffset.UtcNow.AddDays(-1),
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        var request = new Indexer(
            Id: Guid.Empty,
            DefinitionId: _definitionId,
            Name: "Updated Indexer",
            Enabled: false,
            Tags: [],
            Settings: new Dictionary<string, string?> { ["apiKey"] = "newkey" },
            Status: default,
            LastChecked: null,
            CreatedAt: default,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _indexerRepositoryMock
            .Setup(r => r.GetByIdAsync(indexerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<Indexer?>(existingIndexer));

        _indexerRepositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<Indexer>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        // Act
        var response = await _client.PutAsJsonAsync($"/api/indexers/{indexerId}", request).ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        _indexerRepositoryMock.Verify(
            r => r.UpdateAsync(
                It.Is<Indexer>(i =>
                    i.Id == indexerId &&
                    i.Name == "Updated Indexer" &&
                    i.Enabled == false),
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task UpdateIndexer_ReturnsNotFound_WhenIndexerDoesNotExist()
    {
        // Arrange
        var indexerId = Guid.NewGuid();
        var request = new Indexer(
            Id: Guid.Empty,
            DefinitionId: _definitionId,
            Name: "Updated Indexer",
            Enabled: true,
            Tags: [],
            Settings: new Dictionary<string, string?>(),
            Status: default,
            LastChecked: null,
            CreatedAt: default,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _indexerRepositoryMock
            .Setup(r => r.GetByIdAsync(indexerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<Indexer?>(null));

        // Act
        var response = await _client.PutAsJsonAsync($"/api/indexers/{indexerId}", request).ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task PatchIndexer_PatchesNameField()
    {
        // Arrange
        var indexerId = Guid.NewGuid();
        var request = new IndexerPatch { Name = new FieldPatch<string>("Patched") };

        _indexerRepositoryMock
            .Setup(r => r.PatchAsync(
                It.Is<IndexerPatch>(p => p.Id == indexerId && p.Name!.Value == "Patched"),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/indexers/{indexerId}", request).ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task PatchIndexer_PatchesEnabledField()
    {
        // Arrange
        var indexerId = Guid.NewGuid();
        var request = new IndexerPatch { Enabled = new FieldPatch<bool>(false) };

        _indexerRepositoryMock
            .Setup(r => r.PatchAsync(
                It.Is<IndexerPatch>(p => p.Id == indexerId && p.Enabled!.Value == false),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/indexers/{indexerId}", request).ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task PatchIndexer_ReturnsNotFound_WhenIndexerDoesNotExist()
    {
        // Arrange
        var indexerId = Guid.NewGuid();
        var request = new IndexerPatch { Name = new FieldPatch<string>("Patched") };

        _indexerRepositoryMock
            .Setup(r => r.PatchAsync(It.IsAny<IndexerPatch>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(false));

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/indexers/{indexerId}", request).ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DeleteIndexer_DeletesIndexer()
    {
        // Arrange
        var indexerId = Guid.NewGuid();

        _indexerRepositoryMock
            .Setup(r => r.DeleteAsync(indexerId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        // Act
        var response = await _client.DeleteAsync($"/api/indexers/{indexerId}").ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task DeleteIndexer_ReturnsNotFound_WhenIndexerDoesNotExist()
    {
        // Arrange
        var indexerId = Guid.NewGuid();

        _indexerRepositoryMock
            .Setup(r => r.DeleteAsync(indexerId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(false));

        // Act
        var response = await _client.DeleteAsync($"/api/indexers/{indexerId}").ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
