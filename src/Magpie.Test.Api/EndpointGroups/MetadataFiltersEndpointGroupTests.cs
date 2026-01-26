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
using Magpie.Models.Filters;
using Magpie.Models.Filters.Metadata;
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
public class MetadataFiltersEndpointGroupTests
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private Mock<IMetadataFilterRepository> _metadataFilterRepositoryMock = null!;

    private static readonly MetadataContributionCriteria DefaultCriteria = new();

    [SetUp]
    public void SetUp()
    {
        _metadataFilterRepositoryMock = new Mock<IMetadataFilterRepository>();

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

        builder.Services.AddSingleton(_metadataFilterRepositoryMock.Object);
        builder.Services.AddSingleton<IApiEndpointGroup, MetadataFiltersEndpointGroup>();

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
    public async Task GetAllMetadataFilters_ReturnsEmptyArray_WhenNoMetadataFilters()
    {
        // Arrange
        _metadataFilterRepositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<MetadataFilter>>([]));

        // Act
        var response = await _client.GetAsync("/api/metadata-filters").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<MetadataFilter[]>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Is.Empty);
        }
    }

    [Test]
    public async Task GetAllMetadataFilters_ReturnsMetadataFilters()
    {
        // Arrange
        var metadataFilter = new MetadataFilter(
            Id: Guid.NewGuid(),
            Name: "TMDB Provider",
            Criteria: DefaultCriteria,
            Tags: [],
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _metadataFilterRepositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<MetadataFilter>>([metadataFilter]));

        // Act
        var response = await _client.GetAsync("/api/metadata-filters").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<MetadataFilter[]>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(MetadataFilter.Name)).EqualTo("TMDB Provider"));
        }
    }

    [Test]
    public async Task GetMetadataFilterById_ReturnsMetadataFilter()
    {
        // Arrange
        var metadataFilterId = Guid.NewGuid();
        var metadataFilter = new MetadataFilter(
            Id: metadataFilterId,
            Name: "TVDB Provider",
            Criteria: DefaultCriteria,
            Tags: [],
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _metadataFilterRepositoryMock
            .Setup(r => r.GetByIdAsync(metadataFilterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<MetadataFilter?>(metadataFilter));

        // Act
        var response = await _client.GetAsync($"/api/metadata-filters/{metadataFilterId}").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<MetadataFilter>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(MetadataFilter.Id)).EqualTo(metadataFilterId));
            Assert.That(content, Has.Property(nameof(MetadataFilter.Name)).EqualTo("TVDB Provider"));
        }
    }

    [Test]
    public async Task GetMetadataFilterById_ReturnsNotFound_WhenMetadataFilterDoesNotExist()
    {
        // Arrange
        var metadataFilterId = Guid.NewGuid();
        _metadataFilterRepositoryMock
            .Setup(r => r.GetByIdAsync(metadataFilterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<MetadataFilter?>(null));

        // Act
        var response = await _client.GetAsync($"/api/metadata-filters/{metadataFilterId}").ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetMetadataFilterByName_ReturnsMetadataFilter()
    {
        // Arrange
        var metadataFilter = new MetadataFilter(
            Id: Guid.NewGuid(),
            Name: "Music Provider",
            Criteria: DefaultCriteria,
            Tags: [],
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _metadataFilterRepositoryMock
            .Setup(r => r.GetByNameAsync("Music Provider", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<MetadataFilter?>(metadataFilter));

        // Act
        var response = await _client.GetAsync("/api/metadata-filters/by-name/Music%20Provider").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<MetadataFilter>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(MetadataFilter.Name)).EqualTo("Music Provider"));
        }
    }

    [Test]
    public async Task GetMetadataFilterByName_ReturnsNotFound_WhenMetadataFilterDoesNotExist()
    {
        // Arrange
        _metadataFilterRepositoryMock
            .Setup(r => r.GetByNameAsync("NonExistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<MetadataFilter?>(null));

        // Act
        var response = await _client.GetAsync("/api/metadata-filters/by-name/NonExistent").ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetMetadataFiltersByTag_ReturnsFilteredMetadataFilters()
    {
        // Arrange
        var filter1 = new MetadataFilter(
            Id: Guid.NewGuid(),
            Name: "Filter 1",
            Criteria: DefaultCriteria,
            Tags: ["automation"],
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        var filter2 = new MetadataFilter(
            Id: Guid.NewGuid(),
            Name: "Filter 2",
            Criteria: DefaultCriteria,
            Tags: ["automation"],
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _metadataFilterRepositoryMock
            .Setup(r => r.GetByTagAsync("automation", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<MetadataFilter>>([filter1, filter2]));

        // Act
        var response = await _client.GetAsync("/api/metadata-filters/by-tag/automation").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<MetadataFilter[]>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(2));
        }
    }

    [Test]
    public async Task CreateMetadataFilter_CreatesAndReturnsMetadataFilter()
    {
        // Arrange
        var request = new MetadataFilter(
            Id: Guid.Empty,
            Name: "New Filter",
            Criteria: DefaultCriteria,
            Tags: [],
            CreatedAt: default,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _metadataFilterRepositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<MetadataFilter>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MetadataFilter mf, Guid? _, CancellationToken _) => Result.Success(mf));

        // Act
        var response = await _client.PostAsJsonAsync("/api/metadata-filters", request).ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<MetadataFilter>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(content, Has.Property(nameof(MetadataFilter.Name)).EqualTo("New Filter"));
            Assert.That(content, Has.Property(nameof(MetadataFilter.Tags)).Empty);
            Assert.That(response.Headers.Location?.ToString(), Does.Contain($"/api/metadata-filters/{content!.Id}"));
        }
    }

    [Test]
    public async Task CreateMetadataFilter_WithTags_CreatesMetadataFilter()
    {
        // Arrange
        var request = new MetadataFilter(
            Id: Guid.Empty,
            Name: "Tagged Filter",
            Criteria: DefaultCriteria,
            Tags: ["tag1", "tag2"],
            CreatedAt: default,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _metadataFilterRepositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<MetadataFilter>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MetadataFilter mf, Guid? _, CancellationToken _) => Result.Success(mf));

        // Act
        var response = await _client.PostAsJsonAsync("/api/metadata-filters", request).ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<MetadataFilter>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(content, Has.Property(nameof(MetadataFilter.Name)).EqualTo("Tagged Filter"));
            Assert.That(content, Has.Property(nameof(MetadataFilter.Tags)).With.Count.EqualTo(2));
        }
    }

    [Test]
    public async Task PatchMetadataFilter_PatchesNameField()
    {
        // Arrange
        var metadataFilterId = Guid.NewGuid();
        var request = new MetadataFilterPatch
        {
            Name = new FieldPatch<string>("Patched")
        };

        _metadataFilterRepositoryMock
            .Setup(r => r.PatchAsync(
                It.Is<MetadataFilterPatch>(p => p.Id == metadataFilterId && p.Name!.Value == "Patched"),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/metadata-filters/{metadataFilterId}", request)
            .ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task PatchMetadataFilter_PatchesTags()
    {
        // Arrange
        var metadataFilterId = Guid.NewGuid();
        var request = new MetadataFilterPatch
        {
            Tags = new SetPatch<string, string>(["new-tag"], null)
        };

        _metadataFilterRepositoryMock
            .Setup(r => r.PatchAsync(
                It.Is<MetadataFilterPatch>(p =>
                    p.Id == metadataFilterId &&
                    p.Tags != null &&
                    p.Tags.Add != null),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/metadata-filters/{metadataFilterId}", request)
            .ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task PatchMetadataFilter_ReturnsNotFound_WhenMetadataFilterDoesNotExist()
    {
        // Arrange
        var metadataFilterId = Guid.NewGuid();
        var request = new MetadataFilterPatch
        {
            Name = new FieldPatch<string>("Patched")
        };

        _metadataFilterRepositoryMock
            .Setup(r => r.PatchAsync(It.IsAny<MetadataFilterPatch>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(false));

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/metadata-filters/{metadataFilterId}", request)
            .ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DeleteMetadataFilter_DeletesMetadataFilter()
    {
        // Arrange
        var metadataFilterId = Guid.NewGuid();

        _metadataFilterRepositoryMock
            .Setup(r => r.DeleteAsync(metadataFilterId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        // Act
        var response = await _client.DeleteAsync($"/api/metadata-filters/{metadataFilterId}").ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task DeleteMetadataFilter_ReturnsNotFound_WhenMetadataFilterDoesNotExist()
    {
        // Arrange
        var metadataFilterId = Guid.NewGuid();

        _metadataFilterRepositoryMock
            .Setup(r => r.DeleteAsync(metadataFilterId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(false));

        // Act
        var response = await _client.DeleteAsync($"/api/metadata-filters/{metadataFilterId}").ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
