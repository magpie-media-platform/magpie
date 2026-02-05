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
public class RootFoldersEndpointGroupTests
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private Mock<IRootFolderRepository> _rootFolderRepository = null!;

    [SetUp]
    public void SetUp()
    {
        _rootFolderRepository = new Mock<IRootFolderRepository>();

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

        builder.Services.AddSingleton(_rootFolderRepository.Object);
        builder.Services.AddSingleton<IApiEndpointGroup, RootFoldersEndpointGroup>();

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
    public async Task GetAllRootFolders_ReturnsEmptyArray_WhenNoRootFolders()
    {
        // Arrange
        _rootFolderRepository
            .Setup(m => m.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var response = await _client.GetAsync("/api/root-folders").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<RootFolder[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Is.Empty);
        }
    }

    [Test]
    public async Task GetAllRootFolders_ReturnsRootFolders()
    {
        // Arrange
        var contentTypeId = Guid.NewGuid();
        var rootFolder = new RootFolder { Name = "Movies", Path = "/media/movies", ContentTypeId = contentTypeId };

        _rootFolderRepository
            .Setup(m => m.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([rootFolder]);

        // Act
        var response = await _client.GetAsync("/api/root-folders").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<RootFolder[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(RootFolder.Name)).EqualTo("Movies"));
            Assert.That(content, Has.One.With.Property(nameof(RootFolder.Path)).EqualTo("/media/movies"));
            Assert.That(content, Has.One.With.Property(nameof(RootFolder.ContentTypeId)).EqualTo(contentTypeId));
        }
    }

    [Test]
    public async Task GetRootFolderById_ReturnsRootFolder()
    {
        // Arrange
        var rootFolderId = Guid.NewGuid();
        var contentTypeId = Guid.NewGuid();
        var rootFolder = new RootFolder
        {
            Id = rootFolderId,
            Name = "TV Shows",
            Path = "/media/tv",
            ContentTypeId = contentTypeId,
        };

        _rootFolderRepository
            .Setup(m => m.GetByIdAsync(rootFolderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rootFolder);

        // Act
        var response = await _client.GetAsync($"/api/root-folders/{rootFolderId}").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<RootFolder>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(content.Id)).EqualTo(rootFolderId));
            Assert.That(content, Has.Property(nameof(content.Name)).EqualTo("TV Shows"));
            Assert.That(content, Has.Property(nameof(content.Path)).EqualTo("/media/tv"));
        }
    }

    [Test]
    public async Task GetRootFolderById_ReturnsNotFound_WhenRootFolderDoesNotExist()
    {
        // Arrange
        var rootFolderId = Guid.NewGuid();
        _rootFolderRepository
            .Setup(m => m.GetByIdAsync(rootFolderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(RootFolder?));

        // Act
        var response = await _client.GetAsync($"/api/root-folders/{rootFolderId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task CreateRootFolder_CreatesAndReturnsRootFolder()
    {
        // Arrange
        var contentTypeId = Guid.NewGuid();
        var request = new RootFolder { Name = "New Movies", Path = "/media/new-movies", ContentTypeId = contentTypeId };

        _rootFolderRepository
            .Setup(m => m.CreateAsync(It.IsAny<RootFolder>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RootFolder rf, Guid? _, CancellationToken _) => rf);

        // Act
        var response = await _client.PostAsJsonAsync("/api/root-folders", request).ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<RootFolder>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.Created));
            Assert.That(content, Has.Property(nameof(content.Name)).EqualTo("New Movies"));
            Assert.That(content, Has.Property(nameof(content.Path)).EqualTo("/media/new-movies"));
            Assert.That(content, Has.Property(nameof(content.ContentTypeId)).EqualTo(contentTypeId));
        }
    }

    [Test]
    public async Task PatchRootFolder_PatchesRootFolder()
    {
        // Arrange
        var rootFolderId = Guid.NewGuid();
        var contentTypeId = Guid.NewGuid();
        var existingRootFolder = new RootFolder
        {
            Id = rootFolderId,
            Name = "Original Movies",
            Path = "/media/movies",
            ContentTypeId = contentTypeId,
        };
        var request = new RootFolder
        {
            Name = "Updated Movies",
            Path = "/media/updated-movies",
            ContentTypeId = contentTypeId,
        };

        _rootFolderRepository
            .Setup(m => m.GetByIdAsync(rootFolderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingRootFolder);

        _rootFolderRepository
            .Setup(m => m.UpdateAsync(It.IsAny<RootFolder>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/root-folders/{rootFolderId}", request)
            .ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
        _rootFolderRepository.Verify(
            r => r.UpdateAsync(
                It.Is<RootFolder>(rf =>
                    rf.Id == rootFolderId &&
                    rf.Name == "Updated Movies"),
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task PatchRootFolder_ReturnsNotFound_WhenRootFolderDoesNotExist()
    {
        // Arrange
        var rootFolderId = Guid.NewGuid();
        var contentTypeId = Guid.NewGuid();
        var request = new RootFolder
        {
            Name = "Updated Movies",
            Path = "/media/updated-movies",
            ContentTypeId = contentTypeId,
        };

        _rootFolderRepository
            .Setup(m => m.GetByIdAsync(rootFolderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(RootFolder?));

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/root-folders/{rootFolderId}", request)
            .ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DeleteRootFolder_DeletesRootFolder()
    {
        // Arrange
        var rootFolderId = Guid.NewGuid();

        _rootFolderRepository
            .Setup(m => m.DeleteAsync(rootFolderId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.DeleteAsync($"/api/root-folders/{rootFolderId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task DeleteRootFolder_ReturnsNotFound_WhenRootFolderDoesNotExist()
    {
        // Arrange
        var rootFolderId = Guid.NewGuid();

        _rootFolderRepository
            .Setup(m => m.DeleteAsync(rootFolderId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var response = await _client.DeleteAsync($"/api/root-folders/{rootFolderId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }
}
