using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

using Magpie.Extensions.OpenLibrary;
using Magpie.Extensions.OpenLibrary.Mapping;
using Magpie.Extensions.OpenLibrary.QueryEngine;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

using RichardSzalay.MockHttp;

namespace Magpie.Test.Extensions.OpenLibrary;

[TestFixture]
public sealed class OpenLibraryClientTests
{
    [Test]
    public async Task GetJsonAsync_EscapedQueryAndHeaders_WhenRequestCarriesParameters()
    {
        // Arrange
        using var http = new MockHttpMessageHandler();
        var expectation = http.Expect(HttpMethod.Get, "https://openlibrary.org/search.json")
            .WithExactQueryString("q=title%3A%22Dune%20Messiah%22&limit=20")
            .WithHeaders("User-Agent", "magpie/1.0")
            .WithHeaders("Accept", "application/json")
            .Respond("application/json", """{"numFound":1,"numFoundExact":true,"docs":[]}""");
        var factory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        factory
            .Setup(value => value.CreateClient(OpenLibraryClient.HttpClientName))
            .Returns(() => new HttpClient(http, disposeHandler: false));
        var subject = new OpenLibraryClient(factory.Object, NullLogger<OpenLibraryClient>.Instance);

        // Act
        var response = await subject.GetJsonAsync<SearchResponseDto>(
            new ApiRequest
            {
                Kind = ApiRequestKind.Search,
                Path = "search.json",
                Parameters =
                [
                    new ApiRequestParameter("q", "title:\"Dune Messiah\""),
                    new ApiRequestParameter("limit", "20")
                ],
                CacheKey = "search"
            },
            new RequestContext { UserAgent = "magpie/1.0" },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response?.NumFound, Is.EqualTo(1));
            Assert.That(http.GetMatchCount(expectation), Is.EqualTo(1));
        }

        http.VerifyNoOutstandingExpectation();
    }

    [TestCase((int)ApiRequestKind.Author, "authors/OL1A.json")]
    [TestCase((int)ApiRequestKind.Edition, "books/OL9M.json")]
    public async Task GetJsonAsync_Null_WhenRecordLookupIsNotFound(int kind, string path)
    {
        // Arrange
        using var http = new MockHttpMessageHandler();
        http.Expect(HttpMethod.Get, "https://openlibrary.org/" + path).Respond(HttpStatusCode.NotFound);
        var factory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        factory
            .Setup(value => value.CreateClient(OpenLibraryClient.HttpClientName))
            .Returns(() => new HttpClient(http, disposeHandler: false));
        var subject = new OpenLibraryClient(factory.Object, NullLogger<OpenLibraryClient>.Instance);

        // Act
        var response = await subject.GetJsonAsync<AuthorDto>(
            new ApiRequest
            {
                Kind = (ApiRequestKind)kind,
                Path = path,
                Parameters = [],
                CacheKey = "record"
            },
            new RequestContext { UserAgent = "magpie/1.0" },
            default).ConfigureAwait(false);

        // Assert
        Assert.That(response, Is.Null);
        http.VerifyNoOutstandingExpectation();
    }

    [Test]
    public void GetJsonAsync_Throws_WhenSearchIsNotFound()
    {
        // Arrange
        using var http = new MockHttpMessageHandler();
        http.Expect(HttpMethod.Get, "https://openlibrary.org/search.json").Respond(HttpStatusCode.NotFound);
        var factory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        factory
            .Setup(value => value.CreateClient(OpenLibraryClient.HttpClientName))
            .Returns(() => new HttpClient(http, disposeHandler: false));
        var subject = new OpenLibraryClient(factory.Object, NullLogger<OpenLibraryClient>.Instance);

        // Act
        var request = async () => await subject.GetJsonAsync<SearchResponseDto>(
            new ApiRequest
            {
                Kind = ApiRequestKind.Search,
                Path = "search.json",
                Parameters = [],
                CacheKey = "search"
            },
            new RequestContext { UserAgent = "magpie/1.0" },
            default).ConfigureAwait(false);

        // Assert
        Assert.That(request, Throws.TypeOf<HttpRequestException>());
    }

    [Test]
    public void GetJsonAsync_Throws_WhenBodyIsMalformed()
    {
        // Arrange
        using var http = new MockHttpMessageHandler();
        http
            .Expect(HttpMethod.Get, "https://openlibrary.org/search.json")
            .Respond("application/json", "{ not json");
        var factory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        factory
            .Setup(value => value.CreateClient(OpenLibraryClient.HttpClientName))
            .Returns(() => new HttpClient(http, disposeHandler: false));
        var subject = new OpenLibraryClient(factory.Object, NullLogger<OpenLibraryClient>.Instance);

        // Act
        var request = async () => await subject.GetJsonAsync<SearchResponseDto>(
            new ApiRequest
            {
                Kind = ApiRequestKind.Search,
                Path = "search.json",
                Parameters = [],
                CacheKey = "search"
            },
            new RequestContext { UserAgent = "magpie/1.0" },
            default).ConfigureAwait(false);

        // Assert
        Assert.That(request, Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public async Task GetStatusAsync_StatusCode_WhenProbeCompletes()
    {
        // Arrange
        using var http = new MockHttpMessageHandler();
        http.Expect(HttpMethod.Get, "https://openlibrary.org/search.json").Respond(HttpStatusCode.ServiceUnavailable);
        var factory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        factory
            .Setup(value => value.CreateClient(OpenLibraryClient.HttpClientName))
            .Returns(() => new HttpClient(http, disposeHandler: false));
        var subject = new OpenLibraryClient(factory.Object, NullLogger<OpenLibraryClient>.Instance);

        // Act
        var status = await subject.GetStatusAsync(
            new ApiRequest
            {
                Kind = ApiRequestKind.Search,
                Path = "search.json",
                Parameters = [],
                CacheKey = "search"
            },
            new RequestContext { UserAgent = "magpie/1.0" },
            default).ConfigureAwait(false);

        // Assert
        Assert.That(status, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        http.VerifyNoOutstandingExpectation();
    }
}
