using System;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Api;
using Magpie.Sdk.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core;

[TestFixture]
public sealed class RootFoldersEndpointGroupTests
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private Mock<IRootFolderService> _rootFolders = null!;

    [SetUp]
    public async Task SetUp()
    {
        _rootFolders = new Mock<IRootFolderService>();
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(Mock.Of<ISubjectKindRegistry>());
        builder.Services.AddSingleton(_rootFolders.Object);
        builder.Services.AddAuthorization(options => options.AddPolicy("Admin", policy => policy.RequireRole("Admin")));
        new MagpieCoreApiExtension().RegisterServices(builder.Services);
        _app = builder.Build();
        _app.Use((context, next) =>
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "06a4061f-c72d-4a1b-a49d-4d20a1f8bb70"),
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

    [TestCase(true, 204)]
    [TestCase(false, 404)]
    public async Task DeleteRootFolderAsync_ReturnsTheDeletionStatus_WhenTheServiceReportsWhetherTheFolderExists(
        bool deleted, int expectedStatus)
    {
        // Arrange
        _rootFolders.Setup(service => service.DeleteAsync(
                new Guid("bec19636-59a5-499a-9ae0-a1c9858af6d9"),
                new Guid("06a4061f-c72d-4a1b-a49d-4d20a1f8bb70"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(deleted);

        // Act
        using var response = await _client.DeleteAsync("/api/root-folders/bec19636-59a5-499a-9ae0-a1c9858af6d9")
            .ConfigureAwait(false);

        // Assert
        Assert.That((int)response.StatusCode, Is.EqualTo(expectedStatus));
    }
}
