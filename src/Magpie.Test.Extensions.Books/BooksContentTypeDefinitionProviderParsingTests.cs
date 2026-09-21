using System;
using System.Collections.Generic;

using Magpie.Extensions.Books;
using Magpie.Models.Data;
using Magpie.Models.Releases;
using Magpie.Models.Signals;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Books;

[TestFixture]
public class BooksContentTypeDefinitionProviderParsingTests
{
    [Test]
    public void Parse_ExtractsAuthorTitleAndYear_WhenAuthorAndBookFolders()
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
    public void Parse_ReturnsStemTitleOnly_WhenFlatFileNoYearAnywhere()
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

    [TestCase("The Martian", "The Martian (2011) [EPUB]", ReleaseMatchResult.Confident)]
    [TestCase("The Martian", "Andy Weir - The Martian (2011) [EPUB]", ReleaseMatchResult.Confident)]
    [TestCase("The Martian", "Andy_Weir_-_The_Martian_(2011)_[EPUB]", ReleaseMatchResult.Confident)]
    [TestCase("The Martian", "Andy Weir - The Martian (2012) [EPUB]", ReleaseMatchResult.NoMatch)]
    [TestCase("The Martian", "Andy Weir - The Martian [EPUB]", ReleaseMatchResult.Indeterminate)]
    [TestCase("The Martian", "Andy Weir - The Martian (2011) [M4B]", ReleaseMatchResult.NoMatch)]
    [TestCase("The Martian", "Andy Weir - The Martians (2011) [EPUB]", ReleaseMatchResult.NoMatch)]
    [TestCase("The Martian", "Andy Weir - NotThe Martian (2011) [EPUB]", ReleaseMatchResult.NoMatch)]
    [TestCase("The Martian", "Andy Weir - Project Hail Mary (2011) [EPUB]", ReleaseMatchResult.NoMatch)]
    [TestCase(null, "Andy Weir - The Martian (2011) [EPUB]", ReleaseMatchResult.Indeterminate)]
    [TestCase("...", "Andy Weir - The Martian (2011) [EPUB]", ReleaseMatchResult.NoMatch)]
    public void IsMatch_Result_WhenBookTitleIsBoundedWithinRelease(
        string? title,
        string releaseTitle,
        ReleaseMatchResult expected)
    {
        // Arrange
        var subject = new BooksContentTypeDefinitionProvider();
        var item = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ContentType = "book",
            Level = "ebook",
            Slug = "the-martian",
            Name = "The Martian"
        };
        var target = new ContentTarget
        {
            Item = item,
            Lineage = [item],
            Metadata = new Dictionary<Guid, MaterializedMetadata>
            {
                [item.Id] = new() { Title = title, PublishDate = new DateTime(2011, 1, 1) }
            }
        };

        // Act
        var result = subject.IsMatch(target, [], new Release { Protocol = "torrent", Title = releaseTitle }, []);

        // Assert
        Assert.That(result, Is.EqualTo(expected));
    }
}
