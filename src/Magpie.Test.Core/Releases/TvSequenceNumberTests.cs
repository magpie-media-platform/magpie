using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Releases;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Extensions.Tv;
using Magpie.Models.Data;
using Magpie.Models.Releases;
using Magpie.Sdk.Content;
using Magpie.Sdk.Releases;
using Magpie.Sdk.Services;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Releases;

[TestFixture]
public sealed class TvSequenceNumberTests
{
    private SqliteConnection _template = null!;
    private MagpieDbContext _context = null!;
    private ServiceProvider _services = null!;
    private Mock<IContentHierarchyItemRepository> _hierarchy = null!;
    private ReleaseEvaluationService _subject = null!;

    [OneTimeSetUp]
    public void CreateTemplate()
    {
        _template = new SqliteConnection("Data Source=:memory:");
        _template.Open();
        using var context = new MagpieDbContext(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite(_template)
            .Options);
        context.Database.EnsureCreated();
    }

    [OneTimeTearDown]
    public void DisposeTemplate()
    {
        _template.Dispose();
    }

    [SetUp]
    public void SetUp()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        _template.BackupDatabase(connection);
        _context = new MagpieDbContext(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite(connection, contextOwnsConnection: true)
            .Options);
        var services = new ServiceCollection();
        services.AddLogging();
        new TvExtension().RegisterServices(services);
        _services = services.BuildServiceProvider();
        var provider = _services.GetRequiredService<IContentTypeDefinitionProvider>();
        var registry = new Mock<IContentTypeRegistry>();
        registry.Setup(value => value.FindProviderFor("tv")).Returns(provider);
        registry.Setup(value => value.GetDefinitionFor("tv")).Returns(provider.Definition);
        _hierarchy = new Mock<IContentHierarchyItemRepository>();
        _subject = new ReleaseEvaluationService(
            _hierarchy.Object,
            new MaterializedMetadataRepository(_context, NullLogger<MaterializedMetadataRepository>.Instance),
            Mock.Of<IReleaseRepository>(),
            Mock.Of<IDownloadRepository>(),
            Mock.Of<IQualityTierListRepository>(),
            registry.Object,
            _services.GetServices<IContentFactProvider>(),
            TimeProvider.System,
            Mock.Of<IFileSystem>(),
            NullLogger<ReleaseEvaluationService>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _services.Dispose();
        _context.Dispose();
    }

