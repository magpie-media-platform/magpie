using Magpie.Extensions.Music.Parsing;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Music.Parsing;

[TestFixture]
public class FilePathExtensionsTests
{
    [Test]
    public void Parse_ArtistAlbumTrackFolders_ExtractsFullHierarchy()
    {
        // Arrange
        var path = "/library/music/Pink Floyd/The Wall (1979)/01 - In The Flesh.flac";

        // Act
        var result = path.ParseMusicFilename();

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.Artist, Is.EqualTo("Pink Floyd"));
            Assert.That(result.Album, Is.EqualTo("The Wall"));
            Assert.That(result.Year, Is.EqualTo(1979));
        }
    }

    [Test]
    public void Parse_AlbumFolderWithoutYear_ReturnsAlbumTitleOnly()
    {
        // Arrange
        var path = "/library/music/Daft Punk/Discovery/03 Digital Love.flac";

        // Act
        var result = path.ParseMusicFilename();

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.Artist, Is.EqualTo("Daft Punk"));
            Assert.That(result.Album, Is.EqualTo("Discovery"));
            Assert.That(result.Year, Is.Null);
        }
    }

    [Test]
    public void Parse_NoAlbumFolder_UsesFilenameWithoutTrackNumberAsAlbum()
    {
        // Arrange
        var path = "/01 - In The Flesh.flac";

        // Act
        var result = path.ParseMusicFilename();

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.Artist, Is.Null);
            Assert.That(result.Album, Is.EqualTo("In The Flesh"));
            Assert.That(result.Year, Is.Null);
        }
    }

    [Test]
    public void Parse_NoArtistFolder_ReturnsNullArtist()
    {
        // Arrange
        var path = "/The Wall (1979)/01 - In The Flesh.flac";

        // Act
        var result = path.ParseMusicFilename();

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.Artist, Is.Null);
            Assert.That(result.Album, Is.EqualTo("The Wall"));
            Assert.That(result.Year, Is.EqualTo(1979));
        }
    }
}
