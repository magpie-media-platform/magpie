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
using Magpie.Models.Filters.Content;
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
public class ContentFiltersEndpointGroupTests
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private Mock<IContentFilterRepository> _contentFilterRepositoryMock = null!;

    private static readonly ContentItemCriteria DefaultCriteria = new()
    {
        TargetLevel = new ContentHierarchyLevelCriteria()
    };

    [SetUp]
    public void SetUp()
    {
        _contentFilterRepositoryMock = new Mock<IContentFilterRepository>();

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

        builder.Services.AddSingleton(_contentFilterRepositoryMock.Object);
        builder.Services.AddSingleton<IApiEndpointGroup, ContentFiltersEndpointGroup>();

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
    public async Task GetAllContentFilters_ReturnsEmptyArray_WhenNoContentFilters()
    {
        // Arrange
        _contentFilterRepositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<ContentFilter>>([]));

        // Act
        var response = await _client.GetAsync("/api/content-filters").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<ContentFilter[]>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Is.Empty);
        }
    }

    [Test]
    public async Task GetAllContentFilters_ReturnsContentFilters()
    {
        // Arrange
        var contentFilter = new ContentFilter(
            Id: Guid.NewGuid(),
            Name: "HD Movies",
            Criteria: DefaultCriteria,
            Tags: [],
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _contentFilterRepositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<ContentFilter>>([contentFilter]));

        // Act
        var response = await _client.GetAsync("/api/content-filters").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<ContentFilter[]>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(ContentFilter.Name)).EqualTo("HD Movies"));
        }
    }

    [Test]
    public async Task GetContentFilterById_ReturnsContentFilter()
    {
        // Arrange
        var contentFilterId = Guid.NewGuid();
        var contentFilter = new ContentFilter(
            Id: contentFilterId,
            Name: "TV Episodes",
            Criteria: DefaultCriteria,
            Tags: [],
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _contentFilterRepositoryMock
            .Setup(r => r.GetByIdAsync(contentFilterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<ContentFilter?>(contentFilter));

        // Act
        var response = await _client.GetAsync($"/api/content-filters/{contentFilterId}").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<ContentFilter>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(ContentFilter.Id)).EqualTo(contentFilterId));
            Assert.That(content, Has.Property(nameof(ContentFilter.Name)).EqualTo("TV Episodes"));
        }
    }

    [Test]
    public async Task GetContentFilterById_ReturnsNotFound_WhenContentFilterDoesNotExist()
    {
        // Arrange
        var contentFilterId = Guid.NewGuid();
        _contentFilterRepositoryMock
            .Setup(r => r.GetByIdAsync(contentFilterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<ContentFilter?>(null));

        // Act
        var response = await _client.GetAsync($"/api/content-filters/{contentFilterId}").ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetContentFilterByName_ReturnsContentFilter()
    {
        // Arrange
        var contentFilter = new ContentFilter(
            Id: Guid.NewGuid(),
            Name: "Music Albums",
            Criteria: DefaultCriteria,
            Tags: [],
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _contentFilterRepositoryMock
            .Setup(r => r.GetByNameAsync("Music Albums", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<ContentFilter?>(contentFilter));

        // Act
        var response = await _client.GetAsync("/api/content-filters/by-name/Music%20Albums").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<ContentFilter>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(ContentFilter.Name)).EqualTo("Music Albums"));
        }
    }

    [Test]
    public async Task GetContentFilterByName_ReturnsNotFound_WhenContentFilterDoesNotExist()
    {
        // Arrange
        _contentFilterRepositoryMock
            .Setup(r => r.GetByNameAsync("NonExistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<ContentFilter?>(null));

        // Act
        var response = await _client.GetAsync("/api/content-filters/by-name/NonExistent").ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetContentFiltersByTag_ReturnsFilteredContentFilters()
    {
        // Arrange
        var filter1 = new ContentFilter(
            Id: Guid.NewGuid(),
            Name: "Filter 1",
            Criteria: DefaultCriteria,
            Tags: ["automation"],
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        var filter2 = new ContentFilter(
            Id: Guid.NewGuid(),
            Name: "Filter 2",
            Criteria: DefaultCriteria,
            Tags: ["automation"],
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _contentFilterRepositoryMock
            .Setup(r => r.GetByTagAsync("automation", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<ContentFilter>>([filter1, filter2]));

        // Act
        var response = await _client.GetAsync("/api/content-filters/by-tag/automation").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<ContentFilter[]>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(2));
        }
    }

    [Test]
    public async Task CreateContentFilter_CreatesAndReturnsContentFilter()
    {
        // Arrange
        var request = new ContentFilter(
            Id: Guid.Empty,
            Name: "New Filter",
            Criteria: DefaultCriteria,
            Tags: [],
            CreatedAt: default,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _contentFilterRepositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<ContentFilter>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContentFilter cf, Guid? _, CancellationToken _) => Result.Success(cf));

        // Act
        var response = await _client.PostAsJsonAsync("/api/content-filters", request).ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<ContentFilter>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(content, Has.Property(nameof(ContentFilter.Name)).EqualTo("New Filter"));
            Assert.That(content, Has.Property(nameof(ContentFilter.Tags)).Empty);
            Assert.That(response.Headers.Location?.ToString(), Does.Contain($"/api/content-filters/{content!.Id}"));
        }
    }

    [Test]
    public async Task CreateContentFilter_WithTags_CreatesContentFilter()
    {
        // Arrange
        var request = new ContentFilter(
            Id: Guid.Empty,
            Name: "Tagged Filter",
            Criteria: DefaultCriteria,
            Tags: ["tag1", "tag2"],
            CreatedAt: default,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _contentFilterRepositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<ContentFilter>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContentFilter cf, Guid? _, CancellationToken _) => Result.Success(cf));

        // Act
        var response = await _client.PostAsJsonAsync("/api/content-filters", request).ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<ContentFilter>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(content, Has.Property(nameof(ContentFilter.Name)).EqualTo("Tagged Filter"));
            Assert.That(content, Has.Property(nameof(ContentFilter.Tags)).With.Count.EqualTo(2));
        }
    }

    [Test]
    public async Task PatchContentFilter_PatchesNameField()
    {
        // Arrange
        var contentFilterId = Guid.NewGuid();
        var request = new ContentFilterPatch
        {
            Name = new FieldPatch<string>("Patched")
        };

        _contentFilterRepositoryMock
            .Setup(r => r.PatchAsync(
                It.Is<ContentFilterPatch>(p => p.Id == contentFilterId && p.Name!.Value == "Patched"),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/content-filters/{contentFilterId}", request)
            .ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task PatchContentFilter_PatchesTags()
    {
        // Arrange
        var contentFilterId = Guid.NewGuid();
        var request = new ContentFilterPatch
        {
            Tags = new SetPatch<string, string>(["new-tag"], null)
        };

        _contentFilterRepositoryMock
            .Setup(r => r.PatchAsync(
                It.Is<ContentFilterPatch>(p =>
                    p.Id == contentFilterId &&
                    p.Tags != null &&
                    p.Tags.Add != null),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/content-filters/{contentFilterId}", request)
            .ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task PatchContentFilter_ReturnsNotFound_WhenContentFilterDoesNotExist()
    {
        // Arrange
        var contentFilterId = Guid.NewGuid();
        var request = new ContentFilterPatch
        {
            Name = new FieldPatch<string>("Patched")
        };

        _contentFilterRepositoryMock
            .Setup(r => r.PatchAsync(It.IsAny<ContentFilterPatch>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(false));

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/content-filters/{contentFilterId}", request)
            .ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DeleteContentFilter_DeletesContentFilter()
    {
        // Arrange
        var contentFilterId = Guid.NewGuid();

        _contentFilterRepositoryMock
            .Setup(r => r.DeleteAsync(contentFilterId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        // Act
        var response = await _client.DeleteAsync($"/api/content-filters/{contentFilterId}").ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task DeleteContentFilter_ReturnsNotFound_WhenContentFilterDoesNotExist()
    {
        // Arrange
        var contentFilterId = Guid.NewGuid();

        _contentFilterRepositoryMock
            .Setup(r => r.DeleteAsync(contentFilterId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(false));

        // Act
        var response = await _client.DeleteAsync($"/api/content-filters/{contentFilterId}").ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
