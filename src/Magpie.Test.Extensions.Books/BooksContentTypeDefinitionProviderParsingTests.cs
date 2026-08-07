using Magpie.Extensions.Books;
using Magpie.Models.Signals;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Books;

[TestFixture]
public class BooksContentTypeDefinitionProviderParsingTests
{
    [Test]
    public void Parse_AuthorAndBookFolders_ExtractsAuthorTitleAndYear()
    {
        // Arrange
        var path = "/library/books/Andy Weir/The Martian (2011)/book.epub";

        // Act
        var result = new BooksContentTypeDefinitionProvider().Parse(path, out _)!;

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.GetFirstValue("author"), Is.EqualTo("Andy Weir"));
            Assert.That(result.GetFirstValue("title"), Is.EqualTo("The Martian"));
            Assert.That(result.GetFirstInt32("year"), Is.EqualTo(2011));
        }
    }

    [Test]
    public void Parse_ReturnsTitleAndYearOnly_WhenFlatFileHasYearAndNoParentFolder()
    {
        // Arrange
        var path = "/library/books/The Martian (2011).epub";

        // Act
        var result = new BooksContentTypeDefinitionProvider().Parse(path, out _)!;

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.GetFirstValue("author"), Is.Null);
            Assert.That(result.GetFirstValue("title"), Is.EqualTo("The Martian"));
            Assert.That(result.GetFirstInt32("year"), Is.EqualTo(2011));
        }
    }

    [Test]
    public void Parse_FlatFileNoYearAnywhere_ReturnsStemTitleOnly()
    {
        // Arrange
        var path = "/library/books/The Martian.epub";

        // Act
        var result = new BooksContentTypeDefinitionProvider().Parse(path, out _)!;

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.GetFirstValue("author"), Is.Null);
            Assert.That(result.GetFirstValue("title"), Is.EqualTo("The Martian"));
            Assert.That(result.GetFirstInt32("year"), Is.Null);
        }
    }

    [Test]
    public void Parse_ReturnsParentTitle_WhenAudiobookFileIsAnOrderedPart()
    {
        // Arrange
        var path = "/library/books/Andy Weir/Project Hail Mary (2021)/01 - Chapter 1.mp3";

        // Act
        var result = new BooksContentTypeDefinitionProvider().Parse(path, out _)!;

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.GetFirstValue("author"), Is.EqualTo("Andy Weir"));
            Assert.That(result.GetFirstValue("title"), Is.EqualTo("Project Hail Mary"));
            Assert.That(result.GetFirstInt32("year"), Is.EqualTo(2021));
        }
    }
}
