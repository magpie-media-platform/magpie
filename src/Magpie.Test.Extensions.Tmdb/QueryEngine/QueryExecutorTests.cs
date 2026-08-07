using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Tmdb;
using Magpie.Extensions.Tmdb.Mapping;
using Magpie.Extensions.Tmdb.QueryEngine;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Tmdb.QueryEngine;

[TestFixture]
public class QueryExecutorTests
{
    private static readonly RequestContext Context = new() { ApiKey = "key", Language = "en-US" };

    private List<Request> _requests = null!;
    private Dictionary<string, int> _genres = null!;
    private Mock<ITmdbClient> _client = null!;
    private Mock<IQueryCache> _cache = null!;
    private Dictionary<string, QueryScanState> _scanStates = null!;
    private IQueryExecutor _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _requests = [];
        _genres = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        _client = new Mock<ITmdbClient>();
        _client.Setup(client => client.GetAsync<SearchEnvelopeDto<MovieSearchHitDto>>(
                It.IsAny<RequestContext>(),
                It.IsAny<Request>(),
                It.IsAny<CancellationToken>()))
            .Callback((RequestContext _, Request request, CancellationToken _) => _requests.Add(request))
            .ReturnsAsync(new SearchEnvelopeDto<MovieSearchHitDto>
            {
                Page = 1,
                TotalPages = 1,
                TotalResults = 1,
                Results = [new MovieSearchHitDto { Id = 1726, Title = "Iron Man" }]
            });
        _client.Setup(client => client.GetAsync<SearchEnvelopeDto<TvSearchHitDto>>(
                It.IsAny<RequestContext>(),
                It.IsAny<Request>(),
                It.IsAny<CancellationToken>()))
            .Callback((RequestContext _, Request request, CancellationToken _) => _requests.Add(request))
            .ReturnsAsync(new SearchEnvelopeDto<TvSearchHitDto>
            {
                Page = 1,
                TotalPages = 1,
                TotalResults = 1,
                Results = [new TvSearchHitDto { Id = 1399, Name = "Game of Thrones" }]
            });
        _client.Setup(client => client.GetAsync<FindEnvelopeDto>(
                It.IsAny<RequestContext>(),
                It.IsAny<Request>(),
                It.IsAny<CancellationToken>()))
            .Callback((RequestContext _, Request request, CancellationToken _) => _requests.Add(request))
            .ReturnsAsync((FindEnvelopeDto?)null);
        _client.Setup(client => client.GetAsync<MovieDetailDto>(
                It.IsAny<RequestContext>(),
                It.IsAny<Request>(),
                It.IsAny<CancellationToken>()))
            .Callback((RequestContext _, Request request, CancellationToken _) => _requests.Add(request))
            .ReturnsAsync((MovieDetailDto?)null);
        _client.Setup(client => client.GetAsync<TvDetailDto>(
                It.IsAny<RequestContext>(),
                It.IsAny<Request>(),
                It.IsAny<CancellationToken>()))
            .Callback((RequestContext _, Request request, CancellationToken _) => _requests.Add(request))
            .ReturnsAsync((TvDetailDto?)null);
        _cache = new Mock<IQueryCache>();
        _scanStates = [];
        _cache.Setup(cache => cache.GetScanState(It.IsAny<string>()))
            .Returns((string key) =>
            {
                if (!_scanStates.TryGetValue(key, out var state))
                {
                    state = new QueryScanState();
                    _scanStates[key] = state;
                }

                return state;
            });
        _cache.Setup(cache => cache.GetGenreMapAsync(
                It.IsAny<RequestContext>(),
                It.IsAny<Request>(),
                It.IsAny<ITmdbClient>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _genres);
        _subject = new QueryExecutor(_client.Object, _cache.Object, NullLogger<QueryExecutor>.Instance);
    }

    [Test]
    public async Task ExecuteAsync_ReturnsEmptyWithoutFetching_WhenRootIsAnEmptyUnion()
    {
        // Arrange
        var plan = new QueryPlan
        {
            Root = new UnionPlanNode
            {
                Id = 1,
                Precondition = PlanPrecondition.None,
                Contributes = "planned empty",
                Children = []
            },
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 25,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };

        // Act
        var execution = await _subject.ExecuteAsync(
            plan,
            new RequestContext { ApiKey = "key", Language = "en-US" },
            default).ConfigureAwait(false);
        var hits = await execution.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(execution.Supported, Is.True);
            Assert.That(hits, Is.Empty);
            Assert.That(execution.Warnings, Is.Empty);
        }

