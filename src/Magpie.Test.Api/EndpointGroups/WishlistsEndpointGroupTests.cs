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
    private Mock<IWishlistRepository> _wishlistRepository = null!;

    [SetUp]
    public void SetUp()
    {
        _wishlistRepository = new Mock<IWishlistRepository>();

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

        builder.Services.AddSingleton(_wishlistRepository.Object);
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
        // Arrange
        _wishlistRepository
            .Setup(m => m.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var response = await _client.GetAsync("/api/wishlists").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<Wishlist[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Is.Empty);
        }
    }

    [Test]
    public async Task GetAllWishlists_ReturnsWishlists()
    {
        // Arrange
        var wishlist = new Wishlist { Name = "My Wishlist", Enabled = true };

        _wishlistRepository
            .Setup(m => m.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([wishlist]);

        // Act
        var response = await _client.GetAsync("/api/wishlists").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<Wishlist[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(Wishlist.Name)).EqualTo("My Wishlist"));
        }
    }

    [Test]
    public async Task GetWishlistById_ReturnsWishlist()
    {
        // Arrange
        var wishlistId = Guid.NewGuid();
        var wishlist = new Wishlist { Id = wishlistId, Name = "Test Wishlist", Enabled = true };

        _wishlistRepository
            .Setup(m => m.GetByIdAsync(wishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(wishlist);

        // Act
        var response = await _client.GetAsync($"/api/wishlists/{wishlistId}").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<Wishlist>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(content.Id)).EqualTo(wishlistId));
            Assert.That(content, Has.Property(nameof(content.Name)).EqualTo("Test Wishlist"));
        }
    }

    [Test]
    public async Task GetWishlistById_ReturnsNotFound_WhenWishlistDoesNotExist()
    {
        // Arrange
        var wishlistId = Guid.NewGuid();
        _wishlistRepository
            .Setup(m => m.GetByIdAsync(wishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(Wishlist?));

        // Act
        var response = await _client.GetAsync($"/api/wishlists/{wishlistId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetWishlistsByUser_ReturnsFilteredWishlists()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var wishlist = new Wishlist { Name = "UserWishlist", OwnerUserId = userId, Enabled = true };

        _wishlistRepository
            .Setup(m => m.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([wishlist]);

        // Act
        var response = await _client.GetAsync($"/api/wishlists/by-user/{userId}").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<Wishlist[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(Wishlist.Name)).EqualTo("UserWishlist"));
        }
    }

    [Test]
    public async Task GetMonitoredWishlists_ReturnsOnlyMonitoredWishlists()
    {
        // Arrange
        var wishlist = new Wishlist { Name = "MonitoredWishlist", Enabled = true };

        _wishlistRepository
            .Setup(m => m.GetMonitoredAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([wishlist]);

        // Act
        var response = await _client.GetAsync("/api/wishlists/monitored").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<Wishlist[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(Wishlist.Name)).EqualTo("MonitoredWishlist"));
        }
    }

    [Test]
    public async Task CreateWishlist_CreatesAndReturnsWishlist()
    {
        // Arrange
        var contentFilterId = Guid.NewGuid();
        var request = new Wishlist { Name = "New Wishlist", ContentFilterId = contentFilterId, Enabled = true };

        _wishlistRepository
            .Setup(m => m.CreateAsync(It.IsAny<Wishlist>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Wishlist w, Guid? _, CancellationToken _) => w);

        // Act
        var response = await _client.PostAsJsonAsync("/api/wishlists", request).ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<Wishlist>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.Created));
            Assert.That(content, Has.Property(nameof(content.Name)).EqualTo("New Wishlist"));
            Assert.That(content, Has.Property(nameof(content.Enabled)).True);
        }
    }

    [Test]
    public async Task PatchWishlist_PatchesNameField()
    {
        // Arrange
        var wishlistId = Guid.NewGuid();
        var request = new WishlistPatch { Id = wishlistId, Name = new() { Value = "Patched" } };

        _wishlistRepository
            .Setup(m => m.PatchAsync(
                It.Is<WishlistPatch>(p => p.Id == wishlistId && p.Name!.Value == "Patched"),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/wishlists/{wishlistId}", request).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task PatchWishlist_PatchesOwnerLockField()
    {
        // Arrange
        var wishlistId = Guid.NewGuid();
        var request = new WishlistPatch { Id = wishlistId, OwnerLock = new() { Value = true } };

        _wishlistRepository
            .Setup(m => m.PatchAsync(
                It.Is<WishlistPatch>(p => p.Id == wishlistId && p.OwnerLock!.Value == true),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/wishlists/{wishlistId}", request).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task PatchWishlist_PatchesTags()
    {
        // Arrange
        var wishlistId = Guid.NewGuid();
        var request = new WishlistPatch { Id = wishlistId, Tags = new() { Add = ["new-tag"] } };

        _wishlistRepository
            .Setup(m => m.PatchAsync(
                It.Is<WishlistPatch>(p => p.Id == wishlistId && p.Tags != null),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/wishlists/{wishlistId}", request).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task PatchWishlist_ReturnsNotFound_WhenWishlistDoesNotExist()
    {
        // Arrange
        var wishlistId = Guid.NewGuid();
        var request = new WishlistPatch { Id = wishlistId, Name = new() { Value = "Patched" } };

        _wishlistRepository
            .Setup(m => m.PatchAsync(It.IsAny<WishlistPatch>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/wishlists/{wishlistId}", request).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DeleteWishlist_DeletesWishlist()
    {
        // Arrange
        var wishlistId = Guid.NewGuid();

        _wishlistRepository
            .Setup(m => m.DeleteAsync(wishlistId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.DeleteAsync($"/api/wishlists/{wishlistId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task DeleteWishlist_ReturnsNotFound_WhenWishlistDoesNotExist()
    {
        // Arrange
        var wishlistId = Guid.NewGuid();

        _wishlistRepository
            .Setup(m => m.DeleteAsync(wishlistId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var response = await _client.DeleteAsync($"/api/wishlists/{wishlistId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }
}
