using System;
using System.Collections.Generic;
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
    [TestCase("host", "edited-key")]
    [TestCase("HOST", "replacement-key")]
    public async Task ListLibrariesAsync_UsesEditedKeyAndStoredHost_WhenCallerOverridesSavedSettings(
        string hostKey,
        string apiKey)
    {
        // Arrange
        var mediaServerId = Guid.NewGuid();
        var overlay = new Dictionary<string, string>
        {
            [hostKey] = "http://attacker.test",
            ["apiKey"] = apiKey,
        };
        var resolved = new Dictionary<string, string>
        {
            ["host"] = "http://jellyfin.local:8096",
            ["apiKey"] = apiKey,
        };
        var mediaServerService = new Mock<IMediaServerService>();
        mediaServerService
            .Setup(service => service.ResolveSettingsAsync(
                mediaServerId,
                It.Is<IReadOnlyDictionary<string, string>>(settings =>
                    settings.Count == 1 && settings["apiKey"] == apiKey),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolved);
        var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Get, "http://jellyfin.local:8096/Library/VirtualFolders")
            .WithHeaders("X-Emby-Token", apiKey)
            .Respond(
                "application/json",
                "[{\"Name\":\"Movies\",\"ItemId\":\"lib-1\",\"CollectionType\":\"movies\"}," +
                "{\"Name\":null,\"ItemId\":\"lib-2\",\"CollectionType\":\"tvshows\"}," +
                "{\"Name\":\"Missing id\",\"ItemId\":null}]");
        using var http = new HttpClient(handler, disposeHandler: false);
        var subject = new JellyfinLibrariesEndpointGroup();
        var request = new JellyfinLibrariesRequest { MediaServerId = mediaServerId, Settings = overlay };

        // Act
        var result = await subject.ListLibrariesAsync(
            new JellyfinLibraryService(
                mediaServerService.Object,
                new JellyfinClient(http, NullLogger<JellyfinClient>.Instance),
                NullLogger<JellyfinLibraryService>.Instance),
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
                It.Is<IReadOnlyDictionary<string, string>>(settings =>
                    settings.Count == 1 && settings["apiKey"] == apiKey),
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
        using var http = new HttpClient(handler, disposeHandler: false);
        var subject = new JellyfinLibrariesEndpointGroup();
        var request = new JellyfinLibrariesRequest();

        // Act
        var result = await subject.ListLibrariesAsync(
            new JellyfinLibraryService(
                mediaServerService.Object,
                new JellyfinClient(http, NullLogger<JellyfinClient>.Instance),
                NullLogger<JellyfinLibraryService>.Instance),
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
        using var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Get, "http://jellyfin.local:8096/Library/VirtualFolders")
            .Throw(new HttpRequestException());
        using var http = new HttpClient(handler, disposeHandler: false);
        var subject = new JellyfinLibrariesEndpointGroup();
        var request = new JellyfinLibrariesRequest();

        // Act
        var result = await subject.ListLibrariesAsync(
            new JellyfinLibraryService(
                mediaServerService.Object,
                new JellyfinClient(http, NullLogger<JellyfinClient>.Instance),
                NullLogger<JellyfinLibraryService>.Instance),
            request,
            default).ConfigureAwait(false);

        // Assert
        var response = (IStatusCodeHttpResult)result;
        Assert.That(response.StatusCode, Is.EqualTo(StatusCodes.Status502BadGateway));
        handler.VerifyNoOutstandingExpectation();
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
        using var http = new HttpClient(handler, disposeHandler: false);
        var subject = new JellyfinLibrariesEndpointGroup();
        var request = new JellyfinLibrariesRequest();

        // Act
        var result = await subject.ListLibrariesAsync(
            new JellyfinLibraryService(
                mediaServerService.Object,
                new JellyfinClient(http, NullLogger<JellyfinClient>.Instance),
                NullLogger<JellyfinLibraryService>.Instance),
            request,
            default).ConfigureAwait(false);

        // Assert
        var response = (IStatusCodeHttpResult)result;
        Assert.That(response.StatusCode, Is.EqualTo(StatusCodes.Status502BadGateway));
        handler.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task ListLibrariesAsync_ReturnsBadRequest_WhenHostIsInvalid()
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
        using var http = new HttpClient();
        var subject = new JellyfinLibrariesEndpointGroup();
        var request = new JellyfinLibrariesRequest();

        // Act
        var result = await subject.ListLibrariesAsync(
            new JellyfinLibraryService(
                mediaServerService.Object,
                new JellyfinClient(http, NullLogger<JellyfinClient>.Instance),
                NullLogger<JellyfinLibraryService>.Instance),
            request,
            default).ConfigureAwait(false);

        // Assert
        var response = (IStatusCodeHttpResult)result;
        Assert.That(response.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
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
        using var http = new HttpClient();
        var subject = new JellyfinLibrariesEndpointGroup();
        var request = new JellyfinLibrariesRequest();

        // Act
        var result = await subject.ListLibrariesAsync(
            new JellyfinLibraryService(
                mediaServerService.Object,
                new JellyfinClient(http, NullLogger<JellyfinClient>.Instance),
                NullLogger<JellyfinLibraryService>.Instance),
            request,
            default).ConfigureAwait(false);

        // Assert
        var response = (IValueHttpResult)result;
        var options = (IReadOnlyList<SettingSchemaOption>)response.Value!;
        Assert.That(options, Is.Empty);
    }

    [Test]
    public async Task ListLibrariesAsync_UsesCallerSettings_WhenServerIsUnsaved()
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
                resolved,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolved);
        var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Get, "http://jellyfin.local:8096/Library/VirtualFolders")
            .Respond("application/json", "[]");
        using var http = new HttpClient(handler, disposeHandler: false);
        var subject = new JellyfinLibrariesEndpointGroup();
        var request = new JellyfinLibrariesRequest { Settings = resolved };

        // Act
        var result = await subject.ListLibrariesAsync(
            new JellyfinLibraryService(
                mediaServerService.Object,
                new JellyfinClient(http, NullLogger<JellyfinClient>.Instance),
                NullLogger<JellyfinLibraryService>.Instance),
            request,
            default).ConfigureAwait(false);

        // Assert
        var response = (IValueHttpResult)result;
        var options = (IReadOnlyList<SettingSchemaOption>)response.Value!;
        Assert.That(options, Is.Empty);
        mediaServerService.Verify(service => service.ResolveSettingsAsync(
            null, resolved, It.IsAny<CancellationToken>()), Times.Once);
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
