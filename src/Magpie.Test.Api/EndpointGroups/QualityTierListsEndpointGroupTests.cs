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
using Magpie.Models.QualityTiers;
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
public class QualityTierListsEndpointGroupTests
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private Mock<IQualityTierListRepository> _repositoryMock = null!;

    [SetUp]
    public void SetUp()
    {
        _repositoryMock = new Mock<IQualityTierListRepository>();

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
        _repositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<QualityTierList>>([]));

        // Act
        var response = await _client.GetAsync("/api/quality-tier-lists").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<QualityTierList[]>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Is.Empty);
        }
    }

    [Test]
    public async Task GetAllQualityTierLists_ReturnsQualityTierLists()
    {
        // Arrange
        var qualityTierList = CreateQualityTierList("HD Quality");
        _repositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<QualityTierList>>([qualityTierList]));

        // Act
        var response = await _client.GetAsync("/api/quality-tier-lists").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<QualityTierList[]>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
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
        var qualityTierList = CreateQualityTierList("HD Quality");
        _repositoryMock
            .Setup(r => r.GetByIdAsync(qualityTierList.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<QualityTierList?>(qualityTierList));

        // Act
        var response = await _client.GetAsync($"/api/quality-tier-lists/{qualityTierList.Id}").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<QualityTierList>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Has.Property(nameof(QualityTierList.Id)).EqualTo(qualityTierList.Id));
            Assert.That(content, Has.Property(nameof(QualityTierList.Name)).EqualTo("HD Quality"));
            Assert.That(content, Has.Property(nameof(QualityTierList.Tiers)).With.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task GetQualityTierListById_ReturnsNotFound_WhenQualityTierListDoesNotExist()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repositoryMock
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<QualityTierList?>(null));

        // Act
        var response = await _client.GetAsync($"/api/quality-tier-lists/{id}").ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetQualityTierListByName_ReturnsQualityTierList()
    {
        // Arrange
        var qualityTierList = CreateQualityTierList("HD Quality");
        _repositoryMock
            .Setup(r => r.GetByNameAsync("HD Quality", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<QualityTierList?>(qualityTierList));

        // Act
        var response = await _client.GetAsync("/api/quality-tier-lists/by-name/HD%20Quality").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<QualityTierList>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Has.Property(nameof(QualityTierList.Name)).EqualTo("HD Quality"));
        }
    }

    [Test]
    public async Task GetQualityTierListByName_ReturnsNotFound_WhenQualityTierListDoesNotExist()
    {
        // Arrange
        _repositoryMock
            .Setup(r => r.GetByNameAsync("NonExistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<QualityTierList?>(null));

        // Act
        var response = await _client.GetAsync("/api/quality-tier-lists/by-name/NonExistent").ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DeleteQualityTierList_DeletesQualityTierList()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repositoryMock
            .Setup(r => r.DeleteAsync(id, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        // Act
        var response = await _client.DeleteAsync($"/api/quality-tier-lists/{id}").ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        _repositoryMock.Verify(r => r.DeleteAsync(id, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task DeleteQualityTierList_ReturnsNotFound_WhenQualityTierListDoesNotExist()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repositoryMock
            .Setup(r => r.DeleteAsync(id, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(false));

        // Act
        var response = await _client.DeleteAsync($"/api/quality-tier-lists/{id}").ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    private static QualityTierList CreateQualityTierList(string name)
    {
        var tierId = Guid.NewGuid();
        var criterionId = Guid.NewGuid();

        return new(
            Id: Guid.NewGuid(),
            Name: name,
            Tiers:
            [
                new QualityTier(
                    Id: tierId,
                    Ordinal: 0,
                    Name: "Bluray-1080p",
                    IsCutoff: true,
                    Criteria:
                    [
                        new QualityCriterion(
                            Id: criterionId,
                            Field: "resolution",
                            Operator: QualityCriterionOperator.Equals,
                            Value: "1080p")
                    ])
            ],
            UpgradeAllowed: true,
            Tags: [],
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);
    }
}
