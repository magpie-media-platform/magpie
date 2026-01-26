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
using Magpie.Models.Result;
using Magpie.Models.Wishlists;
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
public class WishlistsEndpointGroupTests
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private Mock<IWishlistRepository> _wishlistRepositoryMock = null!;

    [SetUp]
    public void SetUp()
    {
        _wishlistRepositoryMock = new Mock<IWishlistRepository>();

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

        builder.Services.AddSingleton(_wishlistRepositoryMock.Object);
        builder.Services.AddSingleton<IApiEndpointGroup, WishlistsEndpointGroup>();

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
    public async Task GetAllWishlists_ReturnsEmptyArray_WhenNoWishlists()
    {
        _wishlistRepositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<Wishlist>>([]));

        var response = await _client.GetAsync("/api/wishlists").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Wishlist[]>().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Is.Empty);
        }
    }

    [Test]
    public async Task GetAllWishlists_ReturnsWishlists()
    {
        var wishlist = CreateWishlist("My Wishlist");

        _wishlistRepositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<Wishlist>>([wishlist]));

        var response = await _client.GetAsync("/api/wishlists").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Wishlist[]>().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(Wishlist.Name)).EqualTo("My Wishlist"));
        }
    }

    [Test]
    public async Task GetWishlistById_ReturnsWishlist()
    {
        var wishlistId = Guid.NewGuid();
        var wishlist = CreateWishlist("Test Wishlist", wishlistId);

        _wishlistRepositoryMock
            .Setup(r => r.GetByIdAsync(wishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<Wishlist?>(wishlist));

        var response = await _client.GetAsync($"/api/wishlists/{wishlistId}").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Wishlist>().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(Wishlist.Id)).EqualTo(wishlistId));
            Assert.That(content, Has.Property(nameof(Wishlist.Name)).EqualTo("Test Wishlist"));
        }
    }

    [Test]
    public async Task GetWishlistById_ReturnsNotFound_WhenWishlistDoesNotExist()
    {
        var wishlistId = Guid.NewGuid();
        _wishlistRepositoryMock
            .Setup(r => r.GetByIdAsync(wishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<Wishlist?>(null));

        var response = await _client.GetAsync($"/api/wishlists/{wishlistId}").ConfigureAwait(false);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetWishlistByName_ReturnsWishlist()
    {
        var wishlist = CreateWishlist("UniqueWishlistName");

        _wishlistRepositoryMock
            .Setup(r => r.GetByNameAsync("UniqueWishlistName", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<Wishlist?>(wishlist));

        var response = await _client.GetAsync("/api/wishlists/by-name/UniqueWishlistName").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Wishlist>().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(Wishlist.Name)).EqualTo("UniqueWishlistName"));
        }
    }

    [Test]
    public async Task GetWishlistByName_ReturnsNotFound_WhenWishlistDoesNotExist()
    {
        _wishlistRepositoryMock
            .Setup(r => r.GetByNameAsync("NonExistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<Wishlist?>(null));

        var response = await _client.GetAsync("/api/wishlists/by-name/NonExistent").ConfigureAwait(false);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetWishlistsByUser_ReturnsFilteredWishlists()
    {
        var userId = Guid.NewGuid();
        var wishlist = CreateWishlist("UserWishlist", userId: userId);

        _wishlistRepositoryMock
            .Setup(r => r.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<Wishlist>>([wishlist]));

        var response = await _client.GetAsync($"/api/wishlists/by-user/{userId}").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Wishlist[]>().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(Wishlist.Name)).EqualTo("UserWishlist"));
        }
    }

    [Test]
    public async Task GetWishlistsByTag_ReturnsFilteredWishlists()
    {
        var wishlist = CreateWishlist("Wishlist1", tags: ["automation"]);

        _wishlistRepositoryMock
            .Setup(r => r.GetByTagAsync("automation", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<Wishlist>>([wishlist]));

        var response = await _client.GetAsync("/api/wishlists/by-tag/automation").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Wishlist[]>().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(Wishlist.Name)).EqualTo("Wishlist1"));
        }
    }

    [Test]
    public async Task GetWishlistsByContentFilter_ReturnsFilteredWishlists()
    {
        var contentFilterId = Guid.NewGuid();
        var wishlist = CreateWishlist("Wishlist1", contentFilterId: contentFilterId);

        _wishlistRepositoryMock
            .Setup(r => r.GetByContentFilterIdAsync(contentFilterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<Wishlist>>([wishlist]));

        var response = await _client.GetAsync($"/api/wishlists/by-content-filter/{contentFilterId}")
            .ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Wishlist[]>().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(Wishlist.Name)).EqualTo("Wishlist1"));
        }
    }

    [Test]
    public async Task GetMonitoredWishlists_ReturnsOnlyMonitoredWishlists()
    {
        var wishlist = CreateWishlist("MonitoredWishlist", monitored: true);

        _wishlistRepositoryMock
            .Setup(r => r.GetMonitoredAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<Wishlist>>([wishlist]));

        var response = await _client.GetAsync("/api/wishlists/monitored").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Wishlist[]>().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(Wishlist.Name)).EqualTo("MonitoredWishlist"));
        }
    }

    [Test]
    public async Task CreateWishlist_CreatesAndReturnsWishlist()
    {
        var contentFilterId = Guid.NewGuid();
        var request = CreateWishlist("New Wishlist", contentFilterId: contentFilterId);

        _wishlistRepositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<Wishlist>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Wishlist w, Guid? _, CancellationToken _) => Result.Success(w));

        var response = await _client.PostAsJsonAsync("/api/wishlists", request).ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Wishlist>().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(content, Has.Property(nameof(Wishlist.Name)).EqualTo("New Wishlist"));
            Assert.That(content, Has.Property(nameof(Wishlist.Monitored)).True);
            Assert.That(content, Has.Property(nameof(Wishlist.Tags)).Empty);
            Assert.That(response.Headers.Location?.ToString(), Does.Contain($"/api/wishlists/{content!.Id}"));
        }
    }

    [Test]
    public async Task CreateWishlist_WithSyncProvider_CreatesWishlist()
    {
        var userId = Guid.NewGuid();
        var contentFilterId = Guid.NewGuid();
        var request = new Wishlist(
            Id: Guid.Empty,
            Name: "Trakt Wishlist",
            UserId: userId,
            OwnerLock: true,
            ContentFilterId: contentFilterId,
            QualityTierListId: null,
            Monitored: true,
            Tags: [],
            SyncProvider: "trakt",
            SyncListId: "watchlist",
            SyncInterval: TimeSpan.FromHours(1),
            LastSyncedAt: null,
            CreatedAt: default,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _wishlistRepositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<Wishlist>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Wishlist w, Guid? _, CancellationToken _) => Result.Success(w));

        var response = await _client.PostAsJsonAsync("/api/wishlists", request).ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Wishlist>().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(content, Has.Property(nameof(Wishlist.SyncProvider)).EqualTo("trakt"));
            Assert.That(content, Has.Property(nameof(Wishlist.SyncListId)).EqualTo("watchlist"));
            Assert.That(content, Has.Property(nameof(Wishlist.SyncInterval)).EqualTo(TimeSpan.FromHours(1)));
        }
    }

    [Test]
    public async Task PatchWishlist_PatchesNameField()
    {
        var wishlistId = Guid.NewGuid();
        var request = new WishlistPatch { Name = new FieldPatch<string>("Patched") };

        _wishlistRepositoryMock
            .Setup(r => r.PatchAsync(
                It.Is<WishlistPatch>(p => p.Id == wishlistId && p.Name!.Value == "Patched"),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        var response = await _client.PatchAsJsonAsync($"/api/wishlists/{wishlistId}", request).ConfigureAwait(false);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task PatchWishlist_PatchesMonitoredField()
    {
        var wishlistId = Guid.NewGuid();
        var request = new WishlistPatch { Monitored = new FieldPatch<bool>(false) };

        _wishlistRepositoryMock
            .Setup(r => r.PatchAsync(
                It.Is<WishlistPatch>(p => p.Id == wishlistId && p.Monitored!.Value == false),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        var response = await _client.PatchAsJsonAsync($"/api/wishlists/{wishlistId}", request).ConfigureAwait(false);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task PatchWishlist_PatchesTags()
    {
        var wishlistId = Guid.NewGuid();
        var request = new WishlistPatch { Tags = new SetPatch<string, string>(["new-tag"], null) };

        _wishlistRepositoryMock
            .Setup(r => r.PatchAsync(
                It.Is<WishlistPatch>(p => p.Id == wishlistId && p.Tags != null),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        var response = await _client.PatchAsJsonAsync($"/api/wishlists/{wishlistId}", request).ConfigureAwait(false);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task PatchWishlist_ReturnsNotFound_WhenWishlistDoesNotExist()
    {
        var wishlistId = Guid.NewGuid();
        var request = new WishlistPatch { Name = new FieldPatch<string>("Patched") };

        _wishlistRepositoryMock
            .Setup(r => r.PatchAsync(It.IsAny<WishlistPatch>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(false));

        var response = await _client.PatchAsJsonAsync($"/api/wishlists/{wishlistId}", request).ConfigureAwait(false);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task UpdateLastSynced_UpdatesTimestamp()
    {
        var wishlistId = Guid.NewGuid();
        var syncTime = DateTimeOffset.UtcNow;
        var request = new WishlistSyncUpdate(syncTime);

        _wishlistRepositoryMock
            .Setup(r => r.UpdateLastSyncedAsync(wishlistId, syncTime, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        var response = await _client.PostAsJsonAsync($"/api/wishlists/{wishlistId}/sync", request)
            .ConfigureAwait(false);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task UpdateLastSynced_ReturnsNotFound_WhenWishlistDoesNotExist()
    {
        var wishlistId = Guid.NewGuid();
        var request = new WishlistSyncUpdate(DateTimeOffset.UtcNow);

        _wishlistRepositoryMock
            .Setup(r => r.UpdateLastSyncedAsync(wishlistId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(false));

        var response = await _client.PostAsJsonAsync($"/api/wishlists/{wishlistId}/sync", request)
            .ConfigureAwait(false);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DeleteWishlist_DeletesWishlist()
    {
        var wishlistId = Guid.NewGuid();

        _wishlistRepositoryMock
            .Setup(r => r.DeleteAsync(wishlistId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        var response = await _client.DeleteAsync($"/api/wishlists/{wishlistId}").ConfigureAwait(false);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task DeleteWishlist_ReturnsNotFound_WhenWishlistDoesNotExist()
    {
        var wishlistId = Guid.NewGuid();

        _wishlistRepositoryMock
            .Setup(r => r.DeleteAsync(wishlistId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(false));

        var response = await _client.DeleteAsync($"/api/wishlists/{wishlistId}").ConfigureAwait(false);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    private static Wishlist CreateWishlist(
        string name,
        Guid? id = null,
        Guid? userId = null,
        Guid? contentFilterId = null,
        bool monitored = true,
        IReadOnlyList<string>? tags = null) =>
        new(
            Id: id ?? Guid.NewGuid(),
            Name: name,
            UserId: userId,
            OwnerLock: false,
            ContentFilterId: contentFilterId ?? Guid.NewGuid(),
            QualityTierListId: null,
            Monitored: monitored,
            Tags: tags ?? [],
            SyncProvider: null,
            SyncListId: null,
            SyncInterval: null,
            LastSyncedAt: null,
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);
}
