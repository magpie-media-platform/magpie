using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Core.Config;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Sdk.Api;
using Magpie.Sdk.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;
using Moq.Protected;

using NUnit.Framework;

namespace Magpie.Test.Core.Host;

[TestFixture]
public sealed class ApiHostTests
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private Mock<ISettingsService> _settings = null!;
    private HostStatusInfo _status = null!;
    private TaskCompletionSource _work = null!;
    private TaskCompletionSource _stopping = null!;
    private BackgroundService _service = null!;

    [SetUp]
    public async Task SetUp()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        _status = new HostStatusInfo { Mode = HostMode.Sentinel };
        _settings = new Mock<ISettingsService>();
        _settings.Setup(service => service.GetSystemGroupAsync("setup", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SettingsGroupDetail
            {
                Group = new SettingsGroup
                {
                    Name = "setup",
                    Settings = { new Setting { Key = "complete", Value = "true" } }
                }
            });
        _work = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _stopping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new Mock<BackgroundService> { CallBase = true };
        service.Protected().Setup<Task>("ExecuteAsync", ItExpr.IsAny<CancellationToken>()).Returns(_work.Task);
        _service = service.Object;
        var group = new Mock<IApiEndpointGroup>();
        group.SetupGet(value => value.Name).Returns("test");
        group.Setup(value => value.MapEndpoints(It.IsAny<IEndpointRouteBuilder>()))
            .Callback<IEndpointRouteBuilder>(routes => routes.MapPost("", () => Results.NoContent()));
        var migration = new DatabaseMigrationState();
        migration.Complete();
        builder.Services.AddSingleton(new MagpieHostOptions { AllowedOrigins = ["http://localhost:5173"] });
        builder.Services.AddCors();
        builder.Services.AddSingleton(_status);
        builder.Services.AddSingleton(_settings.Object);
        builder.Services.AddSingleton(migration);
        builder.Services.AddSingleton<IHostedService>(_service);
        builder.Services.AddSingleton(group.Object);
        builder.Services.AddAuthentication();
        builder.Services.AddAuthorization();
        builder.Services.AddProblemDetails();
        _app = builder.Build();
        _app.UseMagpieRequestPipeline();
        _app.Lifetime.ApplicationStopping.Register(() => _stopping.TrySetResult());
        await _app.StartAsync().ConfigureAwait(false);
        _client = _app.GetTestClient();
    }

    [TearDown]
    public async Task TearDown()
    {
        _work.TrySetResult();
        _client.Dispose();
        await _app.DisposeAsync().ConfigureAwait(false);
    }

    [TestCase("http://localhost:5173", "http://localhost:5173")]
    [TestCase("https://untrusted.example", null)]
    [TestCase("null", null)]
    public async Task UseMagpieRequestPipeline_PreflightsOnlyConfiguredOrigins_WhenABrowserAsksForApiAccess(
        string origin, string? allowedOrigin)
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/test");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type,authorization");

        // Act
        using var response = await _client.SendAsync(request).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                response.Headers.TryGetValues("Access-Control-Allow-Origin", out var origins)
                    ? string.Join(",", origins)
                    : null,
                Is.EqualTo(allowedOrigin));
            Assert.That(
                response.Headers.TryGetValues("Access-Control-Allow-Credentials", out var credentials)
                    ? string.Join(",", credentials)
                    : null,
                Is.EqualTo(allowedOrigin is null ? null : "true"));
        }
    }

    [TestCase(null, null)]
    [TestCase("http://localhost", null)]
    [TestCase("https://untrusted.example", null)]
    [TestCase("http://localhost:5173", "http://localhost:5173")]
    public async Task UseMagpieRequestPipeline_AcceptsPost_WhenTheUiOrCliCallsTheHost(
        string? origin, string? allowedOrigin)
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/test");
        request.Headers.TryAddWithoutValidation("Origin", origin);

        // Act
        using var response = await _client.SendAsync(request).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((int)response.StatusCode, Is.EqualTo(204));
            Assert.That(
                response.Headers.TryGetValues("Access-Control-Allow-Origin", out var origins)
                    ? string.Join(",", origins)
                    : null,
                Is.EqualTo(allowedOrigin));
        }
    }

    [Test]
    public async Task UseMagpieRequestPipeline_ReportsFailure_WhenABackgroundServiceFaults()
    {
        // Arrange
        _status.ShutdownExitCode = null;

        // Act
        _work.SetException(new InvalidOperationException("The worker failed."));
        await _stopping.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        await _app.StopAsync().ConfigureAwait(false);

        // Assert
        Assert.That(_status.ShutdownExitCode, Is.EqualTo(1));
    }

    [Test]
    public async Task UseMagpieRequestPipeline_ReportsFailure_WhenABackgroundServiceCancelsUnexpectedly()
    {
        // Arrange
        _status.ShutdownExitCode = null;

        // Act
        _work.SetCanceled();
        await _stopping.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        await _app.StopAsync().ConfigureAwait(false);

        // Assert
        Assert.That(_status.ShutdownExitCode, Is.EqualTo(1));
    }

    [TestCase(0)]
    [TestCase(8)]
    public async Task UseMagpieRequestPipeline_PreservesExitStatus_WhenAFaultedServiceStopsARequestedShutdown(
        int exitCode)
    {
        // Arrange
        _status.ShutdownExitCode = exitCode;

        // Act
        _work.SetException(new InvalidOperationException("The worker failed."));
        await _stopping.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        await _app.StopAsync().ConfigureAwait(false);

        // Assert
        Assert.That(_status.ShutdownExitCode, Is.EqualTo(exitCode));
    }

    [TestCase(0)]
    [TestCase(8)]
    public async Task UseMagpieRequestPipeline_PreservesExitStatus_WhenACanceledServiceStopsARequestedShutdown(
        int exitCode)
    {
        // Arrange
        _status.ShutdownExitCode = exitCode;

        // Act
        _work.SetCanceled();
        await _stopping.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        await _app.StopAsync().ConfigureAwait(false);

        // Assert
        Assert.That(_status.ShutdownExitCode, Is.EqualTo(exitCode));
    }

    [TestCase(0)]
    [TestCase(8)]
    [TestCase(null)]
    public async Task UseMagpieRequestPipeline_PreservesExitStatus_WhenShutdownIsRequested(int? exitCode)
    {
        // Arrange
        _status.ShutdownExitCode = exitCode;
        _work.SetResult();

        // Act
        await _app.StopAsync().ConfigureAwait(false);

        // Assert
        Assert.That(_status.ShutdownExitCode, Is.EqualTo(exitCode));
    }

    [TestCase("/api/auth", 404)]
    [TestCase("/api/auth/login", 404)]
    [TestCase("/API/AUTH/login", 404)]
    [TestCase("/api/auth-providers", 503)]
    [TestCase("/api/system/setup/status", 404)]
    [TestCase("/api/system/setupX", 503)]
    [TestCase("/api/system/info", 503)]
    [TestCase("/api/system/restart", 503)]
    public async Task UseMagpieRequestPipeline_UsesRouteSegments_WhenSetupIsIncomplete(string path, int status)
    {
        // Arrange
        _settings.Setup(service => service.GetSystemGroupAsync("setup", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SettingsGroupDetail?)null);

        // Act
        using var response = await _client.GetAsync(path).ConfigureAwait(false);

        // Assert
        Assert.That((int)response.StatusCode, Is.EqualTo(status));
    }

    [Test]
    public async Task UseMagpieRequestPipeline_ReadsTheSetupFlagOnce_WhenSetupIsComplete()
    {
        // Act
        using var first = await _client.GetAsync("/api/content").ConfigureAwait(false);
        using var second = await _client.GetAsync("/api/content").ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((int)first.StatusCode, Is.EqualTo(404));
            Assert.That((int)second.StatusCode, Is.EqualTo(404));
            _settings.Verify(
                service => service.GetSystemGroupAsync("setup", It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [TestCase(null, "sqlite")]
    [TestCase("SQLITE", "sqlite")]
    [TestCase("postgres", "postgresql")]
    [TestCase("POSTGRESQL", "postgresql")]
    [TestCase("mssql", "sqlserver")]
    [TestCase("SQLSERVER", "sqlserver")]
    public void ResolveDatabaseConfiguration_ResolvesAliases_WhenAConnectionStringIsConfigured(
        string? provider, string expected)
    {
        // Arrange
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Magpie:Database:Provider"] = provider,
            ["Magpie:Database:ConnectionString"] = "configured connection"
        }).Build();

        // Act
        var result = MagpieHostBuilderExtensions.ResolveDatabaseConfiguration(
            configuration,
            TestContext.CurrentContext.TestDirectory);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Provider, Is.EqualTo(expected));
            Assert.That(result.ConnectionString, Is.EqualTo("configured connection"));
        }
    }
}
