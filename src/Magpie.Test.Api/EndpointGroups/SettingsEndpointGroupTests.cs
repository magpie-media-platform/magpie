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
public class SettingsEndpointGroupTests
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private Mock<ISettingsRepository> _settingsRepository = null!;

    [SetUp]
    public void SetUp()
    {
        _settingsRepository = new Mock<ISettingsRepository>();

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
            .AddPolicy(nameof(Role.Manager), policy => policy.RequireAssertion(_ => true))
            .AddPolicy(nameof(Role.Admin), policy => policy.RequireAssertion(_ => true));

        builder.Services.AddSingleton(_settingsRepository.Object);
        builder.Services.AddSingleton<IApiEndpointGroup, SettingsEndpointGroup>();

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
    public async Task GetAllSettingsGroups_ReturnsEmptyArray_WhenNoSettingsGroups()
    {
        // Arrange
        _settingsRepository
            .Setup(m => m.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var response = await _client.GetAsync("/api/settings/groups").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<SettingsGroup[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Is.Empty);
        }
    }

    [Test]
    public async Task GetAllSettingsGroups_ReturnsSettingsGroups()
    {
        // Arrange
        var group1 = new SettingsGroup { Name = "general", ExtensionId = null };
        var group2 = new SettingsGroup { Name = "advanced", ExtensionId = Guid.NewGuid() };

        _settingsRepository
            .Setup(m => m.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([group1, group2]);

        // Act
        var response = await _client.GetAsync("/api/settings/groups").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<SettingsGroup[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(2));
            Assert.That(content, Has.One.With.Property(nameof(SettingsGroup.Name)).EqualTo("general"));
            Assert.That(content, Has.One.With.Property(nameof(SettingsGroup.Name)).EqualTo("advanced"));
        }
    }

    [Test]
    public async Task GetSettingsGroupByKey_ReturnsSettingsGroup()
    {
        // Arrange
        var extensionId = Guid.NewGuid();
        var group = new SettingsGroup { Name = "indexer-settings", ExtensionId = extensionId };

        _settingsRepository
            .Setup(m => m.GetByIdAsync(
                It.Is<SettingsGroupLookupKey>(k => k.ExtensionId == extensionId && k.Name == "indexer-settings"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(group);

        // Act
        var response = await _client.GetAsync($"/api/settings/groups/{extensionId}/indexer-settings")
            .ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<SettingsGroup>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(content.Name)).EqualTo("indexer-settings"));
            Assert.That(content, Has.Property(nameof(content.ExtensionId)).EqualTo(extensionId));
        }
    }

    [Test]
    public async Task GetSettingsGroupByKey_ReturnsNotFound_WhenSettingsGroupDoesNotExist()
    {
        // Arrange
        var extensionId = Guid.NewGuid();
        _settingsRepository
            .Setup(m => m.GetByIdAsync(It.IsAny<SettingsGroupLookupKey>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(SettingsGroup?));

        // Act
        var response = await _client.GetAsync($"/api/settings/groups/{extensionId}/nonexistent")
            .ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetSystemSettingsGroupByName_ReturnsSettingsGroup()
    {
        // Arrange
        var group = new SettingsGroup { Name = "system-settings", ExtensionId = null };

        _settingsRepository
            .Setup(m => m.GetByIdAsync(
                It.Is<SettingsGroupLookupKey>(k => k.ExtensionId == null && k.Name == "system-settings"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(group);

        // Act
        var response = await _client.GetAsync("/api/settings/groups/system/system-settings").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<SettingsGroup>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(content.Name)).EqualTo("system-settings"));
            Assert.That(content, Has.Property(nameof(content.ExtensionId)).Null);
        }
    }

    [Test]
    public async Task GetSystemSettingsGroupByName_ReturnsNotFound_WhenSettingsGroupDoesNotExist()
    {
        // Arrange
        _settingsRepository
            .Setup(m => m.GetByIdAsync(It.IsAny<SettingsGroupLookupKey>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(SettingsGroup?));

        // Act
        var response = await _client.GetAsync("/api/settings/groups/system/nonexistent").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task PatchSettingsGroup_PatchesSettings()
    {
        // Arrange
        var extensionId = Guid.NewGuid();
        var patch = new SettingsGroupPatch
        {
            ExtensionId = extensionId,
            Name = "config",
            Settings = new()
            {
                Add = new() { ["key1"] = "value1" }
            }
        };

        _settingsRepository
            .Setup(m => m.PatchAsync(
                It.Is<SettingsGroupPatch>(p => p.ExtensionId == extensionId && p.Name == "config"),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/settings/groups/{extensionId}/config", patch)
            .ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task PatchSettingsGroup_ReturnsNotFound_WhenSettingsGroupDoesNotExist()
    {
        // Arrange
        var extensionId = Guid.NewGuid();
        var patch = new SettingsGroupPatch
        {
            ExtensionId = extensionId,
            Name = "config",
            Settings = new()
            {
                Add = new() { ["key1"] = "value1" }
            }
        };

        _settingsRepository
            .Setup(m => m.PatchAsync(It.IsAny<SettingsGroupPatch>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/settings/groups/{extensionId}/config", patch)
            .ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task PatchSystemSettingsGroup_PatchesSettings()
    {
        // Arrange
        var patch = new SettingsGroupPatch
        {
            ExtensionId = null,
            Name = "system-config",
            Settings = new()
            {
                Add = new() { ["key1"] = "value1" }
            }
        };

        _settingsRepository
            .Setup(m => m.PatchAsync(
                It.Is<SettingsGroupPatch>(p => p.ExtensionId == null && p.Name == "system-config"),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.PatchAsJsonAsync("/api/settings/groups/system/system-config", patch)
            .ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task PatchSystemSettingsGroup_ReturnsNotFound_WhenSettingsGroupDoesNotExist()
    {
        // Arrange
        var patch = new SettingsGroupPatch
        {
            ExtensionId = null,
            Name = "system-config",
            Settings = new()
            {
                Add = new() { ["key1"] = "value1" },
            }
        };

        _settingsRepository
            .Setup(m => m.PatchAsync(It.IsAny<SettingsGroupPatch>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var response = await _client.PatchAsJsonAsync("/api/settings/groups/system/system-config", patch)
            .ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetSystemSettingsGroupByName_ReturnsBackupSettings()
    {
        // Arrange
        var group = new SettingsGroup { Name = "backups", ExtensionId = null };

        _settingsRepository
            .Setup(m => m.GetByIdAsync(
                It.Is<SettingsGroupLookupKey>(k => k.ExtensionId == null && k.Name == "backups"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(group);

        // Act
        var response = await _client.GetAsync("/api/settings/groups/system/backups").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<SettingsGroup>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(content.Name)).EqualTo("backups"));
            Assert.That(content, Has.Property(nameof(content.ExtensionId)).Null);
        }
    }

    [Test]
    public async Task PatchSystemSettingsGroup_EnablesBackupSettings()
    {
        // Arrange
        var patch = new SettingsGroupPatch
        {
            ExtensionId = null,
            Name = "backups",
            Settings = new()
            {
                Add = new()
                {
                    ["enabled"] = "true",
                    ["interval"] = "daily",
                    ["retention"] = "7"
                }
            }
        };

        _settingsRepository
            .Setup(m => m.PatchAsync(
                It.Is<SettingsGroupPatch>(p =>
                    p.ExtensionId == null &&
                    p.Name == "backups" &&
                    p.Settings.Add != null &&
                    p.Settings.Add["enabled"] == "true" &&
                    p.Settings.Add["interval"] == "daily" &&
                    p.Settings.Add["retention"] == "7"),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.PatchAsJsonAsync("/api/settings/groups/system/backups", patch)
            .ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task PatchSystemSettingsGroup_UpdatesBackupInterval()
    {
        // Arrange
        var patch = new SettingsGroupPatch
        {
            ExtensionId = null,
            Name = "backups",
            Settings = new()
            {
                Update = new()
                {
                    ["interval"] = "weekly"
                }
            }
        };

        _settingsRepository
            .Setup(m => m.PatchAsync(
                It.Is<SettingsGroupPatch>(p =>
                    p.ExtensionId == null &&
                    p.Name == "backups" &&
                    p.Settings.Update != null &&
                    p.Settings.Update["interval"] == "weekly"),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.PatchAsJsonAsync("/api/settings/groups/system/backups", patch)
            .ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task PatchSystemSettingsGroup_DisablesBackupSettings()
    {
        // Arrange
        var patch = new SettingsGroupPatch
        {
            ExtensionId = null,
            Name = "backups",
            Settings = new()
            {
                Update = new()
                {
                    ["enabled"] = "false"
                }
            }
        };

        _settingsRepository
            .Setup(m => m.PatchAsync(
                It.Is<SettingsGroupPatch>(p =>
                    p.ExtensionId == null &&
                    p.Name == "backups" &&
                    p.Settings.Update != null &&
                    p.Settings.Update["enabled"] == "false"),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.PatchAsJsonAsync("/api/settings/groups/system/backups", patch)
            .ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task PatchSystemSettingsGroup_UpdatesBackupRetention()
    {
        // Arrange
        var patch = new SettingsGroupPatch
        {
            ExtensionId = null,
            Name = "backups",
            Settings = new()
            {
                Update = new()
                {
                    ["retention"] = "14"
                }
            }
        };

        _settingsRepository
            .Setup(m => m.PatchAsync(
                It.Is<SettingsGroupPatch>(p =>
                    p.ExtensionId == null &&
                    p.Name == "backups" &&
                    p.Settings.Update != null &&
                    p.Settings.Update["retention"] == "14"),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.PatchAsJsonAsync("/api/settings/groups/system/backups", patch)
            .ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
    }
}
