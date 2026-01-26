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
using Magpie.Models.MediaServers;
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
public class MediaServersEndpointGroupTests
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private Mock<IMediaServerRepository> _repositoryMock = null!;

    [SetUp]
    public void SetUp()
    {
        _repositoryMock = new Mock<IMediaServerRepository>();

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

        builder.Services.AddSingleton(_repositoryMock.Object);
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
        _repositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<MediaServer>>([]));

        var response = await _client.GetAsync("/api/media-servers").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<MediaServer[]>().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Is.Empty);
        }
    }

    [Test]
    public async Task GetAllMediaServers_ReturnsMediaServers()
    {
        var server = new MediaServer(
            Id: Guid.NewGuid(),
            Name: "Plex Server",
            Provider: "plex",
            Enabled: true,
            Settings: new Dictionary<string, string?>(),
            Status: MediaServerStatus.Healthy,
            LastChecked: null,
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _repositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<MediaServer>>([server]));

        var response = await _client.GetAsync("/api/media-servers").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<MediaServer[]>().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(MediaServer.Name)).EqualTo("Plex Server"));
            Assert.That(content, Has.One.With.Property(nameof(MediaServer.Provider)).EqualTo("plex"));
            Assert.That(content, Has.One.With.Property(nameof(MediaServer.Enabled)).True);
        }
    }

    [Test]
    public async Task GetMediaServerById_ReturnsMediaServer()
    {
        var serverId = Guid.NewGuid();
        var server = new MediaServer(
            Id: serverId,
            Name: "Jellyfin Server",
            Provider: "jellyfin",
            Enabled: true,
            Settings: new Dictionary<string, string?>(),
            Status: MediaServerStatus.Unknown,
            LastChecked: null,
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _repositoryMock
            .Setup(r => r.GetByIdAsync(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<MediaServer?>(server));

        var response = await _client.GetAsync($"/api/media-servers/{serverId}").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<MediaServer>().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(MediaServer.Id)).EqualTo(serverId));
            Assert.That(content, Has.Property(nameof(MediaServer.Name)).EqualTo("Jellyfin Server"));
            Assert.That(content, Has.Property(nameof(MediaServer.Provider)).EqualTo("jellyfin"));
        }
    }

    [Test]
    public async Task GetMediaServerById_ReturnsNotFound_WhenMediaServerDoesNotExist()
    {
        var serverId = Guid.NewGuid();
        _repositoryMock
            .Setup(r => r.GetByIdAsync(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<MediaServer?>(null));

        var response = await _client.GetAsync($"/api/media-servers/{serverId}").ConfigureAwait(false);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetMediaServersByProvider_ReturnsFilteredMediaServers()
    {
        var server1 = new MediaServer(
            Id: Guid.NewGuid(),
            Name: "Plex 1",
            Provider: "plex",
            Enabled: true,
            Settings: new Dictionary<string, string?>(),
            Status: MediaServerStatus.Healthy,
            LastChecked: null,
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        var server2 = new MediaServer(
            Id: Guid.NewGuid(),
            Name: "Plex 2",
            Provider: "plex",
            Enabled: true,
            Settings: new Dictionary<string, string?>(),
            Status: MediaServerStatus.Healthy,
            LastChecked: null,
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _repositoryMock
            .Setup(r => r.GetByProviderAsync("plex", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<MediaServer>>([server1, server2]));

        var response = await _client.GetAsync("/api/media-servers/by-provider/plex").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<MediaServer[]>().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(2));
            Assert.That(content, Has.All.With.Property(nameof(MediaServer.Provider)).EqualTo("plex"));
        }
    }

    [Test]
    public async Task GetMediaServersByStatus_ReturnsFilteredMediaServers()
    {
        var server = new MediaServer(
            Id: Guid.NewGuid(),
            Name: "Healthy Server",
            Provider: "plex",
            Enabled: true,
            Settings: new Dictionary<string, string?>(),
            Status: MediaServerStatus.Healthy,
            LastChecked: null,
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _repositoryMock
            .Setup(r => r.GetByStatusAsync(MediaServerStatus.Healthy, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<MediaServer>>([server]));

        var response = await _client.GetAsync("/api/media-servers/by-status/1").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<MediaServer[]>().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(MediaServer.Name)).EqualTo("Healthy Server"));
            Assert.That(content, Has.One.With.Property(nameof(MediaServer.Status)).EqualTo(MediaServerStatus.Healthy));
        }
    }

    [Test]
    public async Task GetEnabledMediaServers_ReturnsOnlyEnabledServers()
    {
        var server = new MediaServer(
            Id: Guid.NewGuid(),
            Name: "Enabled Server",
            Provider: "plex",
            Enabled: true,
            Settings: new Dictionary<string, string?>(),
            Status: MediaServerStatus.Healthy,
            LastChecked: null,
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _repositoryMock
            .Setup(r => r.GetEnabledAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<MediaServer>>([server]));

        var response = await _client.GetAsync("/api/media-servers/enabled").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<MediaServer[]>().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(MediaServer.Name)).EqualTo("Enabled Server"));
            Assert.That(content, Has.One.With.Property(nameof(MediaServer.Enabled)).True);
        }
    }

    [Test]
    public async Task CreateMediaServer_CreatesAndReturnsMediaServer()
    {
        var request = new MediaServer(
            Id: Guid.Empty,
            Name: "New Plex Server",
            Provider: "plex",
            Enabled: true,
            Settings: new Dictionary<string, string?> { ["host"] = "192.168.1.100", ["port"] = "32400" },
            Status: MediaServerStatus.Unknown,
            LastChecked: null,
            CreatedAt: default,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _repositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<MediaServer>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MediaServer m, Guid? _, CancellationToken _) => Result.Success(m));

        var response = await _client.PostAsJsonAsync("/api/media-servers", request).ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<MediaServer>().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(content, Has.Property(nameof(MediaServer.Name)).EqualTo("New Plex Server"));
            Assert.That(content, Has.Property(nameof(MediaServer.Provider)).EqualTo("plex"));
            Assert.That(content, Has.Property(nameof(MediaServer.Enabled)).True);
            Assert.That(content!.Settings, Does.ContainKey("host"));
            Assert.That(content.Settings["host"], Is.EqualTo("192.168.1.100"));
            Assert.That(response.Headers.Location?.ToString(), Does.Contain($"/api/media-servers/{content.Id}"));
        }
    }

    [Test]
    public async Task PatchMediaServer_PatchesNameField()
    {
        var serverId = Guid.NewGuid();
        var request = new MediaServerPatch { Name = new FieldPatch<string>("Patched") };

        _repositoryMock
            .Setup(r => r.PatchAsync(
                It.Is<MediaServerPatch>(p => p.Id == serverId && p.Name!.Value == "Patched"),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        var response = await _client.PatchAsJsonAsync($"/api/media-servers/{serverId}", request).ConfigureAwait(false);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task PatchMediaServer_PatchesEnabledField()
    {
        var serverId = Guid.NewGuid();
        var request = new MediaServerPatch { Enabled = new FieldPatch<bool>(false) };

        _repositoryMock
            .Setup(r => r.PatchAsync(
                It.Is<MediaServerPatch>(p => p.Id == serverId && p.Enabled!.Value == false),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        var response = await _client.PatchAsJsonAsync($"/api/media-servers/{serverId}", request).ConfigureAwait(false);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task PatchMediaServer_ReturnsNotFound_WhenMediaServerDoesNotExist()
    {
        var serverId = Guid.NewGuid();
        var request = new MediaServerPatch { Name = new FieldPatch<string>("Patched") };

        _repositoryMock
            .Setup(r => r.PatchAsync(It.IsAny<MediaServerPatch>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(false));

        var response = await _client.PatchAsJsonAsync($"/api/media-servers/{serverId}", request).ConfigureAwait(false);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DeleteMediaServer_DeletesMediaServer()
    {
        var serverId = Guid.NewGuid();

        _repositoryMock
            .Setup(r => r.DeleteAsync(serverId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        var response = await _client.DeleteAsync($"/api/media-servers/{serverId}").ConfigureAwait(false);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task DeleteMediaServer_ReturnsNotFound_WhenMediaServerDoesNotExist()
    {
        var serverId = Guid.NewGuid();

        _repositoryMock
            .Setup(r => r.DeleteAsync(serverId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(false));

        var response = await _client.DeleteAsync($"/api/media-servers/{serverId}").ConfigureAwait(false);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
