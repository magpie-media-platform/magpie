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
    public void IsMatch_ReturnsExpectedMatch_WhenMusicReleaseEvidenceIsEvaluated(
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

    [TestCase("album", "Choking Victim", "No Gods / No Managers", "500 Channels", 1,
        "Choking Victim - No Gods + No Managers - 01 - 500 Channels.flac", ReleaseMatchResult.Confident)]
    [TestCase("album", "NOFX", "Self/Entitled", "72 Hookers", 1,
        "NOFX - Self+Entitled - 01 - 72 Hookers.flac", ReleaseMatchResult.Confident)]
    [TestCase("album", "Choking Victim", "No Gods / No Managers", "500 Channels", 1,
        "Choking Victim - Crack Rock Steady - 01 - Crack Rock Steady.flac", ReleaseMatchResult.NoMatch)]
    [TestCase("track", "Jeff Rosenstock", "NO DREAM", "***BNB", 10,
        "Jeff Rosenstock - NO DREAM - 10 - -BNB.flac", ReleaseMatchResult.Confident)]
    [TestCase("track", "Jeff Rosenstock", "SKA DREAM", "***SKA", 10,
        "Jeff Rosenstock - SKA DREAM - 10 - -SKA.flac", ReleaseMatchResult.Confident)]
    [TestCase("track", "Jeff Rosenstock", "POST\u2010", "9/10", 9,
        "Jeff Rosenstock - POST\u2010 - 09 - 9+10.flac", ReleaseMatchResult.Confident)]
    [TestCase("track", "Jeff Rosenstock", "NO DREAM", "NO DREAM", 1,
        "Jeff Rosenstock - NO DREAM - 10 - -BNB.flac", ReleaseMatchResult.NoMatch)]
    [TestCase("track", "Jeff Rosenstock", "POST\u2010", "USA", 1,
        "Jeff Rosenstock - POST\u2010 - 09 - 9+10.flac", ReleaseMatchResult.NoMatch)]
    [TestCase("track", "Adele", "21", "Rolling in the Deep", 1,
        "Adele - 21 - 01 - Rolling in the Deep.flac", ReleaseMatchResult.Confident)]
    public void IsMatch_TreatsFilesystemSafeSubstitutesAsEqual_WhenTheFileNameReplacedATitleCharacter(
        string targetLevel,
        string artistName,
        string albumName,
        string trackName,
        int trackOrdinal,
        string fileName,
        ReleaseMatchResult expected)
    {
        // Arrange
        var artist = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ContentType = "music",
            Level = "artist",
            Slug = "artist",
            Name = artistName,
            CreatedAt = DateTime.UtcNow,
        };
        var album = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = artist.Id,
            ContentType = "music",
            Level = "album",
            Slug = "album",
            Name = albumName,
            CreatedAt = DateTime.UtcNow,
        };
        var track = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = album.Id,
            ContentType = "music",
            Level = "track",
            Slug = "track",
            Name = trackName,
            Ordinal = trackOrdinal,
            CreatedAt = DateTime.UtcNow,
        };
        var target = targetLevel == "album"
            ? new ContentTarget
            {
                Item = album,
                Lineage = [album, artist],
                Metadata = new Dictionary<Guid, MaterializedMetadata>(),
            }
            : new ContentTarget
            {
                Item = track,
                Lineage = [track, album, artist],
                Metadata = new Dictionary<Guid, MaterializedMetadata>(),
            };

        // Act
        var result = _subject.IsMatch(
            target,
            [],
            new Release { Protocol = "file", Title = fileName },
            []);

        // Assert
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase("", null, "Pack Artist - Album - Song.flac", ReleaseMatchResult.Indeterminate)]
    [TestCase("", null, "Pack Artist - Track 02 - Song.flac", ReleaseMatchResult.Confident)]
    [TestCase("Other", null, "01 - Pack Artist - Title.flac", ReleaseMatchResult.NoMatch)]
    [TestCase("", "Other", "02 - Pack Artist - Other.flac", ReleaseMatchResult.Confident)]
    [TestCase("Other", "Other", "01 - Pack Artist - Title.flac", ReleaseMatchResult.NoMatch)]
    [TestCase("Title", "Title", "01 - Pack Artist - Title.flac", ReleaseMatchResult.Confident)]
    public void IsMatch_RequiresTheTrackTitle_WhenTheTrackTitleComesFromMetadataOrName(
        string trackName,
        string? metadataTitle,
        string fileName,
        ReleaseMatchResult expected)
    {
        // Arrange
        var artist = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ContentType = "music",
            Level = "artist",
            Slug = "pack-artist",
            Name = "Pack Artist",
            CreatedAt = DateTime.UtcNow,
        };
        var album = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = artist.Id,
            ContentType = "music",
            Level = "album",
            Slug = "album",
            Name = "Album",
            CreatedAt = DateTime.UtcNow,
        };
        var track = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = album.Id,
            ContentType = "music",
            Level = "track",
            Slug = "track",
            Name = trackName,
            Ordinal = 2,
            CreatedAt = DateTime.UtcNow,
        };
        var target = new ContentTarget
        {
            Item = track,
            Lineage = [track, album, artist],
            Metadata = new Dictionary<Guid, MaterializedMetadata>
            {
                [artist.Id] = new() { Title = "Pack Artist" },
                [track.Id] = new() { Title = metadataTitle },
            },
        };

        // Act
        var result = _subject.IsMatch(
            target,
            [],
            new Release { Protocol = "file", Title = fileName },
            []);

        // Assert
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void Parse_ReturnsArtistAndAlbumSignals_WhenTrackFileIsProvided()
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
            Assert.That(query!.GetFirstValue("album"), Is.EqualTo("The Wall"));
            Assert.That(query!.GetFirstValue(ParsedFileSignalKeys.TitleToken), Is.EqualTo("The Wall"));
            Assert.That(query!.GetFirstValue(ParsedFileSignalKeys.RootTitle), Is.EqualTo("Pink Floyd"));
            Assert.That(levelSlug, Is.EqualTo("artist"));
        }
    }

    [Test]
    public void BuildPath_ReturnsArtistAlbumTrackPath_WhenArtistAlbumTrackAreAvailable()
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
        Assert.That(path, Is.EqualTo("Pink Floyd/The Wall (1979)/01 - In The Flesh.flac"));
    }

    [Test]
    public void BuildPathPlan_ReturnsArtistAndAlbumDirectories_WhenArtistAlbumTrackAreAvailable()
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
        var plan = ((IPathResolutionContentTypeDefinitionProvider)_subject)
            .BuildPathPlan(ancestors, new Dictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
                "01 - In The Flesh", "flac");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan!.Directories, Has.Count.EqualTo(2));
            Assert.That(plan.Directories[0].LevelSlug, Is.EqualTo("artist"));
            Assert.That(plan.Directories[0].Name, Is.EqualTo("Pink Floyd"));
            Assert.That(plan.Directories[1].LevelSlug, Is.EqualTo("album"));
            Assert.That(plan.Directories[1].Name, Is.EqualTo("The Wall (1979)"));
            Assert.That(plan.FileName, Is.EqualTo("01 - In The Flesh.flac"));
        }
    }

    [Test]
    public void ResolveDirectoryLevelChain_ReturnsArtistAndAlbum_WhenTrackLevelIsGiven()
    {
        // Arrange
        // Act
        var chain = ((IPathResolutionContentTypeDefinitionProvider)_subject).ResolveDirectoryLevelChain("track");

        // Assert
        Assert.That(chain, Is.EqualTo(["artist", "album"]));
    }

    [TestCase("Sample Song.flac", "Sample Song")]
    [TestCase("Sample Size.flac", "Sample Size")]
    [TestCase("The Sample.flac", "The Sample")]
    [TestCase("The Sample Song.flac", "The Sample Song")]
    [TestCase("Sampler.flac", "Sampler")]
    [TestCase("Resample.flac", "Resample")]
    public void Parse_PreservesTitle_WhenSampleIsATitleWord(
        string path,
        string expectedTitle)
    {
        // Arrange
        // Act
        var result = _subject.Parse(path, out _);

        // Assert
        Assert.That(result?.GetFirstValue(ParsedFileSignalKeys.Title), Is.EqualTo(expectedTitle));
    }

    [TestCase("sample.flac")]
    [TestCase("Sample.flac")]
    [TestCase("Song.sample.flac")]
    [TestCase("Song-SAMPLE.flac")]
    [TestCase("Song_sample.flac")]
    [TestCase("Song [sample].flac")]
    [TestCase("sample-Song.Name.2020.flac")]
    [TestCase("sample.Song.Name.flac")]
    [TestCase("SAMPLE_Song.Name.flac")]
    [TestCase("Song.Name.sample.320kbps.flac")]
    [TestCase("Song.Name-SAMPLE-320kbps.flac")]
    [TestCase("Song.Name_sample_320kbps.flac")]
    [TestCase("[sample] Song.Name.flac")]
    [TestCase("Song.Name [sample] 320kbps.flac")]
    public void Parse_ReturnsNull_WhenSampleMarkerIsDelimited(string path)
    {
        // Arrange
        // Act
        var result = _subject.Parse(path, out _);

        // Assert
        Assert.That(result, Is.Null);
    }
}
