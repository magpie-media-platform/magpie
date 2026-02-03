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
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Paging;
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
    private Mock<IReleaseRepository> _repository = null!;

    [SetUp]
    public void SetUp()
    {
        _repository = new Mock<IReleaseRepository>();

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

        builder.Services.AddSingleton(_repository.Object);
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
        // Arrange
        _repository
            .Setup(m => m.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var response = await _client.GetAsync("/api/releases").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<Release[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Is.Empty);
        }
    }

    [Test]
    public async Task GetAllReleases_ReturnsReleases()
    {
        // Arrange
        var release = new Release
        {
            Title = "Test.Movie.2024.1080p.BluRay",
            Protocol = "torrent",
            PublishDate = DateTime.UtcNow,
        };

        _repository
            .Setup(m => m.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([release]);

        // Act
        var response = await _client.GetAsync("/api/releases").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<Release[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One
                .With.Property(nameof(Release.Title)).EqualTo("Test.Movie.2024.1080p.BluRay"));
            Assert.That(content, Has.One.With.Property(nameof(Release.Protocol)).EqualTo("torrent"));
        }
    }

    [Test]
    public async Task GetReleaseById_ReturnsRelease()
    {
        // Arrange
        var releaseId = Guid.NewGuid();
        var release = new Release
        {
            Id = releaseId,
            Title = "Test.Show.S01E01.720p.WEB-DL",
            Protocol = "usenet",
            PublishDate = DateTime.UtcNow,
        };

        _repository
            .Setup(m => m.GetByIdAsync(releaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(release);

        // Act
        var response = await _client.GetAsync($"/api/releases/{releaseId}").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<Release>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(content.Id)).EqualTo(releaseId));
            Assert.That(content, Has.Property(nameof(content.Title)).EqualTo("Test.Show.S01E01.720p.WEB-DL"));
            Assert.That(content, Has.Property(nameof(content.Protocol)).EqualTo("usenet"));
        }
    }

    [Test]
    public async Task GetReleaseById_ReturnsNotFound_WhenReleaseDoesNotExist()
    {
        // Arrange
        var releaseId = Guid.NewGuid();
        _repository
            .Setup(m => m.GetByIdAsync(releaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(Release?));

        // Act
        var response = await _client.GetAsync($"/api/releases/{releaseId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task QueryReleases_ReturnsPagedResults()
    {
        // Arrange
        var release1 = new Release
        {
            Title = "Movie.One.2024.1080p",
            Protocol = "torrent",
            PublishDate = DateTime.UtcNow,
        };
        var release2 = new Release
        {
            Title = "Movie.Two.2024.720p",
            Protocol = "usenet",
            PublishDate = DateTime.UtcNow,
        };

        _repository
            .Setup(m => m.QueryAsync(
                It.IsAny<ReleaseQuery>(),
                It.IsAny<PagingParameter?>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Release>
            {
                Items = [release1, release2],
                PageNumber = 1,
                PageSize = 10,
                TotalItems = 2
            });

        // Act
        var response = await _client.PostAsJsonAsync("/api/releases/query", new ReleaseQuery())
            .ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<PagedList<Release>>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Has.Property(nameof(content.TotalItems)).EqualTo(2));
            Assert.That(content, Has.Property(nameof(content.Items)).With.Count.EqualTo(2));
        }
    }

    [Test]
    public async Task CreateRelease_CreatesAndReturnsRelease()
    {
        // Arrange
        var request = new Release
        {
            Title = "New.Release.2024.HDRip",
            Protocol = "torrent",
            PublishDate = DateTime.UtcNow,
            Categories = ["Movies", "HD"],
            Languages = ["English"]
        };

        _repository
            .Setup(m => m.CreateAsync(It.IsAny<Release>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Release r, Guid? _, CancellationToken _) => r);

        // Act
        var response = await _client.PostAsJsonAsync("/api/releases", request).ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<Release>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.Created));
            Assert.That(content, Has.Property(nameof(content.Title)).EqualTo("New.Release.2024.HDRip"));
            Assert.That(content, Has.Property(nameof(content.Protocol)).EqualTo("torrent"));
        }
    }

    [Test]
    public async Task UpdateRelease_UpdatesRelease()
    {
        // Arrange
        var releaseId = Guid.NewGuid();
        var request = new Release
        {
            Title = "Updated.Release.Title",
            Protocol = "usenet",
            PublishDate = DateTime.UtcNow
        };

        _repository
            .Setup(m => m.UpdateAsync(It.IsAny<Release>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.PutAsJsonAsync($"/api/releases/{releaseId}", request).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
        _repository.Verify(
            r => r.UpdateAsync(
                It.Is<Release>(rel =>
                    rel.Id == releaseId &&
                    rel.Title == "Updated.Release.Title" &&
                    rel.Protocol == "usenet"),
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task UpdateRelease_ReturnsNotFound_WhenReleaseDoesNotExist()
    {
        // Arrange
        var releaseId = Guid.NewGuid();
        var request = new Release
        {
            Title = "Updated.Title",
            Protocol = "torrent",
            PublishDate = DateTime.UtcNow
        };

        _repository
            .Setup(m => m.UpdateAsync(It.IsAny<Release>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var response = await _client.PutAsJsonAsync($"/api/releases/{releaseId}", request).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DeleteRelease_DeletesRelease()
    {
        // Arrange
        var releaseId = Guid.NewGuid();

        _repository
            .Setup(m => m.DeleteAsync(releaseId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.DeleteAsync($"/api/releases/{releaseId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task DeleteRelease_ReturnsNotFound_WhenReleaseDoesNotExist()
    {
        // Arrange
        var releaseId = Guid.NewGuid();

        _repository
            .Setup(m => m.DeleteAsync(releaseId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var response = await _client.DeleteAsync($"/api/releases/{releaseId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task QueryReleases_WithFilterParameters()
    {
        // Arrange
        var indexerId = Guid.NewGuid();
        var query = new ReleaseQuery { IndexerId = indexerId };

        _repository
            .Setup(m => m.QueryAsync(
                It.Is<ReleaseQuery>(q => q.IndexerId == indexerId),
                It.IsAny<PagingParameter?>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Release>
            {
                Items = [],
                PageNumber = 1,
                PageSize = 10,
                TotalItems = 0
            });

        // Act
        var response = await _client.PostAsJsonAsync("/api/releases/query", query).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
        _repository.Verify(
            r => r.QueryAsync(
                It.Is<ReleaseQuery>(q => q.IndexerId == indexerId),
                It.IsAny<PagingParameter?>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
