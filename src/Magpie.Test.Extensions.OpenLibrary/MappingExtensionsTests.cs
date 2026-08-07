using System.Linq;
using System.Text.Json;

using Magpie.Extensions.OpenLibrary.Mapping;
using Magpie.Extensions.OpenLibrary.QueryEngine;

using NUnit.Framework;

namespace Magpie.Test.Extensions.OpenLibrary;

[TestFixture]
public sealed class MappingExtensionsTests
{
    [Test]
    public void ToSourcePage_HitsAndPaging_WhenDocumentsAreValid()
    {
        // Arrange
        var response = JsonSerializer.Deserialize<SearchResponseDto>(
            """
            {
              "numFound": 42,
              "numFoundExact": true,
              "start": 20,
              "docs": [
                {
                  "key": "/works/OL1W",
                  "title": "  Dune  ",
                  "subtitle": "Book One",
                  "first_publish_year": 1965,
                  "cover_i": 7,
                  "cover_edition_key": "/books/OL2M",
                  "language": ["eng"],
                  "isbn": ["9780143111597"]
                },
                {"key": "OL3W", "title": "Messiah", "first_publish_year": 0}
              ]
            }
            """);

        // Act
        var page = response!.ToSourcePage();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.NumFound, Is.EqualTo(42));
            Assert.That(page.NumFoundExact, Is.True);
            Assert.That(page.Offset, Is.EqualTo(20));
            Assert.That(page.SourceRowCount, Is.EqualTo(2));
            Assert.That(page.Hits.Select(hit => hit.WorkOlid), Is.EqualTo(["OL1W", "OL3W"]));
            Assert.That(page.Hits[0].Title, Is.EqualTo("Dune"));
            Assert.That(page.Hits[0].Subtitle, Is.EqualTo("Book One"));
            Assert.That(page.Hits[0].FirstPublishYear, Is.EqualTo(1965));
            Assert.That(page.Hits[0].CoverId, Is.EqualTo(7));
            Assert.That(page.Hits[0].CoverEditionOlid, Is.EqualTo("OL2M"));
            Assert.That(page.Hits[0].Isbns, Is.EqualTo(["9780143111597"]));
            Assert.That(page.Hits[1].FirstPublishYear, Is.Null);
        }
    }

    [TestCase("""{"title":"No Key"}""")]
    [TestCase("""{"key":"/works/OL1W"}""")]
    [TestCase("""{"key":"/works/OL1W","title":"   "}""")]
    [TestCase("""{"key":"/authors/OL1A","title":"Wrong Suffix"}""")]
    public void ToSourcePage_SkipsDocument_WhenKeyOrTitleIsUnusable(string document)
    {
        // Arrange
        var response = JsonSerializer.Deserialize<SearchResponseDto>(
            $$"""
            {"numFound":1,"numFoundExact":true,"start":0,"docs":[{{document}}]}
            """);

        // Act
        var page = response!.ToSourcePage();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.Hits, Is.Empty);
            Assert.That(page.SourceRowCount, Is.EqualTo(1));
        }
    }

    [Test]
    public void ToSourcePage_DeduplicatesPublishers_WhenCasingRepeats()
    {
        // Arrange
        var response = JsonSerializer.Deserialize<SearchResponseDto>(
            """
            {
              "numFound": 1,
              "numFoundExact": true,
              "start": 0,
              "docs": [
                {
                  "key": "/works/OL1W",
                  "title": "Dune",
                  "publisher": ["Penguin", "penguin", " PENGUIN ", "Penguin Books"]
                }
              ]
            }
            """);

        // Act
        var page = response!.ToSourcePage();

        // Assert
        Assert.That(page.Hits[0].Publishers, Is.EqualTo(["Penguin", "Penguin Books"]));
    }

    [Test]
    public void ToSourcePage_PairsAndDeduplicatesAuthors_WhenKeysAndNamesRepeat()
    {
        // Arrange
        var response = JsonSerializer.Deserialize<SearchResponseDto>(
            """
            {
              "numFound": 1,
              "numFoundExact": true,
              "start": 0,
              "docs": [
                {
                  "key": "/works/OL1W",
                  "title": "Dune",
                  "author_key": ["OL1A", "ol1a", "OL2A", "OL4A"],
                  "author_name": ["Frank Herbert", "FRANK HERBERT", "Brian Herbert"]
                }
              ]
            }
            """);

        // Act
        var page = response!.ToSourcePage();

        // Assert
        Assert.That(
            page.Hits[0].Authors,
            Is.EqualTo([new AuthorPair("OL1A", "Frank Herbert"), new AuthorPair("OL2A", "Brian Herbert")]));
    }

    [Test]
    public void ToSourcePage_ClampsPaging_WhenCountsAreNegative()
    {
        // Arrange
        var response = JsonSerializer.Deserialize<SearchResponseDto>(
            """
            {"numFound":-5,"numFoundExact":false,"start":-3,"docs":[]}
            """);

        // Act
        var page = response!.ToSourcePage();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.NumFound, Is.Zero);
            Assert.That(page.Offset, Is.Zero);
            Assert.That(page.SourceRowCount, Is.Zero);
            Assert.That(page.Hits, Is.Empty);
        }
    }
}
