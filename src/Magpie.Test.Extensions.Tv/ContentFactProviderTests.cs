using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Magpie.Extensions.Tv;
using Magpie.Models.Data;
using Magpie.Models.Releases;
using Magpie.Sdk.Content;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Tv;

[TestFixture]
public sealed class ContentFactProviderTests
{
    private IContentFactProvider _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _subject = new ContentFactProvider();
    }

    [TestCase("tv", true)]
    [TestCase("movie", false)]
    public void CanProvideFacts_ReturnsExpected_WhenContentTypeIsGiven(string contentType, bool expected)
    {
        // Arrange
        var item = new ContentHierarchyItem
        {
            ContentType = contentType,
            Level = "show",
            Slug = "show",
            Name = "Show",
        };
        var target = new ContentTarget
        {
            Item = item,
            Lineage = [item],
            Metadata = new Dictionary<Guid, MaterializedMetadata>(),
        };

        // Act
        var result = _subject.CanProvideFacts(target);

        // Assert
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public async Task GetFactsAsync_ReturnsEpisodeFacts_ForTvTarget()
    {
        // Arrange
        var showId = new Guid("11111111-1111-1111-1111-111111111111");
        var seasonId = new Guid("22222222-2222-2222-2222-222222222222");
        var episodeId = new Guid("33333333-3333-3333-3333-333333333333");
        var show = new ContentHierarchyItem
        {
            Id = showId,
            ContentType = Constants.ContentTypeSlug,
            Level = Constants.ShowLevelSlug,
            Slug = "phineas-and-ferb",
            Name = "Phineas and Ferb",
            MetadataContainerId = showId,
        };
        var season = new ContentHierarchyItem
        {
            Id = seasonId,
            ContentType = Constants.ContentTypeSlug,
            Level = Constants.SeasonLevelSlug,
            Slug = "season-1",
            Name = "Season 1",
            Ordinal = 1,
            MetadataContainerId = seasonId,
        };
        var episode = new ContentHierarchyItem
        {
            Id = episodeId,
            ContentType = Constants.ContentTypeSlug,
            Level = Constants.EpisodeLevelSlug,
            Slug = "s01e01",
            Name = "Rollercoaster",
            Ordinal = 1,
            MetadataContainerId = episodeId,
        };
        var target = new ContentTarget
        {
            Item = episode,
            Lineage = [episode, season, show],
            Metadata = new Dictionary<Guid, MaterializedMetadata>
            {
                [showId] = new()
                {
                    Title = "Phineas and Ferb",
                    ExternalIds =
                    [
                        new ExternalId { Source = "tmdb", Value = "42", Ordinal = 0 },
                        new ExternalId { Source = "imdb", Value = "tt0851577", Ordinal = 1 },
                    ],
                },
                [seasonId] = new() { Title = "Season 1" },
                [episodeId] = new()
                {
                    Title = "Rollercoaster",
                    ExternalIds = [new ExternalId { Source = "tmdb", Value = "200", Ordinal = 0 }],
                },
            },
        };

        // Act
        var factSets = await _subject.GetFactsAsync(target).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(factSets, Has.Count.EqualTo(5));
            Assert.That(
                factSets,
                Has.Some.Matches<IReadOnlyList<ContentFact>>(facts =>
                    facts.Count == 2 &&
                    facts[0].Value == "Phineas and Ferb" &&
                    facts[1].Value == "Rollercoaster"));
            Assert.That(
                factSets,
                Has.Some.Matches<IReadOnlyList<ContentFact>>(facts =>
                    facts.Count == 2 &&
                    facts[0].Value == "Phineas and Ferb" &&
                    facts[1].Value == "S01E01" &&
                    facts[1].Facets.TryGetValue("season", out var seasonFacet) &&
                    seasonFacet == "1" &&
                    facts[1].Facets.TryGetValue("episode", out var episodeFacet) &&
                    episodeFacet == "1"));
            Assert.That(
                factSets,
                Has.Some.Matches<IReadOnlyList<ContentFact>>(facts =>
                    facts.Count == 2 &&
                    facts[0].Facets.TryGetValue("tmdb", out var tmdbShow) &&
                    tmdbShow == "42" &&
                    facts[1].Value == "S01E01"));
            Assert.That(
                factSets,
                Has.Some.Matches<IReadOnlyList<ContentFact>>(facts =>
                    facts.Count == 2 &&
                    facts[0].Facets.TryGetValue("imdb", out var imdbShow) &&
                    imdbShow == "tt0851577" &&
                    facts[1].Value == "S01E01"));
            Assert.That(
                factSets,
                Has.Some.Matches<IReadOnlyList<ContentFact>>(facts =>
                    facts.Count == 1 &&
                    facts[0].Facets.TryGetValue("tmdb", out var tmdbEpisode) &&
                    tmdbEpisode == "200"));
        }
    }

    [Test]
    public async Task GetFactsAsync_ReturnsSeasonFacts_ForTvTarget()
    {
        // Arrange
        var showId = new Guid("11111111-1111-1111-1111-111111111111");
        var seasonId = new Guid("22222222-2222-2222-2222-222222222222");
        var show = new ContentHierarchyItem
        {
            Id = showId,
            ContentType = Constants.ContentTypeSlug,
            Level = Constants.ShowLevelSlug,
            Slug = "phineas-and-ferb",
            Name = "Phineas and Ferb",
            MetadataContainerId = showId,
        };
        var season = new ContentHierarchyItem
        {
            Id = seasonId,
            ContentType = Constants.ContentTypeSlug,
            Level = Constants.SeasonLevelSlug,
            Slug = "season-1",
            Name = "Season 1",
            Ordinal = 1,
            MetadataContainerId = seasonId,
        };
        var target = new ContentTarget
        {
            Item = season,
            Lineage = [season, show],
            Metadata = new Dictionary<Guid, MaterializedMetadata>
            {
                [showId] = new()
                {
                    Title = "Phineas and Ferb",
                    ExternalIds = [new ExternalId { Source = "tmdb", Value = "42", Ordinal = 0 }],
                },
                [seasonId] = new()
                {
                    Title = "Season 1",
                    ExternalIds = [new ExternalId { Source = "tmdb", Value = "100", Ordinal = 0 }],
                },
            },
        };

        // Act
        var factSets = await _subject.GetFactsAsync(target).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(factSets, Has.Count.EqualTo(3));
            Assert.That(
                factSets,
                Has.Some.Matches<IReadOnlyList<ContentFact>>(facts =>
                    facts.Count == 2 &&
                    facts[0].Value == "Phineas and Ferb" &&
                    facts[1].Value == "Season 1" &&
                    facts[1].Facets.TryGetValue("season", out var seasonFacet) &&
                    seasonFacet == "1"));
            Assert.That(
                factSets,
                Has.Some.Matches<IReadOnlyList<ContentFact>>(facts =>
                    facts.Count == 2 &&
                    facts[0].Facets.TryGetValue("tmdb", out var tmdbShow) &&
                    tmdbShow == "42" &&
                    facts[1].Value == "Season 1"));
            Assert.That(
                factSets,
                Has.Some.Matches<IReadOnlyList<ContentFact>>(facts =>
                    facts.Count == 1 &&
                    facts[0].Facets.TryGetValue("tmdb", out var tmdbSeason) &&
                    tmdbSeason == "100"));
        }
    }

    [Test]
    public async Task GetFactsAsync_ReturnsEmpty_ForShowTarget()
    {
        // Arrange
        var show = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ContentType = Constants.ContentTypeSlug,
            Level = Constants.ShowLevelSlug,
            Slug = "show",
            Name = "Show",
        };
        var target = new ContentTarget
        {
            Item = show,
            Lineage = [show],
            Metadata = new Dictionary<Guid, MaterializedMetadata>(),
        };

        // Act
        var factSets = await _subject.GetFactsAsync(target).ConfigureAwait(false);

        // Assert
        Assert.That(factSets, Is.Empty);
    }
}
