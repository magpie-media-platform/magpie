using System;
using System.Collections.Generic;
using System.Linq;

using Magpie.Extensions.Tv;
using Magpie.Models.Data;
using Magpie.Models.Releases;
using Magpie.Sdk.Content;

using Microsoft.Extensions.Logging.Abstractions;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Tv;

[TestFixture]
public class TvContentTypeDefinitionProviderTests
{
    private IReleaseMatchContentTypeDefinitionProvider _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _subject = new TvContentTypeDefinitionProvider(NullLogger<TvContentTypeDefinitionProvider>.Instance);
    }

    [Test]
    public void Definition_Result_WhenPathContributionsAreRead()
    {
        // Arrange
        // Act
        var contributions = _subject.Definition.LevelDefinitions.Select(level => level.Subfolder is not null);

        // Assert
        Assert.That(contributions, Is.EqualTo([true, false, false]));
    }

    [Test]
    public void BuildPath_Result_WhenEpisodeHasSeasonAndEpisodeNumbers()
    {
        // Arrange
        ContentHierarchyItem[] ancestors =
        [
            new() { ContentType = "tv", Level = "show", Slug = "example", Name = "Example", MetadataContainer = new() },
            new() { ContentType = "tv", Level = "season", Slug = "season-1", Name = "Season 1", Ordinal = 1 },
            new() { ContentType = "tv", Level = "episode", Slug = "pilot", Name = "Pilot", Ordinal = 2 }
        ];
        ancestors[0].MetadataContainer!.Contributions.Add(
            new() { ProviderType = "test", PublishDate = new(2020, 1, 1) });

        // Act
        var path = ((IPathResolutionContentTypeDefinitionProvider)_subject).BuildPath(
            ancestors, new Dictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<string>>>(), "source", "mkv");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(path, Is.EqualTo("Example (2020)/Example - S01E02 - Pilot.mkv"));
        }
    }

    [TestCase("show", "Phineas.and.Ferb.S01E01.1080p", 1, 1, false, ReleaseMatchResult.Confident)]
    [TestCase("episode", "Phineas.and.Ferb.S01E01.1080p", 1, 1, false, ReleaseMatchResult.Confident)]
    [TestCase("episode", "Phineas.and.Ferb.S01E02.1080p", 1, 1, false, ReleaseMatchResult.NoMatch)]
    [TestCase("episode", "Phineas.and.Ferb.S01E01-E03.1080p", 1, 2, false, ReleaseMatchResult.Confident)]
    [TestCase("episode", "Phineas.and.Ferb.S01.1080p", 1, 12, false, ReleaseMatchResult.Confident)]
    [TestCase("season", "Phineas.and.Ferb.S01-S03.1080p", 2, 1, false, ReleaseMatchResult.Confident)]
    [TestCase("episode", "Phineas.and.Ferb.S01-S03.1080p", 2, 14, false, ReleaseMatchResult.Confident)]
    [TestCase("episode", "Phineas.and.Ferb.Complete.Series.1080p", 2, 14, false, ReleaseMatchResult.Confident)]
    [TestCase("episode", "Gravity.Falls.S01E01.1080p", 1, 1, false, ReleaseMatchResult.NoMatch)]
    [TestCase(
        "episode",
        "Phineas.and.Ferb.Rollercoaster.1080p",
        1,
        1,
        true,
        ReleaseMatchResult.Confident)]
    public void IsMatch_Result_WhenTvReleaseShapeIsEvaluated(
        string targetLevel,
        string releaseTitle,
        int seasonNumber,
        int episodeNumber,
        bool useTitleFacts,
        ReleaseMatchResult expected)
    {
        // Arrange
        var showId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        var showContainer = new MetadataContainer();
        showContainer.Contributions.Add(new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Phineas and Ferb",
        });
        var seasonContainer = new MetadataContainer();
        seasonContainer.Contributions.Add(new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Season " + seasonNumber,
        });
        var episodeContainer = new MetadataContainer();
        episodeContainer.Contributions.Add(new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Rollercoaster",
        });
        var show = new ContentHierarchyItem
        {
            Id = showId,
            ContentType = "tv",
            Level = "show",
            Slug = "phineas-and-ferb",
            Name = "Phineas and Ferb",
            CreatedAt = DateTime.UtcNow,
            MetadataContainer = showContainer,
        };
        var season = new ContentHierarchyItem
        {
            Id = seasonId,
            ParentId = showId,
            ContentType = "tv",
            Level = "season",
            Slug = "season-" + seasonNumber,
            Name = "Season " + seasonNumber,
            Ordinal = seasonNumber,
            CreatedAt = DateTime.UtcNow,
            MetadataContainer = seasonContainer,
        };
        var episode = new ContentHierarchyItem
        {
            Id = episodeId,
            ParentId = seasonId,
            ContentType = "tv",
            Level = "episode",
            Slug = "rollercoaster",
            Name = "Rollercoaster",
            Ordinal = episodeNumber,
            CreatedAt = DateTime.UtcNow,
            MetadataContainer = episodeContainer,
        };
        var targetItem = targetLevel switch
        {
            "show" => show,
            "season" => season,
            _ => episode,
        };
        IReadOnlyList<ContentHierarchyItem> lineage = targetLevel switch
        {
            "show" => [show],
            "season" => [season, show],
            _ => [episode, season, show],
        };
        var target = new ContentTarget
        {
            Item = targetItem,
            Lineage = lineage,
            Metadata = new Dictionary<Guid, MaterializedMetadata>
            {
                [showId] = new() { Title = "Phineas and Ferb" },
            },
        };
        IReadOnlyList<ContentFact> releaseFacts = useTitleFacts
            ?
            [
                new ContentFact { Value = "Phineas and Ferb" },
                new ContentFact { Value = "Rollercoaster" },
            ]
            : [];

        // Act
        var result = _subject.IsMatch(
            target,
            [],
            new Release { Protocol = "torrent", Title = releaseTitle },
            releaseFacts);

        // Assert
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase("Blue Box (Season 1)", ReleaseMatchResult.Indeterminate)]
    [TestCase("Blue Archive The Animation (Season 1)", ReleaseMatchResult.Indeterminate)]
    [TestCase("Blue Ridge The Series S01E01", ReleaseMatchResult.Indeterminate)]
    [TestCase("Blue Ridge The Series S01E06", ReleaseMatchResult.Indeterminate)]
    [TestCase("Blue Bloods 2010 2024 Complete Series", ReleaseMatchResult.Indeterminate)]
    [TestCase("Bluey Series 1 (2018)", ReleaseMatchResult.Confident)]
    [TestCase("Bluey (2018 2019 TV series)", ReleaseMatchResult.Indeterminate)]
    [TestCase("Bluey 1976 Season 1 Complete", ReleaseMatchResult.NoMatch)]
    [TestCase("Bluey (1976) Season 1", ReleaseMatchResult.NoMatch)]
    public void IsMatch_Result_WhenBlueySeasonReleaseIsEvaluated(
        string releaseTitle,
        ReleaseMatchResult expected)
    {
        // Arrange
        var showId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        var show = new ContentHierarchyItem
        {
            Id = showId,
            ContentType = "tv",
            Level = "show",
            Slug = "bluey",
            Name = "Bluey",
        };
        var season = new ContentHierarchyItem
        {
            Id = seasonId,
            ParentId = showId,
            ContentType = "tv",
            Level = "season",
            Slug = "series-1",
            Name = "Series 1",
            Ordinal = 1,
        };
        var target = new ContentTarget
        {
            Item = season,
            Lineage = [season, show],
            Metadata = new Dictionary<Guid, MaterializedMetadata>
            {
                [showId] = new()
                {
                    Title = "Bluey",
                    PublishDate = new DateTime(2018, 10, 1),
                    AlternateTexts = [new AlternateText { Kind = "Title", Text = "Blue" }],
                },
            },
        };

        // Act
        var result = _subject.IsMatch(
            target,
            [],
            new Release { Protocol = "torrent", Title = releaseTitle },
            []);

        // Assert
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase(
        "Blue Bloods",
        2010,
        "Blue Bloods 2010 2024 Complete Series",
        ReleaseMatchResult.Confident)]
    [TestCase("The Office", 2005, "The Office US S01E01", ReleaseMatchResult.Indeterminate)]
    [TestCase("Bluey", 2018, "Bluey 2018 2024 S01E01", ReleaseMatchResult.Confident)]
    public void IsMatch_Result_WhenDatedSeasonReleaseIsEvaluated(
        string targetTitle,
        int targetYear,
        string releaseTitle,
        ReleaseMatchResult expected)
    {
        // Arrange
        var showId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        var show = new ContentHierarchyItem
        {
            Id = showId,
            ContentType = "tv",
            Level = "show",
            Slug = "target-show",
            Name = targetTitle,
        };
        var season = new ContentHierarchyItem
        {
            Id = seasonId,
            ParentId = showId,
            ContentType = "tv",
            Level = "season",
            Slug = "season-1",
            Name = "Season 1",
            Ordinal = 1,
        };
        var target = new ContentTarget
        {
            Item = season,
            Lineage = [season, show],
            Metadata = new Dictionary<Guid, MaterializedMetadata>
            {
                [showId] = new()
                {
                    Title = targetTitle,
                    PublishDate = new DateTime(targetYear, 1, 1),
                },
            },
        };

        // Act
        var result = _subject.IsMatch(
            target,
            [],
            new Release { Protocol = "torrent", Title = releaseTitle },
            []);

        // Assert
        Assert.That(result, Is.EqualTo(expected));
    }
}
