using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

using Magpie.Extensions.Qbittorrent;
using Magpie.Sdk.Caching;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;
using Moq.Protected;

using NUnit.Framework;

using RichardSzalay.MockHttp;

namespace Magpie.Test.Extensions.Qbittorrent;

[TestFixture]
public class QbittorrentClientTests
{
    private const string Hash = "abcdef0123456789abcdef0123456789abcdef01";
    private const string Magnet = $"magnet:?xt=urn:btih:{Hash}&dn=test";
    private static readonly byte[] TorrentBytes = Encoding.ASCII.GetBytes("torrent bytes");

    private MockHttpMessageHandler _handler = null!;
    private HttpClient _http = null!;
    private IQbittorrentClient _client = null!;
    private Mock<ICache> _cache = null!;
    private ICacheFactory _cacheFactory = null!;
    private QbittorrentSettings _anonymousSettings = null!;
    private QbittorrentSettings _credentialSettings = null!;

    [SetUp]
    public void SetUp()
    {
        _handler = new MockHttpMessageHandler();
        _http = new HttpClient(_handler, disposeHandler: false);
        _cache = new Mock<ICache>();
        var factory = new Mock<ICacheFactory>();
        factory.Setup(cache => cache.GetOrCreate(It.IsAny<string>(), It.IsAny<Func<CachePolicy>>()))
            .Returns(_cache.Object);
        _cacheFactory = factory.Object;
        _client = new QbittorrentClient(_http, NullLogger<QbittorrentClient>.Instance, _cacheFactory);
        _anonymousSettings = QbittorrentSettings.Parse(new Dictionary<string, string>
        {
            ["host"] = "http://qbit.local:8080"
        });
        _credentialSettings = QbittorrentSettings.Parse(new Dictionary<string, string>
        {
            ["host"] = "http://qbit.local:8080",
            ["username"] = "admin",
            ["password"] = "secret"
        });
    }

    [TearDown]
    public void TearDown()
    {
        ((IDisposable)_client).Dispose();
        _http.Dispose();
        _handler.Dispose();
    }

    [Test]
    public async Task GetVersionAsync_Version_WhenWebUiIsAnonymous()
    {
        // Arrange
        var versionRequest = _handler.Expect(HttpMethod.Get, "http://qbit.local:8080/api/v2/app/version")
            .With(request => request.Headers.Referrer == new Uri("http://qbit.local:8080/"))
            .Respond("text/plain", " v4.6.0 ");
        var loginRequest = _handler.When(HttpMethod.Post, "http://qbit.local:8080/api/v2/auth/login")
            .Respond("text/plain", "Ok.");

        // Act
        var version = await _client.GetVersionAsync(_anonymousSettings, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(version, Is.EqualTo("v4.6.0"));
            Assert.That(_handler.GetMatchCount(versionRequest), Is.EqualTo(1));
            Assert.That(_handler.GetMatchCount(loginRequest), Is.Zero);
        }

        _handler.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task LoginAsync_True_WhenCredentialsAreAccepted()
    {
        // Arrange
        string? body = null;
        var loginRequest = _handler.Expect(HttpMethod.Post, "http://qbit.local:8080/api/v2/auth/login")
            .With(request =>
            {
                body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return true;
            })
            .Respond("text/plain", "Ok.");

        // Act
        var first = await _client.LoginAsync(_credentialSettings, default).ConfigureAwait(false);
        var second = await _client.LoginAsync(_credentialSettings, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.True);
            Assert.That(second, Is.True);
            Assert.That(body, Does.Contain("username=admin"));
            Assert.That(body, Does.Contain("password=secret"));
            Assert.That(_handler.GetMatchCount(loginRequest), Is.EqualTo(1));
        }

        _handler.VerifyNoOutstandingExpectation();
    }

