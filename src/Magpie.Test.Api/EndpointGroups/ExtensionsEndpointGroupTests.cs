using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Api;
using Magpie.Abstractions.Repositories;
using Magpie.Api.EndpointGroups;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Patch;
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
    private Mock<INuGetService> _nuGetSearchService = null!;

    [SetUp]
    public void SetUp()
    {
        _extensionRepository = new Mock<IExtensionRepository>();
        _nuGetSearchService = new Mock<INuGetService>();

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

        builder.Services.AddSingleton(_extensionRepository.Object);
        builder.Services.AddSingleton(_nuGetSearchService.Object);
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

    [Test]
    public async Task PatchExtension_PatchesNuGetIdField_WhenManaged()
    {
        // Arrange
        var extensionId = Guid.NewGuid();
        var existing = new Extension
        {
            Id = extensionId,
            Name = "Magpie.Extensions.Episodes",
            Version = "1.0.0",
            Enabled = true,
            Managed = true,
            FilePath = "extensions/episodes.dll",
            AssemblyName = "Magpie.Extensions.Episodes"
        };
        var request = new ExtensionPatch { NuGetId = new ScalarPatch<string?> { Value = "Magpie.Episodes" } };

        _extensionRepository
            .Setup(m => m.GetByIdAsync(extensionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        _extensionRepository
            .Setup(m => m.PatchAsync(
                It.Is<ExtensionPatch>(p => p.Id == extensionId && p.NuGetId!.Value == "Magpie.Episodes"),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/extensions/{extensionId}", request).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task PatchExtension_PatchesAutoUpgradeField_WhenManaged()
    {
        // Arrange
        var extensionId = Guid.NewGuid();
        var existing = new Extension
        {
            Id = extensionId,
            Name = "Magpie.Extensions.Episodes",
            Version = "1.0.0",
            Enabled = true,
            Managed = true,
            AutoUpgrade = false,
            FilePath = "extensions/episodes.dll",
            AssemblyName = "Magpie.Extensions.Episodes"
        };
        var request = new ExtensionPatch { AutoUpgrade = new ScalarPatch<bool> { Value = true } };

        _extensionRepository
            .Setup(m => m.GetByIdAsync(extensionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        _extensionRepository
            .Setup(m => m.PatchAsync(
                It.Is<ExtensionPatch>(p => p.Id == extensionId && p.AutoUpgrade!.Value == true),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/extensions/{extensionId}", request).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task PatchExtension_ReturnsNotFound_WhenNotManaged()
    {
        // Arrange
        var extensionId = Guid.NewGuid();
        var existing = new Extension
        {
            Id = extensionId,
            Name = "Magpie.Extensions.Custom",
            Version = "1.0.0",
            Enabled = true,
            Managed = false,
            FilePath = "extensions/custom.dll",
            AssemblyName = "Magpie.Extensions.Custom"
        };
        var request = new ExtensionPatch { NuGetId = new ScalarPatch<string?> { Value = "Magpie.Custom" } };

        _extensionRepository
            .Setup(m => m.GetByIdAsync(extensionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/extensions/{extensionId}", request).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task PatchExtension_ReturnsNotFound_WhenExtensionDoesNotExist()
    {
        // Arrange
        var extensionId = Guid.NewGuid();
        var request = new ExtensionPatch { NuGetId = new ScalarPatch<string?> { Value = "Magpie.Test" } };

        _extensionRepository
            .Setup(m => m.GetByIdAsync(extensionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(Extension?));

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/extensions/{extensionId}", request).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task EnableExtension_EnablesAndReturnsExtension()
    {
        // Arrange
        var extensionId = Guid.NewGuid();
        var existingExtension = new Extension
        {
            Id = extensionId,
            Name = "Magpie.Extensions.Movies",
            Version = "1.0.0",
            Enabled = false,
            DisabledReason = "User disabled",
            FilePath = "extensions/movies.dll",
            AssemblyName = "Magpie.Extensions.Movies",
            CreatedAt = DateTime.UtcNow
        };

        _extensionRepository
            .Setup(m => m.GetByIdAsync(extensionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingExtension);

        _extensionRepository
            .Setup(m => m.UpdateAsync(It.IsAny<Extension>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.PostAsync($"/api/extensions/{extensionId}/enable", null)
            .ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<Extension>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(content.Id)).EqualTo(extensionId));
            Assert.That(content, Has.Property(nameof(content.Enabled)).True);
            Assert.That(content, Has.Property(nameof(content.DisabledReason)).Null);
        }
        _extensionRepository.Verify(
            r => r.UpdateAsync(
                It.Is<Extension>(e => e.Id == extensionId && e.Enabled == true && e.DisabledReason == null),
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task EnableExtension_ReturnsNotFound_WhenExtensionDoesNotExist()
    {
        // Arrange
        var extensionId = Guid.NewGuid();

        _extensionRepository
            .Setup(m => m.GetByIdAsync(extensionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(Extension?));

        // Act
        var response = await _client.PostAsync($"/api/extensions/{extensionId}/enable", null)
            .ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DisableExtension_DisablesAndReturnsExtension()
    {
        // Arrange
        var extensionId = Guid.NewGuid();
        var existingExtension = new Extension
        {
            Id = extensionId,
            Name = "Magpie.Extensions.Movies",
            Version = "1.0.0",
            Enabled = true,
            FilePath = "extensions/movies.dll",
            AssemblyName = "Magpie.Extensions.Movies",
            CreatedAt = DateTime.UtcNow
        };

        _extensionRepository
            .Setup(m => m.GetByIdAsync(extensionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingExtension);

        _extensionRepository
            .Setup(m => m.UpdateAsync(It.IsAny<Extension>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.PostAsync($"/api/extensions/{extensionId}/disable", null)
            .ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<Extension>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(content.Id)).EqualTo(extensionId));
            Assert.That(content, Has.Property(nameof(content.Enabled)).False);
        }
        _extensionRepository.Verify(
            r => r.UpdateAsync(
                It.Is<Extension>(e => e.Id == extensionId && e.Enabled == false),
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task DisableExtension_ReturnsNotFound_WhenExtensionDoesNotExist()
    {
        // Arrange
        var extensionId = Guid.NewGuid();

        _extensionRepository
            .Setup(m => m.GetByIdAsync(extensionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(Extension?));

        // Act
        var response = await _client.PostAsync($"/api/extensions/{extensionId}/disable", null)
            .ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task SearchExtensions_ReturnsEmptyArray_WhenNoResults()
    {
        // Arrange
        _nuGetSearchService
            .Setup(m => m.SearchAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var response = await _client.GetAsync("/api/extensions/search").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<NuGetSearchResult[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Is.Empty);
        }
    }

    [Test]
    public async Task SearchExtensions_ReturnsResults()
    {
        // Arrange
        var searchResult = new NuGetSearchResult
        {
            Id = "Magpie.Extensions.Episodes",
            Version = "1.0.0",
            Title = "Magpie Episodes Extension",
            Description = "Episodes extension for Magpie",
            Authors = ["Magpie Team"],
            IconUrl = null,
            ProjectUrl = "https://github.com/example/magpie-episodes",
            TotalDownloads = 1000,
            Tags = ["magpie-extension", "episodes"]
        };

        _nuGetSearchService
            .Setup(m => m.SearchAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([searchResult]);

        // Act
        var response = await _client.GetAsync("/api/extensions/search").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<NuGetSearchResult[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(NuGetSearchResult.Id))
                .EqualTo("Magpie.Extensions.Episodes"));
            Assert.That(content, Has.One.With.Property(nameof(NuGetSearchResult.Version)).EqualTo("1.0.0"));
        }
    }

    [Test]
    public async Task SearchExtensions_PassesQueryParameter()
    {
        // Arrange
        _nuGetSearchService
            .Setup(m => m.SearchAsync("episodes", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var response = await _client.GetAsync("/api/extensions/search?q=episodes").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
        _nuGetSearchService.Verify(
            m => m.SearchAsync("episodes", It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
