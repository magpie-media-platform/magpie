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
using Magpie.Models.Extensions;
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
public class ExtensionsEndpointGroupTests
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private Mock<IExtensionRepository> _extensionRepositoryMock = null!;

    [SetUp]
    public void SetUp()
    {
        _extensionRepositoryMock = new Mock<IExtensionRepository>();

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

        builder.Services.AddSingleton(_extensionRepositoryMock.Object);
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
        _extensionRepositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<Extension>>([]));

        // Act
        var response = await _client.GetAsync("/api/extensions").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Extension[]>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Is.Empty);
        }
    }

    [Test]
    public async Task GetAllExtensions_ReturnsExtensions()
    {
        // Arrange
        var extensions = new List<Extension>
        {
            CreateExtension(name: "Magpie.Extensions.Episodes", version: "1.0.0", enabled: true)
        };

        _extensionRepositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<Extension>>(extensions));

        // Act
        var response = await _client.GetAsync("/api/extensions").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Extension[]>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
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
        var extension = CreateExtension(id: extensionId, name: "Magpie.Extensions.Movies");

        _extensionRepositoryMock
            .Setup(r => r.GetByIdAsync(extensionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<Extension?>(extension));

        // Act
        var response = await _client.GetAsync($"/api/extensions/{extensionId}").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Extension>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Has.Property(nameof(Extension.Id)).EqualTo(extensionId));
            Assert.That(content, Has.Property(nameof(Extension.Name)).EqualTo("Magpie.Extensions.Movies"));
        }
    }

    [Test]
    public async Task GetExtensionById_ReturnsNotFound_WhenExtensionDoesNotExist()
    {
        // Arrange
        var extensionId = Guid.NewGuid();

        _extensionRepositoryMock
            .Setup(r => r.GetByIdAsync(extensionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<Extension?>(null));

        // Act
        var response = await _client.GetAsync($"/api/extensions/{extensionId}").ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetExtensionByName_ReturnsExtension()
    {
        // Arrange
        var extension = CreateExtension(name: "Magpie.Extensions.Books");

        _extensionRepositoryMock
            .Setup(r => r.GetByNameAsync("Magpie.Extensions.Books", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<Extension?>(extension));

        // Act
        var response = await _client.GetAsync("/api/extensions/by-name/Magpie.Extensions.Books").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Extension>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Has.Property(nameof(Extension.Name)).EqualTo("Magpie.Extensions.Books"));
        }
    }

    [Test]
    public async Task GetExtensionByName_ReturnsNotFound_WhenExtensionDoesNotExist()
    {
        // Arrange
        _extensionRepositoryMock
            .Setup(r => r.GetByNameAsync("NonExistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<Extension?>(null));

        // Act
        var response = await _client.GetAsync("/api/extensions/by-name/NonExistent").ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetExtensionByAssemblyName_ReturnsExtension()
    {
        // Arrange
        var assemblyName = "Magpie.Extensions.Music, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";
        var extension = CreateExtension(name: "Magpie.Extensions.Music", assemblyName: assemblyName);

        _extensionRepositoryMock
            .Setup(r => r.GetByAssemblyNameAsync(assemblyName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<Extension?>(extension));

        // Act
        var response = await _client.GetAsync($"/api/extensions/by-assembly/{Uri.EscapeDataString(assemblyName)}")
            .ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Extension>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Has.Property(nameof(Extension.AssemblyName)).EqualTo(assemblyName));
        }
    }

    [Test]
    public async Task GetExtensionByAssemblyName_ReturnsNotFound_WhenExtensionDoesNotExist()
    {
        // Arrange
        _extensionRepositoryMock
            .Setup(r => r.GetByAssemblyNameAsync("NonExistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<Extension?>(null));

        // Act
        var response = await _client.GetAsync("/api/extensions/by-assembly/NonExistent").ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetEnabledExtensions_ReturnsOnlyEnabledExtensions()
    {
        // Arrange
        var enabledExtensions = new List<Extension>
        {
            CreateExtension(name: "EnabledExtension", enabled: true)
        };

        _extensionRepositoryMock
            .Setup(r => r.GetEnabledAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<Extension>>(enabledExtensions));

        // Act
        var response = await _client.GetAsync("/api/extensions/enabled").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Extension[]>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(Extension.Name)).EqualTo("EnabledExtension"));
            Assert.That(content, Has.One.With.Property(nameof(Extension.Enabled)).True);
        }
    }

    [Test]
    public async Task GetManagedExtensions_ReturnsOnlyManagedExtensions()
    {
        // Arrange
        var managedExtensions = new List<Extension>
        {
            CreateExtension(name: "ManagedExtension", managed: true, nuGetId: "ManagedExtension")
        };

        _extensionRepositoryMock
            .Setup(r => r.GetManagedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<Extension>>(managedExtensions));

        // Act
        var response = await _client.GetAsync("/api/extensions/managed").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Extension[]>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(Extension.Name)).EqualTo("ManagedExtension"));
            Assert.That(content, Has.One.With.Property(nameof(Extension.Managed)).True);
        }
    }

    private static Extension CreateExtension(
        Guid? id = null,
        string name = "TestExtension",
        string version = "1.0.0",
        string? targetAbi = null,
        string? filePath = null,
        string? assemblyName = null,
        bool enabled = true,
        string? disabledReason = null,
        int loadOrder = 100,
        bool managed = false,
        string? nuGetId = null,
        string? packageRepository = null,
        bool autoUpgrade = false)
    {
        var extensionId = id ?? Guid.NewGuid();
        return new(
            Id: extensionId,
            Version: version,
            Name: name,
            TargetABI: targetAbi,
            FilePath: filePath ?? $"plugins/{name.ToLowerInvariant()}/{name}.dll",
            AssemblyName: assemblyName ?? $"{name}, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
            Enabled: enabled,
            DisabledReason: disabledReason,
            LoadOrder: loadOrder,
            Managed: managed,
            NuGetId: nuGetId,
            PackageRepository: packageRepository,
            AutoUpgrade: autoUpgrade,
            LastChecked: null,
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);
    }
}