    [TestCase(HttpStatusCode.OK, "Fails.")]
    [TestCase(HttpStatusCode.Forbidden, "")]
    public async Task LoginAsync_False_WhenCredentialsAreRejected(HttpStatusCode statusCode, string body)
    {
        // Arrange
        _handler.Expect(HttpMethod.Post, "http://qbit.local:8080/api/v2/auth/login")
            .Respond(statusCode, "text/plain", body);

        // Act
        var result = await _client.LoginAsync(_credentialSettings, default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
        _handler.VerifyNoOutstandingExpectation();
    }

    [TestCase(HttpStatusCode.Unauthorized)]
    [TestCase(HttpStatusCode.Forbidden)]
    public void GetVersionAsync_Throws_WhenAuthenticationIsRequired(HttpStatusCode statusCode)
    {
        // Arrange
        _handler.Expect(HttpMethod.Get, "http://qbit.local:8080/api/v2/app/version")
            .Respond(statusCode);

        // Act
        var exception = Assert.ThrowsAsync<QbittorrentAuthenticationException>(
            async () => await _client.GetVersionAsync(_anonymousSettings, default).ConfigureAwait(false));

        // Assert
        Assert.That(exception!.Message, Does.Contain(((int)statusCode).ToString(CultureInfo.InvariantCulture)));
        _handler.VerifyNoOutstandingExpectation();
    }

    [Test]
    public void GetVersionAsync_Throws_WhenRequestFails()
    {
        // Arrange
        _handler.Expect(HttpMethod.Get, "http://qbit.local:8080/api/v2/app/version")
            .Throw(new HttpRequestException("connection refused"));

        // Act
        var exception = Assert.ThrowsAsync<HttpRequestException>(
            async () => await _client.GetVersionAsync(_anonymousSettings, default).ConfigureAwait(false));

        // Assert
        Assert.That(exception!.Message, Does.Contain("connection refused"));
    }

    [Test]
    public async Task GetTorrentInfoAsync_Torrents_WhenResponseContainsJson()
    {
        // Arrange
        var request = _handler.Expect(
                HttpMethod.Get,
                $"http://qbit.local:8080/api/v2/torrents/info?hashes={Hash}")
            .Respond("application/json", $"[{{\"hash\":\"{Hash}\",\"name\":\"Release\"}}]");

        // Act
        var torrents = await _client.GetTorrentInfoAsync(_anonymousSettings, Hash, default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(torrents, Has.Count.EqualTo(1));
            Assert.That(torrents[0].Hash, Is.EqualTo(Hash));
            Assert.That(torrents[0].Name, Is.EqualTo("Release"));
            Assert.That(_handler.GetMatchCount(request), Is.EqualTo(1));
        }

        _handler.VerifyNoOutstandingExpectation();
    }

    [TestCase(HttpStatusCode.OK, true)]
    [TestCase(HttpStatusCode.NotFound, false)]
    public async Task DeleteAsync_Result_WhenWebUiReturnsStatus(HttpStatusCode statusCode, bool expected)
    {
        // Arrange
        string? body = null;
        _handler.Expect(HttpMethod.Post, "http://qbit.local:8080/api/v2/torrents/delete")
            .With(request =>
            {
                body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return true;
            })
            .Respond(statusCode);

        // Act
        var result = await _client.DeleteAsync(_anonymousSettings, Hash, deleteFiles: false, default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo(expected));
            Assert.That(body, Does.Contain($"hashes={Hash}"));
            Assert.That(body, Does.Contain("deleteFiles=false"));
        }

        _handler.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task SetForceStartAsync_True_WhenWebUiAcceptsRequest()
    {
        // Arrange
        string? body = null;
        _handler.Expect(HttpMethod.Post, "http://qbit.local:8080/api/v2/torrents/setForceStart")
            .With(request =>
            {
                body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return true;
            })
            .Respond(HttpStatusCode.OK);

        // Act
        var result = await _client.SetForceStartAsync(_anonymousSettings, Hash, true, default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(body, Does.Contain($"hashes={Hash}"));
            Assert.That(body, Does.Contain("value=true"));
        }

        _handler.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task DeleteAsync_RetriesOnce_WhenSessionExpires()
    {
        // Arrange
        var firstLoginRequest = _handler.Expect(HttpMethod.Post, "http://qbit.local:8080/api/v2/auth/login")
            .Respond("text/plain", "Ok.");
        var deleteRequest = _handler.Expect(HttpMethod.Post, "http://qbit.local:8080/api/v2/torrents/delete")
            .Respond(HttpStatusCode.Forbidden);
        var secondLoginRequest = _handler.Expect(HttpMethod.Post, "http://qbit.local:8080/api/v2/auth/login")
            .Respond("text/plain", "Ok.");
        var retryRequest = _handler.Expect(HttpMethod.Post, "http://qbit.local:8080/api/v2/torrents/delete")
            .Respond(HttpStatusCode.OK);

        // Act
        var result = await _client.DeleteAsync(_credentialSettings, Hash, true, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(_handler.GetMatchCount(firstLoginRequest), Is.EqualTo(1));
            Assert.That(_handler.GetMatchCount(secondLoginRequest), Is.EqualTo(1));
            Assert.That(_handler.GetMatchCount(deleteRequest), Is.EqualTo(1));
            Assert.That(_handler.GetMatchCount(retryRequest), Is.EqualTo(1));
        }

        _handler.VerifyNoOutstandingExpectation();
    }

    [Test]
    public void DeleteAsync_ThrowsAuthenticationFailure_WhenReloginIsForbidden()
    {
        // Arrange
        _handler.Expect(HttpMethod.Post, "http://qbit.local:8080/api/v2/auth/login")
            .Respond("text/plain", "Ok.");
        _handler.Expect(HttpMethod.Post, "http://qbit.local:8080/api/v2/torrents/delete")
            .Respond(HttpStatusCode.Forbidden);
        _handler.Expect(HttpMethod.Post, "http://qbit.local:8080/api/v2/auth/login")
            .Respond(HttpStatusCode.Forbidden);

        // Act
        var action = async () => await _client.DeleteAsync(_credentialSettings, Hash, true, default)
            .ConfigureAwait(false);

        // Assert
        Assert.That(action, Throws.TypeOf<QbittorrentAuthenticationException>());
        _handler.VerifyNoOutstandingExpectation();
    }

    [TestCase("v4.6.7", "pause")]
    [TestCase("4.5.2", "pause")]
    [TestCase("v5.0.0", "stop")]
    [TestCase("v5.0.0rc1", "stop")]
    [TestCase("not-a-version", "pause")]
    [TestCase("", "pause")]
    public async Task PauseAsync_Endpoint_WhenVersionIsResolved(string version, string endpoint)
    {
        // Arrange
        _handler.Expect(HttpMethod.Get, "http://qbit.local:8080/api/v2/app/version")
            .Respond("text/plain", version);
        _handler.Expect(HttpMethod.Post, $"http://qbit.local:8080/api/v2/torrents/{endpoint}")
            .Respond(HttpStatusCode.OK);

        // Act
        var result = await _client.PauseAsync(_anonymousSettings, Hash, default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);
        _handler.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task PauseAsync_RetriesVersionProbe_WhenTransientFailure()
    {
        // Arrange
        _handler.Expect(HttpMethod.Get, "http://qbit.local:8080/api/v2/app/version")
            .Throw(new HttpRequestException("temporary"));
        _handler.Expect(HttpMethod.Post, "http://qbit.local:8080/api/v2/torrents/pause")
            .Respond(HttpStatusCode.OK);
        _handler.Expect(HttpMethod.Get, "http://qbit.local:8080/api/v2/app/version")
            .Respond("text/plain", "v5.0.0");
        _handler.Expect(HttpMethod.Post, "http://qbit.local:8080/api/v2/torrents/stop")
            .Respond(HttpStatusCode.OK);

        // Act
        var first = await _client.PauseAsync(_anonymousSettings, Hash, default).ConfigureAwait(false);
        var second = await _client.PauseAsync(_anonymousSettings, Hash, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.True);
            Assert.That(second, Is.True);
        }

        _handler.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task ResumeAsync_StartEndpoint_WhenVersionIsFive()
    {
        // Arrange
        _handler.Expect(HttpMethod.Get, "http://qbit.local:8080/api/v2/app/version")
            .Respond("text/plain", "v5.0.0");
        _handler.Expect(HttpMethod.Post, "http://qbit.local:8080/api/v2/torrents/start")
            .Respond(HttpStatusCode.OK);

        // Act
        var result = await _client.ResumeAsync(_anonymousSettings, Hash, default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);
        _handler.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task AddMagnetAsync_Fields_WhenSettingsAndTagsAreSupplied()
    {
        // Arrange
        var settings = QbittorrentSettings.Parse(new Dictionary<string, string>
        {
            ["host"] = "http://qbit.local:8080",
            ["category"] = "custom",
            ["paused"] = "true"
        });
        string? urls = null;
        string? category = null;
        string? tags = null;
        string? paused = null;
        var hasTorrents = true;
        _handler.Expect(HttpMethod.Post, "http://qbit.local:8080/api/v2/torrents/add")
            .With(request =>
            {
                var parts = ((MultipartFormDataContent)request.Content!).ToArray();
                urls = parts.Single(part => part.Headers.ContentDisposition?.Name?.Trim('"') == "urls")
                    .ReadAsStringAsync().GetAwaiter().GetResult();
                category = parts.Single(part => part.Headers.ContentDisposition?.Name?.Trim('"') == "category")
                    .ReadAsStringAsync().GetAwaiter().GetResult();
                tags = parts.Single(part => part.Headers.ContentDisposition?.Name?.Trim('"') == "tags")
                    .ReadAsStringAsync().GetAwaiter().GetResult();
                paused = parts.Single(part => part.Headers.ContentDisposition?.Name?.Trim('"') == "paused")
                    .ReadAsStringAsync().GetAwaiter().GetResult();
                hasTorrents = parts.Any(part => part.Headers.ContentDisposition?.Name?.Trim('"') == "torrents");
                return true;
            })
            .Respond("text/plain", "Ok.");

        // Act
        await _client.AddMagnetAsync(settings, Magnet, "movie", default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(urls, Is.EqualTo(Magnet));
            Assert.That(category, Is.EqualTo("custom"));
            Assert.That(tags, Is.EqualTo("movie"));
            Assert.That(paused, Is.EqualTo("true"));
            Assert.That(hasTorrents, Is.False);
        }

        _handler.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task AddTorrentFileAsync_FilePart_WhenTorrentIsSupplied()
    {
        // Arrange
        byte[]? bytes = null;
        string? fileName = null;
        string? contentType = null;
        var hasUrls = true;
        _handler.Expect(HttpMethod.Post, "http://qbit.local:8080/api/v2/torrents/add")
            .With(request =>
            {
                var parts = ((MultipartFormDataContent)request.Content!).ToArray();
                var torrent = parts.Single(part => part.Headers.ContentDisposition?.Name?.Trim('"') == "torrents");
                bytes = torrent.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                fileName = torrent.Headers.ContentDisposition!.FileName!.Trim('"');
                contentType = torrent.Headers.ContentType!.MediaType;
                hasUrls = parts.Any(part => part.Headers.ContentDisposition?.Name?.Trim('"') == "urls");
                return true;
            })
            .Respond("text/plain", "Ok.");

        // Act
        await _client.AddTorrentFileAsync(
            _anonymousSettings,
            TorrentBytes,
            "release.torrent",
            tags: null,
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(bytes, Is.EqualTo(TorrentBytes));
            Assert.That(fileName, Is.EqualTo("release.torrent"));
            Assert.That(contentType, Is.EqualTo("application/x-bittorrent"));
            Assert.That(hasUrls, Is.False);
        }

        _handler.VerifyNoOutstandingExpectation();
    }

    [TestCase("Ok.")]
    [TestCase("{\"success_count\":1,\"failure_count\":0}")]
    public void AddMagnetAsync_Completes_WhenWebUiAcceptsBody(string body)
    {
        // Arrange
        _handler.Expect(HttpMethod.Post, "http://qbit.local:8080/api/v2/torrents/add")
            .Respond("text/plain", body);

        // Act
        Assert.DoesNotThrowAsync(
            async () => await _client.AddMagnetAsync(_anonymousSettings, Magnet, null, default)
                .ConfigureAwait(false));

        // Assert
        _handler.VerifyNoOutstandingExpectation();
    }

    [TestCase("Fails.")]
    [TestCase("{\"success_count\":0,\"failure_count\":1,\"added_torrent_ids\":[]}")]
    public void AddMagnetAsync_Throws_WhenWebUiRejectsBody(string body)
    {
        // Arrange
        _handler.Expect(HttpMethod.Post, "http://qbit.local:8080/api/v2/torrents/add")
            .Respond("text/plain", body);

        // Act
        var exception = Assert.ThrowsAsync<QbittorrentAddRejectedException>(
            async () => await _client.AddMagnetAsync(_anonymousSettings, Magnet, null, default)
                .ConfigureAwait(false));

        // Assert
        Assert.That(exception!.ResponseBody, Is.EqualTo(body));
        _handler.VerifyNoOutstandingExpectation();
    }

    [Test]
    public void AddTorrentFileAsync_Throws_WhenWebUiRejectsMediaType()
    {
        // Arrange
        _handler.Expect(HttpMethod.Post, "http://qbit.local:8080/api/v2/torrents/add")
            .Respond(HttpStatusCode.UnsupportedMediaType);

        // Act
        var exception = Assert.ThrowsAsync<QbittorrentAddRejectedException>(
            async () => await _client.AddTorrentFileAsync(
                _anonymousSettings,
                TorrentBytes,
                "release.torrent",
                null,
                default).ConfigureAwait(false));

        // Assert
        Assert.That(exception!.Message, Does.Contain("HTTP 415"));
        _handler.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task FindTorrentByNameAsync_Match_WhenNamesAreEqual()
    {
        // Arrange
        _handler.Expect(HttpMethod.Get, "http://qbit.local:8080/api/v2/torrents/info")
            .Respond(
                "application/json",
                $"[{{\"hash\":\"other\",\"name\":\"Other\"}},{{\"hash\":\"{Hash}\",\"name\":\"Release\"}}]");

        // Act
        var torrent = await _client.FindTorrentByNameAsync(_anonymousSettings, "Release", default)
            .ConfigureAwait(false);

        // Assert
        Assert.That(torrent!.Hash, Is.EqualTo(Hash));
        _handler.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task GetVersionAsync_RoutesRequests_WhenSettingsDiffer()
    {
        // Arrange
        var otherSettings = QbittorrentSettings.Parse(new Dictionary<string, string>
        {
            ["host"] = "http://other.local:9090/base/"
        });
        _handler.Expect(HttpMethod.Get, "http://qbit.local:8080/api/v2/app/version")
            .Respond("text/plain", "v4.6.0");
        _handler.Expect(HttpMethod.Get, "http://other.local:9090/base/api/v2/app/version")
            .Respond("text/plain", "v5.0.0");

        // Act
        var first = await _client.GetVersionAsync(_anonymousSettings, default).ConfigureAwait(false);
        var second = await _client.GetVersionAsync(otherSettings, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.EqualTo("v4.6.0"));
            Assert.That(second, Is.EqualTo("v5.0.0"));
        }

        _handler.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task PauseAsync_ReusesVersionAcrossClients_WhenServerWasAlreadyProbed()
    {
        // Arrange
        var handler = new Moq.Mock<HttpMessageHandler>();
        handler.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<System.Threading.CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, System.Threading.CancellationToken _) =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(request.RequestUri!.AbsolutePath.EndsWith(
                        "version", StringComparison.Ordinal)
                        ? "v5.0.0" : "Ok."),
                });
        using var http = new HttpClient(handler.Object);
        using var first = new QbittorrentClient(http, NullLogger<QbittorrentClient>.Instance, _cacheFactory);
        using var second = new QbittorrentClient(http, NullLogger<QbittorrentClient>.Instance, _cacheFactory);

        var savedKey = string.Empty;
        _cache.Setup(cache => cache.Set(It.IsAny<string>(), true))
            .Callback<string, bool>((key, _) => savedKey = key);

        // Act
        await first.PauseAsync(_anonymousSettings, Hash, default).ConfigureAwait(false);
        var cachedVersion = true;
        _cache.Setup(cache => cache.TryGet(savedKey, out cachedVersion)).Returns(true);
        await second.ResumeAsync(_anonymousSettings, Hash, default).ConfigureAwait(false);

        // Assert
        Assert.That(savedKey, Is.Not.Empty);
        handler.Protected().Verify("SendAsync", Moq.Times.Once(),
            ItExpr.Is<HttpRequestMessage>(request => request.RequestUri!.AbsolutePath == "/api/v2/app/version"),
            ItExpr.IsAny<System.Threading.CancellationToken>());
        handler.Protected().Verify("SendAsync", Moq.Times.Once(),
            ItExpr.Is<HttpRequestMessage>(request => request.RequestUri!.AbsolutePath == "/api/v2/torrents/start"),
            ItExpr.IsAny<System.Threading.CancellationToken>());
    }

    [TestCase(HttpStatusCode.OK, "Fails.")]
    [TestCase(HttpStatusCode.Unauthorized, "")]
    [TestCase(HttpStatusCode.Forbidden, "")]
    public void GetTorrentInfoAsync_ThrowsAuthenticationFailure_WhenLoginIsRejected(HttpStatusCode status, string body)
    {
        // Arrange
        var handler = new Moq.Mock<HttpMessageHandler>();
        handler.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<System.Threading.CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage(status) { Content = new StringContent(body) });
        using var http = new HttpClient(handler.Object);
        using var client = new QbittorrentClient(http, NullLogger<QbittorrentClient>.Instance, _cacheFactory);

        // Act
        var action = async () => await client.GetTorrentInfoAsync(_credentialSettings, Hash, default)
            .ConfigureAwait(false);

        // Assert
        Assert.That(action, Throws.TypeOf<QbittorrentAuthenticationException>());
        handler.Protected().Verify("SendAsync", Moq.Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<System.Threading.CancellationToken>());
    }
}
