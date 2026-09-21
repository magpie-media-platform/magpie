using System;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Api;
using Magpie.Models.Data;
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
public sealed class IndexersEndpointGroupTests
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private Mock<IIndexerService> _indexers = null!;

    [SetUp]
    public async Task SetUp()
    {
        _indexers = new Mock<IIndexerService>();
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(Mock.Of<ISubjectKindRegistry>());
        builder.Services.AddSingleton(_indexers.Object);
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
    public async Task GetIndexerSchemaAsync_ReturnsNotFound_WhenTheIndexerDoesNotExist()
    {
        // Arrange
        _indexers
            .Setup(service => service.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Indexer?)null);

        // Act
        using var response = await _client
            .GetAsync("/api/indexers/11111111-1111-1111-1111-111111111111/schema")
            .ConfigureAwait(false);

        // Assert
        Assert.That((int)response.StatusCode, Is.EqualTo(404));
    }
}