        _client.VerifyNoOtherCalls();
    }

    [Test]
    public async Task ExecuteAsync_WarnsAndMarksTheStreamFaulted_WhenTheRemoteCallThrows()
    {
        // Arrange
        _client.Setup(client => client.GetAsync<SearchEnvelopeDto<MovieSearchHitDto>>(
                It.IsAny<RequestContext>(),
                It.IsAny<Request>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("remote boom"));
        var plan = new QueryPlan
        {
            Root = new FetchPlanNode
            {
                Id = 1,
                Precondition = PlanPrecondition.None,
                Contributes = "fetch",
                Route = ContentRoute.Movie,
                AnchorKind = QueryAnchorKind.Search,
                SubjectBindings = [],
                PushedFilters = [],
                RequestedPage = 1,
                Fingerprint = "fetch-1",
                OrdinarySearchTerm = "Iron Man"
            },
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 25,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };

        // Act
        var execution = await _subject.ExecuteAsync(
            plan,
            new RequestContext { ApiKey = "key", Language = "en-US" },
            default).ConfigureAwait(false);
        var hits = await execution.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(hits, Is.Empty);
            Assert.That(
                execution.Warnings.Select(warning => warning),
                Is.EqualTo(["TMDb could not complete the remote query."]));
            Assert.That(execution.Diagnostics.StreamFaulted, Is.True);
        }
    }

    [TestCase(1, 4, true)]
    [TestCase(1, 1, false)]
    public async Task ExecuteAsync_ReportsSourcePaging_WhenTheFetchCarriesAnOrdinarySearchTerm(
        int requestedPage,
        int totalPages,
        bool expectedHasMore)
    {
        // Arrange
        _client.Setup(client => client.GetAsync<SearchEnvelopeDto<MovieSearchHitDto>>(
                It.IsAny<RequestContext>(),
                It.Is<Request>(request => request.CacheKey == "3/search/movie?query=Iron Man&language=en-US&page=1"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchEnvelopeDto<MovieSearchHitDto>
            {
                Page = requestedPage,
                TotalPages = totalPages,
                TotalResults = 80,
                Results = [new MovieSearchHitDto { Id = 1726, Title = "Iron Man" }]
            });
        var plan = new QueryPlan
        {
            Root = new FetchPlanNode
            {
                Id = 7,
                Precondition = PlanPrecondition.None,
                Contributes = "fetch",
                Route = ContentRoute.Movie,
                AnchorKind = QueryAnchorKind.Search,
                SubjectBindings = [],
                PushedFilters = [],
                RequestedPage = requestedPage,
                Fingerprint = "fetch-7",
                OrdinarySearchTerm = "Iron Man"
            },
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 25,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };

        // Act
        var execution = await _subject.ExecuteAsync(
            plan,
            new RequestContext { ApiKey = "key", Language = "en-US" },
            default).ConfigureAwait(false);
        var hits = await execution.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(hits.Single().Id, Is.EqualTo(1726));
            Assert.That(hits.Single().Title, Is.EqualTo("Iron Man"));
            Assert.That(hits.Single().SourcePlanNodeId, Is.EqualTo(7));
            Assert.That(execution.PagingMode, Is.EqualTo(QueryPagingMode.RequestedSourcePage));
            Assert.That(execution.SourceHasMore, Is.EqualTo(expectedHasMore));
        }

        _client.Verify(
            client => client.GetAsync<SearchEnvelopeDto<MovieSearchHitDto>>(
                It.IsAny<RequestContext>(),
                It.Is<Request>(request => request.CacheKey == "3/search/movie?query=Iron Man&language=en-US&page=1"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_StopsVisitingUnionChildren_WhenTheFilterResultLimitIsSatisfied()
    {
        // Arrange
        _client.Setup(client => client.GetAsync<SearchEnvelopeDto<MovieSearchHitDto>>(
                It.IsAny<RequestContext>(),
                It.IsAny<Request>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchEnvelopeDto<MovieSearchHitDto>
            {
                Page = 1,
                TotalPages = 1,
                TotalResults = 2,
                Results =
                [
                    new MovieSearchHitDto { Id = 10, Title = "First" },
                    new MovieSearchHitDto { Id = 11, Title = "Second" }
                ]
            });
        var trace = new ExecutionTrace();
        var plan = new QueryPlan
        {
            Root = new FilterPlanNode
            {
                Id = 5,
                Precondition = PlanPrecondition.None,
                Contributes = "filter",
                Route = ContentRoute.Movie,
                Child = new UnionPlanNode
                {
                    Id = 4,
                    Precondition = PlanPrecondition.None,
                    Contributes = "union",
                    Children =
                    [
                        new FetchPlanNode
                        {
                            Id = 2,
                            Precondition = PlanPrecondition.None,
                            Contributes = "fetch",
                            Route = ContentRoute.Movie,
                            AnchorKind = QueryAnchorKind.Search,
                            SubjectBindings = [],
                            PushedFilters = [],
                            RequestedPage = 1,
                            Fingerprint = "fetch-2",
                            OrdinarySearchTerm = "Iron Man"
                        },
                        new FetchPlanNode
                        {
                            Id = 3,
                            Precondition = PlanPrecondition.None,
                            Contributes = "fetch",
                            Route = ContentRoute.Movie,
                            AnchorKind = QueryAnchorKind.Search,
                            SubjectBindings = [],
                            PushedFilters = [],
                            RequestedPage = 1,
                            Fingerprint = "fetch-3",
                            OrdinarySearchTerm = "Iron Man 2"
                        }
                    ]
                },
                ResidualPredicates = [],
                TextFilters = [],
                PublishDateFilters = [],
                DurationFilters = [],
                GenreFilters = [],
                SubjectBindings = [],
                ContentIdFilters = [],
                ElementFilters = [],
                RequestedPage = 1,
                MaximumResults = 1
            },
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 25,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };

        // Act
        var execution = await _subject.ExecuteAsync(
            plan,
            new RequestContext { ApiKey = "key", Language = "en-US", ExecutionTrace = trace },
            default).ConfigureAwait(false);
        var hits = await execution.Hits.ToListAsync().ConfigureAwait(false);
        var outcomes = trace.Nodes();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(hits, Has.Count.EqualTo(2));
            Assert.That(outcomes.Single(outcome => outcome.NodeId == 3).Status, Is.EqualTo("skipped"));
            Assert.That(
                outcomes.Single(outcome => outcome.NodeId == 3).Reason,
                Is.EqualTo("union result limit was satisfied"));
            Assert.That(outcomes.Single(outcome => outcome.NodeId == 2).Status, Is.EqualTo("ran"));
        }

        _client.Verify(
            client => client.GetAsync<SearchEnvelopeDto<MovieSearchHitDto>>(
                It.IsAny<RequestContext>(),
                It.IsAny<Request>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_EmitsEachContentOnlyOnce_WhenUnionChildrenOverlap()
    {
        // Arrange
        _client.Setup(client => client.GetAsync<SearchEnvelopeDto<MovieSearchHitDto>>(
                It.IsAny<RequestContext>(),
                It.IsAny<Request>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchEnvelopeDto<MovieSearchHitDto>
            {
                Page = 1,
                TotalPages = 1,
                TotalResults = 1,
                Results = [new MovieSearchHitDto { Id = 10, Title = "First" }]
            });
        var plan = new QueryPlan
        {
            Root = new UnionPlanNode
            {
                Id = 4,
                Precondition = PlanPrecondition.None,
                Contributes = "union",
                Children =
                [
                    new FetchPlanNode
                    {
                        Id = 2,
                        Precondition = PlanPrecondition.None,
                        Contributes = "fetch",
                        Route = ContentRoute.Movie,
                        AnchorKind = QueryAnchorKind.Search,
                        SubjectBindings = [],
                        PushedFilters = [],
                        RequestedPage = 1,
                        Fingerprint = "fetch-2",
                        OrdinarySearchTerm = "Iron Man"
                    },
                    new FetchPlanNode
                    {
                        Id = 3,
                        Precondition = PlanPrecondition.None,
                        Contributes = "fetch",
                        Route = ContentRoute.Movie,
                        AnchorKind = QueryAnchorKind.Search,
                        SubjectBindings = [],
                        PushedFilters = [],
                        RequestedPage = 1,
                        Fingerprint = "fetch-3",
                        OrdinarySearchTerm = "Iron Man 2"
                    }
                ]
            },
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 25,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };

        // Act
        var execution = await _subject.ExecuteAsync(
            plan,
            new RequestContext { ApiKey = "key", Language = "en-US" },
            default).ConfigureAwait(false);
        var hits = await execution.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(hits.Select(hit => hit.Id), Is.EqualTo([10]));
            Assert.That(execution.PagingMode, Is.EqualTo(QueryPagingMode.LogicalStream));
        }
    }

    [Test]
    public async Task ExecuteAsync_KeepsOnlySharedContent_WhenRootIsAnIntersect()
    {
        // Arrange
        _client.Setup(client => client.GetAsync<SearchEnvelopeDto<MovieSearchHitDto>>(
                It.IsAny<RequestContext>(),
                It.Is<Request>(request => request.CacheKey.Contains("query=Left", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchEnvelopeDto<MovieSearchHitDto>
            {
                Page = 1,
                TotalPages = 1,
                TotalResults = 2,
                Results =
                [
                    new MovieSearchHitDto { Id = 10, Title = "Shared" },
                    new MovieSearchHitDto { Id = 11, Title = "Left only" }
                ]
            });
        _client.Setup(client => client.GetAsync<SearchEnvelopeDto<MovieSearchHitDto>>(
                It.IsAny<RequestContext>(),
                It.Is<Request>(request => request.CacheKey.Contains("query=Right", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchEnvelopeDto<MovieSearchHitDto>
            {
                Page = 1,
                TotalPages = 1,
                TotalResults = 2,
                Results =
                [
                    new MovieSearchHitDto { Id = 12, Title = "Right only" },
                    new MovieSearchHitDto { Id = 10, Title = "Shared" }
                ]
            });
        var plan = new QueryPlan
        {
            Root = new IntersectPlanNode
            {
                Id = 4,
                Precondition = PlanPrecondition.None,
                Contributes = "intersect",
                Children =
                [
                    new FetchPlanNode
                    {
                        Id = 2,
                        Precondition = PlanPrecondition.None,
                        Contributes = "fetch",
                        Route = ContentRoute.Movie,
                        AnchorKind = QueryAnchorKind.Search,
                        SubjectBindings = [],
                        PushedFilters = [],
                        RequestedPage = 1,
                        Fingerprint = "fetch-2",
                        OrdinarySearchTerm = "Left"
                    },
                    new FetchPlanNode
                    {
                        Id = 3,
                        Precondition = PlanPrecondition.None,
                        Contributes = "fetch",
                        Route = ContentRoute.Movie,
                        AnchorKind = QueryAnchorKind.Search,
                        SubjectBindings = [],
                        PushedFilters = [],
                        RequestedPage = 1,
                        Fingerprint = "fetch-3",
                        OrdinarySearchTerm = "Right"
                    }
                ]
            },
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 25,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };

        // Act
        var execution = await _subject.ExecuteAsync(
            plan,
            new RequestContext { ApiKey = "key", Language = "en-US" },
            default).ConfigureAwait(false);
        var hits = await execution.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        Assert.That(hits.Select(hit => hit.Id), Is.EqualTo([10]));
    }

    [Test]
    public async Task ExecuteAsync_SkipsTheFetch_WhenItsPreconditionIsUnsatisfied()
    {
        // Arrange
        var trace = new ExecutionTrace();
        var plan = new QueryPlan
        {
            Root = new FetchPlanNode
            {
                Id = 2,
                Precondition = new PlanPrecondition { Kind = PlanPreconditionKind.NodeFailed, NodeId = 1 },
                Contributes = "fetch",
                Route = ContentRoute.Movie,
                AnchorKind = QueryAnchorKind.Search,
                SubjectBindings = [],
                PushedFilters = [],
                RequestedPage = 1,
                Fingerprint = "fetch-2",
                OrdinarySearchTerm = "Iron Man",
                Resolve = new ResolvePlanNode
                {
                    Id = 1,
                    Precondition = PlanPrecondition.None,
                    Contributes = "resolve",
                    Route = ContentRoute.Movie,
                    SubjectBindings =
                    [
                        new SubjectResolutionBinding(
                            "person-0",
                            new SubjectFilter(false, [new SubjectAlternative([SubjectRole.Person], [])]))
                    ],
                    GenreFilters = [],
                    ResolveImageBase = false
                }
            },
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 25,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };

        // Act
        var execution = await _subject.ExecuteAsync(
            plan,
            new RequestContext { ApiKey = "key", Language = "en-US", ExecutionTrace = trace },
            default).ConfigureAwait(false);
        var hits = await execution.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(hits, Is.Empty);
            Assert.That(trace.Nodes().Single(outcome => outcome.NodeId == 2).Status, Is.EqualTo("skipped"));
            Assert.That(
                trace.Nodes().Single(outcome => outcome.NodeId == 2).Reason,
                Is.EqualTo("precondition unsatisfied"));
        }

        _client.VerifyNoOtherCalls();
    }

    [Test]
    public async Task ExecuteAsync_RunsTheFallbackFetch_WhenSubjectResolutionFailedRemotely()
    {
        // Arrange
        _cache.Setup(cache => cache.GetSubjectResolutionsAsync(
                It.IsAny<string>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Func<It.IsAnyType, CancellationToken, Task<IReadOnlyList<SubjectResolution>>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new SubjectResolution
                {
                    Kind = SubjectResolutionKind.Failed,
                    Warning = "TMDb could not resolve one or more relationship names."
                }
            ]);
        _client.Setup(client => client.GetAsync<SearchEnvelopeDto<MovieSearchHitDto>>(
                It.IsAny<RequestContext>(),
                It.IsAny<Request>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchEnvelopeDto<MovieSearchHitDto>
            {
                Page = 1,
                TotalPages = 1,
                TotalResults = 1,
                Results = [new MovieSearchHitDto { Id = 10, Title = "Fallback" }]
            });
        var plan = new QueryPlan
        {
            Root = new FetchPlanNode
            {
                Id = 2,
                Precondition = new PlanPrecondition { Kind = PlanPreconditionKind.NodeFailed, NodeId = 1 },
                Contributes = "fetch",
                Route = ContentRoute.Movie,
                AnchorKind = QueryAnchorKind.Search,
                SubjectBindings = [],
                PushedFilters = [],
                RequestedPage = 1,
                Fingerprint = "fetch-2",
                OrdinarySearchTerm = "Iron Man",
                Resolve = new ResolvePlanNode
                {
                    Id = 1,
                    Precondition = PlanPrecondition.None,
                    Contributes = "resolve",
                    Route = ContentRoute.Movie,
                    SubjectBindings =
                    [
                        new SubjectResolutionBinding(
                            "person-0",
                            new SubjectFilter(
                                false,
                                [
                                    new SubjectAlternative(
                                        [SubjectRole.Person],
                                        [[new SubjectIdentity("Alice", null, null)]])
                                ]))
                    ],
                    GenreFilters = [],
                    ResolveImageBase = false
                }
            },
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 25,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };

        // Act
        var execution = await _subject.ExecuteAsync(
            plan,
            new RequestContext { ApiKey = "key", Language = "en-US" },
            default).ConfigureAwait(false);
        var hits = await execution.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(hits.Single().Id, Is.EqualTo(10));
            Assert.That(
                execution.Warnings.Select(warning => warning),
                Has.Some.Contains("could not resolve"));
        }
    }

    [Test]
    public async Task ExecuteAsync_ReturnsEmptyWithoutFetching_WhenARequiredSubjectGroupResolvedToNothing()
    {
        // Arrange
        var trace = new ExecutionTrace();
        var plan = new QueryPlan
        {
            Root = new FetchPlanNode
            {
                Id = 2,
                Precondition = PlanPrecondition.None,
                Contributes = "fetch",
                Route = ContentRoute.Movie,
                AnchorKind = QueryAnchorKind.Search,
                SubjectBindings = [],
                PushedFilters = [],
                RequestedPage = 1,
                Fingerprint = "fetch-2",
                OrdinarySearchTerm = "Iron Man",
                Resolve = new ResolvePlanNode
                {
                    Id = 1,
                    Precondition = PlanPrecondition.None,
                    Contributes = "resolve",
                    Route = ContentRoute.Movie,
                    SubjectBindings =
                    [
                        new SubjectResolutionBinding(
                            "person-0",
                            new SubjectFilter(false, [new SubjectAlternative([SubjectRole.Person], [])]))
                    ],
                    GenreFilters = [],
                    ResolveImageBase = false
                }
            },
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 25,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };

        // Act
        var execution = await _subject.ExecuteAsync(
            plan,
            new RequestContext { ApiKey = "key", Language = "en-US", ExecutionTrace = trace },
            default).ConfigureAwait(false);
        var hits = await execution.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(hits, Is.Empty);
            Assert.That(
                trace.Nodes().Single(outcome => outcome.NodeId == 2).Reason,
                Is.EqualTo("resolved anchor is empty"));
        }

        _client.VerifyNoOtherCalls();
    }

    [Test]
    public async Task ExecuteAsync_ResolvesSubjectsOnce_WhenTwoFetchNodesShareOneResolveNode()
    {
        // Arrange
        _cache.Setup(cache => cache.GetSubjectResolutionsAsync(
                It.IsAny<string>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Func<It.IsAnyType, CancellationToken, Task<IReadOnlyList<SubjectResolution>>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SubjectResolution { Kind = SubjectResolutionKind.Resolved, Id = 1, Name = "Alice" }]);
        _client.Setup(client => client.GetAsync<SearchEnvelopeDto<MovieSearchHitDto>>(
                It.IsAny<RequestContext>(),
                It.IsAny<Request>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchEnvelopeDto<MovieSearchHitDto>
            {
                Page = 1,
                TotalPages = 1,
                TotalResults = 1,
                Results = [new MovieSearchHitDto { Id = 10, Title = "First" }]
            });
        var resolve = new ResolvePlanNode
        {
            Id = 1,
            Precondition = PlanPrecondition.None,
            Contributes = "resolve",
            Route = ContentRoute.Movie,
            SubjectBindings =
            [
                new SubjectResolutionBinding(
                    "person-0",
                    new SubjectFilter(
                        false,
                        [new SubjectAlternative([SubjectRole.Person], [[new SubjectIdentity("Alice", null, null)]])]))
            ],
            GenreFilters = [],
            ResolveImageBase = false
        };
        var plan = new QueryPlan
        {
            Root = new UnionPlanNode
            {
                Id = 4,
                Precondition = PlanPrecondition.None,
                Contributes = "union",
                Children =
                [
                    new FetchPlanNode
                    {
                        Id = 2,
                        Precondition = PlanPrecondition.None,
                        Contributes = "fetch",
                        Route = ContentRoute.Movie,
                        AnchorKind = QueryAnchorKind.Search,
                        SubjectBindings = [],
                        PushedFilters = [],
                        RequestedPage = 1,
                        Fingerprint = "fetch-2",
                        OrdinarySearchTerm = "One",
                        Resolve = resolve
                    },
                    new FetchPlanNode
                    {
                        Id = 3,
                        Precondition = PlanPrecondition.None,
                        Contributes = "fetch",
                        Route = ContentRoute.Movie,
                        AnchorKind = QueryAnchorKind.Search,
                        SubjectBindings = [],
                        PushedFilters = [],
                        RequestedPage = 1,
                        Fingerprint = "fetch-3",
                        OrdinarySearchTerm = "Two",
                        Resolve = resolve
                    }
                ]
            },
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 25,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };

        // Act
        var execution = await _subject.ExecuteAsync(
            plan,
            new RequestContext { ApiKey = "key", Language = "en-US" },
            default).ConfigureAwait(false);
        await execution.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        _cache.Verify(
            cache => cache.GetSubjectResolutionsAsync(
                It.IsAny<string>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Func<It.IsAnyType, CancellationToken, Task<IReadOnlyList<SubjectResolution>>>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_RunsTheFirstApplicableCoalesceChild_AndSkipsTheRemainder()
    {
        // Arrange
        _client.Setup(client => client.GetAsync<SearchEnvelopeDto<MovieSearchHitDto>>(
                It.IsAny<RequestContext>(),
                It.IsAny<Request>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchEnvelopeDto<MovieSearchHitDto>
            {
                Page = 1,
                TotalPages = 1,
                TotalResults = 1,
                Results = [new MovieSearchHitDto { Id = 10, Title = "First" }]
            });
        var trace = new ExecutionTrace();
        var plan = new QueryPlan
        {
            Root = new CoalescePlanNode
            {
                Id = 4,
                Precondition = PlanPrecondition.None,
                Contributes = "coalesce",
                Children =
                [
                    new FetchPlanNode
                    {
                        Id = 2,
                        Precondition = PlanPrecondition.None,
                        Contributes = "fetch",
                        Route = ContentRoute.Movie,
                        AnchorKind = QueryAnchorKind.Search,
                        SubjectBindings = [],
                        PushedFilters = [],
                        RequestedPage = 1,
                        Fingerprint = "fetch-2",
                        OrdinarySearchTerm = "One"
                    },
                    new FetchPlanNode
                    {
                        Id = 3,
                        Precondition = PlanPrecondition.None,
                        Contributes = "fetch",
                        Route = ContentRoute.Movie,
                        AnchorKind = QueryAnchorKind.Search,
                        SubjectBindings = [],
                        PushedFilters = [],
                        RequestedPage = 1,
                        Fingerprint = "fetch-3",
                        OrdinarySearchTerm = "Two"
                    }
                ]
            },
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 25,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };

        // Act
        var execution = await _subject.ExecuteAsync(
            plan,
            new RequestContext { ApiKey = "key", Language = "en-US", ExecutionTrace = trace },
            default).ConfigureAwait(false);
        var hits = await execution.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(hits.Single().Id, Is.EqualTo(10));
            Assert.That(trace.Nodes().Single(outcome => outcome.NodeId == 3).Status, Is.EqualTo("skipped"));
            Assert.That(
                trace.Nodes().Single(outcome => outcome.NodeId == 3).Reason,
                Is.EqualTo("coalesce already satisfied"));
        }

        _client.Verify(
            client => client.GetAsync<SearchEnvelopeDto<MovieSearchHitDto>>(
                It.IsAny<RequestContext>(),
                It.IsAny<Request>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_FetchesOneDetailWithoutCredits_WhenTheContentIdIsATmdbId()
    {
        // Arrange
        _client.Setup(client => client.GetAsync<MovieDetailDto>(
                It.IsAny<RequestContext>(),
                It.IsAny<Request>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MovieDetailDto { Id = 550, Title = "Fight Club" });
        var plan = new QueryPlan
        {
            Root = new FetchPlanNode
            {
                Id = 1,
                Precondition = PlanPrecondition.None,
                Contributes = "fetch",
                Route = ContentRoute.Movie,
                AnchorKind = QueryAnchorKind.ContentIds,
                SubjectBindings = [],
                PushedFilters = [],
                RequestedPage = 1,
                Fingerprint = "fetch-1",
                ContentIdFilters = [new ContentIdFilter(false, [new ContentId("tmdb", "550")])]
            },
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 25,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };

        // Act
        var execution = await _subject.ExecuteAsync(
            plan,
            new RequestContext { ApiKey = "key", Language = "en-US" },
            default).ConfigureAwait(false);
        var hits = await execution.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(hits.Single().Id, Is.EqualTo(550));
            Assert.That(execution.Warnings, Is.Empty);
            _client.Verify(
                client => client.GetAsync<MovieDetailDto>(
                    It.IsAny<RequestContext>(),
                    It.Is<Request>(request => request.CacheKey == "3/movie/550?language=en-US"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        _client.Verify(
            client => client.GetAsync<SearchEnvelopeDto<MovieSearchHitDto>>(
                It.IsAny<RequestContext>(),
                It.IsAny<Request>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_AppendsCreditsToTheContentIdDetail_WhenAPersonSubjectIsBound()
    {
        // Arrange
        _client.Setup(client => client.GetAsync<MovieDetailDto>(
                It.IsAny<RequestContext>(),
                It.IsAny<Request>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MovieDetailDto { Id = 550, Title = "Fight Club" });
        var plan = new QueryPlan
        {
            Root = new FetchPlanNode
            {
                Id = 1,
                Precondition = PlanPrecondition.None,
                Contributes = "fetch",
                Route = ContentRoute.Movie,
                AnchorKind = QueryAnchorKind.ContentIds,
                SubjectBindings =
                [
                    new SubjectResolutionBinding(
                        "person-0",
                        new SubjectFilter(false, [new SubjectAlternative([SubjectRole.Person], [])]))
                ],
                PushedFilters = [],
                RequestedPage = 1,
                Fingerprint = "fetch-1",
                ContentIdFilters = [new ContentIdFilter(false, [new ContentId("tmdb", "550")])]
            },
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 25,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };

        // Act
        var execution = await _subject.ExecuteAsync(
            plan,
            new RequestContext { ApiKey = "key", Language = "en-US" },
            default).ConfigureAwait(false);
        await execution.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        _client.Verify(
            client => client.GetAsync<MovieDetailDto>(
                It.IsAny<RequestContext>(),
                It.Is<Request>(request =>
                    request.CacheKey == "3/movie/550?language=en-US&append_to_response=credits"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestCase("imdb", "imdb_id")]
    [TestCase("tvdb", "tvdb_id")]
    [TestCase("wikidata", "wikidata_id")]
    public async Task ExecuteAsync_LooksUpTheMappedExternalSource_WhenTheContentIdIsNotATmdbId(
        string source,
        string expectedExternalSource)
    {
        // Arrange
        _client.Setup(client => client.GetAsync<FindEnvelopeDto>(
                It.IsAny<RequestContext>(),
                It.IsAny<Request>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FindEnvelopeDto
            {
                MovieResults = [new MovieSearchHitDto { Id = 550, Title = "Fight Club" }]
            });
        var plan = new QueryPlan
        {
            Root = new FetchPlanNode
            {
                Id = 1,
                Precondition = PlanPrecondition.None,
                Contributes = "fetch",
                Route = ContentRoute.Movie,
                AnchorKind = QueryAnchorKind.ContentIds,
                SubjectBindings = [],
                PushedFilters = [],
                RequestedPage = 1,
                Fingerprint = "fetch-1",
                ContentIdFilters = [new ContentIdFilter(false, [new ContentId(source, "external-1")])]
            },
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 25,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };

        // Act
        var execution = await _subject.ExecuteAsync(
            plan,
            new RequestContext { ApiKey = "key", Language = "en-US" },
            default).ConfigureAwait(false);
        var hits = await execution.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(hits.Single().Id, Is.EqualTo(550));
            Assert.That(hits.Single().ExternalIds[source], Is.EqualTo("external-1"));
            _client.Verify(
                client => client.GetAsync<FindEnvelopeDto>(
                    It.IsAny<RequestContext>(),
                    It.Is<Request>(request => request.CacheKey ==
                        "3/find/external-1?external_source=" + expectedExternalSource + "&language=en-US"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_WarnsWithoutCallingRemote_WhenTheContentIdSourceHasNoLookup()
    {
        // Arrange
        var plan = new QueryPlan
        {
            Root = new FetchPlanNode
            {
                Id = 1,
                Precondition = PlanPrecondition.None,
                Contributes = "fetch",
                Route = ContentRoute.Movie,
                AnchorKind = QueryAnchorKind.ContentIds,
                SubjectBindings = [],
                PushedFilters = [],
                RequestedPage = 1,
                Fingerprint = "fetch-1",
                ContentIdFilters = [new ContentIdFilter(false, [new ContentId("musicbrainz", "abc")])]
            },
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 25,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };

        // Act
        var execution = await _subject.ExecuteAsync(
            plan,
            new RequestContext { ApiKey = "key", Language = "en-US" },
            default).ConfigureAwait(false);
        var hits = await execution.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(hits, Is.Empty);
            Assert.That(
                execution.Warnings.Select(warning => warning),
                Has.Some.Contains("were not found on the requested route"));
        }

        _client.VerifyNoOtherCalls();
    }

    [Test]
    public async Task ExecuteAsync_WarnsAboutMissingIdentifiers_WhenOnlySomeAlternativesResolve()
    {
        // Arrange
        _client.Setup(client => client.GetAsync<MovieDetailDto>(
                It.IsAny<RequestContext>(),
                It.IsAny<Request>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((MovieDetailDto?)null);
        var plan = new QueryPlan
        {
            Root = new FetchPlanNode
            {
                Id = 1,
                Precondition = PlanPrecondition.None,
                Contributes = "fetch",
                Route = ContentRoute.Movie,
                AnchorKind = QueryAnchorKind.ContentIds,
                SubjectBindings = [],
                PushedFilters = [],
                RequestedPage = 1,
                Fingerprint = "fetch-1",
                ContentIdFilters = [new ContentIdFilter(false, [new ContentId("tmdb", "550")])]
            },
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 25,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };

        // Act
        var execution = await _subject.ExecuteAsync(
            plan,
            new RequestContext { ApiKey = "key", Language = "en-US" },
            default).ConfigureAwait(false);
        var hits = await execution.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(hits, Is.Empty);
            Assert.That(
                execution.Warnings.Select(warning => warning),
                Is.EqualTo(["One or more exact TMDb content identifiers were not found on the requested route."]));
        }
    }

    [Test]
    public async Task ExecuteAsync_IntersectsCredits_WhenMultiplePersonGroupsAreRequired()
    {
        // Arrange
        _cache.Setup(cache => cache.GetCreditsAsync(
                "3/person/1/tv_credits?language=en-US",
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Func<It.IsAnyType, CancellationToken, Task<CreditResponse>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreditResponse
            {
                Tv = new PersonTvCreditsDto
                {
                    Cast =
                    [
                        new TvCreditDto { Id = 10, Name = "Shared" },
                        new TvCreditDto { Id = 11, Name = "First only" }
                    ]
                }
            });
        _cache.Setup(cache => cache.GetCreditsAsync(
                "3/person/2/tv_credits?language=en-US",
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Func<It.IsAnyType, CancellationToken, Task<CreditResponse>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreditResponse
            {
                Tv = new PersonTvCreditsDto
                {
                    Cast =
                    [
                        new TvCreditDto { Id = 12, Name = "Second only" },
                        new TvCreditDto { Id = 10, Name = "Shared" }
                    ]
                }
            });
        _cache.Setup(cache => cache.GetSubjectResolutionsAsync(
                It.Is<string>(key => key.EndsWith("ALICE", StringComparison.Ordinal)),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Func<It.IsAnyType, CancellationToken, Task<IReadOnlyList<SubjectResolution>>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SubjectResolution { Kind = SubjectResolutionKind.Resolved, Id = 1, Name = "Alice" }]);
        _cache.Setup(cache => cache.GetSubjectResolutionsAsync(
                It.Is<string>(key => key.EndsWith("BOB", StringComparison.Ordinal)),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Func<It.IsAnyType, CancellationToken, Task<IReadOnlyList<SubjectResolution>>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SubjectResolution { Kind = SubjectResolutionKind.Resolved, Id = 2, Name = "Bob" }]);
        List<SubjectResolutionBinding> bindings =
        [
            new(
                "person-0",
                new SubjectFilter(
                    false,
                    [new SubjectAlternative([SubjectRole.Person], [[new SubjectIdentity("Alice", null, null)]])])),
            new(
                "person-1",
                new SubjectFilter(
                    false,
                    [new SubjectAlternative([SubjectRole.Person], [[new SubjectIdentity("Bob", null, null)]])]))
        ];
        var plan = new QueryPlan
        {
            Root = new FetchPlanNode
            {
                Id = 2,
                Precondition = PlanPrecondition.None,
                Contributes = "fetch",
                Route = ContentRoute.TvShow,
                AnchorKind = QueryAnchorKind.PersonCredits,
                SubjectBindings = bindings,
                PushedFilters = [],
                RequestedPage = 1,
                Fingerprint = "fetch-2",
                Resolve = new ResolvePlanNode
                {
                    Id = 1,
                    Precondition = PlanPrecondition.None,
                    Contributes = "resolve",
                    Route = ContentRoute.TvShow,
                    SubjectBindings = bindings,
                    GenreFilters = [],
                    ResolveImageBase = false
                }
            },
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 25,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };

        // Act
        var execution = await _subject.ExecuteAsync(
            plan,
            new RequestContext { ApiKey = "key", Language = "en-US" },
            default).ConfigureAwait(false);
        var hits = await execution.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        Assert.That(hits.Select(hit => hit.Id), Is.EqualTo([10]));
    }

    [Test]
    public async Task ExecuteAsync_DropsTheCrewCredit_WhenTheJobIsOutsideTheDirectorVocabulary()
    {
        // Arrange
        _cache.Setup(cache => cache.GetCreditsAsync(
                "3/person/1/tv_credits?language=en-US",
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Func<It.IsAnyType, CancellationToken, Task<CreditResponse>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreditResponse
            {
                Tv = new PersonTvCreditsDto
                {
                    Crew = [new TvCreditDto { Id = 10, Name = "Credited", Job = "Assistant Director" }]
                }
            });
        _cache.Setup(cache => cache.GetSubjectResolutionsAsync(
                It.IsAny<string>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Func<It.IsAnyType, CancellationToken, Task<IReadOnlyList<SubjectResolution>>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SubjectResolution { Kind = SubjectResolutionKind.Resolved, Id = 1, Name = "Alice" }]);
        var binding = new SubjectResolutionBinding(
            "person-0",
            new SubjectFilter(
                false,
                [new SubjectAlternative([SubjectRole.Director], [[new SubjectIdentity("Alice", null, null)]])]));
        var plan = new QueryPlan
        {
            Root = new FetchPlanNode
            {
                Id = 2,
                Precondition = PlanPrecondition.None,
                Contributes = "fetch",
                Route = ContentRoute.TvShow,
                AnchorKind = QueryAnchorKind.PersonCredits,
                SubjectBindings = [binding],
                PushedFilters = [],
                RequestedPage = 1,
                Fingerprint = "fetch-2",
                Resolve = new ResolvePlanNode
                {
                    Id = 1,
                    Precondition = PlanPrecondition.None,
                    Contributes = "resolve",
                    Route = ContentRoute.TvShow,
                    SubjectBindings = [binding],
                    GenreFilters = [],
                    ResolveImageBase = false
                }
            },
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 25,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };

        // Act
        var execution = await _subject.ExecuteAsync(
            plan,
            new RequestContext { ApiKey = "key", Language = "en-US" },
            default).ConfigureAwait(false);
        var hits = await execution.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        Assert.That(hits, Is.Empty);
    }

    [Test]
    public async Task ExecuteAsync_KeepsTheCrewCredit_WhenTheJobIsInsideTheWriterVocabulary()
    {
        // Arrange
        _cache.Setup(cache => cache.GetCreditsAsync(
                "3/person/1/tv_credits?language=en-US",
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Func<It.IsAnyType, CancellationToken, Task<CreditResponse>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreditResponse
            {
                Tv = new PersonTvCreditsDto
                {
                    Crew = [new TvCreditDto { Id = 10, Name = "Credited", Job = "Story" }]
                }
            });
        _cache.Setup(cache => cache.GetSubjectResolutionsAsync(
                It.IsAny<string>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Func<It.IsAnyType, CancellationToken, Task<IReadOnlyList<SubjectResolution>>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SubjectResolution { Kind = SubjectResolutionKind.Resolved, Id = 1, Name = "Alice" }]);
        var binding = new SubjectResolutionBinding(
            "person-0",
            new SubjectFilter(
                false,
                [new SubjectAlternative([SubjectRole.Writer], [[new SubjectIdentity("Alice", null, null)]])]));
        var plan = new QueryPlan
        {
            Root = new FetchPlanNode
            {
                Id = 2,
                Precondition = PlanPrecondition.None,
                Contributes = "fetch",
                Route = ContentRoute.TvShow,
                AnchorKind = QueryAnchorKind.PersonCredits,
                SubjectBindings = [binding],
                PushedFilters = [],
                RequestedPage = 1,
                Fingerprint = "fetch-2",
                Resolve = new ResolvePlanNode
                {
                    Id = 1,
                    Precondition = PlanPrecondition.None,
                    Contributes = "resolve",
                    Route = ContentRoute.TvShow,
                    SubjectBindings = [binding],
                    GenreFilters = [],
                    ResolveImageBase = false
                }
            },
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 25,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };

        // Act
        var execution = await _subject.ExecuteAsync(
            plan,
            new RequestContext { ApiKey = "key", Language = "en-US" },
            default).ConfigureAwait(false);
        var hits = await execution.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        Assert.That(hits, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task ExecuteAsync_ContinuesFromCachedScanState_WhenTheSameFetchRunsTwice()
    {
        // Arrange
        _client.SetupSequence(client => client.GetAsync<SearchEnvelopeDto<MovieSearchHitDto>>(
                It.IsAny<RequestContext>(),
                It.IsAny<Request>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchEnvelopeDto<MovieSearchHitDto>
            {
                Page = 1,
                TotalPages = 2,
                TotalResults = 2,
                Results = [new MovieSearchHitDto { Id = 10, Title = "First" }]
            })
            .ReturnsAsync(new SearchEnvelopeDto<MovieSearchHitDto>
            {
                Page = 2,
                TotalPages = 2,
                TotalResults = 2,
                Results = [new MovieSearchHitDto { Id = 11, Title = "Second" }]
            });
        var plan = new QueryPlan
        {
            Root = new FetchPlanNode
            {
                Id = 1,
                Precondition = PlanPrecondition.None,
                Contributes = "fetch",
                Route = ContentRoute.Movie,
                AnchorKind = QueryAnchorKind.Search,
                SubjectBindings = [],
                PushedFilters = [],
                RequestedPage = 1,
                Fingerprint = "fetch-1",
                TextFilters = [new TextFilter(TextField.Title, CriteriaValueOperator.Equals, false, ["Iron Man"])]
            },
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 25,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };

        // Act
        var firstExecution = await _subject.ExecuteAsync(
            plan,
            new RequestContext { ApiKey = "key", Language = "en-US" },
            default).ConfigureAwait(false);
        var first = await firstExecution.Hits.ToListAsync().ConfigureAwait(false);
        var secondExecution = await _subject.ExecuteAsync(
            plan,
            new RequestContext { ApiKey = "key", Language = "en-US" },
            default).ConfigureAwait(false);
        var second = await secondExecution.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Select(hit => hit.Id), Is.EqualTo([10, 11]));
            Assert.That(second.Select(hit => hit.Id), Is.EqualTo([10, 11]));
        }

        _client.Verify(
            client => client.GetAsync<SearchEnvelopeDto<MovieSearchHitDto>>(
                It.IsAny<RequestContext>(),
                It.IsAny<Request>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Test]
    public async Task ExecuteAsync_InterleavesSearchTerms_WhenTheFetchHasTitleAlternatives()
    {
        // Arrange
        _client.Setup(client => client.GetAsync<SearchEnvelopeDto<MovieSearchHitDto>>(
                It.IsAny<RequestContext>(),
                It.Is<Request>(request => request.CacheKey.Contains("query=Iron Man&", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchEnvelopeDto<MovieSearchHitDto>
            {
                Page = 1,
                TotalPages = 1,
                TotalResults = 2,
                Results =
                [
                    new MovieSearchHitDto { Id = 10, Title = "Iron Man" },
                    new MovieSearchHitDto { Id = 11, Title = "Iron Man rerelease" }
                ]
            });
        _client.Setup(client => client.GetAsync<SearchEnvelopeDto<MovieSearchHitDto>>(
                It.IsAny<RequestContext>(),
                It.Is<Request>(request => request.CacheKey.Contains("query=Iron Man 2&", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchEnvelopeDto<MovieSearchHitDto>
            {
                Page = 1,
                TotalPages = 1,
                TotalResults = 1,
                Results = [new MovieSearchHitDto { Id = 20, Title = "Iron Man 2" }]
            });
        var plan = new QueryPlan
        {
            Root = new FetchPlanNode
            {
                Id = 1,
                Precondition = PlanPrecondition.None,
                Contributes = "fetch",
                Route = ContentRoute.Movie,
                AnchorKind = QueryAnchorKind.Search,
                SubjectBindings = [],
                PushedFilters = [],
                RequestedPage = 1,
                Fingerprint = "fetch-1",
                TextFilters =
                [
                    new TextFilter(TextField.Title, CriteriaValueOperator.Equals, false, ["Iron Man", "Iron Man 2"])
                ]
            },
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 25,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };

        // Act
        var execution = await _subject.ExecuteAsync(
            plan,
            new RequestContext { ApiKey = "key", Language = "en-US" },
            default).ConfigureAwait(false);
        var hits = await execution.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        Assert.That(hits.Select(hit => hit.Id), Is.EqualTo([10, 20, 11]));
    }

    [Test]
    public async Task ExecuteAsync_FallsThroughToTheNextTerm_WhenTheFirstSearchReturnsNothing()
    {
        // Arrange
        _client.Setup(client => client.GetAsync<SearchEnvelopeDto<MovieSearchHitDto>>(
                It.IsAny<RequestContext>(),
                It.Is<Request>(request => request.CacheKey.Contains("query=Missing&", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchEnvelopeDto<MovieSearchHitDto>
            {
                Page = 1,
                TotalPages = 1,
                TotalResults = 0,
                Results = []
            });
        _client.Setup(client => client.GetAsync<SearchEnvelopeDto<MovieSearchHitDto>>(
                It.IsAny<RequestContext>(),
                It.Is<Request>(request => request.CacheKey.Contains("query=Iron Man&", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchEnvelopeDto<MovieSearchHitDto>
            {
                Page = 1,
                TotalPages = 1,
                TotalResults = 1,
                Results = [new MovieSearchHitDto { Id = 1726, Title = "Iron Man" }]
            });
        var plan = new QueryPlan
        {
            Root = new FetchPlanNode
            {
                Id = 1,
                Precondition = PlanPrecondition.None,
                Contributes = "fetch",
                Route = ContentRoute.Movie,
                AnchorKind = QueryAnchorKind.Search,
                SubjectBindings = [],
                PushedFilters = [],
                RequestedPage = 1,
                Fingerprint = "fetch-1",
                TextFilters =
                [
                    new TextFilter(TextField.Title, CriteriaValueOperator.Equals, false, ["Missing", "Iron Man"])
                ]
            },
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 25,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };

        // Act
        var execution = await _subject.ExecuteAsync(
            plan,
            new RequestContext { ApiKey = "key", Language = "en-US" },
            default).ConfigureAwait(false);
        var hits = await execution.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        Assert.That(hits.Select(hit => hit.Id), Is.EqualTo([1726]));
    }

    [Test]
    public async Task ExecuteAsync_SendsTitleYearLanguageAndPageInOneRequest_WhenTheMovieAnchorIsSearch()
    {
        // Arrange
        var fetch = new FetchPlanNode
        {
            Id = 1,
            Precondition = PlanPrecondition.None,
            Contributes = "fetch",
            Route = ContentRoute.Movie,
            AnchorKind = QueryAnchorKind.Search,
            SubjectBindings = [],
            PushedFilters = [],
            RequestedPage = 2,
            Fingerprint = "fetch",
            OrdinarySearchTerm = "iron man",
            TextFilters = [new TextFilter(TextField.Title, CriteriaValueOperator.Equals, false, ["iron man"])],
            PublishDateFilters =
            [
                new TemporalFilter(
                    CriteriaValueOperator.Equals,
                    false,
                    [
                        new TemporalValue
                        {
                            Value = new DateTime(2008, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                            Precision = TemporalPrecision.Year
                        }
                    ])
            ]
        };
        var plan = new QueryPlan
        {
            Root = fetch,
            Warnings = [],
            RequestedPage = 2,
            MaximumResults = 25,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };

        // Act
        var execution = await _subject.ExecuteAsync(plan, Context, default).ConfigureAwait(false);
        await execution.Hits.ToListAsync().ConfigureAwait(false);
        var request = _requests.Single();
        var parameters = request.QueryParameters.ToDictionary(pair => pair.Key, pair => pair.Value);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(request.Path, Is.EqualTo("3/search/movie"));
            Assert.That(parameters["query"], Is.EqualTo("iron man"));
            Assert.That(parameters["primary_release_year"], Is.EqualTo("2008"));
            Assert.That(parameters["language"], Is.EqualTo("en-US"));
            Assert.That(parameters["page"], Is.EqualTo("2"));
            Assert.That(request.CacheKey, Does.Not.Contain("api_key"));
        }
    }

    [Test]
    public async Task ExecuteAsync_SearchesEveryTitleAlternative_WhenTheTitleHasAlternatives()
    {
        // Arrange
        var fetch = new FetchPlanNode
        {
            Id = 1,
            Precondition = PlanPrecondition.None,
            Contributes = "fetch",
            Route = ContentRoute.Movie,
            AnchorKind = QueryAnchorKind.Search,
            SubjectBindings = [],
            PushedFilters = [],
            RequestedPage = 1,
            Fingerprint = "fetch",
            TextFilters =
            [
                new TextFilter(TextField.Title, CriteriaValueOperator.Equals, false, ["Missing", "The Matrix"])
            ]
        };
        var plan = new QueryPlan
        {
            Root = fetch,
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 25,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };

        // Act
        var execution = await _subject.ExecuteAsync(plan, Context, default).ConfigureAwait(false);
        await execution.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        Assert.That(
            _requests.Select(request => request.QueryParameters.Single(pair => pair.Key == "query").Value),
            Is.EqualTo(["Missing", "The Matrix"]));
    }

    [Test]
    public async Task ExecuteAsync_SendsInclusiveMonthBounds_WhenThePublishDateIsAMonthEquality()
    {
        // Arrange
        var fetch = new FetchPlanNode
        {
            Id = 1,
            Precondition = PlanPrecondition.None,
            Contributes = "fetch",
            Route = ContentRoute.TvShow,
            AnchorKind = QueryAnchorKind.Discover,
            SubjectBindings = [],
            PushedFilters = [],
            RequestedPage = 1,
            Fingerprint = "fetch",
            PublishDateFilters =
            [
                new TemporalFilter(
                    CriteriaValueOperator.Equals,
                    false,
                    [
                        new TemporalValue
                        {
                            Value = new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                            Precision = TemporalPrecision.Month
                        }
                    ])
            ]
        };
        var plan = new QueryPlan
        {
            Root = fetch,
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 25,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };

        // Act
        var execution = await _subject.ExecuteAsync(plan, Context, default).ConfigureAwait(false);
        await execution.Hits.ToListAsync().ConfigureAwait(false);
        var parameters = _requests.Single().QueryParameters.ToDictionary(pair => pair.Key, pair => pair.Value);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(parameters["first_air_date.gte"], Is.EqualTo("2024-02-01"));
            Assert.That(parameters["first_air_date.lte"], Is.EqualTo("2024-02-29"));
            Assert.That(parameters, Does.Not.ContainKey("year"));
        }
    }

    [TestCase(CriteriaValueOperator.GreaterThan, "primary_release_date.gte", "2025-01-01")]
    [TestCase(CriteriaValueOperator.GreaterThanOrEqual, "primary_release_date.gte", "2024-01-01")]
    [TestCase(CriteriaValueOperator.LessThan, "primary_release_date.lte", "2023-12-31")]
    [TestCase(CriteriaValueOperator.LessThanOrEqual, "primary_release_date.lte", "2024-12-31")]
    public async Task ExecuteAsync_SendsExactPeriodBoundaries_WhenTheYearIsAnInequality(
        CriteriaValueOperator op,
        string expectedKey,
        string expectedValue)
    {
        // Arrange
        var fetch = new FetchPlanNode
        {
            Id = 1,
            Precondition = PlanPrecondition.None,
            Contributes = "fetch",
            Route = ContentRoute.Movie,
            AnchorKind = QueryAnchorKind.Discover,
            SubjectBindings = [],
            PushedFilters = [],
            RequestedPage = 1,
            Fingerprint = "fetch",
            PublishDateFilters =
            [
                new TemporalFilter(
                    op,
                    false,
                    [
                        new TemporalValue
                        {
                            Value = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                            Precision = TemporalPrecision.Year
                        }
                    ])
            ]
        };
        var plan = new QueryPlan
        {
            Root = fetch,
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 25,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };

        // Act
        var execution = await _subject.ExecuteAsync(plan, Context, default).ConfigureAwait(false);
        await execution.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        Assert.That(
            _requests.Single().QueryParameters.Single(pair => pair.Key == expectedKey).Value,
            Is.EqualTo(expectedValue));
    }

    [Test]
    public async Task ExecuteAsync_SendsBothIntegerMinuteBounds_WhenTheRuntimeIsAnEquality()
    {
        // Arrange
        var fetch = new FetchPlanNode
        {
            Id = 1,
            Precondition = PlanPrecondition.None,
            Contributes = "fetch",
            Route = ContentRoute.Movie,
            AnchorKind = QueryAnchorKind.Discover,
            SubjectBindings = [],
            PushedFilters = [],
            RequestedPage = 1,
            Fingerprint = "fetch",
            DurationFilters =
            [
                new DurationFilter(CriteriaValueOperator.Equals, false, [TimeSpan.FromMinutes(42)])
            ]
        };
        var plan = new QueryPlan
        {
            Root = fetch,
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 25,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };

        // Act
        var execution = await _subject.ExecuteAsync(plan, Context, default).ConfigureAwait(false);
        var hits = await execution.Hits.ToListAsync().ConfigureAwait(false);
        var parameters = _requests.Single().QueryParameters.ToDictionary(pair => pair.Key, pair => pair.Value);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(parameters["with_runtime.gte"], Is.EqualTo("42"));
            Assert.That(parameters["with_runtime.lte"], Is.EqualTo("42"));
            Assert.That(
                hits.Single().AppliedCriteria!.AppliedDurationFilters,
                Is.EquivalentTo(fetch.DurationFilters));
        }
    }

    [Test]
    public async Task ExecuteAsync_SendsDistinctDocumentedParameters_WhenMovieRelationshipRolesDiffer()
    {
        // Arrange
        var bindings = new[]
        {
            new SubjectResolutionBinding(
                "company",
                new SubjectFilter(
                    false,
                    [
                        new SubjectAlternative(
                            [SubjectRole.Company],
                            [[new SubjectIdentity("Marvel Studios", "tmdb", "420")]])
                    ])),
            new SubjectResolutionBinding(
                "cast",
                new SubjectFilter(
                    false,
                    [
                        new SubjectAlternative(
                            [SubjectRole.Cast],
                            [[new SubjectIdentity("Robert Downey Jr.", "tmdb", "3223")]])
                    ])),
            new SubjectResolutionBinding(
                "crew",
                new SubjectFilter(
                    false,
                    [
                        new SubjectAlternative(
                            [SubjectRole.Director],
                            [[new SubjectIdentity("Christopher Nolan", "tmdb", "525")]])
                    ])),
            new SubjectResolutionBinding(
                "person",
                new SubjectFilter(
                    false,
                    [new SubjectAlternative([SubjectRole.Person], [[new SubjectIdentity("Someone", "tmdb", "12")]])]))
        };
        var resolve = new ResolvePlanNode
        {
            Id = 1,
            Precondition = PlanPrecondition.None,
            Contributes = "resolve",
            Route = ContentRoute.Movie,
            SubjectBindings = bindings,
            GenreFilters = [],
            ResolveImageBase = false
        };
        var fetch = new FetchPlanNode
        {
            Id = 2,
            Precondition = PlanPrecondition.None,
            Contributes = "fetch",
            Route = ContentRoute.Movie,
            AnchorKind = QueryAnchorKind.Discover,
            SubjectBindings = bindings,
            PushedFilters = [],
            RequestedPage = 1,
            Fingerprint = "fetch",
            Resolve = resolve,
            DiscoverParameters =
            [
                new DiscoverParameter
                {
                    Name = "with_companies",
                    Combination = DiscoverCombination.Or,
                    SubjectBindingKeys = ["company"]
                },
                new DiscoverParameter
                {
                    Name = "with_cast",
                    Combination = DiscoverCombination.Or,
                    SubjectBindingKeys = ["cast"]
                },
                new DiscoverParameter
                {
                    Name = "with_crew",
                    Combination = DiscoverCombination.Or,
                    SubjectBindingKeys = ["crew"]
                },
                new DiscoverParameter
                {
                    Name = "with_people",
                    Combination = DiscoverCombination.Or,
                    SubjectBindingKeys = ["person"]
                }
            ]
        };
        var plan = new QueryPlan
        {
            Root = fetch,
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 25,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };

        // Act
        var execution = await _subject.ExecuteAsync(plan, Context, default).ConfigureAwait(false);
        var hits = await execution.Hits.ToListAsync().ConfigureAwait(false);
        var parameters = _requests.Single().QueryParameters.ToDictionary(pair => pair.Key, pair => pair.Value);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(parameters["with_companies"], Is.EqualTo("420"));
            Assert.That(parameters["with_cast"], Is.EqualTo("3223"));
            Assert.That(parameters["with_crew"], Is.EqualTo("525"));
            Assert.That(parameters["with_people"], Is.EqualTo("12"));
            Assert.That(
                hits.Single().AppliedCriteria!.AppliedSubjectGroups.Select(group => group.BindingKey),
                Is.EquivalentTo(["company", "cast", "person"]));
        }
    }

    [Test]
    public async Task ExecuteAsync_SendsWithNetworks_WhenOneNetworkIdResolvesOnTheTvRoute()
    {
        // Arrange
        var bindings = new[]
        {
            new SubjectResolutionBinding(
                "network",
                new SubjectFilter(
                    false,
                    [new SubjectAlternative([SubjectRole.Network], [[new SubjectIdentity("HBO", "tmdb", "49")]])]))
        };
        var resolve = new ResolvePlanNode
        {
            Id = 1,
            Precondition = PlanPrecondition.None,
            Contributes = "resolve",
            Route = ContentRoute.TvShow,
            SubjectBindings = bindings,
            GenreFilters = [],
            ResolveImageBase = false
        };
        var fetch = new FetchPlanNode
        {
            Id = 2,
            Precondition = PlanPrecondition.None,
            Contributes = "fetch",
            Route = ContentRoute.TvShow,
            AnchorKind = QueryAnchorKind.Discover,
            SubjectBindings = bindings,
            PushedFilters = [],
            RequestedPage = 1,
            Fingerprint = "fetch",
            Resolve = resolve,
            DiscoverParameters =
            [
                new DiscoverParameter
                {
                    Name = "with_networks",
                    Combination = DiscoverCombination.Or,
                    SubjectBindingKeys = ["network"]
                }
            ]
        };
        var plan = new QueryPlan
        {
            Root = fetch,
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 25,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };

        // Act
        var execution = await _subject.ExecuteAsync(plan, Context, default).ConfigureAwait(false);
        var hits = await execution.Hits.ToListAsync().ConfigureAwait(false);
        var parameters = _requests.Single().QueryParameters.ToDictionary(pair => pair.Key, pair => pair.Value);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(parameters["with_networks"], Is.EqualTo("49"));
            Assert.That(
                hits.Single().AppliedCriteria!.AppliedSubjectGroups.Select(group => group.BindingKey),
                Is.EquivalentTo(["network"]));
        }
    }

    [Test]
    public async Task ExecuteAsync_OmitsWithNetworks_WhenSeveralNetworkIdsResolve()
    {
        // Arrange
        var bindings = new[]
        {
            new SubjectResolutionBinding(
                "network",
                new SubjectFilter(
                    false,
                    [
                        new SubjectAlternative(
                            [SubjectRole.Network],
                            [
                                [
                                    new SubjectIdentity("HBO", "tmdb", "49"),
                                    new SubjectIdentity("Netflix", "tmdb", "213")
                                ]
                            ])
                    ]))
        };
        var resolve = new ResolvePlanNode
        {
            Id = 1,
            Precondition = PlanPrecondition.None,
            Contributes = "resolve",
            Route = ContentRoute.TvShow,
            SubjectBindings = bindings,
            GenreFilters = [],
            ResolveImageBase = false
        };
        var fetch = new FetchPlanNode
        {
            Id = 2,
            Precondition = PlanPrecondition.None,
            Contributes = "fetch",
            Route = ContentRoute.TvShow,
            AnchorKind = QueryAnchorKind.Discover,
            SubjectBindings = bindings,
            PushedFilters = [],
            RequestedPage = 1,
            Fingerprint = "fetch",
            Resolve = resolve,
            DiscoverParameters =
            [
                new DiscoverParameter
                {
                    Name = "with_networks",
                    Combination = DiscoverCombination.Or,
                    SubjectBindingKeys = ["network"]
                }
            ]
        };
        var plan = new QueryPlan
        {
            Root = fetch,
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 25,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };

        // Act
        var execution = await _subject.ExecuteAsync(plan, Context, default).ConfigureAwait(false);
        var hits = await execution.Hits.ToListAsync().ConfigureAwait(false);
        var parameters = _requests.Single().QueryParameters.ToDictionary(pair => pair.Key, pair => pair.Value);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(parameters, Does.Not.ContainKey("with_networks"));
            Assert.That(hits.Single().AppliedCriteria!.AppliedSubjectGroups, Is.Empty);
        }
    }

    [Test]
    public async Task ExecuteAsync_SendsWithCompaniesUnapplied_WhenTheNetworkResolvedViaCompanySearch()
    {
        // Arrange
        _cache.Setup(cache => cache.GetSubjectResolutionsAsync(
                It.IsAny<string>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Func<It.IsAnyType, CancellationToken, Task<IReadOnlyList<SubjectResolution>>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new SubjectResolution
                {
                    Kind = SubjectResolutionKind.Resolved,
                    Id = 3268,
                    Name = "HBO",
                    ViaCompanySearch = true
                }
            ]);
        var bindings = new[]
        {
            new SubjectResolutionBinding(
                "network",
                new SubjectFilter(
                    false,
                    [new SubjectAlternative([SubjectRole.Network], [[new SubjectIdentity("HBO", null, null)]])]))
        };
        var resolve = new ResolvePlanNode
        {
            Id = 1,
            Precondition = PlanPrecondition.None,
            Contributes = "resolve",
            Route = ContentRoute.TvShow,
            SubjectBindings = bindings,
            GenreFilters = [],
            ResolveImageBase = false
        };
        var fetch = new FetchPlanNode
        {
            Id = 2,
            Precondition = PlanPrecondition.None,
            Contributes = "fetch",
            Route = ContentRoute.TvShow,
            AnchorKind = QueryAnchorKind.Discover,
            SubjectBindings = bindings,
            PushedFilters = [],
            RequestedPage = 1,
            Fingerprint = "fetch",
            Resolve = resolve,
            DiscoverParameters =
            [
                new DiscoverParameter
                {
                    Name = "with_companies",
                    Combination = DiscoverCombination.Or,
                    SubjectBindingKeys = ["network"]
                }
            ]
        };
        var plan = new QueryPlan
        {
            Root = fetch,
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 25,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };

        // Act
        var execution = await _subject.ExecuteAsync(plan, Context, default).ConfigureAwait(false);
        var hits = await execution.Hits.ToListAsync().ConfigureAwait(false);
        var parameters = _requests.Single().QueryParameters.ToDictionary(pair => pair.Key, pair => pair.Value);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(parameters["with_companies"], Is.EqualTo("3268"));
            Assert.That(hits.Single().AppliedCriteria!.AppliedSubjectGroups, Is.Empty);
        }
    }

    [Test]
    public async Task ExecuteAsync_OmitsNetworkParameters_WhenTheRouteIsMovie()
    {
        // Arrange
        var bindings = new[]
        {
            new SubjectResolutionBinding(
                "network",
                new SubjectFilter(
                    false,
                    [new SubjectAlternative([SubjectRole.Network], [[new SubjectIdentity("HBO", "tmdb", "49")]])]))
        };
        var resolve = new ResolvePlanNode
        {
            Id = 1,
            Precondition = PlanPrecondition.None,
            Contributes = "resolve",
            Route = ContentRoute.Movie,
            SubjectBindings = bindings,
            GenreFilters = [],
            ResolveImageBase = false
        };
        var fetch = new FetchPlanNode
        {
            Id = 2,
            Precondition = PlanPrecondition.None,
            Contributes = "fetch",
            Route = ContentRoute.Movie,
            AnchorKind = QueryAnchorKind.Discover,
            SubjectBindings = bindings,
            PushedFilters = [],
            RequestedPage = 1,
            Fingerprint = "fetch",
            Resolve = resolve
        };
        var plan = new QueryPlan
        {
            Root = fetch,
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 25,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };

        // Act
        var execution = await _subject.ExecuteAsync(plan, Context, default).ConfigureAwait(false);
        var hits = await execution.Hits.ToListAsync().ConfigureAwait(false);
        var parameters = _requests.Single().QueryParameters.ToDictionary(pair => pair.Key, pair => pair.Value);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(parameters, Does.Not.ContainKey("with_networks"));
            Assert.That(parameters, Does.Not.ContainKey("with_companies"));
            Assert.That(hits.Single().AppliedCriteria!.AppliedSubjectGroups, Is.Empty);
        }
    }

    [Test]
    public async Task ExecuteAsync_OmitsSubjectParameters_WhenOneGroupMixesRoles()
    {
        // Arrange
        var bindings = new[]
        {
            new SubjectResolutionBinding(
                "mixed",
                new SubjectFilter(
                    false,
                    [
                        new SubjectAlternative(
                            [SubjectRole.Cast, SubjectRole.Company],
                            [[new SubjectIdentity("Both", "tmdb", "1")]])
                    ]))
        };
        var resolve = new ResolvePlanNode
        {
            Id = 1,
            Precondition = PlanPrecondition.None,
            Contributes = "resolve",
            Route = ContentRoute.Movie,
            SubjectBindings = bindings,
            GenreFilters = [],
            ResolveImageBase = false
        };
        var fetch = new FetchPlanNode
        {
            Id = 2,
            Precondition = PlanPrecondition.None,
            Contributes = "fetch",
            Route = ContentRoute.Movie,
            AnchorKind = QueryAnchorKind.Discover,
            SubjectBindings = bindings,
            PushedFilters = [],
            RequestedPage = 1,
            Fingerprint = "fetch",
            Resolve = resolve
        };
        var plan = new QueryPlan
        {
            Root = fetch,
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 25,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };

        // Act
        var execution = await _subject.ExecuteAsync(plan, Context, default).ConfigureAwait(false);
        await execution.Hits.ToListAsync().ConfigureAwait(false);
        var parameters = _requests.Single().QueryParameters.ToDictionary(pair => pair.Key, pair => pair.Value);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(parameters, Does.Not.ContainKey("with_cast"));
            Assert.That(parameters, Does.Not.ContainKey("with_companies"));
        }
    }

    [Test]
    public async Task ExecuteAsync_SendsOnlyTheCriteriaItCanPush_WhenSomeCriteriaStayResidual()
    {
        // Arrange
        var fetch = new FetchPlanNode
        {
            Id = 1,
            Precondition = PlanPrecondition.None,
            Contributes = "fetch",
            Route = ContentRoute.Movie,
            AnchorKind = QueryAnchorKind.Discover,
            SubjectBindings = [],
            PushedFilters = [],
            RequestedPage = 1,
            Fingerprint = "fetch",
            TextFilters = [new TextFilter(TextField.Language, CriteriaValueOperator.In, false, ["en", "fr"])],
            PublishDateFilters =
            [
                new TemporalFilter(
                    CriteriaValueOperator.GreaterThan,
                    false,
                    [
                        new TemporalValue
                        {
                            Value = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                            Precision = TemporalPrecision.Year
                        }
                    ]),
                new TemporalFilter(
                    CriteriaValueOperator.LessThanOrEqual,
                    false,
                    [
                        new TemporalValue
                        {
                            Value = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                            Precision = TemporalPrecision.Year
                        }
                    ])
            ],
            DurationFilters =
            [
                new DurationFilter(CriteriaValueOperator.GreaterThanOrEqual, false, [TimeSpan.FromMinutes(90)]),
                new DurationFilter(CriteriaValueOperator.LessThanOrEqual, false, [TimeSpan.FromMinutes(180)])
            ]
        };
        var plan = new QueryPlan
        {
            Root = fetch,
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 25,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };

        // Act
        var execution = await _subject.ExecuteAsync(plan, Context, default).ConfigureAwait(false);
        var hits = await execution.Hits.ToListAsync().ConfigureAwait(false);
        var applied = hits.Single().AppliedCriteria!;
        var parameters = _requests.Single().QueryParameters.ToDictionary(pair => pair.Key, pair => pair.Value);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(parameters, Does.Not.ContainKey("with_original_language"));
            Assert.That(parameters["primary_release_date.gte"], Is.EqualTo("2021-01-01"));
            Assert.That(parameters["primary_release_date.lte"], Is.EqualTo("2025-12-31"));
            Assert.That(parameters["with_runtime.gte"], Is.EqualTo("90"));
            Assert.That(parameters["with_runtime.lte"], Is.EqualTo("180"));
            Assert.That(applied.AppliedTextFilters, Is.Empty);
            Assert.That(applied.AppliedPublishDateFilters, Is.EquivalentTo(fetch.PublishDateFilters));
            Assert.That(applied.AppliedDurationFilters, Is.EquivalentTo(fetch.DurationFilters));
        }
    }

    [Test]
    public async Task ExecuteAsync_SendsResolvedGenreAlternatives_WhenGenreNamesHaveConjunctivePredicates()
    {
        // Arrange
        _genres["Action"] = 28;
        _genres["Drama"] = 18;
        _genres["Comedy"] = 35;
        var genre = new GenreFilter(
            false,
            [
                [
                    new GenrePredicate(CriteriaValueOperator.StartsWithString, false, ["Act"]),
                    new GenrePredicate(CriteriaValueOperator.EndsWithString, false, ["ion"])
                ],
                [new GenrePredicate(CriteriaValueOperator.Equals, false, ["Drama"])]
            ]);
        var fetch = new FetchPlanNode
        {
            Id = 2,
            Precondition = PlanPrecondition.None,
            Contributes = "fetch",
            Route = ContentRoute.Movie,
            AnchorKind = QueryAnchorKind.Discover,
            SubjectBindings = [],
            PushedFilters = [],
            RequestedPage = 1,
            Fingerprint = "fetch",
            Resolve = new ResolvePlanNode
            {
                Id = 1,
                Precondition = PlanPrecondition.None,
                Contributes = "resolve",
                Route = ContentRoute.Movie,
                SubjectBindings = [],
                GenreFilters = [genre],
                ResolveImageBase = false
            },
            DiscoverParameters =
            [
                new DiscoverParameter
                {
                    Name = "with_genres",
                    Combination = DiscoverCombination.Or,
                    GenreFilters = [genre]
                }
            ]
        };
        var plan = new QueryPlan
        {
            Root = fetch,
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 25,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };

        // Act
        var execution = await _subject.ExecuteAsync(plan, Context, default).ConfigureAwait(false);
        await execution.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        Assert.That(
            _requests.Single().QueryParameters.Single(pair => pair.Key == "with_genres").Value,
            Is.EqualTo("28|18"));
    }

    [Test]
    public async Task ExecuteAsync_SendsCommaSeparatedGenreIds_WhenGenresUseAndComposition()
    {
        // Arrange
        _genres["Action"] = 28;
        _genres["Drama"] = 18;
        var action = new GenreFilter(false, [[new GenrePredicate(CriteriaValueOperator.Equals, false, ["Action"])]]);
        var drama = new GenreFilter(false, [[new GenrePredicate(CriteriaValueOperator.Equals, false, ["Drama"])]]);
        var fetch = new FetchPlanNode
        {
            Id = 2,
            Precondition = PlanPrecondition.None,
            Contributes = "fetch",
            Route = ContentRoute.Movie,
            AnchorKind = QueryAnchorKind.Discover,
            SubjectBindings = [],
            PushedFilters = [],
            RequestedPage = 1,
            Fingerprint = "fetch",
            Resolve = new ResolvePlanNode
            {
                Id = 1,
                Precondition = PlanPrecondition.None,
                Contributes = "resolve",
                Route = ContentRoute.Movie,
                SubjectBindings = [],
                GenreFilters = [action, drama],
                ResolveImageBase = false
            },
            DiscoverParameters =
            [
                new DiscoverParameter
                {
                    Name = "with_genres",
                    Combination = DiscoverCombination.And,
                    GenreFilters = [action, drama]
                }
            ]
        };
        var plan = new QueryPlan
        {
            Root = fetch,
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 25,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };

        // Act
        var execution = await _subject.ExecuteAsync(plan, Context, default).ConfigureAwait(false);
        await execution.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        Assert.That(
            _requests.Single().QueryParameters.Single(pair => pair.Key == "with_genres").Value,
            Is.EqualTo("28,18"));
    }

    [TestCase("with_people")]
    [TestCase("with_companies")]
    [TestCase("with_cast")]
    [TestCase("with_crew")]
    public async Task ExecuteAsync_JoinsTheSubjectIdsWithCommas_WhenTheParameterCombinesWithAnd(string parameter)
    {
        // Arrange
        var bindings = new[]
        {
            new SubjectResolutionBinding(
                "first",
                new SubjectFilter(
                    false,
                    [new SubjectAlternative([SubjectRole.Person], [[new SubjectIdentity("First", "tmdb", "11")]])])),
            new SubjectResolutionBinding(
                "second",
                new SubjectFilter(
                    false,
                    [new SubjectAlternative([SubjectRole.Person], [[new SubjectIdentity("Second", "tmdb", "22")]])]))
        };
        var resolve = new ResolvePlanNode
        {
            Id = 1,
            Precondition = PlanPrecondition.None,
            Contributes = "resolve",
            Route = ContentRoute.Movie,
            SubjectBindings = bindings,
            GenreFilters = [],
            ResolveImageBase = false
        };
        var fetch = new FetchPlanNode
        {
            Id = 2,
            Precondition = PlanPrecondition.None,
            Contributes = "fetch",
            Route = ContentRoute.Movie,
            AnchorKind = QueryAnchorKind.Discover,
            SubjectBindings = bindings,
            PushedFilters = [],
            RequestedPage = 1,
            Fingerprint = "fetch",
            Resolve = resolve,
            DiscoverParameters =
            [
                new DiscoverParameter
                {
                    Name = parameter,
                    Combination = DiscoverCombination.And,
                    SubjectBindingKeys = ["first", "second"]
                }
            ]
        };
        var plan = new QueryPlan
        {
            Root = fetch,
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 25,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };

        // Act
        var execution = await _subject.ExecuteAsync(plan, Context, default).ConfigureAwait(false);
        await execution.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        Assert.That(
            _requests.Single().QueryParameters.Single(pair => pair.Key == parameter).Value,
            Is.EqualTo("11,22"));
    }

    [TestCase("with_people")]
    [TestCase("with_companies")]
    [TestCase("with_cast")]
    [TestCase("with_crew")]
    public async Task ExecuteAsync_JoinsTheSubjectIdsWithPipes_WhenTheParameterCombinesWithOr(string parameter)
    {
        // Arrange
        var bindings = new[]
        {
            new SubjectResolutionBinding(
                "first",
                new SubjectFilter(
                    false,
                    [
                        new SubjectAlternative(
                            [SubjectRole.Person],
                            [
                                [
                                    new SubjectIdentity("First", "tmdb", "11"),
                                    new SubjectIdentity("Second", "tmdb", "22")
                                ]
                            ])
                    ]))
        };
        var resolve = new ResolvePlanNode
        {
            Id = 1,
            Precondition = PlanPrecondition.None,
            Contributes = "resolve",
            Route = ContentRoute.Movie,
            SubjectBindings = bindings,
            GenreFilters = [],
            ResolveImageBase = false
        };
        var fetch = new FetchPlanNode
        {
            Id = 2,
            Precondition = PlanPrecondition.None,
            Contributes = "fetch",
            Route = ContentRoute.Movie,
            AnchorKind = QueryAnchorKind.Discover,
            SubjectBindings = bindings,
            PushedFilters = [],
            RequestedPage = 1,
            Fingerprint = "fetch",
            Resolve = resolve,
            DiscoverParameters =
            [
                new DiscoverParameter
                {
                    Name = parameter,
                    Combination = DiscoverCombination.Or,
                    SubjectBindingKeys = ["first"]
                }
            ]
        };
        var plan = new QueryPlan
        {
            Root = fetch,
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 25,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };

        // Act
        var execution = await _subject.ExecuteAsync(plan, Context, default).ConfigureAwait(false);
        await execution.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        Assert.That(
            _requests.Single().QueryParameters.Single(pair => pair.Key == parameter).Value,
            Is.EqualTo("11|22"));
    }

    [Test]
    public async Task ExecuteAsync_EncodesExternalIdentity_WhenValueContainsPathDelimiters()
    {
        // Arrange
        var fetch = new FetchPlanNode
        {
            Id = 1,
            Precondition = PlanPrecondition.None,
            Contributes = "fetch",
            Route = ContentRoute.Movie,
            AnchorKind = QueryAnchorKind.ContentIds,
            SubjectBindings = [],
            PushedFilters = [],
            RequestedPage = 1,
            Fingerprint = "fetch",
            ContentIdFilters = [new ContentIdFilter(false, [new ContentId("imdb", "tt123/evil?x#fragment")])]
        };
        var plan = new QueryPlan
        {
            Root = fetch,
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 25,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };

        // Act
        var execution = await _subject.ExecuteAsync(plan, Context, default).ConfigureAwait(false);
        await execution.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        Assert.That(_requests.Single().Path, Is.EqualTo("3/find/tt123%2Fevil%3Fx%23fragment"));
    }

    [Test]
    public async Task ExecuteAsync_AppendsTheRequestedElementSections_WhenTheDetailIsFetchedById()
    {
        // Arrange
        var fetch = new FetchPlanNode
        {
            Id = 1,
            Precondition = PlanPrecondition.None,
            Contributes = "fetch",
            Route = ContentRoute.Movie,
            AnchorKind = QueryAnchorKind.ContentIds,
            SubjectBindings = [],
            PushedFilters = [],
            RequestedPage = 1,
            Fingerprint = "fetch",
            ContentIdFilters = [new ContentIdFilter(false, [new ContentId("tmdb", "550")])],
            ElementFilters =
            [
                new ElementFilter
                {
                    Family = ElementFamily.Reference,
                    Excluded = false,
                    Alternatives = []
                },
                new ElementFilter
                {
                    Family = ElementFamily.AlternateText,
                    Excluded = false,
                    Alternatives = []
                },
                new ElementFilter
                {
                    Family = ElementFamily.ContentRating,
                    Excluded = false,
                    Alternatives = []
                },
                new ElementFilter
                {
                    Family = ElementFamily.Preview,
                    Excluded = false,
                    Alternatives = []
                },
                new ElementFilter
                {
                    Family = ElementFamily.Image,
                    Excluded = false,
                    Alternatives = []
                }
            ]
        };
        var plan = new QueryPlan
        {
            Root = fetch,
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 25,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };

        // Act
        var execution = await _subject.ExecuteAsync(plan, Context, default).ConfigureAwait(false);
        await execution.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        Assert.That(
            _requests.Single().QueryParameters.Single(pair => pair.Key == "append_to_response").Value,
            Is.EqualTo("external_ids,alternative_titles,release_dates,videos,images"));
    }

    [Test]
    public async Task ExecuteAsync_AppendsContentRatings_WhenTheTvDetailIsFetchedById()
    {
        // Arrange
        var fetch = new FetchPlanNode
        {
            Id = 1,
            Precondition = PlanPrecondition.None,
            Contributes = "fetch",
            Route = ContentRoute.TvShow,
            AnchorKind = QueryAnchorKind.ContentIds,
            SubjectBindings = [],
            PushedFilters = [],
            RequestedPage = 1,
            Fingerprint = "fetch",
            ContentIdFilters = [new ContentIdFilter(false, [new ContentId("tmdb", "42")])],
            ElementFilters =
            [
                new ElementFilter
                {
                    Family = ElementFamily.ContentRating,
                    Excluded = false,
                    Alternatives = []
                }
            ]
        };
        var plan = new QueryPlan
        {
            Root = fetch,
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 25,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };

        // Act
        var execution = await _subject.ExecuteAsync(plan, Context, default).ConfigureAwait(false);
        await execution.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        Assert.That(
            _requests.Single().QueryParameters.Single(pair => pair.Key == "append_to_response").Value,
            Is.EqualTo("content_ratings"));
    }

    [Test]
    public async Task ExecuteAsync_SendsCertification_WhenOneCountryAndRatingAreRequested()
    {
        // Arrange
        var fetch = new FetchPlanNode
        {
            Id = 1,
            Precondition = PlanPrecondition.None,
            Contributes = "fetch",
            Route = ContentRoute.Movie,
            AnchorKind = QueryAnchorKind.Discover,
            SubjectBindings = [],
            PushedFilters = [],
            RequestedPage = 1,
            Fingerprint = "fetch",
            ElementFilters =
            [
                new ElementFilter
                {
                    Family = ElementFamily.ContentRating,
                    Excluded = false,
                    Alternatives =
                    [
                        [
                            new ElementPredicate
                            {
                                Field = ElementField.RatingBody,
                                Operator = CriteriaValueOperator.Equals,
                                StringValues = ["us"]
                            },
                            new ElementPredicate
                            {
                                Field = ElementField.RatingValue,
                                Operator = CriteriaValueOperator.Equals,
                                StringValues = ["PG-13"]
                            }
                        ]
                    ]
                }
            ]
        };
        var plan = new QueryPlan
        {
            Root = fetch,
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 25,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };

        // Act
        var execution = await _subject.ExecuteAsync(plan, Context, default).ConfigureAwait(false);
        var hits = await execution.Hits.ToListAsync().ConfigureAwait(false);
        var parameters = _requests.Single().QueryParameters.ToDictionary(pair => pair.Key, pair => pair.Value);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(parameters["certification_country"], Is.EqualTo("US"));
            Assert.That(parameters["certification"], Is.EqualTo("PG-13"));
            Assert.That(
                hits.Single().AppliedCriteria!.AppliedElementFilters,
                Is.EquivalentTo(fetch.ElementFilters));
        }
    }

    [Test]
    public async Task ExecuteAsync_OmitsCertification_WhenTheRouteIsTv()
    {
        // Arrange
        var fetch = new FetchPlanNode
        {
            Id = 1,
            Precondition = PlanPrecondition.None,
            Contributes = "fetch",
            Route = ContentRoute.TvShow,
            AnchorKind = QueryAnchorKind.Discover,
            SubjectBindings = [],
            PushedFilters = [],
            RequestedPage = 1,
            Fingerprint = "fetch",
            ElementFilters =
            [
                new ElementFilter
                {
                    Family = ElementFamily.ContentRating,
                    Excluded = false,
                    Alternatives =
                    [
                        [
                            new ElementPredicate
                            {
                                Field = ElementField.RatingBody,
                                Operator = CriteriaValueOperator.Equals,
                                StringValues = ["US"]
                            },
                            new ElementPredicate
                            {
                                Field = ElementField.RatingValue,
                                Operator = CriteriaValueOperator.Equals,
                                StringValues = ["TV-MA"]
                            }
                        ]
                    ]
                }
            ]
        };
        var plan = new QueryPlan
        {
            Root = fetch,
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 25,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };

        // Act
        var execution = await _subject.ExecuteAsync(plan, Context, default).ConfigureAwait(false);
        var hits = await execution.Hits.ToListAsync().ConfigureAwait(false);
        var parameters = _requests.Single().QueryParameters.ToDictionary(pair => pair.Key, pair => pair.Value);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(parameters, Does.Not.ContainKey("certification"));
            Assert.That(parameters, Does.Not.ContainKey("certification_country"));
            Assert.That(hits.Single().AppliedCriteria!.AppliedElementFilters, Is.Empty);
        }
    }

    [Test]
    public async Task ExecuteAsync_OmitsCertification_WhenTheRatingHasAlternatives()
    {
        // Arrange
        var fetch = new FetchPlanNode
        {
            Id = 1,
            Precondition = PlanPrecondition.None,
            Contributes = "fetch",
            Route = ContentRoute.Movie,
            AnchorKind = QueryAnchorKind.Discover,
            SubjectBindings = [],
            PushedFilters = [],
            RequestedPage = 1,
            Fingerprint = "fetch",
            ElementFilters =
            [
                new ElementFilter
                {
                    Family = ElementFamily.ContentRating,
                    Excluded = false,
                    Alternatives =
                    [
                        [
                            new ElementPredicate
                            {
                                Field = ElementField.RatingBody,
                                Operator = CriteriaValueOperator.Equals,
                                StringValues = ["US"]
                            },
                            new ElementPredicate
                            {
                                Field = ElementField.RatingValue,
                                Operator = CriteriaValueOperator.In,
                                StringValues = ["PG", "PG-13"]
                            }
                        ]
                    ]
                }
            ]
        };
        var plan = new QueryPlan
        {
            Root = fetch,
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 25,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };

        // Act
        var execution = await _subject.ExecuteAsync(plan, Context, default).ConfigureAwait(false);
        var hits = await execution.Hits.ToListAsync().ConfigureAwait(false);
        var parameters = _requests.Single().QueryParameters.ToDictionary(pair => pair.Key, pair => pair.Value);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(parameters, Does.Not.ContainKey("certification"));
            Assert.That(hits.Single().AppliedCriteria!.AppliedElementFilters, Is.Empty);
        }
    }
}
