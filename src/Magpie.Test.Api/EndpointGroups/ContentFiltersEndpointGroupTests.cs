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
public class ContentFiltersEndpointGroupTests
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private Mock<IContentFilterRepository> _contentFilterRepository = null!;
    private Mock<IContentHierarchyItemRepository> _contentHierarchyItemRepository = null!;
    private Guid _userId;

    private static readonly ContentFilterCriteria DefaultCriteria = new();

    [SetUp]
    public void SetUp()
    {
        _userId = Guid.NewGuid();
        _contentFilterRepository = new Mock<IContentFilterRepository>();
        _contentHierarchyItemRepository = new Mock<IContentHierarchyItemRepository>();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders().AddTestLogger();
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, _userId.ToString()) };
        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var authServiceMock = new Mock<IAuthenticationService>();
        authServiceMock
            .Setup(m => m.AuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<string>()))
            .ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(principal, "Test")));
        builder.Services.AddSingleton(authServiceMock.Object);
        builder.Services.AddAuthorizationBuilder()
            .SetFallbackPolicy(null)
            .SetDefaultPolicy(new AuthorizationPolicyBuilder()
                .RequireAssertion(_ => true)
                .Build())
            .AddPolicy(nameof(Role.Viewer), policy => policy.RequireAssertion(_ => true))
            .AddPolicy(nameof(Role.Requester), policy => policy.RequireAssertion(_ => true))
            .AddPolicy(nameof(Role.Manager), policy => policy.RequireAssertion(_ => true))
            .AddPolicy(nameof(Role.Admin), policy => policy.RequireAssertion(_ => true));

        builder.Services.AddSingleton(_contentFilterRepository.Object);
        builder.Services.AddSingleton(_contentHierarchyItemRepository.Object);
        builder.Services.AddSingleton<IApiEndpointGroup, ContentFiltersEndpointGroup>();

        var app = builder.Build();

        app.Use(async (context, next) =>
        {
            context.User = principal;
            await next(context).ConfigureAwait(false);
        });

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
    public async Task GetMyContentFilters_ReturnsEmptyArray_WhenNoContentFilters()
    {
        // Arrange
        _contentFilterRepository
            .Setup(m => m.ListByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var response = await _client.GetAsync("/api/content-filters/my").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<ContentFilter[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Is.Empty);
        }
    }

    [Test]
    public async Task GetMyContentFilters_ReturnsContentFilters()
    {
        // Arrange
        var contentFilter = new ContentFilter
        {
            Id = Guid.NewGuid(),
            Name = "My Filter",
            OwnerUserId = _userId,
            Criteria = DefaultCriteria,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _userId,
            ModifiedAt = null,
            ModifiedByUserId = null
        };

        _contentFilterRepository
            .Setup(m => m.ListByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([contentFilter]);

        // Act
        var response = await _client.GetAsync("/api/content-filters/my").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<ContentFilter[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(ContentFilter.Name)).EqualTo("My Filter"));
        }
    }

    [Test]
    public async Task GetMyContentFilter_ReturnsContentFilter()
    {
        // Arrange
        var contentFilterId = Guid.NewGuid();
        var contentFilter = new ContentFilter
        {
            Id = contentFilterId,
            Name = "My Filter",
            OwnerUserId = _userId,
            Criteria = DefaultCriteria,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _userId,
            ModifiedAt = null,
            ModifiedByUserId = null
        };

        _contentFilterRepository
            .Setup(m => m.GetByIdAsync(contentFilterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(contentFilter);

        // Act
        var response = await _client.GetAsync($"/api/content-filters/my/{contentFilterId}").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<ContentFilter>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(content.Id)).EqualTo(contentFilterId));
            Assert.That(content, Has.Property(nameof(content.Name)).EqualTo("My Filter"));
        }
    }

    [Test]
    public async Task GetMyContentFilter_ReturnsNotFound_WhenContentFilterDoesNotExist()
    {
        // Arrange
        var contentFilterId = Guid.NewGuid();
        _contentFilterRepository
            .Setup(m => m.GetByIdAsync(contentFilterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(ContentFilter?));

        // Act
        var response = await _client.GetAsync($"/api/content-filters/my/{contentFilterId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetMyContentFilter_ReturnsNotFound_WhenContentFilterBelongsToAnotherUser()
    {
        // Arrange
        var contentFilterId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var contentFilter = new ContentFilter
        {
            Id = contentFilterId,
            Name = "Other User Filter",
            OwnerUserId = otherUserId,
            Criteria = DefaultCriteria,
            CreatedAt = DateTime.UtcNow
        };

        _contentFilterRepository
            .Setup(m => m.GetByIdAsync(contentFilterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(contentFilter);

        // Act
        var response = await _client.GetAsync($"/api/content-filters/my/{contentFilterId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task CreateMyContentFilter_CreatesAndReturnsContentFilter()
    {
        // Arrange
        var request = new ContentFilter
        {
            Id = Guid.Empty,
            Name = "New Filter",
            Criteria = DefaultCriteria,
            CreatedAt = default,
            CreatedByUserId = null,
            ModifiedAt = null,
            ModifiedByUserId = null
        };

        _contentFilterRepository
            .Setup(m => m.CreateAsync(It.IsAny<ContentFilter>(), _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContentFilter cf, Guid? _, CancellationToken _) => cf);

        // Act
        var response = await _client.PostAsJsonAsync("/api/content-filters/my", request).ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<ContentFilter>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.Created));
            Assert.That(content, Has.Property(nameof(content.Name)).EqualTo("New Filter"));
            Assert.That(content, Has.Property(nameof(content.OwnerUserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task PatchMyContentFilter_PatchesNameField()
    {
        // Arrange
        var contentFilterId = Guid.NewGuid();
        var existingFilter = new ContentFilter
        {
            Id = contentFilterId,
            Name = "Old Name",
            OwnerUserId = _userId,
            Criteria = DefaultCriteria,
            CreatedAt = DateTime.UtcNow
        };
        var request = new ContentFilterPatch { Id = contentFilterId, Name = new() { Value = "Patched" } };

        _contentFilterRepository
            .Setup(m => m.GetByIdAsync(contentFilterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingFilter);

        _contentFilterRepository
            .Setup(m => m.PatchAsync(
                It.Is<ContentFilterPatch>(p => p.Id == contentFilterId && p.Name!.Value == "Patched"),
                _userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/content-filters/my/{contentFilterId}", request)
            .ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task PatchMyContentFilter_PatchesTags()
    {
        // Arrange
        var contentFilterId = Guid.NewGuid();
        var existingFilter = new ContentFilter
        {
            Id = contentFilterId,
            Name = "Test",
            OwnerUserId = _userId,
            Criteria = DefaultCriteria,
            CreatedAt = DateTime.UtcNow
        };
        var request = new ContentFilterPatch { Id = contentFilterId, Tags = new() { Add = ["new-tag"] } };

        _contentFilterRepository
            .Setup(m => m.GetByIdAsync(contentFilterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingFilter);

        _contentFilterRepository
            .Setup(m => m.PatchAsync(
                It.Is<ContentFilterPatch>(p =>
                    p.Id == contentFilterId &&
                    p.Tags != null &&
                    p.Tags.Add != null),
                _userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/content-filters/my/{contentFilterId}", request)
            .ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task PatchMyContentFilter_ReturnsNotFound_WhenContentFilterDoesNotExist()
    {
        // Arrange
        var contentFilterId = Guid.NewGuid();
        var request = new ContentFilterPatch { Id = contentFilterId, Name = new() { Value = "Patched" } };

        _contentFilterRepository
            .Setup(m => m.GetByIdAsync(contentFilterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(ContentFilter?));

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/content-filters/my/{contentFilterId}", request)
            .ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task PatchMyContentFilter_ReturnsNotFound_WhenContentFilterBelongsToAnotherUser()
    {
        // Arrange
        var contentFilterId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var existingFilter = new ContentFilter
        {
            Id = contentFilterId,
            Name = "Other User Filter",
            OwnerUserId = otherUserId,
            Criteria = DefaultCriteria,
            CreatedAt = DateTime.UtcNow
        };
        var request = new ContentFilterPatch { Id = contentFilterId, Name = new() { Value = "Patched" } };

        _contentFilterRepository
            .Setup(m => m.GetByIdAsync(contentFilterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingFilter);

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/content-filters/my/{contentFilterId}", request)
            .ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DeleteMyContentFilter_DeletesContentFilter()
    {
        // Arrange
        var contentFilterId = Guid.NewGuid();
        var existingFilter = new ContentFilter
        {
            Id = contentFilterId,
            Name = "Test",
            OwnerUserId = _userId,
            Criteria = DefaultCriteria,
            CreatedAt = DateTime.UtcNow
        };

        _contentFilterRepository
            .Setup(m => m.GetByIdAsync(contentFilterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingFilter);

        _contentFilterRepository
            .Setup(m => m.DeleteAsync(contentFilterId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.DeleteAsync($"/api/content-filters/my/{contentFilterId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task DeleteMyContentFilter_ReturnsNotFound_WhenContentFilterDoesNotExist()
    {
        // Arrange
        var contentFilterId = Guid.NewGuid();

        _contentFilterRepository
            .Setup(m => m.GetByIdAsync(contentFilterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(ContentFilter?));

        // Act
        var response = await _client.DeleteAsync($"/api/content-filters/my/{contentFilterId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DeleteMyContentFilter_ReturnsNotFound_WhenContentFilterBelongsToAnotherUser()
    {
        // Arrange
        var contentFilterId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var existingFilter = new ContentFilter
        {
            Id = contentFilterId,
            Name = "Other User Filter",
            OwnerUserId = otherUserId,
            Criteria = DefaultCriteria,
            CreatedAt = DateTime.UtcNow
        };

        _contentFilterRepository
            .Setup(m => m.GetByIdAsync(contentFilterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingFilter);

        // Act
        var response = await _client.DeleteAsync($"/api/content-filters/my/{contentFilterId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetSharedContentFilters_ReturnsEmptyArray_WhenNoContentFilters()
    {
        // Arrange
        _contentFilterRepository
            .Setup(m => m.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var response = await _client.GetAsync("/api/content-filters").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<ContentFilter[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Is.Empty);
        }
    }

    [Test]
    public async Task GetSharedContentFilters_ReturnsContentFilters()
    {
        // Arrange
        var contentFilter = new ContentFilter
        {
            Id = Guid.NewGuid(),
            Name = "HD Movies",
            OwnerUserId = null,
            Criteria = DefaultCriteria,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = null,
            ModifiedAt = null,
            ModifiedByUserId = null
        };

        _contentFilterRepository
            .Setup(m => m.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([contentFilter]);

        // Act
        var response = await _client.GetAsync("/api/content-filters").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<ContentFilter[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(ContentFilter.Name)).EqualTo("HD Movies"));
        }
    }

    [Test]
    public async Task GetSharedContentFilters_ReturnsFiltersByName_WhenNameQueryParam()
    {
        // Arrange
        var contentFilter = new ContentFilter
        {
            Id = Guid.NewGuid(),
            Name = "Music Albums",
            OwnerUserId = null,
            Criteria = DefaultCriteria,
            CreatedAt = DateTime.UtcNow
        };

        _contentFilterRepository
            .Setup(m => m.GetByNameAsync("Music Albums", It.IsAny<CancellationToken>()))
            .ReturnsAsync(contentFilter);

        // Act
        var response = await _client.GetAsync("/api/content-filters?name=Music%20Albums").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<ContentFilter[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(ContentFilter.Name)).EqualTo("Music Albums"));
        }
    }

    [Test]
    public async Task GetSharedContentFilters_ReturnsEmpty_WhenNameNotFound()
    {
        // Arrange
        _contentFilterRepository
            .Setup(m => m.GetByNameAsync("NonExistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(ContentFilter?));

        // Act
        var response = await _client.GetAsync("/api/content-filters?name=NonExistent").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<ContentFilter[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Empty);
        }
    }

    [Test]
    public async Task GetSharedContentFilters_ReturnsEmpty_WhenNameMatchesUserFilter()
    {
        // Arrange
        var contentFilter = new ContentFilter
        {
            Id = Guid.NewGuid(),
            Name = "User Filter",
            OwnerUserId = _userId,
            Criteria = DefaultCriteria,
            CreatedAt = DateTime.UtcNow
        };

        _contentFilterRepository
            .Setup(m => m.GetByNameAsync("User Filter", It.IsAny<CancellationToken>()))
            .ReturnsAsync(contentFilter);

        // Act
        var response = await _client.GetAsync("/api/content-filters?name=User%20Filter").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<ContentFilter[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Empty);
        }
    }

    [Test]
    public async Task GetSharedContentFilters_ReturnsFiltersByTag_WhenTagQueryParam()
    {
        // Arrange
        var filter1 = new ContentFilter
        {
            Id = Guid.NewGuid(),
            Name = "Filter 1",
            OwnerUserId = null,
            Criteria = DefaultCriteria,
            CreatedAt = DateTime.UtcNow
        };

        var filter2 = new ContentFilter
        {
            Id = Guid.NewGuid(),
            Name = "Filter 2",
            OwnerUserId = null,
            Criteria = DefaultCriteria,
            CreatedAt = DateTime.UtcNow
        };

        _contentFilterRepository
            .Setup(m => m.ListByTagAsync("automation", It.IsAny<CancellationToken>()))
            .ReturnsAsync([filter1, filter2]);

        // Act
        var response = await _client.GetAsync("/api/content-filters?tag=automation").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<ContentFilter[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(2));
        }
    }

    [Test]
    public async Task GetSharedContentFilter_ReturnsContentFilter()
    {
        // Arrange
        var contentFilterId = Guid.NewGuid();
        var contentFilter = new ContentFilter
        {
            Id = contentFilterId,
            Name = "TV Episodes",
            OwnerUserId = null,
            Criteria = DefaultCriteria,
            CreatedAt = DateTime.UtcNow
        };

        _contentFilterRepository
            .Setup(m => m.GetByIdAsync(contentFilterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(contentFilter);

        // Act
        var response = await _client.GetAsync($"/api/content-filters/{contentFilterId}").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<ContentFilter>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(content.Id)).EqualTo(contentFilterId));
            Assert.That(content, Has.Property(nameof(content.Name)).EqualTo("TV Episodes"));
        }
    }

    [Test]
    public async Task GetSharedContentFilter_ReturnsNotFound_WhenContentFilterDoesNotExist()
    {
        // Arrange
        var contentFilterId = Guid.NewGuid();
        _contentFilterRepository
            .Setup(m => m.GetByIdAsync(contentFilterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(ContentFilter?));

        // Act
        var response = await _client.GetAsync($"/api/content-filters/{contentFilterId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetSharedContentFilter_ReturnsNotFound_WhenContentFilterHasOwner()
    {
        // Arrange
        var contentFilterId = Guid.NewGuid();
        var contentFilter = new ContentFilter
        {
            Id = contentFilterId,
            Name = "User Filter",
            OwnerUserId = _userId,
            Criteria = DefaultCriteria,
            CreatedAt = DateTime.UtcNow
        };

        _contentFilterRepository
            .Setup(m => m.GetByIdAsync(contentFilterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(contentFilter);

        // Act
        var response = await _client.GetAsync($"/api/content-filters/{contentFilterId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task CreateSharedContentFilter_CreatesAndReturnsContentFilter()
    {
        // Arrange
        var request = new ContentFilter
        {
            Id = Guid.Empty,
            Name = "New Shared Filter",
            Criteria = DefaultCriteria,
            CreatedAt = default,
            CreatedByUserId = null,
            ModifiedAt = null,
            ModifiedByUserId = null
        };

        _contentFilterRepository
            .Setup(m => m.CreateAsync(It.IsAny<ContentFilter>(), _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContentFilter cf, Guid? _, CancellationToken _) => cf);

        // Act
        var response = await _client.PostAsJsonAsync("/api/content-filters", request).ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<ContentFilter>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.Created));
            Assert.That(content, Has.Property(nameof(content.Name)).EqualTo("New Shared Filter"));
            Assert.That(content, Has.Property(nameof(content.OwnerUserId)).Null);
        }
    }

    [Test]
    public async Task PatchSharedContentFilter_PatchesNameField()
    {
        // Arrange
        var contentFilterId = Guid.NewGuid();
        var existingFilter = new ContentFilter
        {
            Id = contentFilterId,
            Name = "Old Name",
            OwnerUserId = null,
            Criteria = DefaultCriteria,
            CreatedAt = DateTime.UtcNow
        };
        var request = new ContentFilterPatch { Id = contentFilterId, Name = new() { Value = "Patched" } };

        _contentFilterRepository
            .Setup(m => m.GetByIdAsync(contentFilterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingFilter);

        _contentFilterRepository
            .Setup(m => m.PatchAsync(
                It.Is<ContentFilterPatch>(p => p.Id == contentFilterId && p.Name!.Value == "Patched"),
                _userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/content-filters/{contentFilterId}", request)
            .ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task PatchSharedContentFilter_PatchesTags()
    {
        // Arrange
        var contentFilterId = Guid.NewGuid();
        var existingFilter = new ContentFilter
        {
            Id = contentFilterId,
            Name = "Test",
            OwnerUserId = null,
            Criteria = DefaultCriteria,
            CreatedAt = DateTime.UtcNow
        };
        var request = new ContentFilterPatch { Id = contentFilterId, Tags = new() { Add = ["new-tag"] } };

        _contentFilterRepository
            .Setup(m => m.GetByIdAsync(contentFilterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingFilter);

        _contentFilterRepository
            .Setup(m => m.PatchAsync(
                It.Is<ContentFilterPatch>(p =>
                    p.Id == contentFilterId &&
                    p.Tags != null &&
                    p.Tags.Add != null),
                _userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/content-filters/{contentFilterId}", request)
            .ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task PatchSharedContentFilter_ReturnsNotFound_WhenContentFilterDoesNotExist()
    {
        // Arrange
        var contentFilterId = Guid.NewGuid();
        var request = new ContentFilterPatch { Id = contentFilterId, Name = new() { Value = "Patched" } };

        _contentFilterRepository
            .Setup(m => m.GetByIdAsync(contentFilterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(ContentFilter?));

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/content-filters/{contentFilterId}", request)
            .ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task PatchSharedContentFilter_ReturnsNotFound_WhenContentFilterHasOwner()
    {
        // Arrange
        var contentFilterId = Guid.NewGuid();
        var existingFilter = new ContentFilter
        {
            Id = contentFilterId,
            Name = "User Filter",
            OwnerUserId = _userId,
            Criteria = DefaultCriteria,
            CreatedAt = DateTime.UtcNow
        };
        var request = new ContentFilterPatch { Id = contentFilterId, Name = new() { Value = "Patched" } };

        _contentFilterRepository
            .Setup(m => m.GetByIdAsync(contentFilterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingFilter);

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/content-filters/{contentFilterId}", request)
            .ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DeleteSharedContentFilter_DeletesContentFilter()
    {
        // Arrange
        var contentFilterId = Guid.NewGuid();
        var existingFilter = new ContentFilter
        {
            Id = contentFilterId,
            Name = "Test",
            OwnerUserId = null,
            Criteria = DefaultCriteria,
            CreatedAt = DateTime.UtcNow
        };

        _contentFilterRepository
            .Setup(m => m.GetByIdAsync(contentFilterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingFilter);

        _contentFilterRepository
            .Setup(m => m.DeleteAsync(contentFilterId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.DeleteAsync($"/api/content-filters/{contentFilterId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task DeleteSharedContentFilter_ReturnsNotFound_WhenContentFilterDoesNotExist()
    {
        // Arrange
        var contentFilterId = Guid.NewGuid();

        _contentFilterRepository
            .Setup(m => m.GetByIdAsync(contentFilterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(ContentFilter?));

        // Act
        var response = await _client.DeleteAsync($"/api/content-filters/{contentFilterId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DeleteSharedContentFilter_ReturnsNotFound_WhenContentFilterHasOwner()
    {
        // Arrange
        var contentFilterId = Guid.NewGuid();
        var existingFilter = new ContentFilter
        {
            Id = contentFilterId,
            Name = "User Filter",
            OwnerUserId = _userId,
            Criteria = DefaultCriteria,
            CreatedAt = DateTime.UtcNow
        };

        _contentFilterRepository
            .Setup(m => m.GetByIdAsync(contentFilterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingFilter);

        // Act
        var response = await _client.DeleteAsync($"/api/content-filters/{contentFilterId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }
}
