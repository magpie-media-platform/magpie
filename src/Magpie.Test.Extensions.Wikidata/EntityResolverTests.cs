using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Wikidata;
using Magpie.Extensions.Wikidata.Mapping;
using Magpie.Extensions.Wikidata.QueryEngine;
using Magpie.Models.Data;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Wikidata;

[TestFixture]
internal sealed class EntityResolverTests
{
    [Test]
    public async Task ResolveContentAsync_UsesSeasonQidAndEpisodeOrdinal_WhenEpisodeWithoutSeasonNumber()
    {
        // Arrange
        SparqlRequest? request = null;
        var client = new Mock<IWikidataClient>();
        client.Setup(value => value.ExecuteSparqlAsync(
            It.IsAny<SparqlRequest>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .Callback((
                SparqlRequest sparqlRequest,
                RequestContext _,
                CancellationToken _) => request = sparqlRequest)
            .ReturnsAsync(
            [
                new Dictionary<string, SparqlBindingDto>
                {
                    ["item"] = new SparqlBindingDto
                    {
                        Type = "uri",
                        Value = "http://www.wikidata.org/entity/Q300"
                    }
                }
            ]);
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetIdentityAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<EntityResolution>>>(),
            It.IsAny<CancellationToken>()))
            .Returns((
                string _,
                Func<CancellationToken, Task<EntityResolution>> factory,
                CancellationToken cancellationToken) => factory(cancellationToken));
        IEntityResolver subject = new EntityResolver(client.Object, cache.Object);
        var item = new ContentHierarchyItem
        {
            ContentType = Constants.TvContentType,
            Level = Constants.EpisodeLevel,
            Slug = "episode-two",
            Name = "Second Episode"
        };
        var existing = new MetadataContribution
        {
            ProviderType = "seed",
            SequenceNumber = 2
        };
        var season = new MetadataContribution { ProviderType = "seed" };
        season.ExternalIds.Add(new ExternalId { Source = Constants.ProviderType, Value = "Q100" });
        var show = new MetadataContribution { ProviderType = "seed" };
        show.ExternalIds.Add(new ExternalId { Source = Constants.ProviderType, Value = "Q200" });
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };

