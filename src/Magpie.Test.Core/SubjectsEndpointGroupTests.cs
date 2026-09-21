using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;

using Magpie.Api;
using Magpie.Models.Data;
using Magpie.Models.Filtering;
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
public sealed class SubjectsEndpointGroupTests
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private Mock<IFilterService> _filters = null!;

    [SetUp]
    public async Task SetUp()
    {
        _filters = new Mock<IFilterService>();
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(Mock.Of<ISubjectKindRegistry>());
        builder.Services.AddSingleton(_filters.Object);
        builder.Services.AddSingleton(Mock.Of<ISubjectService>());
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
    public async Task GetSubjectsAsync_WritesAProblemDetailsBody_WhenTheFilterDoesNotParse()
    {
        // Arrange
        _filters
            .Setup(service => service.ParseSubjectCriteria("name ~~ 'x'"))
            .Returns(new MagpieQlParseResult<SubjectFilterCriteria>
            {
                Error = new MagpieQlParseError
                {
                    Message = "Unexpected token.",
                    Position = 5,
                    Expected = "an operator"
                }
            });

        // Act
        using var response = await _client.GetAsync("/api/subjects/?filter=name%20~~%20'x'").ConfigureAwait(false);

        // Assert
        var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That((int)response.StatusCode, Is.EqualTo(400));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
            Assert.That(payload, Does.Contain("\"detail\":\"Unexpected token.\""));
            Assert.That(payload, Does.Contain("\"position\":5"));
        }
    }
}
