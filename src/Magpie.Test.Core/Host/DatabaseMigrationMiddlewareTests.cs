using System.IO;
using System.Threading.Tasks;

using Magpie.Models;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

using NUnit.Framework;

namespace Magpie.Test.Core.Host;

[TestFixture]
public sealed class DatabaseMigrationMiddlewareTests
{
    private WebApplication _app = null!;
    private DatabaseMigrationState _migrationState = null!;
    private RequestDelegate _pipeline = null!;
    private bool _reachedTheEndOfThePipeline;

    [SetUp]
    public void SetUp()
    {
        _migrationState = new DatabaseMigrationState();
        _reachedTheEndOfThePipeline = false;

        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddSingleton(_migrationState);
        _app = builder.Build();
        _app.UseMagpieDatabaseMigrationGate();
        _app.Run(_ =>
        {
            _reachedTheEndOfThePipeline = true;
            return Task.CompletedTask;
        });
        _pipeline = ((IApplicationBuilder)_app).Build();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _app.DisposeAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task UseMagpieDatabaseMigrationGate_AnswersProblemDetails_WhenTheDatabaseWorkIsPending()
    {
        // Arrange
        var context = new DefaultHttpContext { RequestServices = _app.Services };
        context.Request.Path = "/api/content";
        context.Response.Body = new MemoryStream();

        // Act
        await _pipeline(context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(context.Response.StatusCode, Is.EqualTo(503));
            Assert.That(context.Response.ContentType, Does.StartWith("application/problem+json"));
            Assert.That(_reachedTheEndOfThePipeline, Is.False);
        }
    }

    [Test]
    public async Task UseMagpieDatabaseMigrationGate_ReportsTheMigratingStatus_WhenTheHealthRouteRuns()
    {
        // Arrange
        var context = new DefaultHttpContext { RequestServices = _app.Services };
        context.Request.Path = "/api/system/health";
        context.Response.Body = new MemoryStream();

        // Act
        await _pipeline(context).ConfigureAwait(false);
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(context.Response.StatusCode, Is.EqualTo(200));
            Assert.That(body, Is.EqualTo("{\"status\":\"Migrating\"}"));
            Assert.That(_reachedTheEndOfThePipeline, Is.False);
        }
    }

    [Test]
    public async Task UseMagpieDatabaseMigrationGate_AnswersHtml_WhenABrowserNavigationRuns()
    {
        // Arrange
        var context = new DefaultHttpContext { RequestServices = _app.Services };
        context.Request.Path = "/";
        context.Request.Headers.Accept = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";
        context.Response.Body = new MemoryStream();

        // Act
        await _pipeline(context).ConfigureAwait(false);
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(context.Response.StatusCode, Is.EqualTo(503));
            Assert.That(context.Response.ContentType, Does.StartWith("text/html"));
            Assert.That(body, Does.Contain("The host is applying database migrations."));
            Assert.That(_reachedTheEndOfThePipeline, Is.False);
        }
    }

    [Test]
    public async Task UseMagpieDatabaseMigrationGate_ReportsTheStartupFailedStatus_WhenTheDatabaseWorkFailed()
    {
        // Arrange
        var context = new DefaultHttpContext { RequestServices = _app.Services };
        context.Request.Path = "/api/system/health";
        context.Response.Body = new MemoryStream();
        _migrationState.Fail();

        // Act
        await _pipeline(context).ConfigureAwait(false);
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(context.Response.StatusCode, Is.EqualTo(200));
            Assert.That(body, Is.EqualTo("{\"status\":\"StartupFailed\"}"));
        }
    }

    [Test]
    public async Task UseMagpieDatabaseMigrationGate_RunsTheRestOfThePipeline_WhenTheDatabaseWorkIsComplete()
    {
        // Arrange
        var context = new DefaultHttpContext { RequestServices = _app.Services };
        context.Request.Path = "/api/content";
        context.Response.Body = new MemoryStream();
        _migrationState.Complete();

        // Act
        await _pipeline(context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_reachedTheEndOfThePipeline, Is.True);
            Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        }
    }
}
