using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.OpenLibrary;
using Magpie.Extensions.OpenLibrary.QueryEngine;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Models.MetadataSearch;
using Magpie.Sdk.Content;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Extensions.OpenLibrary;

[TestFixture]
public sealed class ContentMetadataSearcherTests
{
    private Mock<IContentTypeDefinitionProvider> _definitionProvider = null!;
    private Mock<ISearchEngine> _searchEngine = null!;
    private ContentHierarchyLevelDefinition _ebook = null!;
    private ContentMetadataSearcher _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _ebook = new ContentHierarchyLevelDefinition
        {
            Slug = "ebook",
            DisplayName = "E-book",
            SupportedMetadataFields = MetadataField.Language | MetadataField.PublishDate |
                MetadataField.ExternalIds | MetadataField.Images | MetadataField.ContentRatings |
                MetadataField.Relationships | MetadataField.SequenceNumber,
            CustomMetadataFields = []
        };
        _definitionProvider = new Mock<IContentTypeDefinitionProvider>(MockBehavior.Strict);
        _definitionProvider.SetupGet(value => value.Definition).Returns(new ContentTypeDefinition
        {
            Slug = "books",
            DisplayName = "Books",
            LevelDefinitions = [_ebook, _ebook with { Slug = "audiobook", DisplayName = "Audiobook" }]
        });
        _searchEngine = new Mock<ISearchEngine>(MockBehavior.Strict);
        _searchEngine.Setup(value => value.SearchAsync(It.IsAny<QueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryResult
            {
                Hits = [new WorkHit { WorkOlid = "OL1W", Title = "Dune", Authors = [] }],
                PageInfo = new ProviderSearchPageInfo { RequestedPage = 1, HasMore = false }
            });
        _subject = new ContentMetadataSearcher(
            [_definitionProvider.Object], _searchEngine.Object, NullLogger<ContentMetadataSearcher>.Instance);
    }

    [Test]
    public void SearchAsync_Throws_WhenTheProviderFails()
    {
        // Arrange
        _searchEngine.Setup(value => value.SearchAsync(It.IsAny<QueryRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Provider unavailable."));

        // Act and Assert
        Assert.ThrowsAsync<HttpRequestException>(() => _subject.SearchAsync(_ebook, "Book", ""));
    }

    [Test]
    public async Task AdvancedSearchAsync_CandidateSurvivesRootLevelResolution_WhenLevelIsUnspecified()
    {
        // Arrange
        var query = new ContentHierarchyItemCriteria();

        // Act
        var result = await _subject.AdvancedSearchAsync("books", query, 10, "").ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Supported, Is.True);
            Assert.That(result.Matches, Has.Count.EqualTo(1));
            Assert.That(result.Matches[0].ExternalId, Is.EqualTo("OL1W"));
            Assert.That(result.Matches[0].ContentType, Is.EqualTo("books"));
            Assert.That(result.Warnings, Is.Empty);
        }

        Assert.That(result.Matches[0].Level, Is.EqualTo("ebook"));
    }

    [Test]
    public async Task AdvancedSearchAsync_NotSupported_WhenContentTypeIsUnknown()
    {
        // Arrange
        var query = new ContentHierarchyItemCriteria();

        // Act
        var result = await _subject.AdvancedSearchAsync("movies", query, 10, "").ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Supported, Is.False);
            Assert.That(result.Matches, Is.Empty);
            Assert.That(result.Warnings, Is.Empty);
        }

        _searchEngine.VerifyNoOtherCalls();
    }

    [TestCase("ebook")]
    [TestCase("audiobook")]
    public async Task AdvancedSearchAsync_CorrectCandidateLevel_WhenLevelIsExplicit(string level)
    {
        // Arrange
        var query = new ContentHierarchyItemCriteria
        {
            Level = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Value = level }]
            }
        };

        // Act
        var result = await _subject.AdvancedSearchAsync("books", query, 10, "").ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Supported, Is.True);
            Assert.That(result.Matches, Has.Count.EqualTo(1));
            Assert.That(result.Matches[0].Level, Is.EqualTo(level));
        }
    }

    [TestCase("ebook")]
    [TestCase("audiobook")]
    public async Task HydrateAsync_CandidateKeepsTargetLevel_WhenLevelIsExplicit(string level)
    {
        // Arrange
        _searchEngine
            .Setup(value => value.ResolveWorkAsync(It.IsAny<QueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkResolution
            {
                Kind = WorkResolutionKind.Resolved,
                Hit = new WorkHit { WorkOlid = "OL1W", Title = "Dune", Authors = [] }
            });
        var candidate = new MetadataSearchCandidate
        {
            ProviderType = "openlibrary",
            ExternalIdSource = "openlibrary",
            ExternalId = "OL1W",
            ContentType = "books",
            Level = level
        };

        // Act
        var hydrated = await _subject.HydrateAsync(candidate, "").ConfigureAwait(false);

        // Assert
        Assert.That(hydrated, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(hydrated!.Title, Is.EqualTo("Dune"));
            Assert.That(hydrated.Level, Is.EqualTo(level));
        }
    }

    [Test]
    public async Task HydrateAsync_NoCandidate_WhenLevelIsAmbiguous()
    {
        // Arrange
        var candidate = new MetadataSearchCandidate
        {
            ProviderType = "openlibrary",
            ExternalIdSource = "openlibrary",
            ExternalId = "OL1W",
            ContentType = "books"
        };

        // Act
        var hydrated = await _subject.HydrateAsync(candidate, "").ConfigureAwait(false);

        // Assert
        Assert.That(hydrated, Is.Null);
        _searchEngine.VerifyNoOtherCalls();
    }

    [Test]
    public async Task AdvancedSearchAsync_Candidate_WhenOnlyOneHierarchyExists()
    {
        // Arrange
        _definitionProvider.SetupGet(value => value.Definition).Returns(new ContentTypeDefinition
        {
            Slug = "books",
            DisplayName = "Books",
            LevelDefinitions = [_ebook]
        });
        var subject = new ContentMetadataSearcher(
            [_definitionProvider.Object], _searchEngine.Object, NullLogger<ContentMetadataSearcher>.Instance);

        // Act
        var result = await subject.AdvancedSearchAsync("books", new ContentHierarchyItemCriteria(), 10, "")
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Supported, Is.True);
            Assert.That(result.Matches, Has.Count.EqualTo(1));
            Assert.That(result.Matches[0].Level, Is.EqualTo("ebook"));
        }
    }
}
