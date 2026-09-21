using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Tvmaze;
using Magpie.Extensions.Tvmaze.Mapping;
using Magpie.Extensions.Tvmaze.Pipeline;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Filtering;
using Magpie.Models.MetadataSearch;
using Magpie.Sdk.Content;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Tvmaze;

[TestFixture]
public class TvmazeContentMetadataSearcherTests
{
    private static readonly ContentHierarchyLevelDefinition ShowLevel = new()
    {
        Slug = "show",
        ParentSlug = null,
        DisplayName = "show",
        CustomMetadataFields = []
    };

    private static readonly ContentHierarchyLevelDefinition EpisodeLevel = new()
    {
        Slug = "episode",
        ParentSlug = "season",
        DisplayName = "episode",
        CustomMetadataFields = []
    };

    private Mock<IClient> _client = null!;
    private Mock<ISearchPlanner> _planner = null!;
    private Mock<ISearchExecutor> _executor = null!;
    private IContentHierarchyMetadataAdvancedSearcher _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _client = new Mock<IClient>();
        _planner = new Mock<ISearchPlanner>();
        _executor = new Mock<ISearchExecutor>();
        _subject = new ContentMetadataSearcher(
            _client.Object,
            _planner.Object,
            _executor.Object,
            NullLogger<ContentMetadataSearcher>.Instance);
    }

    [Test]
    public void ProviderType_ReturnsTvmaze_WhenSearcherHandlesTelevision()
    {
        // Arrange

        // Act
        var providerType = _subject.ProviderType;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(providerType, Is.EqualTo("tvmaze"));
            Assert.That(_subject.SupportedContentTypes, Is.EquivalentTo(["tv"]));
        }
    }

    [Test]
    public async Task SearchAsync_TrimsTermAndReturnsMappedHits_WhenLevelIsShow()
    {
        // Arrange
        _client
            .Setup(client => client.SearchShowsAsync("game of thrones", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new SearchHitDto { Show = new ShowDto { Id = 82, Name = "Game of Thrones" } },
                new SearchHitDto { Show = null }
            ]);

        // Act
        var results = await _subject.SearchAsync(
            ShowLevel,
            "  game of thrones  ",
            defaultLanguagePreference: "en",
            page: 1,
            default).ConfigureAwait(false);

        // Assert
        Assert.That(results.Select(result => result.ExternalId), Is.EqualTo(["82"]));
        _client.Verify(
            client => client.SearchShowsAsync("game of thrones", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestCase("", 1, "show")]
    [TestCase("   ", 1, "show")]
    [TestCase("game of thrones", 2, "show")]
    [TestCase("game of thrones", 1, "episode")]
    public async Task SearchAsync_ReturnsEmptyWithoutAcquiringClient_WhenRequestIsOutOfScope(
        string searchTerm,
        int page,
        string levelSlug)
    {
        // Arrange
        var level = levelSlug == "show" ? ShowLevel : EpisodeLevel;

        // Act
        var results = await _subject.SearchAsync(
            level,
            searchTerm,
            defaultLanguagePreference: "en",
            page,
            default).ConfigureAwait(false);

        // Assert
        Assert.That(results, Is.Empty);
        _client.VerifyNoOtherCalls();
    }

    [Test]
    public async Task AdvancedSearchAsync_ReturnsNotSupported_WhenContentTypeIsNotTelevision()
    {
        // Arrange

        // Act
        var result = await _subject.AdvancedSearchAsync(
            "movie",
            new ContentHierarchyItemCriteria(),
            maxResults: 10,
            defaultLanguagePreference: "en",
            default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Supported, Is.False);
        _planner.Verify(
            planner => planner.Plan(It.IsAny<ContentHierarchyItemCriteria>(), It.IsAny<int>()),
            Times.Never);
        _executor.Verify(
            executor => executor.ExecuteAsync(It.IsAny<QueryPlan>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task AdvancedSearchAsync_ReturnsEmpty_WhenLevelIsNotShow()
    {
        // Arrange
        var searcher = new ContentMetadataSearcher(
            _client.Object,
            new SearchPlanner(NullLogger<SearchPlanner>.Instance),
            new SearchExecutor(_client.Object, NullLogger<SearchExecutor>.Instance),
            NullLogger<ContentMetadataSearcher>.Instance);
        var query = new ContentHierarchyItemCriteria
        {
            Level = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Value = "episode" }]
            }
        };

        // Act
        var result = await searcher.AdvancedSearchAsync("tv", query, 25, "en", default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Supported, Is.True);
            Assert.That(result.Matches, Is.Empty);
            Assert.That(result.Warnings, Is.Empty);
        }

        _client.VerifyNoOtherCalls();
    }

    [Test]
    public async Task AdvancedSearchAsync_DelegatesToThePlannedQuery_WhenLevelIsShow()
    {
        // Arrange
        var criteria = new MetadataCriteria();
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = criteria,
            MaximumResults = 25,
            Root = new FetchPlanNode
            {
                Id = 1,
                Contributes = "seed",
                AnchorKind = SearchAnchorKind.ExternalId,
                Seed = new SearchSeed.IdLookup(
                [
                    new SearchSeed.Lookup(Constants.ImdbSource, "tt0944947", null)
                ])
            },
            Dispositions = []
        };
        _planner
            .Setup(planner => planner.Plan(
                It.Is<ContentHierarchyItemCriteria>(query => query.Metadata.And.Single() == criteria),
                25))
            .Returns(plan);
        _executor
            .Setup(executor => executor.ExecuteAsync(plan, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MetadataSearchPage
            {
                Supported = true,
                Matches =
                [
                    new MetadataSearchCandidate
                    {
                        ProviderType = "tvmaze",
                        ExternalIdSource = "tvmaze",
                        ExternalId = "82",
                        ContentType = "tv",
                        Level = "show"
                    }
                ],
                PageInfo = new ProviderSearchPageInfo { RequestedPage = 1, HasMore = false }
            });

        // Act
        var result = await _subject.AdvancedSearchAsync(
            "tv",
            new ContentHierarchyItemCriteria
            {
                Level = new CriteriaSet<PropertyCriterion<string>>
                {
                    And = [new PropertyCriterion<string> { Value = "show" }]
                },
                Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
            },
            maxResults: 25,
            defaultLanguagePreference: "en",
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Supported, Is.True);
            Assert.That(result.Matches.Select(match => match.ExternalId), Is.EqualTo(["82"]));
        }

        _executor.Verify(executor => executor.ExecuteAsync(plan, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task AdvancedSearchAsync_ExecutesThePlannedQuery_WhenContentTypeIsTelevision()
    {
        // Arrange
        var query = new ContentHierarchyItemCriteria();
        var plan = new QueryPlan
        {
            Query = query,
            Metadata = new MetadataCriteria(),
            MaximumResults = 10,
            Root = new FetchPlanNode
            {
                Id = 1,
                Contributes = "seed",
                AnchorKind = SearchAnchorKind.NativeId,
                Seed = new SearchSeed.IdLookup([new SearchSeed.Lookup(Constants.ProviderType, "82", 82)])
            },
            Dispositions = []
        };
        _planner.Setup(planner => planner.Plan(query, 10)).Returns(plan);
        _executor
            .Setup(executor => executor.ExecuteAsync(plan, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MetadataSearchPage
            {
                Supported = true,
                Matches =
                [
                    new MetadataSearchCandidate
                    {
                        ProviderType = "tvmaze",
                        ExternalIdSource = "tvmaze",
                        ExternalId = "82",
                        ContentType = "tv",
                        Level = "show"
                    }
                ],
                PageInfo = new ProviderSearchPageInfo { RequestedPage = 1, HasMore = false }
            });

        // Act
        var result = await _subject.AdvancedSearchAsync(
            "tv",
            query,
            maxResults: 10,
            defaultLanguagePreference: "en",
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Supported, Is.True);
            Assert.That(result.Matches.Select(match => match.ExternalId), Is.EqualTo(["82"]));
        }

        _planner.Verify(planner => planner.Plan(query, 10), Times.Once);
        _executor.Verify(executor => executor.ExecuteAsync(plan, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task HydrateAsync_LoadsShowCredits_WhenScopeIsRelatedSubjects()
    {
        // Arrange
        _client
            .Setup(client => client.GetShowWithEmbedsAsync(82, new[] { "cast", "crew" }, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShowDto
            {
                Id = 82,
                Name = "Game of Thrones",
                Embedded = new ShowEmbeddedDto
                {
                    Cast =
                    [
                        new CastEntryDto
                        {
                            Person = new PersonDto { Id = 14072, Name = "Peter Dinklage" },
                            Character = new CharacterDto { Id = 1, Name = "Tyrion Lannister" }
                        }
                    ]
                }
            });
        var candidate = new MetadataSearchCandidate
        {
            ProviderType = "tvmaze",
            ExternalIdSource = "tvmaze",
            ExternalId = "82",
            ContentType = "tv",
            Level = "show"
        };

        // Act
        var hydrated = await _subject.HydrateAsync(
            candidate,
            defaultLanguagePreference: "en",
            default).ConfigureAwait(false);

        // Assert
        Assert.That(
            hydrated!.SubjectReferences,
            Has.Some.Matches<ProviderMetadataSearchSubjectReference>(reference =>
                reference.DisplayName == "Peter Dinklage" &&
                reference.SubjectKind == "person" &&
                reference.RelationshipKind == "cast"));
        _client.Verify(client => client.GetShowWithEmbedsAsync(
            82, new[] { "cast", "crew" }, It.IsAny<CancellationToken>()), Times.Once);
        _client.Verify(
            client => client.GetShowWithEmbedsAsync(
                It.IsAny<int>(), Array.Empty<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestCase("tmdb", "tvmaze", "tv", "82")]
    [TestCase("tvmaze", "imdb", "tv", "82")]
    [TestCase("tvmaze", "tvmaze", "movie", "82")]
    [TestCase("tvmaze", "tvmaze", "tv", "not-a-number")]
    [TestCase("tvmaze", "tvmaze", "tv", "0")]
    public async Task HydrateAsync_ReturnsNull_WhenCandidateIsNotAnAddressableTvmazeShow(
        string providerType,
        string externalIdSource,
        string contentType,
        string externalId)
    {
        // Arrange
        var candidate = new MetadataSearchCandidate
        {
            ProviderType = providerType,
            ExternalIdSource = externalIdSource,
            ExternalId = externalId,
            ContentType = contentType,
            Level = "show"
        };

        // Act
        var hydrated = await _subject.HydrateAsync(
            candidate,
            defaultLanguagePreference: "en",
            default).ConfigureAwait(false);

        // Assert
        Assert.That(hydrated, Is.Null);
        _client.VerifyNoOtherCalls();
    }
}
