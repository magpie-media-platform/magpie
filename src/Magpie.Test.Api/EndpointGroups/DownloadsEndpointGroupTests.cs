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
public class DownloadsEndpointGroupTests
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private Mock<IDownloadRepository> _downloadRepository = null!;

    [SetUp]
    public void SetUp()
    {
        _downloadRepository = new Mock<IDownloadRepository>();

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

        builder.Services.AddSingleton(_downloadRepository.Object);
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
    public async Task QueryDownloads_ReturnsEmptyPagedList_WhenNoDownloads()
    {
        // Arrange
        _downloadRepository
            .Setup(m => m.QueryAsync(
                It.IsAny<DownloadQuery>(),
                It.IsAny<PagingParameter?>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Download>
            {
                Items = [],
                PageNumber = 1,
                PageSize = 20,
                TotalItems = 0
            });

        // Act
        var response = await _client.GetAsync("/api/downloads").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<PagedList<Download>>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Has.Property(nameof(content.Items)).Empty);
        }
    }

    [Test]
    public async Task QueryDownloads_ReturnsDownloads()
    {
        // Arrange
        var downloadClientId = Guid.NewGuid();
        var download = new Download
        {
            Id = Guid.NewGuid(),
            ReleaseId = null,
            DownloadClientId = downloadClientId,
            ExternalId = "torrent-123",
            Title = "Test Download",
            Status = DownloadStatus.Grabbed,
            OutputPath = null,
            StatusReason = null,
            DateCompleted = null,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = null,
            ModifiedAt = null,
            ModifiedByUserId = null
        };

        _downloadRepository
            .Setup(m => m.QueryAsync(
                It.IsAny<DownloadQuery>(),
                It.IsAny<PagingParameter?>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Download>
            {
                Items = [download],
                PageNumber = 1,
                PageSize = 20,
                TotalItems = 1
            });

        // Act
        var response = await _client.GetAsync("/api/downloads").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<PagedList<Download>>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(content.Items)).With.Count.EqualTo(1));
            Assert.That(content, Has.Property(nameof(content.Items))
                .With.One.With.Property(nameof(Download.Title)).EqualTo("Test Download"));
            Assert.That(content, Has.Property(nameof(content.Items))
                .With.One.With.Property(nameof(Download.ExternalId)).EqualTo("torrent-123"));
        }
    }

    [Test]
    public async Task GetDownloadById_ReturnsDownload()
    {
        // Arrange
        var downloadId = Guid.NewGuid();
        var downloadClientId = Guid.NewGuid();
        var download = new Download
        {
            Id = downloadId,
            ReleaseId = null,
            DownloadClientId = downloadClientId,
            ExternalId = "torrent-123",
            Title = "Test Download",
            Status = DownloadStatus.Grabbed,
            OutputPath = null,
            StatusReason = null,
            DateCompleted = null,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = null,
            ModifiedAt = null,
            ModifiedByUserId = null
        };

        _downloadRepository
            .Setup(m => m.GetByIdAsync(downloadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(download);

        // Act
        var response = await _client.GetAsync($"/api/downloads/{downloadId}").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<Download>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(content.Id)).EqualTo(downloadId));
            Assert.That(content, Has.Property(nameof(content.Title)).EqualTo("Test Download"));
        }
    }

    [Test]
    public async Task GetDownloadById_ReturnsNotFound_WhenDownloadDoesNotExist()
    {
        // Arrange
        var downloadId = Guid.NewGuid();
        _downloadRepository
            .Setup(m => m.GetByIdAsync(downloadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(Download?));

        // Act
        var response = await _client.GetAsync($"/api/downloads/{downloadId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task QueryDownloads_FiltersByDownloadClientId()
    {
        // Arrange
        var downloadClientId = Guid.NewGuid();
        var download = new Download
        {
            Id = Guid.NewGuid(),
            ReleaseId = null,
            DownloadClientId = downloadClientId,
            ExternalId = "torrent-123",
            Title = "Download 1",
            Status = DownloadStatus.Grabbed,
            OutputPath = null,
            StatusReason = null,
            DateCompleted = null,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = null,
            ModifiedAt = null,
            ModifiedByUserId = null
        };

        _downloadRepository
            .Setup(m => m.QueryAsync(
                It.Is<DownloadQuery>(q => q.DownloadClientId == downloadClientId),
                It.IsAny<PagingParameter?>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Download>
            {
                Items = [download],
                PageNumber = 1,
                PageSize = 20,
                TotalItems = 1
            });

        // Act
        var response = await _client.GetAsync($"/api/downloads?downloadClientId={downloadClientId}")
            .ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<PagedList<Download>>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(content.Items)).With.Count.EqualTo(1));
            Assert.That(content, Has.Property(nameof(content.Items))
                .With.One.With.Property(nameof(Download.Title)).EqualTo("Download 1"));
        }
    }

    [Test]
    public async Task QueryDownloads_FiltersByStatus()
    {
        // Arrange
        var downloadClientId = Guid.NewGuid();
        var download = new Download
        {
            Id = Guid.NewGuid(),
            ReleaseId = null,
            DownloadClientId = downloadClientId,
            ExternalId = "torrent-2",
            Title = "Downloading",
            Status = DownloadStatus.Downloading,
            OutputPath = null,
            StatusReason = null,
            DateCompleted = null,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = null,
            ModifiedAt = null,
            ModifiedByUserId = null
        };

        _downloadRepository
            .Setup(m => m.QueryAsync(
                It.Is<DownloadQuery>(q => q.Status == DownloadStatus.Downloading),
                It.IsAny<PagingParameter?>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Download>
            {
                Items = [download],
                PageNumber = 1,
                PageSize = 20,
                TotalItems = 1
            });

        // Act
        var response = await _client.GetAsync("/api/downloads?status=Downloading").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<PagedList<Download>>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(content.Items)).With.Count.EqualTo(1));
            Assert.That(content, Has.Property(nameof(content.Items))
                .With.One.With.Property(nameof(Download.Status)).EqualTo(DownloadStatus.Downloading));
        }
    }

    [Test]
    public async Task CreateDownload_CreatesAndReturnsDownload()
    {
        // Arrange
        var downloadClientId = Guid.NewGuid();
        var request = new Download
        {
            Id = Guid.Empty,
            ReleaseId = null,
            DownloadClientId = downloadClientId,
            ExternalId = "torrent-new",
            Title = "New Download",
            Status = default,
            OutputPath = null,
            StatusReason = null,
            DateCompleted = null,
            CreatedAt = default,
            CreatedByUserId = null,
            ModifiedAt = null,
            ModifiedByUserId = null
        };

        _downloadRepository
            .Setup(m => m.CreateAsync(It.IsAny<Download>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Download d, Guid? _, CancellationToken _) => d with { Status = DownloadStatus.Grabbed });

        // Act
        var response = await _client.PostAsJsonAsync("/api/downloads", request).ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<Download>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.Created));
            Assert.That(content, Has.Property(nameof(content.Title)).EqualTo("New Download"));
            Assert.That(content, Has.Property(nameof(content.ExternalId)).EqualTo("torrent-new"));
            Assert.That(content, Has.Property(nameof(content.Status)).EqualTo(DownloadStatus.Grabbed));
        }
    }

    [Test]
    public async Task CreateDownload_WithStatus_CreatesDownload()
    {
        // Arrange
        var downloadClientId = Guid.NewGuid();
        var request = new Download
        {
            Id = Guid.Empty,
            ReleaseId = null,
            DownloadClientId = downloadClientId,
            ExternalId = "torrent-downloading",
            Title = "Downloading Item",
            Status = DownloadStatus.Downloading,
            OutputPath = null,
            StatusReason = null,
            DateCompleted = null,
            CreatedAt = default,
            CreatedByUserId = null,
            ModifiedAt = null,
            ModifiedByUserId = null
        };

        _downloadRepository
            .Setup(m => m.CreateAsync(It.IsAny<Download>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Download d, Guid? _, CancellationToken _) => d);

        // Act
        var response = await _client.PostAsJsonAsync("/api/downloads", request).ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<Download>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.Created));
            Assert.That(content, Has.Property(nameof(content.Status)).EqualTo(DownloadStatus.Downloading));
        }
    }

    [Test]
    public async Task DeleteDownload_DeletesDownload()
    {
        // Arrange
        var downloadId = Guid.NewGuid();

        _downloadRepository
            .Setup(m => m.DeleteAsync(downloadId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.DeleteAsync($"/api/downloads/{downloadId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task DeleteDownload_ReturnsNotFound_WhenDownloadDoesNotExist()
    {
        // Arrange
        var downloadId = Guid.NewGuid();

        _downloadRepository
            .Setup(m => m.DeleteAsync(downloadId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var response = await _client.DeleteAsync($"/api/downloads/{downloadId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }
}
