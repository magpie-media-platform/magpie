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
        Assert.That(path, Is.EqualTo("Example (2020)/Example - S01E02 - Pilot.mkv"));
    }

    [Test]
    public void BuildPathPlan_Result_WhenEpisodeHasSeasonAndEpisodeNumbers()
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
    public void Parse_Result_WhenEpisodeFileIsProvided()
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

    [Test]
    public void ResolveDirectoryLevelChain_Result_WhenEpisodeLevelIsGiven()
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
    public void Parse_PreservesTitle_WhenWordsResembleReleaseTags(string path, string expectedTitle)
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
    public void Parse_StripsTrailingReleaseTags_WhenTheTagIsNotATitleWord(string path, string expectedTitle)
    {
        // Arrange
        // Act
        var result = _subject.Parse(path, out _);

        // Assert
        Assert.That(result?.GetFirstValue(ParsedFileSignalKeys.Title), Is.EqualTo(expectedTitle));
    }
}
