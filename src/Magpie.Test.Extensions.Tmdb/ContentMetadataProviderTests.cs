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
public class ContentMetadataProviderTests
{
    private Mock<ITmdbClient> _client = null!;
    private IContentHierarchyMetadataProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _client = new Mock<ITmdbClient>();
        var resolver = new Mock<ISettingsResolver>();
        resolver.Setup(subject => subject.AcquireContextAsync(
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RequestContext { ApiKey = "key", Language = "en-US" });
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(subject => subject.GetImageBaseUrlAsync(
                It.IsAny<RequestContext>(),
                It.IsAny<ITmdbClient>(),
                It.IsAny<ILogger>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://image.tmdb.org/t/p/");
        _provider = new ContentMetadataProvider(
            resolver.Object,
            _client.Object,
            cache.Object,
            new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            NullLogger<ContentMetadataProvider>.Instance);
    }

    [Test]
    public async Task TryGetMetadataAsync_AppendsImagesWithLanguageFallback_WhenFetchingMovieDetail()
    {
        // Arrange
        _client.Setup(subject => subject.GetAsync<MovieDetailDto>(
                new RequestContext { ApiKey = "key", Language = "en-US" },
                "3/movie/550",
                It.IsAny<IReadOnlyList<(string Key, string Value)>?>(),
                ResponseProfile.Detail,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MovieDetailDto
            {
                Id = 550,
                Title = "Fight Club",
                PosterPath = "/p1.jpg",
                Images = new ImagesPayloadDto
                {
                    Posters = [new ImageEntryDto { FilePath = "/p2.jpg", LanguageCode = "en" }],
                    Logos = [new ImageEntryDto { FilePath = "/l1.png" }]
                }
            });
        var item = new ContentHierarchyItem
        {
            Id = new Guid("11111111-1111-1111-1111-111111111111"),
            ContentType = "movie",
            Level = "movie",
            Name = "Fight Club",
            Slug = "fight-club"
        };
        var existing = new MetadataContribution { ProviderType = Constants.ProviderType };
        existing.ExternalIds.Add(new ExternalId { Source = Constants.ProviderType, Value = "550" });

        // Act
        var result = await _provider.TryGetMetadataAsync(
            new MetadataFetchRequest
            {
                Item = item,
                ExistingMetadata = [existing],
                DefaultLanguagePreference = "en-US"
            },
            default).ConfigureAwait(false);

        // Assert
        Assert.That(result?.Contribution, Is.Not.Null);
        var images = result!.Contribution!.Images.OrderBy(image => image.Ordinal).ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(images.Select(image => image.Type), Is.EqualTo(["poster", "poster", "logo"]));
            Assert.That(images[1].Language, Is.EqualTo("en"));
            _client.Verify(
                subject => subject.GetAsync<MovieDetailDto>(
                    new RequestContext { ApiKey = "key", Language = "en-US" },
                    "3/movie/550",
                    It.Is<IReadOnlyList<(string Key, string Value)>?>(parameters =>
                        parameters!.Any(parameter =>
                            parameter.Key == "append_to_response" &&
                            parameter.Value ==
                                "external_ids,credits,alternative_titles,release_dates,videos,images") &&
                        parameters!.Any(parameter =>
                            parameter.Key == "include_image_language" &&
                            parameter.Value == "en,null")),
                    ResponseProfile.Detail,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task TryGetMetadataAsync_MapsRatingsAndAlternateTitles_WhenFetchingMovieDetail()
    {
        // Arrange
        _client.Setup(subject => subject.GetAsync<MovieDetailDto>(
                new RequestContext { ApiKey = "key", Language = "en-US" },
                "3/movie/550",
                It.IsAny<IReadOnlyList<(string Key, string Value)>?>(),
                ResponseProfile.Detail,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MovieDetailDto
            {
                Id = 550,
                Title = "Fight Club",
                ReleaseDates = new MovieReleaseDatesDto
                {
                    Results =
                    [
                        new MovieReleaseDateCountryDto
                        {
                            CountryCode = "us",
                            ReleaseDates =
                            [
                                new ReleaseDateEntryDto { Certification = "NC-17", Type = 4 },
                                new ReleaseDateEntryDto { Certification = "R", Type = 3 }
                            ]
                        },
                        new MovieReleaseDateCountryDto
                        {
                            CountryCode = "de",
                            ReleaseDates = [new ReleaseDateEntryDto { Certification = "  ", Type = 3 }]
                        }
                    ]
                },
                AlternativeTitles = new MovieAlternativeTitlesDto
                {
                    Titles =
                    [
                        new AlternativeTitleEntryDto { Title = "El club de la lucha", Type = "working title" },
                        new AlternativeTitleEntryDto { Title = "Le Club de combat", Type = "  " }
                    ]
                }
            });
        var item = new ContentHierarchyItem
        {
            Id = new Guid("44444444-4444-4444-4444-444444444444"),
            ContentType = "movie",
            Level = "movie",
            Name = "Fight Club",
            Slug = "fight-club"
        };
        var existing = new MetadataContribution { ProviderType = Constants.ProviderType };
        existing.ExternalIds.Add(new ExternalId { Source = Constants.ProviderType, Value = "550" });

        // Act
        var result = await _provider.TryGetMetadataAsync(
            new MetadataFetchRequest
            {
                Item = item,
                ExistingMetadata = [existing],
                DefaultLanguagePreference = "en-US"
            },
            default).ConfigureAwait(false);

        // Assert
        Assert.That(result?.Contribution, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result!.Contribution!.ContentRatings.Select(rating => rating.Body),
                Is.EqualTo(["US"]));
            Assert.That(
                result.Contribution.ContentRatings.Select(rating => rating.Rating),
                Is.EqualTo(["R"]));
            Assert.That(
                result.Contribution.AlternateTexts.Select(text => text.Kind),
                Is.EqualTo(["working title", "title"]));
            Assert.That(
                result.Contribution.AlternateTexts.Select(text => text.Text),
                Is.EqualTo(["El club de la lucha", "Le Club de combat"]));
            Assert.That(
                result.Contribution.AlternateTexts.Select(text => text.Ordinal),
                Is.EqualTo([0, 1]));
        }
    }

    [Test]
    public async Task TryGetMetadataAsync_MapsRatingsAndSeasonChildren_WhenFetchingTvDetail()
    {
        // Arrange
        _client.Setup(subject => subject.GetAsync<TvDetailDto>(
                new RequestContext { ApiKey = "key", Language = "en-US" },
                "3/tv/1396",
                It.IsAny<IReadOnlyList<(string Key, string Value)>?>(),
                ResponseProfile.Detail,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TvDetailDto
            {
                Id = 1396,
                Name = "Breaking Bad",
                ContentRatings = new TvContentRatingsDto
                {
                    Results = [new TvContentRatingEntryDto { CountryCode = "us", Rating = "TV-MA" }]
                },
                AlternativeTitles = new TvAlternativeTitlesDto
                {
                    Results = [new AlternativeTitleEntryDto { Title = "Totalmente Quebrado", Type = "BR" }]
                },
                Seasons =
                [
                    new TvSeasonSummaryDto { Id = 3572, SeasonNumber = 1, Name = "Season 1", PosterPath = "/s1.jpg" }
                ]
            });
        _client.Setup(subject => subject.GetAsync<SeasonDetailDto>(
                new RequestContext { ApiKey = "key", Language = "en-US" },
                "3/tv/1396/season/1",
                It.IsAny<IReadOnlyList<(string Key, string Value)>?>(),
                ResponseProfile.Detail,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SeasonDetailDto
            {
                Id = 3572,
                SeasonNumber = 1,
                ExternalIds = new ExternalIdsDto { TvdbId = 30272 },
                Episodes =
                [
                    new EpisodeDto
                    {
                        Id = 62085,
                        SeasonNumber = 1,
                        EpisodeNumber = 1,
                        Name = "Pilot",
                        Runtime = 58,
                        StillPath = "/still.jpg"
                    }
                ]
            });
        var item = new ContentHierarchyItem
        {
            Id = new Guid("55555555-5555-5555-5555-555555555555"),
            ContentType = "tv",
            Level = "show",
            Name = "Breaking Bad",
            Slug = "breaking-bad"
        };
        var existing = new MetadataContribution { ProviderType = Constants.ProviderType };
        existing.ExternalIds.Add(new ExternalId { Source = Constants.ProviderType, Value = "1396" });

        // Act
        var result = await _provider.TryGetMetadataAsync(
            new MetadataFetchRequest
            {
                Item = item,
                ExistingMetadata = [existing],
                DefaultLanguagePreference = "en-US"
            },
            default).ConfigureAwait(false);

        // Assert
        Assert.That(result?.Contribution, Is.Not.Null);
        var season = result!.Children.Single();
        var episode = season.Children.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Contribution!.ContentRatings.Single().Body, Is.EqualTo("US"));
            Assert.That(result.Contribution.ContentRatings.Single().Rating, Is.EqualTo("TV-MA"));
            Assert.That(result.Contribution.AlternateTexts.Single().Kind, Is.EqualTo("BR"));
            Assert.That(result.Contribution.AlternateTexts.Single().Text, Is.EqualTo("Totalmente Quebrado"));
            Assert.That(season.Level, Is.EqualTo("season"));
            Assert.That(season.Name, Is.EqualTo("Season 1"));
            Assert.That(season.Slug, Is.EqualTo("season-1"));
            Assert.That(season.Ordinal, Is.EqualTo(1));
            Assert.That(season.AlignmentKey, Is.EqualTo("season:1"));
            Assert.That(season.ExternalIds["tmdb"], Is.EqualTo("3572"));
            Assert.That(season.InitialContribution!.Source, Is.EqualTo("tmdb:season:3572"));
            Assert.That(
                season.InitialContribution.ExternalIds.Select(externalId => externalId.Source),
                Is.EqualTo(["tmdb", "tvdb"]));
            Assert.That(
                season.InitialContribution.Images.Single().Url.ToString(),
                Is.EqualTo("https://image.tmdb.org/t/p/w500/s1.jpg"));
            Assert.That(episode.Level, Is.EqualTo("episode"));
            Assert.That(episode.Name, Is.EqualTo("Pilot"));
            Assert.That(episode.Slug, Is.EqualTo("s01e01"));
            Assert.That(episode.Ordinal, Is.EqualTo(1));
            Assert.That(episode.AlignmentKey, Is.EqualTo("episode:1:1"));
            Assert.That(episode.ExternalIds["tmdb"], Is.EqualTo("62085"));
            Assert.That(episode.InitialContribution!.Source, Is.EqualTo("tmdb:episode:62085"));
            Assert.That(episode.InitialContribution.Duration, Is.EqualTo(TimeSpan.FromMinutes(58)));
            Assert.That(
                episode.InitialContribution.Images.Single().Url.ToString(),
                Is.EqualTo("https://image.tmdb.org/t/p/w300/still.jpg"));
        }
    }

    [Test]
    public async Task TryGetMetadataAsync_MapsSeasonContribution_WhenItemIsATvSeason()
    {
        // Arrange
        _client.Setup(subject => subject.GetAsync<SeasonDetailDto>(
                new RequestContext { ApiKey = "key", Language = "en-US" },
                "3/tv/1396/season/2",
                It.IsAny<IReadOnlyList<(string Key, string Value)>?>(),
                ResponseProfile.Detail,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SeasonDetailDto
            {
                Id = 3573,
                SeasonNumber = 2,
                Name = "Season 2",
                Overview = "Walt and Jesse face the fallout.",
                AirDate = "2009-03-08",
                PosterPath = "/s2.jpg",
                ExternalIds = new ExternalIdsDto { TvdbId = 30273 }
            });
        var item = new ContentHierarchyItem
        {
            Id = new Guid("66666666-6666-6666-6666-666666666666"),
            ContentType = "tv",
            Level = "season",
            Name = "Season 2",
            Slug = "season-2"
        };
        var seriesMetadata = new MetadataContribution { ProviderType = Constants.ProviderType };
        seriesMetadata.ExternalIds.Add(new ExternalId { Source = Constants.ProviderType, Value = "1396" });

        // Act
        var result = await _provider.TryGetMetadataAsync(
            new MetadataFetchRequest
            {
                Item = item,
                ExistingMetadata = [],
                AncestorMetadata = [seriesMetadata],
                DefaultLanguagePreference = "en-US"
            },
            default).ConfigureAwait(false);

        // Assert
        Assert.That(result?.Contribution, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.Contribution!.Source, Is.EqualTo("tmdb:season:3573"));
            Assert.That(result.Contribution.Title, Is.EqualTo("Season 2"));
            Assert.That(result.Contribution.Overview, Is.EqualTo("Walt and Jesse face the fallout."));
            Assert.That(result.Contribution.SequenceNumber, Is.EqualTo(2));
            Assert.That(result.Contribution.PublishDate, Is.EqualTo(new DateTime(2009, 3, 8)));
            Assert.That(
                result.Contribution.ExternalIds.Select(externalId => externalId.Source),
                Is.EqualTo(["tmdb", "tvdb"]));
            Assert.That(
                result.Contribution.Images.Single().Url.ToString(),
                Is.EqualTo("https://image.tmdb.org/t/p/w500/s2.jpg"));
            Assert.That(result.Children, Is.Empty);
        }
    }

    [Test]
    public async Task TryGetMetadataAsync_MapsEpisodeContribution_WhenItemIsATvEpisode()
    {
        // Arrange
        _client.Setup(subject => subject.GetAsync<EpisodeDto>(
                new RequestContext { ApiKey = "key", Language = "en-US" },
                "3/tv/1396/season/2/episode/3",
                It.IsAny<IReadOnlyList<(string Key, string Value)>?>(),
                ResponseProfile.Detail,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EpisodeDto
            {
                Id = 62141,
                SeasonNumber = 2,
                EpisodeNumber = 3,
                Name = "Bit by a Dead Bee",
                Overview = "Walt fakes amnesia.",
                AirDate = "2009-03-22",
                Runtime = 47,
                StillPath = "/still.jpg",
                ExternalIds = new ExternalIdsDto { ImdbId = "tt1615186" }
            });
        var item = new ContentHierarchyItem
        {
            Id = new Guid("77777777-7777-7777-7777-777777777777"),
            ContentType = "tv",
            Level = "episode",
            Name = "Bit by a Dead Bee",
            Slug = "s02e03"
        };
        var seriesMetadata = new MetadataContribution { ProviderType = Constants.ProviderType };
        seriesMetadata.ExternalIds.Add(new ExternalId { Source = Constants.ProviderType, Value = "1396" });

        // Act
        var result = await _provider.TryGetMetadataAsync(
            new MetadataFetchRequest
            {
                Item = item,
                ExistingMetadata = [],
                AncestorMetadata = [seriesMetadata],
                DefaultLanguagePreference = "en-US"
            },
            default).ConfigureAwait(false);

        // Assert
        Assert.That(result?.Contribution, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.Contribution!.Source, Is.EqualTo("tmdb:episode:62141"));
            Assert.That(result.Contribution.Title, Is.EqualTo("Bit by a Dead Bee"));
            Assert.That(result.Contribution.Overview, Is.EqualTo("Walt fakes amnesia."));
            Assert.That(result.Contribution.SequenceNumber, Is.EqualTo(3));
            Assert.That(result.Contribution.PublishDate, Is.EqualTo(new DateTime(2009, 3, 22)));
            Assert.That(result.Contribution.Duration, Is.EqualTo(TimeSpan.FromMinutes(47)));
            Assert.That(
                result.Contribution.ExternalIds.Select(externalId => externalId.Source),
                Is.EqualTo(["tmdb", "imdb"]));
            Assert.That(
                result.Contribution.Images.Single().Url.ToString(),
                Is.EqualTo("https://image.tmdb.org/t/p/w300/still.jpg"));
        }
    }

    [Test]
    public async Task TryGetMetadataAsync_ReturnsNull_WhenNoTmdbIdIsPresent()
    {
        // Arrange
        var item = new ContentHierarchyItem
        {
            Id = new Guid("22222222-2222-2222-2222-222222222222"),
            ContentType = "movie",
            Level = "movie",
            Name = "Fight Club",
            Slug = "fight-club"
        };

        // Act
        var result = await _provider.TryGetMetadataAsync(
            new MetadataFetchRequest
            {
                Item = item,
                ExistingMetadata = [],
                DefaultLanguagePreference = "en-US"
            },
            default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Null);
        _client.VerifyNoOtherCalls();
    }

    [Test]
    public async Task TryGetMetadataAsync_ReturnsNull_WhenContentTypeIsUnsupported()
    {
        // Arrange
        var item = new ContentHierarchyItem
        {
            Id = new Guid("33333333-3333-3333-3333-333333333333"),
            ContentType = "book",
            Level = "book",
            Name = "Dune",
            Slug = "dune"
        };

        // Act
        var result = await _provider.TryGetMetadataAsync(
            new MetadataFetchRequest
            {
                Item = item,
                ExistingMetadata = [],
                DefaultLanguagePreference = "en-US"
            },
            default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Null);
        _client.VerifyNoOtherCalls();
    }
}
