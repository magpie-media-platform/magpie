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
using Magpie.Models.RootFolders;
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
    private Mock<IRootFolderRepository> _rootFolderRepositoryMock = null!;

    [SetUp]
    public void SetUp()
    {
        _rootFolderRepositoryMock = new Mock<IRootFolderRepository>();

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

        builder.Services.AddSingleton(_rootFolderRepositoryMock.Object);
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
        _rootFolderRepositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<RootFolder>>([]));

        // Act
        var response = await _client.GetAsync("/api/root-folders").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<RootFolder[]>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Is.Empty);
        }
    }

    [Test]
    public async Task GetAllRootFolders_ReturnsRootFolders()
    {
        // Arrange
        var contentTypeId = Guid.NewGuid();
        var rootFolder = new RootFolder(
            Id: Guid.NewGuid(),
            Name: "Movies",
            Path: "/media/movies",
            ContentTypeId: contentTypeId,
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _rootFolderRepositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<RootFolder>>([rootFolder]));

        // Act
        var response = await _client.GetAsync("/api/root-folders").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<RootFolder[]>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
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
        var rootFolder = new RootFolder(
            Id: rootFolderId,
            Name: "TV Shows",
            Path: "/media/tv",
            ContentTypeId: contentTypeId,
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _rootFolderRepositoryMock
            .Setup(r => r.GetByIdAsync(rootFolderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<RootFolder?>(rootFolder));

        // Act
        var response = await _client.GetAsync($"/api/root-folders/{rootFolderId}").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<RootFolder>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(RootFolder.Id)).EqualTo(rootFolderId));
            Assert.That(content, Has.Property(nameof(RootFolder.Name)).EqualTo("TV Shows"));
            Assert.That(content, Has.Property(nameof(RootFolder.Path)).EqualTo("/media/tv"));
        }
    }

    [Test]
    public async Task GetRootFolderById_ReturnsNotFound_WhenRootFolderDoesNotExist()
    {
        // Arrange
        var rootFolderId = Guid.NewGuid();
        _rootFolderRepositoryMock
            .Setup(r => r.GetByIdAsync(rootFolderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<RootFolder?>(null));

        // Act
        var response = await _client.GetAsync($"/api/root-folders/{rootFolderId}").ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetRootFoldersByContentType_ReturnsFilteredRootFolders()
    {
        // Arrange
        var contentTypeId = Guid.NewGuid();

        var rootFolder1 = new RootFolder(
            Id: Guid.NewGuid(),
            Name: "Movies 1",
            Path: "/media/movies1",
            ContentTypeId: contentTypeId,
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        var rootFolder2 = new RootFolder(
            Id: Guid.NewGuid(),
            Name: "Movies 2",
            Path: "/media/movies2",
            ContentTypeId: contentTypeId,
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _rootFolderRepositoryMock
            .Setup(r => r.GetByContentTypeIdAsync(contentTypeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<RootFolder>>([rootFolder1, rootFolder2]));

        // Act
        var response = await _client.GetAsync($"/api/root-folders/by-content-type/{contentTypeId}")
            .ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<RootFolder[]>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(2));
            Assert.That(content, Has.All.With.Property(nameof(RootFolder.ContentTypeId)).EqualTo(contentTypeId));
        }
    }

    [Test]
    public async Task CreateRootFolder_CreatesAndReturnsRootFolder()
    {
        // Arrange
        var contentTypeId = Guid.NewGuid();
        var request = new RootFolder(
            Id: Guid.Empty,
            Name: "New Movies",
            Path: "/media/new-movies",
            ContentTypeId: contentTypeId,
            CreatedAt: default,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _rootFolderRepositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<RootFolder>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RootFolder rf, Guid? _, CancellationToken _) => Result.Success(rf));

        // Act
        var response = await _client.PostAsJsonAsync("/api/root-folders", request).ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<RootFolder>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(content, Has.Property(nameof(RootFolder.Name)).EqualTo("New Movies"));
            Assert.That(content, Has.Property(nameof(RootFolder.Path)).EqualTo("/media/new-movies"));
            Assert.That(content, Has.Property(nameof(RootFolder.ContentTypeId)).EqualTo(contentTypeId));
            Assert.That(response.Headers.Location?.ToString(), Does.Contain($"/api/root-folders/{content!.Id}"));
        }
    }

    [Test]
    public async Task PatchRootFolder_PatchesNameField()
    {
        // Arrange
        var rootFolderId = Guid.NewGuid();
        var request = new RootFolderPatch { Name = new FieldPatch<string>("Patched") };

        _rootFolderRepositoryMock
            .Setup(r => r.PatchAsync(
                It.Is<RootFolderPatch>(p => p.Id == rootFolderId && p.Name!.Value == "Patched"),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/root-folders/{rootFolderId}", request)
            .ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task PatchRootFolder_PatchesPathField()
    {
        // Arrange
        var rootFolderId = Guid.NewGuid();
        var request = new RootFolderPatch { Path = new FieldPatch<string>("/media/patched") };

        _rootFolderRepositoryMock
            .Setup(r => r.PatchAsync(
                It.Is<RootFolderPatch>(p => p.Id == rootFolderId && p.Path!.Value == "/media/patched"),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/root-folders/{rootFolderId}", request)
            .ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task PatchRootFolder_PatchesContentTypeIdField()
    {
        // Arrange
        var rootFolderId = Guid.NewGuid();
        var newContentTypeId = Guid.NewGuid();
        var request = new RootFolderPatch { ContentTypeId = new FieldPatch<Guid>(newContentTypeId) };

        _rootFolderRepositoryMock
            .Setup(r => r.PatchAsync(
                It.Is<RootFolderPatch>(p => p.Id == rootFolderId && p.ContentTypeId!.Value == newContentTypeId),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/root-folders/{rootFolderId}", request)
            .ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task PatchRootFolder_ReturnsNotFound_WhenRootFolderDoesNotExist()
    {
        // Arrange
        var rootFolderId = Guid.NewGuid();
        var request = new RootFolderPatch { Name = new FieldPatch<string>("Patched") };

        _rootFolderRepositoryMock
            .Setup(r => r.PatchAsync(It.IsAny<RootFolderPatch>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(false));

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/root-folders/{rootFolderId}", request)
            .ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DeleteRootFolder_DeletesRootFolder()
    {
        // Arrange
        var rootFolderId = Guid.NewGuid();

        _rootFolderRepositoryMock
            .Setup(r => r.DeleteAsync(rootFolderId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        // Act
        var response = await _client.DeleteAsync($"/api/root-folders/{rootFolderId}").ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task DeleteRootFolder_ReturnsNotFound_WhenRootFolderDoesNotExist()
    {
        // Arrange
        var rootFolderId = Guid.NewGuid();

        _rootFolderRepositoryMock
            .Setup(r => r.DeleteAsync(rootFolderId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(false));

        // Act
        var response = await _client.DeleteAsync($"/api/root-folders/{rootFolderId}").ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
