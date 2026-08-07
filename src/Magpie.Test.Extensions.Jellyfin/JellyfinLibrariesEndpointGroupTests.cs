using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Jellyfin;
using Magpie.Models;
using Magpie.Models.Enums;
using Magpie.Sdk.Services;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

using RichardSzalay.MockHttp;

namespace Magpie.Test.Extensions.Jellyfin;

[TestFixture]
public class JellyfinLibrariesEndpointGroupTests
{
    [Test]
    public async Task GetAssetStreamAsync_OmitsBlankConnectionSettings_WhenLibraryAssetLoads()
    {
        // Arrange
        var subject = new JellyfinAssetProvider();

        // Act
        using var stream = await subject.GetAssetStreamAsync("jellyfin-libraries.js", default)
            .ConfigureAwait(false);
        using var reader = new StreamReader(stream!);
        var script = await reader.ReadToEndAsync(default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stream, Is.Not.Null);
            Assert.That(script, Does.Contain($"const hostKey = '{JellyfinSettings.HostKey}';"));
            Assert.That(script, Does.Contain($"const apiKeyKey = '{JellyfinSettings.ApiKeyKey}';"));
            Assert.That(
                script,
                Does.Contain("if (typeof state[hostKey] === 'string' && state[hostKey].trim())"));
            Assert.That(script, Does.Contain("settings[hostKey] = state[hostKey];"));
            Assert.That(
                script,
                Does.Contain("if (typeof state[apiKeyKey] === 'string' && state[apiKeyKey].trim())"));
            Assert.That(script, Does.Contain("settings[apiKeyKey] = state[apiKeyKey];"));
            Assert.That(script, Does.Not.Contain("Object.prototype.hasOwnProperty.call(state"));
            Assert.That(
                script,
                Does.Contain("body: JSON.stringify({ mediaServerId: serverId, settings: settings || {} })"));
        }
    }

    [Test]
    public async Task ListLibrariesAsync_ReturnsOptions_WhenResolvedSettingsAuthenticate()
    {
        // Arrange
        var mediaServerId = Guid.NewGuid();
        var overlay = new Dictionary<string, string>
        {
            ["host"] = "http://jellyfin.local:8096",
            ["apiKey"] = string.Empty,
        };
        var resolved = new Dictionary<string, string>
        {
            ["host"] = "http://jellyfin.local:8096",
            ["apiKey"] = "stored-key",
        };
        var mediaServerService = new Mock<IMediaServerService>();
        mediaServerService
            .Setup(service => service.ResolveSettingsAsync(
                mediaServerId,
                overlay,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolved);
        var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Get, "http://jellyfin.local:8096/Library/VirtualFolders")
            .WithHeaders("X-Emby-Token", "stored-key")
            .Respond(
                "application/json",
                "[{\"Name\":\"Movies\",\"ItemId\":\"lib-1\",\"CollectionType\":\"movies\"}," +
                "{\"Name\":null,\"ItemId\":\"lib-2\",\"CollectionType\":\"tvshows\"}," +
                "{\"Name\":\"Missing id\",\"ItemId\":null}]");
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory
            .Setup(factory => factory.CreateClient(JellyfinClient.HttpClientName))
            .Returns(() => new HttpClient(handler, disposeHandler: false));
        var subject = new JellyfinLibrariesEndpointGroup();
        var request = new JellyfinLibrariesRequest { MediaServerId = mediaServerId, Settings = overlay };

        // Act
        var result = await subject.ListLibrariesAsync(
            mediaServerService.Object,
            new JellyfinClient(
                httpClientFactory.Object.CreateClient(JellyfinClient.HttpClientName),
                NullLogger<JellyfinClient>.Instance),
            NullLogger<JellyfinLibrariesEndpointGroup>.Instance,
            request,
            default).ConfigureAwait(false);

        // Assert
        var response = (IValueHttpResult)result;
        var options = (IReadOnlyList<SettingSchemaOption>)response.Value!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(options, Has.Count.EqualTo(2));
            Assert.That(options[0].Value, Is.EqualTo("lib-1"));
            Assert.That(options[0].Label, Is.EqualTo("Movies"));
            Assert.That(options[0].Description, Is.EqualTo("movies"));
            Assert.That(options[1].Value, Is.EqualTo("lib-2"));
            Assert.That(options[1].Label, Is.EqualTo("lib-2"));
        }

        mediaServerService.Verify(
            service => service.ResolveSettingsAsync(
                mediaServerId,
                overlay,
                It.IsAny<CancellationToken>()),
            Times.Once);
        handler.VerifyNoOutstandingExpectation();
    }

    [TestCase(HttpStatusCode.Unauthorized)]
    [TestCase(HttpStatusCode.Forbidden)]
    public async Task ListLibrariesAsync_ReturnsUnauthorized_WhenAuthenticationFails(HttpStatusCode statusCode)
    {
        // Arrange
        var resolved = new Dictionary<string, string>
        {
            ["host"] = "http://jellyfin.local:8096",
            ["apiKey"] = "invalid-key",
        };
        var mediaServerService = new Mock<IMediaServerService>();
        mediaServerService
            .Setup(service => service.ResolveSettingsAsync(
                null,
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolved);
        var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Get, "http://jellyfin.local:8096/Library/VirtualFolders")
            .Respond(statusCode);
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory
            .Setup(factory => factory.CreateClient(JellyfinClient.HttpClientName))
            .Returns(() => new HttpClient(handler, disposeHandler: false));
        var subject = new JellyfinLibrariesEndpointGroup();
        var request = new JellyfinLibrariesRequest();

        // Act
        var result = await subject.ListLibrariesAsync(
            mediaServerService.Object,
            new JellyfinClient(
                httpClientFactory.Object.CreateClient(JellyfinClient.HttpClientName),
                NullLogger<JellyfinClient>.Instance),
            NullLogger<JellyfinLibrariesEndpointGroup>.Instance,
            request,
            default).ConfigureAwait(false);

        // Assert
        var response = (IStatusCodeHttpResult)result;
        Assert.That(response.StatusCode, Is.EqualTo(StatusCodes.Status401Unauthorized));
        handler.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task ListLibrariesAsync_ReturnsBadGateway_WhenJellyfinIsUnreachable()
    {
        // Arrange
        var resolved = new Dictionary<string, string>
        {
            ["host"] = "http://127.0.0.1:1",
            ["apiKey"] = "invalid-key",
        };
        var mediaServerService = new Mock<IMediaServerService>();
        mediaServerService
            .Setup(service => service.ResolveSettingsAsync(
                null,
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolved);
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory
            .Setup(factory => factory.CreateClient(JellyfinClient.HttpClientName))
            .Returns(() => new HttpClient { Timeout = TimeSpan.FromSeconds(5) });
        var subject = new JellyfinLibrariesEndpointGroup();
        var request = new JellyfinLibrariesRequest();

        // Act
        var result = await subject.ListLibrariesAsync(
            mediaServerService.Object,
            new JellyfinClient(
                httpClientFactory.Object.CreateClient(JellyfinClient.HttpClientName),
                NullLogger<JellyfinClient>.Instance),
            NullLogger<JellyfinLibrariesEndpointGroup>.Instance,
            request,
            default).ConfigureAwait(false);

        // Assert
        var response = (IStatusCodeHttpResult)result;
        Assert.That(response.StatusCode, Is.EqualTo(StatusCodes.Status502BadGateway));
    }

    [Test]
    public async Task ListLibrariesAsync_ReturnsBadGateway_WhenJellyfinReturnsMalformedJson()
    {
        // Arrange
        var resolved = new Dictionary<string, string>
        {
            ["host"] = "http://jellyfin.local:8096",
            ["apiKey"] = "key",
        };
        var mediaServerService = new Mock<IMediaServerService>();
        mediaServerService
            .Setup(service => service.ResolveSettingsAsync(
                null,
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolved);
        var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Get, "http://jellyfin.local:8096/Library/VirtualFolders")
            .Respond("application/json", "not-json");
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory
            .Setup(factory => factory.CreateClient(JellyfinClient.HttpClientName))
            .Returns(() => new HttpClient(handler, disposeHandler: false));
        var subject = new JellyfinLibrariesEndpointGroup();
        var request = new JellyfinLibrariesRequest();

        // Act
        var result = await subject.ListLibrariesAsync(
            mediaServerService.Object,
            new JellyfinClient(
                httpClientFactory.Object.CreateClient(JellyfinClient.HttpClientName),
                NullLogger<JellyfinClient>.Instance),
            NullLogger<JellyfinLibrariesEndpointGroup>.Instance,
            request,
            default).ConfigureAwait(false);

        // Assert
        var response = (IStatusCodeHttpResult)result;
        Assert.That(response.StatusCode, Is.EqualTo(StatusCodes.Status502BadGateway));
        handler.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task ListLibrariesAsync_ReturnsBadGateway_WhenHostIsInvalid()
    {
        // Arrange
        var resolved = new Dictionary<string, string>
        {
            ["host"] = "not-a-url",
            ["apiKey"] = "key",
        };
        var mediaServerService = new Mock<IMediaServerService>();
        mediaServerService
            .Setup(service => service.ResolveSettingsAsync(
                null,
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolved);
        var httpClientFactory = new Mock<IHttpClientFactory>();
        var subject = new JellyfinLibrariesEndpointGroup();
        var request = new JellyfinLibrariesRequest();

        // Act
        var result = await subject.ListLibrariesAsync(
            mediaServerService.Object,
            new JellyfinClient(
                httpClientFactory.Object.CreateClient(JellyfinClient.HttpClientName),
                NullLogger<JellyfinClient>.Instance),
            NullLogger<JellyfinLibrariesEndpointGroup>.Instance,
            request,
            default).ConfigureAwait(false);

        // Assert
        var response = (IStatusCodeHttpResult)result;
        Assert.That(response.StatusCode, Is.EqualTo(StatusCodes.Status502BadGateway));
    }

    [Test]
    public async Task ListLibrariesAsync_ReturnsEmpty_WhenCredentialsAreMissing()
    {
        // Arrange
        var resolved = new Dictionary<string, string>
        {
            ["host"] = "http://jellyfin.local:8096",
            ["apiKey"] = string.Empty,
        };
        var mediaServerService = new Mock<IMediaServerService>();
        mediaServerService
            .Setup(service => service.ResolveSettingsAsync(
                null,
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolved);
        var httpClientFactory = new Mock<IHttpClientFactory>();
        var subject = new JellyfinLibrariesEndpointGroup();
        var request = new JellyfinLibrariesRequest();

        // Act
        var result = await subject.ListLibrariesAsync(
            mediaServerService.Object,
            new JellyfinClient(
                httpClientFactory.Object.CreateClient(JellyfinClient.HttpClientName),
                NullLogger<JellyfinClient>.Instance),
            NullLogger<JellyfinLibrariesEndpointGroup>.Instance,
            request,
            default).ConfigureAwait(false);

        // Assert
        var response = (IValueHttpResult)result;
        var options = (IReadOnlyList<SettingSchemaOption>)response.Value!;
        Assert.That(options, Is.Empty);
    }

    [Test]
    public async Task ListLibrariesAsync_ReturnsEmpty_WhenJellyfinHasNoLibraries()
    {
        // Arrange
        var resolved = new Dictionary<string, string>
        {
            ["host"] = "http://jellyfin.local:8096",
            ["apiKey"] = "key",
        };
        var mediaServerService = new Mock<IMediaServerService>();
        mediaServerService
            .Setup(service => service.ResolveSettingsAsync(
                null,
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolved);
        var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Get, "http://jellyfin.local:8096/Library/VirtualFolders")
            .Respond("application/json", "[]");
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory
            .Setup(factory => factory.CreateClient(JellyfinClient.HttpClientName))
            .Returns(() => new HttpClient(handler, disposeHandler: false));
        var subject = new JellyfinLibrariesEndpointGroup();
        var request = new JellyfinLibrariesRequest();

        // Act
        var result = await subject.ListLibrariesAsync(
            mediaServerService.Object,
            new JellyfinClient(
                httpClientFactory.Object.CreateClient(JellyfinClient.HttpClientName),
                NullLogger<JellyfinClient>.Instance),
            NullLogger<JellyfinLibrariesEndpointGroup>.Instance,
            request,
            default).ConfigureAwait(false);

        // Assert
        var response = (IValueHttpResult)result;
        var options = (IReadOnlyList<SettingSchemaOption>)response.Value!;
        Assert.That(options, Is.Empty);
        handler.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task MapEndpoints_RequiresManager_WhenLibrariesEndpointIsMapped()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddAuthorization();
        var app = builder.Build();
        var subject = new JellyfinLibrariesEndpointGroup();
        subject.MapEndpoints(app.MapGroup("api/jellyfin"));

        // Act
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText == "api/jellyfin/libraries");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods, Does.Contain("POST"));
            Assert.That(
                endpoint.Metadata.GetMetadata<IAuthorizeData>()!.Policy,
                Is.EqualTo(nameof(Role.Manager)));
        }

        await app.DisposeAsync().ConfigureAwait(false);
    }
}
