using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Database;
using Magpie.Api.EndpointGroups;
using Magpie.Models.Api;
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

using ProviderBackupInfo = Magpie.Abstractions.Database.IDatabaseManagementProvider.BackupInfo;

namespace Magpie.Test.Api.EndpointGroups;

[TestFixture]
public class BackupsEndpointGroupTests
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private Mock<IDatabaseManagementProvider> _databaseProvider = null!;

    [SetUp]
    public void SetUp()
    {
        _databaseProvider = new Mock<IDatabaseManagementProvider>();

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
            .AddPolicy(nameof(Role.Requester), policy => policy.RequireAssertion(_ => true))
            .AddPolicy(nameof(Role.Manager), policy => policy.RequireAssertion(_ => true))
            .AddPolicy(nameof(Role.Admin), policy => policy.RequireAssertion(_ => true));

        builder.Services.AddSingleton(_databaseProvider.Object);
        builder.Services.AddSingleton<IApiEndpointGroup, BackupsEndpointGroup>();

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
    public async Task ListBackups_ReturnsEmptyList_WhenNoBackups()
    {
        // Arrange
        _databaseProvider
            .Setup(m => m.ListBackupsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var response = await _client.GetAsync("/api/backups").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<BackupInfo[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Is.Empty);
        }
    }

    [Test]
    public async Task ListBackups_ReturnsBackups()
    {
        // Arrange
        var createdAt = DateTime.UtcNow;
        var backupInfo = new ProviderBackupInfo("20240115_103000", "before upgrade", createdAt, 4096);
        _databaseProvider
            .Setup(m => m.ListBackupsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([backupInfo]);

        // Act
        var response = await _client.GetAsync("/api/backups").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<BackupInfo[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content![0], Has.Property(nameof(BackupInfo.Id)).EqualTo("20240115_103000"));
            Assert.That(content[0], Has.Property(nameof(BackupInfo.Name)).EqualTo("before upgrade"));
            Assert.That(content[0], Has.Property(nameof(BackupInfo.SizeBytes)).EqualTo(4096L));
        }
    }

    [Test]
    public async Task CreateBackup_CreatesAndReturnsBackupId()
    {
        // Arrange
        var backupId = "backup-456";
        _databaseProvider
            .Setup(m => m.BackupAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(backupId);

        // Act
        var response = await _client.PostAsJsonAsync("/api/backups", new { }).ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<BackupCreateResponse>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.Created));
            Assert.That(content, Has.Property(nameof(content.Id)).EqualTo(backupId));
        }
    }

    [Test]
    public async Task CreateBackup_WithName_CreatesBackupWithName()
    {
        // Arrange
        var backupName = "my-backup";
        _databaseProvider
            .Setup(m => m.BackupAsync(backupName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(backupName);

        // Act
        var response = await _client.PostAsJsonAsync("/api/backups", new { name = backupName }).ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<BackupCreateResponse>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.Created));
            Assert.That(content, Has.Property(nameof(content.Id)).EqualTo(backupName));
        }
    }

    [Test]
    public async Task DeleteBackup_DeletesBackup()
    {
        // Arrange
        var backupId = "backup-789";
        _databaseProvider
            .Setup(m => m.DeleteBackupAsync(backupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.DeleteAsync($"/api/backups/{backupId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task DeleteBackup_ReturnsNotFound_WhenBackupDoesNotExist()
    {
        // Arrange
        var backupId = "nonexistent";
        _databaseProvider
            .Setup(m => m.DeleteBackupAsync(backupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var response = await _client.DeleteAsync($"/api/backups/{backupId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task RestoreBackup_RestoresBackup()
    {
        // Arrange
        var backupId = "backup-restore";
        _databaseProvider
            .Setup(m => m.RestoreAsync(backupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.PostAsync($"/api/backups/{backupId}/restore", null).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task RestoreBackup_ReturnsNotFound_WhenBackupDoesNotExist()
    {
        // Arrange
        var backupId = "nonexistent";
        _databaseProvider
            .Setup(m => m.RestoreAsync(backupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var response = await _client.PostAsync($"/api/backups/{backupId}/restore", null).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DownloadBackup_ReturnsFile()
    {
        // Arrange
        var backupId = "backup-download";
        var tempFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFile, "test backup content").ConfigureAwait(false);

        _databaseProvider
            .Setup(m => m.GetBackupFilePathAsync(backupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tempFile);

        try
        {
            // Act
            var response = await _client.GetAsync($"/api/backups/{backupId}/download").ConfigureAwait(false);

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
                Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/octet-stream"));
                Assert.That(
                    response.Content.Headers.ContentDisposition?.FileName,
                    Is.EqualTo($"magpie-backup-{backupId}.db"));
                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                Assert.That(content, Is.EqualTo("test backup content"));
            }
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Test]
    public async Task DownloadBackup_ReturnsNotFound_WhenBackupDoesNotExist()
    {
        // Arrange
        var backupId = "nonexistent";
        _databaseProvider
            .Setup(m => m.GetBackupFilePathAsync(backupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        var response = await _client.GetAsync($"/api/backups/{backupId}/download").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }
}
