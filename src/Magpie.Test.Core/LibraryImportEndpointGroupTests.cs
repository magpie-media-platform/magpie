using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

using Magpie.Api;
using Magpie.Sdk.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core;

[TestFixture]
public sealed class LibraryImportEndpointGroupTests
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    [SetUp]
    public async Task SetUp()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(Mock.Of<ISubjectKindRegistry>());
        builder.Services.AddSingleton(Mock.Of<ILibraryImportService>());
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("Viewer", policy => policy.RequireRole("Admin"));
            options.AddPolicy("Requester", policy => policy.RequireRole("Admin"));
            options.AddPolicy("Manager", policy => policy.RequireRole("Admin"));
            options.AddPolicy("Admin", policy => policy.RequireRole("Admin"));
        });
        new MagpieCoreApiExtension().RegisterServices(builder.Services);
        _app = builder.Build();
        _app.Use((context, next) =>
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "11111111-1111-1111-1111-111111111111"),
                 new Claim(ClaimTypes.Role, "Admin")], "test"));
            return next(context);
        });
        _app.UseAuthorization();
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
    public async Task BulkAcceptLibraryImportCandidatesAsync_WritesProblemDetails_WhenTheMonitoringModeIsMissing()
    {
        // Arrange
        using var body = new StringContent("{}", Encoding.UTF8, "application/json");

        // Act
        using var response = await _client.PostAsync("/api/import/candidates/bulk-accept", body)
            .ConfigureAwait(false);

        // Assert
        var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That((int)response.StatusCode, Is.EqualTo(400));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
            Assert.That(payload, Does.Contain("\"detail\":\"A monitoring mode is required.\""));
        }
    }

    [TestCase("/api/import/candidates")]
    [TestCase("/api/import/candidate-groups")]
    public async Task QueryLibraryImportCandidatesAsync_WritesProblemDetails_WhenAStatusFilterValueIsUnknown(
        string route)
    {
        // Act
        using var response = await _client.GetAsync($"{route}?status=NotAStatus").ConfigureAwait(false);

        // Assert
        var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That((int)response.StatusCode, Is.EqualTo(400));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
            Assert.That(payload, Does.Contain("\"detail\":\"A status filter value is not a known status.\""));
        }
    }

    [Test]
    public async Task AcceptLibraryImportCandidateGroupAsync_WritesProblemDetails_WhenTheGroupKeyIsMissing()
    {
        // Arrange
        using var body = new StringContent(string.Empty, Encoding.UTF8, "application/json");

        // Act
        using var response = await _client.PostAsync("/api/import/candidate-groups/accept?groupKey=", body)
            .ConfigureAwait(false);

        // Assert
        var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That((int)response.StatusCode, Is.EqualTo(400));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
            Assert.That(payload, Does.Contain("\"detail\":\"A group key is required.\""));
        }
    }
}
