using System;
using System.Collections.Generic;
using System.Linq;

using Magpie.Extensions.Tv;
using Magpie.Models.Data;
using Magpie.Models.Releases;
using Magpie.Models.Signals;
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
    public void Definition_ReturnsSubfolderOnShowLevelOnly_WhenPathContributionsAreRead()
    {
        // Arrange
        // Act
        var contributions = _subject.Definition.LevelDefinitions.Select(level => level.Subfolder is not null);

        // Assert
        Assert.That(contributions, Is.EqualTo([true, false, false]));
    }

    [TestCase("/media/An Alias/Season 1/s01e02_reordered.mkv", "show", 1, "season", 1, true)]
    [TestCase("/media/An Alias/Season 1/s01e02_reordered.mkv", "season", 1, "episode", 2, true)]
    [TestCase("/media/An Alias/Season 4/s04e05.mkv", "season", 2, "episode", 5, true)]
    [TestCase("/media/An Alias/Season 4/s04e05.mkv", "show", 1, "season", 2, false)]
    [TestCase("/media/An Alias/Season 1/02.mkv", "season", 1, "episode", 2, true)]
    public void ResolveFileChildren_UsesConfirmedParent_WhenFileTitleOrProviderNumberingDiffers(
        string path, string parentLevel, int parentOrdinal, string childLevel, int childOrdinal, bool expectedMatch)
    {
        // Arrange
        var parent = new ContentHierarchyItem
        {
            ContentType = "tv",
            Level = parentLevel,
            Name = "Canonical title",
            Slug = "canonical",
            Ordinal = parentOrdinal
        };
        var child = new ContentHierarchyItem
        {
            Id = new Guid("00000000-0000-0000-0000-000000000001"),
            ParentId = parent.Id,
            ContentType = "tv",
            Level = childLevel,
            Name = "Provider title",
            Slug = "provider",
            Ordinal = childOrdinal
        };
        var parsed = _subject.Parse(path, out _)!;

        // Act
        var result = ((IFileRoutingContentTypeDefinitionProvider)_subject).ResolveFileChildren(
            parent, [child], parsed.ToDictionary(pair => pair.Key, pair => pair.Value.ToList()));

        // Assert
        Assert.That(result.Any(), Is.EqualTo(expectedMatch));
    }

    [TestCase(
        "/media/Show/Season 1/Show.S01E34-E36.mkv",
        new[]
        {
            "00000000-0000-0000-0000-000000000034",
            "00000000-0000-0000-0000-000000000035",
            "00000000-0000-0000-0000-000000000036"
        })]
    [TestCase(
        "/media/Show/Season 1/Show.S01E34-36.mkv",
        new[]
        {
            "00000000-0000-0000-0000-000000000034",
            "00000000-0000-0000-0000-000000000035",
            "00000000-0000-0000-0000-000000000036"
        })]
    [TestCase(
        "/media/Show/Season 2/Show.S02E55E56.mkv",
        new[] { "00000000-0000-0000-0000-000000000055", "00000000-0000-0000-0000-000000000056" })]
    [TestCase("/media/Show/Season 1/Show.S01E35.mkv", new[] { "00000000-0000-0000-0000-000000000035" })]
    public void ResolveFileChildren_ReturnsEveryEpisodeInOrdinalOrder_WhenTheFileNamesEpisodes(
        string path,
        string[] expected)
    {
        // Arrange
        var parent = new ContentHierarchyItem { ContentType = "tv", Level = "season", Name = "Season", Slug = "s" };
        ContentHierarchyItem[] children =
        [
            new()
            {
                Id = new Guid("00000000-0000-0000-0000-000000000056"),
                ContentType = "tv",
                Level = "episode",
                Name = "E56",
                Slug = "e56",
                Ordinal = 56
            },
            new()
            {
                Id = new Guid("00000000-0000-0000-0000-000000000036"),
                ContentType = "tv",
                Level = "episode",
                Name = "E36",
                Slug = "e36",
                Ordinal = 36
            },
            new()
            {
                Id = new Guid("00000000-0000-0000-0000-000000000034"),
                ContentType = "tv",
                Level = "episode",
                Name = "E34",
                Slug = "e34",
                Ordinal = 34
            },
            new()
            {
                Id = new Guid("00000000-0000-0000-0000-000000000055"),
                ContentType = "tv",
                Level = "episode",
                Name = "E55",
                Slug = "e55",
                Ordinal = 55
            },
            new()
            {
                Id = new Guid("00000000-0000-0000-0000-000000000035"),
                ContentType = "tv",
                Level = "episode",
                Name = "E35",
                Slug = "e35",
                Ordinal = 35
            }
        ];
        var parsed = _subject.Parse(path, out _)!;

        // Act
        var result = ((IFileRoutingContentTypeDefinitionProvider)_subject).ResolveFileChildren(
            parent, children, parsed.ToDictionary(pair => pair.Key, pair => pair.Value.ToList()));

        // Assert
        Assert.That(result, Is.EqualTo(expected.Select(Guid.Parse)));
    }

    [Test]
    public void ResolveFileChildren_ReturnsUnresolved_WhenAnEpisodeThatTheFileNamesIsMissing()
    {
        // Arrange
        var parent = new ContentHierarchyItem { ContentType = "tv", Level = "season", Name = "Season", Slug = "s" };
        ContentHierarchyItem[] children =
        [
            new() { ContentType = "tv", Level = "episode", Name = "E34", Slug = "e34", Ordinal = 34 },
            new() { ContentType = "tv", Level = "episode", Name = "E35", Slug = "e35", Ordinal = 35 }
        ];
        var parsed = _subject.Parse("/media/Show/Season 1/Show.S01E34-E36.mkv", out _)!;

        // Act
        var result = ((IFileRoutingContentTypeDefinitionProvider)_subject).ResolveFileChildren(
            parent, children, parsed.ToDictionary(pair => pair.Key, pair => pair.Value.ToList()));

        // Assert
        Assert.That(result, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ResolveFileChildren_ReturnsUnresolved_WhenChildIsMissingOrAmbiguous(bool duplicate)
    {
        // Arrange
        var parent = new ContentHierarchyItem { ContentType = "tv", Level = "show", Name = "Show", Slug = "show" };
        var first = new ContentHierarchyItem
        {
            ContentType = "tv",
            Level = "season",
            Name = "Season 1",
            Slug = "one",
            Ordinal = 1
        };
        var second = first with { Id = Guid.NewGuid(), Slug = "duplicate" };
        ContentHierarchyItem[] children = duplicate ? [first, second] : [];

        // Act
        var result = ((IFileRoutingContentTypeDefinitionProvider)_subject).ResolveFileChildren(
            parent, children, new Dictionary<string, List<string>> { ["season"] = ["1"] });

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void BuildPath_ReturnsShowFolderAndEpisodeFileName_WhenEpisodeHasSeasonAndEpisodeNumbers()
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
        Assert.That(path, Is.EqualTo("Example (2020)/Example - S01E02 - Pilot.mkv"));
    }

    [Test]
    public void BuildPathPlan_ReturnsShowDirectory_WhenEpisodeHasSeasonAndEpisodeNumbers()
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
        var plan = ((IPathResolutionContentTypeDefinitionProvider)_subject).BuildPathPlan(
            ancestors, new Dictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<string>>>(), "source", "mkv");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan!.Directories, Has.Count.EqualTo(1));
            Assert.That(plan.Directories[0].LevelSlug, Is.EqualTo("show"));
            Assert.That(plan.Directories[0].Name, Is.EqualTo("Example (2020)"));
            Assert.That(plan.FileName, Is.EqualTo("Example - S01E02 - Pilot.mkv"));
        }
    }

    [Test]
    public void BuildPathPlan_KeepsTheOriginalName_WhenTheLineageEndsAtTheSeason()
    {
        // Arrange
        ContentHierarchyItem[] ancestors =
        [
            new() { ContentType = "tv", Level = "show", Slug = "example", Name = "Example" },
            new() { ContentType = "tv", Level = "season", Slug = "season-1", Name = "Season 1", Ordinal = 1 }
        ];

        // Act
        var plan = ((IPathResolutionContentTypeDefinitionProvider)_subject).BuildPathPlan(
            ancestors,
            new Dictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
            "Example.S01E34-E36",
            "mkv");

        // Assert
        Assert.That(plan!.FileName, Is.EqualTo("Example.S01E34-E36.mkv"));
    }

    [Test]
    public void Parse_ReturnsShowSignals_WhenEpisodeFileIsProvided()
    {
        // Arrange
        // Act
        var signals = _subject.Parse("/library/tv/Bluey.2018.S01E01.1080p.mkv", out var levelSlug);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(signals, Is.Not.Null);
            Assert.That(signals!.GetFirstValue(ParsedFileSignalKeys.Title), Is.EqualTo("Bluey"));
            Assert.That(signals!.GetFirstValue(ParsedFileSignalKeys.TitleToken), Is.EqualTo("Bluey 2018"));
            Assert.That(signals!.GetFirstValue(ParsedFileSignalKeys.RootTitle), Is.EqualTo("Bluey 2018"));
            Assert.That(levelSlug, Is.EqualTo("show"));
        }
    }

    [TestCase("/library/tv/Show/Season 1/Show.S01E34-E36.mkv", new[] { "34", "35", "36" })]
    [TestCase("/library/tv/Show/Season 1/Show.S01E34-36.mkv", new[] { "34", "35", "36" })]
    [TestCase("/library/tv/Show/Season 2/Show.S02E55E56.mkv", new[] { "55", "56" })]
    [TestCase("/library/tv/Show/Season 3/Show 3x59-60.mkv", new[] { "59", "60" })]
    [TestCase("/library/tv/Show/Season 1/Show.S01E05.mkv", new[] { "5" })]
    [TestCase("/library/tv/Show/Season 1/Show.S01E05.2019.mkv", new[] { "5" })]
    [TestCase("/library/tv/Show/Season 1/Show.S01E01.720p.WEB.mkv", new[] { "1" })]
    [TestCase("/library/tv/Show/Season 1/Show.S01E01.1080p.x265.mkv", new[] { "1" })]
    [TestCase("/library/tv/Show/Season 1/Show S01E01 2160p.mkv", new[] { "1" })]
    [TestCase("/library/tv/Show/Season 1/Show.S01E01E02.mkv", new[] { "1", "2" })]
    [TestCase("/library/tv/Show/Season 1/Show.S01E01-E03.mkv", new[] { "1", "2", "3" })]
    [TestCase("/library/tv/Show/Season 1/Show.S01E01-E02-E03.mkv", new[] { "1", "2", "3" })]
    [TestCase("/library/tv/Show/Season 1/Show.S01E01E02E03.mkv", new[] { "1", "2", "3" })]
    [TestCase("/library/tv/Show/Season 1/Show - S01E01 - Title.mkv", new[] { "1" })]
    [TestCase("/library/tv/Show/Season 1/Show - S01E01 - 100 Days.mkv", new[] { "1" })]
    [TestCase("/library/tv/Show/Season 1/Show.S01E01.100.Days.720p.mkv", new[] { "1" })]
    public void Parse_ReportsEveryEpisode_WhenTheFileNamesEpisodes(string path, string[] expected)
    {
        // Arrange
        // Act
        var signals = _subject.Parse(path, out _);

        // Assert
        Assert.That(signals!["episode"], Is.EqualTo(expected));
    }

    [Test]
    public void ResolveDirectoryLevelChain_ReturnsShow_WhenEpisodeLevelIsGiven()
    {
        // Arrange
        // Act
        var chain = ((IPathResolutionContentTypeDefinitionProvider)_subject).ResolveDirectoryLevelChain("episode");

        // Assert
        Assert.That(chain, Is.EqualTo(["show"]));
    }

    [Test]
    public void ResolveDirectoryLevelChain_ReturnsNull_WhenLevelIsUnknown()
    {
        // Arrange
        // Act
        var chain = ((IPathResolutionContentTypeDefinitionProvider)_subject)
            .ResolveDirectoryLevelChain("nonexistent-level");

        // Assert
        Assert.That(chain, Is.Null);
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
    public void IsMatch_ReturnsExpectedMatch_WhenTvReleaseShapeIsEvaluated(
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
                [episodeId] = new()
                {
                    Title = "Rollercoaster",
                    PublishDate = new DateTime(2008, 2, 1, 0, 0, 0, DateTimeKind.Utc)
                },
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
    public void IsMatch_ReturnsExpectedMatch_WhenBlueySeasonReleaseIsEvaluated(
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

    [TestCase("Show.S01.1080p-GRP", "2026-06-29", "2026-06-29", "2026-07-22", ReleaseMatchResult.Confident)]
    [TestCase("Show.S01.1080p-GRP", "2026-07-22", "2026-06-29", "2026-07-22", ReleaseMatchResult.Confident)]
    [TestCase("Show.S01.1080p-GRP", "2026-07-23", "2026-06-29", "2026-07-22", ReleaseMatchResult.Confident)]
    [TestCase("Show.S01.1080p-GRP", "2026-07-24", "2026-06-29", "2026-07-22", ReleaseMatchResult.NoMatch)]
    [TestCase("Show.S01.1080p-GRP", null, "2026-06-29", "2026-07-22", ReleaseMatchResult.Indeterminate)]
    [TestCase("Show.S01.1080p-GRP", null, null, "2026-07-22", ReleaseMatchResult.Indeterminate)]
    [TestCase("Show.S01E40.1080p-GRP", null, "2026-06-29", "2026-07-22", ReleaseMatchResult.Confident)]
    [TestCase("Show.S01E40.1080p-GRP", "2026-07-24", "2026-06-29", "2026-07-22", ReleaseMatchResult.Confident)]
    [TestCase("Show.Complete.Series.1080p-GRP", "2026-06-29", "2026-06-29", "2026-07-22", ReleaseMatchResult.Confident)]
    [TestCase("Show.Complete.Series.1080p-GRP", "2026-07-24", "2026-06-29", "2026-07-22", ReleaseMatchResult.NoMatch)]
    [TestCase("Show.Complete.Series.1080p-GRP", null, "2026-06-29", "2026-07-22", ReleaseMatchResult.Indeterminate)]
    [TestCase("Show.S01.1080p-GRP", null, "1988-01-02", "2026-07-22", ReleaseMatchResult.Indeterminate)]
    [TestCase("Show.S01.1080p-GRP", null, "2025-07-21", "2026-07-22", ReleaseMatchResult.Indeterminate)]
    [TestCase("Show.S01.1080p-GRP", null, "2025-07-22", "2026-07-22", ReleaseMatchResult.Indeterminate)]
    [TestCase("Show.S01.1080p-GRP", "1970-01-01", "1970-01-01", "2026-07-22", ReleaseMatchResult.Confident)]
    [TestCase("Show.S01.1080p-GRP", "0001-01-01", "2026-06-29", "2026-07-22", ReleaseMatchResult.Indeterminate)]
    [TestCase("Show.S01.1080p-GRP", "2026-07-24", "2026-06-29", "0001-01-01", ReleaseMatchResult.Confident)]
    [TestCase("Show.S01.1080p-GRP", "2026-07-24", "2026-06-29", "1970-01-01", ReleaseMatchResult.NoMatch)]
    [TestCase("Show.S01.1080p-GRP", null, "2026-06-29", "0001-01-01", ReleaseMatchResult.Indeterminate)]
    public void IsMatch_ReturnsExpectedMatch_WhenPackIsPublishedBeforeOrAfterTheEpisode(
        string releaseTitle,
        DateTime? episodePublishDate,
        DateTime? seasonPublishDate,
        DateTime releasePublishDate,
        ReleaseMatchResult expected)
    {
        // Arrange
        var show = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ContentType = "tv",
            Level = "show",
            Slug = "show",
            Name = "Show",
        };
        var season = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = show.Id,
            ContentType = "tv",
            Level = "season",
            Slug = "season-1",
            Name = "Season 1",
            Ordinal = 1,
        };
        var episode = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = season.Id,
            ContentType = "tv",
            Level = "episode",
            Slug = "ice-chair",
            Name = "Ice Chair",
            Ordinal = 40,
        };
        var target = new ContentTarget
        {
            Item = episode,
            Lineage = [episode, season, show],
            Metadata = new Dictionary<Guid, MaterializedMetadata>
            {
                [show.Id] = new() { Title = "Show" },
                [season.Id] = new() { Title = "Season 1", PublishDate = seasonPublishDate },
                [episode.Id] = new() { Title = "Ice Chair", PublishDate = episodePublishDate },
            },
        };

        // Act
        var result = _subject.IsMatch(
            target,
            [],
            new Release
            {
                Protocol = "torrent",
                Title = releaseTitle,
                PublishDate = releasePublishDate,
            },
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
    public void IsMatch_ReturnsExpectedMatch_WhenDatedSeasonReleaseIsEvaluated(
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

    [TestCase("The.Web.S01E01.mkv", "The Web")]
    [TestCase("Proper.S01E01.mkv", "Proper")]
    [TestCase("The.Internal.S01E01.mkv", "The Internal")]
    [TestCase("Limited.S01E01.mkv", "Limited")]
    [TestCase("Multi.S01E01.mkv", "Multi")]
    [TestCase("Dual.S01E01.mkv", "Dual")]
    [TestCase("HDTV.S01E01.mkv", "HDTV")]
    [TestCase("The.Web.1080p.WEB-DL.S01E01.mkv", "The Web")]
    [TestCase("The\tWeb__S01E01.mkv", "The Web")]
    [TestCase("The Web/Season 1/01.mkv", "The Web")]
    [TestCase("The Web/S01E01.mkv", "The Web")]
    [TestCase("Sample Show.S01E01.mkv", "Sample Show")]
    [TestCase("Sample Size.S01E01.mkv", "Sample Size")]
    [TestCase("The Sample S01E01.mkv", "The Sample")]
    [TestCase("The Sample Show.S01E01.mkv", "The Sample Show")]
    [TestCase("Sampler.S01E01.mkv", "Sampler")]
    [TestCase("Resample.S01E01.mkv", "Resample")]
    [TestCase("Show.1.1080p.S01E01.mkv", "Show 1")]
    [TestCase("Show.5.1080p.S01E01.mkv", "Show 5")]
    [TestCase("Show.7.1080p.S01E01.mkv", "Show 7")]
    [TestCase("Show.5.1.1080p.S01E01.mkv", "Show 5 1")]
    [TestCase("Show.7.1.1080p.S01E01.mkv", "Show 7 1")]
    [TestCase("Show.DDP5.2.x264.S01E01.mkv", "Show DDP5 2")]
    [TestCase("Show.DDP5.1.Story.S01E01.mkv", "Show DDP5 1 Story")]
    [TestCase("The.Show.DVD.S01E01.mkv", "The Show")]
    [TestCase("The.Show.REPACK.S01E01.mkv", "The Show")]
    [TestCase("The Show 1080p BluRay DDP5.1 x264 S01E01.mkv", "The Show")]
    [TestCase("The.Show.1080p.BluRay.ddp5.1.x264.S01E01.mkv", "The Show")]
    [TestCase("The.Show.1080p.BluRay.DD5.1.x264.S01E01.mkv", "The Show")]
    [TestCase("The.Show.1080p.BluRay.5.1.x264.S01E01.mkv", "The Show")]
    [TestCase("The.Show.1080p.BluRay.7.1.x264.S01E01.mkv", "The Show")]
    [TestCase("Show.DDP5.1.S01E01.mkv", "Show")]
    [TestCase("Show.DD5.1.S01E01.mkv", "Show")]
    [TestCase("Show.AAC.5.1.S01E01.mkv", "Show")]
    [TestCase("Show.DTS.7.1.S01E01.mkv", "Show")]
    [TestCase("Show.1080p.DDP5.1/S01E01.mkv", "Show")]
    [TestCase("Show.1080p.DD5.1/Season 1/01.mkv", "Show")]
    [TestCase("Show.1080p.AAC.5.1/Specials/01.mkv", "Show")]
    public void Parse_ReturnsTitle_WhenReleaseTagsSurroundIt(string path, string expectedTitle)
    {
        // Arrange
        // Act
        var result = _subject.Parse(path, out _);

        // Assert
        Assert.That(result?.GetFirstValue(ParsedFileSignalKeys.Title), Is.EqualTo(expectedTitle));
    }

    [TestCase("Show.S01E01.sample.mkv")]
    [TestCase("Show.S01E01-SAMPLE.mkv")]
    [TestCase("Show.S01E01_sample.mkv")]
    [TestCase("Show.S01E01 [sample].mkv")]
    [TestCase("sample-Show.S01E01.mkv")]
    [TestCase("sample.Show.S01E01.mkv")]
    [TestCase("SAMPLE_Show.S01E01.mkv")]
    [TestCase("Show.sample.S01E01.mkv")]
    [TestCase("Show.S01E01.sample.1080p.mkv")]
    [TestCase("Show.S01E01-SAMPLE-1080p.mkv")]
    [TestCase("Show.S01E01_sample_1080p.mkv")]
    [TestCase("[sample] Show.S01E01.mkv")]
    [TestCase("Show.S01E01 [sample] 1080p.mkv")]
    [TestCase("Sample.Show.S01E01.mkv")]
    [TestCase("sample.mkv")]
    [TestCase("Sample.mkv")]
    public void Parse_ReturnsNull_WhenSampleMarkerIsDelimited(string path)
    {
        // Arrange
        // Act
        var result = _subject.Parse(path, out _);

        // Assert
        Assert.That(result, Is.Null);
    }
}
