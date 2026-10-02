using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

using Magpie.Extensions.Jellyfin;
using Magpie.Extensions.Jellyfin.Models;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Caching;
using Magpie.Sdk.Integrations;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

using RichardSzalay.MockHttp;

namespace Magpie.Test.Extensions.Jellyfin;

[TestFixture]
public class JellyfinMediaServerIntegrationTests
{
    private static readonly IReadOnlyDictionary<string, string> ValidSettings =
        new Dictionary<string, string>
        {
            ["host"] = "http://jellyfin.local:8096",
            ["apiKey"] = "test-api-key"
        };

    private const string SystemInfoJson =
        "{\"ServerName\":\"test-server\",\"Version\":\"10.10.7\"," +
        "\"Id\":\"abc123\",\"OperatingSystem\":\"Linux\"}";

    private const string MoviesPath = "/data/movies";
    private const string TvPath = "/data/tv";

    private ICacheFactory _cacheFactory = null!;
    private MockHttpMessageHandler _handler = null!;
    private JellyfinMediaServerIntegration _integration = null!;

    [SetUp]
    public void SetUp()
    {
        var cacheFactory = new Mock<ICacheFactory>();
        var caches = new Dictionary<string, ICache>(StringComparer.Ordinal);
        cacheFactory.Setup(factory => factory.GetOrCreate(It.IsAny<string>(), It.IsAny<Func<CachePolicy>>()))
            .Returns<string, Func<CachePolicy>?>((name, _) =>
            {
                if (caches.TryGetValue(name, out var existing))
                {
                    return existing;
                }

                var values = new Dictionary<string, object?>(StringComparer.Ordinal);
                var cache = new Mock<ICache>();
                cache.Setup(value => value.Set(It.IsAny<string>(), It.IsAny<It.IsAnyType>()))
                    .Callback(new InvocationAction(invocation =>
                        values[(string)invocation.Arguments[0]] = invocation.Arguments[1]));
                cache.Setup(value => value.TryGet(It.IsAny<string>(), out It.Ref<It.IsAnyType?>.IsAny))
                    .Returns(new InvocationFunc(invocation =>
                    {
                        var found = values.TryGetValue((string)invocation.Arguments[0], out var value);
                        ((object?[])invocation.Arguments)[1] = value;
                        return found;
                    }));
                cache.Setup(value => value.TryGet(It.IsAny<string>(), out It.Ref<DeepLinkLookupResult>.IsAny))
                    .Returns(new InvocationFunc(invocation =>
                    {
                        var found = values.TryGetValue((string)invocation.Arguments[0], out var value);
                        ((object?[])invocation.Arguments)[1] = value ?? default(DeepLinkLookupResult);
                        return found;
                    }));
                cache.Setup(value => value.Remove(It.IsAny<string>())).Returns<string>(values.Remove);
                return caches[name] = cache.Object;
            });
        _cacheFactory = cacheFactory.Object;
        _handler = new MockHttpMessageHandler();
        _integration = new JellyfinMediaServerIntegration(
            _cacheFactory,
            NullLogger<JellyfinMediaServerIntegration>.Instance,
            new JellyfinClient(new HttpClient(_handler, false), NullLogger<JellyfinClient>.Instance));
    }

    [TearDown]
    public void TearDown()
    {
        _handler.Dispose();
    }

    [Test]
    public async Task TestConnectionAsync_ReturnsServerIdentity_WhenSettingsAreValid()
    {
        // Arrange
        var systemInfoRequest = _handler.Expect(HttpMethod.Get, "http://jellyfin.local:8096/System/Info")
            .WithHeaders("X-Emby-Token", "test-api-key")
            .Respond("application/json", SystemInfoJson);

        // Act
        var result = await _integration.TestConnectionAsync(ValidSettings, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.Message, Is.EqualTo("Connected to Jellyfin test-server 10.10.7"));
            Assert.That(result.FailureKind, Is.EqualTo(ConfigurationFailureKind.None));
            Assert.That(_handler.GetMatchCount(systemInfoRequest), Is.EqualTo(1));
        }

