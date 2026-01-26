using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Data;
using Magpie.Api.EndpointGroups;
using Magpie.Models.Configuration;
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

using static Magpie.Abstractions.Data.ISettingsRepository;

namespace Magpie.Test.Api.EndpointGroups;

[TestFixture]
public class SettingsEndpointGroupTests
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private Mock<ISettingsRepository> _settingsRepositoryMock = null!;
    private readonly Guid _extensionId = Guid.NewGuid();

    [SetUp]
    public void SetUp()
    {
        _settingsRepositoryMock = new Mock<ISettingsRepository>();

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

        builder.Services.AddSingleton(_settingsRepositoryMock.Object);
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
    public async Task GetAllSettingsGroups_ReturnsEmptyArray_WhenNoGroups()
    {
        // Arrange
        _settingsRepositoryMock
            .Setup(r => r.GetAllSettingsGroupsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<SettingsGroup>>([]));

        // Act
        var response = await _client.GetAsync("/api/settings/groups").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<SettingsGroup[]>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Is.Empty);
        }
    }

    [Test]
    public async Task GetAllSettingsGroups_ReturnsGroups()
    {
        // Arrange
        var group = new SettingsGroup(Guid.NewGuid(), "TestGroup", _extensionId);

        _settingsRepositoryMock
            .Setup(r => r.GetAllSettingsGroupsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<SettingsGroup>>([group]));

        // Act
        var response = await _client.GetAsync("/api/settings/groups").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<SettingsGroup[]>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(SettingsGroup.Name)).EqualTo("TestGroup"));
            Assert.That(content, Has.One.With.Property(nameof(SettingsGroup.ExtensionId)).EqualTo(_extensionId));
        }
    }

    [Test]
    public async Task GetSettingsGroupByKey_ReturnsGroup()
    {
        // Arrange
        var group = new SettingsGroup(Guid.NewGuid(), "TestGroup", _extensionId);

        _settingsRepositoryMock
            .Setup(r => r.GetSettingsGroupAsync(_extensionId, "TestGroup", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<SettingsGroup?>(group));

        // Act
        var response = await _client.GetAsync($"/api/settings/groups/{_extensionId}/TestGroup").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<SettingsGroup>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(SettingsGroup.Name)).EqualTo("TestGroup"));
            Assert.That(content, Has.Property(nameof(SettingsGroup.ExtensionId)).EqualTo(_extensionId));
        }
    }

    [Test]
    public async Task GetSettingsGroupByKey_ReturnsNotFound_WhenGroupDoesNotExist()
    {
        // Arrange
        _settingsRepositoryMock
            .Setup(r => r.GetSettingsGroupAsync(_extensionId, "NonExistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<SettingsGroup?>(null));

        // Act
        var response = await _client.GetAsync($"/api/settings/groups/{_extensionId}/NonExistent").ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetSystemSettingsGroupByName_ReturnsGroup()
    {
        // Arrange
        var group = new SettingsGroup(Guid.NewGuid(), "SystemGroup", null);

        _settingsRepositoryMock
            .Setup(r => r.GetSettingsGroupAsync(null, "SystemGroup", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<SettingsGroup?>(group));

        // Act
        var response = await _client.GetAsync("/api/settings/groups/system/SystemGroup").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<SettingsGroup>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(SettingsGroup.Name)).EqualTo("SystemGroup"));
            Assert.That(content, Has.Property(nameof(SettingsGroup.ExtensionId)).Null);
        }
    }

    [Test]
    public async Task CreateSettingsGroup_CreatesAndReturnsGroup()
    {
        // Arrange
        var request = new SettingsGroup(Guid.Empty, "NewGroup", _extensionId);

        _settingsRepositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<SettingsGroup>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SettingsGroup g, Guid? _, CancellationToken _) => Result.Success(g));

        // Act
        var response = await _client.PostAsJsonAsync("/api/settings/groups", request).ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<SettingsGroup>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(content, Has.Property(nameof(SettingsGroup.Name)).EqualTo("NewGroup"));
            Assert.That(content, Has.Property(nameof(SettingsGroup.ExtensionId)).EqualTo(_extensionId));
            Assert.That(response.Headers.Location?.ToString(),
                Does.Contain($"/api/settings/groups/{_extensionId}/NewGroup"));
        }
    }

    [Test]
    public async Task CreateSettingsGroup_CreatesSystemGroup_WhenExtensionIdIsNull()
    {
        // Arrange
        var request = new SettingsGroup(Guid.Empty, "SystemGroup", null);

        _settingsRepositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<SettingsGroup>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SettingsGroup g, Guid? _, CancellationToken _) => Result.Success(g));

        // Act
        var response = await _client.PostAsJsonAsync("/api/settings/groups", request).ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<SettingsGroup>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(content, Has.Property(nameof(SettingsGroup.Name)).EqualTo("SystemGroup"));
            Assert.That(content, Has.Property(nameof(SettingsGroup.ExtensionId)).Null);
            Assert.That(response.Headers.Location?.ToString(), Does.Contain("/api/settings/groups/system/SystemGroup"));
        }
    }

    [Test]
    public async Task DeleteSettingsGroup_DeletesGroup()
    {
        // Arrange
        _settingsRepositoryMock
            .Setup(r => r.DeleteAsync(
                It.Is<SettingsGroupKey>(k => k.ExtensionId == _extensionId && k.Name == "ToDelete"),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        // Act
        var response = await _client.DeleteAsync($"/api/settings/groups/{_extensionId}/ToDelete").ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task DeleteSettingsGroup_ReturnsNotFound_WhenGroupDoesNotExist()
    {
        // Arrange
        _settingsRepositoryMock
            .Setup(r => r.DeleteAsync(It.IsAny<SettingsGroupKey>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(false));

        // Act
        var response = await _client.DeleteAsync($"/api/settings/groups/{_extensionId}/NonExistent")
            .ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DeleteSystemSettingsGroup_DeletesGroup()
    {
        // Arrange
        _settingsRepositoryMock
            .Setup(r => r.DeleteAsync(
                It.Is<SettingsGroupKey>(k => k.ExtensionId == null && k.Name == "SystemToDelete"),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        // Act
        var response = await _client.DeleteAsync("/api/settings/groups/system/SystemToDelete").ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task GetSettings_ReturnsSettings()
    {
        // Arrange
        var groupId = Guid.NewGuid();
        var setting = new Setting(groupId, "TestKey", "TestValue");

        _settingsRepositoryMock
            .Setup(r => r.GetSettingsAsync(_extensionId, "TestGroup", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<Setting>>([setting]));

        // Act
        var response = await _client.GetAsync($"/api/settings/{_extensionId}/TestGroup").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Setting[]>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(Setting.Key)).EqualTo("TestKey"));
            Assert.That(content, Has.One.With.Property(nameof(Setting.Value)).EqualTo("TestValue"));
        }
    }

    [Test]
    public async Task GetSystemSettings_ReturnsSettings()
    {
        // Arrange
        var groupId = Guid.NewGuid();
        var setting = new Setting(groupId, "SystemKey", "SystemValue");

        _settingsRepositoryMock
            .Setup(r => r.GetSettingsAsync(null, "SystemGroup", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<Setting>>([setting]));

        // Act
        var response = await _client.GetAsync("/api/settings/system/SystemGroup").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Setting[]>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(Setting.Key)).EqualTo("SystemKey"));
        }
    }

    [Test]
    public async Task GetSetting_ReturnsSetting()
    {
        // Arrange
        var groupId = Guid.NewGuid();
        var setting = new Setting(groupId, "TestKey", "TestValue");

        _settingsRepositoryMock
            .Setup(r => r.GetSettingAsync(_extensionId, "TestGroup", "TestKey", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<Setting?>(setting));

        // Act
        var response = await _client.GetAsync($"/api/settings/{_extensionId}/TestGroup/TestKey").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Setting>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(Setting.Key)).EqualTo("TestKey"));
            Assert.That(content, Has.Property(nameof(Setting.Value)).EqualTo("TestValue"));
        }
    }

    [Test]
    public async Task GetSetting_ReturnsNotFound_WhenSettingDoesNotExist()
    {
        // Arrange
        _settingsRepositoryMock
            .Setup(r => r.GetSettingAsync(_extensionId, "TestGroup", "NonExistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<Setting?>(null));

        // Act
        var response = await _client.GetAsync($"/api/settings/{_extensionId}/TestGroup/NonExistent")
            .ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DeleteSetting_DeletesSetting()
    {
        // Arrange
        _settingsRepositoryMock
            .Setup(r => r.DeleteAsync(
                It.Is<SettingKey>(k =>
                    k.ExtensionId == _extensionId &&
                    k.GroupName == "TestGroup" &&
                    k.Key == "ToDelete"),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        // Act
        var response = await _client.DeleteAsync($"/api/settings/{_extensionId}/TestGroup/ToDelete")
            .ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task DeleteSetting_ReturnsNotFound_WhenSettingDoesNotExist()
    {
        // Arrange
        _settingsRepositoryMock
            .Setup(r => r.DeleteAsync(It.IsAny<SettingKey>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(false));

        // Act
        var response = await _client.DeleteAsync($"/api/settings/{_extensionId}/TestGroup/NonExistent")
            .ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetSystemSetting_ReturnsSetting()
    {
        // Arrange
        var groupId = Guid.NewGuid();
        var setting = new Setting(groupId, "SystemKey", "SystemValue");

        _settingsRepositoryMock
            .Setup(r => r.GetSettingsAsync(null, "SystemGroup", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<Setting>>([setting]));

        // Act
        var response = await _client.GetAsync("/api/settings/system/SystemGroup/SystemKey").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Setting>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(Setting.Key)).EqualTo("SystemKey"));
            Assert.That(content, Has.Property(nameof(Setting.Value)).EqualTo("SystemValue"));
        }
    }

    [Test]
    public async Task GetSystemSetting_ReturnsNotFound_WhenSettingDoesNotExist()
    {
        // Arrange
        _settingsRepositoryMock
            .Setup(r => r.GetSettingsAsync(null, "SystemGroup", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<Setting>>([]));

        // Act
        var response = await _client.GetAsync("/api/settings/system/SystemGroup/NonExistent").ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DeleteSystemSetting_DeletesSetting()
    {
        // Arrange
        _settingsRepositoryMock
            .Setup(r => r.PatchAsync(
                It.Is<SettingsGroupPatch>(p =>
                    p.ExtensionId == null &&
                    p.Name == "SystemGroup" &&
                    p.Settings.Remove != null &&
                    p.Settings.Remove.Contains("SystemKey")),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        // Act
        var response = await _client.DeleteAsync("/api/settings/system/SystemGroup/SystemKey").ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }
}
