using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Tmdb;
using Magpie.Extensions.Tmdb.Mapping;
using Magpie.Extensions.Tmdb.QueryEngine;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Sdk.Content;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Tmdb;

[TestFixture]
public class ContentMetadataProviderRelationshipTests
{
    private Mock<ITmdbClient> _client = null!;
    private Mock<ISettingsResolver> _resolver = null!;
    private IContentHierarchyMetadataRelationshipProvider _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _client = new Mock<ITmdbClient>();
        _resolver = new Mock<ISettingsResolver>();
        _resolver.Setup(resolver => resolver.AcquireContextAsync(
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RequestContext { ApiKey = "key", Language = "en-US" });
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(queryCache => queryCache.GetImageBaseUrlAsync(
                It.IsAny<RequestContext>(),
                It.IsAny<ITmdbClient>(),
                It.IsAny<ILogger>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://image.tmdb.org/t/p/");
        _subject = new ContentMetadataProvider(
            _resolver.Object,
            _client.Object,
            cache.Object,
            new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            NullLogger<ContentMetadataProvider>.Instance);
    }

    [TestCase("book", "movie")]
    [TestCase("tv", "collection")]
    [TestCase("movie", "show")]
    public async Task GetRelationshipsAsync_ReturnsEmpty_WhenRouteCannotBeResolved(
        string contentType,
        string level)
    {
        // Arrange
        var item = new ContentHierarchyItem
        {
            Id = new Guid("11111111-1111-1111-1111-111111111111"),
            ContentType = contentType,
            Level = level,
            Slug = "item",
            Name = "Item"
        };
        var existing = new MetadataContribution { ProviderType = Constants.ProviderType };
        existing.ExternalIds.Add(new ExternalId { Source = "tmdb", Value = "550" });

        // Act
        var relationships = await _subject.GetRelationshipsAsync(
            new MetadataRelationshipRequest
            {
                Item = item,
                Contribution = new MetadataContribution { ProviderType = Constants.ProviderType },
                ExistingMetadata = [existing],
            },
            default).ConfigureAwait(false);

        // Assert
        Assert.That(relationships, Is.Empty);
        _client.VerifyNoOtherCalls();
    }

    [TestCase(null)]
    [TestCase("not-a-number")]
    public async Task GetRelationshipsAsync_ReturnsEmpty_WhenMovieIdIsMissingOrNonNumeric(string? tmdbId)
    {
        // Arrange
        var item = new ContentHierarchyItem
        {
            Id = new Guid("22222222-2222-2222-2222-222222222222"),
            ContentType = "movie",
            Level = "movie",
            Slug = "fight-club",
            Name = "Fight Club"
        };
        var existing = new MetadataContribution { ProviderType = Constants.ProviderType };
        if (tmdbId is not null)
        {
            existing.ExternalIds.Add(new ExternalId { Source = "tmdb", Value = tmdbId });
        }

        // Act
        var relationships = await _subject.GetRelationshipsAsync(
            new MetadataRelationshipRequest
            {
                Item = item,
                Contribution = new MetadataContribution { ProviderType = Constants.ProviderType },
                ExistingMetadata = [existing],
            },
            default).ConfigureAwait(false);

        // Assert
        Assert.That(relationships, Is.Empty);
        _client.VerifyNoOtherCalls();
    }

    [Test]
    public async Task GetRelationshipsAsync_ReturnsEmpty_WhenNoContextIsAvailable()
    {
        // Arrange
        _resolver.Setup(resolver => resolver.AcquireContextAsync(
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((RequestContext?)null);
        var item = new ContentHierarchyItem
        {
            Id = new Guid("33333333-3333-3333-3333-333333333333"),
            ContentType = "movie",
            Level = "movie",
            Slug = "fight-club",
            Name = "Fight Club"
        };
        var existing = new MetadataContribution { ProviderType = Constants.ProviderType };
        existing.ExternalIds.Add(new ExternalId { Source = "tmdb", Value = "550" });

        // Act
        var relationships = await _subject.GetRelationshipsAsync(
            new MetadataRelationshipRequest
            {
                Item = item,
                Contribution = new MetadataContribution { ProviderType = Constants.ProviderType },
                ExistingMetadata = [existing],
            },
            default).ConfigureAwait(false);

        // Assert
        Assert.That(relationships, Is.Empty);
        _client.VerifyNoOtherCalls();
    }

    [Test]
    public async Task GetRelationshipsAsync_EmitsCastCrewAndStudios_WhenRouteIsMovie()
    {
        // Arrange
        _client.Setup(client => client.GetAsync<MovieDetailDto>(
                It.IsAny<RequestContext>(),
                "3/movie/550",
                It.IsAny<IReadOnlyList<(string Key, string Value)>?>(),
                ResponseProfile.Detail,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MovieDetailDto
            {
                Id = 550,
                Credits = new CreditsDto
                {
                    Cast =
                    [
                        new CastMemberDto { Id = 819, Name = "Edward Norton", Character = "The Narrator", Order = 0 }
                    ],
                    Crew = [new CrewMemberDto { Id = 7467, Name = "David Fincher", Job = "Director" }]
                },
                ProductionCompanies = [new ProductionCompanyDto { Id = 508, Name = "Regency Enterprises" }]
            });
        var item = new ContentHierarchyItem
        {
            Id = new Guid("44444444-4444-4444-4444-444444444444"),
            ContentType = "movie",
            Level = "movie",
            Slug = "fight-club",
            Name = "Fight Club"
        };
        var existing = new MetadataContribution { ProviderType = Constants.ProviderType };
        existing.ExternalIds.Add(new ExternalId { Source = "tmdb", Value = "550" });

        // Act
        var relationships = await _subject.GetRelationshipsAsync(
            new MetadataRelationshipRequest
            {
                Item = item,
                Contribution = new MetadataContribution { ProviderType = Constants.ProviderType },
                ExistingMetadata = [existing],
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                relationships.Select(relationship => relationship.RelationshipKind),
                Is.EqualTo(["cast", "director", "studio"]));
            Assert.That(
                relationships.Select(relationship => relationship.SubjectName),
                Is.EqualTo(["Edward Norton", "David Fincher", "Regency Enterprises"]));
            Assert.That(
                relationships.Select(relationship => relationship.RelationshipOrdinal),
                Is.EqualTo([0, 1, 2]));
            Assert.That(relationships[0].RelationshipQualifier, Is.EqualTo("The Narrator"));
            Assert.That(relationships[2].SubjectKind, Is.EqualTo("studio"));
        }
    }

    [Test]
    public async Task GetRelationshipsAsync_AppendsNetworks_WhenRouteIsTvShow()
    {
        // Arrange
        _client.Setup(client => client.GetAsync<TvDetailDto>(
                It.IsAny<RequestContext>(),
                "3/tv/1396",
                It.IsAny<IReadOnlyList<(string Key, string Value)>?>(),
                ResponseProfile.Detail,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TvDetailDto
            {
                Id = 1396,
                Credits = new CreditsDto
                {
                    Cast =
                    [
                        new CastMemberDto
                        {
                            Id = 17419,
                            Name = "Bryan Cranston",
                            Character = "Walter White",
                            Order = 0
                        }
                    ]
                },
                ProductionCompanies = [new ProductionCompanyDto { Id = 11073, Name = "Sony Pictures Television" }],
                Networks = [new ProductionCompanyDto { Id = 174, Name = "AMC", OriginCountry = "US" }]
            });
        var item = new ContentHierarchyItem
        {
            Id = new Guid("55555555-5555-5555-5555-555555555555"),
            ContentType = "tv",
            Level = "show",
            Slug = "breaking-bad",
            Name = "Breaking Bad"
        };
        var existing = new MetadataContribution { ProviderType = Constants.ProviderType };
        existing.ExternalIds.Add(new ExternalId { Source = "tmdb", Value = "1396" });

        // Act
        var relationships = await _subject.GetRelationshipsAsync(
            new MetadataRelationshipRequest
            {
                Item = item,
                Contribution = new MetadataContribution { ProviderType = Constants.ProviderType },
                ExistingMetadata = [existing],
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                relationships.Select(relationship => relationship.RelationshipKind),
                Is.EqualTo(["cast", "studio", "network"]));
            Assert.That(
                relationships.Select(relationship => relationship.RelationshipOrdinal),
                Is.EqualTo([0, 1, 2]));
            Assert.That(relationships[2].SubjectName, Is.EqualTo("AMC"));
            Assert.That(
                relationships[2].InitialSubjectContribution!.Source,
                Is.EqualTo("tmdb:network:174"));
        }
    }

    [Test]
    public async Task GetRelationshipsAsync_ReadsTheSeasonEndpoint_WhenRouteIsTvSeason()
    {
        // Arrange
        _client.Setup(client => client.GetAsync<SeasonDetailDto>(
                It.IsAny<RequestContext>(),
                "3/tv/1396/season/2",
                It.IsAny<IReadOnlyList<(string Key, string Value)>?>(),
                ResponseProfile.Detail,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SeasonDetailDto
            {
                Id = 3573,
                Credits = new CreditsDto
                {
                    Cast =
                    [
                        new CastMemberDto { Id = 84497, Name = "Aaron Paul", Character = "Jesse Pinkman", Order = 0 }
                    ]
                }
            });
        var item = new ContentHierarchyItem
        {
            Id = new Guid("66666666-6666-6666-6666-666666666666"),
            ContentType = "tv",
            Level = "season",
            Slug = "season-2",
            Name = "Season 2"
        };
        var seriesMetadata = new MetadataContribution { ProviderType = Constants.ProviderType };
        seriesMetadata.ExternalIds.Add(new ExternalId { Source = "tmdb", Value = "1396" });

        // Act
        var relationships = await _subject.GetRelationshipsAsync(
            new MetadataRelationshipRequest
            {
                Item = item,
                Contribution = new MetadataContribution { ProviderType = Constants.ProviderType },
                ExistingMetadata = [],
                AncestorMetadata = [seriesMetadata],
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(relationships.Single().RelationshipKind, Is.EqualTo("cast"));
            Assert.That(relationships.Single().SubjectName, Is.EqualTo("Aaron Paul"));
        }
    }

    [Test]
    public async Task GetRelationshipsAsync_MergesGuestStars_WhenRouteIsTvEpisode()
    {
        // Arrange
        _client.Setup(client => client.GetAsync<EpisodeDto>(
                It.IsAny<RequestContext>(),
                "3/tv/1396/season/2/episode/3",
                It.IsAny<IReadOnlyList<(string Key, string Value)>?>(),
                ResponseProfile.Detail,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EpisodeDto
            {
                Id = 62141,
                Credits = new CreditsDto
                {
                    Cast =
                    [
                        new CastMemberDto
                        {
                            Id = 17419,
                            Name = "Bryan Cranston",
                            Character = "Walter White",
                            Order = 0
                        }
                    ]
                },
                GuestStars =
                [
                    new CastMemberDto { Id = 21125, Name = "Raymond Cruz", Character = "Tuco Salamanca", Order = 1 }
                ],
                Crew = [new CrewMemberDto { Id = 1223867, Name = "Michelle MacLaren", Job = "Director" }]
            });
        var item = new ContentHierarchyItem
        {
            Id = new Guid("77777777-7777-7777-7777-777777777777"),
            ContentType = "tv",
            Level = "episode",
            Slug = "s02e03",
            Name = "Bit by a Dead Bee"
        };
        var seriesMetadata = new MetadataContribution { ProviderType = Constants.ProviderType };
        seriesMetadata.ExternalIds.Add(new ExternalId { Source = "tmdb", Value = "1396" });

        // Act
        var relationships = await _subject.GetRelationshipsAsync(
            new MetadataRelationshipRequest
            {
                Item = item,
                Contribution = new MetadataContribution { ProviderType = Constants.ProviderType },
                ExistingMetadata = [],
                AncestorMetadata = [seriesMetadata],
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                relationships.Select(relationship => relationship.SubjectName),
                Is.EqualTo(["Bryan Cranston", "Raymond Cruz", "Michelle MacLaren"]));
            Assert.That(
                relationships.Select(relationship => relationship.RelationshipKind),
                Is.EqualTo(["cast", "cast", "director"]));
        }
    }

    [Test]
    public async Task GetRelationshipsAsync_ReturnsEmpty_WhenTheSeriesIdIsUnknownForASeason()
    {
        // Arrange
        var item = new ContentHierarchyItem
        {
            Id = new Guid("88888888-8888-8888-8888-888888888888"),
            ContentType = "tv",
            Level = "season",
            Slug = "season-2",
            Name = "Season 2"
        };

        // Act
        var relationships = await _subject.GetRelationshipsAsync(
            new MetadataRelationshipRequest
            {
                Item = item,
                Contribution = new MetadataContribution { ProviderType = Constants.ProviderType },
                ExistingMetadata = [],
            },
            default).ConfigureAwait(false);

        // Assert
        Assert.That(relationships, Is.Empty);
        _client.VerifyNoOtherCalls();
    }
}
