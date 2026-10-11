using System.Diagnostics;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Auth;
using Magpie.Api;
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
public sealed class ExtensionsEndpointGroupTests
{
    private Mock<IExtensionService> _extensionService = null!;
    private ServiceProvider _services = null!;
    private RequestDelegate _pipeline = null!;

    [SetUp]
    public void SetUp()
    {
        _extensionService = new Mock<IExtensionService>();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new DiagnosticListener("Magpie.Test.Auth"));
        services.AddRouting();
        services.AddMagpieAuth(new ConfigurationBuilder().Build());
        services.AddMagpieAuthenticationScheme();
        services.AddSingleton(Mock.Of<ITokenService>());
        services.AddSingleton(Mock.Of<IApiKeyService>());
        services.AddSingleton(Mock.Of<IUserService>());
        services.AddSingleton(_extensionService.Object);
        new MagpieCoreApiExtension().RegisterServices(services);
        _services = services.BuildServiceProvider();

        var app = new ApplicationBuilder(_services);
        app.UseRouting();
        app.UseAuthorization();
        app.UseEndpoints(endpoints => _services
            .GetServices<IApiEndpointGroup>()
            .Single(group => group.Name == "extensions")
            .MapEndpoints(endpoints.MapGroup("/api/extensions")));
        _pipeline = app.Build();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _services.DisposeAsync().ConfigureAwait(false);
    }

    [TestCase("Viewer", 403, 0)]
    [TestCase("Requester", 403, 0)]
    [TestCase("Manager", 403, 0)]
    [TestCase("Admin", 200, 1)]
    public async Task SearchExtensionsAsync_SearchesFeedsOnlyForAdmin_WhenCalledWithRole(
        string role,
        int expectedStatus,
        int expectedSearches)
    {
        // Arrange
        var httpContext = new DefaultHttpContext
        {
            RequestServices = _services,
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "Test")),
        };
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Request.Path = "/api/extensions/search";

        // Act
        await _pipeline(httpContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(httpContext.Response.StatusCode, Is.EqualTo(expectedStatus));
            _extensionService.Verify(
                service => service.SearchAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()),
                Times.Exactly(expectedSearches));
        }
    }
}
