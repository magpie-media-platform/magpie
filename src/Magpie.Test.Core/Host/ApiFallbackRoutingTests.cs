using System.Net.Http;
using System.Threading.Tasks;

using Magpie.Api;
using Magpie.Models;
using Magpie.Sdk.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Host;

[TestFixture]
public sealed class ApiFallbackRoutingTests
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    [SetUp]
    public async Task SetUp()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(new DatabaseMigrationState());
        builder.Services.AddSingleton(Mock.Of<ISubjectKindRegistry>());
        new MagpieCoreApiExtension().RegisterServices(builder.Services);
        _app = builder.Build();
        _app.MapMagpieEndpoints();

        _app.MapFallback("{*path:nonfile}", () => Results.Content("<html></html>", "text/html"))
            .WithMetadata(new HttpMethodMetadata(["GET", "HEAD"]));

        await _app.StartAsync().ConfigureAwait(false);
        _client = _app.GetTestClient();
    }

    [TearDown]
    public async Task TearDown()
    {
        _client.Dispose();
        await _app.DisposeAsync().ConfigureAwait(false);
    }

    [TestCase("GET", "/api/import/candidate-groups/accept", "POST")]
    [TestCase("GET", "/api/jobs/enqueue", "POST")]
    [TestCase("PUT", "/api/jobs/enqueue", "POST")]
    [TestCase("GET", "/api/jobs/execute-immediate", "POST")]
    [TestCase("PUT", "/api/jobs/00000000-0000-0000-0000-000000000001", "GET")]
    [TestCase("PUT", "/api/content/00000000-0000-0000-0000-000000000001", "DELETE, GET, PATCH")]
    public async Task MapMagpieEndpoints_AnswersMethodNotAllowed_WhenTheMethodDoesNotMatchARealRoute(
        string method, string path, string expectedMethods)
    {
        // Arrange
        using var request = new HttpRequestMessage(new HttpMethod(method), path);

        // Act
        using var response = await _client.SendAsync(request).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((int)response.StatusCode, Is.EqualTo(405));
            Assert.That(string.Join(", ", response.Content.Headers.Allow), Is.EqualTo(expectedMethods));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
        }
    }

    [TestCase("GET", "/api/there-is-no-such-route")]
    [TestCase("PUT", "/api/there-is-no-such-route")]
    [TestCase("GET", "/api/jobs/not-a-guid")]
    [TestCase("PUT", "/api/jobs/not-a-guid")]
    [TestCase("PUT", "/api/jobs/not-a-guid/cancel")]
    [TestCase("PUT", "/api/content/not-a-guid")]
    public async Task MapMagpieEndpoints_AnswersNotFound_WhenTheApiPathIsUnknown(string method, string path)
    {
        // Arrange
        using var request = new HttpRequestMessage(new HttpMethod(method), path);

        // Act
        using var response = await _client.SendAsync(request).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((int)response.StatusCode, Is.EqualTo(404));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
            Assert.That(response.Content.Headers.Allow, Is.Empty);
        }
    }

    [Test]
    public async Task MapMagpieEndpoints_AnswersTheSpaShell_WhenThePathIsNotAnApiPath()
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Get, "/library/import");

        // Act
        using var response = await _client.SendAsync(request).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((int)response.StatusCode, Is.EqualTo(200));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/html"));
        }
    }
}
