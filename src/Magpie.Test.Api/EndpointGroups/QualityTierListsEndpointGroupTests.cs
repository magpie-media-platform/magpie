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
public class QualityTierListsEndpointGroupTests
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private Mock<IQualityTierListRepository> _repository = null!;

    [SetUp]
    public void SetUp()
    {
        _repository = new Mock<IQualityTierListRepository>();

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
        builder.Services.AddSingleton<IApiEndpointGroup, QualityTierListsEndpointGroup>();

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
    public async Task GetAllQualityTierLists_ReturnsEmptyArray_WhenNoQualityTierLists()
    {
        // Arrange
        _repository
            .Setup(m => m.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var response = await _client.GetAsync("/api/quality-tier-lists").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<QualityTierList[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Is.Empty);
        }
    }

    [Test]
    public async Task GetAllQualityTierLists_ReturnsQualityTierLists()
    {
        // Arrange
        var qualityTierList = new QualityTierList { Name = "HD Quality", UpgradeAllowed = true };
        _repository
            .Setup(m => m.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([qualityTierList]);

        // Act
        var response = await _client.GetAsync("/api/quality-tier-lists").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<QualityTierList[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(QualityTierList.Name)).EqualTo("HD Quality"));
            Assert.That(content, Has.One.With.Property(nameof(QualityTierList.UpgradeAllowed)).True);
        }
    }

    [Test]
    public async Task GetQualityTierListById_ReturnsQualityTierList()
    {
        // Arrange
        var qualityTierList = new QualityTierList { Name = "HD Quality", UpgradeAllowed = true };
        _repository
            .Setup(m => m.GetByIdAsync(qualityTierList.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(qualityTierList);

        // Act
        var response = await _client.GetAsync($"/api/quality-tier-lists/{qualityTierList.Id}").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<QualityTierList>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Has.Property(nameof(content.Id)).EqualTo(qualityTierList.Id));
            Assert.That(content, Has.Property(nameof(content.Name)).EqualTo("HD Quality"));
        }
    }

    [Test]
    public async Task GetQualityTierListById_ReturnsNotFound_WhenQualityTierListDoesNotExist()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository
            .Setup(m => m.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(QualityTierList?));

        // Act
        var response = await _client.GetAsync($"/api/quality-tier-lists/{id}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetQualityTierListsByTag_ReturnsQualityTierLists()
    {
        // Arrange
        var qualityTierList = new QualityTierList { Name = "HD Quality", UpgradeAllowed = true };
        _repository
            .Setup(m => m.ListByTagAsync("hd", It.IsAny<CancellationToken>()))
            .ReturnsAsync([qualityTierList]);

        // Act
        var response = await _client.GetAsync("/api/quality-tier-lists/by-tag/hd").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<QualityTierList[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(QualityTierList.Name)).EqualTo("HD Quality"));
        }
    }

    [Test]
    public async Task CreateQualityTierList_CreatesAndReturnsQualityTierList()
    {
        // Arrange
        var request = new QualityTierList { Name = "New Quality Profile", UpgradeAllowed = true };

        _repository
            .Setup(m => m.CreateAsync(It.IsAny<QualityTierList>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((QualityTierList q, Guid? _, CancellationToken _) => q);

        // Act
        var response = await _client.PostAsJsonAsync("/api/quality-tier-lists", request).ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<QualityTierList>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.Created));
            Assert.That(content, Has.Property(nameof(content.Name)).EqualTo("New Quality Profile"));
        }
    }

    [Test]
    public async Task PatchQualityTierList_PatchesNameField()
    {
        // Arrange
        var id = Guid.NewGuid();
        var request = new QualityTierListPatch { Id = id, Name = new() { Value = "Patched" } };

        _repository
            .Setup(m => m.PatchAsync(
                It.Is<QualityTierListPatch>(p => p.Id == id && p.Name!.Value == "Patched"),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/quality-tier-lists/{id}", request).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task PatchQualityTierList_ReturnsNotFound_WhenQualityTierListDoesNotExist()
    {
        // Arrange
        var id = Guid.NewGuid();
        var request = new QualityTierListPatch { Id = id, Name = new() { Value = "Patched" } };

        _repository
            .Setup(m => m.PatchAsync(It.IsAny<QualityTierListPatch>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/quality-tier-lists/{id}", request).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DeleteQualityTierList_DeletesQualityTierList()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository
            .Setup(m => m.DeleteAsync(id, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.DeleteAsync($"/api/quality-tier-lists/{id}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
        _repository.Verify(m => m.DeleteAsync(id, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task DeleteQualityTierList_ReturnsNotFound_WhenQualityTierListDoesNotExist()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository
            .Setup(m => m.DeleteAsync(id, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var response = await _client.DeleteAsync($"/api/quality-tier-lists/{id}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }
}
