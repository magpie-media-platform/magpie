using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Tmdb;
using Magpie.Extensions.Tmdb.QueryEngine;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Models.MetadataSearch;
using Magpie.Sdk.Caching;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

using RichardSzalay.MockHttp;

namespace Magpie.Test.Extensions.Tmdb;

[TestFixture]
public class SearchPipelineTests
{
    private const string SnowWhiteSearchJson = """
        {
          "page": 1,
          "total_pages": 1,
          "total_results": 2,
          "results": [
            { "id": 447273, "title": "Snow White", "original_title": "Snow White",
              "original_language": "en", "release_date": "2025-03-19",
              "overview": "A princess joins forces with seven dwarfs.",
              "poster_path": "/xWWg47tTfparvjK0WJNX4xL8lW2.jpg", "genre_ids": [10751, 14] },
            { "id": 408220, "title": "Snow White and the Seven Dwarfs", "original_title": "Snow White",
              "original_language": "en", "release_date": "1937-12-21",
              "overview": "The classic.", "poster_path": "/poster2.jpg", "genre_ids": [16] }
          ]
        }
        """;

    private ICacheFactory _cacheFactory = null!;

    private IServiceScopeFactory _scopeFactory = null!;

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
                cache.Setup(value => value.Remove(It.IsAny<string>())).Returns<string>(values.Remove);
                cache.Setup(value => value.GetOrAddAsync(
                        It.IsAny<string>(),
                        It.IsAny<Func<CancellationToken, ValueTask<MetadataSearchCandidate?>>>(),
                        It.IsAny<CancellationToken>()))
                    .Returns<string, Func<CancellationToken, ValueTask<MetadataSearchCandidate?>>, CancellationToken>(
                        async (key, valueFactory, token) =>
                        {
                            if (values.TryGetValue(key, out var cached))
                            {
                                return (MetadataSearchCandidate?)cached;
                            }

                            var created = await valueFactory(token).ConfigureAwait(false);
                            values[key] = created;
                            return created;
                        });
                return caches[name] = cache.Object;
            });
        _cacheFactory = cacheFactory.Object;
        var provider = new Mock<IServiceProvider>();
        provider.Setup(value => value.GetService(typeof(ICacheFactory))).Returns(_cacheFactory);
        var scope = new Mock<IServiceScope>();
        scope.Setup(value => value.ServiceProvider).Returns(provider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(value => value.CreateScope()).Returns(scope.Object);
        _scopeFactory = scopeFactory.Object;
    }

    [Test]
    public async Task AdvancedSearchAsync_ReturnsUpstreamHitsWithoutWarnings_WhenTitleContainsCriterionMatches()
    {
        // Arrange
        using var http = new MockHttpMessageHandler();
        http.When(HttpMethod.Get, "https://api.themoviedb.org/3/search/movie*")
            .Respond("application/json", SnowWhiteSearchJson);
        http.When(HttpMethod.Get, "https://api.themoviedb.org/3/configuration*")
            .Respond(request => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{ "images": { "secure_base_url": "https://image.tmdb.org/t/p/" } }""",
                    Encoding.UTF8,
                    "application/json")
            });
        var httpClient = http.ToHttpClient();
        httpClient.BaseAddress = new Uri(TmdbClient.DefaultBaseUrl, UriKind.Absolute);
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(factory => factory.CreateClient(TmdbClient.HttpClientName)).Returns(httpClient);
        var client = new TmdbClient(httpClientFactory.Object, TimeProvider.System, NullLogger<TmdbClient>.Instance);
        var settingsResolver = new Mock<ISettingsResolver>();
        settingsResolver
            .Setup(resolver => resolver.AcquireContextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RequestContext { ApiKey = "key", Language = "en-US" });
        var coordinator = new QueryCoordinator(_scopeFactory, Mock.Of<IHostApplicationLifetime>());
        var engine = new Magpie.Extensions.Tmdb.QueryEngine.QueryEngine(
            settingsResolver.Object,
            new QueryExecutor(client, coordinator, NullLogger<QueryExecutor>.Instance),
            coordinator,
            client,
            new QueryPlanner(),
            NullLogger<Magpie.Extensions.Tmdb.QueryEngine.QueryEngine>.Instance);
        var searcher = new ContentMetadataSearcher(
            engine,
            settingsResolver.Object,
            client,
            _cacheFactory,
            coordinator,
            NullLogger<ContentMetadataSearcher>.Instance);
        var query = new ContentHierarchyItemCriteria
        {
            ContentType = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Value = "movie" },
                    new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Value = "movie" }
                ]
            },
            Metadata = new CriteriaSet<MetadataCriteria>
            {
                And =
                [
                    new MetadataCriteria
                    {
                        Title = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And =
                            [
                                new PropertyCriterion<string>
                                {
                                    Operator = CriteriaValueOperator.ContainsString,
                                    Value = "snow white"
                                }
                            ]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await searcher.AdvancedSearchAsync("movie", query, 25, string.Empty, default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Supported, Is.True);
            Assert.That(result.Matches, Has.Count.EqualTo(2));
            Assert.That(result.Matches[0].Title, Is.EqualTo("Snow White"));
            Assert.That(result.Matches[0].ProviderType, Is.EqualTo("tmdb"));
            Assert.That(result.Matches[0].ExternalIdSource, Is.EqualTo("tmdb"));
            Assert.That(result.Matches[0].ExternalId, Is.EqualTo("447273"));
            Assert.That(result.Matches[0].Year, Is.EqualTo(2025));
            Assert.That(result.Matches[1].Title, Is.EqualTo("Snow White and the Seven Dwarfs"));
            Assert.That(result.Matches[1].ExternalId, Is.EqualTo("408220"));
            Assert.That(result.Warnings, Is.Empty);
        }
    }
}
