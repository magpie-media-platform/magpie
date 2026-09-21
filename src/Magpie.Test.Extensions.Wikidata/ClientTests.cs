using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Wikidata;
using Magpie.Extensions.Wikidata.QueryEngine;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

using RichardSzalay.MockHttp;

namespace Magpie.Test.Extensions.Wikidata;

[TestFixture]
internal sealed class ClientTests
{
    [Test]
    public void SearchEntitiesAsync_FailsAfterTenPages_WhenContinuationNeverEnds()
    {
        // Arrange
        var pages = 0;
        using var http = new MockHttpMessageHandler();
        http.When(HttpMethod.Get, WikidataClient.DefaultApiUrl + "*")
            .Respond(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"search\":[{\"id\":\"Q42\"}],\"search-continue\":" + ++pages + "}")
            });
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(value => value.CreateClient(WikidataClient.HttpClientName))
            .Returns(() => new HttpClient(http, disposeHandler: false));
        var subject = new WikidataClient(factory.Object, TimeProvider.System, NullLogger<WikidataClient>.Instance);
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };

        // Act
        var failure = Assert.ThrowsAsync<WikidataBudgetException>(async () =>
            await subject.SearchEntitiesAsync("common name", context, default).ConfigureAwait(false));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure, Is.TypeOf<WikidataBudgetException>());
            Assert.That(
                failure!.Message,
                Is.EqualTo("Wikidata entity search scanned too many pages without finishing."));
            Assert.That(pages, Is.EqualTo(10));
        }
    }

    [Test]
    public async Task GetEntitiesAsync_UsesBatchParametersAndHeaders_WhenRequest()
    {
        // Arrange
        string? requestUri = null;
        string? userAgent = null;
        using var http = new MockHttpMessageHandler();
        http.Expect(HttpMethod.Get, WikidataClient.DefaultApiUrl + "*")
            .Respond(request =>
            {
                requestUri = request.RequestUri?.AbsoluteUri;
                userAgent = string.Join(" ", request.Headers.GetValues("User-Agent"));
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"entities\":{\"Q1\":{\"id\":\"Q1\"}}}")
                };
            });
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory
            .Setup(factory => factory.CreateClient(WikidataClient.HttpClientName))
            .Returns(() => new HttpClient(http, disposeHandler: false));
        IWikidataClient subject = new WikidataClient(
            httpClientFactory.Object,
            TimeProvider.System,
            NullLogger<WikidataClient>.Instance);
        var context = new RequestContext
        {
            Language = "fr",
            FallbackLanguages = ["fr", "en"],
            UserAgent = "Magpie/test (contact)"
        };

        // Act
        var result = await subject.GetEntitiesAsync(["Q1"], context, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Keys, Is.EquivalentTo(["Q1"]));
            Assert.That(requestUri, Does.Contain("action=wbgetentities"));
            Assert.That(requestUri, Does.Contain("props=labels%7Cdescriptions%7Caliases%7Cclaims"));
            Assert.That(requestUri, Does.Contain("languages=fr%7Cen"));
            Assert.That(requestUri, Does.Contain("languagefallback=1"));
            Assert.That(requestUri, Does.Contain("redirects=yes"));
            Assert.That(requestUri, Does.Contain("formatversion=2"));
            Assert.That(requestUri, Does.Contain("maxlag=5"));
            Assert.That(userAgent, Is.EqualTo("Magpie/test (contact)"));
        }

        http.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task GetEntitiesAsync_OmitsLanguageRestriction_WhenFetchAllLanguages()
    {
        // Arrange
        string? requestUri = null;
        using var http = new MockHttpMessageHandler();
        http.Expect(HttpMethod.Get, WikidataClient.DefaultApiUrl + "*")
            .Respond(request =>
            {
                requestUri = request.RequestUri?.AbsoluteUri;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"entities\":{\"Q1\":{\"id\":\"Q1\"}}}")
                };
            });
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory
            .Setup(factory => factory.CreateClient(WikidataClient.HttpClientName))
            .Returns(() => new HttpClient(http, disposeHandler: false));
        IWikidataClient subject = new WikidataClient(
            httpClientFactory.Object,
            TimeProvider.System,
            NullLogger<WikidataClient>.Instance);
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test",
            FetchAllLanguages = true
        };

        // Act
        await subject.GetEntitiesAsync(["Q1"], context, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(requestUri, Does.Not.Contain("languages="));
            Assert.That(requestUri, Does.Not.Contain("languagefallback="));
        }

        http.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task SearchEntitiesAsync_ReturnsStableDistinctQidsFromEveryPage_WhenContinuation()
    {
        // Arrange
        var requestUris = new List<string>();
        using var http = new MockHttpMessageHandler();
        http.Expect(HttpMethod.Get, WikidataClient.DefaultApiUrl + "*")
            .Respond(request =>
            {
                requestUris.Add(request.RequestUri?.AbsoluteUri ?? string.Empty);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"search\":[{\"id\":\"Q43416\"},{\"id\":\"P31\"},{\"id\":\"Q42\"}]," +
                        "\"search-continue\":50}")
                };
            });
        http.Expect(HttpMethod.Get, WikidataClient.DefaultApiUrl + "*")
            .Respond(request =>
            {
                requestUris.Add(request.RequestUri?.AbsoluteUri ?? string.Empty);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"search\":[{\"id\":\"Q42\"},{\"id\":\"Q99\"}]}")
                };
            });
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory
            .Setup(factory => factory.CreateClient(WikidataClient.HttpClientName))
            .Returns(() => new HttpClient(http, disposeHandler: false));
        IWikidataClient subject = new WikidataClient(
            httpClientFactory.Object,
            TimeProvider.System,
            NullLogger<WikidataClient>.Instance);
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };

        // Act
        var result = await subject.SearchEntitiesAsync("Keanu Reeves", context, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo(["Q43416", "Q42", "Q99"]));
            Assert.That(requestUris, Has.Count.EqualTo(2));
            Assert.That(requestUris[0], Does.Contain("action=wbsearchentities"));
            Assert.That(requestUris[0], Does.Contain("search=Keanu%20Reeves"));
            Assert.That(requestUris[0], Does.Contain("language=en"));
            Assert.That(requestUris[0], Does.Contain("uselang=en"));
            Assert.That(requestUris[0], Does.Contain("type=item"));
            Assert.That(requestUris[0], Does.Contain("limit=50"));
            Assert.That(requestUris[0], Does.Contain("maxlag=5"));
            Assert.That(requestUris[0], Does.Not.Contain("continue="));
            Assert.That(requestUris[1], Does.Contain("continue=50"));
        }

        http.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task ExecuteSparqlAsync_UseGetAndPost_WhenShortAndLongQueries()
    {
        // Arrange
        var methods = new List<HttpMethod>();
        var bodies = new List<string?>();
        using var http = new MockHttpMessageHandler();
        http.Expect(HttpMethod.Get, WikidataClient.DefaultSparqlUrl + "*")
            .Respond(request =>
            {
                methods.Add(request.Method);
                bodies.Add(request.Content?.ReadAsStringAsync().GetAwaiter().GetResult());
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"results\":{\"bindings\":[]}}")
                };
            });
        http.Expect(HttpMethod.Post, WikidataClient.DefaultSparqlUrl)
            .Respond(request =>
            {
                methods.Add(request.Method);
                bodies.Add(request.Content?.ReadAsStringAsync().GetAwaiter().GetResult());
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"results\":{\"bindings\":[]}}")
                };
            });
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory
            .Setup(factory => factory.CreateClient(WikidataClient.HttpClientName))
            .Returns(() => new HttpClient(http, disposeHandler: false));
        IWikidataClient subject = new WikidataClient(
            httpClientFactory.Object,
            TimeProvider.System,
            NullLogger<WikidataClient>.Instance);
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };

        // Act
        await subject.ExecuteSparqlAsync(
            new SparqlRequest
            {
                QueryText = "SELECT ?item WHERE { ?item ?p ?o. }",
                CanonicalCacheKey = "short",
                Timeout = TimeSpan.FromSeconds(1)
            },
            context,
            default).ConfigureAwait(false);
        await subject.ExecuteSparqlAsync(
            new SparqlRequest
            {
                QueryText = "SELECT ?item WHERE { " + new string('x', 3000) + " }",
                CanonicalCacheKey = "long",
                Timeout = TimeSpan.FromSeconds(1)
            },
            context,
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(methods, Is.EqualTo([HttpMethod.Get, HttpMethod.Post]));
            Assert.That(bodies[0], Is.Null);
            Assert.That(bodies[1], Does.Contain("query="));
        }

        http.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task GetEntitiesAsync_RetriesOnce_WhenMaxlagEnvelope()
    {
        // Arrange
        using var http = new MockHttpMessageHandler();
        var first = http.Expect(HttpMethod.Get, WikidataClient.DefaultApiUrl + "*")
            .Respond(_ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"error\":{\"code\":\"maxlag\",\"info\":\"lagged\"}}")
                };
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
                return response;
            });
        var second = http.Expect(HttpMethod.Get, WikidataClient.DefaultApiUrl + "*")
            .Respond("application/json", "{\"entities\":{}}");
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory
            .Setup(factory => factory.CreateClient(WikidataClient.HttpClientName))
            .Returns(() => new HttpClient(http, disposeHandler: false));
        IWikidataClient subject = new WikidataClient(
            httpClientFactory.Object,
            TimeProvider.System,
            NullLogger<WikidataClient>.Instance);
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };

        // Act
        var result = await subject.GetEntitiesAsync(["Q1"], context, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Empty);
            Assert.That(http.GetMatchCount(first), Is.EqualTo(1));
            Assert.That(http.GetMatchCount(second), Is.EqualTo(1));
        }

        http.VerifyNoOutstandingExpectation();
    }

    [Test]
    public void GetEntitiesAsync_Throws_WhenTooManyIds()
    {
        // Arrange
        using var http = new MockHttpMessageHandler();
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory
            .Setup(factory => factory.CreateClient(WikidataClient.HttpClientName))
            .Returns(() => new HttpClient(http, disposeHandler: false));
        IWikidataClient subject = new WikidataClient(
            httpClientFactory.Object,
            TimeProvider.System,
            NullLogger<WikidataClient>.Instance);
        var ids = Enumerable.Range(1, 51).Select(value => "Q" + value).ToArray();
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };

        // Act
        var action = async () => await subject.GetEntitiesAsync(ids, context, default)
            .ConfigureAwait(false);

        // Assert
        Assert.That(action, Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public async Task GetEntitiesAsync_ReturnsTargetUnderRequestedKey_WhenResponseContainsNestedRedirect()
    {
        // Arrange
        using var http = new MockHttpMessageHandler();
        http.Expect(HttpMethod.Get, WikidataClient.DefaultApiUrl + "*")
            .Respond(
                "application/json",
                "{\"entities\":{\"Q59431323\":{" +
                "\"redirects\":{\"from\":\"Q59431323\",\"to\":\"Q42\"}," +
                "\"id\":\"Q42\"}}}");
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory
            .Setup(factory => factory.CreateClient(WikidataClient.HttpClientName))
            .Returns(() => new HttpClient(http, disposeHandler: false));
        IWikidataClient subject = new WikidataClient(
            httpClientFactory.Object,
            TimeProvider.System,
            NullLogger<WikidataClient>.Instance);
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };

        // Act
        var result = await subject.GetEntitiesAsync(["Q59431323"], context, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Keys, Is.EqualTo(["Q59431323"]));
            Assert.That(result["Q59431323"].Id, Is.EqualTo("Q42"));
        }

        http.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task GetEntitiesAsync_ReturnsEachRequestedKey_WhenBatchContainsRedirectAndTarget()
    {
        // Arrange
        using var http = new MockHttpMessageHandler();
        http.Expect(HttpMethod.Get, WikidataClient.DefaultApiUrl + "*")
            .Respond(
                "application/json",
                "{\"entities\":{" +
                "\"Q59431323\":{\"redirects\":{\"from\":\"Q59431323\",\"to\":\"Q42\"}," +
                "\"id\":\"Q42\"}," +
                "\"Q42\":{\"id\":\"Q42\"}}}");
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory
            .Setup(factory => factory.CreateClient(WikidataClient.HttpClientName))
            .Returns(() => new HttpClient(http, disposeHandler: false));
        IWikidataClient subject = new WikidataClient(
            httpClientFactory.Object,
            TimeProvider.System,
            NullLogger<WikidataClient>.Instance);
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };

        // Act
        var result = await subject.GetEntitiesAsync(
            ["Q59431323", "Q42"],
            context,
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Keys, Is.EqualTo(["Q59431323", "Q42"]));
            Assert.That(result["Q59431323"].Id, Is.EqualTo("Q42"));
            Assert.That(result["Q42"].Id, Is.EqualTo("Q42"));
        }

        http.VerifyNoOutstandingExpectation();
    }

    [Test]
    public void ExecuteSparqlAsync_ThrowsRateLimited_WhenTooManyRequests()
    {
        // Arrange
        using var http = new MockHttpMessageHandler();
        var expected = http.Expect(HttpMethod.Get, WikidataClient.DefaultSparqlUrl + "*")
            .Respond(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory
            .Setup(factory => factory.CreateClient(WikidataClient.HttpClientName))
            .Returns(() => new HttpClient(http, disposeHandler: false));
        IWikidataClient subject = new WikidataClient(
            httpClientFactory.Object,
            TimeProvider.System,
            NullLogger<WikidataClient>.Instance);
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        var request = new SparqlRequest
        {
            QueryText = "SELECT ?item WHERE { ?item ?p ?o. }",
            CanonicalCacheKey = "rate-limit",
            Timeout = TimeSpan.FromSeconds(1)
        };

        // Act
        var exception = Assert.ThrowsAsync<HttpRequestException>(async () => await subject.ExecuteSparqlAsync(
            request,
            context,
            default).ConfigureAwait(false));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception!.Data.Contains("wikidata.rate-limited"), Is.True);
            Assert.That(http.GetMatchCount(expected), Is.EqualTo(1));
        }

        http.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task ExecuteSparqlAsync_ThrowsCancellation_WhenRequestBudgetExpires()
    {
        // Arrange
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stream = new Mock<Stream> { CallBase = true };
        stream.SetupGet(value => value.CanRead).Returns(true);
        stream.Setup(value => value.ReadAsync(
            It.IsAny<byte[]>(),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()))
            .Returns(async (byte[] _, int _, int _, CancellationToken cancellationToken) =>
            {
                started.TrySetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
                return 0;
            });
        using var http = new MockHttpMessageHandler();
        var expected = http.Expect(HttpMethod.Get, WikidataClient.DefaultSparqlUrl + "*")
            .Respond(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(stream.Object)
            });
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory
            .Setup(factory => factory.CreateClient(WikidataClient.HttpClientName))
            .Returns(() => new HttpClient(http, disposeHandler: false));
        IWikidataClient subject = new WikidataClient(
            httpClientFactory.Object,
            TimeProvider.System,
            NullLogger<WikidataClient>.Instance);
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };

        // Act
        var execution = subject.ExecuteSparqlAsync(
            new SparqlRequest
            {
                QueryText = "SELECT ?item WHERE { ?item ?p ?o. }",
                CanonicalCacheKey = "budget-timeout",
                Timeout = TimeSpan.FromSeconds(2)
            },
            context,
            default);
        var firstCompletion = await Task.WhenAny(started.Task, execution).ConfigureAwait(false);
        var exception = Assert.CatchAsync<OperationCanceledException>(async () =>
            await execution.ConfigureAwait(false));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstCompletion, Is.SameAs(started.Task));
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception!.Data.Contains("wikidata.rate-limited"), Is.False);
            Assert.That(http.GetMatchCount(expected), Is.EqualTo(1));
        }

        http.VerifyNoOutstandingExpectation();
    }
}