    [TestCase("episode", 10, 1, 17, 5, "Show.S17E05", "S17E05", ReleaseMatchResult.Confident)]
    [TestCase("episode", 10, 1, 17, 5, "Show.S10E01", "S17E05", ReleaseMatchResult.NoMatch)]
    [TestCase("episode", 10, 1, 17, 5, "Show.S17E01", "S17E05", ReleaseMatchResult.NoMatch)]
    [TestCase("episode", 10, 1, 17, 5, "Show.S17E04-E06", "S17E05", ReleaseMatchResult.Confident)]
    [TestCase("episode", 10, 1, 17, 5, "Show.S17E01-E03", "S17E05", ReleaseMatchResult.NoMatch)]
    [TestCase("episode", 10, 1, 17, 5, "Show.S17.Complete", "S17E05", ReleaseMatchResult.Confident)]
    [TestCase("episode", 10, 1, 17, 5, "Show.S16-S18", "S17E05", ReleaseMatchResult.Confident)]
    [TestCase("episode", 10, 1, 17, 5, "Show.Complete.Series", "S17E05", ReleaseMatchResult.Confident)]
    [TestCase("season", 10, 1, 17, 5, "Show.S17", "Season 17", ReleaseMatchResult.Confident)]
    [TestCase("season", 10, 1, 17, 5, "Show.S10", "Season 17", ReleaseMatchResult.NoMatch)]
    [TestCase("season", 10, 1, 17, 5, "Show.S16-S18", "Season 17", ReleaseMatchResult.Confident)]
    [TestCase("episode", 10, 1, null, null, "Show.S10E01", "S10E01", ReleaseMatchResult.Confident)]
    [TestCase("season", 10, 1, null, null, "Show.S10", "Season 10", ReleaseMatchResult.Confident)]
    [TestCase("episode", 10, 1, 0, 5, "Show.S00E05", "S00E05", ReleaseMatchResult.Confident)]
    [TestCase("season", 10, 1, 0, null, "Show.S00", "Season 0", ReleaseMatchResult.Confident)]
    [TestCase("episode", null, 1, null, null, "Show.S10E01", null, ReleaseMatchResult.Indeterminate)]
    [TestCase("episode", 10, null, null, null, "Show.S10E01", null, ReleaseMatchResult.Indeterminate)]
    [TestCase("season", null, 1, null, null, "Show.S10", null, ReleaseMatchResult.Indeterminate)]
    public async Task CreateEvaluationContextAsync_UsesResolvedSequenceNumbers_WhenContributionsDifferFromOrder(
        string targetLevel,
        int? providerSeasonNumber,
        int? providerEpisodeNumber,
        int? userSeasonNumber,
        int? userEpisodeNumber,
        string releaseTitle,
        string? expectedNumberFact,
        ReleaseMatchResult expectedMatch)
    {
        // Arrange
        var showContainer = new MetadataContainer
        {
            Contributions = { new() { ProviderType = "tmdb", Title = "Show" } }
        };
        var seasonContainer = new MetadataContainer
        {
            Contributions =
            {
                new() { ProviderType = "tmdb", Ordinal = 0, SequenceNumber = providerSeasonNumber, Language = "en" },
                new() { ProviderType = "user", Ordinal = -1, SequenceNumber = userSeasonNumber }
            }
        };
        var episodeContainer = new MetadataContainer
        {
            Contributions =
            {
                new()
                {
                    ProviderType = "tmdb",
                    Ordinal = 0,
                    Title = "Pilot",
                    SequenceNumber = providerEpisodeNumber,
                    PublishDate = new DateTime(2020, 1, 1),
                    Language = "en"
                },
                new() { ProviderType = "user", Ordinal = -1, SequenceNumber = userEpisodeNumber }
            }
        };
        _context.MetadataContainers.AddRange(showContainer, seasonContainer, episodeContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var show = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainerId = showContainer.Id,
            ContentType = "tv",
            Level = "show",
            Slug = "show",
            Name = "Show"
        };
        var season = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = show.Id,
            MetadataContainerId = seasonContainer.Id,
            MetadataContainer = seasonContainer,
            ContentType = "tv",
            Level = "season",
            Slug = "season-10",
            Name = "Series 17",
            Ordinal = 10
        };
        var episode = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = season.Id,
            MetadataContainerId = episodeContainer.Id,
            MetadataContainer = episodeContainer,
            ContentType = "tv",
            Level = "episode",
            Slug = "episode-1",
            Name = "Pilot",
            Ordinal = 1
        };
        IReadOnlyList<ContentHierarchyItem> lineage = targetLevel == "season"
            ? [season, show]
            : [episode, season, show];
        _hierarchy.Setup(value => value.GetLineageAsync(lineage[0].Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lineage);

        // Act
        var evaluation = await _subject.CreateEvaluationContextAsync(lineage[0].Id).ConfigureAwait(false);
        var assessment = _subject.AssessMatch(
            new Release { Protocol = "torrent", Title = releaseTitle },
            [],
            evaluation);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                evaluation.ContentFacts.SelectMany(facts => facts)
                    .Where(fact => fact.Facets.ContainsKey("season"))
                    .Select(fact => fact.Value),
                Is.EqualTo(expectedNumberFact is null ? Array.Empty<string>() : [expectedNumberFact]));
            Assert.That(assessment.MatchResult, Is.EqualTo(expectedMatch));
            Assert.That(season.Ordinal, Is.EqualTo(10));
            Assert.That(season.Slug, Is.EqualTo("season-10"));
            Assert.That(episode.Ordinal, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task AssignFilesToLeavesAsync_AssignsEachFileToItsEpisode_WhenSeasonPackFilesHaveNoShowTitle()
    {
        // Arrange
        var showContainer = new MetadataContainer
        {
            Contributions = { new() { ProviderType = "tmdb", Title = "Show" } }
        };
        var seasonContainer = new MetadataContainer
        {
            Contributions = { new() { ProviderType = "tmdb", SequenceNumber = 1 } }
        };
        var firstEpisodeContainer = new MetadataContainer
        {
            Contributions = { new() { ProviderType = "tmdb", Title = "Pilot", SequenceNumber = 1 } }
        };
        var secondEpisodeContainer = new MetadataContainer
        {
            Contributions = { new() { ProviderType = "tmdb", Title = "Second", SequenceNumber = 2 } }
        };
        _context.MetadataContainers.AddRange(
            showContainer,
            seasonContainer,
            firstEpisodeContainer,
            secondEpisodeContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var show = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainerId = showContainer.Id,
            ContentType = "tv",
            Level = "show",
            Slug = "show",
            Name = "Show"
        };
        var season = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = show.Id,
            MetadataContainerId = seasonContainer.Id,
            MetadataContainer = seasonContainer,
            ContentType = "tv",
            Level = "season",
            Slug = "season-1",
            Name = "Season 1",
            Ordinal = 1
        };
        var firstEpisode = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = season.Id,
            MetadataContainerId = firstEpisodeContainer.Id,
            MetadataContainer = firstEpisodeContainer,
            ContentType = "tv",
            Level = "episode",
            Slug = "pilot",
            Name = "Pilot",
            Ordinal = 1
        };
        var secondEpisode = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = season.Id,
            MetadataContainerId = secondEpisodeContainer.Id,
            MetadataContainer = secondEpisodeContainer,
            ContentType = "tv",
            Level = "episode",
            Slug = "second",
            Name = "Second",
            Ordinal = 2
        };
        _hierarchy.Setup(value => value.GetLineageAsync(firstEpisode.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([firstEpisode, season, show]);
        _hierarchy.Setup(value => value.GetLineageAsync(secondEpisode.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([secondEpisode, season, show]);

        // Act
        var assignments = await _subject.AssignFilesToLeavesAsync(
            new Release { Protocol = "torrent", Title = "Show.S01.1080p.WEB-DL-GRP" },
            season,
            [firstEpisode, secondEpisode],
            [
                new ReleaseFileRef("/downloads/Show.S01/S01E02 - Second.mkv", "S01E02 - Second.mkv"),
                new ReleaseFileRef("/downloads/Show.S01/S01E01 - Pilot.mkv", "S01E01 - Pilot.mkv"),
            ]).ConfigureAwait(false);

        // Assert
        Assert.That(
            assignments.Select(assignment => assignment.LeafContentHierarchyItemId),
            Is.EqualTo(new Guid?[] { secondEpisode.Id, firstEpisode.Id }));
    }
}
