using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Tmdb;
using Magpie.Extensions.Tmdb.QueryEngine;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

using RichardSzalay.MockHttp;

namespace Magpie.Test.Extensions.Tmdb;

[TestFixture]
public class TmdbClientTests
{
    private const string SecretKey = "super-secret-tmdb-key-do-not-leak";

    private TaskCompletionSource _reading = null!;
    private Mock<Stream> _body = null!;

    [SetUp]
    public void SetUp()
    {
        _reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _body = new Mock<Stream> { CallBase = true };
        _body.SetupGet(stream => stream.CanRead).Returns(true);
        _body
            .Setup(stream => stream.ReadAsync(
                It.IsAny<byte[]>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Returns(async Task<int> (byte[] _, int _, int _, CancellationToken cancellationToken) =>
            {
                _reading.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
                return 0;
            });
    }

    [Test]
    public void GetAsync_ThrowsWithStatusCode_WhenServerFails()
    {
        // Arrange
        using var http = new MockHttpMessageHandler();
        http.Expect(HttpMethod.Get, "https://api.themoviedb.org/3/movie/550*")
            .Respond(HttpStatusCode.InternalServerError, "text/plain", "server boom");
        var httpClient = http.ToHttpClient();
        httpClient.BaseAddress = new Uri(TmdbClient.DefaultBaseUrl, UriKind.Absolute);
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(factory => factory.CreateClient(TmdbClient.HttpClientName)).Returns(httpClient);
        var subject = new TmdbClient(httpClientFactory.Object, TimeProvider.System, NullLogger<TmdbClient>.Instance);
        var context = new RequestContext { ApiKey = SecretKey, Language = "en-US" };

        // Act
        var exception = Assert.ThrowsAsync<HttpRequestException>(async () => await subject.GetAsync<object>(
            context,
            "3/movie/550",
            queryParameters: null,
            ResponseProfile.Detail,
            default).ConfigureAwait(false));

        // Assert
        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        http.VerifyNoOutstandingExpectation();
    }

    [Test]
    public void GetAsync_PropagatesTransportException_WhenTransportFails()
    {
        // Arrange
        using var http = new MockHttpMessageHandler();
        http.Expect(HttpMethod.Get, "https://api.themoviedb.org/3/movie/550*")
            .Throw(new HttpRequestException(
                $"transport failed for https://api.themoviedb.org/3/movie/550?api_key={SecretKey}"));
        var httpClient = http.ToHttpClient();
        httpClient.BaseAddress = new Uri(TmdbClient.DefaultBaseUrl, UriKind.Absolute);
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(factory => factory.CreateClient(TmdbClient.HttpClientName)).Returns(httpClient);
        var subject = new TmdbClient(httpClientFactory.Object, TimeProvider.System, NullLogger<TmdbClient>.Instance);
        var context = new RequestContext { ApiKey = SecretKey, Language = "en-US" };

        // Act
        var exception = Assert.ThrowsAsync<HttpRequestException>(async () => await subject.GetAsync<object>(
            context,
            "3/movie/550",
            queryParameters: null,
            ResponseProfile.Detail,
            default).ConfigureAwait(false));

        // Assert
        Assert.That(exception!.Message, Does.Contain("api_key=super-secret-tmdb-key-do-not-leak"));
        http.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task GetAsync_ReturnsNull_WhenDetailProfileIsNotFound()
    {
        // Arrange
        using var http = new MockHttpMessageHandler();
        http.Expect(HttpMethod.Get, "https://api.themoviedb.org/3/movie/550*")
            .Respond(HttpStatusCode.NotFound);
        var httpClient = http.ToHttpClient();
        httpClient.BaseAddress = new Uri(TmdbClient.DefaultBaseUrl, UriKind.Absolute);
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(factory => factory.CreateClient(TmdbClient.HttpClientName)).Returns(httpClient);
        var subject = new TmdbClient(httpClientFactory.Object, TimeProvider.System, NullLogger<TmdbClient>.Instance);
        var context = new RequestContext { ApiKey = SecretKey, Language = "en-US" };

        // Act
        var detail = await subject.GetAsync<object>(
            context,
            "3/movie/550",
            queryParameters: null,
            ResponseProfile.Detail,
            default).ConfigureAwait(false);

        // Assert
        Assert.That(detail, Is.Null);
        http.VerifyNoOutstandingExpectation();
    }

    [Test]
    public void GetAsync_Throws_WhenQueryProfileIsNotFound()
    {
        // Arrange
        using var http = new MockHttpMessageHandler();
        http.Expect(HttpMethod.Get, "https://api.themoviedb.org/3/search/movie*")
            .Respond(HttpStatusCode.NotFound);
        var httpClient = http.ToHttpClient();
        httpClient.BaseAddress = new Uri(TmdbClient.DefaultBaseUrl, UriKind.Absolute);
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(factory => factory.CreateClient(TmdbClient.HttpClientName)).Returns(httpClient);
        var subject = new TmdbClient(httpClientFactory.Object, TimeProvider.System, NullLogger<TmdbClient>.Instance);
        var context = new RequestContext { ApiKey = SecretKey, Language = "en-US" };

        // Act
        // Assert
        Assert.ThrowsAsync<HttpRequestException>(async () => await subject.GetAsync<object>(
            context,
            "3/search/movie",
            queryParameters: null,
            ResponseProfile.Query,
            default).ConfigureAwait(false));
        http.VerifyNoOutstandingExpectation();
    }

    [Test]
    public void GetAsync_FailsBeforeSend_WhenRequestTargetIsOversized()
    {
        // Arrange
        using var http = new MockHttpMessageHandler();
        var httpClient = http.ToHttpClient();
        httpClient.BaseAddress = new Uri(TmdbClient.DefaultBaseUrl, UriKind.Absolute);
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(factory => factory.CreateClient(TmdbClient.HttpClientName)).Returns(httpClient);
        var subject = new TmdbClient(httpClientFactory.Object, TimeProvider.System, NullLogger<TmdbClient>.Instance);
        var context = new RequestContext { ApiKey = SecretKey, Language = "en-US" };

        // Act
        // Assert
        Assert.ThrowsAsync<InvalidOperationException>(async () => await subject.GetAsync<object>(
            context,
            "3/search/movie",
            [("query", new string('x', 9000))],
            ResponseProfile.Query,
            default).ConfigureAwait(false));
    }

    [Test]
    public void GetAsync_Throws_WhenAdvertisedBodyIsOversized()
    {
        // Arrange
        using var http = new MockHttpMessageHandler();
        http.Expect(HttpMethod.Get, "https://api.themoviedb.org/3/search/movie*")
            .Respond(_ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}")
                };
                response.Content.Headers.ContentLength = 9 * 1024 * 1024;
                return response;
            });
        var httpClient = http.ToHttpClient();
        httpClient.BaseAddress = new Uri(TmdbClient.DefaultBaseUrl, UriKind.Absolute);
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(factory => factory.CreateClient(TmdbClient.HttpClientName)).Returns(httpClient);
        var subject = new TmdbClient(httpClientFactory.Object, TimeProvider.System, NullLogger<TmdbClient>.Instance);
        var context = new RequestContext { ApiKey = SecretKey, Language = "en-US" };

        // Act
        // Assert
        Assert.ThrowsAsync<HttpRequestException>(async () => await subject.GetAsync<object>(
            context,
            "3/search/movie",
            queryParameters: null,
            ResponseProfile.Query,
            default).ConfigureAwait(false));
        http.VerifyNoOutstandingExpectation();
    }

