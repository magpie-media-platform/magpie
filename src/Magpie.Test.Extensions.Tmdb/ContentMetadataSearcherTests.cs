using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Tmdb;
using Magpie.Extensions.Tmdb.Mapping;
using Magpie.Extensions.Tmdb.QueryEngine;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Models.MetadataSearch;
using Magpie.Sdk.Content;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Tmdb;

[TestFixture]
public class ContentMetadataSearcherTests
{
    private Mock<IQueryEngine> _engine = null!;
    private Mock<ITmdbClient> _client = null!;
    private Mock<IHydrationCache> _hydrationCache = null!;
    private IContentHierarchyMetadataAdvancedSearcher _searcher = null!;

    [SetUp]
    public void SetUp()
    {
        _engine = new Mock<IQueryEngine>();
        _client = new Mock<ITmdbClient>();
        _hydrationCache = new Mock<IHydrationCache>();
        var settingsResolver = new Mock<ISettingsResolver>();
        settingsResolver.Setup(resolver => resolver.AcquireContextAsync(
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RequestContext { ApiKey = "key", Language = "en-US" });
        var queryCache = new Mock<IQueryCache>();
        queryCache.Setup(cache => cache.GetImageBaseUrlAsync(
                It.IsAny<RequestContext>(),
                It.IsAny<ITmdbClient>(),
                It.IsAny<ILogger>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://image.tmdb.org/t/p/");
        _searcher = new ContentMetadataSearcher(
            _engine.Object,
            settingsResolver.Object,
            _client.Object,
            _hydrationCache.Object,
            queryCache.Object,
            NullLogger<ContentMetadataSearcher>.Instance);
    }

    [TestCase("")]
    [TestCase("   ")]
    public async Task SearchAsync_ReturnsEmptyWithoutQuerying_WhenSearchTermIsBlank(string searchTerm)
    {
        // Arrange
        var level = new ContentHierarchyLevelDefinition
        {
            Slug = "movie",
            ParentSlug = null,
            DisplayName = "Movie",
            CustomMetadataFields = []
        };

        // Act
        var results = await _searcher.SearchAsync(level, searchTerm, "en-US", 1, default)
            .ConfigureAwait(false);

        // Assert
        Assert.That(results, Is.Empty);
        _engine.VerifyNoOtherCalls();
    }

    [Test]
    public async Task SearchAsync_ReturnsEmptyWithoutQuerying_WhenLevelSlugIsNotSearchable()
    {
        // Arrange
        var level = new ContentHierarchyLevelDefinition
        {
            Slug = "season",
            ParentSlug = "show",
            DisplayName = "Season",
            CustomMetadataFields = []
        };

        // Act
        var results = await _searcher.SearchAsync(level, "Iron Man", "en-US", 1, default)
            .ConfigureAwait(false);

        // Assert
        Assert.That(results, Is.Empty);
        _engine.VerifyNoOtherCalls();
    }

    [Test]
    public async Task SearchAsync_TrimsTermAndProjectsOrdinaryHits_WhenLevelSlugIsSearchable()
    {
        // Arrange
        var level = new ContentHierarchyLevelDefinition
        {
            Slug = "movie",
            ParentSlug = null,
            DisplayName = "Movie",
            CustomMetadataFields = []
        };
        List<SearchHit> hits =
        [
            new()
            {
                Route = ContentRoute.Movie,
                Id = 1726,
                Title = "Iron Man",
                ReleaseDate = new DateTime(2008, 5, 2, 0, 0, 0, DateTimeKind.Utc)
            }
        ];
        _engine.Setup(engine => engine.SearchAsync(It.IsAny<QueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new QueryExecution
            {
                Supported = true,
                Hits = hits.ToAsyncEnumerable(),
                Warnings = [],
                RequestedPage = 1,
                PagingMode = QueryPagingMode.RequestedSourcePage,
                SourceHasMore = false
            });

        // Act
        var results = (await _searcher.SearchAsync(level, " Iron Man ", "en-US", 1, default)
            .ConfigureAwait(false)).ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].ProviderType, Is.EqualTo("tmdb"));
            Assert.That(results[0].ExternalIdSource, Is.EqualTo("tmdb"));
            Assert.That(results[0].ExternalId, Is.EqualTo("1726"));
            Assert.That(results[0].Title, Is.EqualTo("Iron Man"));
            _engine.Verify(
                engine => engine.SearchAsync(
                    It.Is<QueryRequest>(request =>
                        request.Route == ContentRoute.Movie &&
                        request.OrdinarySearchTerm == "Iron Man" &&
                        request.Page == 1),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task AdvancedSearchAsync_ReturnsNotSupported_WhenContentTypeIsOutsideMovieAndTv()
    {
        // Arrange

        // Act
        var result = await _searcher.AdvancedSearchAsync(
            "book",
            new ContentHierarchyItemCriteria(),
            25,
            "en-US",
            default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Supported, Is.False);
        _engine.VerifyNoOtherCalls();
    }

    [Test]
    public async Task AdvancedSearchAsync_WarnsWithoutQuerying_WhenTvLevelSelectsOnlySeasons()
    {
        // Arrange
        var query = new ContentHierarchyItemCriteria
        {
            Level = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["season"] }]
            }
        };

        // Act
        var result = await _searcher.AdvancedSearchAsync("tv", query, 25, "en-US", default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Matches, Is.Empty);
            Assert.That(
                result.Warnings.Select(warning => warning),
                Has.Some.Contains("cannot search individual TV seasons or episodes"));
        }

        _engine.VerifyNoOtherCalls();
    }

    [Test]
    public async Task AdvancedSearchAsync_WarnsAndSearchesShows_WhenTvLevelIncludesSeasonsAndShows()
    {
        // Arrange
        List<SearchHit> hits = [new() { Route = ContentRoute.TvShow, Id = 1399, Title = "Game of Thrones" }];
        _engine.Setup(engine => engine.SearchAsync(
                It.IsAny<QueryRequest>(),
                It.IsAny<ExecutionTrace?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new QueryExecution
            {
                Supported = true,
                Hits = hits.ToAsyncEnumerable(),
                Warnings = [],
                RequestedPage = 1,
                PagingMode = QueryPagingMode.RequestedSourcePage,
                SourceHasMore = false
            });
        var query = new ContentHierarchyItemCriteria
        {
            Level = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.In,
                        Values = ["show", "season"]
                    }
                ]
            }
        };

