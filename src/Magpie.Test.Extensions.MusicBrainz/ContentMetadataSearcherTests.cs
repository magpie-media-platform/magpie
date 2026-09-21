using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.MusicBrainz;
using Magpie.Extensions.MusicBrainz.Mapping;
using Magpie.Extensions.MusicBrainz.QueryEngine;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Models.MetadataSearch;
using Magpie.Models.Releases;
using Magpie.Sdk.Content;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Extensions.MusicBrainz;

[TestFixture]
public class ContentMetadataSearcherTests
{
    private static readonly ContentHierarchyLevelDefinition ArtistLevel = new()
    {
        Slug = "artist",
        DisplayName = "Creator",
        CustomMetadataFields = []
    };

    private static readonly ProviderSearchPageInfo NotExhausted =
        new() { RequestedPage = 1, HasMore = false };

    private Mock<ISettingsResolver> _settingsResolver = null!;
    private Mock<IQueryPlanner> _queryPlanner = null!;
    private Mock<IQueryExecutor> _queryExecutor = null!;
    private Mock<IQueryCoordinator> _cache = null!;
    private Mock<IMusicBrainzClient> _client = null!;
    private Mock<ICoverArtArchiveClient> _coverClient = null!;
    private IContentHierarchyMetadataAdvancedSearcher _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _settingsResolver = new Mock<ISettingsResolver>();
        _settingsResolver.Setup(resolver => resolver.ResolveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Settings());
        _queryPlanner = new Mock<IQueryPlanner>();
        _queryPlanner.Setup(planner => planner.Plan(It.IsAny<QueryRequest>()))
            .Returns((QueryRequest request) => new QueryPlan
            {
                RequestedPage = request.RequestedPage,
                MaxResults = request.MaxResults,
                Settings = request.Settings,
                Warnings = [],
                Fingerprint = request.Target.ToString()
            });
        _queryExecutor = new Mock<IQueryExecutor>();
        _cache = new Mock<IQueryCoordinator>();
        _cache.Setup(cache => cache.GetCoverAsync(
                It.IsAny<CoverCacheKey>(),
                It.IsAny<Func<CancellationToken, Task<FetchResult<Uri>>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<Uri> { Outcome = FetchOutcome.NotFound });
        _client = new Mock<IMusicBrainzClient>();
        _coverClient = new Mock<ICoverArtArchiveClient>();
        _subject = new ContentMetadataSearcher(
            _settingsResolver.Object,
            _queryPlanner.Object,
            _queryExecutor.Object,
            _cache.Object,
            _client.Object,
            _coverClient.Object,
            NullLogger<ContentMetadataSearcher>.Instance);
    }