        _handler.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task TestConnectionAsync_ReturnsAuthenticationFailure_WhenUnauthorized()
    {
        // Arrange
        _handler.Expect(HttpMethod.Get, "http://jellyfin.local:8096/System/Info")
            .Respond(HttpStatusCode.Unauthorized);

        // Act
        var result = await _integration.TestConnectionAsync(ValidSettings, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Is.EqualTo("Authentication required or invalid API key"));
            Assert.That(result.FailureKind, Is.EqualTo(ConfigurationFailureKind.Auth));
        }
    }

    [Test]
    public async Task TestConnectionAsync_ReturnsAuthenticationFailure_WhenForbidden()
    {
        // Arrange
        _handler.Expect(HttpMethod.Get, "http://jellyfin.local:8096/System/Info")
            .Respond(HttpStatusCode.Forbidden);

        // Act
        var result = await _integration.TestConnectionAsync(ValidSettings, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Does.Contain("Authentication"));
            Assert.That(result.FailureKind, Is.EqualTo(ConfigurationFailureKind.Auth));
        }
    }

    [Test]
    public async Task TestConnectionAsync_ReturnsNetworkFailure_WhenRequestThrows()
    {
        // Arrange
        _handler.When(HttpMethod.Get, "http://jellyfin.local:8096/System/Info")
            .Throw(new HttpRequestException("connection refused"));

        // Act
        var result = await _integration.TestConnectionAsync(ValidSettings, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Does.Contain("connection refused"));
            Assert.That(result.FailureKind, Is.EqualTo(ConfigurationFailureKind.Fatal));
        }
    }

    [Test]
    public async Task TestConnectionAsync_ReturnsFailure_WhenHostIsMissing()
    {
        // Arrange
        var settings = new Dictionary<string, string> { ["apiKey"] = "k" };
        var integration = new JellyfinMediaServerIntegration(
            _cacheFactory,
            NullLogger<JellyfinMediaServerIntegration>.Instance,
            new JellyfinClient(
                new HttpClient(new MockHttpMessageHandler(), false),
                NullLogger<JellyfinClient>.Instance));

        // Act
        var result = await integration.TestConnectionAsync(settings, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Does.Contain("host"));
            Assert.That(result.FailureKind, Is.EqualTo(ConfigurationFailureKind.Fatal));
        }
    }

    [Test]
    public async Task TestConnectionAsync_ReturnsFailure_WhenApiKeyIsMissing()
    {
        // Arrange
        var settings = new Dictionary<string, string> { ["host"] = "http://jellyfin.local:8096" };
        var integration = new JellyfinMediaServerIntegration(
            _cacheFactory,
            NullLogger<JellyfinMediaServerIntegration>.Instance,
            new JellyfinClient(
                new HttpClient(new MockHttpMessageHandler(), false),
                NullLogger<JellyfinClient>.Instance));

        // Act
        var result = await integration.TestConnectionAsync(settings, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Does.Contain("apiKey"));
        }
    }

    [Test]
    public async Task TestConnectionAsync_ReturnsFailure_WhenHostIsNotAbsoluteHttpUrl()
    {
        // Arrange
        var settings = new Dictionary<string, string>
        {
            ["host"] = "not a url",
            ["apiKey"] = "k"
        };
        var integration = new JellyfinMediaServerIntegration(
            _cacheFactory,
            NullLogger<JellyfinMediaServerIntegration>.Instance,
            new JellyfinClient(
                new HttpClient(new MockHttpMessageHandler(), false),
                NullLogger<JellyfinClient>.Instance));

        // Act
        var result = await integration.TestConnectionAsync(settings, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Does.Contain("absolute http(s) URL"));
        }
    }

    [Test]
    public async Task RefreshForContentAsync_RefreshesAllLibraries_WhenNoMappingMatches()
    {
        // Arrange
        var refreshRequest = _handler.Expect(HttpMethod.Post, "http://jellyfin.local:8096/Library/Refresh")
            .WithHeaders("X-Emby-Token", ValidSettings["apiKey"])
            .Respond(HttpStatusCode.NoContent);

        var server = new MediaServer
        {
            Id = Guid.NewGuid(),
            Name = "Test Jellyfin",
            ProviderType = JellyfinMediaServerIntegration.ProviderTypeName,
            Settings = new Dictionary<string, string>(ValidSettings, StringComparer.Ordinal),
        };

        // Act
        var ok = await _integration.RefreshForContentAsync(
            server,
            new Dictionary<string, string>(),
            contentType: "movies",
            rootFolderPath: "/data/movies",
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ok, Is.True);
            Assert.That(_handler.GetMatchCount(refreshRequest), Is.EqualTo(1));
        }

        _handler.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task RefreshForContentAsync_ReturnsFalse_WhenServerReturnsNotFound()
    {
        // Arrange
        _handler.Expect(HttpMethod.Post, "http://jellyfin.local:8096/Library/Refresh")
            .Respond(HttpStatusCode.NotFound);

        var server = new MediaServer
        {
            Id = Guid.NewGuid(),
            Name = "Test Jellyfin",
            ProviderType = JellyfinMediaServerIntegration.ProviderTypeName,
            Settings = new Dictionary<string, string>(ValidSettings, StringComparer.Ordinal),
        };

        // Act
        var ok = await _integration.RefreshForContentAsync(
            server,
            new Dictionary<string, string>(),
            contentType: "movies",
            rootFolderPath: "/data/movies",
            default).ConfigureAwait(false);

        // Assert
        Assert.That(ok, Is.False);
    }

    [Test]
    public async Task RefreshForContentAsync_RefreshesMappedLibrary_WhenMappingMatchesContent()
    {
        // Arrange
        var refreshRequest = _handler.Expect(
            HttpMethod.Post,
            "http://jellyfin.local:8096/Items/lib-7/Refresh" +
            "?Recursive=true&ImageRefreshMode=Default&MetadataRefreshMode=Default")
                .Respond(HttpStatusCode.NoContent);

        var settings = new Dictionary<string, string>(ValidSettings)
        {
            [Constants.LibraryMappingsSettingKey] =
                "[{\"rootFolderPath\":\"/data/movies\"," +
                "\"contentType\":\"movies\"," +
                "\"serverLibraryId\":\"lib-7\"}]"
        };
        var server = new MediaServer
        {
            Id = Guid.NewGuid(),
            Name = "Test Jellyfin",
            ProviderType = JellyfinMediaServerIntegration.ProviderTypeName,
            Settings = new Dictionary<string, string>(settings, StringComparer.Ordinal),
        };

        // Act
        var ok = await _integration.RefreshForContentAsync(
            server,
            new Dictionary<string, string>(),
            contentType: "movies",
            rootFolderPath: "/data/movies",
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ok, Is.True);
            Assert.That(_handler.GetMatchCount(refreshRequest), Is.EqualTo(1));
        }

        _handler.VerifyNoOutstandingExpectation();
    }

    [Test]
    public void ToJellyfinSettings_Throws_WhenHostIsMissing()
    {
        // Arrange
        var settings = new Dictionary<string, string> { ["apiKey"] = "k" };

        // Act
        var ex = Assert.Throws<ArgumentException>(() => settings.ToJellyfinSettings());

        // Assert
        Assert.That(ex!.Message, Does.Contain("host"));
    }

    [Test]
    public void ToJellyfinSettings_Throws_WhenApiKeyIsMissing()
    {
        // Arrange
        var settings = new Dictionary<string, string> { ["host"] = "http://jellyfin.local:8096" };

        // Act
        var ex = Assert.Throws<ArgumentException>(() => settings.ToJellyfinSettings());

        // Assert
        Assert.That(ex!.Message, Does.Contain("apiKey"));
    }

    [Test]
    public void ToJellyfinSettings_Throws_WhenHostIsNotAbsoluteHttpUrl()
    {
        // Arrange
        var settings = new Dictionary<string, string>
        {
            ["host"] = "not a url",
            ["apiKey"] = "k"
        };

        // Act
        var ex = Assert.Throws<ArgumentException>(() => settings.ToJellyfinSettings());

        // Assert
        Assert.That(ex!.Message, Does.Contain("absolute http(s) URL"));
    }

    [TestCase("file:///etc/passwd")]
    [TestCase("ftp://jellyfin.local")]
    public void ToJellyfinSettings_Throws_WhenHostSchemeIsNotHttp(string host)
    {
        // Arrange
        var settings = new Dictionary<string, string>
        {
            ["host"] = host,
            ["apiKey"] = "k"
        };

        // Act
        var ex = Assert.Throws<ArgumentException>(() => settings.ToJellyfinSettings());

        // Assert
        Assert.That(ex!.Message, Does.Contain("absolute http(s) URL"));
    }

    [TestCase("http://jellyfin.local:8096", "http")]
    [TestCase("https://jellyfin.example.com", "https")]
    public void ToJellyfinSettings_ReturnsSettings_WhenHostUsesHttpOrHttps(string host, string expectedScheme)
    {
        // Arrange
        var settings = new Dictionary<string, string>
        {
            ["host"] = host,
            ["apiKey"] = "k"
        };

        // Act
        var parsed = settings.ToJellyfinSettings();

        // Assert
        Assert.That(parsed.Host.Scheme, Is.EqualTo(expectedScheme));
    }

    [Test]
    public void ToJellyfinSettings_AppendsTrailingSlash_WhenHostOmitsIt()
    {
        // Arrange
        var settings = new Dictionary<string, string>
        {
            ["host"] = "http://jellyfin.local:8096",
            ["apiKey"] = "k"
        };

        // Act
        var parsed = settings.ToJellyfinSettings();

        // Assert
        Assert.That(parsed.Host.AbsoluteUri, Is.EqualTo("http://jellyfin.local:8096/"));
    }

    [Test]
    public void ProviderType_ReturnsJellyfin_WhenRead()
    {
        // Arrange
        var integration = new JellyfinMediaServerIntegration(
            _cacheFactory,
            NullLogger<JellyfinMediaServerIntegration>.Instance,
            Mock.Of<IJellyfinClient>());

        // Act
        // Assert
        Assert.That(integration.ProviderType, Is.EqualTo("jellyfin"));
    }

    [Test]
    public async Task TryFindItemIdByExternalIdsAsync_ReturnsFirstItemId_WhenHappyPath()
    {
        // Arrange
        Uri? requestUri = null;
        var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Get, "http://jellyfin.local:8096/Items*")
            .With(request =>
            {
                requestUri = request.RequestUri;
                return true;
            })
            .Respond(
                "application/json",
                "{\"Items\":[{\"Id\":\"jf-1\",\"ProviderIds\":{\"Imdb\":\"tt1\"}}," +
                "{\"Id\":\"jf-2\",\"ProviderIds\":{\"Imdb\":\"tt2\"}}]}");

        var client = new JellyfinClient(
            new HttpClient(handler, disposeHandler: false),
            NullLogger<JellyfinClient>.Instance);

        // Act
        var result = await client.TryFindItemIdByExternalIdsAsync(
            ValidSettings.ToJellyfinSettings(),
            new Dictionary<string, string> { ["imdb"] = "tt1" },
            title: "Sample",
            parentLibraryId: null,
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ItemId, Is.EqualTo("jf-1"));
            var query = Uri.UnescapeDataString(requestUri!.PathAndQuery);
            Assert.That(query, Does.Contain("searchTerm=Sample"));
            Assert.That(query, Does.Contain("fields=ProviderIds"));
            Assert.That(query, Does.Not.Contain("parentId="));
        }

        handler.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task TryFindItemIdByExternalIdsAsync_ScopesLookupToLibrary_WhenWithParentLibraryId()
    {
        // Arrange
        Uri? requestUri = null;
        var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Get, "http://jellyfin.local:8096/Items*")
            .With(request =>
            {
                requestUri = request.RequestUri;
                return true;
            })
            .Respond(
                "application/json",
                "{\"Items\":[{\"Id\":\"jf-1\",\"ProviderIds\":{\"Imdb\":\"tt1\"}}]}");

        var client = new JellyfinClient(
            new HttpClient(handler, disposeHandler: false),
            NullLogger<JellyfinClient>.Instance);

        // Act
        var result = await client.TryFindItemIdByExternalIdsAsync(
            ValidSettings.ToJellyfinSettings(),
            new Dictionary<string, string> { ["imdb"] = "tt1" },
            title: "Sample",
            parentLibraryId: "lib-42",
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ItemId, Is.EqualTo("jf-1"));
            var query = Uri.UnescapeDataString(requestUri!.PathAndQuery);
            Assert.That(query, Does.Contain("parentId=lib-42"));
        }

        handler.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task TryFindItemIdByExternalIdsAsync_ReturnsNull_WhenNoMatches()
    {
        // Arrange
        var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Get, "http://jellyfin.local:8096/Items*")
            .Respond("application/json", "{\"Items\":[]}");

        var client = new JellyfinClient(
            new HttpClient(handler, disposeHandler: false),
            NullLogger<JellyfinClient>.Instance);

        // Act
        var result = await client.TryFindItemIdByExternalIdsAsync(
            ValidSettings.ToJellyfinSettings(),
            new Dictionary<string, string> { ["imdb"] = "tt404" },
            title: "Sample",
            parentLibraryId: null,
            default).ConfigureAwait(false);

        // Assert
        Assert.That(result.ItemId, Is.Null);
    }

    [Test]
    public async Task TryFindItemIdByExternalIdsAsync_ReturnsNull_WhenUpstreamErrorStatus()
    {
        // Arrange
        var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Get, "http://jellyfin.local:8096/Items*")
            .Respond(HttpStatusCode.InternalServerError);

        var client = new JellyfinClient(
            new HttpClient(handler, disposeHandler: false),
            NullLogger<JellyfinClient>.Instance);

        // Act
        var result = await client.TryFindItemIdByExternalIdsAsync(
            ValidSettings.ToJellyfinSettings(),
            new Dictionary<string, string> { ["imdb"] = "tt1" },
            title: "Sample",
            parentLibraryId: null,
            default).ConfigureAwait(false);

        // Assert
        Assert.That(result.ItemId, Is.Null);
    }

    [Test]
    public async Task TryFindItemIdByExternalIdsAsync_ReturnsNull_WhenMalformedJson()
    {
        // Arrange
        var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Get, "http://jellyfin.local:8096/Items*")
            .Respond("application/json", "not json");

        var client = new JellyfinClient(
            new HttpClient(handler, disposeHandler: false),
            NullLogger<JellyfinClient>.Instance);

        // Act
        var result = await client.TryFindItemIdByExternalIdsAsync(
            ValidSettings.ToJellyfinSettings(),
            new Dictionary<string, string> { ["imdb"] = "tt1" },
            title: "Sample",
            parentLibraryId: null,
            default).ConfigureAwait(false);

        // Assert
        Assert.That(result.ItemId, Is.Null);
    }

    [Test]
    public async Task TryFindItemIdByExternalIdsAsync_ReturnsNullWithoutHittingUpstream_WhenNoExternalIds()
    {
        // Arrange
        var handler = new MockHttpMessageHandler();
        var fallback = handler.Fallback.Respond(HttpStatusCode.OK);

        var client = new JellyfinClient(
            new HttpClient(handler, disposeHandler: false),
            NullLogger<JellyfinClient>.Instance);

        // Act
        var result = await client.TryFindItemIdByExternalIdsAsync(
            ValidSettings.ToJellyfinSettings(),
            new Dictionary<string, string> { ["custom"] = "   " },
            title: "Sample",
            parentLibraryId: null,
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ItemId, Is.Null);
            Assert.That(handler.GetMatchCount(fallback), Is.Zero);
        }
    }

    [TestCase("tvdb", "Tvdb", "8440385", "8440385")]
    [TestCase("imdb", "IMDB", "tt1", "tt1")]
    public async Task TryFindItemIdByExternalIdsAsync_ReturnsMatchingItemId_WhenProviderNameCaseDiffers(
        string magpieSource,
        string jellyfinProvider,
        string magpieValue,
        string jellyfinValue)
    {
        // Arrange
        var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Get, "http://jellyfin.local:8096/Items*")
            .Respond(
                "application/json",
                "{\"Items\":[{\"Id\":\"jf-1\",\"ProviderIds\":{\"" +
                jellyfinProvider +
                "\":\"" +
                jellyfinValue +
                "\"}}]}");

        var client = new JellyfinClient(
            new HttpClient(handler, disposeHandler: false),
            NullLogger<JellyfinClient>.Instance);

        // Act
        var result = await client.TryFindItemIdByExternalIdsAsync(
            ValidSettings.ToJellyfinSettings(),
            new Dictionary<string, string> { [magpieSource] = magpieValue },
            title: "Sample",
            parentLibraryId: null,
            default).ConfigureAwait(false);

        // Assert
        Assert.That(result.ItemId, Is.EqualTo("jf-1"));
        handler.VerifyNoOutstandingExpectation();
    }

    [TestCase("MusicArtist", "MusicBrainzArtist")]
    [TestCase("MusicAlbum", "MusicBrainzReleaseGroup")]
    [TestCase("Audio", "MusicBrainzRecording")]
    [TestCase("Audio", "MusicBrainzTrack")]
    public async Task TryFindItemIdByExternalIdsAsync_ReturnsMatchingItemId_WhenMusicBrainzIdIdentifiesItem(
        string itemType,
        string jellyfinProvider)
    {
        // Arrange
        var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Get, "http://jellyfin.local:8096/Items*")
            .Respond(
                "application/json",
                "{\"Items\":[{\"Id\":\"jf-1\",\"Type\":\"" +
                itemType +
                "\",\"ProviderIds\":{\"" +
                jellyfinProvider +
                "\":\"4cd13a09-ea6e-4eb5-9bc4-4b9141bdd3cd\"}}]}");

        var client = new JellyfinClient(
            new HttpClient(handler, disposeHandler: false),
            NullLogger<JellyfinClient>.Instance);

        // Act
        var result = await client.TryFindItemIdByExternalIdsAsync(
            ValidSettings.ToJellyfinSettings(),
            new Dictionary<string, string> { ["musicbrainz"] = "4cd13a09-ea6e-4eb5-9bc4-4b9141bdd3cd" },
            title: "Sample",
            parentLibraryId: null,
            default).ConfigureAwait(false);

        // Assert
        Assert.That(result.ItemId, Is.EqualTo("jf-1"));
        handler.VerifyNoOutstandingExpectation();
    }

    [TestCase(
        "{\"Items\":[{\"Id\":\"jf-artist\",\"Type\":\"MusicArtist\"," +
        "\"ProviderIds\":{\"MusicBrainzArtist\":\"4cb775ac-f1f6-4a5d-b7be-21a61116a550\"}}]}")]
    [TestCase(
        "{\"Items\":[{\"Id\":\"jf-track\",\"Type\":\"Audio\"," +
        "\"ProviderIds\":{\"MusicBrainzArtist\":\"4cd13a09-ea6e-4eb5-9bc4-4b9141bdd3cd\"}}]}")]
    [TestCase(
        "{\"Items\":[{\"Id\":\"jf-album\",\"Type\":\"MusicAlbum\"," +
        "\"ProviderIds\":{\"MusicBrainzAlbumArtist\":\"4cd13a09-ea6e-4eb5-9bc4-4b9141bdd3cd\"}}]}")]
    public async Task TryFindItemIdByExternalIdsAsync_ReturnsNull_WhenMusicBrainzIdDoesNotIdentifyItem(string body)
    {
        // Arrange
        var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Get, "http://jellyfin.local:8096/Items*")
            .Respond("application/json", body);

        var client = new JellyfinClient(
            new HttpClient(handler, disposeHandler: false),
            NullLogger<JellyfinClient>.Instance);

        // Act
        var result = await client.TryFindItemIdByExternalIdsAsync(
            ValidSettings.ToJellyfinSettings(),
            new Dictionary<string, string> { ["musicbrainz"] = "4cd13a09-ea6e-4eb5-9bc4-4b9141bdd3cd" },
            title: "Sample",
            parentLibraryId: null,
            default).ConfigureAwait(false);

        // Assert
        Assert.That(result.ItemId, Is.Null);
        handler.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task TryFindItemIdByExternalIdsAsync_ReturnsNull_WhenNetworkException()
    {
        // Arrange
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Get, "http://jellyfin.local:8096/Items*")
            .Throw(new HttpRequestException("connection refused"));

        var client = new JellyfinClient(
            new HttpClient(handler, disposeHandler: false),
            NullLogger<JellyfinClient>.Instance);

        // Act
        var result = await client.TryFindItemIdByExternalIdsAsync(
            ValidSettings.ToJellyfinSettings(),
            new Dictionary<string, string> { ["imdb"] = "tt1" },
            title: "Sample",
            parentLibraryId: null,
            default).ConfigureAwait(false);

        // Assert
        Assert.That(result.ItemId, Is.Null);
    }

    [Test]
    public async Task TryFindItemIdByExternalIdsAsync_ReturnsNull_WhenResponseProviderIdDoesNotMatch()
    {
        // Arrange
        var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Get, "http://jellyfin.local:8096/Items*")
            .Respond(
                "application/json",
                "{\"Items\":[" +
                "{\"Id\":\"jf-other-1\",\"ProviderIds\":{\"Tmdb\":\"999\"}}," +
                "{\"Id\":\"jf-other-2\",\"ProviderIds\":{\"Tmdb\":\"888\"}}]}");

        var client = new JellyfinClient(
            new HttpClient(handler, disposeHandler: false),
            NullLogger<JellyfinClient>.Instance);

        // Act
        var result = await client.TryFindItemIdByExternalIdsAsync(
            ValidSettings.ToJellyfinSettings(),
            new Dictionary<string, string> { ["tmdb"] = "584" },
            title: "Sample",
            parentLibraryId: null,
            default).ConfigureAwait(false);

        // Assert
        Assert.That(result.ItemId, Is.Null);
    }

    [Test]
    public async Task TryFindItemIdByExternalIdsAsync_ReturnsMatchingItemId_WhenEarlierItemDoesNotMatch()
    {
        // Arrange
        var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Get, "http://jellyfin.local:8096/Items*")
            .Respond(
                "application/json",
                "{\"Items\":[" +
                "{\"Id\":\"jf-other\",\"ProviderIds\":{\"Tmdb\":\"888\"}}," +
                "{\"Id\":\"jf-2f2f\",\"ProviderIds\":{\"Imdb\":\"tt0322259\",\"Tmdb\":\"584\"}}]}");

        var client = new JellyfinClient(
            new HttpClient(handler, disposeHandler: false),
            NullLogger<JellyfinClient>.Instance);

        // Act
        var result = await client.TryFindItemIdByExternalIdsAsync(
            ValidSettings.ToJellyfinSettings(),
            new Dictionary<string, string> { ["tmdb"] = "584" },
            title: "Sample",
            parentLibraryId: null,
            default).ConfigureAwait(false);

        // Assert
        Assert.That(result.ItemId, Is.EqualTo("jf-2f2f"));
    }

    [Test]
    public async Task BuildItemDeepLink_ReturnsDetailsUrl_WhenMappedAndPresent()
    {
        // Arrange
        _handler.When(HttpMethod.Get, "http://jellyfin.local:8096/Items*")
            .Respond(
                "application/json",
                "{\"Items\":[{\"Id\":\"abc-123\",\"ProviderIds\":{\"Imdb\":\"tt1234567\"}}]}");

        var server = new MediaServer
        {
            Id = Guid.NewGuid(),
            Name = "Test Jellyfin",
            ProviderType = JellyfinMediaServerIntegration.ProviderTypeName,
            Settings = new Dictionary<string, string>
            {
                ["host"] = "http://jellyfin.local:8096",
                ["apiKey"] = "test-key",
                [Constants.LibraryMappingsSettingKey] =
                    "[{\"rootFolderPath\":\"" +
                    MoviesPath +
                    "\"," +
                    "\"contentType\":\"movies\"," +
                    "\"serverLibraryId\":\"lib-movies\"}]"
            },
        };
        var ids = new Dictionary<string, string>
        {
            ["imdb"] = "tt1234567"
        };

        // Act
        var uri = await _integration.BuildItemDeepLinkAsync(
            server, ids, contentType: "movies", rootFolderPath: MoviesPath, title: "Sample", default)
                .ConfigureAwait(false);

        // Assert
        Assert.That(uri, Is.Not.Null);
        Assert.That(uri!.AbsoluteUri, Is.EqualTo("http://jellyfin.local:8096/web/index.html#!/details?id=abc-123"));
    }

    [Test]
    public async Task BuildItemDeepLink_ScopesLookupToMappedLibrary_WhenMappedAndPresent()
    {
        // Arrange
        var requests = new List<Uri>();
        _handler.When(HttpMethod.Get, "http://jellyfin.local:8096/Items*")
            .With(request =>
            {
                requests.Add(request.RequestUri!);
                return true;
            })
            .Respond(
                "application/json",
                "{\"Items\":[{\"Id\":\"abc-123\",\"ProviderIds\":{\"Imdb\":\"tt1234567\"}}]}");

        var server = new MediaServer
        {
            Id = Guid.NewGuid(),
            Name = "Test Jellyfin",
            ProviderType = JellyfinMediaServerIntegration.ProviderTypeName,
            Settings = new Dictionary<string, string>
            {
                ["host"] = "http://jellyfin.local:8096",
                ["apiKey"] = "test-key",
                [Constants.LibraryMappingsSettingKey] =
                    "[{\"rootFolderPath\":\"" +
                    MoviesPath +
                    "\"," +
                    "\"contentType\":\"movies\"," +
                    "\"serverLibraryId\":\"lib-7\"}]"
            },
        };
        var ids = new Dictionary<string, string>
        {
            ["imdb"] = "tt1234567"
        };

        // Act
        await _integration.BuildItemDeepLinkAsync(
            server,
            ids,
            contentType: "movies",
            rootFolderPath: MoviesPath,
            title: "Sample",
            default)
                .ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            var query = Uri.UnescapeDataString(requests.Last().PathAndQuery);
            Assert.That(query, Does.Contain("searchTerm="));
            Assert.That(query, Does.Contain("parentId=lib-7"));
        }
    }

    [Test]
    public async Task BuildItemDeepLinkAsync_ReturnsServerWideLink_WhenMappingsExistButNoneMatchAndItemExists()
    {
        // Arrange
        var requests = new List<Uri>();
        _handler.When(HttpMethod.Get, "http://jellyfin.local:8096/Items*")
            .With(request =>
            {
                requests.Add(request.RequestUri!);
                return true;
            })
            .Respond(
                "application/json",
                "{\"Items\":[{\"Id\":\"abc-123\",\"ProviderIds\":{\"Imdb\":\"tt1234567\"}}]}");

        var server = new MediaServer
        {
            Id = Guid.NewGuid(),
            Name = "Test Jellyfin",
            ProviderType = JellyfinMediaServerIntegration.ProviderTypeName,
            Settings = new Dictionary<string, string>
            {
                ["host"] = "http://jellyfin.local:8096",
                ["apiKey"] = "test-key",
                [Constants.LibraryMappingsSettingKey] =
                    "[{\"rootFolderPath\":\"" +
                    MoviesPath +
                    "\"," +
                    "\"contentType\":\"movies\"," +
                    "\"serverLibraryId\":\"lib-movies\"}]"
            },
        };
        var ids = new Dictionary<string, string>
        {
            ["imdb"] = "tt1234567"
        };

        // Act
        var uri = await _integration.BuildItemDeepLinkAsync(
            server, ids, contentType: "movies", rootFolderPath: "/elsewhere", title: "Sample", default)
                .ConfigureAwait(false);

        // Assert
        Assert.That(uri, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(uri!.AbsoluteUri, Is.EqualTo("http://jellyfin.local:8096/web/index.html#!/details?id=abc-123"));

            var query = Uri.UnescapeDataString(requests.Last().PathAndQuery);
            Assert.That(query, Does.Contain("searchTerm="));
            Assert.That(query, Does.Not.Contain("parentId="));
        }
    }

    [Test]
    public async Task BuildItemDeepLinkAsync_ReturnsNull_WhenMappingsExistButNoneMatchAndItemIsAbsent()
    {
        // Arrange
        var requests = new List<Uri>();
        _handler.When(HttpMethod.Get, "http://jellyfin.local:8096/Items*")
            .With(request =>
            {
                requests.Add(request.RequestUri!);
                return true;
            })
            .Respond("application/json", "{\"Items\":[]}");

        var server = new MediaServer
        {
            Id = Guid.NewGuid(),
            Name = "Test Jellyfin",
            ProviderType = JellyfinMediaServerIntegration.ProviderTypeName,
            Settings = new Dictionary<string, string>
            {
                ["host"] = "http://jellyfin.local:8096",
                ["apiKey"] = "test-key",
                [Constants.LibraryMappingsSettingKey] =
                    "[{\"rootFolderPath\":\"" +
                    MoviesPath +
                    "\"," +
                    "\"contentType\":\"movies\"," +
                    "\"serverLibraryId\":\"lib-movies\"}]"
            },
        };
        var ids = new Dictionary<string, string>
        {
            ["imdb"] = "tt1234567"
        };

        // Act
        var uri = await _integration.BuildItemDeepLinkAsync(
            server, ids, contentType: "movies", rootFolderPath: "/elsewhere", title: "Sample", default)
                .ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(uri, Is.Null);
            Assert.That(requests, Has.Count.EqualTo(1));
        }

        var query = Uri.UnescapeDataString(requests.Last().PathAndQuery);
        Assert.That(query, Does.Not.Contain("parentId="));
    }

    [Test]
    public async Task BuildItemDeepLinkAsync_ReturnsServerWideLink_WhenMappingsAreAbsentAndItemExists()
    {
        // Arrange
        var requests = new List<Uri>();
        _handler.When(HttpMethod.Get, "http://jellyfin.local:8096/Items*")
            .With(request =>
            {
                requests.Add(request.RequestUri!);
                return true;
            })
            .Respond(
                "application/json",
                "{\"Items\":[{\"Id\":\"abc-123\",\"ProviderIds\":{\"Imdb\":\"tt1234567\"}}]}");

        var server = new MediaServer
        {
            Id = Guid.NewGuid(),
            Name = "Test Jellyfin",
            ProviderType = JellyfinMediaServerIntegration.ProviderTypeName,
            Settings = new Dictionary<string, string>
            {
                ["host"] = "http://jellyfin.local:8096",
                ["apiKey"] = "test-key"
            },
        };
        var ids = new Dictionary<string, string>
        {
            ["imdb"] = "tt1234567"
        };

        // Act
        var uri = await _integration.BuildItemDeepLinkAsync(
            server, ids, contentType: "movies", rootFolderPath: MoviesPath, title: "Sample", default)
                .ConfigureAwait(false);

        // Assert
        Assert.That(uri, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(uri!.AbsoluteUri, Is.EqualTo("http://jellyfin.local:8096/web/index.html#!/details?id=abc-123"));

            var query = Uri.UnescapeDataString(requests.Last().PathAndQuery);
            Assert.That(query, Does.Contain("searchTerm="));
            Assert.That(query, Does.Not.Contain("parentId="));
        }
    }

    [Test]
    public async Task BuildItemDeepLinkAsync_ReturnsNull_WhenMappingsAreAbsentAndItemIsAbsent()
    {
        // Arrange
        var requests = new List<Uri>();
        _handler.When(HttpMethod.Get, "http://jellyfin.local:8096/Items*")
            .With(request =>
            {
                requests.Add(request.RequestUri!);
                return true;
            })
            .Respond("application/json", "{\"Items\":[]}");

        var server = new MediaServer
        {
            Id = Guid.NewGuid(),
            Name = "Test Jellyfin",
            ProviderType = JellyfinMediaServerIntegration.ProviderTypeName,
            Settings = new Dictionary<string, string>
            {
                ["host"] = "http://jellyfin.local:8096",
                ["apiKey"] = "test-key"
            },
        };
        var ids = new Dictionary<string, string>
        {
            ["imdb"] = "tt1234567"
        };

        // Act
        var uri = await _integration.BuildItemDeepLinkAsync(
            server, ids, contentType: "movies", rootFolderPath: MoviesPath, title: "Sample", default)
                .ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(uri, Is.Null);
            Assert.That(requests, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task BuildItemDeepLink_FallsBackServerWide_WhenUnparseableMappingsJson()
    {
        // Arrange
        var requests = new List<Uri>();
        _handler.When(HttpMethod.Get, "http://jellyfin.local:8096/Items*")
            .With(request =>
            {
                requests.Add(request.RequestUri!);
                return true;
            })
            .Respond(
                "application/json",
                "{\"Items\":[{\"Id\":\"abc-123\",\"ProviderIds\":{\"Imdb\":\"tt1234567\"}}]}");

        var server = new MediaServer
        {
            Id = Guid.NewGuid(),
            Name = "Test Jellyfin",
            ProviderType = JellyfinMediaServerIntegration.ProviderTypeName,
            Settings = new Dictionary<string, string>
            {
                ["host"] = "http://jellyfin.local:8096",
                ["apiKey"] = "test-key",
                [Constants.LibraryMappingsSettingKey] = "{ this is not valid json ["
            },
        };
        var ids = new Dictionary<string, string>
        {
            ["imdb"] = "tt1234567"
        };

        // Act
        var uri = await _integration.BuildItemDeepLinkAsync(
            server, ids, contentType: "movies", rootFolderPath: MoviesPath, title: "Sample", default)
                .ConfigureAwait(false);

        // Assert
        Assert.That(uri, Is.Not.Null);
        var query = Uri.UnescapeDataString(requests.Last().PathAndQuery);
        Assert.That(query, Does.Not.Contain("parentId="));
    }

    [Test]
    public async Task BuildItemDeepLinkAsync_UsesServerWideLookup_WhenMappingsExistButNoneMatch()
    {
        // Arrange
        _handler.When(HttpMethod.Get, "http://jellyfin.local:8096/Items*")
            .Respond(
                "application/json",
                "{\"Items\":[{\"Id\":\"abc-123\",\"ProviderIds\":{\"Imdb\":\"tt1234567\"}}]}");

        var server = new MediaServer
        {
            Id = Guid.NewGuid(),
            Name = "Test Jellyfin",
            ProviderType = JellyfinMediaServerIntegration.ProviderTypeName,
            Settings = new Dictionary<string, string>
            {
                ["host"] = "http://jellyfin.local:8096",
                ["apiKey"] = "test-key",
                [Constants.LibraryMappingsSettingKey] =
                    "[{\"rootFolderPath\":\"" +
                    MoviesPath +
                    "\"," +
                    "\"contentType\":\"movies\"," +
                    "\"serverLibraryId\":\"lib-movies\"}]"
            },
        };
        var ids = new Dictionary<string, string>
        {
            ["imdb"] = "tt1234567"
        };

        // Act
        var uri = await _integration.BuildItemDeepLinkAsync(
            server, ids, contentType: "movies", rootFolderPath: "/elsewhere", title: "Sample", default)
                .ConfigureAwait(false);

        // Assert
        Assert.That(uri, Is.Not.Null);
    }

    [Test]
    public async Task BuildItemDeepLink_ReturnsNull_WhenNotFoundOnServer()
    {
        // Arrange
        _handler.When(HttpMethod.Get, "http://jellyfin.local:8096/Items*")
            .Respond("application/json", "{\"Items\":[]}");

        var server = new MediaServer
        {
            Id = Guid.NewGuid(),
            Name = "Test Jellyfin",
            ProviderType = JellyfinMediaServerIntegration.ProviderTypeName,
            Settings = new Dictionary<string, string>
            {
                ["host"] = "http://jellyfin.local:8096",
                ["apiKey"] = "test-key",
                [Constants.LibraryMappingsSettingKey] =
                    "[{\"rootFolderPath\":\"" +
                    MoviesPath +
                    "\"," +
                    "\"contentType\":\"movies\"," +
                    "\"serverLibraryId\":\"lib-movies\"}]"
            },
        };
        var ids = new Dictionary<string, string>
        {
            ["imdb"] = "tt1234567"
        };

        // Act
        var uri = await _integration.BuildItemDeepLinkAsync(
            server, ids, contentType: "movies", rootFolderPath: MoviesPath, title: "Sample", default)
                .ConfigureAwait(false);

        // Assert
        Assert.That(uri, Is.Null);
    }

    [Test]
    public async Task BuildItemDeepLink_ReturnsNull_WhenLookupError()
    {
        // Arrange
        _handler.When(HttpMethod.Get, "http://jellyfin.local:8096/Items*")
            .Respond(HttpStatusCode.InternalServerError);

        var server = new MediaServer
        {
            Id = Guid.NewGuid(),
            Name = "Test Jellyfin",
            ProviderType = JellyfinMediaServerIntegration.ProviderTypeName,
            Settings = new Dictionary<string, string>
            {
                ["host"] = "http://jellyfin.local:8096",
                ["apiKey"] = "test-key",
                [Constants.LibraryMappingsSettingKey] =
                    "[{\"rootFolderPath\":\"" +
                    MoviesPath +
                    "\"," +
                    "\"contentType\":\"movies\"," +
                    "\"serverLibraryId\":\"lib-movies\"}]"
            },
        };
        var ids = new Dictionary<string, string>
        {
            ["imdb"] = "tt1234567"
        };

        // Act
        var uri = await _integration.BuildItemDeepLinkAsync(
            server, ids, contentType: "movies", rootFolderPath: MoviesPath, title: "Sample", default)
                .ConfigureAwait(false);

        // Assert
        Assert.That(uri, Is.Null);
    }

    [Test]
    public async Task BuildItemDeepLink_DistinguishesScopedFromServerWide_WhenCacheKey()
    {
        // Arrange
        var requests = new List<Uri>();
        _handler.When(HttpMethod.Get, "http://jellyfin.local:8096/Items*")
            .With(request =>
            {
                requests.Add(request.RequestUri!);
                return true;
            })
            .Respond(
                "application/json",
                "{\"Items\":[{\"Id\":\"abc-123\",\"ProviderIds\":{\"Imdb\":\"tt1234567\"}}]}");

        var ids = new Dictionary<string, string>
        {
            ["imdb"] = "tt1234567"
        };

        // Act
        var serverWide = new MediaServer
        {
            Id = Guid.NewGuid(),
            Name = "Test Jellyfin",
            ProviderType = JellyfinMediaServerIntegration.ProviderTypeName,
            Settings = new Dictionary<string, string>
            {
                ["host"] = "http://jellyfin.local:8096",
                ["apiKey"] = "test-key"
            },
        };
        await _integration.BuildItemDeepLinkAsync(
            serverWide,
            ids,
            contentType: "movies",
            rootFolderPath: MoviesPath,
            title: "Sample",
            default).ConfigureAwait(false);

        var scoped = new MediaServer
        {
            Id = serverWide.Id,
            Name = "Test Jellyfin",
            ProviderType = JellyfinMediaServerIntegration.ProviderTypeName,
            Settings = new Dictionary<string, string>
            {
                ["host"] = "http://jellyfin.local:8096",
                ["apiKey"] = "test-key",
                [Constants.LibraryMappingsSettingKey] =
                    "[{\"rootFolderPath\":\"" +
                    MoviesPath +
                    "\"," +
                    "\"contentType\":\"movies\"," +
                    "\"serverLibraryId\":\"lib-7\"}]"
            },
        };

        await _integration.BuildItemDeepLinkAsync(
            scoped,
            ids,
            contentType: "movies",
            rootFolderPath: MoviesPath,
            title: "Sample",
            default).ConfigureAwait(false);

        // Assert
        Assert.That(requests, Has.Count.EqualTo(2));
        var first = Uri.UnescapeDataString(requests[0].PathAndQuery);
        var second = Uri.UnescapeDataString(requests[1].PathAndQuery);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Does.Not.Contain("parentId="));
            Assert.That(second, Does.Contain("parentId=lib-7"));
        }
    }

    [Test]
    public async Task BuildItemDeepLinkAsync_UsesServerWideCacheKey_WhenMappingDoesNotMatch()
    {
        // Arrange
        var requests = new List<Uri>();
        _handler.When(HttpMethod.Get, "http://jellyfin.local:8096/Items*")
            .With(request =>
            {
                requests.Add(request.RequestUri!);
                return true;
            })
            .Respond(
                "application/json",
                "{\"Items\":[{\"Id\":\"abc-123\",\"ProviderIds\":{\"Imdb\":\"tt1234567\"}}]}");

        var ids = new Dictionary<string, string>
        {
            ["imdb"] = "tt1234567"
        };

        // Act
        var withMapping = new MediaServer
        {
            Id = Guid.NewGuid(),
            Name = "Test Jellyfin",
            ProviderType = JellyfinMediaServerIntegration.ProviderTypeName,
            Settings = new Dictionary<string, string>
            {
                ["host"] = "http://jellyfin.local:8096",
                ["apiKey"] = "test-key",
                [Constants.LibraryMappingsSettingKey] =
                    "[{\"rootFolderPath\":\"" +
                    MoviesPath +
                    "\"," +
                    "\"contentType\":\"movies\"," +
                    "\"serverLibraryId\":\"lib-7\"}]"
            },
        };
        await _integration.BuildItemDeepLinkAsync(
            withMapping,
            ids,
            contentType: "movies",
            rootFolderPath: "/elsewhere",
            title: "Sample",
            default).ConfigureAwait(false);

        var zeroMappings = new MediaServer
        {
            Id = withMapping.Id,
            Name = "Test Jellyfin",
            ProviderType = JellyfinMediaServerIntegration.ProviderTypeName,
            Settings = new Dictionary<string, string>
            {
                ["host"] = "http://jellyfin.local:8096",
                ["apiKey"] = "test-key"
            },
        };
        await _integration.BuildItemDeepLinkAsync(
            zeroMappings,
            ids,
            contentType: "movies",
            rootFolderPath: "/elsewhere",
            title: "Sample",
            default).ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(requests, Has.Count.EqualTo(1));
            var query = Uri.UnescapeDataString(requests[0].PathAndQuery);
            Assert.That(query, Does.Not.Contain("parentId="));
        }
    }

    [Test]
    public async Task BuildItemDeepLinkAsync_UsesMovieLibrary_WhenMultipleMappingsExistForMovie()
    {
        // Arrange
        var requests = new List<Uri>();
        _handler.When(HttpMethod.Get, "http://jellyfin.local:8096/Items*")
            .With(request =>
            {
                requests.Add(request.RequestUri!);
                return true;
            })
            .Respond(
                "application/json",
                "{\"Items\":[{\"Id\":\"abc-123\",\"ProviderIds\":{\"Imdb\":\"tt1234567\"}}]}");

        var server = new MediaServer
        {
            Id = Guid.NewGuid(),
            Name = "Test Jellyfin",
            ProviderType = JellyfinMediaServerIntegration.ProviderTypeName,
            Settings = new Dictionary<string, string>
            {
                ["host"] = "http://jellyfin.local:8096",
                ["apiKey"] = "test-key",
                [Constants.LibraryMappingsSettingKey] =
                    "[" +
                    "{\"rootFolderPath\":\"" +
                    MoviesPath +
                    "\"," +
                    "\"contentType\":\"movies\"," +
                    "\"serverLibraryId\":\"lib-movies\"}," +
                    "{\"rootFolderPath\":\"" +
                    TvPath +
                    "\"," +
                    "\"contentType\":\"tv\"," +
                    "\"serverLibraryId\":\"lib-tv\"}" +
                    "]"
            },
        };
        var ids = new Dictionary<string, string>
        {
            ["imdb"] = "tt1234567"
        };

        // Act
        await _integration.BuildItemDeepLinkAsync(
            server, ids, contentType: "movies", rootFolderPath: MoviesPath, title: "Sample", default)
                .ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            var query = Uri.UnescapeDataString(requests.Last().PathAndQuery);
            Assert.That(query, Does.Contain("parentId=lib-movies"));
            Assert.That(query, Does.Not.Contain("lib-tv"));
        }
    }

    [Test]
    public async Task BuildItemDeepLinkAsync_UsesCachedLink_WhenRequestRepeats()
    {
        // Arrange
        var requests = new List<Uri>();
        _handler.When(HttpMethod.Get, "http://jellyfin.local:8096/Items*")
            .With(request =>
            {
                requests.Add(request.RequestUri!);
                return true;
            })
            .Respond(
                "application/json",
                "{\"Items\":[{\"Id\":\"abc-123\",\"ProviderIds\":{\"Imdb\":\"tt1234567\"}}]}");

        var server = new MediaServer
        {
            Id = Guid.NewGuid(),
            Name = "Test Jellyfin",
            ProviderType = JellyfinMediaServerIntegration.ProviderTypeName,
            Settings = new Dictionary<string, string>
            {
                ["host"] = "http://jellyfin.local:8096",
                ["apiKey"] = "test-key",
                [Constants.LibraryMappingsSettingKey] =
                    "[{\"rootFolderPath\":\"" +
                    MoviesPath +
                    "\"," +
                    "\"contentType\":\"movies\"," +
                    "\"serverLibraryId\":\"lib-movies\"}]"
            },
        };
        var ids = new Dictionary<string, string>
        {
            ["imdb"] = "tt1234567"
        };

        // Act
        var first = await _integration.BuildItemDeepLinkAsync(
            server, ids, contentType: "movies", rootFolderPath: MoviesPath, title: "Sample", default)
                .ConfigureAwait(false);
        var second = await _integration.BuildItemDeepLinkAsync(
            server, ids, contentType: "movies", rootFolderPath: MoviesPath, title: "Sample", default)
                .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first!.AbsoluteUri, Is.EqualTo(second!.AbsoluteUri));
            Assert.That(requests, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task BuildItemDeepLinkAsync_RetriesLookup_WhenPreviousLookupFailed()
    {
        // Arrange
        var requests = new List<Uri>();
        _handler.Expect(HttpMethod.Get, "http://jellyfin.local:8096/Items*")
            .With(request =>
            {
                requests.Add(request.RequestUri!);
                return true;
            })
            .Respond(HttpStatusCode.InternalServerError);
        _handler.Expect(HttpMethod.Get, "http://jellyfin.local:8096/Items*")
            .With(request =>
            {
                requests.Add(request.RequestUri!);
                return true;
            })
            .Respond(
                "application/json",
                "{\"Items\":[{\"Id\":\"abc-123\",\"ProviderIds\":{\"Imdb\":\"tt1234567\"}}]}");

        var server = new MediaServer
        {
            Id = Guid.NewGuid(),
            Name = "Test Jellyfin",
            ProviderType = JellyfinMediaServerIntegration.ProviderTypeName,
            Settings = new Dictionary<string, string>
            {
                ["host"] = "http://jellyfin.local:8096",
                ["apiKey"] = "test-key",
                [Constants.LibraryMappingsSettingKey] =
                    "[{\"rootFolderPath\":\"" +
                    MoviesPath +
                    "\"," +
                    "\"contentType\":\"movies\"," +
                    "\"serverLibraryId\":\"lib-movies\"}]"
            },
        };
        var ids = new Dictionary<string, string>
        {
            ["imdb"] = "tt1234567"
        };

        // Act
        var first = await _integration.BuildItemDeepLinkAsync(
            server, ids, contentType: "movies", rootFolderPath: MoviesPath, title: "Sample", default)
                .ConfigureAwait(false);
        var second = await _integration.BuildItemDeepLinkAsync(
            server, ids, contentType: "movies", rootFolderPath: MoviesPath, title: "Sample", default)
                .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.Null);
            Assert.That(second, Is.Not.Null);
            Assert.That(
                second!.AbsoluteUri,
                Is.EqualTo("http://jellyfin.local:8096/web/index.html#!/details?id=abc-123"));
            Assert.That(requests, Has.Count.EqualTo(2));
        }

        _handler.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task BuildItemDeepLinkAsync_UsesCachedMiss_WhenItemWasNotFound()
    {
        // Arrange
        var requests = new List<Uri>();
        _handler.When(HttpMethod.Get, "http://jellyfin.local:8096/Items*")
            .With(request =>
            {
                requests.Add(request.RequestUri!);
                return true;
            })
            .Respond("application/json", "{\"Items\":[]}");

        var server = new MediaServer
        {
            Id = Guid.NewGuid(),
            Name = "Test Jellyfin",
            ProviderType = JellyfinMediaServerIntegration.ProviderTypeName,
            Settings = new Dictionary<string, string>
            {
                ["host"] = "http://jellyfin.local:8096",
                ["apiKey"] = "test-key",
                [Constants.LibraryMappingsSettingKey] =
                    "[{\"rootFolderPath\":\"" +
                    MoviesPath +
                    "\"," +
                    "\"contentType\":\"movies\"," +
                    "\"serverLibraryId\":\"lib-movies\"}]"
            },
        };
        var ids = new Dictionary<string, string>
        {
            ["imdb"] = "tt1234567"
        };

        // Act
        var first = await _integration.BuildItemDeepLinkAsync(
            server, ids, contentType: "movies", rootFolderPath: MoviesPath, title: "Sample", default)
                .ConfigureAwait(false);
        var second = await _integration.BuildItemDeepLinkAsync(
            server, ids, contentType: "movies", rootFolderPath: MoviesPath, title: "Sample", default)
                .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.Null);
            Assert.That(second, Is.Null);
            Assert.That(requests, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task BuildItemDeepLinkAsync_ReturnsNull_WhenExternalIdsAreEmpty()
    {
        // Arrange
        var fallback = _handler.Fallback.Respond(HttpStatusCode.OK);

        var server = new MediaServer
        {
            Id = Guid.NewGuid(),
            Name = "Test Jellyfin",
            ProviderType = JellyfinMediaServerIntegration.ProviderTypeName,
            Settings = new Dictionary<string, string>
            {
                ["host"] = "http://jellyfin.local:8096",
                ["apiKey"] = "test-key",
                [Constants.LibraryMappingsSettingKey] =
                    "[{\"rootFolderPath\":\"" +
                    MoviesPath +
                    "\"," +
                    "\"contentType\":\"movies\"," +
                    "\"serverLibraryId\":\"lib-movies\"}]"
            },
        };
        var ids = new Dictionary<string, string>();

        // Act
        var uri = await _integration.BuildItemDeepLinkAsync(
            server, ids, contentType: "movies", rootFolderPath: MoviesPath, title: "Sample", default)
                .ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(uri, Is.Null);
            Assert.That(_handler.GetMatchCount(fallback), Is.Zero);
        }
    }

    [Test]
    public async Task BuildItemDeepLinkAsync_ReturnsNull_WhenSettingsAreInvalid()
    {
        // Arrange

        var server = new MediaServer
        {
            Id = Guid.NewGuid(),
            Name = "Misconfigured",
            ProviderType = JellyfinMediaServerIntegration.ProviderTypeName,
            Settings = new Dictionary<string, string>
            {
                ["host"] = "http://jellyfin.local:8096"
            }
        };
        var ids = new Dictionary<string, string>
        {
            ["imdb"] = "tt1234567"
        };

        // Act
        var uri = await _integration.BuildItemDeepLinkAsync(
            server, ids, contentType: "movies", rootFolderPath: MoviesPath, title: "Sample", default)
            .ConfigureAwait(false);

        // Assert
        Assert.That(uri, Is.Null);
    }

    [Test]
    public void SettingsSchema_ReturnsExpectedKeys_WhenRead()
    {
        // Arrange
        var integration = new JellyfinMediaServerIntegration(
            _cacheFactory,
            NullLogger<JellyfinMediaServerIntegration>.Instance,
            Mock.Of<IJellyfinClient>());

        // Act
        var keys = integration.SettingsSchema.Select(d => d.Key).ToArray();

        // Assert
        Assert.That(keys, Is.EquivalentTo(["host", "apiKey", "libraryMappings"]));
    }

    [Test]
    public void SettingsSchema_ReturnsLibraryMappingRowSchema_WhenRead()
    {
        // Arrange
        var integration = new JellyfinMediaServerIntegration(
            _cacheFactory,
            NullLogger<JellyfinMediaServerIntegration>.Instance,
            Mock.Of<IJellyfinClient>());

        // Act
        var mappings = integration.SettingsSchema.Single(d => d.Key == "libraryMappings");
        var rowSchema = mappings.RowSchema!;
        var rootFolder = rowSchema.Single(field => field.Key == "rootFolderPath");
        var contentType = rowSchema.Single(field => field.Key == "contentType");
        var serverLibrary = rowSchema.Single(field => field.Key == "serverLibraryId");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(mappings.Type, Is.EqualTo(DataType.MappingsList));
            Assert.That(mappings.Required, Is.False);
            Assert.That(mappings.RowSchema, Is.Not.Null);
            Assert.That(
                rowSchema.Select(c => c.Key).ToArray(),
                Is.EquivalentTo(["rootFolderPath", "contentType", "serverLibraryId"]));
            Assert.That(rootFolder.OptionsSource!.Kind, Is.EqualTo(SettingOptionsSourceKind.RootFolders));
            Assert.That(contentType.OptionsSource!.Kind, Is.EqualTo(SettingOptionsSourceKind.ContentTypes));
            Assert.That(serverLibrary.OptionsSource!.Kind, Is.EqualTo(SettingOptionsSourceKind.JSFunction));
            Assert.That(
                serverLibrary.OptionsSource.FunctionName,
                Is.EqualTo(JellyfinMediaServerIntegration.LibraryOptionsFunctionName));
        }
    }

    [Test]
    public void SettingsSchema_ReturnsRequiredPublicHost_WhenRead()
    {
        // Arrange
        var integration = new JellyfinMediaServerIntegration(
            _cacheFactory,
            NullLogger<JellyfinMediaServerIntegration>.Instance,
            Mock.Of<IJellyfinClient>());

        // Act
        var host = integration.SettingsSchema.Single(d => d.Key == "host");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(host.Type, Is.EqualTo(DataType.String));
            Assert.That(host.Required, Is.True);
            Assert.That(host.Secret, Is.False);
        }
    }

    [Test]
    public void SettingsSchema_ReturnsRequiredSecretApiKey_WhenRead()
    {
        // Arrange
        var integration = new JellyfinMediaServerIntegration(
            _cacheFactory,
            NullLogger<JellyfinMediaServerIntegration>.Instance,
            Mock.Of<IJellyfinClient>());

        // Act
        var apiKey = integration.SettingsSchema.Single(d => d.Key == "apiKey");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(apiKey.Type, Is.EqualTo(DataType.String));
            Assert.That(apiKey.Required, Is.True);
            Assert.That(apiKey.Secret, Is.True);
        }
    }

    [Test]
    public async Task ValidateSettingsAsync_ReturnsNoIssues_WhenSettingsAreValid()
    {
        // Arrange
        var integration = new JellyfinMediaServerIntegration(
            _cacheFactory,
            NullLogger<JellyfinMediaServerIntegration>.Instance,
            Mock.Of<IJellyfinClient>());
        var settings = new Dictionary<string, string>
        {
            ["host"] = "http://localhost:8096",
            ["apiKey"] = "abc123"
        };

        // Act
        var result = await integration.ValidateSettingsAsync(settings, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Issues, Is.Empty);
        }
    }

    [Test]
    public async Task ValidateSettingsAsync_ReturnsValid_WhenHostUsesHttps()
    {
        // Arrange
        var integration = new JellyfinMediaServerIntegration(
            _cacheFactory,
            NullLogger<JellyfinMediaServerIntegration>.Instance,
            Mock.Of<IJellyfinClient>());
        var settings = new Dictionary<string, string>
        {
            ["host"] = "https://jellyfin.example.com",
            ["apiKey"] = "abc123"
        };

        // Act
        var result = await integration.ValidateSettingsAsync(settings, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.IsValid, Is.True);
    }

    [TestCase("not a url")]
    [TestCase("/relative/only")]
    [TestCase("ftp://jellyfin.example.com")]
    public async Task ValidateSettingsAsync_ReturnsHostIssue_WhenHostIsInvalid(string badHost)
    {
        // Arrange
        var integration = new JellyfinMediaServerIntegration(
            _cacheFactory,
            NullLogger<JellyfinMediaServerIntegration>.Instance,
            Mock.Of<IJellyfinClient>());
        var settings = new Dictionary<string, string>
        {
            ["host"] = badHost,
            ["apiKey"] = "abc123"
        };

        // Act
        var result = await integration.ValidateSettingsAsync(settings, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(
                result.Issues,
                Has.Some
                    .Property("Key").EqualTo("host")
                    .And.Property("Message").EqualTo("Host must be an absolute http(s) URL."));
        }
    }

    [Test]
    public async Task ValidateSettingsAsync_ReturnsNoIssue_WhenApiKeyIsMissing()
    {
        // Arrange
        var integration = new JellyfinMediaServerIntegration(
            _cacheFactory,
            NullLogger<JellyfinMediaServerIntegration>.Instance,
            Mock.Of<IJellyfinClient>());
        var settings = new Dictionary<string, string>
        {
            ["host"] = "http://localhost:8096"
        };

        // Act
        var result = await integration.ValidateSettingsAsync(settings, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Issues, Is.Empty);
        }
    }

    [Test]
    public async Task BuildItemDeepLinkAsync_HashesSettings_WhenCacheStoresResolution()
    {
        // Arrange
        var cache = new Mock<ICache>();
        var factory = new Mock<ICacheFactory>();
        factory.Setup(value => value.GetOrCreate(It.IsAny<string>(), It.IsAny<Func<CachePolicy>>()))
            .Returns(cache.Object);
        var keys = new List<string>();
        cache.Setup(value => value.Set(It.IsAny<string>(), It.IsAny<DeepLinkLookupResult>()))
            .Callback<string, DeepLinkLookupResult>((key, _) => keys.Add(key));
        var client = new Mock<IJellyfinClient>();
        client.Setup(value => value.TryFindItemIdByExternalIdsAsync(It.IsAny<JellyfinSettings>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(new DeepLinkLookupResult(
                "item", DeepLinkLookupStatus.Resolved));
        var integration = new JellyfinMediaServerIntegration(factory.Object,
            NullLogger<JellyfinMediaServerIntegration>.Instance, client.Object);
        var server = new MediaServer
        {
            Id = Guid.Empty,
            Name = "Server",
            ProviderType = "jellyfin",
            Settings = new Dictionary<string, string>
            {
                ["host"] = "https://jellyfin.test/",
                ["apiKey"] = "secret-one",
            },
        };

        // Act
        await integration.BuildItemDeepLinkAsync(server, new Dictionary<string, string> { ["catalog"] = "1" },
            "content", "/library", "Title", default).ConfigureAwait(false);
        server.Settings["apiKey"] = "secret-two";
        await integration.BuildItemDeepLinkAsync(server, new Dictionary<string, string> { ["catalog"] = "1" },
            "content", "/library", "Title", default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(keys, Has.Count.EqualTo(2));
            Assert.That(keys[0], Does.Not.Contain("secret-one"));
            Assert.That(keys[1], Does.Not.Contain("secret-two"));
            Assert.That(keys[0], Is.Not.EqualTo(keys[1]));
        }
    }
}
