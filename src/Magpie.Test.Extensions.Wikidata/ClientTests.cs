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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;
using Moq.Protected;

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
        var registrations = new ServiceCollection().AddSingleton<TimeProvider>(TimeProvider.System);
        new WikidataExtension().RegisterServices(registrations);
        registrations.AddHttpClient(WikidataClient.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => http);
        using var transport = registrations.BuildServiceProvider();
        var factory = transport.GetRequiredService<IHttpClientFactory>();
        var subject = new WikidataClient(factory);
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
        var registrations = new ServiceCollection().AddSingleton<TimeProvider>(TimeProvider.System);
        new WikidataExtension().RegisterServices(registrations);
        registrations.AddHttpClient(WikidataClient.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => http);
        using var transport = registrations.BuildServiceProvider();
        var httpClientFactory = transport.GetRequiredService<IHttpClientFactory>();
        IWikidataClient subject = new WikidataClient(httpClientFactory);
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
        var registrations = new ServiceCollection().AddSingleton<TimeProvider>(TimeProvider.System);
        new WikidataExtension().RegisterServices(registrations);
        registrations.AddHttpClient(WikidataClient.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => http);
        using var transport = registrations.BuildServiceProvider();
        var httpClientFactory = transport.GetRequiredService<IHttpClientFactory>();
        IWikidataClient subject = new WikidataClient(httpClientFactory);
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
        var registrations = new ServiceCollection().AddSingleton<TimeProvider>(TimeProvider.System);
        new WikidataExtension().RegisterServices(registrations);
        registrations.AddHttpClient(WikidataClient.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => http);
        using var transport = registrations.BuildServiceProvider();
        var httpClientFactory = transport.GetRequiredService<IHttpClientFactory>();
        IWikidataClient subject = new WikidataClient(httpClientFactory);
        var upstreamRequests = 0;
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test",
            UpstreamRequestObserver = () => upstreamRequests++
        };

        // Act
        var result = await subject.SearchEntitiesAsync("Keanu Reeves", context, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo(["Q43416", "Q42", "Q99"]));
            Assert.That(upstreamRequests, Is.EqualTo(2));
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
        var registrations = new ServiceCollection().AddSingleton<TimeProvider>(TimeProvider.System);
        new WikidataExtension().RegisterServices(registrations);
        registrations.AddHttpClient(WikidataClient.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => http);
        using var transport = registrations.BuildServiceProvider();
        var httpClientFactory = transport.GetRequiredService<IHttpClientFactory>();
        IWikidataClient subject = new WikidataClient(httpClientFactory);
        var upstreamRequests = 0;
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test",
            UpstreamRequestObserver = () => upstreamRequests++
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
            Assert.That(upstreamRequests, Is.EqualTo(2));
            Assert.That(methods, Is.EqualTo([HttpMethod.Get, HttpMethod.Post]));
            Assert.That(bodies[0], Is.Null);
            Assert.That(bodies[1], Does.Contain("query="));
        }

        http.VerifyNoOutstandingExpectation();
    }

    [TestCase("maxlag")]
    [TestCase("ratelimited")]
    public async Task GetEntitiesAsync_RetriesInternally_WhenThrottleEnvelope(string errorCode)
    {
        // Arrange
        var time = new FakeTimeProvider { AutoAdvanceAmount = TimeSpan.FromSeconds(2) };
        using var http = new MockHttpMessageHandler();
        var first = http.Expect(HttpMethod.Get, WikidataClient.DefaultApiUrl + "*")
            .Respond(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Headers =
                {
                    RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(1))
                },
                Content = new StringContent("{\"error\":{\"code\":\"" + errorCode + "\"}}")
            });
        var second = http.Expect(HttpMethod.Get, WikidataClient.DefaultApiUrl + "*")
            .Respond("application/json", "{\"entities\":{}}");
        var registrations = new ServiceCollection().AddSingleton<TimeProvider>(time);
        new WikidataExtension().RegisterServices(registrations);
        registrations.AddHttpClient(WikidataClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => http);
        using var transport = registrations.BuildServiceProvider();
        var subject = new WikidataClient(transport.GetRequiredService<IHttpClientFactory>());
        var upstreamRequests = 0;
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test",
            UpstreamRequestObserver = () => upstreamRequests++
        };

        // Act
        var result = await subject.GetEntitiesAsync(["Q1"], context, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Empty);
            Assert.That(upstreamRequests, Is.EqualTo(2));
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
        var registrations = new ServiceCollection().AddSingleton<TimeProvider>(TimeProvider.System);
        new WikidataExtension().RegisterServices(registrations);
        registrations.AddHttpClient(WikidataClient.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => http);
        using var transport = registrations.BuildServiceProvider();
        var httpClientFactory = transport.GetRequiredService<IHttpClientFactory>();
        IWikidataClient subject = new WikidataClient(httpClientFactory);
        var ids = Enumerable.Range(1, 51).Select(value => "Q" + value).ToArray();
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };

        // Act
        var action = async () => await subject.GetEntitiesAsync(ids, context, default).ConfigureAwait(false);

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
        var registrations = new ServiceCollection().AddSingleton<TimeProvider>(TimeProvider.System);
        new WikidataExtension().RegisterServices(registrations);
        registrations.AddHttpClient(WikidataClient.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => http);
        using var transport = registrations.BuildServiceProvider();
        var httpClientFactory = transport.GetRequiredService<IHttpClientFactory>();
        IWikidataClient subject = new WikidataClient(httpClientFactory);
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
        var registrations = new ServiceCollection().AddSingleton<TimeProvider>(TimeProvider.System);
        new WikidataExtension().RegisterServices(registrations);
        registrations.AddHttpClient(WikidataClient.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => http);
        using var transport = registrations.BuildServiceProvider();
        var httpClientFactory = transport.GetRequiredService<IHttpClientFactory>();
        IWikidataClient subject = new WikidataClient(httpClientFactory);
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
    public async Task ExecuteSparqlAsync_RetriesInternally_WhenTooManyRequests()
    {
        // Arrange
        var time = new FakeTimeProvider { AutoAdvanceAmount = TimeSpan.FromSeconds(2) };
        using var http = new MockHttpMessageHandler();
        var expected = http.Expect(HttpMethod.Get, WikidataClient.DefaultSparqlUrl + "*")
            .Respond(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Headers =
                {
                    RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(1))
                }
            });
        http.Expect(HttpMethod.Get, WikidataClient.DefaultSparqlUrl + "*")
            .Respond("application/json", "{\"results\":{\"bindings\":[]}}");
        var registrations = new ServiceCollection().AddSingleton<TimeProvider>(time);
        new WikidataExtension().RegisterServices(registrations);
        registrations.AddHttpClient(WikidataClient.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => http);
        using var transport = registrations.BuildServiceProvider();
        var httpClientFactory = transport.GetRequiredService<IHttpClientFactory>();
        IWikidataClient subject = new WikidataClient(httpClientFactory);
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
            Timeout = TimeSpan.FromSeconds(30)
        };

        // Act
        var result = await subject.ExecuteSparqlAsync(
            request,
            context,
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Empty);
            Assert.That(http.GetMatchCount(expected), Is.EqualTo(1));
        }

        http.VerifyNoOutstandingExpectation();
    }

    [TestCase(false, 0.3)]
    [TestCase(true, 21)]
    public async Task SendAsync_DoesNotSpendRequestBudget_WhenWaitingForAdmission(bool action, double queueSeconds)
    {
        // Arrange
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero));
        var occupied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sends = 0;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns(async (HttpRequestMessage _, CancellationToken token) =>
            {
                var send = Interlocked.Increment(ref sends);
                if (send <= 2)
                {
                    if (send == 2)
                    {
                        occupied.SetResult();
                    }

                    await release.Task.WaitAsync(token).ConfigureAwait(false);
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"entities\":{},\"results\":{\"bindings\":[]}}")
                };
            });
        var registrations = new ServiceCollection().AddSingleton<TimeProvider>(time);
        new WikidataExtension().RegisterServices(registrations);
        registrations.AddHttpClient(WikidataClient.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler.Object);
        using var transport = registrations.BuildServiceProvider();
        var factory = transport.GetRequiredService<IHttpClientFactory>();
        var subject = new WikidataClient(factory);
        var context = new RequestContext { Language = "en", FallbackLanguages = ["en"], UserAgent = "test" };
        var occupying = new SparqlRequest
        {
            QueryText = "SELECT ?item WHERE { ?item ?p ?o. }",
            CanonicalCacheKey = "occupying",
            Timeout = TimeSpan.FromSeconds(30)
        };
        var queued = new SparqlRequest
        {
            QueryText = "SELECT ?item WHERE { ?item ?p ?o. }",
            CanonicalCacheKey = "queued",
            Timeout = TimeSpan.FromMilliseconds(100)
        };
        var first = subject.ExecuteSparqlAsync(occupying, context, default);
        var second = subject.ExecuteSparqlAsync(occupying, context, default);
        await occupied.Task.ConfigureAwait(false);

        // Act
        Task pending = action
            ? subject.GetEntitiesAsync(["Q42"], context, default)
            : subject.ExecuteSparqlAsync(queued, context, default);
        time.Advance(TimeSpan.FromSeconds(queueSeconds));
        var queuedSends = sends;
        release.SetResult();
        await pending.ConfigureAwait(false);
        await Task.WhenAll(first, second).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(queuedSends, Is.EqualTo(2));
            Assert.That(sends, Is.EqualTo(3));
        }
    }

    [Test]
    public async Task SearchEntitiesAsync_ReadsEveryPage_WhenThePagesTogetherTakeLongerThanOneRequestTimeout()
    {
        // Arrange
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero));
        using var arrived = new SemaphoreSlim(0);
        string[] pages =
        [
            "{\"search\":[{\"id\":\"Q1\"}],\"search-continue\":1}",
            "{\"search\":[{\"id\":\"Q2\"}],\"search-continue\":2}",
            "{\"search\":[{\"id\":\"Q3\"}],\"search-continue\":3}",
            "{\"search\":[{\"id\":\"Q4\"}]}"
        ];
        var sends = 0;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns(async (HttpRequestMessage _, CancellationToken token) =>
            {
                var page = pages[Interlocked.Increment(ref sends) - 1];
                var response = Task.Delay(TimeSpan.FromSeconds(16), time, token);
                arrived.Release();
                await response.ConfigureAwait(false);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(page) };
            });
        var registrations = new ServiceCollection().AddSingleton<TimeProvider>(time);
        new WikidataExtension().RegisterServices(registrations);
        registrations.AddHttpClient(WikidataClient.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler.Object);
        using var transport = registrations.BuildServiceProvider();
        var factory = transport.GetRequiredService<IHttpClientFactory>();
        var subject = new WikidataClient(factory);
        var context = new RequestContext { Language = "en", FallbackLanguages = ["en"], UserAgent = "test" };
        var started = time.GetUtcNow();

        // Act
        var pending = subject.SearchEntitiesAsync("test", context, default);
        await arrived.WaitAsync().ConfigureAwait(false);
        time.Advance(TimeSpan.FromSeconds(16));
        await arrived.WaitAsync().ConfigureAwait(false);
        time.Advance(TimeSpan.FromSeconds(16));
        await arrived.WaitAsync().ConfigureAwait(false);
        time.Advance(TimeSpan.FromSeconds(16));
        await arrived.WaitAsync().ConfigureAwait(false);
        time.Advance(TimeSpan.FromSeconds(16));
        var result = await pending.ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo(["Q1", "Q2", "Q3", "Q4"]));
            Assert.That(time.GetUtcNow() - started, Is.EqualTo(TimeSpan.FromSeconds(64)));
        }
    }

    [Test]
    public async Task SearchEntitiesAsync_StopsAfterOnePage_WhenTheRequestTimesOut()
    {
        // Arrange
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero));
        using var arrived = new SemaphoreSlim(0);
        using var lifetime = new CancellationTokenSource();
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns(async (HttpRequestMessage _, CancellationToken token) =>
            {
                var response = Task.Delay(TimeSpan.FromSeconds(25), time, token);
                arrived.Release();
                await response.ConfigureAwait(false);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"search\":[{\"id\":\"Q1\"}],\"search-continue\":1}")
                };
            });
        var registrations = new ServiceCollection().AddSingleton<TimeProvider>(time);
        new WikidataExtension().RegisterServices(registrations);
        registrations.AddHttpClient(WikidataClient.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler.Object);
        using var transport = registrations.BuildServiceProvider();
        var factory = transport.GetRequiredService<IHttpClientFactory>();
        var subject = new WikidataClient(factory);
        var context = new RequestContext { Language = "en", FallbackLanguages = ["en"], UserAgent = "test" };

        // Act
        var pending = subject.SearchEntitiesAsync("test", context, lifetime.Token);
        await arrived.WaitAsync().ConfigureAwait(false);
        time.Advance(TimeSpan.FromSeconds(25));

        // Assert
        Assert.CatchAsync<OperationCanceledException>(async () => await pending.ConfigureAwait(false));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(arrived.CurrentCount, Is.Zero);
            Assert.That(lifetime.IsCancellationRequested, Is.False);
        }
    }

    [Test]
    public async Task SearchEntitiesAsync_StopsAfterOnePage_WhenTheCallerCancels()
    {
        // Arrange
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero));
        using var arrived = new SemaphoreSlim(0);
        using var lifetime = new CancellationTokenSource();
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns(async (HttpRequestMessage _, CancellationToken token) =>
            {
                var response = Task.Delay(TimeSpan.FromSeconds(16), time, token);
                arrived.Release();
                await response.ConfigureAwait(false);
                await lifetime.CancelAsync().ConfigureAwait(false);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"search\":[{\"id\":\"Q1\"}],\"search-continue\":1}")
                };
            });
        var registrations = new ServiceCollection().AddSingleton<TimeProvider>(time);
        new WikidataExtension().RegisterServices(registrations);
        registrations.AddHttpClient(WikidataClient.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler.Object);
        using var transport = registrations.BuildServiceProvider();
        var factory = transport.GetRequiredService<IHttpClientFactory>();
        var subject = new WikidataClient(factory);
        var context = new RequestContext { Language = "en", FallbackLanguages = ["en"], UserAgent = "test" };

        // Act
        var pending = subject.SearchEntitiesAsync("test", context, lifetime.Token);
        await arrived.WaitAsync().ConfigureAwait(false);
        time.Advance(TimeSpan.FromSeconds(16));

        // Assert
        Assert.CatchAsync<OperationCanceledException>(async () => await pending.ConfigureAwait(false));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(arrived.CurrentCount, Is.Zero);
            Assert.That(lifetime.IsCancellationRequested, Is.True);
        }
    }
}
