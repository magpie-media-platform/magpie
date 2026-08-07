using System.IO;

using Magpie.Extensions.Movies.Parsing;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Movies.Parsing;

[TestFixture]
public class FilePathExtensionsTests
{
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
    public void Parse_ParenthesisedYear_ExtractsTitleAndYear()
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
    public void Parse_BareTitleYear_ExtractsBoth()
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
    public void Parse_TitleContainsYearLikeToken_PrefersTrailingYear()
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
    public void Parse_NoYear_ReturnsTitleOnly()
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
    public void Parse_GroupSuffix_RemovedFromTitle()
    {
        // Arrange
        var path = "Inception.2010.2160p.UHD.BluRay-PRESENT.mkv";

        // Act
        var result = path.ParseMovieFilename();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Title, Is.EqualTo("Inception"));
        }
    }
}
