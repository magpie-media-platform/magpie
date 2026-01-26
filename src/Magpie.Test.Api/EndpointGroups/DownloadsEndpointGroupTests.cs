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
using Magpie.Models.Downloads;
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
public class DownloadsEndpointGroupTests
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private Mock<IDownloadRepository> _downloadRepositoryMock = null!;

    [SetUp]
    public void SetUp()
    {
        _downloadRepositoryMock = new Mock<IDownloadRepository>();

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

        builder.Services.AddSingleton(_downloadRepositoryMock.Object);
        builder.Services.AddSingleton<IApiEndpointGroup, DownloadsEndpointGroup>();

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
    public async Task GetAllDownloads_ReturnsEmptyArray_WhenNoDownloads()
    {
        // Arrange
        _downloadRepositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<Download>>([]));

        // Act
        var response = await _client.GetAsync("/api/downloads").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Download[]>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Is.Empty);
        }
    }

    [Test]
    public async Task GetAllDownloads_ReturnsDownloads()
    {
        // Arrange
        var downloadClientId = Guid.NewGuid();
        var download = new Download(
            Id: Guid.NewGuid(),
            ContentItemId: null,
            ReleaseId: null,
            DownloadClientId: downloadClientId,
            ExternalId: "torrent-123",
            Title: "Test Download",
            State: DownloadState.Grabbed,
            OutputPath: null,
            FailureReason: null,
            DateCompleted: null,
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _downloadRepositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<Download>>([download]));

        // Act
        var response = await _client.GetAsync("/api/downloads").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Download[]>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(Download.Title)).EqualTo("Test Download"));
            Assert.That(content, Has.One.With.Property(nameof(Download.ExternalId)).EqualTo("torrent-123"));
        }
    }

    [Test]
    public async Task GetDownloadById_ReturnsDownload()
    {
        // Arrange
        var downloadId = Guid.NewGuid();
        var downloadClientId = Guid.NewGuid();
        var download = new Download(
            Id: downloadId,
            ContentItemId: null,
            ReleaseId: null,
            DownloadClientId: downloadClientId,
            ExternalId: "torrent-123",
            Title: "Test Download",
            State: DownloadState.Grabbed,
            OutputPath: null,
            FailureReason: null,
            DateCompleted: null,
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _downloadRepositoryMock
            .Setup(r => r.GetByIdAsync(downloadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<Download?>(download));

        // Act
        var response = await _client.GetAsync($"/api/downloads/{downloadId}").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Download>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(Download.Id)).EqualTo(downloadId));
            Assert.That(content, Has.Property(nameof(Download.Title)).EqualTo("Test Download"));
        }
    }

    [Test]
    public async Task GetDownloadById_ReturnsNotFound_WhenDownloadDoesNotExist()
    {
        // Arrange
        var downloadId = Guid.NewGuid();
        _downloadRepositoryMock
            .Setup(r => r.GetByIdAsync(downloadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<Download?>(null));

        // Act
        var response = await _client.GetAsync($"/api/downloads/{downloadId}").ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetDownloadsByDownloadClient_ReturnsFilteredDownloads()
    {
        // Arrange
        var downloadClientId = Guid.NewGuid();
        var download = new Download(
            Id: Guid.NewGuid(),
            ContentItemId: null,
            ReleaseId: null,
            DownloadClientId: downloadClientId,
            ExternalId: "torrent-123",
            Title: "Download 1",
            State: DownloadState.Grabbed,
            OutputPath: null,
            FailureReason: null,
            DateCompleted: null,
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _downloadRepositoryMock
            .Setup(r => r.GetByDownloadClientIdAsync(downloadClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<Download>>([download]));

        // Act
        var response = await _client.GetAsync($"/api/downloads/by-download-client/{downloadClientId}")
            .ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Download[]>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(Download.Title)).EqualTo("Download 1"));
        }
    }

    [Test]
    public async Task GetDownloadByExternalId_ReturnsDownload()
    {
        // Arrange
        var downloadClientId = Guid.NewGuid();
        var download = new Download(
            Id: Guid.NewGuid(),
            ContentItemId: null,
            ReleaseId: null,
            DownloadClientId: downloadClientId,
            ExternalId: "torrent-abc123",
            Title: "Test Download",
            State: DownloadState.Grabbed,
            OutputPath: null,
            FailureReason: null,
            DateCompleted: null,
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _downloadRepositoryMock
            .Setup(r => r.GetByExternalIdAsync(downloadClientId, "torrent-abc123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<Download?>(download));

        // Act
        var response = await _client
            .GetAsync($"/api/downloads/by-external-id/{downloadClientId}/torrent-abc123")
            .ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Download>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(Download.ExternalId)).EqualTo("torrent-abc123"));
        }
    }

    [Test]
    public async Task GetDownloadByExternalId_ReturnsNotFound_WhenNotExists()
    {
        // Arrange
        var downloadClientId = Guid.NewGuid();
        _downloadRepositoryMock
            .Setup(r => r.GetByExternalIdAsync(downloadClientId, "nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<Download?>(null));

        // Act
        var response = await _client
            .GetAsync($"/api/downloads/by-external-id/{downloadClientId}/nonexistent")
            .ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetDownloadsByState_ReturnsFilteredDownloads()
    {
        // Arrange
        var downloadClientId = Guid.NewGuid();
        var download = new Download(
            Id: Guid.NewGuid(),
            ContentItemId: null,
            ReleaseId: null,
            DownloadClientId: downloadClientId,
            ExternalId: "torrent-2",
            Title: "Downloading",
            State: DownloadState.Downloading,
            OutputPath: null,
            FailureReason: null,
            DateCompleted: null,
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _downloadRepositoryMock
            .Setup(r => r.GetByStateAsync(DownloadState.Downloading, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<Download>>([download]));

        // Act
        var response = await _client.GetAsync("/api/downloads/by-state/Downloading").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Download[]>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(Download.State)).EqualTo(DownloadState.Downloading));
        }
    }

    [Test]
    public async Task CreateDownload_CreatesAndReturnsDownload()
    {
        // Arrange
        var downloadClientId = Guid.NewGuid();
        var request = new Download(
            Id: Guid.Empty,
            ContentItemId: null,
            ReleaseId: null,
            DownloadClientId: downloadClientId,
            ExternalId: "torrent-new",
            Title: "New Download",
            State: default,
            OutputPath: null,
            FailureReason: null,
            DateCompleted: null,
            CreatedAt: default,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _downloadRepositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<Download>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Download d, Guid? _, CancellationToken _) => Result.Success(d));

        // Act
        var response = await _client.PostAsJsonAsync("/api/downloads", request).ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Download>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(content, Has.Property(nameof(Download.Title)).EqualTo("New Download"));
            Assert.That(content, Has.Property(nameof(Download.ExternalId)).EqualTo("torrent-new"));
            Assert.That(content, Has.Property(nameof(Download.State)).EqualTo(DownloadState.Grabbed));
            Assert.That(response.Headers.Location?.ToString(), Does.Contain($"/api/downloads/{content!.Id}"));
        }
    }

    [Test]
    public async Task CreateDownload_WithState_CreatesDownload()
    {
        // Arrange
        var downloadClientId = Guid.NewGuid();
        var request = new Download(
            Id: Guid.Empty,
            ContentItemId: null,
            ReleaseId: null,
            DownloadClientId: downloadClientId,
            ExternalId: "torrent-downloading",
            Title: "Downloading Item",
            State: DownloadState.Downloading,
            OutputPath: null,
            FailureReason: null,
            DateCompleted: null,
            CreatedAt: default,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _downloadRepositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<Download>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Download d, Guid? _, CancellationToken _) => Result.Success(d));

        // Act
        var response = await _client.PostAsJsonAsync("/api/downloads", request).ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Download>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(content, Has.Property(nameof(Download.State)).EqualTo(DownloadState.Downloading));
        }
    }

    [Test]
    public async Task UpdateDownloadState_UpdatesState()
    {
        // Arrange
        var downloadId = Guid.NewGuid();
        var request = new DownloadStatePatch(
            State: DownloadState.Downloading,
            OutputPath: null,
            FailureReason: null,
            DateCompleted: null);

        _downloadRepositoryMock
            .Setup(r => r.UpdateStateAsync(
                downloadId,
                DownloadState.Downloading,
                null,
                null,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/downloads/{downloadId}/state", request)
            .ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task UpdateDownloadState_ToCompleted_SetsOutputPathAndDate()
    {
        // Arrange
        var downloadId = Guid.NewGuid();
        var completedDate = DateTimeOffset.UtcNow;
        var request = new DownloadStatePatch(
            State: DownloadState.Downloaded,
            OutputPath: "/downloads/complete/test.mkv",
            FailureReason: null,
            DateCompleted: completedDate);

        _downloadRepositoryMock
            .Setup(r => r.UpdateStateAsync(
                downloadId,
                DownloadState.Downloaded,
                "/downloads/complete/test.mkv",
                null,
                completedDate,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/downloads/{downloadId}/state", request)
            .ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        _downloadRepositoryMock.Verify(
            r => r.UpdateStateAsync(
                downloadId,
                DownloadState.Downloaded,
                "/downloads/complete/test.mkv",
                null,
                completedDate,
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task UpdateDownloadState_ToFailed_SetsFailureReason()
    {
        // Arrange
        var downloadId = Guid.NewGuid();
        var request = new DownloadStatePatch(
            State: DownloadState.Failed,
            OutputPath: null,
            FailureReason: "Tracker returned error",
            DateCompleted: null);

        _downloadRepositoryMock
            .Setup(r => r.UpdateStateAsync(
                downloadId,
                DownloadState.Failed,
                null,
                "Tracker returned error",
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/downloads/{downloadId}/state", request)
            .ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        _downloadRepositoryMock.Verify(
            r => r.UpdateStateAsync(
                downloadId,
                DownloadState.Failed,
                null,
                "Tracker returned error",
                null,
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task UpdateDownloadState_ReturnsNotFound_WhenDownloadDoesNotExist()
    {
        // Arrange
        var downloadId = Guid.NewGuid();
        var request = new DownloadStatePatch(
            State: DownloadState.Downloading,
            OutputPath: null,
            FailureReason: null,
            DateCompleted: null);

        _downloadRepositoryMock
            .Setup(r => r.UpdateStateAsync(
                downloadId,
                It.IsAny<DownloadState>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<DateTimeOffset?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(false));

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/downloads/{downloadId}/state", request)
            .ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DeleteDownload_DeletesDownload()
    {
        // Arrange
        var downloadId = Guid.NewGuid();

        _downloadRepositoryMock
            .Setup(r => r.DeleteAsync(downloadId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        // Act
        var response = await _client.DeleteAsync($"/api/downloads/{downloadId}").ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task DeleteDownload_ReturnsNotFound_WhenDownloadDoesNotExist()
    {
        // Arrange
        var downloadId = Guid.NewGuid();

        _downloadRepositoryMock
            .Setup(r => r.DeleteAsync(downloadId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(false));

        // Act
        var response = await _client.DeleteAsync($"/api/downloads/{downloadId}").ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DeleteDownloadsByDownloadClient_DeletesAllDownloads()
    {
        // Arrange
        var downloadClientId = Guid.NewGuid();

        _downloadRepositoryMock
            .Setup(r => r.DeleteByDownloadClientIdAsync(downloadClientId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(2));

        // Act
        var response = await _client.DeleteAsync($"/api/downloads/by-download-client/{downloadClientId}")
            .ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<int>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.EqualTo(2));
        }
    }

    [Test]
    public async Task GetDownloadsByContentItem_ReturnsFilteredDownloads()
    {
        // Arrange
        var contentItemId = Guid.NewGuid();
        var downloadClientId = Guid.NewGuid();
        var download = new Download(
            Id: Guid.NewGuid(),
            ContentItemId: contentItemId,
            ReleaseId: null,
            DownloadClientId: downloadClientId,
            ExternalId: "torrent-123",
            Title: "Content Download",
            State: DownloadState.Grabbed,
            OutputPath: null,
            FailureReason: null,
            DateCompleted: null,
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _downloadRepositoryMock
            .Setup(r => r.GetByContentItemIdAsync(contentItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<Download>>([download]));

        // Act
        var response = await _client.GetAsync($"/api/downloads/by-content-item/{contentItemId}")
            .ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Download[]>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(Download.ContentItemId)).EqualTo(contentItemId));
        }
    }
}
