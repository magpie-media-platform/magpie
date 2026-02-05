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
public class WishlistsEndpointGroupTests
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private Mock<IWishlistRepository> _wishlistRepository = null!;
    private Guid _userId;

    [SetUp]
    public void SetUp()
    {
        _userId = Guid.NewGuid();
        _wishlistRepository = new Mock<IWishlistRepository>();

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
            .AddPolicy(nameof(Role.Admin), policy => policy.RequireAssertion(_ => true));

        builder.Services.AddSingleton(_wishlistRepository.Object);
        builder.Services.AddSingleton<IApiEndpointGroup, WishlistsEndpointGroup>();

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
    public async Task GetMyWishlists_ReturnsEmptyArray_WhenNoWishlists()
    {
        // Arrange
        _wishlistRepository
            .Setup(m => m.GetByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var response = await _client.GetAsync("/api/wishlists/my").ConfigureAwait(false);

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
    public async Task GetMyWishlists_ReturnsWishlists()
    {
        // Arrange
        var wishlist = new Wishlist { Name = "My Wishlist", OwnerUserId = _userId, Enabled = true };

        _wishlistRepository
            .Setup(m => m.GetByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([wishlist]);

        // Act
        var response = await _client.GetAsync("/api/wishlists/my").ConfigureAwait(false);

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
    public async Task GetMyWishlist_ReturnsWishlist()
    {
        // Arrange
        var wishlistId = Guid.NewGuid();
        var wishlist = new Wishlist { Id = wishlistId, Name = "Test Wishlist", OwnerUserId = _userId, Enabled = true };

        _wishlistRepository
            .Setup(m => m.GetByIdAsync(wishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(wishlist);

        // Act
        var response = await _client.GetAsync($"/api/wishlists/my/{wishlistId}").ConfigureAwait(false);

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
    public async Task GetMyWishlist_ReturnsNotFound_WhenWishlistDoesNotExist()
    {
        // Arrange
        var wishlistId = Guid.NewGuid();
        _wishlistRepository
            .Setup(m => m.GetByIdAsync(wishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(Wishlist?));

        // Act
        var response = await _client.GetAsync($"/api/wishlists/my/{wishlistId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetMyWishlist_ReturnsNotFound_WhenWishlistBelongsToAnotherUser()
    {
        // Arrange
        var wishlistId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var wishlist = new Wishlist { Id = wishlistId, Name = "Test Wishlist", OwnerUserId = otherUserId, Enabled = true };

        _wishlistRepository
            .Setup(m => m.GetByIdAsync(wishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(wishlist);

        // Act
        var response = await _client.GetAsync($"/api/wishlists/my/{wishlistId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task CreateMyWishlist_CreatesAndReturnsWishlist()
    {
        // Arrange
        var contentFilterId = Guid.NewGuid();
        var request = new Wishlist { Name = "New Wishlist", ContentFilterId = contentFilterId, Enabled = true };

        _wishlistRepository
            .Setup(m => m.CreateAsync(It.IsAny<Wishlist>(), _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Wishlist w, Guid? _, CancellationToken _) => w);

        // Act
        var response = await _client.PostAsJsonAsync("/api/wishlists/my", request).ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<Wishlist>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.Created));
            Assert.That(content, Has.Property(nameof(content.Name)).EqualTo("New Wishlist"));
            Assert.That(content, Has.Property(nameof(content.OwnerUserId)).EqualTo(_userId));
            Assert.That(content, Has.Property(nameof(content.Enabled)).True);
        }
    }

    [Test]
    public async Task PatchMyWishlist_PatchesNameField()
    {
        // Arrange
        var wishlistId = Guid.NewGuid();
        var existingWishlist = new Wishlist { Id = wishlistId, Name = "Old Name", OwnerUserId = _userId };
        var request = new WishlistPatch { Id = wishlistId, Name = new() { Value = "Patched" } };

        _wishlistRepository
            .Setup(m => m.GetByIdAsync(wishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingWishlist);

        _wishlistRepository
            .Setup(m => m.PatchAsync(
                It.Is<WishlistPatch>(p => p.Id == wishlistId && p.Name!.Value == "Patched"),
                _userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/wishlists/my/{wishlistId}", request).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task PatchMyWishlist_PatchesOwnerLockField()
    {
        // Arrange
        var wishlistId = Guid.NewGuid();
        var existingWishlist = new Wishlist { Id = wishlistId, Name = "Test", OwnerUserId = _userId };
        var request = new WishlistPatch { Id = wishlistId, OwnerLock = new() { Value = true } };

        _wishlistRepository
            .Setup(m => m.GetByIdAsync(wishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingWishlist);

        _wishlistRepository
            .Setup(m => m.PatchAsync(
                It.Is<WishlistPatch>(p => p.Id == wishlistId && p.OwnerLock!.Value == true),
                _userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/wishlists/my/{wishlistId}", request).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task PatchMyWishlist_PatchesTags()
    {
        // Arrange
        var wishlistId = Guid.NewGuid();
        var existingWishlist = new Wishlist { Id = wishlistId, Name = "Test", OwnerUserId = _userId };
        var request = new WishlistPatch { Id = wishlistId, Tags = new() { Add = ["new-tag"] } };

        _wishlistRepository
            .Setup(m => m.GetByIdAsync(wishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingWishlist);

        _wishlistRepository
            .Setup(m => m.PatchAsync(
                It.Is<WishlistPatch>(p => p.Id == wishlistId && p.Tags != null),
                _userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/wishlists/my/{wishlistId}", request).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task PatchMyWishlist_ReturnsNotFound_WhenWishlistDoesNotExist()
    {
        // Arrange
        var wishlistId = Guid.NewGuid();
        var request = new WishlistPatch { Id = wishlistId, Name = new() { Value = "Patched" } };

        _wishlistRepository
            .Setup(m => m.GetByIdAsync(wishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(Wishlist?));

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/wishlists/my/{wishlistId}", request).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task PatchMyWishlist_ReturnsNotFound_WhenWishlistBelongsToAnotherUser()
    {
        // Arrange
        var wishlistId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var existingWishlist = new Wishlist { Id = wishlistId, Name = "Test", OwnerUserId = otherUserId };
        var request = new WishlistPatch { Id = wishlistId, Name = new() { Value = "Patched" } };

        _wishlistRepository
            .Setup(m => m.GetByIdAsync(wishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingWishlist);

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/wishlists/my/{wishlistId}", request).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DeleteMyWishlist_DeletesWishlist()
    {
        // Arrange
        var wishlistId = Guid.NewGuid();
        var existingWishlist = new Wishlist { Id = wishlistId, Name = "Test", OwnerUserId = _userId };

        _wishlistRepository
            .Setup(m => m.GetByIdAsync(wishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingWishlist);

        _wishlistRepository
            .Setup(m => m.DeleteAsync(wishlistId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.DeleteAsync($"/api/wishlists/my/{wishlistId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task DeleteMyWishlist_ReturnsNotFound_WhenWishlistDoesNotExist()
    {
        // Arrange
        var wishlistId = Guid.NewGuid();

        _wishlistRepository
            .Setup(m => m.GetByIdAsync(wishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(Wishlist?));

        // Act
        var response = await _client.DeleteAsync($"/api/wishlists/my/{wishlistId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DeleteMyWishlist_ReturnsNotFound_WhenWishlistBelongsToAnotherUser()
    {
        // Arrange
        var wishlistId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var existingWishlist = new Wishlist { Id = wishlistId, Name = "Test", OwnerUserId = otherUserId };

        _wishlistRepository
            .Setup(m => m.GetByIdAsync(wishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingWishlist);

        // Act
        var response = await _client.DeleteAsync($"/api/wishlists/my/{wishlistId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetWishlists_ReturnsAllWishlists()
    {
        // Arrange
        var wishlist = new Wishlist { Name = "All Wishlists Test", Enabled = true };

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
            Assert.That(content, Has.One.With.Property(nameof(Wishlist.Name)).EqualTo("All Wishlists Test"));
        }
    }

    [Test]
    public async Task GetWishlists_ReturnsMonitoredWishlists_WhenMonitoredQueryParam()
    {
        // Arrange
        var wishlist = new Wishlist { Name = "MonitoredWishlist", Enabled = true };

        _wishlistRepository
            .Setup(m => m.GetMonitoredAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([wishlist]);

        // Act
        var response = await _client.GetAsync("/api/wishlists?monitored=true").ConfigureAwait(false);

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
    public async Task GetWishlists_ReturnsUserWishlists_WhenUserIdQueryParam()
    {
        // Arrange
        var targetUserId = Guid.NewGuid();
        var wishlist = new Wishlist { Name = "UserWishlist", OwnerUserId = targetUserId, Enabled = true };

        _wishlistRepository
            .Setup(m => m.GetByUserIdAsync(targetUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([wishlist]);

        // Act
        var response = await _client.GetAsync($"/api/wishlists?userId={targetUserId}").ConfigureAwait(false);

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
    public async Task LockMyWishlist_SetsOwnerLock()
    {
        // Arrange
        var wishlistId = Guid.NewGuid();
        var existingWishlist = new Wishlist { Id = wishlistId, Name = "Test", OwnerUserId = _userId, OwnerLock = false };

        _wishlistRepository
            .Setup(m => m.GetByIdAsync(wishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingWishlist);

        _wishlistRepository
            .Setup(m => m.UpdateAsync(It.Is<Wishlist>(w => w.OwnerLock == true), _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.PostAsync($"/api/wishlists/my/{wishlistId}/lock", null).ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<Wishlist>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(content.OwnerLock)).True);
        }
    }

    [Test]
    public async Task LockMyWishlist_ReturnsNotFound_WhenWishlistDoesNotExist()
    {
        // Arrange
        var wishlistId = Guid.NewGuid();
        _wishlistRepository
            .Setup(m => m.GetByIdAsync(wishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(Wishlist?));

        // Act
        var response = await _client.PostAsync($"/api/wishlists/my/{wishlistId}/lock", null).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task LockMyWishlist_ReturnsNotFound_WhenWishlistBelongsToAnotherUser()
    {
        // Arrange
        var wishlistId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var existingWishlist = new Wishlist { Id = wishlistId, Name = "Test", OwnerUserId = otherUserId };

        _wishlistRepository
            .Setup(m => m.GetByIdAsync(wishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingWishlist);

        // Act
        var response = await _client.PostAsync($"/api/wishlists/my/{wishlistId}/lock", null).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task UnlockMyWishlist_ClearsOwnerLock()
    {
        // Arrange
        var wishlistId = Guid.NewGuid();
        var existingWishlist = new Wishlist { Id = wishlistId, Name = "Test", OwnerUserId = _userId, OwnerLock = true };

        _wishlistRepository
            .Setup(m => m.GetByIdAsync(wishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingWishlist);

        _wishlistRepository
            .Setup(m => m.UpdateAsync(It.Is<Wishlist>(w => w.OwnerLock == false), _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.PostAsync($"/api/wishlists/my/{wishlistId}/unlock", null).ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<Wishlist>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(content.OwnerLock)).False);
        }
    }

    [Test]
    public async Task UnlockMyWishlist_ReturnsNotFound_WhenWishlistDoesNotExist()
    {
        // Arrange
        var wishlistId = Guid.NewGuid();
        _wishlistRepository
            .Setup(m => m.GetByIdAsync(wishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(Wishlist?));

        // Act
        var response = await _client.PostAsync($"/api/wishlists/my/{wishlistId}/unlock", null).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task PatchSharedWishlist_PatchesUnlockedWishlist()
    {
        // Arrange
        var wishlistId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var existingWishlist = new Wishlist { Id = wishlistId, Name = "Old Name", OwnerUserId = otherUserId, OwnerLock = false };
        var request = new WishlistPatch { Id = wishlistId, Name = new() { Value = "Patched" } };

        _wishlistRepository
            .Setup(m => m.GetByIdAsync(wishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingWishlist);

        _wishlistRepository
            .Setup(m => m.PatchAsync(
                It.Is<WishlistPatch>(p => p.Id == wishlistId && p.Name!.Value == "Patched"),
                _userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/wishlists/{wishlistId}", request).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task PatchSharedWishlist_ReturnsForbidden_WhenOwnerLocked()
    {
        // Arrange
        var wishlistId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var existingWishlist = new Wishlist { Id = wishlistId, Name = "Test", OwnerUserId = otherUserId, OwnerLock = true };
        var request = new WishlistPatch { Id = wishlistId, Name = new() { Value = "Patched" } };

        _wishlistRepository
            .Setup(m => m.GetByIdAsync(wishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingWishlist);

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/wishlists/{wishlistId}", request).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task PatchSharedWishlist_PatchesOwnWishlistEvenIfLocked()
    {
        // Arrange
        var wishlistId = Guid.NewGuid();
        var existingWishlist = new Wishlist { Id = wishlistId, Name = "Old Name", OwnerUserId = _userId, OwnerLock = true };
        var request = new WishlistPatch { Id = wishlistId, Name = new() { Value = "Patched" } };

        _wishlistRepository
            .Setup(m => m.GetByIdAsync(wishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingWishlist);

        _wishlistRepository
            .Setup(m => m.PatchAsync(
                It.Is<WishlistPatch>(p => p.Id == wishlistId && p.Name!.Value == "Patched"),
                _userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/wishlists/{wishlistId}", request).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task PatchSharedWishlist_ReturnsNotFound_WhenWishlistDoesNotExist()
    {
        // Arrange
        var wishlistId = Guid.NewGuid();
        var request = new WishlistPatch { Id = wishlistId, Name = new() { Value = "Patched" } };

        _wishlistRepository
            .Setup(m => m.GetByIdAsync(wishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(Wishlist?));

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/wishlists/{wishlistId}", request).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task UnlockWishlist_AdminUnlocksAnyWishlist()
    {
        // Arrange
        var wishlistId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var existingWishlist = new Wishlist { Id = wishlistId, Name = "Test", OwnerUserId = otherUserId, OwnerLock = true };

        _wishlistRepository
            .Setup(m => m.GetByIdAsync(wishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingWishlist);

        _wishlistRepository
            .Setup(m => m.UpdateAsync(It.Is<Wishlist>(w => w.OwnerLock == false), _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.PostAsync($"/api/wishlists/{wishlistId}/unlock", null).ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<Wishlist>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(content.OwnerLock)).False);
        }
    }

    [Test]
    public async Task UnlockWishlist_ReturnsNotFound_WhenWishlistDoesNotExist()
    {
        // Arrange
        var wishlistId = Guid.NewGuid();
        _wishlistRepository
            .Setup(m => m.GetByIdAsync(wishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(Wishlist?));

        // Act
        var response = await _client.PostAsync($"/api/wishlists/{wishlistId}/unlock", null).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetUserWishlist_ReturnsWishlist()
    {
        // Arrange
        var targetUserId = Guid.NewGuid();
        var wishlistId = Guid.NewGuid();
        var wishlist = new Wishlist { Id = wishlistId, Name = "UserWishlist", OwnerUserId = targetUserId, Enabled = true };

        _wishlistRepository
            .Setup(m => m.GetByIdAsync(wishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(wishlist);

        // Act
        var response = await _client.GetAsync($"/api/wishlists/{targetUserId}/{wishlistId}").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<Wishlist>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(content.Id)).EqualTo(wishlistId));
            Assert.That(content, Has.Property(nameof(content.Name)).EqualTo("UserWishlist"));
        }
    }

    [Test]
    public async Task GetUserWishlist_ReturnsNotFound_WhenWishlistDoesNotExist()
    {
        // Arrange
        var targetUserId = Guid.NewGuid();
        var wishlistId = Guid.NewGuid();

        _wishlistRepository
            .Setup(m => m.GetByIdAsync(wishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(Wishlist?));

        // Act
        var response = await _client.GetAsync($"/api/wishlists/{targetUserId}/{wishlistId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetUserWishlist_ReturnsNotFound_WhenWishlistBelongsToAnotherUser()
    {
        // Arrange
        var targetUserId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var wishlistId = Guid.NewGuid();
        var wishlist = new Wishlist { Id = wishlistId, Name = "Test", OwnerUserId = otherUserId };

        _wishlistRepository
            .Setup(m => m.GetByIdAsync(wishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(wishlist);

        // Act
        var response = await _client.GetAsync($"/api/wishlists/{targetUserId}/{wishlistId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DeleteUserWishlist_DeletesWishlist()
    {
        // Arrange
        var targetUserId = Guid.NewGuid();
        var wishlistId = Guid.NewGuid();
        var existingWishlist = new Wishlist { Id = wishlistId, Name = "Test", OwnerUserId = targetUserId };

        _wishlistRepository
            .Setup(m => m.GetByIdAsync(wishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingWishlist);

        _wishlistRepository
            .Setup(m => m.DeleteAsync(wishlistId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.DeleteAsync($"/api/wishlists/{targetUserId}/{wishlistId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task DeleteUserWishlist_ReturnsNotFound_WhenWishlistDoesNotExist()
    {
        // Arrange
        var targetUserId = Guid.NewGuid();
        var wishlistId = Guid.NewGuid();

        _wishlistRepository
            .Setup(m => m.GetByIdAsync(wishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(Wishlist?));

        // Act
        var response = await _client.DeleteAsync($"/api/wishlists/{targetUserId}/{wishlistId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DeleteUserWishlist_ReturnsNotFound_WhenWishlistBelongsToAnotherUser()
    {
        // Arrange
        var targetUserId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var wishlistId = Guid.NewGuid();
        var existingWishlist = new Wishlist { Id = wishlistId, Name = "Test", OwnerUserId = otherUserId };

        _wishlistRepository
            .Setup(m => m.GetByIdAsync(wishlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingWishlist);

        // Act
        var response = await _client.DeleteAsync($"/api/wishlists/{targetUserId}/{wishlistId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }
}