        // Act
        var result = await subject.ResolveContentAsync(
            EntityKind.Episode,
            item,
            [existing],
            [season, show],
            context,
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Kind, Is.EqualTo(EntityResolutionKind.Resolved));
            Assert.That(result.Qid, Is.EqualTo("Q300"));
            Assert.That(request!.QueryText, Does.Contain("wdt:P179 wd:Q200"));
            Assert.That(request.QueryText, Does.Contain("ps:P4908 wd:Q100"));
            Assert.That(request.QueryText, Does.Contain("pq:P1545 \"2\""));
        }

        client.Verify(
            value => value.ExecuteSparqlAsync(
                It.IsAny<SparqlRequest>(),
                It.IsAny<RequestContext>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ResolveSubjectAsync_PrefersOwnProviderIdAndStripsEntityUri_WhenSeveralProvidersCarryASlug()
    {
        // Arrange
        var client = new Mock<IWikidataClient>();
        var cache = new Mock<IQueryCoordinator>();
        IEntityResolver subject = new EntityResolver(client.Object, cache.Object);
        var seed = new MetadataContribution { ProviderType = "seed" };
        seed.ExternalIds.Add(new ExternalId { Source = Constants.ProviderType, Value = "Q999" });
        var own = new MetadataContribution { ProviderType = Constants.ProviderType };
        own.ExternalIds.Add(new ExternalId
        {
            Source = Constants.ProviderType,
            Value = "http://www.wikidata.org/entity/q42"
        });
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };

        // Act
        var result = await subject.ResolveSubjectAsync(
            EntityKind.Person,
            [seed, own],
            context,
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Kind, Is.EqualTo(EntityResolutionKind.Resolved));
            Assert.That(result.Qid, Is.EqualTo("Q42"));
        }

        client.VerifyNoOtherCalls();
        cache.VerifyNoOtherCalls();
    }

    [Test]
    public async Task ResolveSubjectAsync_QueriesTmdbPersonProperty_WhenNoSlugIsPresent()
    {
        // Arrange
        SparqlRequest? request = null;
        var client = new Mock<IWikidataClient>();
        client.Setup(value => value.ExecuteSparqlAsync(
            It.IsAny<SparqlRequest>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .Callback((
                SparqlRequest sparqlRequest,
                RequestContext _,
                CancellationToken _) => request = sparqlRequest)
            .ReturnsAsync(
            [
                new Dictionary<string, SparqlBindingDto>
                {
                    ["item"] = new SparqlBindingDto
                    {
                        Type = "uri",
                        Value = "http://www.wikidata.org/entity/Q42"
                    }
                }
            ]);
        string? cacheKey = null;
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetIdentityAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<EntityResolution>>>(),
            It.IsAny<CancellationToken>()))
            .Returns((
                string key,
                Func<CancellationToken, Task<EntityResolution>> factory,
                CancellationToken cancellationToken) =>
            {
                cacheKey = key;
                return factory(cancellationToken);
            });
        IEntityResolver subject = new EntityResolver(client.Object, cache.Object);
        var contribution = new MetadataContribution { ProviderType = "seed" };
        contribution.ExternalIds.Add(new ExternalId { Source = Constants.TmdbSource, Value = " 287 " });
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };

        // Act
        var result = await subject.ResolveSubjectAsync(
            EntityKind.Person,
            [contribution],
            context,
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Kind, Is.EqualTo(EntityResolutionKind.Resolved));
            Assert.That(result.Qid, Is.EqualTo("Q42"));
            Assert.That(cacheKey, Is.EqualTo("Person|P4985|287"));
            Assert.That(request!.QueryText, Does.Contain("wdt:P4985 \"287\""));
        }
    }

    [Test]
    public async Task ResolveSubjectAsync_ReturnsAmbiguous_WhenExternalLookupMatchesTwoEntities()
    {
        // Arrange
        var client = new Mock<IWikidataClient>();
        client.Setup(value => value.ExecuteSparqlAsync(
            It.IsAny<SparqlRequest>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Dictionary<string, SparqlBindingDto>
                {
                    ["item"] = new SparqlBindingDto
                    {
                        Type = "uri",
                        Value = "http://www.wikidata.org/entity/Q42"
                    }
                },
                new Dictionary<string, SparqlBindingDto>
                {
                    ["item"] = new SparqlBindingDto
                    {
                        Type = "uri",
                        Value = "http://www.wikidata.org/entity/Q43"
                    }
                }
            ]);
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetIdentityAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<EntityResolution>>>(),
            It.IsAny<CancellationToken>()))
            .Returns((
                string _,
                Func<CancellationToken, Task<EntityResolution>> factory,
                CancellationToken cancellationToken) => factory(cancellationToken));
        IEntityResolver subject = new EntityResolver(client.Object, cache.Object);
        var contribution = new MetadataContribution { ProviderType = "seed" };
        contribution.ExternalIds.Add(new ExternalId { Source = Constants.ImdbSource, Value = "nm0000206" });
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };

        // Act
        var result = await subject.ResolveSubjectAsync(
            EntityKind.Person,
            [contribution],
            context,
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Kind, Is.EqualTo(EntityResolutionKind.Ambiguous));
            Assert.That(result.Qid, Is.Null);
        }
    }

    [Test]
    public async Task ResolveSubjectAsync_ReturnsNotFound_WhenNoSourceIsKnownForTheKind()
    {
        // Arrange
        var client = new Mock<IWikidataClient>();
        var cache = new Mock<IQueryCoordinator>();
        IEntityResolver subject = new EntityResolver(client.Object, cache.Object);
        var contribution = new MetadataContribution { ProviderType = "seed" };
        contribution.ExternalIds.Add(new ExternalId { Source = Constants.ImdbSource, Value = "co0144901" });
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };

        // Act
        var result = await subject.ResolveSubjectAsync(
            EntityKind.Studio,
            [contribution],
            context,
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Kind, Is.EqualTo(EntityResolutionKind.NotFound));
            Assert.That(result.Qid, Is.Null);
        }

        client.VerifyNoOtherCalls();
        cache.VerifyNoOtherCalls();
    }

    [Test]
    public async Task ResolveContentAsync_FallsBackToImdb_WhenTheTmdbLookupFindsNothing()
    {
        // Arrange
        var client = new Mock<IWikidataClient>();
        client.Setup(value => value.ExecuteSparqlAsync(
            It.Is<SparqlRequest>(request => request.QueryText.Contains("wdt:P4947")),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        client.Setup(value => value.ExecuteSparqlAsync(
            It.Is<SparqlRequest>(request => request.QueryText.Contains("wdt:P345")),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Dictionary<string, SparqlBindingDto>
                {
                    ["item"] = new SparqlBindingDto
                    {
                        Type = "uri",
                        Value = "http://www.wikidata.org/entity/Q83495"
                    }
                }
            ]);
        var cacheKeys = new List<string>();
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetIdentityAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<EntityResolution>>>(),
            It.IsAny<CancellationToken>()))
            .Returns((
                string key,
                Func<CancellationToken, Task<EntityResolution>> factory,
                CancellationToken cancellationToken) =>
            {
                cacheKeys.Add(key);
                return factory(cancellationToken);
            });
        IEntityResolver subject = new EntityResolver(client.Object, cache.Object);
        var item = new ContentHierarchyItem
        {
            ContentType = Constants.MovieContentType,
            Level = Constants.MovieLevel,
            Slug = "the-matrix",
            Name = "The Matrix"
        };
        var contribution = new MetadataContribution { ProviderType = "seed" };
        contribution.ExternalIds.Add(new ExternalId { Source = Constants.TmdbSource, Value = "603" });
        contribution.ExternalIds.Add(new ExternalId { Source = Constants.ImdbSource, Value = "tt0133093" });
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };

        // Act
        var result = await subject.ResolveContentAsync(
            EntityKind.Movie,
            item,
            [contribution],
            [],
            context,
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Kind, Is.EqualTo(EntityResolutionKind.Resolved));
            Assert.That(result.Qid, Is.EqualTo("Q83495"));
            Assert.That(cacheKeys, Is.EqualTo(["Movie|P4947|603", "Movie|P345|TT0133093"]));
        }
    }

    [Test]
    public async Task ResolveContentAsync_ReadsShowQidFromTheSeasonEntity_WhenNoAncestorSuppliesIt()
    {
        // Arrange
        SparqlRequest? request = null;
        var client = new Mock<IWikidataClient>();
        client.Setup(value => value.ExecuteSparqlAsync(
            It.IsAny<SparqlRequest>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .Callback((
                SparqlRequest sparqlRequest,
                RequestContext _,
                CancellationToken _) => request = sparqlRequest)
            .ReturnsAsync(
            [
                new Dictionary<string, SparqlBindingDto>
                {
                    ["item"] = new SparqlBindingDto
                    {
                        Type = "uri",
                        Value = "http://www.wikidata.org/entity/Q300"
                    }
                }
            ]);
        string? cacheKey = null;
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntityAsync(
            "Q100",
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityDto
            {
                Id = "Q100",
                Claims = new Dictionary<string, List<StatementDto>>
                {
                    [Constants.PartOfSeriesPropertyId] =
                    [
                        new StatementDto
                        {
                            Rank = "deprecated",
                            Mainsnak = new SnakDto
                            {
                                DataValue = new DataValueDto
                                {
                                    Value = JsonDocument.Parse("{\"id\":\"Q900\"}").RootElement.Clone()
                                }
                            }
                        },
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                DataValue = new DataValueDto
                                {
                                    Value = JsonDocument.Parse("{\"id\":\"Q800\"}").RootElement.Clone()
                                }
                            }
                        },
                        new StatementDto
                        {
                            Rank = "preferred",
                            Mainsnak = new SnakDto
                            {
                                DataValue = new DataValueDto
                                {
                                    Value = JsonDocument.Parse("{\"id\":\"Q200\"}").RootElement.Clone()
                                }
                            }
                        }
                    ]
                }
            });
        cache.Setup(value => value.GetIdentityAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<EntityResolution>>>(),
            It.IsAny<CancellationToken>()))
            .Returns((
                string key,
                Func<CancellationToken, Task<EntityResolution>> factory,
                CancellationToken cancellationToken) =>
            {
                cacheKey = key;
                return factory(cancellationToken);
            });
        IEntityResolver subject = new EntityResolver(client.Object, cache.Object);
        var item = new ContentHierarchyItem
        {
            ContentType = Constants.TvContentType,
            Level = Constants.EpisodeLevel,
            Slug = "s01e05",
            Name = "Fifth Episode"
        };
        var season = new MetadataContribution { ProviderType = "seed" };
        season.ExternalIds.Add(new ExternalId { Source = Constants.ProviderType, Value = "Q100" });
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };

        // Act
        var result = await subject.ResolveContentAsync(
            EntityKind.Episode,
            item,
            [],
            [season],
            context,
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Kind, Is.EqualTo(EntityResolutionKind.Resolved));
            Assert.That(result.Qid, Is.EqualTo("Q300"));
            Assert.That(cacheKey, Is.EqualTo("coordinate|Episode|Q100|Q200|5"));
            Assert.That(request!.QueryText, Does.Contain("wdt:P179 wd:Q200"));
            Assert.That(request.QueryText, Does.Contain("ps:P4908 wd:Q100"));
            Assert.That(request.QueryText, Does.Contain("pq:P1545 \"5\""));
        }
    }

    [Test]
    public async Task ResolveContentAsync_ReturnsNotFound_WhenNoAncestorCarriesAValidQid()
    {
        // Arrange
        var client = new Mock<IWikidataClient>();
        var cache = new Mock<IQueryCoordinator>();
        IEntityResolver subject = new EntityResolver(client.Object, cache.Object);
        var item = new ContentHierarchyItem
        {
            ContentType = Constants.TvContentType,
            Level = Constants.EpisodeLevel,
            Slug = "s01e05",
            Name = "Fifth Episode"
        };
        var season = new MetadataContribution { ProviderType = "seed" };
        season.ExternalIds.Add(new ExternalId { Source = Constants.ProviderType, Value = "Q0100" });
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };

        // Act
        var result = await subject.ResolveContentAsync(
            EntityKind.Episode,
            item,
            [],
            [season],
            context,
            default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Kind, Is.EqualTo(EntityResolutionKind.NotFound));
        client.VerifyNoOtherCalls();
        cache.VerifyNoOtherCalls();
    }

    [Test]
    public async Task ResolveContentAsync_UsesTheSeasonNumberFromTheSlug_WhenNoOrdinalIsStored()
    {
        // Arrange
        SparqlRequest? request = null;
        var client = new Mock<IWikidataClient>();
        client.Setup(value => value.ExecuteSparqlAsync(
            It.IsAny<SparqlRequest>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .Callback((
                SparqlRequest sparqlRequest,
                RequestContext _,
                CancellationToken _) => request = sparqlRequest)
            .ReturnsAsync(
            [
                new Dictionary<string, SparqlBindingDto>
                {
                    ["item"] = new SparqlBindingDto
                    {
                        Type = "uri",
                        Value = "http://www.wikidata.org/entity/Q400"
                    }
                }
            ]);
        string? cacheKey = null;
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetIdentityAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<EntityResolution>>>(),
            It.IsAny<CancellationToken>()))
            .Returns((
                string key,
                Func<CancellationToken, Task<EntityResolution>> factory,
                CancellationToken cancellationToken) =>
            {
                cacheKey = key;
                return factory(cancellationToken);
            });
        IEntityResolver subject = new EntityResolver(client.Object, cache.Object);
        var item = new ContentHierarchyItem
        {
            ContentType = Constants.TvContentType,
            Level = Constants.SeasonLevel,
            Slug = "season-3",
            Name = "Season 3"
        };
        var show = new MetadataContribution { ProviderType = "seed" };
        show.ExternalIds.Add(new ExternalId { Source = Constants.ProviderType, Value = "Q200" });
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };

        // Act
        var result = await subject.ResolveContentAsync(
            EntityKind.Season,
            item,
            [],
            [show],
            context,
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Kind, Is.EqualTo(EntityResolutionKind.Resolved));
            Assert.That(result.Qid, Is.EqualTo("Q400"));
            Assert.That(cacheKey, Is.EqualTo("coordinate|Season|Q200||3"));
            Assert.That(request!.QueryText, Does.Contain("ps:P179 wd:Q200"));
            Assert.That(request.QueryText, Does.Contain("pq:P1545 \"3\""));
        }
    }

    [Test]
    public async Task ResolveContentAsync_ReturnsNotFound_WhenTheStoredAndSlugOrdinalsDisagree()
    {
        // Arrange
        var client = new Mock<IWikidataClient>();
        var cache = new Mock<IQueryCoordinator>();
        IEntityResolver subject = new EntityResolver(client.Object, cache.Object);
        var item = new ContentHierarchyItem
        {
            ContentType = Constants.TvContentType,
            Level = Constants.SeasonLevel,
            Slug = "season-3",
            Name = "Season 3"
        };
        var existing = new MetadataContribution
        {
            ProviderType = "seed",
            SequenceNumber = 2
        };
        var show = new MetadataContribution { ProviderType = "seed" };
        show.ExternalIds.Add(new ExternalId { Source = Constants.ProviderType, Value = "Q200" });
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };

        // Act
        var result = await subject.ResolveContentAsync(
            EntityKind.Season,
            item,
            [existing],
            [show],
            context,
            default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Kind, Is.EqualTo(EntityResolutionKind.NotFound));
        client.VerifyNoOtherCalls();
        cache.VerifyNoOtherCalls();
    }

    [Test]
    public async Task ResolveContentAsync_ReturnsNotFound_WhenTheAlignmentKeyAndSlugSeasonsDisagree()
    {
        // Arrange
        var client = new Mock<IWikidataClient>();
        var cache = new Mock<IQueryCoordinator>();
        IEntityResolver subject = new EntityResolver(client.Object, cache.Object);
        var item = new ContentHierarchyItem
        {
            ContentType = Constants.TvContentType,
            Level = Constants.SeasonLevel,
            Slug = "season-5",
            Name = "Season 5",
            AlignmentKey = "season:2"
        };
        var show = new MetadataContribution { ProviderType = "seed" };
        show.ExternalIds.Add(new ExternalId { Source = Constants.ProviderType, Value = "Q200" });
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };

        // Act
        var result = await subject.ResolveContentAsync(
            EntityKind.Season,
            item,
            [],
            [show],
            context,
            default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Kind, Is.EqualTo(EntityResolutionKind.NotFound));
        client.VerifyNoOtherCalls();
        cache.VerifyNoOtherCalls();
    }
}
