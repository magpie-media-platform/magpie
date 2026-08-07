using System;
using System.Collections.Generic;
using System.Linq;

using Magpie.Extensions.Music;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Releases;
using Magpie.Models.Signals;
using Magpie.Sdk.Content;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Music;

[TestFixture]
public class MusicContentTypeDefinitionProviderTests
{
    private IReleaseMatchContentTypeDefinitionProvider _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _subject = new MusicContentTypeDefinitionProvider();
    }

    [Test]
    public void Definition_ReturnsExpectedFileContractAndHierarchy_WhenRead()
    {
        // Arrange
        // Act
        var definition = _subject.Definition;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(definition.Slug, Is.EqualTo("music"));
            Assert.That(
                definition.LevelDefinitions.Single(level => level.Slug == "track").AcceptedFileTypes,
                Is.EqualTo(["audio/*"]));
            Assert.That(
                definition.LevelDefinitions.Select(level => level.Slug),
                Is.EqualTo(["artist", "album", "track"]));
            Assert.That(
                definition.LevelDefinitions.Select(level => level.Subfolder is not null),
                Is.EqualTo([true, true, false]));
            Assert.That(
                definition.LevelDefinitions.Single(level => level.Slug == "artist")
                    .SupportedMetadataFields.HasFlag(MetadataField.BirthDate),
                Is.True);
        }
    }

    [TestCase(
        "track",
        "Court.Ordered.Smoothie.Unlicensed.Forklift.Ballet.Dads.Emergency.Contact.Track.4.FLAC",
        4,
        ReleaseMatchResult.Confident)]
    [TestCase(
        "track",
        "Court.Ordered.Smoothie.Unlicensed.Forklift.Ballet.Dads.Emergency.Contact.Track.5.FLAC",
        4,
        ReleaseMatchResult.NoMatch)]
    [TestCase(
        "album",
        "Court.Ordered.Smoothie.Unlicensed.Forklift.Ballet.FLAC",
        4,
        ReleaseMatchResult.Confident)]
    [TestCase("album", "Court.Ordered.Smoothie.Wrong.Album.FLAC", 4, ReleaseMatchResult.NoMatch)]
    [TestCase("artist", "Court.Ordered.Smoothie.Any.Album.FLAC", 4, ReleaseMatchResult.Confident)]
    public void IsMatch_Result_WhenMusicReleaseEvidenceIsEvaluated(
        string targetLevel,
        string releaseTitle,
        int trackNumber,
        ReleaseMatchResult expected)
    {
        // Arrange
        var artistId = Guid.NewGuid();
        var albumId = Guid.NewGuid();
        var trackId = Guid.NewGuid();
        var artist = new ContentHierarchyItem
        {
            Id = artistId,
            ContentType = "music",
            Level = "artist",
            Slug = "court-ordered-smoothie",
            Name = "Court Ordered Smoothie",
            CreatedAt = DateTime.UtcNow,
        };
        var album = new ContentHierarchyItem
        {
            Id = albumId,
            ParentId = artistId,
            ContentType = "music",
            Level = "album",
            Slug = "unlicensed-forklift-ballet",
            Name = "Unlicensed Forklift Ballet",
            CreatedAt = DateTime.UtcNow,
        };
        var track = new ContentHierarchyItem
        {
            Id = trackId,
            ParentId = albumId,
            ContentType = "music",
            Level = "track",
            Slug = "dads-emergency-contact",
            Name = "Dads Emergency Contact",
            Ordinal = trackNumber,
            CreatedAt = DateTime.UtcNow,
        };
        var targetItem = targetLevel switch
        {
            "artist" => artist,
            "album" => album,
            _ => track,
        };
        var lineage = targetLevel switch
        {
            "artist" => new[] { artist },
            "album" => [album, artist],
            _ => [track, album, artist],
        };
        var contentFactSets = targetLevel switch
        {
            "artist" => [],
            "album" =>
           [
                [
                    new ContentFact { Value = "Court Ordered Smoothie" },
                    new ContentFact { Value = "Unlicensed Forklift Ballet" },
                ],
           ],
            _ => new[]
           {
                new []
                {
                    new ContentFact { Value = "Court Ordered Smoothie" },
                    new ContentFact { Value = "Unlicensed Forklift Ballet" },
                    new ContentFact { Value = "Dads Emergency Contact" },
                },
           },
        };
        var target = new ContentTarget
        {
            Item = targetItem,
            Lineage = lineage,
            Metadata = new Dictionary<Guid, MaterializedMetadata>(),
        };

        // Act
        var result = _subject.IsMatch(
            target,
            contentFactSets,
            new Release { Protocol = "torrent", Title = releaseTitle },
            []);

        // Assert
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void Parse_Result_WhenTrackFileIsProvided()
    {
        // Arrange
        // Act
        var query = _subject.Parse(
            "/library/music/Pink Floyd/The Wall (1979)/01 - In The Flesh.flac",
            out var levelSlug);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(query, Is.Not.Null);
            Assert.That(query!.GetFirstValue(ParsedFileSignalKeys.Title), Is.EqualTo("The Wall"));
            Assert.That(query!.GetFirstValue("artist"), Is.EqualTo("Pink Floyd"));
            Assert.That(levelSlug, Is.EqualTo("artist"));
        }
    }

    [Test]
    public void BuildPath_Result_WhenArtistAlbumTrackAreAvailable()
    {
        // Arrange
        var ancestors = new ContentHierarchyItem[]
       {
            new()
            {
                ContentType = "music",
                Level = "artist",
                Slug                 = "pink-floyd",
                Name = "Pink Floyd"
            },
            new()
            {
                ContentType = "music",
                Level = "album",
                Slug = "the-wall",
                Name = "The Wall",
                MetadataContainer = new()
            },
            new()
            {
                ContentType = "music",
                Level = "track",
                Slug = "in-the-flesh",
                Name = "In The Flesh",
                Ordinal = 1
            },
       };
        ancestors[1].MetadataContainer!.Contributions.Add(
            new() { ProviderType = "test", PublishDate = new(1979, 1, 1) });

        // Act
        var path = ((IPathResolutionContentTypeDefinitionProvider)_subject)
            .BuildPath(ancestors, new Dictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
                "01 - In The Flesh", "flac");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(path, Is.EqualTo("Pink Floyd/The Wall (1979)/01 - In The Flesh.flac"));
        }
    }
}
