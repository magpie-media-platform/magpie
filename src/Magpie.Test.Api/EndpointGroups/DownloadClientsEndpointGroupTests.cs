using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Integrations;
using Magpie.Abstractions.Repositories;
using Magpie.Api.EndpointGroups;
using Magpie.Models;
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
public class DownloadClientsEndpointGroupTests
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private Mock<IDownloadClientRepository> _downloadClientRepository = null!;
    private FakeDownloadClientIntegration _integration = null!;

    [SetUp]
    public void SetUp()
    {
        _downloadClientRepository = new Mock<IDownloadClientRepository>();
        _integration = new FakeDownloadClientIntegration();

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
                .Build())
            .AddPolicy(nameof(Role.Viewer), policy => policy.RequireAssertion(_ => true))
            .AddPolicy(nameof(Role.Manager), policy => policy.RequireAssertion(_ => true));

        builder.Services.AddSingleton(_downloadClientRepository.Object);
        builder.Services.AddSingleton<IDownloadClientIntegration>(_integration);
        builder.Services.AddSingleton<IApiEndpointGroup, DownloadClientsEndpointGroup>();

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
    public async Task GetAllDownloadClients_ReturnsEmptyArray_WhenNoDownloadClients()
    {
        // Arrange
        _downloadClientRepository
            .Setup(m => m.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var response = await _client.GetAsync("/api/download-clients").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<DownloadClient[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Is.Empty);
        }
    }

    [Test]
    public async Task GetAllDownloadClients_ReturnsDownloadClients()
    {
        // Arrange
        var downloadClient = new DownloadClient
        {
            Id = Guid.NewGuid(),
            Name = "qBittorrent",
            ProviderType = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Healthy,
            Settings = new()
            {
                ["Host"] = "localhost",
                ["Port"] = "8080"
            },
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = null,
            ModifiedAt = null,
            ModifiedByUserId = null
        };

        _downloadClientRepository
            .Setup(m => m.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([downloadClient]);

        // Act
        var response = await _client.GetAsync("/api/download-clients").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<DownloadClient[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(DownloadClient.Name)).EqualTo("qBittorrent"));
            Assert.That(content, Has.One.With.Property(nameof(DownloadClient.ProviderType)).EqualTo("qbittorrent"));
        }
    }

    [Test]
    public async Task GetDownloadClientById_ReturnsDownloadClient()
    {
        // Arrange
        var downloadClientId = Guid.NewGuid();
        var downloadClient = new DownloadClient
        {
            Id = downloadClientId,
            Name = "Transmission",
            ProviderType = "transmission",
            Enabled = true,
            Status = DownloadClientStatus.Unknown,
            Settings = [],
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = null,
            ModifiedAt = null,
            ModifiedByUserId = null
        };

        _downloadClientRepository
            .Setup(m => m.GetByIdAsync(downloadClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(downloadClient);

        // Act
        var response = await _client.GetAsync($"/api/download-clients/{downloadClientId}").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<DownloadClient>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(content.Id)).EqualTo(downloadClientId));
            Assert.That(content, Has.Property(nameof(content.Name)).EqualTo("Transmission"));
            Assert.That(content, Has.Property(nameof(content.ProviderType)).EqualTo("transmission"));
        }
    }

    [Test]
    public async Task GetDownloadClientById_ReturnsNotFound_WhenDownloadClientDoesNotExist()
    {
        // Arrange
        var downloadClientId = Guid.NewGuid();
        _downloadClientRepository
            .Setup(m => m.GetByIdAsync(downloadClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(DownloadClient?));

        // Act
        var response = await _client.GetAsync($"/api/download-clients/{downloadClientId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task CreateDownloadClient_CreatesAndReturnsDownloadClient()
    {
        // Arrange
        var request = new DownloadClient
        {
            Id = Guid.Empty,
            Name = "New Client",
            ProviderType = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Unknown,
            Settings = new()
            {
                ["Host"] = "localhost"
            },
            CreatedAt = default,
            CreatedByUserId = null,
            ModifiedAt = null,
            ModifiedByUserId = null
        };

        _downloadClientRepository
            .Setup(m => m.CreateAsync(It.IsAny<DownloadClient>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DownloadClient dc, Guid? _, CancellationToken _) => dc);

        // Act
        var response = await _client.PostAsJsonAsync("/api/download-clients", request).ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<DownloadClient>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.Created));
            Assert.That(content, Has.Property(nameof(content.Name)).EqualTo("New Client"));
            Assert.That(content, Has.Property(nameof(content.ProviderType)).EqualTo("qbittorrent"));
        }
    }

    [Test]
    public async Task PatchDownloadClient_PatchesFields()
    {
        // Arrange
        var downloadClientId = Guid.NewGuid();
        var request = new DownloadClientPatch { Id = downloadClientId, Name = new() { Value = "Patched Client" } };

        _downloadClientRepository
            .Setup(m => m.PatchAsync(
                It.Is<DownloadClientPatch>(p => p.Id == downloadClientId && p.Name!.Value == "Patched Client"),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/download-clients/{downloadClientId}", request)
            .ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task PatchDownloadClient_ReturnsNotFound_WhenDownloadClientDoesNotExist()
    {
        // Arrange
        var downloadClientId = Guid.NewGuid();
        var request = new DownloadClientPatch { Id = downloadClientId, Name = new() { Value = "Patched" } };

        _downloadClientRepository
            .Setup(m => m.PatchAsync(It.IsAny<DownloadClientPatch>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/download-clients/{downloadClientId}", request)
            .ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DeleteDownloadClient_DeletesDownloadClient()
    {
        // Arrange
        var downloadClientId = Guid.NewGuid();

        _downloadClientRepository
            .Setup(m => m.DeleteAsync(downloadClientId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.DeleteAsync($"/api/download-clients/{downloadClientId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task DeleteDownloadClient_ReturnsNotFound_WhenDownloadClientDoesNotExist()
    {
        // Arrange
        var downloadClientId = Guid.NewGuid();

        _downloadClientRepository
            .Setup(m => m.DeleteAsync(downloadClientId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var response = await _client.DeleteAsync($"/api/download-clients/{downloadClientId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task EnableDownloadClient_EnablesAndReturnsDownloadClient()
    {
        // Arrange
        var downloadClientId = Guid.NewGuid();
        var existingClient = new DownloadClient
        {
            Id = downloadClientId,
            Name = "Test Client",
            ProviderType = "qbittorrent",
            Enabled = false,
            Status = DownloadClientStatus.Unknown,
            Settings = [],
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = null,
            ModifiedAt = null,
            ModifiedByUserId = null
        };

        _downloadClientRepository
            .Setup(m => m.GetByIdAsync(downloadClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingClient);

        _downloadClientRepository
            .Setup(m => m.UpdateAsync(It.IsAny<DownloadClient>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.PostAsync($"/api/download-clients/{downloadClientId}/enable", null)
            .ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<DownloadClient>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(content.Id)).EqualTo(downloadClientId));
            Assert.That(content, Has.Property(nameof(content.Enabled)).True);
        }
        _downloadClientRepository.Verify(
            r => r.UpdateAsync(
                It.Is<DownloadClient>(c => c.Id == downloadClientId && c.Enabled == true),
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task EnableDownloadClient_ReturnsNotFound_WhenDownloadClientDoesNotExist()
    {
        // Arrange
        var downloadClientId = Guid.NewGuid();

        _downloadClientRepository
            .Setup(m => m.GetByIdAsync(downloadClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(DownloadClient?));

        // Act
        var response = await _client.PostAsync($"/api/download-clients/{downloadClientId}/enable", null)
            .ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DisableDownloadClient_DisablesAndReturnsDownloadClient()
    {
        // Arrange
        var downloadClientId = Guid.NewGuid();
        var existingClient = new DownloadClient
        {
            Id = downloadClientId,
            Name = "Test Client",
            ProviderType = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Healthy,
            Settings = [],
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = null,
            ModifiedAt = null,
            ModifiedByUserId = null
        };

        _downloadClientRepository
            .Setup(m => m.GetByIdAsync(downloadClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingClient);

        _downloadClientRepository
            .Setup(m => m.UpdateAsync(It.IsAny<DownloadClient>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.PostAsync($"/api/download-clients/{downloadClientId}/disable", null)
            .ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<DownloadClient>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(content.Id)).EqualTo(downloadClientId));
            Assert.That(content, Has.Property(nameof(content.Enabled)).False);
        }
        _downloadClientRepository.Verify(
            r => r.UpdateAsync(
                It.Is<DownloadClient>(c => c.Id == downloadClientId && c.Enabled == false),
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task DisableDownloadClient_ReturnsNotFound_WhenDownloadClientDoesNotExist()
    {
        // Arrange
        var downloadClientId = Guid.NewGuid();

        _downloadClientRepository
            .Setup(m => m.GetByIdAsync(downloadClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(DownloadClient?));

        // Act
        var response = await _client.PostAsync($"/api/download-clients/{downloadClientId}/disable", null)
            .ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task TestDownloadClient_DispatchesToMatchingIntegration()
    {
        // Arrange
        var downloadClientId = Guid.NewGuid();
        var existing = new DownloadClient
        {
            Id = downloadClientId,
            Name = "qBittorrent",
            ProviderType = "fake",
            Enabled = true,
            Status = DownloadClientStatus.Unknown,
            Settings = new() { ["Host"] = "localhost" }
        };

        _downloadClientRepository
            .Setup(m => m.GetByIdAsync(downloadClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        // Act
        var response = await _client.PostAsync($"/api/download-clients/{downloadClientId}/test", null)
            .ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<ConnectionTestResult>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(content.Success)).True);
            Assert.That(content, Has.Property(nameof(content.Message)).EqualTo("ok"));
            Assert.That(_integration.LastSettings, Is.Not.Null);
            Assert.That(_integration.LastSettings!["Host"], Is.EqualTo("localhost"));
        }
    }

    [Test]
    public async Task TestDownloadClient_ReturnsNotFound_WhenDownloadClientDoesNotExist()
    {
        // Arrange
        var downloadClientId = Guid.NewGuid();

        _downloadClientRepository
            .Setup(m => m.GetByIdAsync(downloadClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(DownloadClient?));

        // Act
        var response = await _client.PostAsync($"/api/download-clients/{downloadClientId}/test", null)
            .ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task TestDownloadClient_ReturnsFailureResult_WhenNoMatchingIntegrationRegistered()
    {
        // Arrange
        var downloadClientId = Guid.NewGuid();
        var existing = new DownloadClient
        {
            Id = downloadClientId,
            Name = "Other",
            ProviderType = "unknown-provider",
            Enabled = true,
            Status = DownloadClientStatus.Unknown,
            Settings = []
        };

        _downloadClientRepository
            .Setup(m => m.GetByIdAsync(downloadClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        // Act
        var response = await _client.PostAsync($"/api/download-clients/{downloadClientId}/test", null)
            .ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<ConnectionTestResult>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(content.Success)).False);
            Assert.That(content!.Message, Does.Contain("unknown-provider"));
        }
    }

    private sealed class FakeDownloadClientIntegration : IDownloadClientIntegration
    {
        public string ProviderType => "fake";

        public IEnumerable<string> SupportedProtocols => ["torrent"];

        public IReadOnlyDictionary<string, string>? LastSettings { get; private set; }

        public Task<DownloadInfo> DownloadReleaseAsync(Release release, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<DownloadInfo> GetDownloadAsync(string externalId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<bool> RemoveDownloadAsync(string downloadId, bool deleteFiles, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<bool> PauseDownloadAsync(
            string externalId,
            IReadOnlyDictionary<string, string> settings,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<bool> ResumeDownloadAsync(
            string externalId,
            IReadOnlyDictionary<string, string> settings,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<ConnectionTestResult> TestConnectionAsync(
            IReadOnlyDictionary<string, string> settings,
            CancellationToken cancellationToken = default)
        {
            LastSettings = settings;
            return Task.FromResult(new ConnectionTestResult { Success = true, Message = "ok" });
        }
    }
}
