using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.UI;
using Magpie.Api.EndpointGroups;
using Magpie.Models.Enums;
using Magpie.Models.UI;
using Magpie.Test.Shared;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Api.EndpointGroups;

[TestFixture]
public class UIEndpointGroupTests
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private FakeUIExtensionProvider _uiProvider = null!;
    private FakeAssetProvider _assetProvider = null!;

    [SetUp]
    public void SetUp()
    {
        _uiProvider = new FakeUIExtensionProvider();
        _assetProvider = new FakeAssetProvider();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders().AddTestLogger();
        var authServiceMock = new Mock<IAuthenticationService>();
        authServiceMock
            .Setup(m => m.AuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<string>()))
            .ReturnsAsync(AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity("Test")), "Test")));
        builder.Services.AddSingleton(authServiceMock.Object);
        builder.Services.AddAuthorizationBuilder()
            .SetFallbackPolicy(null)
            .SetDefaultPolicy(new AuthorizationPolicyBuilder()
                .RequireAssertion(_ => true)
                .Build())
            .AddPolicy(nameof(Role.Viewer), policy => policy.RequireAssertion(_ => true));

        builder.Services.AddSingleton<IUIExtensionProvider>(_uiProvider);
        builder.Services.AddSingleton<IAssetProvider>(_assetProvider);
        builder.Services.AddSingleton<IApiEndpointGroup, UIEndpointGroup>();

        var app = builder.Build();

        var apiGroup = app.MapGroup("/api");
        var endpointGroups = app.Services.GetServices<IApiEndpointGroup>();
        foreach (var group in endpointGroups)
        {
            var groupRoute = apiGroup.MapGroup(group.Name);
            group.MapEndpoints(groupRoute);
        }

        app.UseAuthorization();

        _host = app;
        _host.Start();
        _client = _host.GetTestClient();
    }

    [TearDown]
    public void TearDown()
    {
        _client?.Dispose();
        _host?.Dispose();
    }

    [Test]
    public async Task GetManifest_ReturnsEmpty_WhenNoProvidersContribute()
    {
        // Act
        var response = await _client.GetAsync("/api/ui/manifest").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<UIManifest>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content!.Extensions, Is.Empty);
            Assert.That(content.Assets, Is.Empty);
        }
    }

    [Test]
    public async Task GetManifest_AggregatesExtensionsAndAssets()
    {
        // Arrange
        var extensionId = Guid.NewGuid();
        var uiExtensionId = Guid.NewGuid();
        var assetId = Guid.NewGuid();

        _uiProvider.Extensions =
        [
            new UIExtension
            {
                Id = uiExtensionId,
                Name = "Header Widget",
                Slot = UISlot.Header,
                Modifier = UISlotModifier.After,
                TagName = "x-header-widget",
                Priority = 10
            }
        ];

        _assetProvider.Assets =
        [
            new Asset
            {
                Id = assetId,
                ExtensionId = extensionId,
                Name = "main.js",
                Kind = AssetKind.Script,
                RelativePath = "main.js",
                Priority = 0
            }
        ];

        // Act
        var response = await _client.GetAsync("/api/ui/manifest").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<UIManifest>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content!.Extensions, Has.Count.EqualTo(1));
            Assert.That(content.Extensions[0].Id, Is.EqualTo(uiExtensionId));
            Assert.That(content.Extensions[0].Name, Is.EqualTo("Header Widget"));
            Assert.That(content.Extensions[0].TagName, Is.EqualTo("x-header-widget"));
            Assert.That(content.Assets, Has.Count.EqualTo(1));
            Assert.That(content.Assets[0].Id, Is.EqualTo(assetId));
            Assert.That(content.Assets[0].ExtensionId, Is.EqualTo(extensionId));
            Assert.That(content.Assets[0].RelativePath, Is.EqualTo("main.js"));
        }
    }

    [Test]
    public async Task GetManifest_SkipsProvidersThatThrow()
    {
        // Arrange
        _uiProvider.ThrowOnList = true;
        _assetProvider.Assets =
        [
            new Asset
            {
                Id = Guid.NewGuid(),
                ExtensionId = Guid.NewGuid(),
                Name = "main.js",
                Kind = AssetKind.Script,
                RelativePath = "main.js"
            }
        ];

        // Act
        var response = await _client.GetAsync("/api/ui/manifest").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<UIManifest>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content!.Extensions, Is.Empty);
            Assert.That(content.Assets, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task GetAsset_ReturnsFileContents_WhenProviderSuppliesStream()
    {
        // Arrange
        var extensionId = Guid.NewGuid();
        var assetBytes = Encoding.UTF8.GetBytes("console.log('hello extension');");

        _assetProvider.Assets =
        [
            new Asset
            {
                Id = Guid.NewGuid(),
                ExtensionId = extensionId,
                Name = "main.js",
                Kind = AssetKind.Script,
                RelativePath = "main.js"
            }
        ];
        _assetProvider.Files["main.js"] = assetBytes;

        // Act
        var response = await _client.GetAsync($"/api/ui/assets/{extensionId}/main.js").ConfigureAwait(false);

        // Assert
        var body = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/javascript"));
            Assert.That(body, Is.EqualTo(assetBytes));
        }
    }

    [Test]
    public async Task GetAsset_ResolvesNestedRelativePath()
    {
        // Arrange
        var extensionId = Guid.NewGuid();
        var assetBytes = Encoding.UTF8.GetBytes("body { color: red; }");

        _assetProvider.Assets =
        [
            new Asset
            {
                Id = Guid.NewGuid(),
                ExtensionId = extensionId,
                Name = "styles",
                Kind = AssetKind.Style,
                RelativePath = "css/site.css"
            }
        ];
        _assetProvider.Files["css/site.css"] = assetBytes;

        // Act
        var response = await _client.GetAsync($"/api/ui/assets/{extensionId}/css/site.css").ConfigureAwait(false);

        // Assert
        var body = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/css"));
            Assert.That(body, Is.EqualTo(assetBytes));
        }
    }

    [Test]
    public async Task GetAsset_ReturnsNotFound_WhenStreamIsNull()
    {
        // Arrange
        var extensionId = Guid.NewGuid();
        _assetProvider.Assets =
        [
            new Asset
            {
                Id = Guid.NewGuid(),
                ExtensionId = extensionId,
                Name = "main.js",
                Kind = AssetKind.Script,
                RelativePath = "main.js"
            }
        ];

        // Act
        var response = await _client.GetAsync($"/api/ui/assets/{extensionId}/missing.js").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetAsset_RejectsParentTraversal_ViaDoubleEncodedDots()
    {
        // Arrange
        var extensionId = Guid.NewGuid();
        // Double-encode the dots so they survive ASP.NET path normalisation and reach
        // the handler as the literal string "..", which the IsSafeRelativePath check
        // must reject.
        var response = await _client.GetAsync(
            $"/api/ui/assets/{extensionId}/%252E%252E/secret.js").ConfigureAwait(false);

        // Assert -- routing rejects the URL outright (404) or the handler rejects it (400).
        // Either is an acceptable defence-in-depth outcome; what must never happen is 200.
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.Not.EqualTo(HttpStatusCode.OK));
            Assert.That(response.StatusCode, Is.AnyOf(HttpStatusCode.BadRequest, HttpStatusCode.NotFound));
        }
    }

    [Test]
    public async Task GetAsset_RejectsParentTraversal_ViaCollapsedRoute()
    {
        // Arrange
        var extensionId = Guid.NewGuid();

        // ".." segments embedded in the URL get collapsed by ASP.NET routing before the
        // handler runs. The end result is either a route mismatch (404) or, if the path
        // survives normalisation in some form, a handler-level rejection (400). Both
        // outcomes are acceptable; the asset must never be served.
        var response = await _client.GetAsync($"/api/ui/assets/{extensionId}/css/../../etc/passwd")
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.Not.EqualTo(HttpStatusCode.OK));
            Assert.That(response.StatusCode, Is.AnyOf(HttpStatusCode.BadRequest, HttpStatusCode.NotFound));
        }
    }

    [Test]
    public async Task GetAsset_ResolvesAssetFromOwningProviderWithoutScanningOthers()
    {
        // Build a fresh host with two asset providers; assert the request only hits the
        // provider that owns the requested extension.
        var ownedExtensionId = Guid.NewGuid();
        var otherExtensionId = Guid.NewGuid();

        var ownerProvider = new FakeAssetProvider
        {
            Assets =
            [
                new Asset
                {
                    Id = Guid.NewGuid(),
                    ExtensionId = ownedExtensionId,
                    Name = "main.js",
                    Kind = AssetKind.Script,
                    RelativePath = "main.js"
                }
            ]
        };
        ownerProvider.Files["main.js"] = "console.log('owner');"u8.ToArray();

        var otherProvider = new FakeAssetProvider
        {
            Assets =
            [
                new Asset
                {
                    Id = Guid.NewGuid(),
                    ExtensionId = otherExtensionId,
                    Name = "main.js",
                    Kind = AssetKind.Script,
                    RelativePath = "main.js"
                }
            ]
        };

        using var host = BuildHostWithProviders([ownerProvider, otherProvider]);
        host.Start();
        using var client = host.GetTestClient();

        // Act -- two requests for the owned extension; the second must hit a cached index
        var response1 = await client.GetAsync($"/api/ui/assets/{ownedExtensionId}/main.js")
            .ConfigureAwait(false);
        var response2 = await client.GetAsync($"/api/ui/assets/{ownedExtensionId}/main.js")
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response1.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response2.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            // Owner should have served both streams; non-owner should never be probed
            // for either request, and ListAssets must only have been called once on
            // each provider while the index was built.
            Assert.That(ownerProvider.GetAssetStreamCallCount, Is.EqualTo(2));
            Assert.That(otherProvider.GetAssetStreamCallCount, Is.EqualTo(0));
            Assert.That(ownerProvider.ListAssetsCallCount, Is.EqualTo(1));
            Assert.That(otherProvider.ListAssetsCallCount, Is.EqualTo(1));
        }
    }

    private static IHost BuildHostWithProviders(IReadOnlyList<IAssetProvider> providers)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders().AddTestLogger();
        var authServiceMock = new Mock<IAuthenticationService>();
        authServiceMock
            .Setup(m => m.AuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<string>()))
            .ReturnsAsync(AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity("Test")), "Test")));
        builder.Services.AddSingleton(authServiceMock.Object);
        builder.Services.AddAuthorizationBuilder()
            .SetFallbackPolicy(null)
            .SetDefaultPolicy(new AuthorizationPolicyBuilder()
                .RequireAssertion(_ => true)
                .Build())
            .AddPolicy(nameof(Role.Viewer), policy => policy.RequireAssertion(_ => true));

        foreach (var p in providers)
        {
            builder.Services.AddSingleton(p);
        }
        builder.Services.AddSingleton<IApiEndpointGroup, UIEndpointGroup>();

        var app = builder.Build();
        var apiGroup = app.MapGroup("/api");
        var endpointGroups = app.Services.GetServices<IApiEndpointGroup>();
        foreach (var group in endpointGroups)
        {
            var groupRoute = apiGroup.MapGroup(group.Name);
            group.MapEndpoints(groupRoute);
        }
        app.UseAuthorization();
        return app;
    }

    [Test]
    public void IsSafeRelativePath_RejectsAbsolutePaths()
    {
        Assert.Multiple(() =>
        {
            Assert.That(UIEndpointGroup.IsSafeRelativePath("/etc/passwd"), Is.False);
            Assert.That(UIEndpointGroup.IsSafeRelativePath("C:\\Windows"), Is.False);
            Assert.That(UIEndpointGroup.IsSafeRelativePath("../etc/passwd"), Is.False);
            Assert.That(UIEndpointGroup.IsSafeRelativePath("css/../etc/passwd"), Is.False);
            Assert.That(UIEndpointGroup.IsSafeRelativePath(""), Is.False);
            Assert.That(UIEndpointGroup.IsSafeRelativePath("foo:bar"), Is.False);
        });
    }

    [Test]
    public void IsSafeRelativePath_AcceptsSafePaths()
    {
        Assert.Multiple(() =>
        {
            Assert.That(UIEndpointGroup.IsSafeRelativePath("main.js"), Is.True);
            Assert.That(UIEndpointGroup.IsSafeRelativePath("css/site.css"), Is.True);
            Assert.That(UIEndpointGroup.IsSafeRelativePath("nested/deeper/file.json"), Is.True);
        });
    }

    private sealed class FakeUIExtensionProvider : IUIExtensionProvider
    {
        public IReadOnlyList<UIExtension> Extensions { get; set; } = [];
        public bool ThrowOnList { get; set; }

        public IEnumerable<UIExtension> GetUIExtensions()
        {
            if (ThrowOnList)
            {
                throw new InvalidOperationException("synthetic failure");
            }

            return Extensions;
        }
    }

    private sealed class FakeAssetProvider : IAssetProvider
    {
        public IReadOnlyList<Asset> Assets { get; set; } = [];
        public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);
        public int ListAssetsCallCount { get; private set; }
        public int GetAssetStreamCallCount { get; private set; }

        public IEnumerable<Asset> ListAssets()
        {
            ListAssetsCallCount++;
            return Assets;
        }

        public Task<Stream?> GetAssetStreamAsync(
            string relativePath,
            CancellationToken cancellationToken = default)
        {
            GetAssetStreamCallCount++;
            if (Files.TryGetValue(relativePath, out var bytes))
            {
                return Task.FromResult<Stream?>(new MemoryStream(bytes));
            }

            return Task.FromResult<Stream?>(null);
        }
    }
}
