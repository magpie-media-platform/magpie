using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.OpenLibrary;
using Magpie.Extensions.OpenLibrary.Mapping;
using Magpie.Extensions.OpenLibrary.QueryEngine;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Sdk.Content;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Extensions.OpenLibrary;

[TestFixture]
public sealed class ContentMetadataProviderTests
{
    private Mock<ISearchEngine> _searchEngine = null!;
    private Mock<IContentTypeDefinitionProvider> _definitionProvider = null!;
    private IContentHierarchyMetadataProvider _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _searchEngine = new Mock<ISearchEngine>(MockBehavior.Strict);
        _definitionProvider = new Mock<IContentTypeDefinitionProvider>(MockBehavior.Strict);
        _definitionProvider.SetupGet(value => value.Definition).Returns(new ContentTypeDefinition
        {
            Slug = "books",
            DisplayName = "Books",
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "book",
                    DisplayName = "Book",
                    SupportedMetadataFields = MetadataField.Language |
                        MetadataField.PublishDate |
                        MetadataField.ExternalIds |
                        MetadataField.Images |
                        MetadataField.ContentRatings |
                        MetadataField.Relationships |
                        MetadataField.SequenceNumber,
                    CustomMetadataFields = []
                }
            ]
        });
        _subject = new ContentMetadataProvider(
            [_definitionProvider.Object],
            _searchEngine.Object,
            new FakeTimeProvider(new DateTimeOffset(2026, 7, 21, 0, 0, 0, TimeSpan.Zero)),
            NullLogger<ContentMetadataProvider>.Instance);
    }

    [Test]
    public async Task TryGetMetadataAsync_ResolvedWorkContribution_WhenTheBookHasNoRepresentativeEdition()
    {
        // Arrange
        var hit = new WorkHit
        {
            WorkOlid = "OL1W",
            Title = "Book",
            Authors = [new AuthorPair("OL1A", "Author")],
            Description = "Overview",
            Subjects = ["Fantasy"]
        };
        _searchEngine
            .Setup(value => value.ResolveWorkAsync(It.IsAny<QueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkResolution { Kind = WorkResolutionKind.Resolved, Hit = hit });
        _searchEngine
            .Setup(value => value.GetRepresentativeEditionAsync(hit, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EditionDto?)null);
        var contribution = new MetadataContribution { ProviderType = Constants.ProviderType };
        contribution.ExternalIds.Add(new ExternalId
        {
            Source = Constants.ProviderType,
            Value = "OL1W"
        });

        // Act
        var result = await _subject.TryGetMetadataAsync(new MetadataFetchRequest
        {
            Item = new ContentHierarchyItem
            {
                Id = Guid.NewGuid(),
                ContentType = "books",
                Level = "book",
                Slug = "book",
                Name = "Book"
            },
            ExistingMetadata = [contribution],
            DefaultLanguagePreference = string.Empty
        }).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result?.Contribution?.Overview, Is.EqualTo("Overview"));
            Assert.That(result?.Contribution?.Genres.Single().Name, Is.EqualTo("Fantasy"));
            Assert.That(result?.Children, Is.Empty);
        }
    }

    [Test]
    public async Task TryGetMetadataAsync_ExactPagesAndEditionCovers_WhenTheBookHasACoverEdition()
    {
        // Arrange
        var hit = new WorkHit
        {
            WorkOlid = "OL1W",
            Title = "Book",
            Authors = [new AuthorPair("OL1A", "Author")],
            FirstPublishYear = 1937,
            CoverEditionOlid = "OL9M",
            NumberOfPagesMedian = 300
        };
        var edition = new EditionDto
        {
            NumberOfPages = 310,
            PhysicalFormat = "Hardcover",
            Series = ["Middle Earth #1"],
            PublishDate = "September 21, 1937",
            Covers = [7, -1],
            Identifiers = new Dictionary<string, IReadOnlyList<string>>
            {
                ["librarything"] = ["6446"]
            }
        };
        _searchEngine
            .Setup(value => value.ResolveWorkAsync(It.IsAny<QueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkResolution { Kind = WorkResolutionKind.Resolved, Hit = hit });
        _searchEngine
            .Setup(value => value.GetRepresentativeEditionAsync(hit, It.IsAny<CancellationToken>()))
            .ReturnsAsync(edition);
        var contribution = new MetadataContribution { ProviderType = Constants.ProviderType };
        contribution.ExternalIds.Add(new ExternalId
        {
            Source = Constants.ProviderType,
            Value = "OL1W"
        });

        // Act
        var result = await _subject.TryGetMetadataAsync(new MetadataFetchRequest
        {
            Item = new ContentHierarchyItem
            {
                Id = Guid.NewGuid(),
                ContentType = "books",
                Level = "book",
                Slug = "book",
                Name = "Book"
            },
            ExistingMetadata = [contribution],
            DefaultLanguagePreference = string.Empty
        }).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result?.Contribution?.CustomMetadata.Single(value => value.Key == "pages").Value,
                Is.EqualTo("310"));
            Assert.That(
                result?.Contribution?.CustomMetadata.Single(value => value.Key == "format").Value,
                Is.EqualTo("Hardcover"));
            Assert.That(
                result?.Contribution?.CustomMetadata.Single(value => value.Key == "series").Value,
                Is.EqualTo("Middle Earth #1"));
            Assert.That(
                result?.Contribution?.PublishDate,
                Is.EqualTo(new DateTime(1937, 9, 21, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(
                result?.Contribution?.ExternalIds.Select(value => (value.Source, value.Value)),
                Does.Contain(("librarything", "6446")));
            Assert.That(
                result?.Contribution?.Images.Select(value => value.Url.AbsoluteUri),
                Is.EqualTo(
                [
                    "https://covers.openlibrary.org/b/olid/OL9M-L.jpg?default=false",
                    "https://covers.openlibrary.org/b/id/7-L.jpg?default=false"
                ]));
        }
    }

    [TestCase(null, 1, 1, TemporalPrecision.Year)]
    [TestCase("March 1975", 3, 1, TemporalPrecision.Month)]
    [TestCase("March 21, 1975", 3, 21, TemporalPrecision.Day)]
    public async Task TryGetMetadataAsync_CarriesThePublishDatePrecision_WhenTheWorkHasAPublishDate(
        string? editionPublishDate,
        int month,
        int day,
        TemporalPrecision precision)
    {
        // Arrange
        var hit = new WorkHit
        {
            WorkOlid = "OL1W",
            Title = "Book",
            Authors = [new AuthorPair("OL1A", "Author")],
            FirstPublishYear = 1975
        };
        _searchEngine
            .Setup(value => value.ResolveWorkAsync(It.IsAny<QueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkResolution { Kind = WorkResolutionKind.Resolved, Hit = hit });
        _searchEngine
            .Setup(value => value.GetRepresentativeEditionAsync(hit, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EditionDto { PublishDate = editionPublishDate });
        var contribution = new MetadataContribution { ProviderType = Constants.ProviderType };
        contribution.ExternalIds.Add(new ExternalId
        {
            Source = Constants.ProviderType,
            Value = "OL1W"
        });

        // Act
        var result = await _subject.TryGetMetadataAsync(new MetadataFetchRequest
        {
            Item = new ContentHierarchyItem
            {
                Id = Guid.NewGuid(),
                ContentType = "books",
                Level = "book",
                Slug = "book",
                Name = "Book"
            },
            ExistingMetadata = [contribution],
            DefaultLanguagePreference = string.Empty
        }).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result?.Contribution?.PublishDate,
                Is.EqualTo(new DateTime(1975, month, day, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(result?.Contribution?.PublishDatePrecision, Is.EqualTo(precision));
        }
    }

    [Test]
    public async Task TryGetMetadataAsync_Null_WhenWorkCannotBeResolved()
    {
        // Arrange
        _searchEngine
            .Setup(value => value.ResolveWorkAsync(It.IsAny<QueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkResolution { Kind = WorkResolutionKind.NotFound });
        var contribution = new MetadataContribution { ProviderType = Constants.ProviderType };
        contribution.ExternalIds.Add(new ExternalId
        {
            Source = Constants.ProviderType,
            Value = "OL1W"
        });

        // Act
        var result = await _subject.TryGetMetadataAsync(new MetadataFetchRequest
        {
            Item = new ContentHierarchyItem
            {
                Id = Guid.NewGuid(),
                ContentType = "books",
                Level = "book",
                Slug = "book",
                Name = "Book"
            },
            ExistingMetadata = [contribution],
            DefaultLanguagePreference = string.Empty
        }).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Null);
    }
}
