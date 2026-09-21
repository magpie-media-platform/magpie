using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Api;
using Magpie.Models;
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
public sealed class MediaServersEndpointGroupTests
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private Mock<IMediaServerService> _mediaServers = null!;

    [SetUp]
    public async Task SetUp()
    {
        _mediaServers = new Mock<IMediaServerService>();
        _mediaServers
            .Setup(service => service.GetSettingsSchemaAsync("jellyfin", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SettingSchemaDefinition>());
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(Mock.Of<ISubjectKindRegistry>());
        builder.Services.AddSingleton(_mediaServers.Object);
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
    public async Task GetMediaServersAsync_OmitsTheStoredSettings_WhenTheProviderSchemaCannotBeResolved()
    {
        // Arrange
        _mediaServers
            .Setup(service => service.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new MediaServer
                {
                    Name = "Jellyfin",
                    ProviderType = "jellyfin",
                    Settings = new Dictionary<string, string> { ["apiKey"] = "s3cr3t" }
                }
            ]);

        // Act
        using var response = await _client.GetAsync("/api/media-servers/").ConfigureAwait(false);

        // Assert
        var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That((int)response.StatusCode, Is.EqualTo(200));
            Assert.That(payload, Does.Not.Contain("s3cr3t"));
        }
    }

    [Test]
    public async Task CreateMediaServerAsync_WritesAnAbsolutePathLocation_WhenTheMediaServerIsCreated()
    {
        // Arrange
        _mediaServers
            .Setup(service => service.CreateAsync(
                It.IsAny<MediaServer>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaServer
            {
                Id = new Guid("22222222-2222-2222-2222-222222222222"),
                Name = "Jellyfin",
                ProviderType = "jellyfin"
            });
        using var body = new StringContent(
            "{\"name\":\"Jellyfin\",\"providerType\":\"jellyfin\"}", Encoding.UTF8, "application/json");

        // Act
        using var response = await _client.PostAsync("/api/media-servers/", body).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((int)response.StatusCode, Is.EqualTo(201));
            Assert.That(
                response.Headers.Location?.OriginalString,
                Is.EqualTo("/api/media-servers/22222222-2222-2222-2222-222222222222"));
        }
    }
}
