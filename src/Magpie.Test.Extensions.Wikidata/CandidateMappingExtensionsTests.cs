using System;
using System.Collections.Generic;
using System.Linq;

using Magpie.Extensions.Wikidata;
using Magpie.Extensions.Wikidata.Mapping;
using Magpie.Extensions.Wikidata.QueryEngine;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Filtering;
using Magpie.Models.MetadataSearch;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Wikidata;

[TestFixture]
internal sealed class CandidateMappingExtensionsTests
{
    [TestCase(
        "1999-01-01T00:00:00Z|9|http://www.wikidata.org/entity/Q1985727|0",
        TemporalPrecision.Year,
        1,
        1,
        0,
        0)]
    [TestCase(
        "1999-03-01T00:00:00Z|10|http://www.wikidata.org/entity/Q1985727|0",
        TemporalPrecision.Month,
        3,
        1,
        0,
        0)]
    [TestCase(
        "1999-03-31T00:00:00Z|11|http://www.wikidata.org/entity/Q1985727|0",
        TemporalPrecision.Day,
        3,
        31,
        0,
        0)]
    [TestCase(
        "1999-03-31T12:30:00Z|12|http://www.wikidata.org/entity/Q1985727|0",
        TemporalPrecision.Instant,
        3,
        31,
        12,
        30)]
    public void ToSearchHit_MapsPeriodStartAcrossOutputs_WhenTemporalMetadataIsSupported(
        string encodedDate,
        TemporalPrecision expectedPrecision,
        int expectedMonth,
        int expectedDay,
        int expectedHour,
        int expectedMinute)
    {
        // Arrange
        var binding = new Dictionary<string, SparqlBindingDto>
        {
            ["item"] = new SparqlBindingDto
            {
                Type = "uri",
                Value = "http://www.wikidata.org/entity/Q83495"
            },
            ["type"] = new SparqlBindingDto
            {
                Type = "uri",
                Value = "http://www.wikidata.org/entity/Q11424"
            },
            ["itemLabel"] = new SparqlBindingDto { Type = "literal", Value = "The Matrix" },
            ["publishDate"] = new SparqlBindingDto { Type = "literal", Value = encodedDate },
            ["endDate"] = new SparqlBindingDto { Type = "literal", Value = encodedDate }
        };
        var expected = new DateTime(
            1999,
            expectedMonth,
            expectedDay,
            expectedHour,
            expectedMinute,
            0,
            DateTimeKind.Utc);

        // Act
        var hit = binding.ToSearchHit();
        var candidate = hit!.ToMetadataSearchCandidate(EntityKind.Show, Constants.ShowLevel);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(hit, Is.Not.Null);
            Assert.That(hit!.PublishDate?.Precision, Is.EqualTo(expectedPrecision));
            Assert.That(hit.EndDate?.Precision, Is.EqualTo(expectedPrecision));
            Assert.That(candidate.Year, Is.EqualTo(1999));
            Assert.That(candidate.ReleaseDate, Is.EqualTo(expected));
            Assert.That(candidate.ReleaseDatePrecision, Is.EqualTo(expectedPrecision));
        }
    }

    [TestCase("1999-01-01T00:00:00Z|9|http://www.wikidata.org/entity/Q1985786|0")]
    [TestCase("1999-01-01T00:00:00Z|9|http://www.wikidata.org/entity/Q1985727|60")]
    [TestCase("1999-01-01T00:00:00Z|8|http://www.wikidata.org/entity/Q1985727|0")]
    [TestCase("1999-01-01T00:00:00Z")]
    public void ToSearchHit_SkipsDate_WhenTemporalMetadataIsUnsupported(string encodedDate)
    {
        // Arrange
        var binding = new Dictionary<string, SparqlBindingDto>
        {
            ["item"] = new SparqlBindingDto
            {
                Type = "uri",
                Value = "http://www.wikidata.org/entity/Q83495"
            },
            ["type"] = new SparqlBindingDto
            {
                Type = "uri",
                Value = "http://www.wikidata.org/entity/Q11424"
            },
            ["itemLabel"] = new SparqlBindingDto { Type = "literal", Value = "The Matrix" },
            ["publishDate"] = new SparqlBindingDto { Type = "literal", Value = encodedDate }
        };

        // Act
        var result = binding.ToSearchHit();

        // Assert
        Assert.That(result?.PublishDate, Is.Null);
    }

    [Test]
    public void ToSearchHit_MarksEndDatePresence_WhenEndPropertyExistsWithoutParsedDate()
    {
        // Arrange
        var binding = new Dictionary<string, SparqlBindingDto>
        {
            ["item"] = new SparqlBindingDto
            {
                Type = "uri",
                Value = "http://www.wikidata.org/entity/Q83495"
            },
            ["type"] = new SparqlBindingDto
            {
                Type = "uri",
                Value = "http://www.wikidata.org/entity/Q5398426"
            },
            ["itemLabel"] = new SparqlBindingDto { Type = "literal", Value = "Ended show" },
            ["hasEndDate"] = new SparqlBindingDto { Type = "literal", Value = "1" }
        };

        // Act
        var hit = binding.ToSearchHit();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(hit!.EndDate, Is.Null);
            Assert.That(hit.HasEndDate, Is.True);
        }
    }

    [Test]
    public void ToSearchHit_MapsSingleEscapedThumbnail_WhenCommonsFilePathIri()
    {
        // Arrange
        var binding = new Dictionary<string, SparqlBindingDto>
        {
            ["item"] = new SparqlBindingDto
            {
                Type = "uri",
                Value = "http://www.wikidata.org/entity/Q83495"
            },
            ["type"] = new SparqlBindingDto
            {
                Type = "uri",
                Value = "http://www.wikidata.org/entity/Q11424"
            },
            ["itemLabel"] = new SparqlBindingDto { Type = "literal", Value = "The Matrix" },
            ["posterFilename"] = new SparqlBindingDto
            {
                Type = "uri",
                Value = "http://commons.wikimedia.org/wiki/Special:FilePath/The%20Matrix%20Poster.jpg"
            }
        };

        // Act
        var hit = binding.ToSearchHit();
        var candidate = hit!.ToMetadataSearchCandidate(EntityKind.Movie, Constants.MovieLevel);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(hit?.PosterFilename, Is.EqualTo("The Matrix Poster.jpg"));
            Assert.That(
                candidate.ThumbnailUrl?.AbsoluteUri,
                Is.EqualTo(
                    "https://commons.wikimedia.org/wiki/Special:Redirect/file/The%20Matrix%20Poster.jpg"));
            Assert.That(candidate.ThumbnailUrl?.AbsoluteUri, Does.Not.Contain("http%3A"));
        }
    }

    [Test]
    public void ToSearchHit_PreservesEveryProjectedLabel_WhenMoreThanThirtyGenres()
    {
        // Arrange
        var genres = Enumerable.Range(1, 31).Select(value => "Genre " + value).ToArray();
        var binding = new Dictionary<string, SparqlBindingDto>
        {
            ["item"] = new SparqlBindingDto
            {
                Type = "uri",
                Value = "http://www.wikidata.org/entity/Q83495"
            },
            ["type"] = new SparqlBindingDto
            {
                Type = "uri",
                Value = "http://www.wikidata.org/entity/Q11424"
            },
            ["itemLabel"] = new SparqlBindingDto { Type = "literal", Value = "The Matrix" },
            ["genres"] = new SparqlBindingDto
            {
                Type = "literal",
                Value = string.Join(SparqlQueryBuilder.GenreSeparator, genres)
            }
        };

        // Act
        var result = binding.ToSearchHit();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.Genres, Has.Count.EqualTo(31));
            Assert.That(result.Genres[^1], Is.EqualTo("Genre 31"));
        }
    }

    [Test]
    public void ToMetadataSearchCandidate_MergesTheSeedExternalIds_WhenTheContributionHasItsOwn()
    {
        // Arrange
        var contribution = new MetadataContribution { ProviderType = Constants.ProviderType, Title = "The Matrix" };
        contribution.ExternalIds.Add(new ExternalId { Source = Constants.ProviderType, Value = "Q83495" });
        contribution.ExternalIds.Add(new ExternalId { Source = "imdb", Value = "tt0133093" });
        var seed = new MetadataSearchCandidate
        {
            ProviderType = "seed-provider",
            ExternalIdSource = "seed-source",
            ExternalId = "seed-id",
            ContentType = "seed-content",
            Level = Constants.MovieLevel,
            Title = "The Matrix",
            AdditionalExternalIds =
            [
                new MetadataSearchCandidateExternalId { Source = "tmdb", Value = "603" },
                new MetadataSearchCandidateExternalId { Source = "IMDB", Value = "tt0000000" }
            ]
        };

        // Act
        var result = contribution.ToMetadataSearchCandidate(seed, EntityKind.Movie);

        // Assert
        Assert.That(
            result!.AdditionalExternalIds.Select(id => (id.Source, id.Value)),
            Is.EqualTo([("imdb", "tt0133093"), ("tmdb", "603")]));
    }

    [Test]
    public void ToMetadataSearchCandidate_UsesContributionPrecision_WhenContributionHasPublishDate()
    {
        // Arrange
        var contribution = new MetadataContribution
        {
            ProviderType = Constants.ProviderType,
            PublishDate = new DateTime(1999, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            PublishDatePrecision = TemporalPrecision.Year
        };
        contribution.ExternalIds.Add(new() { Source = Constants.ProviderType, Value = "Q12345" });
        var seed = new MetadataSearchCandidate
        {
            ProviderType = "seed-provider",
            ExternalIdSource = "seed-source",
            ExternalId = "seed-id",
            ContentType = "seed-content",
            ReleaseDate = new DateTime(2001, 2, 3, 0, 0, 0, DateTimeKind.Utc),
            ReleaseDatePrecision = TemporalPrecision.Day
        };

        // Act
        var result = contribution.ToMetadataSearchCandidate(seed, EntityKind.Movie);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.ReleaseDate, Is.EqualTo(new DateTime(1999, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(result.ReleaseDatePrecision, Is.EqualTo(TemporalPrecision.Year));
        }
    }

    [Test]
    public void ToMetadataSearchCandidate_PreservesSeedFields_WhenContributionFieldsAreBlank()
    {
        // Arrange
        var contribution = new MetadataContribution
        {
            ProviderType = Constants.ProviderType,
            Title = " ",
            Overview = null,
            Language = "",
            PublishDate = null
        };
        contribution.ExternalIds.Add(new ExternalId
        {
            Source = Constants.ProviderType,
            Value = "Q12345"
        });
        var seed = new MetadataSearchCandidate
        {
            ProviderType = "seed-provider",
            ExternalIdSource = "seed-source",
            ExternalId = "seed-id",
            ContentType = "seed-content",
            Level = Constants.ShowLevel,
            Title = "Seed title",
            Year = 2001,
            ReleaseDate = new DateTime(2001, 2, 3, 0, 0, 0, DateTimeKind.Utc),
            ReleaseDatePrecision = TemporalPrecision.Month,
            Overview = "Seed overview",
            Language = "en",
            ThumbnailUrl = new Uri("https://example.com/seed.jpg"),
            SubjectReferences =
            [
                new ProviderMetadataSearchSubjectReference
                {
                    SubjectKind = "person",
                    DisplayName = "Seed person",
                    RelationshipKind = "cast",
                    ExternalIds = new Dictionary<string, string>
                    {
                        [Constants.ProviderType] = "Q67890"
                    }
                }
            ],
        };

        // Act
        var result = contribution.ToMetadataSearchCandidate(seed, EntityKind.Show);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.ProviderType, Is.EqualTo("wikidata"));
            Assert.That(result.ExternalIdSource, Is.EqualTo("wikidata"));
            Assert.That(result.ExternalId, Is.EqualTo("Q12345"));
            Assert.That(result.ContentType, Is.EqualTo("tv"));
            Assert.That(result.Level, Is.EqualTo("show"));
            Assert.That(result.Title, Is.EqualTo("Seed title"));
            Assert.That(result.Year, Is.EqualTo(2001));
            Assert.That(
                result.ReleaseDate,
                Is.EqualTo(
                    new DateTime(2001, 2, 3, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(result.ReleaseDatePrecision, Is.EqualTo(TemporalPrecision.Month));
            Assert.That(result.Overview, Is.EqualTo("Seed overview"));
            Assert.That(result.Language, Is.EqualTo("en"));
            Assert.That(result.ThumbnailUrl, Is.EqualTo(new Uri("https://example.com/seed.jpg")));
            Assert.That(result.SubjectReferences, Is.SameAs(seed.SubjectReferences));
        }
    }
}
