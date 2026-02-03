using System;
using System.Collections.Generic;
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
public class MediaServersEndpointGroupTests
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private Mock<IMediaServerRepository> _repository = null!;

    [SetUp]
    public void SetUp()
    {
        _repository = new Mock<IMediaServerRepository>();

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
        builder.Services.AddSingleton<IApiEndpointGroup, MediaServersEndpointGroup>();

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
    public async Task GetAllMediaServers_ReturnsEmptyArray_WhenNoMediaServers()
    {
        // Arrange
        _repository
            .Setup(m => m.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var response = await _client.GetAsync("/api/media-servers").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<MediaServer[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Is.Empty);
        }
    }

    [Test]
    public async Task GetAllMediaServers_ReturnsMediaServers()
    {
        // Arrange
        var server = new MediaServer { Name = "Plex Server", ProviderType = "plex", Enabled = true };

        _repository
            .Setup(m => m.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([server]);

        // Act
        var response = await _client.GetAsync("/api/media-servers").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<MediaServer[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(MediaServer.Name)).EqualTo("Plex Server"));
            Assert.That(content, Has.One.With.Property(nameof(MediaServer.ProviderType)).EqualTo("plex"));
            Assert.That(content, Has.One.With.Property(nameof(MediaServer.Enabled)).True);
        }
    }

    [Test]
    public async Task GetMediaServerById_ReturnsMediaServer()
    {
        // Arrange
        var serverId = Guid.NewGuid();
        var server = new MediaServer
        {
            Id = serverId,
            Name = "Jellyfin Server",
            ProviderType = "jellyfin",
            Enabled = true,
        };

        _repository
            .Setup(m => m.GetByIdAsync(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(server);

        // Act
        var response = await _client.GetAsync($"/api/media-servers/{serverId}").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<MediaServer>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(content.Id)).EqualTo(serverId));
            Assert.That(content, Has.Property(nameof(content.Name)).EqualTo("Jellyfin Server"));
            Assert.That(content, Has.Property(nameof(content.ProviderType)).EqualTo("jellyfin"));
        }
    }

    [Test]
    public async Task GetMediaServerById_ReturnsNotFound_WhenMediaServerDoesNotExist()
    {
        // Arrange
        var serverId = Guid.NewGuid();
        _repository
            .Setup(m => m.GetByIdAsync(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(MediaServer?));

        // Act
        var response = await _client.GetAsync($"/api/media-servers/{serverId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task CreateMediaServer_CreatesAndReturnsMediaServer()
    {
        // Arrange
        var request = new MediaServer
        {
            Name = "New Plex Server",
            ProviderType = "plex",
            Enabled = true,
            Settings = new Dictionary<string, string> { ["host"] = "192.168.1.100", ["port"] = "32400" }
        };

        _repository
            .Setup(m => m.CreateAsync(It.IsAny<MediaServer>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MediaServer m, Guid? _, CancellationToken _) => m);

        // Act
        var response = await _client.PostAsJsonAsync("/api/media-servers", request).ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<MediaServer>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.Created));
            Assert.That(content, Has.Property(nameof(content.Name)).EqualTo("New Plex Server"));
            Assert.That(content, Has.Property(nameof(content.ProviderType)).EqualTo("plex"));
            Assert.That(content, Has.Property(nameof(content.Enabled)).True);
            Assert.That(content, Has.Property(nameof(content.Settings)).ContainKey("host"));
            Assert.That(content, Has.Property(nameof(content.Settings)).ContainValue("192.168.1.100"));
        }
    }

    [Test]
    public async Task PatchMediaServer_PatchesNameField()
    {
        // Arrange
        var serverId = Guid.NewGuid();
        var request = new MediaServerPatch { Id = serverId, Name = new() { Value = "Patched" } };

        _repository
            .Setup(m => m.PatchAsync(
                It.Is<MediaServerPatch>(p => p.Id == serverId && p.Name!.Value == "Patched"),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/media-servers/{serverId}", request).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task PatchMediaServer_PatchesEnabledField()
    {
        // Arrange
        var serverId = Guid.NewGuid();
        var request = new MediaServerPatch { Id = serverId, Enabled = new() { Value = false } };

        _repository
            .Setup(m => m.PatchAsync(
                It.Is<MediaServerPatch>(p => p.Id == serverId && p.Enabled!.Value == false),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/media-servers/{serverId}", request).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task PatchMediaServer_ReturnsNotFound_WhenMediaServerDoesNotExist()
    {
        // Arrange
        var serverId = Guid.NewGuid();
        var request = new MediaServerPatch { Id = serverId, Name = new() { Value = "Patched" } };

        _repository
            .Setup(m => m.PatchAsync(It.IsAny<MediaServerPatch>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/media-servers/{serverId}", request).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DeleteMediaServer_DeletesMediaServer()
    {
        // Arrange
        var serverId = Guid.NewGuid();

        _repository
            .Setup(m => m.DeleteAsync(serverId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.DeleteAsync($"/api/media-servers/{serverId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task DeleteMediaServer_ReturnsNotFound_WhenMediaServerDoesNotExist()
    {
        // Arrange
        var serverId = Guid.NewGuid();

        _repository
            .Setup(m => m.DeleteAsync(serverId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var response = await _client.DeleteAsync($"/api/media-servers/{serverId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }
}