    [TestCase("music", true)]
    [TestCase("movie", false)]
    public void CanProvideFacts_ReturnsExpected_WhenContentTypeIsGiven(string contentType, bool expected)
    {
        // Arrange
        var item = new ContentHierarchyItem
        {
            ContentType = contentType,
            Level = "artist",
            Slug = "artist",
            Name = "Creator",
        };
        var target = new ContentTarget
        {
            Item = item,
            Lineage = [item],
            Metadata = new Dictionary<Guid, MaterializedMetadata>(),
        };
        IContentFactProvider subject = new ContentFactProvider(Mock.Of<IMetadataService>());

        // Act
        var result = subject.CanProvideFacts(target);

        // Assert
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public async Task AdvancedSearchAsync_QueriesAllThreeTargets_WhenNoIdentityCriteriaNarrowRouting()
    {
        // Arrange
        _queryExecutor.Setup(executor => executor.ExecuteAsync(It.IsAny<QueryPlan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryResult { Supported = true, Hits = [], PageInfo = NotExhausted });

        // Act
        await _subject.AdvancedSearchAsync(
            "music",
            new ContentHierarchyItemCriteria(),
            25,
            "en",
            default).ConfigureAwait(false);

        // Assert
        _queryPlanner.Verify(
            planner => planner.Plan(It.Is<QueryRequest>(request => request.Target == QueryTarget.Artist)),
            Times.Once);
        _queryPlanner.Verify(
            planner => planner.Plan(It.Is<QueryRequest>(request => request.Target == QueryTarget.Album)),
            Times.Once);
        _queryPlanner.Verify(
            planner => planner.Plan(It.Is<QueryRequest>(request => request.Target == QueryTarget.Track)),
            Times.Once);
    }

    [TestCase(Constants.IsniSource)]
    [TestCase(Constants.IpiSource)]
    public async Task AdvancedSearchAsync_QueriesOnlyArtistTarget_WhenExternalIdSourceIsArtistAuthority(string source)
    {
        // Arrange
        _queryExecutor.Setup(executor => executor.ExecuteAsync(It.IsAny<QueryPlan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryResult { Supported = true, Hits = [], PageInfo = NotExhausted });
        var criteria = new MetadataCriteria
        {
            ExternalIds = new CollectionCriteria<ExternalIdCriteria>
            {
                Some =
                [
                    [new ExternalIdCriteria
                    {
                        Source = new CriteriaSet<PropertyCriterion<string>>
                        { And = [new PropertyCriterion<string> { Values = [source] }] },
                        Value = new CriteriaSet<PropertyCriterion<string>>
                        { And = [new PropertyCriterion<string> { Values = ["0000000123486830"] }] }
                    }]
                ]
            }
        };

        // Act
        await _subject.AdvancedSearchAsync(
            "music",
            new ContentHierarchyItemCriteria { Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] } },
            25,
            "en",
            default).ConfigureAwait(false);

        // Assert
        _queryPlanner.Verify(
            planner => planner.Plan(It.IsAny<QueryRequest>()),
            Times.Once);
        _queryPlanner.Verify(
            planner => planner.Plan(It.Is<QueryRequest>(request => request.Target == QueryTarget.Artist)),
            Times.Once);
    }

    [TestCase(Constants.IsniSource, (int)QueryTarget.Artist)]
    [TestCase(Constants.IfpiSource, (int)QueryTarget.Album)]
    [TestCase(Constants.IsrcSource, (int)QueryTarget.Track)]
    public async Task AdvancedSearchAsync_QueriesOnlyMatchingTarget_WhenExternalIdSourceIsEmbeddedInTheValue(
        string source,
        int expectedTargetValue)
    {
        // Arrange
        var expectedTarget = (QueryTarget)expectedTargetValue;
        _queryExecutor.Setup(executor => executor.ExecuteAsync(It.IsAny<QueryPlan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryResult { Supported = true, Hits = [], PageInfo = NotExhausted });
        var criteria = new MetadataCriteria
        {
            ExternalIds = new CollectionCriteria<ExternalIdCriteria>
            {
                Some =
                [
                    [new ExternalIdCriteria
                    {
                        Value = new CriteriaSet<PropertyCriterion<string>>
                        { And = [new PropertyCriterion<string> { Values = [source + ":some-value"] }] }
                    }]
                ]
            }
        };

        // Act
        await _subject.AdvancedSearchAsync(
            "music",
            new ContentHierarchyItemCriteria { Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] } },
            25,
            "en",
            default).ConfigureAwait(false);

        // Assert
        _queryPlanner.Verify(
            planner => planner.Plan(It.IsAny<QueryRequest>()),
            Times.Once);
        _queryPlanner.Verify(
            planner => planner.Plan(It.Is<QueryRequest>(request => request.Target == expectedTarget)),
            Times.Once);
    }

    [Test]
    public async Task AdvancedSearchAsync_QueriesOnlyAlbumTarget_WhenExternalIdSourceIsIfpi()
    {
        // Arrange
        _queryExecutor.Setup(executor => executor.ExecuteAsync(It.IsAny<QueryPlan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryResult { Supported = true, Hits = [], PageInfo = NotExhausted });
        var criteria = new MetadataCriteria
        {
            ExternalIds = new CollectionCriteria<ExternalIdCriteria>
            {
                Some =
                [
                    [new ExternalIdCriteria
                    {
                        Source = new CriteriaSet<PropertyCriterion<string>>
                        { And = [new PropertyCriterion<string> { Values = [Constants.IfpiSource] }] },
                        Value = new CriteriaSet<PropertyCriterion<string>>
                        { And = [new PropertyCriterion<string> { Values = ["1234567890"] }] }
                    }]
                ]
            }
        };

        // Act
        await _subject.AdvancedSearchAsync(
            "music",
            new ContentHierarchyItemCriteria { Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] } },
            25,
            "en",
            default).ConfigureAwait(false);

