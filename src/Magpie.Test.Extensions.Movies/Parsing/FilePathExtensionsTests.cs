using System.IO;

using Magpie.Extensions.Movies.Parsing;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Movies.Parsing;

[TestFixture]
public class FilePathExtensionsTests
{
    [TestCase("Movie Name 1080p BluRay DDP5.1 x264.mkv", "Movie Name")]
    [TestCase("Movie.Name.1080p.BluRay.ddp5.1.x264.mkv", "Movie Name")]
    [TestCase("Movie.Name.1080p.BluRay.DD5.1.x264.mkv", "Movie Name")]
    [TestCase("Movie.Name.1080p.BluRay.5.1.x264.mkv", "Movie Name")]
    [TestCase("Movie.Name.1080p.BluRay.7.1.x264.mkv", "Movie Name")]
    [TestCase("Movie.Name.AAC.5.1.mkv", "Movie Name")]
    [TestCase("Movie.Name.DTS.7.1.mkv", "Movie Name")]
    [TestCase("Movie.DDP5.1.mkv", "Movie")]
    [TestCase("Movie.DD5.1.mkv", "Movie")]
    [TestCase("Movie.AAC.5.1.mkv", "Movie")]
    [TestCase("Movie.Name.1.1080p.mkv", "Movie Name 1")]
    [TestCase("Movie.Name.5.1080p.mkv", "Movie Name 5")]
    [TestCase("Movie.Name.7.1080p.mkv", "Movie Name 7")]
    [TestCase("Movie.Name.5.1.1080p.mkv", "Movie Name 5 1")]
    [TestCase("Movie.Name.7.1.1080p.mkv", "Movie Name 7 1")]
    [TestCase("Movie.Name.DDP5.2.x264.mkv", "Movie Name DDP5 2")]
    [TestCase("Movie.DDP5.1.Story.mkv", "Movie DDP5 1 Story")]
    public void ParseMovieFilename_PreservesTitle_WhenAudioChannelsAreInReleaseSuffix(
        string path,
        string expectedTitle)
    {
        // Arrange
        // Act
        var result = path.ParseMovieFilename();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Title, Is.EqualTo(expectedTitle));
            Assert.That(result.Year, Is.Null);
        }
    }

    [TestCase("Body Cam (2020).mkv", "Body Cam", 2020)]
    [TestCase("The Web (1947).mkv", "The Web", 1947)]
    [TestCase("Body.Cam.2020.1080p.BluRay.x264-GROUP.mkv", "Body Cam", 2020)]
    [TestCase("The.Web.1947.WEBRip.x264.mkv", "The Web", 1947)]
    [TestCase("Charlottes.Web.1080p.BluRay.x264.mkv", "Charlottes Web", null)]
    [TestCase("Movie.Title.1080p.2020.mkv", "Movie Title", 2020)]
    public void ParseMovieFilename_PreservesTitleWords_WhenReleaseVocabularyPrecedesYear(
        string path,
        string title,
        int? year)
    {
        // Arrange
        // Act
        var result = path.ParseMovieFilename();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Title, Is.EqualTo(title));
            Assert.That(result.Year, Is.EqualTo(year));
        }
    }

    [Test]
    public void Parse_ReturnsTitleAndYearWithoutGroup_WhenReleaseNameIsDotted()
    {
        // Arrange
        var path = "/library/movies/The.Matrix.1999.1080p.BluRay.x264-GROUP.mkv";

        // Act
        var result = path.ParseMovieFilename();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Title, Is.EqualTo("The Matrix"));
            Assert.That(result.Year, Is.EqualTo(1999));
        }
    }

    [Test]
    public void Parse_ExtractsTitleAndYear_WhenParenthesisedYear()
    {
        // Arrange
        var path = Path.Combine("/library/movies", "The Matrix (1999) 1080p.mkv");

        // Act
        var result = path.ParseMovieFilename();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Title, Is.EqualTo("The Matrix"));
            Assert.That(result.Year, Is.EqualTo(1999));
        }
    }

    [Test]
    public void Parse_ReturnsFolderYear_WhenFileNameOmitsYear()
    {
        // Arrange
        var path = Path.Combine("/library/movies/The Matrix (1999)", "The Matrix 1080p.mkv");

        // Act
        var result = path.ParseMovieFilename();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Title, Is.EqualTo("The Matrix"));
            Assert.That(result.Year, Is.EqualTo(1999));
        }
    }

    [Test]
    public void Parse_ExtractsBoth_WhenBareTitleYear()
    {
        // Arrange
        var path = "The.Matrix.1999.mkv";

        // Act
        var result = path.ParseMovieFilename();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Title, Is.EqualTo("The Matrix"));
            Assert.That(result.Year, Is.EqualTo(1999));
        }
    }

    [Test]
    public void Parse_PrefersTrailingYear_WhenTitleContainsYearLikeToken()
    {
        // Arrange
        var path = "2001 A Space Odyssey 1968 1080p BluRay.mkv";

        // Act
        var result = path.ParseMovieFilename();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Year, Is.EqualTo(1968));
            Assert.That(result.Title, Is.EqualTo("2001 A Space Odyssey"));
        }
    }

    [Test]
    public void Parse_ReturnsTitleOnly_WhenNoYear()
    {
        // Arrange
        var path = "Some.Random.Movie.mkv";

        // Act
        var result = path.ParseMovieFilename();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Title, Is.EqualTo("Some Random Movie"));
            Assert.That(result.Year, Is.Null);
        }
    }

    [Test]
    public void Parse_RemovedFromTitle_WhenGroupSuffix()
    {
        // Arrange
        var path = "Inception.2010.2160p.UHD.BluRay-PRESENT.mkv";

        // Act
        var result = path.ParseMovieFilename();

        // Assert
        Assert.That(result.Title, Is.EqualTo("Inception"));
    }
}
