using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Extensions.Tv;
using Magpie.Models.Data;
using Magpie.Models.Releases;

using Microsoft.Extensions.Logging.Abstractions;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Tv;

[TestFixture]
public sealed class SequenceNumberTests
{
    private TvContentTypeDefinitionProvider _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _subject = new TvContentTypeDefinitionProvider(NullLogger<TvContentTypeDefinitionProvider>.Instance);
    }

    [TestCase("show", "season", 10, 17, "17", true)]
    [TestCase("show", "season", 10, 17, "10", false)]
    [TestCase("show", "season", 10, 0, "0", true)]
    [TestCase("show", "season", 10, null, "10", true)]
    [TestCase("show", "season", null, null, "10", false)]
    [TestCase("season", "episode", 1, 5, "5", true)]
    [TestCase("season", "episode", 1, 5, "1", false)]
    [TestCase("season", "episode", null, null, "1", false)]
    public void ResolveFileChildren_UsesSequenceNumber_WhenContributionsDifferFromOrder(
        string parentLevel,
        string childLevel,
        int? providerNumber,
        int? userNumber,
        string signal,
        bool expectedMatch)
    {
        // Arrange
        var parent = new ContentHierarchyItem { ContentType = "tv", Level = parentLevel, Name = "Show", Slug = "show" };
        var child = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ContentType = "tv",
            Level = childLevel,
            Name = "Series 17",
            Slug = "season-10",
            Ordinal = 10,
            MetadataContainer = new()
            {
                Contributions =
                {
                    new() { ProviderType = "tmdb", Ordinal = 0, SequenceNumber = providerNumber, Language = "en" },
                    new() { ProviderType = "user", Ordinal = -1, SequenceNumber = userNumber }
                }
            }
        };

        // Act
        var result = _subject.ResolveFileChildren(
            parent,
            [child],
            new Dictionary<string, List<string>> { [childLevel] = [signal] });

        // Assert
        Assert.That(result, Is.EqualTo(expectedMatch ? [child.Id] : Array.Empty<Guid>()));
    }

    [TestCase(6, new[] { "00000000-0000-0000-0000-000000000005", "00000000-0000-0000-0000-000000000006" })]
    [TestCase(5, new string[0])]
    [TestCase(null, new string[0])]
    public void ResolveFileChildren_ReturnsCompleteUnambiguousCoverage_WhenEpisodeNumbersDifferFromOrder(
        int? secondNumber,
        string[] expected)
    {
        // Arrange
        var parent = new ContentHierarchyItem { ContentType = "tv", Level = "season", Name = "Season", Slug = "s" };
        ContentHierarchyItem[] children =
        [
            new()
            {
                Id = new Guid("00000000-0000-0000-0000-000000000006"),
                ContentType = "tv",
                Level = "episode",
                Name = "Second",
                Slug = "second",
                Ordinal = 1,
                MetadataContainer = new()
                {
                    Contributions = { new() { ProviderType = "user", SequenceNumber = secondNumber } }
                }
            },
            new()
            {
                Id = new Guid("00000000-0000-0000-0000-000000000005"),
                ContentType = "tv",
                Level = "episode",
                Name = "First",
                Slug = "first",
                Ordinal = 2,
                MetadataContainer = new() { Contributions = { new() { ProviderType = "user", SequenceNumber = 5 } } }
            }
        ];
        var parsed = _subject.Parse("Show.S17E05-E06.mkv", out _)!;

        // Act
        var result = _subject.ResolveFileChildren(
            parent,
            children,
            parsed.ToDictionary(pair => pair.Key, pair => pair.Value.ToList()));

        // Assert
        Assert.That(result, Is.EqualTo(expected.Select(Guid.Parse)));
    }

    [TestCase(10, 17, 1, 5, "Show/Show - S17E05 - Pilot.mkv")]
    [TestCase(10, 0, 1, 5, "Show/Show - S00E05 - Pilot.mkv")]
    [TestCase(null, null, 1, 5, "Show/source.mkv")]
    [TestCase(10, 17, null, null, "Show/source.mkv")]
    public void BuildPath_UsesSequenceNumbers_WhenHierarchyOrderDiffers(
        int? providerSeasonNumber,
        int? seasonNumber,
        int? providerEpisodeNumber,
        int? episodeNumber,
        string expected)
    {
        // Arrange
        ContentHierarchyItem[] ancestors =
        [
            new() { ContentType = "tv", Level = "show", Name = "Show", Slug = "show" },
            new()
            {
                ContentType = "tv",
                Level = "season",
                Name = "Season 10",
                Slug = "season-10",
                Ordinal = 10,
                MetadataContainer = new()
                {
                    Contributions =
                    {
                        new()
                        {
                            ProviderType = "tmdb",
                            Ordinal = 0,
                            SequenceNumber = providerSeasonNumber,
                            Language = "en"
                        },
                        new() { ProviderType = "user", Ordinal = -1, SequenceNumber = seasonNumber }
                    }
                }
            },
            new()
            {
                ContentType = "tv",
                Level = "episode",
                Name = "Pilot",
                Slug = "pilot",
                Ordinal = 1,
                MetadataContainer = new()
                {
                    Contributions =
                    {
                        new() { ProviderType = "tmdb", Ordinal = 0, SequenceNumber = providerEpisodeNumber },
                        new() { ProviderType = "user", Ordinal = -1, SequenceNumber = episodeNumber }
                    }
                }
            }
        ];

        // Act
        var path = _subject.BuildPath(
            ancestors,
            new Dictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
            "source",
            "mkv");

        // Assert
        Assert.That(path, Is.EqualTo(expected));
    }

    [TestCase("episode", "Show.S10E01", 1)]
    [TestCase("season", "Show.S10", 0)]
    public async Task GetFactsAsync_OmitsNumberFacts_WhenMetadataIsAbsent(
        string targetLevel,
        string releaseTitle,
        int expectedFactCount)
    {
        // Arrange
        var show = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ContentType = "tv",
            Level = "show",
            Name = "Show",
            Slug = "show"
        };
        var season = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ContentType = "tv",
            Level = "season",
            Name = "Series 17",
            Slug = "season-10",
            Ordinal = 10
        };
        var episode = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ContentType = "tv",
            Level = "episode",
            Name = "Pilot",
            Slug = "episode-1",
            Ordinal = 1
        };
        var target = new ContentTarget
        {
            Item = targetLevel == "season" ? season : episode,
            Lineage = targetLevel == "season" ? [season, show] : [episode, season, show],
            Metadata = new Dictionary<Guid, MaterializedMetadata> { [show.Id] = new() { Title = "Show" } }
        };

        // Act
        var facts = await new ContentFactProvider().GetFactsAsync(target).ConfigureAwait(false);
        var match = _subject.IsMatch(target, facts, new Release { Protocol = "torrent", Title = releaseTitle }, []);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(facts, Has.Count.EqualTo(expectedFactCount));
            Assert.That(match, Is.EqualTo(ReleaseMatchResult.Indeterminate));
        }
    }

    [Test]
    public async Task GetFactsAsync_KeepsSeasonIdentity_WhenSequenceNumberIsMissing()
    {
        // Arrange
        var season = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ContentType = "tv",
            Level = "season",
            Name = "Series 17",
            Slug = "season-10",
            Ordinal = 10
        };
        var target = new ContentTarget
        {
            Item = season,
            Lineage = [season],
            Metadata = new Dictionary<Guid, MaterializedMetadata>
            {
                [season.Id] = new() { ExternalIds = [new() { Source = "tmdb", Value = "123" }] }
            }
        };

        // Act
        var facts = await new ContentFactProvider().GetFactsAsync(target).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(facts, Has.Count.EqualTo(1));
            Assert.That(facts[0], Has.Count.EqualTo(1));
            Assert.That(facts[0][0].Value, Is.Null);
            Assert.That(facts[0][0].Facets, Is.EquivalentTo(new Dictionary<string, string> { ["tmdb"] = "123" }));
        }
    }
}