        // Assert
        _queryPlanner.Verify(
            planner => planner.Plan(It.Is<QueryRequest>(request => request.Target == QueryTarget.Album)),
            Times.Once);
    }

    [Test]
    public async Task AdvancedSearchAsync_ReturnsValidEmptyWithoutWarning_WhenExternalIdSourceMapsToNoTarget()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            ExternalIds = new CollectionCriteria<ExternalIdCriteria>
            {
                Some =
                [
                    [new ExternalIdCriteria
                    {
                        Source = new CriteriaSet<PropertyCriterion<string>>
                        { And = [new PropertyCriterion<string> { Values = ["imdb"] }] },
                        Value = new CriteriaSet<PropertyCriterion<string>>
                        { And = [new PropertyCriterion<string> { Values = ["nm0000001"] }] }
                    }]
                ]
            }
        };

        // Act
        var result = await _subject.AdvancedSearchAsync(
            "music",
            new ContentHierarchyItemCriteria { Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] } },
            25,
            "en",
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Supported, Is.True);
            Assert.That(result.Matches, Is.Empty);
            Assert.That(result.PageInfo.HasMore, Is.False);
            Assert.That(result.Warnings, Is.Empty);
        }

        _queryPlanner.Verify(
            planner => planner.Plan(It.IsAny<QueryRequest>()),
            Times.Never);
    }

    [Test]
    public async Task AdvancedSearchAsync_ReturnsValidEmptyWithoutWarning_WhenThePlanRefusesACriterion()
    {
        // Arrange
        _queryPlanner.Setup(planner => planner.Plan(It.IsAny<QueryRequest>()))
            .Returns(new QueryPlan
            {
                RequestedPage = 1,
                MaxResults = 25,
                Settings = new Settings(),
                Warnings = [],
                Fingerprint = "fp-refused",
                TraceProvenance = new QueryTraceProvenance
                {
                    Criteria =
                    [
                        new QueryTraceProvenance.Criterion
                        {
                            Source = "Metadata.OriginalTitle.And[0]",
                            FinalDisposition = QueryTraceProvenance.Disposition.Refused
                        }
                    ]
                }
            });

        // Act
        var result = await _subject.AdvancedSearchAsync(
            "music",
            new ContentHierarchyItemCriteria(),
            25,
            "en",
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Supported, Is.True);
            Assert.That(result.Matches, Is.Empty);
            Assert.That(result.Warnings, Is.Empty);
        }

        _queryExecutor.VerifyNoOtherCalls();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task AdvancedSearchAsync_ReturnsValidEmpty_WhenContentTypeCriterionCannotBeEvaluated(bool negate)
    {
        // Arrange
        var query = new ContentHierarchyItemCriteria
        {
            ContentType = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.GreaterThan,
                        Negate = negate,
                        Values = ["music"]
                    }
                ]
            }
        };

        // Act
        var result = await _subject.AdvancedSearchAsync("music", query, 25, "en", default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Supported, Is.True);
            Assert.That(result.Matches, Is.Empty);
            Assert.That(result.Warnings, Is.Empty);
        }

        _settingsResolver.VerifyNoOtherCalls();
        _queryPlanner.VerifyNoOtherCalls();
    }

    [Test]
    public async Task AdvancedSearchAsync_ReturnsValidEmpty_WhenLevelDoesNotMatchAnySupportedTarget()
    {
        // Arrange
        var query = new ContentHierarchyItemCriteria
        {
            Level = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["movement"] }]
            }
        };

        // Act
        var result = await _subject.AdvancedSearchAsync("music", query, 25, "en", default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Supported, Is.True);
            Assert.That(result.Matches, Is.Empty);
            Assert.That(result.Warnings, Is.Empty);
        }

        _settingsResolver.VerifyNoOtherCalls();
        _queryPlanner.VerifyNoOtherCalls();
    }

    [Test]
    public async Task AdvancedSearchAsync_StopsRoutingAdditionalTargets_WhenMaxResultsIsAlreadyReached()
    {
        // Arrange
        _queryExecutor.Setup(executor => executor.ExecuteAsync(
                It.Is<QueryPlan>(plan => plan.Fingerprint == "Artist"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryResult
            {
                Supported = true,
                Hits = [new SearchHit { Target = QueryTarget.Artist, PrimaryMbid = "artist-1", Title = "Home" }],
                PageInfo = NotExhausted
            });
        _queryExecutor.Setup(executor => executor.ExecuteAsync(
                It.Is<QueryPlan>(plan => plan.Fingerprint == "Album"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryResult
            {
                Supported = true,
                Hits = [new SearchHit { Target = QueryTarget.Album, PrimaryMbid = "album-1", Title = "Home" }],
                PageInfo = NotExhausted
            });

        // Act
        var result = await _subject.AdvancedSearchAsync(
            "music",
            new ContentHierarchyItemCriteria
            {
                Metadata = new CriteriaSet<MetadataCriteria>
                {
                    And = [new MetadataCriteria { Title = new() { And = [new() { Values = ["Home"] }] } }]
                }
            },
            2,
            "en",
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Matches.Select(match => match.ExternalId), Is.EqualTo(["artist-1", "album-1"]));
            Assert.That(result.PageInfo.HasMore, Is.True);
        }

        _queryPlanner.Verify(
            planner => planner.Plan(It.Is<QueryRequest>(request => request.Target == QueryTarget.Track)),
            Times.Never);
    }

    [Test]
    public async Task AdvancedSearchAsync_DedupesHitsAndAggregatesWarnings_WhenMultipleTargetsMatch()
    {
        // Arrange
        var shared = new SearchHit { Target = QueryTarget.Artist, PrimaryMbid = "shared-artist", Title = "Home" };
        var warning = "musicbrainz is unavailable.";
        _queryExecutor.SetupSequence(
                executor => executor.ExecuteAsync(It.IsAny<QueryPlan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryResult
            {
                Supported = true,
                Hits = [shared],
                PageInfo = NotExhausted,
                Warnings = [warning]
            })
            .ReturnsAsync(new QueryResult
            {
                Supported = false,
                Hits = [shared],
                PageInfo = NotExhausted,
                Warnings = [warning]
            })
            .ReturnsAsync(new QueryResult { Supported = true, Hits = [], PageInfo = NotExhausted });

        // Act
        var result = await _subject.AdvancedSearchAsync(
            "music",
            new ContentHierarchyItemCriteria(),
            25,
            "en",
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Matches.Select(match => match.ExternalId), Is.EqualTo(["shared-artist"]));
            Assert.That(result.Supported, Is.True);
            Assert.That(result.Warnings, Is.EqualTo([warning]));
        }
    }

    [Test]
    public async Task AdvancedSearchAsync_MapsHitToCandidate_WhenSingleLevelTarget()
    {
        // Arrange
        var hit = new SearchHit { Target = QueryTarget.Album, PrimaryMbid = "album-1", Title = "OK Computer" };
        var warning = "a residual warning";
        _queryExecutor.Setup(executor => executor.ExecuteAsync(It.IsAny<QueryPlan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryResult
            {
                Supported = true,
                Hits = [hit],
                PageInfo = new ProviderSearchPageInfo { RequestedPage = 1, HasMore = true },
                Warnings = [warning]
            });

        // Act
        var result = await _subject.AdvancedSearchAsync(
            "music",
            new ContentHierarchyItemCriteria
            {
                Level = new CriteriaSet<PropertyCriterion<string>>
                {
                    And = [new PropertyCriterion<string> { Values = ["album"] }]
                }
            },
            25,
            "en",
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Supported, Is.True);
            Assert.That(result.Matches.Single().Title, Is.EqualTo("OK Computer"));
            Assert.That(result.Matches.Single().Level, Is.EqualTo("album"));
            Assert.That(result.PageInfo.HasMore, Is.True);
            Assert.That(result.Warnings, Is.EqualTo([warning]));
        }

        _queryPlanner.Verify(
            planner => planner.Plan(It.Is<QueryRequest>(request =>
                request.Target == QueryTarget.Album && request.Criteria != null)),
            Times.Once);
    }

    [Test]
    public async Task AdvancedSearchAsync_ReturnsNotSupported_WhenContentTypeDoesNotMatchHierarchy()
    {
        // Arrange
        // Act
        var result = await _subject.AdvancedSearchAsync(
            "movies",
            new ContentHierarchyItemCriteria(),
            25,
            "en",
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Supported, Is.False);
            Assert.That(result.Matches, Is.Empty);
        }

        _queryPlanner.Verify(
            planner => planner.Plan(It.IsAny<QueryRequest>()),
            Times.Never);
    }

    [Test]
    public async Task SearchAsync_MapsHitsToSearchResults_WhenResolvedLevel()
    {
        // Arrange
        var hit = new SearchHit { Target = QueryTarget.Track, PrimaryMbid = "track-1", Title = "Bohemian Rhapsody" };
        _queryExecutor.Setup(executor => executor.ExecuteAsync(It.IsAny<QueryPlan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryResult { Supported = true, Hits = [hit], PageInfo = NotExhausted });
        var trackLevel = new ContentHierarchyLevelDefinition
        { Slug = "track", ParentSlug = "album", DisplayName = "Piece", CustomMetadataFields = [] };

        // Act
        var results = (await _subject.SearchAsync(trackLevel, "Bohemian Rhapsody", "en", 2, default)
            .ConfigureAwait(false)).ToArray();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Single().Title, Is.EqualTo("Bohemian Rhapsody"));
            Assert.That(results.Single().Level, Is.EqualTo("track"));
        }

        _queryPlanner.Verify(
            planner => planner.Plan(It.Is<QueryRequest>(request => request.Target == QueryTarget.Track &&
                    request.IsOrdinary &&
                    request.OrdinaryTerm == "Bohemian Rhapsody" &&
                    request.RequestedPage == 2)),
            Times.Once);
    }

    [Test]
    public async Task SearchAsync_ReturnsEmpty_WhenSearchTermIsBlank()
    {
        // Arrange
        // Act
        var results = await _subject.SearchAsync(ArtistLevel, "   ", "en", 1, default).ConfigureAwait(false);

        // Assert
        Assert.That(results, Is.Empty);
        _queryPlanner.Verify(
            planner => planner.Plan(It.IsAny<QueryRequest>()),
            Times.Never);
    }

    [Test]
    public async Task HydrateAsync_BuildsCanonicalCacheKey_WhenCandidateExternalIdIsUppercase()
    {
        // Arrange
        const string mbid = "0383dadf-2a4e-4d10-a46a-e9e041da8eb3";
        _cache.Setup(cache => cache.GetEntityAsync(
                It.IsAny<EntityCacheKey>(),
                It.IsAny<Func<CancellationToken, Task<FetchResult<EntityResponseDto>>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<EntityResponseDto>
            {
                Outcome = FetchOutcome.Success,
                Value = new EntityResponseDto { Artist = new ArtistDto { Id = mbid, Name = "Queen" } }
            });
        var candidate = new MetadataSearchCandidate
        {
            ProviderType = Constants.ProviderType,
            ExternalIdSource = Constants.ProviderType,
            ExternalId = mbid.ToUpperInvariant(),
            ContentType = "music",
            Level = "artist",
            Title = "Queen"
        };

        // Act
        var hydrated = await _subject.HydrateAsync(candidate, "en", default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(hydrated, Is.Not.Null);
            Assert.That(hydrated!.Title, Is.EqualTo("Queen"));
        }

        _cache.Verify(
            cache => cache.GetEntityAsync(
                It.Is<EntityCacheKey>(key => key.Kind == EntityKind.Artist && key.Mbid == mbid),
                It.IsAny<Func<CancellationToken, Task<FetchResult<EntityResponseDto>>>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestCase("movies", "musicbrainz", "artist")]
    [TestCase("music", "tmdb", "artist")]
    [TestCase("music", "musicbrainz", "unmapped")]
    public async Task HydrateAsync_ReturnsNull_WhenCandidateDoesNotMatchThisProvider(
        string contentType,
        string providerType,
        string level)
    {
        // Arrange
        var candidate = new MetadataSearchCandidate
        {
            ProviderType = providerType,
            ExternalIdSource = Constants.ProviderType,
            ExternalId = "0383dadf-2a4e-4d10-a46a-e9e041da8eb3",
            ContentType = contentType,
            Level = level,
            Title = "Queen"
        };

        // Act
        var hydrated = await _subject.HydrateAsync(candidate, "en", default)
            .ConfigureAwait(false);

        // Assert
        Assert.That(hydrated, Is.Null);
    }

    [Test]
    public async Task HydrateAsync_MergesRepresentativeLabels_WhenCandidateIsAnAlbum()
    {
        // Arrange
        const string groupMbid = "550e8400-e29b-41d4-a716-446655440000";
        _cache.Setup(cache => cache.GetEntityAsync(
                It.IsAny<EntityCacheKey>(),
                It.IsAny<Func<CancellationToken, Task<FetchResult<EntityResponseDto>>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<EntityResponseDto>
            {
                Outcome = FetchOutcome.Success,
                Value = new EntityResponseDto
                { ReleaseGroup = new ReleaseGroupDto { Id = groupMbid, Title = "Blue Train" } }
            });
        _queryExecutor.Setup(executor => executor.GetRepresentativeReleaseLabelsAsync(
                It.IsAny<SearchHit>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<IReadOnlyList<LabelReference>>
            {
                Outcome = FetchOutcome.Success,
                Value = [new LabelReference { Mbid = "44444444-4444-4444-8444-444444444444", Name = "Blue Note" }]
            });
        var candidate = new MetadataSearchCandidate
        {
            ProviderType = Constants.ProviderType,
            ExternalIdSource = Constants.ProviderType,
            ExternalId = groupMbid,
            ContentType = "music",
            Level = "album",
            Title = "Blue Train"
        };

        // Act
        var hydrated = await _subject.HydrateAsync(
            candidate,
            "en",
            default).ConfigureAwait(false);

        // Assert
        var label = hydrated!.SubjectReferences.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(label.DisplayName, Is.EqualTo("Blue Note"));
            Assert.That(label.SubjectKind, Is.EqualTo("music-label"));
            Assert.That(
                label.ExternalIds[Constants.ProviderType],
                Is.EqualTo("44444444-4444-4444-8444-444444444444"));
        }
    }

    [Test]
    public async Task HydrateAsync_SetsThumbnail_WhenCandidateIsAnAlbum()
    {
        // Arrange
        const string groupMbid = "550e8400-e29b-41d4-a716-446655440000";
        var thumbnailUrl = new Uri("https://coverartarchive.org/release-group/" + groupMbid + "/front-500");
        _cache.Setup(cache => cache.GetEntityAsync(
                It.IsAny<EntityCacheKey>(),
                It.IsAny<Func<CancellationToken, Task<FetchResult<EntityResponseDto>>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<EntityResponseDto>
            {
                Outcome = FetchOutcome.Success,
                Value = new EntityResponseDto
                { ReleaseGroup = new ReleaseGroupDto { Id = groupMbid, Title = "Blue Train" } }
            });
        _cache.Setup(cache => cache.GetCoverAsync(
                It.IsAny<CoverCacheKey>(),
                It.IsAny<Func<CancellationToken, Task<FetchResult<Uri>>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<Uri> { Outcome = FetchOutcome.Success, Value = thumbnailUrl });
        var candidate = new MetadataSearchCandidate
        {
            ProviderType = Constants.ProviderType,
            ExternalIdSource = Constants.ProviderType,
            ExternalId = groupMbid,
            ContentType = "music",
            Level = "album",
            Title = "Blue Train"
        };

        // Act
        var hydrated = await _subject.HydrateAsync(
            candidate,
            "en",
            default).ConfigureAwait(false);

        // Assert
        Assert.That(hydrated!.ThumbnailUrl, Is.EqualTo(thumbnailUrl));
    }

    [Test]
    public async Task HydrateAsync_ReturnsNull_WhenEntityLookupFails()
    {
        // Arrange
        const string mbid = "0383dadf-2a4e-4d10-a46a-e9e041da8eb3";
        _cache.Setup(cache => cache.GetEntityAsync(
                It.IsAny<EntityCacheKey>(),
                It.IsAny<Func<CancellationToken, Task<FetchResult<EntityResponseDto>>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<EntityResponseDto> { Outcome = FetchOutcome.NotFound });
        var candidate = new MetadataSearchCandidate
        {
            ProviderType = Constants.ProviderType,
            ExternalIdSource = Constants.ProviderType,
            ExternalId = mbid,
            ContentType = "music",
            Level = "artist",
            Title = "Queen"
        };

        // Act
        var hydrated = await _subject.HydrateAsync(candidate, "en", default)
            .ConfigureAwait(false);

        // Assert
        Assert.That(hydrated, Is.Null);
    }

    [Test]
    public async Task GetFactsAsync_ReturnsAlbumFacts_WhenLiteralMusicTarget()
    {
        // Arrange
        var albumId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var artistContainerId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var relationships = new List<Relationship>
        {
            new()
            {
                Kind = "artist",
                Ordinal = 0,
                RelatedMetadataContainerId = artistContainerId
            }
        };
        var target = new ContentTarget
        {
            Item = new ContentHierarchyItem
            {
                Id = albumId,
                ContentType = "music",
                Level = "album",
                Slug = "unlicensed-forklift-ballet",
                Name = "Unlicensed Forklift Ballet",
                MetadataContainerId = albumId
            },
            Lineage =
            [
                new ContentHierarchyItem
                {
                    Id = albumId,
                    ContentType = "music",
                    Level = "album",
                    Slug = "unlicensed-forklift-ballet",
                    Name = "Unlicensed Forklift Ballet",
                    MetadataContainerId = albumId
                },
                new ContentHierarchyItem
                {
                    Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    ContentType = "music",
                    Level = "artist",
                    Slug = "court-ordered-smoothie",
                    Name = "Court Ordered Smoothie",
                    MetadataContainerId = Guid.Parse("11111111-1111-1111-1111-111111111111")
                }
            ],
            Metadata = new Dictionary<Guid, MaterializedMetadata>
            {
                [albumId] = new()
                {
                    Title = "Unlicensed Forklift Ballet",
                    OriginalTitle = "Forklift Ballet",
                    Relationships = relationships
                }
            }
        };
        var metadataService = new Mock<IMetadataService>();
        metadataService
            .Setup(service => service.GetRelatedMetadataAsync(
                It.IsAny<Guid>(),
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new MaterializedMetadata
                {
                    Title = "Court Ordered Smoothie",
                    OriginalTitle = "Judicial Smoothie",
                },
            ]);

        IContentFactProvider subject = new ContentFactProvider(metadataService.Object);

        // Act
        var factSets = await subject.GetFactsAsync(target).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(factSets, Has.Count.EqualTo(4));
            Assert.That(factSets[0], Has.Count.EqualTo(2));
            Assert.That(factSets[0][0].Value, Is.EqualTo("Court Ordered Smoothie"));
            Assert.That(factSets[0][1].Value, Is.EqualTo("Unlicensed Forklift Ballet"));
            Assert.That(factSets[1][0].Value, Is.EqualTo("Court Ordered Smoothie"));
            Assert.That(factSets[1][1].Value, Is.EqualTo("Forklift Ballet"));
            Assert.That(factSets[2][0].Value, Is.EqualTo("Judicial Smoothie"));
            Assert.That(factSets[2][1].Value, Is.EqualTo("Unlicensed Forklift Ballet"));
            Assert.That(factSets[3][0].Value, Is.EqualTo("Judicial Smoothie"));
            Assert.That(factSets[3][1].Value, Is.EqualTo("Forklift Ballet"));
        }
    }

    [Test]
    public async Task GetFactsAsync_ReturnsTrackFacts_WhenLiteralMusicTarget()
    {
        // Arrange
        var artistId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var albumId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var trackId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var artistContainerId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var relationships = new List<Relationship>
        {
            new()
            {
                Kind = "artist",
                Ordinal = 0,
                RelatedMetadataContainerId = artistContainerId
            }
        };
        var target = new ContentTarget
        {
            Item = new ContentHierarchyItem
            {
                Id = trackId,
                ContentType = "music",
                Level = "track",
                Slug = "dads-emergency-contact",
                Name = "Dad's Emergency Contact",
                MetadataContainerId = trackId
            },
            Lineage =
            [
                new ContentHierarchyItem
                {
                    Id = trackId,
                    ContentType = "music",
                    Level = "track",
                    Slug = "dads-emergency-contact",
                    Name = "Dad's Emergency Contact",
                    MetadataContainerId = trackId
                },
                new ContentHierarchyItem
                {
                    Id = albumId,
                    ContentType = "music",
                    Level = "album",
                    Slug = "unlicensed-forklift-ballet",
                    Name = "Unlicensed Forklift Ballet",
                    MetadataContainerId = albumId
                },
                new ContentHierarchyItem
                {
                    Id = artistId,
                    ContentType = "music",
                    Level = "artist",
                    Slug = "court-ordered-smoothie",
                    Name = "Court Ordered Smoothie",
                    MetadataContainerId = artistId
                }
            ],
            Metadata = new Dictionary<Guid, MaterializedMetadata>
            {
                [albumId] = new() { Title = "Unlicensed Forklift Ballet" },
                [trackId] = new()
                {
                    Title = "Dad's Emergency Contact",
                    OriginalTitle = "Emergency Contact",
                    Relationships = relationships
                }
            }
        };
        var metadataService = new Mock<IMetadataService>();
        metadataService
            .Setup(service => service.GetRelatedMetadataAsync(
                It.IsAny<Guid>(),
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new MaterializedMetadata { Title = "Court Ordered Smoothie" }]);

        IContentFactProvider subject = new ContentFactProvider(metadataService.Object);

        // Act
        var factSets = await subject.GetFactsAsync(target).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(factSets, Has.Count.EqualTo(4));
            Assert.That(factSets[0], Has.Count.EqualTo(3));
            Assert.That(factSets[0][0].Value, Is.EqualTo("Court Ordered Smoothie"));
            Assert.That(factSets[0][1].Value, Is.EqualTo("Unlicensed Forklift Ballet"));
            Assert.That(factSets[0][2].Value, Is.EqualTo("Dad's Emergency Contact"));
            Assert.That(factSets[1], Has.Count.EqualTo(2));
            Assert.That(factSets[1][0].Value, Is.EqualTo("Court Ordered Smoothie"));
            Assert.That(factSets[1][1].Value, Is.EqualTo("Dad's Emergency Contact"));
            Assert.That(factSets[2], Has.Count.EqualTo(3));
            Assert.That(factSets[2][0].Value, Is.EqualTo("Court Ordered Smoothie"));
            Assert.That(factSets[2][1].Value, Is.EqualTo("Unlicensed Forklift Ballet"));
            Assert.That(factSets[2][2].Value, Is.EqualTo("Emergency Contact"));
            Assert.That(factSets[3], Has.Count.EqualTo(2));
            Assert.That(factSets[3][0].Value, Is.EqualTo("Court Ordered Smoothie"));
            Assert.That(factSets[3][1].Value, Is.EqualTo("Emergency Contact"));
        }
    }

    [Test]
    public async Task GetFactsAsync_ReturnsEmpty_WhenArtistTarget()
    {
        // Arrange
        var artistId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var artist = new ContentHierarchyItem
        {
            Id = artistId,
            ContentType = "music",
            Level = "artist",
            Slug = "court-ordered-smoothie",
            Name = "Court Ordered Smoothie",
            MetadataContainerId = artistId
        };
        var target = new ContentTarget
        {
            Item = artist,
            Lineage = [artist],
            Metadata = new Dictionary<Guid, MaterializedMetadata>()
        };
        var metadataService = new Mock<IMetadataService>();
        metadataService
            .Setup(service => service.GetRelatedMetadataAsync(
                It.IsAny<Guid>(),
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        IContentFactProvider subject = new ContentFactProvider(metadataService.Object);

        // Act
        var factSets = await subject.GetFactsAsync(target).ConfigureAwait(false);

        // Assert
        Assert.That(factSets, Is.Empty);
    }
}
