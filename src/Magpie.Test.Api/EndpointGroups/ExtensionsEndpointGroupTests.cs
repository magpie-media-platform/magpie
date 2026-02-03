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
public class ExtensionsEndpointGroupTests
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private Mock<IExtensionRepository> _extensionRepository = null!;

    [SetUp]
    public void SetUp()
    {
        _extensionRepository = new Mock<IExtensionRepository>();

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

        builder.Services.AddSingleton(_extensionRepository.Object);
        builder.Services.AddSingleton<IApiEndpointGroup, ExtensionsEndpointGroup>();

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
    public async Task GetAllExtensions_ReturnsEmptyArray_WhenNoExtensions()
    {
        // Arrange
        _extensionRepository
            .Setup(m => m.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var response = await _client.GetAsync("/api/extensions").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<Extension[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Is.Empty);
        }
    }

    [Test]
    public async Task GetAllExtensions_ReturnsExtensions()
    {
        // Arrange
        var extension = new Extension
        {
            Name = "Magpie.Extensions.Episodes",
            Version = "1.0.0",
            Enabled = true,
            FilePath = "extensions/episodes.dll",
            AssemblyName = "Magpie.Extensions.Episodes"
        };

        _extensionRepository
            .Setup(m => m.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([extension]);

        // Act
        var response = await _client.GetAsync("/api/extensions").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<Extension[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(Extension.Name)).EqualTo("Magpie.Extensions.Episodes"));
            Assert.That(content, Has.One.With.Property(nameof(Extension.Version)).EqualTo("1.0.0"));
            Assert.That(content, Has.One.With.Property(nameof(Extension.Enabled)).True);
        }
    }

    [Test]
    public async Task GetExtensionById_ReturnsExtension()
    {
        // Arrange
        var extensionId = Guid.NewGuid();
        var extension = new Extension
        {
            Id = extensionId,
            Name = "Magpie.Extensions.Movies",
            Version = "1.0.0",
            Enabled = true,
            FilePath = "extensions/movies.dll",
            AssemblyName = "Magpie.Extensions.Movies"
        };

        _extensionRepository
            .Setup(m => m.GetByIdAsync(extensionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(extension);

        // Act
        var response = await _client.GetAsync($"/api/extensions/{extensionId}").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<Extension>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Has.Property(nameof(content.Id)).EqualTo(extensionId));
            Assert.That(content, Has.Property(nameof(content.Name)).EqualTo("Magpie.Extensions.Movies"));
        }
    }

    [Test]
    public async Task GetExtensionById_ReturnsNotFound_WhenExtensionDoesNotExist()
    {
        // Arrange
        var extensionId = Guid.NewGuid();

        _extensionRepository
            .Setup(m => m.GetByIdAsync(extensionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(Extension?));

        // Act
        var response = await _client.GetAsync($"/api/extensions/{extensionId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }
}