        // Act
        var result = await _searcher.AdvancedSearchAsync("tv", query, 25, "en-US", default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Matches, Has.Count.EqualTo(1));
            Assert.That(result.Matches[0].ExternalId, Is.EqualTo("1399"));
            Assert.That(
                result.Warnings.Select(warning => warning),
                Has.Some.Contains("cannot search individual TV seasons or episodes"));
        }
    }

    [Test]
    public async Task AdvancedSearchAsync_PagesSourceUntilMaximumIsExceeded_WhenSourceReportsMore()
    {
        // Arrange
        List<SearchHit> firstPageHits =
        [
            new() { Route = ContentRoute.Movie, Id = 1, Title = "One" },
            new() { Route = ContentRoute.Movie, Id = 2, Title = "Two" }
        ];
        List<SearchHit> secondPageHits =
        [
            new() { Route = ContentRoute.Movie, Id = 3, Title = "Three" },
            new() { Route = ContentRoute.Movie, Id = 4, Title = "Four" }
        ];
        _engine.Setup(engine => engine.SearchAsync(
                It.Is<QueryRequest>(request => request.Page == 1),
                It.IsAny<ExecutionTrace?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new QueryExecution
            {
                Supported = true,
                Hits = firstPageHits.ToAsyncEnumerable(),
                Warnings = [],
                RequestedPage = 1,
                PagingMode = QueryPagingMode.RequestedSourcePage,
                SourceHasMore = true
            });
        _engine.Setup(engine => engine.SearchAsync(
                It.Is<QueryRequest>(request => request.Page == 2),
                It.IsAny<ExecutionTrace?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new QueryExecution
            {
                Supported = true,
                Hits = secondPageHits.ToAsyncEnumerable(),
                Warnings = [],
                RequestedPage = 1,
                PagingMode = QueryPagingMode.RequestedSourcePage,
                SourceHasMore = true
            });

        // Act
        var result = await _searcher.AdvancedSearchAsync(
            "movie",
            new ContentHierarchyItemCriteria(),
            3,
            "en-US",
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Matches.Select(match => match.ExternalId), Is.EqualTo(["1", "2", "3"]));
            Assert.That(result.PageInfo.HasMore, Is.True);
        }

        _engine.Verify(
            engine => engine.SearchAsync(
                It.IsAny<QueryRequest>(),
                It.IsAny<ExecutionTrace?>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Test]
    public async Task AdvancedSearchAsync_StopsAtOnePage_WhenPagingModeIsLogicalStream()
    {
        // Arrange
        List<SearchHit> hits =
        [
            new() { Route = ContentRoute.Movie, Id = 1, Title = "One" },
            new() { Route = ContentRoute.Movie, Id = 2, Title = "Two" }
        ];
        _engine.Setup(engine => engine.SearchAsync(
                It.IsAny<QueryRequest>(),
                It.IsAny<ExecutionTrace?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new QueryExecution
            {
                Supported = true,
                Hits = hits.ToAsyncEnumerable(),
                Warnings = [],
                RequestedPage = 1,
                PagingMode = QueryPagingMode.LogicalStream,
                SourceHasMore = true
            });

        // Act
        var result = await _searcher.AdvancedSearchAsync(
            "movie",
            new ContentHierarchyItemCriteria(),
            1,
            "en-US",
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Matches.Select(match => match.ExternalId), Is.EqualTo(["1"]));
            Assert.That(result.PageInfo.HasMore, Is.True);
        }

        _engine.Verify(
            engine => engine.SearchAsync(
                It.IsAny<QueryRequest>(),
                It.IsAny<ExecutionTrace?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task AdvancedSearchAsync_AddsPageLimitWarning_WhenSourcePagingReachesTheLastAllowedPage()
    {
        // Arrange
        List<SearchHit> hits = [new() { Route = ContentRoute.Movie, Id = 1, Title = "One" }];
        _engine.Setup(engine => engine.SearchAsync(
                It.IsAny<QueryRequest>(),
                It.IsAny<ExecutionTrace?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new QueryExecution
            {
                Supported = true,
                Hits = hits.ToAsyncEnumerable(),
                Warnings = [],
                RequestedPage = 1,
                PagingMode = QueryPagingMode.RequestedSourcePage,
                SourceHasMore = true
            });

        // Act
        var result = await _searcher.AdvancedSearchAsync(
            "movie",
            new ContentHierarchyItemCriteria(),
            5000,
            "en-US",
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Warnings.Select(warning => warning), Has.Some.Contains("500"));
            Assert.That(result.Matches, Has.Count.EqualTo(500));
        }
    }

    [Test]
    public async Task AdvancedSearchAsync_DeduplicatesWarnings_WhenEveryPageRepeatsTheSameWarning()
    {
        // Arrange
        List<SearchHit> firstPageHits = [new() { Route = ContentRoute.Movie, Id = 1, Title = "One" }];
        List<SearchHit> secondPageHits = [new() { Route = ContentRoute.Movie, Id = 2, Title = "Two" }];
        var warning = "TMDb could not complete the remote query.";
        _engine.Setup(engine => engine.SearchAsync(
                It.Is<QueryRequest>(request => request.Page == 1),
                It.IsAny<ExecutionTrace?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new QueryExecution
            {
                Supported = true,
                Hits = firstPageHits.ToAsyncEnumerable(),
                Warnings = [warning],
                RequestedPage = 1,
                PagingMode = QueryPagingMode.RequestedSourcePage,
                SourceHasMore = true
            });
        _engine.Setup(engine => engine.SearchAsync(
                It.Is<QueryRequest>(request => request.Page == 2),
                It.IsAny<ExecutionTrace?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new QueryExecution
            {
                Supported = true,
                Hits = secondPageHits.ToAsyncEnumerable(),
                Warnings = [warning],
                RequestedPage = 1,
                PagingMode = QueryPagingMode.RequestedSourcePage,
                SourceHasMore = false
            });

        // Act
        var result = await _searcher.AdvancedSearchAsync(
            "movie",
            new ContentHierarchyItemCriteria(),
            25,
            "en-US",
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Warnings, Has.Count.EqualTo(1));
            Assert.That(result.Warnings[0], Is.EqualTo(warning));
            Assert.That(result.PageInfo.HasMore, Is.False);
        }
    }

    [Test]
    public async Task SearchAsync_SkipsPrecedingLogicalPages_WhenRequestedPageIsBeyondTheFirst()
    {
        // Arrange
        var level = new ContentHierarchyLevelDefinition
        {
            Slug = "movie",
            ParentSlug = null,
            DisplayName = "Movie",
            CustomMetadataFields = []
        };
        List<SearchHit> hits =
        [
            new() { Route = ContentRoute.Movie, Id = 1, Title = "Title" },
            new() { Route = ContentRoute.Movie, Id = 2, Title = "Title" },
            new() { Route = ContentRoute.Movie, Id = 3, Title = "Title" },
            new() { Route = ContentRoute.Movie, Id = 4, Title = "Title" },
            new() { Route = ContentRoute.Movie, Id = 5, Title = "Title" },
            new() { Route = ContentRoute.Movie, Id = 6, Title = "Title" },
            new() { Route = ContentRoute.Movie, Id = 7, Title = "Title" },
            new() { Route = ContentRoute.Movie, Id = 8, Title = "Title" },
            new() { Route = ContentRoute.Movie, Id = 9, Title = "Title" },
            new() { Route = ContentRoute.Movie, Id = 10, Title = "Title" },
            new() { Route = ContentRoute.Movie, Id = 11, Title = "Title" },
            new() { Route = ContentRoute.Movie, Id = 12, Title = "Title" },
            new() { Route = ContentRoute.Movie, Id = 13, Title = "Title" },
            new() { Route = ContentRoute.Movie, Id = 14, Title = "Title" },
            new() { Route = ContentRoute.Movie, Id = 15, Title = "Title" },
            new() { Route = ContentRoute.Movie, Id = 16, Title = "Title" },
            new() { Route = ContentRoute.Movie, Id = 17, Title = "Title" },
            new() { Route = ContentRoute.Movie, Id = 18, Title = "Title" },
            new() { Route = ContentRoute.Movie, Id = 19, Title = "Title" },
            new() { Route = ContentRoute.Movie, Id = 20, Title = "Title" },
            new() { Route = ContentRoute.Movie, Id = 21, Title = "Title" },
            new() { Route = ContentRoute.Movie, Id = 22, Title = "Title" },
            new() { Route = ContentRoute.Movie, Id = 23, Title = "Title" },
            new() { Route = ContentRoute.Movie, Id = 24, Title = "Title" },
            new() { Route = ContentRoute.Movie, Id = 25, Title = "Title" }
        ];
        _engine.Setup(engine => engine.SearchAsync(It.IsAny<QueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new QueryExecution
            {
                Supported = true,
                Hits = hits.ToAsyncEnumerable(),
                Warnings = [],
                RequestedPage = 2,
                PagingMode = QueryPagingMode.LogicalStream
            });

        // Act
        var results = await _searcher.SearchAsync(level, "Title", "en-US", 2, default).ConfigureAwait(false);

        // Assert
        Assert.That(
            results.Select(item => item.ExternalId),
            Is.EqualTo(["21", "22", "23", "24", "25"]));
    }

    [Test]
    public async Task AdvancedSearchAsync_ReportsMore_WhenTheExecutionStreamFaulted()
    {
        // Arrange
        List<SearchHit> hits = [new() { Route = ContentRoute.Movie, Id = 1, Title = "One" }];
        var warning = "TMDb could not complete the remote query.";
        var execution = new QueryExecution
        {
            Supported = true,
            Hits = hits.ToAsyncEnumerable(),
            Warnings = [warning],
            RequestedPage = 1,
            PagingMode = QueryPagingMode.LogicalStream
        };
        execution.Diagnostics.StreamFaulted = true;
        _engine.Setup(engine => engine.SearchAsync(
                It.IsAny<QueryRequest>(),
                It.IsAny<ExecutionTrace?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(execution);

        // Act
        var result = await _searcher.AdvancedSearchAsync(
            "movie",
            new ContentHierarchyItemCriteria(),
            1,
            "en-US",
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Matches, Has.Count.EqualTo(1));
            Assert.That(
                result.Warnings.Select(item => item),
                Has.Some.Contains("could not complete"));
            Assert.That(result.PageInfo.HasMore, Is.True);
        }
    }

    [TestCase("wikidata", "550", "movie")]
    [TestCase("tmdb", "not-a-number", "movie")]
    [TestCase("tmdb", "0", "movie")]
    [TestCase("tmdb", "550", "book")]
    public async Task HydrateAsync_ReturnsNull_WhenCandidateCannotAddressATmdbDetail(
        string providerType,
        string externalId,
        string contentType)
    {
        // Arrange
        var candidate = new MetadataSearchCandidate
        {
            ProviderType = providerType,
            ExternalIdSource = "tmdb",
            ExternalId = externalId,
            ContentType = contentType,
            Title = "Fight Club"
        };

        // Act
        var hydrated = await _searcher.HydrateAsync(
            candidate,
            "en-US",
            default).ConfigureAwait(false);

        // Assert
        Assert.That(hydrated, Is.Null);
        _hydrationCache.VerifyNoOtherCalls();
    }

    [Test]
    public async Task HydrateAsync_AppendsMetadataSections_WhenCandidateIsValid()
    {
        // Arrange
        _hydrationCache.Setup(cache => cache.GetOrCreateAsync(
                It.IsAny<HydrationCache.HydrationKey>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Func<It.IsAnyType, CancellationToken, Task<MetadataSearchCandidate?>>>(),
                It.IsAny<CancellationToken>()))
            .Returns(new InvocationFunc(invocation => ((Delegate)invocation.Arguments[2]).DynamicInvoke(
                invocation.Arguments[1],
                invocation.Arguments[3])!));
        var issued = default(Request);
        _client.Setup(client => client.GetAsync<MovieDetailDto>(
                It.IsAny<RequestContext>(),
                It.IsAny<Request>(),
                It.IsAny<CancellationToken>()))
            .Callback((RequestContext _, Request request, CancellationToken _) => issued = request)
            .ReturnsAsync(new MovieDetailDto { Id = 550, Title = "Fight Club" });
        var candidate = new MetadataSearchCandidate
        {
            ProviderType = Constants.ProviderType,
            ExternalIdSource = "tmdb",
            ExternalId = "550",
            ContentType = "movie",
            Title = "Fight Club"
        };

        // Act
        var hydrated = await _searcher.HydrateAsync(
            candidate,
            "en-US",
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(hydrated, Is.Not.Null);
            Assert.That(issued!.Path, Is.EqualTo("3/movie/550"));
            Assert.That(
                issued.QueryParameters,
                Is.EqualTo([
                    ("language", "en-US"),
                    ("append_to_response", "release_dates,credits,images,external_ids,videos")
                ]));
        }

        _hydrationCache.Verify(cache => cache.GetOrCreateAsync(
            It.IsAny<HydrationCache.HydrationKey>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Func<It.IsAnyType, CancellationToken, Task<MetadataSearchCandidate?>>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task HydrateAsync_MergesCandidateAliasesIntoHydratedSubjects_WhenHydrated()
    {
        // Arrange
        _hydrationCache.Setup(cache => cache.GetOrCreateAsync(
                It.IsAny<HydrationCache.HydrationKey>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Func<It.IsAnyType, CancellationToken, Task<MetadataSearchCandidate?>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MetadataSearchCandidate
            {
                ProviderType = Constants.ProviderType,
                ExternalIdSource = "tmdb",
                ExternalId = "1399",
                ContentType = "tv",
                Title = "Game of Thrones",
                SubjectReferences =
                [
                    new ProviderMetadataSearchSubjectReference
                    {
                        RelationshipKind = "person",
                        SubjectKind = "person",
                        DisplayName = "David Benioff",
                        ExternalIds = new Dictionary<string, string> { ["tmdb"] = "228068" }
                    }
                ]
            });
        var candidate = new MetadataSearchCandidate
        {
            ProviderType = Constants.ProviderType,
            ExternalIdSource = "tmdb",
            ExternalId = "1399",
            ContentType = "tv",
            Title = "Game of Thrones",
            SubjectReferences =
            [
                new ProviderMetadataSearchSubjectReference
                {
                    RelationshipKind = "person",
                    SubjectKind = "person",
                    DisplayName = "David Benioff",
                    ExternalIds = new Dictionary<string, string> { ["imdb"] = "nm1481301" }
                }
            ]
        };

        // Act
        var hydrated = await _searcher.HydrateAsync(
            candidate,
            "en-US",
            default).ConfigureAwait(false);

        // Assert
        Assert.That(hydrated, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(hydrated!.SubjectReferences, Has.Count.EqualTo(1));
            Assert.That(hydrated.SubjectReferences[0].ExternalIds["tmdb"], Is.EqualTo("228068"));
            Assert.That(hydrated.SubjectReferences[0].ExternalIds["imdb"], Is.EqualTo("nm1481301"));
        }
    }
}
