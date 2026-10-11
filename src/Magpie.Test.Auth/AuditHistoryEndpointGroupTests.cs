using System.Diagnostics;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Auth;
using Magpie.Api;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Paging;
using Magpie.Sdk.Api;
using Magpie.Sdk.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Auth;

[TestFixture]
public sealed class AuditHistoryEndpointGroupTests
{
    private Mock<IAuditHistoryService> _auditHistoryService = null!;
    private ServiceProvider _services = null!;
    private RequestDelegate _pipeline = null!;

    [SetUp]
    public void SetUp()
    {
        _auditHistoryService = new Mock<IAuditHistoryService>();
        _auditHistoryService
            .Setup(service => service.QueryAsync(It.IsAny<AuditHistoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<AuditHistoryEntry>());

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new DiagnosticListener("Magpie.Test.Auth"));
        services.AddRouting();
        services.AddMagpieAuth(new ConfigurationBuilder().Build());
        services.AddMagpieAuthenticationScheme();
        services.AddSingleton(Mock.Of<ITokenService>());
        services.AddSingleton(Mock.Of<IApiKeyService>());
        services.AddSingleton(Mock.Of<IUserService>());
        services.AddSingleton(_auditHistoryService.Object);
        new MagpieCoreApiExtension().RegisterServices(services);
        _services = services.BuildServiceProvider();

        var app = new ApplicationBuilder(_services);
        app.UseRouting();
        app.UseAuthorization();
        app.UseEndpoints(endpoints => _services
            .GetServices<IApiEndpointGroup>()
            .Single(group => group.Name == "audit-history")
            .MapEndpoints(endpoints.MapGroup("/api/audit-history")));
        _pipeline = app.Build();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _services.DisposeAsync().ConfigureAwait(false);
    }

    [TestCase("Viewer", "", 403, 0)]
    [TestCase("Manager", "", 403, 0)]
    [TestCase("Admin", "", 200, 1)]
    [TestCase("Viewer", "?contentId=00000000-0000-0000-0000-0000000000c1", 200, 1)]
    [TestCase("Viewer", "?jobExecutionId=00000000-0000-0000-0000-0000000000e1", 200, 1)]
    public async Task GetAuditHistoryAsync_QueriesTheHistoryOnlyForAnAdminOrAScope_WhenCalledWithRole(
        string role,
        string queryString,
        int expectedStatus,
        int expectedQueries)
    {
        // Arrange
        var httpContext = new DefaultHttpContext
        {
            RequestServices = _services,
            User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, "00000000-0000-0000-0000-0000000000a1"),
                        new Claim(ClaimTypes.Role, role)
                    ],
                    "Test"))
        };
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Request.Path = "/api/audit-history";
        httpContext.Request.QueryString = new(queryString);

        // Act
        await _pipeline(httpContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(httpContext.Response.StatusCode, Is.EqualTo(expectedStatus));
            _auditHistoryService.Verify(
                service => service.QueryAsync(It.IsAny<AuditHistoryQuery>(), It.IsAny<CancellationToken>()),
                Times.Exactly(expectedQueries));
        }
    }
}
