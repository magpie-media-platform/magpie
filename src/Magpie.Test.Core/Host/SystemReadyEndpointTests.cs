using System.Net.Http;
using System.Threading.Tasks;

using Magpie.Api;
using Magpie.Models;
using Magpie.Sdk.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Host;

[TestFixture]
public sealed class SystemReadyEndpointTests
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private DatabaseMigrationState _migrationState = null!;

    [SetUp]
    public async Task SetUp()
    {
        _migrationState = new DatabaseMigrationState();

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(_migrationState);
        builder.Services.AddSingleton(Mock.Of<ISubjectKindRegistry>());
        new MagpieCoreApiExtension().RegisterServices(builder.Services);
        _app = builder.Build();
        _app.UseMagpieDatabaseMigrationGate();
        _app.MapMagpieEndpoints();
        await _app.StartAsync().ConfigureAwait(false);
        _client = _app.GetTestClient();
    }

    [TearDown]
    public async Task TearDown()
    {
        _client.Dispose();
        await _app.DisposeAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task GetSystemReadiness_AnswersOk_WhenTheDatabaseWorkIsComplete()
    {
        // Arrange
        _migrationState.Complete();

        // Act
        using var response = await _client.GetAsync("/api/system/ready").ConfigureAwait(false);

        // Assert
        Assert.That((int)response.StatusCode, Is.EqualTo(200));
    }

    [Test]
    public async Task GetSystemReadiness_AnswersProblemDetails_WhenTheDatabaseWorkIsPending()
    {
        // Act
        using var response = await _client.GetAsync("/api/system/ready").ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((int)response.StatusCode, Is.EqualTo(503));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
        }
    }

    [Test]
    public async Task GetSystemReadiness_AnswersProblemDetails_WhenTheDatabaseWorkFailed()
    {
        // Arrange
        _migrationState.Fail();

        // Act
        using var response = await _client.GetAsync("/api/system/ready").ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((int)response.StatusCode, Is.EqualTo(503));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
        }
    }
}
