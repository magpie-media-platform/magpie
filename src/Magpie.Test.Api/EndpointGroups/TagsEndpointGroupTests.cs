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
public class TagsEndpointGroupTests
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private Mock<ITagRepository> _tagRepositoryMock = null!;

    [SetUp]
    public void SetUp()
    {
        _tagRepositoryMock = new Mock<ITagRepository>();

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

        builder.Services.AddSingleton(_tagRepositoryMock.Object);
        builder.Services.AddSingleton<IApiEndpointGroup, TagsEndpointGroup>();

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
    public async Task GetAllTags_ReturnsEmptyArray_WhenNoTags()
    {
        // Arrange
        _tagRepositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<Tag>>([]));

        // Act
        var response = await _client.GetAsync("/api/tags").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Tag[]>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Is.Empty);
        }
    }

    [Test]
    public async Task GetAllTags_ReturnsTags()
    {
        // Arrange
        var tags = new List<Tag>
        {
            new("action"),
            new("comedy"),
            new("drama")
        };

        _tagRepositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<Tag>>(tags));

        // Act
        var response = await _client.GetAsync("/api/tags").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Tag[]>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Has.Length.EqualTo(3));
            Assert.That(content, Has.One.With.Property(nameof(Tag.Name)).EqualTo("action"));
            Assert.That(content, Has.One.With.Property(nameof(Tag.Name)).EqualTo("comedy"));
            Assert.That(content, Has.One.With.Property(nameof(Tag.Name)).EqualTo("drama"));
        }
    }

    [Test]
    public async Task GetTagsByPrefix_ReturnsMatchingTags()
    {
        // Arrange
        var matchingTags = new List<Tag>
        {
            new("genre:action"),
            new("genre:comedy"),
            new("genre:drama")
        };

        _tagRepositoryMock
            .Setup(r => r.GetByPrefixAsync("genre:", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<Tag>>(matchingTags));

        // Act
        var response = await _client.GetAsync("/api/tags/by-prefix/genre:").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Tag[]>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Has.Length.EqualTo(3));
            Assert.That(content, Has.All.With.Property(nameof(Tag.Name)).StartsWith("genre:"));
        }
    }

    [Test]
    public async Task GetTagsByPrefix_ReturnsEmptyArray_WhenNoMatchingTags()
    {
        // Arrange
        _tagRepositoryMock
            .Setup(r => r.GetByPrefixAsync("rating:", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<Tag>>([]));

        // Act
        var response = await _client.GetAsync("/api/tags/by-prefix/rating:").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Tag[]>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Is.Empty);
        }
    }

    [Test]
    public async Task GetTagsByPrefix_ReturnsEmptyArray_WhenNoTags()
    {
        // Arrange
        _tagRepositoryMock
            .Setup(r => r.GetByPrefixAsync("genre:", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<Tag>>([]));

        // Act
        var response = await _client.GetAsync("/api/tags/by-prefix/genre:").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Tag[]>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Is.Empty);
        }
    }
}