    [Test]
    public void GetAsync_ThrowsMalformedResponse_WhenSuccessfulQueryBodyIsEmpty()
    {
        // Arrange
        using var http = new MockHttpMessageHandler();
        http.Expect(HttpMethod.Get, "https://api.themoviedb.org/3/search/movie*")
            .Respond(HttpStatusCode.OK, "application/json", "   ");
        var httpClient = http.ToHttpClient();
        httpClient.BaseAddress = new Uri(TmdbClient.DefaultBaseUrl, UriKind.Absolute);
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(factory => factory.CreateClient(TmdbClient.HttpClientName)).Returns(httpClient);
        var subject = new TmdbClient(httpClientFactory.Object, TimeProvider.System, NullLogger<TmdbClient>.Instance);
        var context = new RequestContext { ApiKey = SecretKey, Language = "en-US" };

        // Act
        var exception = Assert.ThrowsAsync<HttpRequestException>(async () => await subject.GetAsync<object>(
            context,
            "3/search/movie",
            queryParameters: null,
            ResponseProfile.Query,
            default).ConfigureAwait(false));

        // Assert
        Assert.That(exception!.Message, Does.Contain("malformed empty response body"));
        http.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task GetAsync_ThrowsCancellation_WhenTheBodyStallsForTheClientTimeout()
    {
        // Arrange
        using var http = new MockHttpMessageHandler();
        http.Expect(HttpMethod.Get, "https://api.themoviedb.org/3/configuration*")
            .Respond(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(_body.Object) });
        var httpClient = http.ToHttpClient();
        httpClient.BaseAddress = new Uri(TmdbClient.DefaultBaseUrl, UriKind.Absolute);
        httpClient.Timeout = TimeSpan.FromSeconds(30);
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(factory => factory.CreateClient(TmdbClient.HttpClientName)).Returns(httpClient);
        var timeProvider = new FakeTimeProvider();
        var subject = new TmdbClient(httpClientFactory.Object, timeProvider, NullLogger<TmdbClient>.Instance);
        var pending = subject.GetAsync<object>(
            new RequestContext { ApiKey = SecretKey, Language = "en-US" },
            "3/configuration",
            queryParameters: null,
            ResponseProfile.Configuration,
            CancellationToken.None);
        await _reading.Task.ConfigureAwait(false);

        // Act
        timeProvider.Advance(TimeSpan.FromSeconds(30));

        // Assert
        Assert.CatchAsync<OperationCanceledException>(async () =>
            await pending.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
    }
}
