using System;
using System.Collections.Generic;
using System.Linq;

using Magpie.Extensions.Movies;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Releases;
using Magpie.Models.Signals;
using Magpie.Sdk.Content;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Movies;

[TestFixture]
public class MoviesContentTypeDefinitionProviderTests
{
    private const MetadataField ExpectedMovieFields =
        MetadataField.Title |
        MetadataField.OriginalTitle |
        MetadataField.SortTitle |
        MetadataField.Language |
        MetadataField.Overview |
        MetadataField.PublishDate |
        MetadataField.Duration |
        MetadataField.ProductionStatus |
        MetadataField.ExternalIds |
        MetadataField.References |
        MetadataField.Relationships |
        MetadataField.Images |
        MetadataField.Previews |
        MetadataField.AlternateTexts |
        MetadataField.ContentRatings |
        MetadataField.Awards |
        MetadataField.Genres;

    private IReleaseMatchContentTypeDefinitionProvider _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _subject = new MoviesContentTypeDefinitionProvider();
    }

    [Test]
    public void Definition_ReturnsExpectedFileContract_WhenRead()
    {
        // Arrange
        // Act
        var definition = _subject.Definition;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(definition.Slug, Is.EqualTo("movie"));
            Assert.That(definition.DisplayName, Is.EqualTo("Movie"));
            Assert.That(definition.PluralDisplayName, Is.EqualTo("Movies"));
            Assert.That(definition.LevelDefinitions[0].AcceptedFileTypes, Is.EqualTo(["video/*"]));
            Assert.That(definition.Categories, Is.EqualTo(["movies"]));
            Assert.That(
                definition.LevelDefinitions[0].AcceptedSidecarTypes,
                Is.EqualTo([".srt", ".ass", ".vtt", ".sub", ".idx"]));
        }
    }

    [Test]
    public void Definition_ReturnsFilmSlateIcons_WhenRead()
    {
        // Arrange
        // Act
        var definition = _subject.Definition;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(definition.IconName, Is.EqualTo("film-slate"));
            Assert.That(definition.LevelDefinitions.Single().IconName, Is.EqualTo("film-slate"));
        }
    }

    [Test]
    public void Definition_ReturnsSingleMovieType_WhenRead()
    {
        // Arrange
        var provider = new MoviesContentTypeDefinitionProvider();

        // Act
        var definition = provider.Definition;

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(definition.Slug, Is.EqualTo("movie"));
            Assert.That(definition.DisplayName, Is.EqualTo("Movie"));
        }
    }

    [Test]
    public void LevelDefinitions_ReturnsSingleFlatMovieLevel_WhenRead()
    {
        // Arrange
        var provider = new MoviesContentTypeDefinitionProvider();

        // Act
        var levels = provider.Definition.LevelDefinitions;

        // Assert
        Assert.That(levels, Has.Count.EqualTo(1));

        var movie = levels.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(movie.Slug, Is.EqualTo("movie"));
            Assert.That(movie.DisplayName, Is.EqualTo("Movie"));
            Assert.That(movie.ParentSlug, Is.Null);

            Assert.That(levels.Any(l => l.ParentSlug == movie.Slug), Is.False);
        }
    }

    [Test]
    public void LevelDefinitions_ReturnsExpectedMovieMetadataFields_WhenRead()
    {
        // Arrange
        var provider = new MoviesContentTypeDefinitionProvider();

        // Act
        var movie = provider.Definition.LevelDefinitions.Single();

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(movie.SupportedMetadataFields, Is.EqualTo(ExpectedMovieFields));
            Assert.That(movie.CustomMetadataFields, Is.Empty);
        }
    }

    [TestCase("Dune.2021.1080p.BluRay", ReleaseMatchResult.Confident)]
    [TestCase("Dune.2020.1080p.BluRay", ReleaseMatchResult.NoMatch)]
    [TestCase("Arrival.2021.1080p.BluRay", ReleaseMatchResult.NoMatch)]
    [TestCase("Dune.1080p.BluRay", ReleaseMatchResult.Indeterminate)]
    public void IsMatch_Result_WhenMovieEvidenceIsEvaluated(string releaseTitle, ReleaseMatchResult expected)
    {
        // Arrange
        var itemId = Guid.NewGuid();
        var container = new MetadataContainer();
        container.Contributions.Add(new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Dune",
            PublishDate = new DateTime(2021, 10, 22),
        });
        var item = new ContentHierarchyItem
        {
            Id = itemId,
            ContentType = "movie",
            Level = "movie",
            Slug = "dune",
            Name = "Dune",
            CreatedAt = DateTime.UtcNow,
            MetadataContainer = container,
        };
        var target = new ContentTarget
        {
            Item = item,
            Lineage = [item],
            Metadata = new Dictionary<Guid, MaterializedMetadata>
            {
                [itemId] = new() { Title = "Dune", PublishDate = new DateTime(2021, 10, 22) },
            },
        };
        var release = new Release { Protocol = "torrent", Title = releaseTitle };

        // Act
        var result = _subject.IsMatch(
            target,
            [[new ContentFact { Value = "Dune" }]],
            release,
            []);

        // Assert
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void Parse_Result_WhenDottedReleaseNameHasYear()
    {
        // Arrange
        // Act
        var query = _subject.Parse("/lib/The.Matrix.1999.1080p.BluRay.mkv", out _);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(query, Is.Not.Null);
            Assert.That(query!.GetFirstValue(ParsedFileSignalKeys.Title), Is.EqualTo("The Matrix"));
            Assert.That(query!.GetFirstInt32(ParsedFileSignalKeys.Year), Is.EqualTo(1999));
        }
    }

    [TestCase("/lib/Final.Cut.1080p.BluRay.x264.mkv", "Final Cut")]
    [TestCase("/lib/Open.WEBRip.x264.mkv", "Open")]
    [TestCase("/lib/Extended.mkv", "Extended")]
    [TestCase("/lib/Movie.Name.1080p.HD.mkv", "Movie Name")]
    [TestCase("/lib/Movie.Name.DVD.mkv", "Movie Name")]
    [TestCase("/lib/Movie.Name.SD.REPACK.mkv", "Movie Name")]
    [TestCase("/lib/Movie.Name.RERIP.mkv", "Movie Name")]
    [TestCase("/lib/Sample Size.mkv", "Sample Size")]
    [TestCase("/lib/The Sample.mkv", "The Sample")]
    [TestCase("/lib/The Sample Show.mkv", "The Sample Show")]
    [TestCase("/lib/Sampler.mkv", "Sampler")]
    [TestCase("/lib/Resample.mkv", "Resample")]
    public void Parse_StripsTheTrailingReleaseTags_WhenTheReleaseNameHasNoYear(string path, string expected)
    {
        // Arrange
        // Act
        var query = _subject.Parse(path, out _);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(query, Is.Not.Null);
            Assert.That(query!.GetFirstValue(ParsedFileSignalKeys.Title), Is.EqualTo(expected));
        }
    }

    [TestCase("/lib/Movie.sample.mkv")]
    [TestCase("/lib/Movie-SAMPLE.mkv")]
    [TestCase("/lib/Movie_sample.mkv")]
    [TestCase("/lib/Movie [sample].mkv")]
    [TestCase("/lib/sample-Movie.Name.2020.mkv")]
    [TestCase("/lib/sample.Movie.Name.mkv")]
    [TestCase("/lib/SAMPLE_Movie.Name.mkv")]
    [TestCase("/lib/Movie.Name.sample.1080p.mkv")]
    [TestCase("/lib/Movie.Name-SAMPLE-1080p.mkv")]
    [TestCase("/lib/Movie.Name_sample_1080p.mkv")]
    [TestCase("/lib/[sample] Movie.Name.mkv")]
    [TestCase("/lib/Movie.Name [sample] 1080p.mkv")]
    [TestCase("/lib/Sample.Size.mkv")]
    [TestCase("/lib/sample.mkv")]
    [TestCase("/lib/Sample.mkv")]
    [TestCase("/lib/")]
    public void Parse_ReturnsNull_WhenFileIsNotAUsableMovie(string path)
    {
        // Arrange
        // Act
        var query = _subject.Parse(path, out _);

        // Assert
        Assert.That(query, Is.Null);
    }
}
