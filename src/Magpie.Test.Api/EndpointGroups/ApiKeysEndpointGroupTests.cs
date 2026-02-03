using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Security;
using Magpie.Api.EndpointGroups;
using Magpie.Models.Api;
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
public class ApiKeysEndpointGroupTests
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private Mock<IApiKeyManager> _apiKeyManager = null!;
    private Guid _userId;

    [SetUp]
    public void SetUp()
    {
        _userId = Guid.NewGuid();
        _apiKeyManager = new Mock<IApiKeyManager>();

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

        builder.Services.AddSingleton(_apiKeyManager.Object);
        builder.Services.AddSingleton<IApiEndpointGroup, ApiKeysEndpointGroup>();

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
    public async Task GetAllApiKeys_ReturnsEmptyArray_WhenNoApiKeys()
    {
        // Arrange
        _apiKeyManager
            .Setup(m => m.ListApiKeysAsync(null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var response = await _client.GetAsync("/api/api-keys").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<ApiKeyResponse[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Is.Empty);
        }
    }

    [Test]
    public async Task GetAllApiKeys_ReturnsApiKeys()
    {
        // Arrange
        var apiKey = new ApiKey
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            Name = "TestKey",
            KeyPrefix = "mgp_test",
            Enabled = true,
            ExpiresAt = null,
            LastUsedAt = null,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = null,
            ModifiedAt = null,
            ModifiedByUserId = null
        };

        _apiKeyManager
            .Setup(m => m.ListApiKeysAsync(null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([apiKey]);

        // Act
        var response = await _client.GetAsync("/api/api-keys").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<ApiKeyResponse[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(ApiKeyResponse.Name)).EqualTo("TestKey"));
            Assert.That(content, Has.One.With.Property(nameof(ApiKeyResponse.KeyPrefix)).EqualTo("mgp_test"));
        }
    }

    [Test]
    public async Task GetAllApiKeys_FiltersByUserId()
    {
        // Arrange
        var apiKey = new ApiKey
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            Name = "TestKey",
            KeyPrefix = "mgp_test",
            Enabled = true,
            ExpiresAt = null,
            LastUsedAt = null,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = null,
            ModifiedAt = null,
            ModifiedByUserId = null
        };

        _apiKeyManager
            .Setup(m => m.ListApiKeysAsync(_userId, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([apiKey]);

        // Act
        var response = await _client.GetAsync($"/api/api-keys?userId={_userId}").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<ApiKeyResponse[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
        }
    }

    [Test]
    public async Task GetApiKeyById_ReturnsApiKey()
    {
        // Arrange
        var apiKeyId = Guid.NewGuid();
        var apiKey = new ApiKey
        {
            Id = apiKeyId,
            UserId = _userId,
            Name = "TestKey",
            KeyPrefix = "mgp_test",
            Enabled = true,
            ExpiresAt = null,
            LastUsedAt = null,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = null,
            ModifiedAt = null,
            ModifiedByUserId = null
        };

        _apiKeyManager
            .Setup(m => m.GetApiKeyAsync(apiKeyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(apiKey);

        // Act
        var response = await _client.GetAsync($"/api/api-keys/{apiKeyId}").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<ApiKeyResponse>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(content.Id)).EqualTo(apiKeyId));
            Assert.That(content, Has.Property(nameof(content.Name)).EqualTo("TestKey"));
            Assert.That(content, Has.Property(nameof(content.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task GetApiKeyById_ReturnsNotFound_WhenApiKeyDoesNotExist()
    {
        // Arrange
        var apiKeyId = Guid.NewGuid();
        _apiKeyManager
            .Setup(m => m.GetApiKeyAsync(apiKeyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(ApiKey?));

        // Act
        var response = await _client.GetAsync($"/api/api-keys/{apiKeyId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task CreateApiKey_CreatesApiKey()
    {
        // Arrange
        var apiKeyId = Guid.NewGuid();
        var apiKey = new ApiKey
        {
            Id = apiKeyId,
            UserId = _userId,
            Name = "NewKey",
            KeyPrefix = "mgp_new",
            Enabled = true,
            ExpiresAt = null,
            LastUsedAt = null,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = null,
            ModifiedAt = null,
            ModifiedByUserId = null
        };
        var apiKeyWithKey = new ApiKeyWithPlaintextKey(apiKey, "mgp_new_fullkeyvalue");

        _apiKeyManager
            .Setup(m => m.CreateApiKeyAsync(_userId, "NewKey", null, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(apiKeyWithKey);

        // Act
        var request = new CreateApiKeyRequest(_userId, "NewKey", null);
        var response = await _client.PostAsJsonAsync("/api/api-keys", request).ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<ApiKeyCreatedResponse>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.Created));
            Assert.That(content, Has.Property(nameof(content.Key)).EqualTo("mgp_new_fullkeyvalue"));
        }
    }

    [Test]
    public async Task RotateApiKey_RotatesApiKey()
    {
        // Arrange
        var apiKeyId = Guid.NewGuid();
        var existingApiKey = new ApiKey
        {
            Id = apiKeyId,
            UserId = _userId,
            Name = "TestKey",
            KeyPrefix = "mgp_test",
            Enabled = true,
            ExpiresAt = null,
            LastUsedAt = null,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = null,
            ModifiedAt = null,
            ModifiedByUserId = null
        };
        var rotatedApiKey = new ApiKey
        {
            Id = apiKeyId,
            UserId = _userId,
            Name = "TestKey",
            KeyPrefix = "mgp_rotated",
            Enabled = true,
            ExpiresAt = null,
            LastUsedAt = null,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = null,
            ModifiedAt = DateTime.UtcNow,
            ModifiedByUserId = null
        };
        var apiKeyWithKey = new ApiKeyWithPlaintextKey(rotatedApiKey, "mgp_rotated_fullkeyvalue");

        _apiKeyManager
            .Setup(m => m.GetApiKeyAsync(apiKeyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingApiKey);

        _apiKeyManager
            .Setup(m => m.RotateApiKeyAsync(apiKeyId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(apiKeyWithKey);

        // Act
        var response = await _client.PostAsync($"/api/api-keys/{apiKeyId}/rotate", null).ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<ApiKeyCreatedResponse>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(content.Key)).EqualTo("mgp_rotated_fullkeyvalue"));
        }
    }

    [Test]
    public async Task RotateApiKey_ReturnsNotFound_WhenApiKeyDoesNotExist()
    {
        // Arrange
        var apiKeyId = Guid.NewGuid();
        _apiKeyManager
            .Setup(m => m.GetApiKeyAsync(apiKeyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(ApiKey?));

        // Act
        var response = await _client.PostAsync($"/api/api-keys/{apiKeyId}/rotate", null).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task EnableApiKey_EnablesApiKey()
    {
        // Arrange
        var apiKeyId = Guid.NewGuid();
        var existingApiKey = new ApiKey
        {
            Id = apiKeyId,
            UserId = _userId,
            Name = "TestKey",
            KeyPrefix = "mgp_test",
            Enabled = false,
            ExpiresAt = null,
            LastUsedAt = null,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = null,
            ModifiedAt = null,
            ModifiedByUserId = null
        };
        var enabledApiKey = new ApiKey
        {
            Id = apiKeyId,
            UserId = _userId,
            Name = "TestKey",
            KeyPrefix = "mgp_test",
            Enabled = true,
            ExpiresAt = null,
            LastUsedAt = null,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = null,
            ModifiedAt = DateTime.UtcNow,
            ModifiedByUserId = null
        };

        _apiKeyManager
            .Setup(m => m.GetApiKeyAsync(apiKeyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingApiKey);

        _apiKeyManager
            .Setup(m => m.EnableApiKeyAsync(apiKeyId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(enabledApiKey);

        // Act
        var response = await _client.PostAsync($"/api/api-keys/{apiKeyId}/enable", null).ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<ApiKeyResponse>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(content.Enabled)).True);
        }
    }

    [Test]
    public async Task EnableApiKey_ReturnsNotFound_WhenApiKeyDoesNotExist()
    {
        // Arrange
        var apiKeyId = Guid.NewGuid();
        _apiKeyManager
            .Setup(m => m.GetApiKeyAsync(apiKeyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(ApiKey?));

        // Act
        var response = await _client.PostAsync($"/api/api-keys/{apiKeyId}/enable", null).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DisableApiKey_DisablesApiKey()
    {
        // Arrange
        var apiKeyId = Guid.NewGuid();
        var existingApiKey = new ApiKey
        {
            Id = apiKeyId,
            UserId = _userId,
            Name = "TestKey",
            KeyPrefix = "mgp_test",
            Enabled = true,
            ExpiresAt = null,
            LastUsedAt = null,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = null,
            ModifiedAt = null,
            ModifiedByUserId = null
        };
        var disabledApiKey = new ApiKey
        {
            Id = apiKeyId,
            UserId = _userId,
            Name = "TestKey",
            KeyPrefix = "mgp_test",
            Enabled = false,
            ExpiresAt = null,
            LastUsedAt = null,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = null,
            ModifiedAt = null,
            ModifiedByUserId = null
        };

        _apiKeyManager
            .Setup(m => m.GetApiKeyAsync(apiKeyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingApiKey);

        _apiKeyManager
            .Setup(m => m.DisableApiKeyAsync(apiKeyId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(disabledApiKey);

        // Act
        var response = await _client.PostAsync($"/api/api-keys/{apiKeyId}/disable", null).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task DisableApiKey_ReturnsNotFound_WhenApiKeyDoesNotExist()
    {
        // Arrange
        var apiKeyId = Guid.NewGuid();
        _apiKeyManager
            .Setup(m => m.GetApiKeyAsync(apiKeyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(ApiKey?));

        // Act
        var response = await _client.PostAsync($"/api/api-keys/{apiKeyId}/disable", null).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DeleteApiKey_DeletesApiKey()
    {
        // Arrange
        var apiKeyId = Guid.NewGuid();

        _apiKeyManager
            .Setup(m => m.DeleteApiKeyAsync(apiKeyId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.DeleteAsync($"/api/api-keys/{apiKeyId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task DeleteApiKey_ReturnsNotFound_WhenApiKeyDoesNotExist()
    {
        // Arrange
        var apiKeyId = Guid.NewGuid();

        _apiKeyManager
            .Setup(m => m.DeleteApiKeyAsync(apiKeyId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var response = await _client.DeleteAsync($"/api/api-keys/{apiKeyId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }
}
