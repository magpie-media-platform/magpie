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
public class ReleasesEndpointGroupTests
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private Mock<IReleaseRepository> _releaseRepositoryMock = null!;
    private Guid _indexerId;

    [SetUp]
    public void SetUp()
    {
        _releaseRepositoryMock = new Mock<IReleaseRepository>();
        _indexerId = Guid.NewGuid();

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

        builder.Services.AddSingleton(_releaseRepositoryMock.Object);
        builder.Services.AddSingleton<IApiEndpointGroup, ReleasesEndpointGroup>();

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
    public async Task GetAllReleases_ReturnsEmptyArray_WhenNoReleases()
    {
        _releaseRepositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<Release>>([]));

        var response = await _client.GetAsync("/api/releases").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Release[]>().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Is.Empty);
        }
    }

    [Test]
    public async Task GetAllReleases_ReturnsReleases()
    {
        var release = CreateRelease("Test Release");

        _releaseRepositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<Release>>([release]));

        var response = await _client.GetAsync("/api/releases").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Release[]>().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(Release.Title)).EqualTo("Test Release"));
        }
    }

    [Test]
    public async Task GetReleaseById_ReturnsRelease()
    {
        var releaseId = Guid.NewGuid();
        var release = CreateRelease("Found Release", releaseId);

        _releaseRepositoryMock
            .Setup(r => r.GetByIdAsync(releaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<Release?>(release));

        var response = await _client.GetAsync($"/api/releases/{releaseId}").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Release>().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(Release.Id)).EqualTo(releaseId));
            Assert.That(content, Has.Property(nameof(Release.Title)).EqualTo("Found Release"));
        }
    }

    [Test]
    public async Task GetReleaseById_ReturnsNotFound_WhenReleaseDoesNotExist()
    {
        var releaseId = Guid.NewGuid();
        _releaseRepositoryMock
            .Setup(r => r.GetByIdAsync(releaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<Release?>(null));

        var response = await _client.GetAsync($"/api/releases/{releaseId}").ConfigureAwait(false);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetReleasesByIndexer_ReturnsFilteredReleases()
    {
        var release1 = CreateRelease("Indexer Release 1");
        var release2 = CreateRelease("Indexer Release 2");

        _releaseRepositoryMock
            .Setup(r => r.GetByIndexerIdAsync(_indexerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<Release>>([release1, release2]));

        var response = await _client.GetAsync($"/api/releases/by-indexer/{_indexerId}").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Release[]>().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(2));
            Assert.That(content, Has.All.With.Property(nameof(Release.IndexerId)).EqualTo(_indexerId));
        }
    }

    [Test]
    public async Task GetReleaseByExternalId_ReturnsRelease()
    {
        var release = CreateRelease("External Release");

        _releaseRepositoryMock
            .Setup(r => r.GetByExternalIdAsync(_indexerId, "ext-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<Release?>(release));

        var response = await _client
            .GetAsync($"/api/releases/by-external-id/{_indexerId}/ext-123")
            .ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Release>().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(Release.Title)).EqualTo("External Release"));
            Assert.That(content, Has.Property(nameof(Release.ExternalId)).EqualTo("ext-123"));
        }
    }

    [Test]
    public async Task GetReleaseByExternalId_ReturnsNotFound_WhenReleaseDoesNotExist()
    {
        _releaseRepositoryMock
            .Setup(r => r.GetByExternalIdAsync(_indexerId, "nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<Release?>(null));

        var response = await _client.GetAsync(
            $"/api/releases/by-external-id/{_indexerId}/nonexistent").ConfigureAwait(false);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetReleasesByProvider_ReturnsFilteredReleases()
    {
        var release = CreateRelease("Torrent Release", provider: "torrent");

        _releaseRepositoryMock
            .Setup(r => r.GetByProviderAsync("torrent", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<Release>>([release]));

        var response = await _client.GetAsync("/api/releases/by-provider/torrent").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Release[]>().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(Release.Provider)).EqualTo("torrent"));
        }
    }

    [Test]
    public async Task GetReleasesByInfoHash_ReturnsFilteredReleases()
    {
        var release = CreateRelease("Hash Release", infoHash: "abc123");

        _releaseRepositoryMock
            .Setup(r => r.GetByInfoHashAsync("abc123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<Release>>([release]));

        var response = await _client.GetAsync("/api/releases/by-info-hash/abc123").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Release[]>().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(Release.InfoHash)).EqualTo("abc123"));
        }
    }

    [Test]
    public async Task GetReleasesByTag_ReturnsFilteredReleases()
    {
        var release = CreateRelease("Tagged Release", tags: ["hdr", "4k"]);

        _releaseRepositoryMock
            .Setup(r => r.GetByTagAsync("hdr", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<Release>>([release]));

        var response = await _client.GetAsync("/api/releases/by-tag/hdr").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Release[]>().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(Release.Title)).EqualTo("Tagged Release"));
        }
    }

    [Test]
    public async Task CreateRelease_CreatesAndReturnsRelease()
    {
        var request = CreateRelease("New Release");

        _releaseRepositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<Release>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Release r, Guid? _, CancellationToken _) => Result.Success(r));

        var response = await _client.PostAsJsonAsync("/api/releases", request).ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Release>().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(content, Has.Property(nameof(Release.Title)).EqualTo("New Release"));
            Assert.That(content, Has.Property(nameof(Release.Provider)).EqualTo("torrent"));
            Assert.That(response.Headers.Location?.ToString(), Does.Contain($"/api/releases/{content!.Id}"));
        }
    }

    [Test]
    public async Task PatchRelease_UpdatesRelease()
    {
        var releaseId = Guid.NewGuid();
        var existingRelease = CreateRelease("Original Title", releaseId);
        var request = CreateRelease("Updated Title", releaseId);

        _releaseRepositoryMock
            .Setup(r => r.GetByIdAsync(releaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<Release?>(existingRelease));

        _releaseRepositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<Release>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        var response = await _client.PatchAsJsonAsync($"/api/releases/{releaseId}", request).ConfigureAwait(false);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        _releaseRepositoryMock.Verify(
            r => r.UpdateAsync(
                It.Is<Release>(rel => rel.Id == releaseId && rel.Title == "Updated Title"),
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task PatchRelease_ReturnsNotFound_WhenReleaseDoesNotExist()
    {
        var releaseId = Guid.NewGuid();
        var request = CreateRelease("Updated Title", releaseId);

        _releaseRepositoryMock
            .Setup(r => r.GetByIdAsync(releaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<Release?>(null));

        var response = await _client.PatchAsJsonAsync($"/api/releases/{releaseId}", request).ConfigureAwait(false);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DeleteRelease_DeletesRelease()
    {
        var releaseId = Guid.NewGuid();

        _releaseRepositoryMock
            .Setup(r => r.DeleteAsync(releaseId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        var response = await _client.DeleteAsync($"/api/releases/{releaseId}").ConfigureAwait(false);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task DeleteRelease_ReturnsNotFound_WhenReleaseDoesNotExist()
    {
        var releaseId = Guid.NewGuid();

        _releaseRepositoryMock
            .Setup(r => r.DeleteAsync(releaseId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(false));

        var response = await _client.DeleteAsync($"/api/releases/{releaseId}").ConfigureAwait(false);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DeleteReleasesByIndexer_ReturnsDeletedCount()
    {
        _releaseRepositoryMock
            .Setup(r => r.DeleteByIndexerIdAsync(_indexerId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(5));

        var response = await _client.DeleteAsync($"/api/releases/by-indexer/{_indexerId}").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<int>().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.EqualTo(5));
        }
    }

    private Release CreateRelease(
        string title,
        Guid? id = null,
        string provider = "torrent",
        string? infoHash = null,
        string[]? tags = null)
    {
        return new(
            Id: id ?? Guid.NewGuid(),
            IndexerId: _indexerId,
            ExternalId: "ext-123",
            Provider: provider,
            Title: title,
            PublishDate: DateTimeOffset.UtcNow,
            Size: 1024,
            InfoHash: infoHash,
            Categories: ["Movies"],
            DownloadUrl: new Uri("https://example.com/download"),
            InfoUrl: new Uri("https://example.com/info"),
            Properties: new Dictionary<string, string?> { ["seeders"] = "10" },
            Languages: ["en"],
            Tags: tags ?? [],
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);
    }
}
