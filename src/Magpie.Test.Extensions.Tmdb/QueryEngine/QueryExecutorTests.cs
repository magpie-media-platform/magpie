using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Tmdb;
using Magpie.Extensions.Tmdb.Mapping;
using Magpie.Extensions.Tmdb.QueryEngine;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Sdk.Caching;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Tmdb.QueryEngine;

[TestFixture]
public class QueryExecutorTests
{
    private static readonly RequestContext Context = new() { ApiKey = "key", Language = "en-US" };

    private static readonly object[] NameSearchCases =
    [
        new object[] { CriteriaValueOperator.Equals, 5, "1|2|3|4|5" },
        new object[] { CriteriaValueOperator.ContainsString, 5, "1|2|3|4|5" }
    ];

    private List<Request> _requests = null!;
    private Dictionary<string, int> _genres = null!;
    private Mock<ITmdbClient> _client = null!;
    private Mock<IQueryCoordinator> _cache = null!;
    private Dictionary<string, QueryScanState> _scanStates = null!;
    private IQueryExecutor _subject = null!;

    private ICacheFactory _cacheFactory = null!;

    private IServiceScopeFactory _scopeFactory = null!;

    [SetUp]
    public void SetUp()
    {
        var cacheFactory = new Mock<ICacheFactory>();
        var caches = new Dictionary<string, ICache>(StringComparer.Ordinal);
        cacheFactory.Setup(factory => factory.GetOrCreate(It.IsAny<string>(), It.IsAny<Func<CachePolicy>>()))
            .Returns<string, Func<CachePolicy>?>((name, _) =>
            {
                if (caches.TryGetValue(name, out var existing))
                {
                    return existing;
                }

                var values = new Dictionary<string, object?>(StringComparer.Ordinal);
                var cache = new Mock<ICache>();
                cache.Setup(value => value.Set(It.IsAny<string>(), It.IsAny<It.IsAnyType>()))
                    .Callback(new InvocationAction(invocation =>
                        values[(string)invocation.Arguments[0]] = invocation.Arguments[1]));
                cache.Setup(value => value.TryGet(It.IsAny<string>(), out It.Ref<It.IsAnyType?>.IsAny))
                    .Returns(new InvocationFunc(invocation =>
                    {
                        var found = values.TryGetValue((string)invocation.Arguments[0], out var value);
                        ((object?[])invocation.Arguments)[1] = value;
                        return found;
                    }));
                cache.Setup(value => value.Remove(It.IsAny<string>())).Returns<string>(values.Remove);
                return caches[name] = cache.Object;
            });
        _cacheFactory = cacheFactory.Object;
        var provider = new Mock<IServiceProvider>();
        provider.Setup(value => value.GetService(typeof(ICacheFactory))).Returns(_cacheFactory);
        var scope = new Mock<IServiceScope>();
        scope.Setup(value => value.ServiceProvider).Returns(provider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(value => value.CreateScope()).Returns(scope.Object);
        _scopeFactory = scopeFactory.Object;
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
        _cache = new Mock<IQueryCoordinator>();
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

    [TestCase(1726, true)]
    [TestCase(9999, false)]
    public void Matches_Result_WhenAppliedSubjectGroupIsReconstructed(int resolvedSubjectId, bool expected)
    {
        // Arrange
        var appliedGroup = new ResolvedSubjectGroup
        {
            BindingKey = "cast",
            Excluded = false,
            Subjects = [new ResolvedSubject(1726, "Performer", SubjectRole.Cast)]
        };
        var currentGroup = new ResolvedSubjectGroup
        {
            BindingKey = "cast",
            Excluded = false,
            Subjects = [new ResolvedSubject(resolvedSubjectId, "Performer", SubjectRole.Cast)]
        };
        var hit = new SearchHit
        {
            Route = ContentRoute.Movie,
            Id = 1,
            AppliedCriteria = new DiscoverRequest
            {
                Request = new Request
                {
                    Endpoint = "api",
                    Path = "3/discover/movie",
                    QueryParameters = [],
                    CacheKey = "request",
                    ResponseProfile = ResponseProfile.Query
                },
                AppliedTextFilters = [],
                AppliedPublishDateFilters = [],
                AppliedDurationFilters = [],
                AppliedGenreFilters = [],
                AppliedSubjectGroups = [appliedGroup]
            }
        };
        var filter = new FilterPlanNode
        {
            Id = 2,
            Precondition = PlanPrecondition.None,
            Contributes = "filter",
            Route = ContentRoute.Movie,
            Child = new ResolvePlanNode
            {
                Id = 1,
                Precondition = PlanPrecondition.None,
                Contributes = "resolve",
                Route = ContentRoute.Movie,
                SubjectBindings = [],
                GenreFilters = [],
                ResolveImageBase = false
            },
            ResidualPredicates = [],
            TextFilters = [],
            PublishDateFilters = [],
            DurationFilters = [],
            GenreFilters = [],
            SubjectBindings = [],
            ContentIdFilters = [],
            ElementFilters = [],
            RequestedPage = 1
        };

        // Act
        var result = hit.Matches(filter, _genres, [currentGroup], appliedCriteria: hit.AppliedCriteria);

        // Assert
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public async Task ContinueSearchAsync_RequestsNoDetails_WhenAppliedSubjectGroupIsReconstructedAcrossPages()
    {
        // Arrange
        _client.Setup(client => client.GetAsync<SearchEnvelopeDto<MovieSearchHitDto>>(
                It.IsAny<RequestContext>(),
                It.IsAny<Request>(),
                It.IsAny<CancellationToken>()))
            .Callback((RequestContext _, Request request, CancellationToken _) => _requests.Add(request))
            .ReturnsAsync((RequestContext _, Request request, CancellationToken _) =>
            {
                var page = int.Parse(
                    request.QueryParameters.Single(pair => pair.Key == "page").Value,
                    CultureInfo.InvariantCulture);
                return new SearchEnvelopeDto<MovieSearchHitDto>
                {
                    Page = page,
                    TotalPages = 2,
                    TotalResults = 2,
                    Results =
                    [
                        new MovieSearchHitDto
                        {
                            Id = page == 1 ? 1726 : 1727,
                            Title = page == 1 ? "Iron Man" : "Iron Man 2",
                        },
                    ],
                };
            });
        var bindings = new[]
        {
            new SubjectResolutionBinding(
                "cast",
                new SubjectFilter(
                    false,
                    [
                        new SubjectAlternative(
                            [SubjectRole.Cast],
                            [[
                                new SubjectIdentity("First performer", "tmdb", "100"),
                                new SubjectIdentity("Second performer", "tmdb", "200")
                            ]])
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
            Fingerprint = "reconstructed-subjects",
            Resolve = resolve,
            RequiresLogicalStream = true,
            DiscoverParameters =
            [
                new DiscoverParameter
                {
                    Name = "with_cast",
                    Combination = DiscoverCombination.Or,
                    SubjectBindingKeys = ["cast"]
                }
            ]
        };
        var filter = new FilterPlanNode
        {
            Id = 3,
            Precondition = PlanPrecondition.None,
            Contributes = "filter",
            Route = ContentRoute.Movie,
            Child = fetch,
            ResidualPredicates = [],
            TextFilters = [],
            PublishDateFilters = [],
            DurationFilters = [],
            GenreFilters = [],
            SubjectBindings = bindings,
            ContentIdFilters = [],
            ElementFilters = [],
            RequestedPage = 1,
            MaximumResults = 1
        };
        var plan = new QueryPlan
        {
            Root = filter,
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 1,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "reconstructed-subject-plan"
        };
        var planner = new Mock<IQueryPlanner>();
        planner.Setup(value => value.Plan(It.IsAny<QueryRequest>())).Returns(plan);
        var settings = new Mock<ISettingsResolver>();
        settings.Setup(value => value.AcquireContextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Context);
        var engine = new Magpie.Extensions.Tmdb.QueryEngine.QueryEngine(
            settings.Object,
            _subject,
            _cache.Object,
            _client.Object,
            planner.Object,
            NullLogger<Magpie.Extensions.Tmdb.QueryEngine.QueryEngine>.Instance);
        var searcher = new ContentMetadataSearcher(
            engine,
            settings.Object,
            _client.Object,
            _cacheFactory,
            _cache.Object,
            NullLogger<ContentMetadataSearcher>.Instance);

        // Act
        var first = await searcher.ContinueSearchAsync("movie", new(), 1, "en-US", null).ConfigureAwait(false);
        var second = await searcher.ContinueSearchAsync("movie", new(), 1, "en-US", first.Continuation)
            .ConfigureAwait(false);
        var replay = await searcher.ContinueSearchAsync("movie", new(), 1, "en-US", first.Continuation)
            .ConfigureAwait(false);
        var independent = await searcher.ContinueSearchAsync("movie", new(), 1, "en-US", null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Page.Matches.Select(hit => hit.ExternalId), Is.EqualTo(["1726"]));
            Assert.That(second.Page.Matches.Select(hit => hit.ExternalId), Is.EqualTo(["1727"]));
            Assert.That(replay.Page.Matches.Select(hit => hit.ExternalId), Is.EqualTo(["1727"]));
            Assert.That(independent.Page.Matches.Select(hit => hit.ExternalId), Is.EqualTo(["1726"]));
            Assert.That(
                _requests.Count(request => request.ResponseProfile == ResponseProfile.Query),
                Is.EqualTo(6));
            _client.Verify(client => client.GetAsync<MovieDetailDto>(
                It.IsAny<RequestContext>(),
                It.IsAny<Request>(),
                It.IsAny<CancellationToken>()), Times.Never);
            _cache.Verify(cache => cache.GetCreditsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Func<It.IsAnyType, CancellationToken, Task<CreditResponse>>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_RequestsDetail_WhenAppliedSubjectGroupHasResidualDurationConstraint()
    {
        // Arrange
        _client.Setup(client => client.GetAsync<MovieDetailDto>(
                It.IsAny<RequestContext>(),
                It.IsAny<Request>(),
                It.IsAny<CancellationToken>()))
            .Callback((RequestContext _, Request request, CancellationToken _) => _requests.Add(request))
            .ReturnsAsync(new MovieDetailDto { Id = 1726, Title = "Iron Man", Runtime = 126 });
        var bindings = new[]
        {
            new SubjectResolutionBinding(
                "cast",
                new SubjectFilter(
                    false,
                    [
                        new SubjectAlternative(
                            [SubjectRole.Cast],
                            [[new SubjectIdentity("Performer", "tmdb", "100")]])
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
            Fingerprint = "applied-subject-residual-duration",
            Resolve = resolve,
            RequiresLogicalStream = true,
            DiscoverParameters =
            [
                new DiscoverParameter
                {
                    Name = "with_cast",
                    Combination = DiscoverCombination.Or,
                    SubjectBindingKeys = ["cast"]
                }
            ]
        };
        var filter = new FilterPlanNode
        {
            Id = 3,
            Precondition = PlanPrecondition.None,
            Contributes = "filter",
            Route = ContentRoute.Movie,
            Child = fetch,
            ResidualPredicates = [],
            TextFilters = [],
            PublishDateFilters = [],
            DurationFilters =
            [
                new DurationFilter(CriteriaValueOperator.Equals, false, [TimeSpan.FromMinutes(126)])
            ],
            GenreFilters = [],
            SubjectBindings = bindings,
            ContentIdFilters = [],
            ElementFilters = [],
            RequestedPage = 1,
            MaximumResults = 25
        };
        var plan = new QueryPlan
        {
            Root = filter,
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 25,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "applied-subject-residual-duration-plan"
        };

        // Act
        var execution = await _subject.ExecuteAsync(plan, Context, default).ConfigureAwait(false);
        var hits = await execution.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(hits.Select(hit => hit.Id), Is.EqualTo([1726]));
            _client.Verify(client => client.GetAsync<SearchEnvelopeDto<MovieSearchHitDto>>(
                It.IsAny<RequestContext>(),
                It.IsAny<Request>(),
                It.IsAny<CancellationToken>()), Times.Once);
            _client.Verify(client => client.GetAsync<MovieDetailDto>(
                It.IsAny<RequestContext>(),
                It.IsAny<Request>(),
                It.IsAny<CancellationToken>()), Times.Once);
            _cache.Verify(cache => cache.GetCreditsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Func<It.IsAnyType, CancellationToken, Task<CreditResponse>>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_SkipsUnverifiedCandidates_WhenOneDetailLookupFails([Values] bool failureFirst)
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
                    new MovieSearchHitDto { Id = 1726, Title = "Iron Man" },
                    new MovieSearchHitDto { Id = 1727, Title = "Other" }
                ]
            });
        var calls = 0;
        _client.Setup(client => client.GetAsync<MovieDetailDto>(
                It.IsAny<RequestContext>(),
                It.IsAny<Request>(),
                It.IsAny<CancellationToken>()))
            .Callback((RequestContext _, Request request, CancellationToken _) => _requests.Add(request))
            .ReturnsAsync(() =>
            {
                calls++;
                if (calls == (failureFirst ? 1 : 2))
                {
                    throw new System.Net.Http.HttpRequestException("fixture failure");
                }

                return new MovieDetailDto { Id = failureFirst ? 1727 : 1726, Title = "Verified", Runtime = 126 };
            });
        var bindings = new[]
        {
            new SubjectResolutionBinding(
                "cast",
                new SubjectFilter(
                    false,
                    [
                        new SubjectAlternative(
                            [SubjectRole.Cast],
                            [[new SubjectIdentity("Performer", "tmdb", "100")]])
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
            Fingerprint = "applied-subject-residual-duration",
            Resolve = resolve,
            RequiresLogicalStream = true,
            DiscoverParameters =
            [
                new DiscoverParameter
                {
                    Name = "with_cast",
                    Combination = DiscoverCombination.Or,
                    SubjectBindingKeys = ["cast"]
                }
            ]
        };
        var filter = new FilterPlanNode
        {
            Id = 3,
            Precondition = PlanPrecondition.None,
            Contributes = "filter",
            Route = ContentRoute.Movie,
            Child = fetch,
            ResidualPredicates = [],
            TextFilters = [],
            PublishDateFilters = [],
            DurationFilters =
            [
                new DurationFilter(CriteriaValueOperator.Equals, false, [TimeSpan.FromMinutes(126)])
            ],
            GenreFilters = [],
            SubjectBindings = bindings,
            ContentIdFilters = [],
            ElementFilters = [],
            RequestedPage = 1,
            MaximumResults = 25
        };
        var plan = new QueryPlan
        {
            Root = filter,
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 25,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "applied-subject-residual-duration-plan"
        };

        // Act
        var execution = await _subject.ExecuteAsync(plan, Context, default).ConfigureAwait(false);
        var hits = await execution.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(hits.Select(hit => hit.Id), Is.EqualTo(failureFirst ? new[] { 1727 } : [1726]));
            Assert.That(execution.Warnings, Is.Not.Empty);
            Assert.That(calls, Is.EqualTo(2));
        }
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
    public void ExecuteAsync_PropagatesFailure_WhenTheRemoteCallThrows()
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

        // Act and assert
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            var execution = await _subject.ExecuteAsync(
                plan,
                new RequestContext { ApiKey = "key", Language = "en-US" },
                default).ConfigureAwait(false);
            await execution.Hits.ToListAsync().ConfigureAwait(false);
        });
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
            new RequestContext { ApiKey = "key", Language = "en-US" },
            default).ConfigureAwait(false);
        var hits = await execution.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        Assert.That(hits, Has.Count.EqualTo(2));
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
            new RequestContext { ApiKey = "key", Language = "en-US" },
            default).ConfigureAwait(false);
        var hits = await execution.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        Assert.That(hits, Is.Empty);
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
            new RequestContext { ApiKey = "key", Language = "en-US" },
            default).ConfigureAwait(false);
        var hits = await execution.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        Assert.That(hits, Is.Empty);
        _client.VerifyNoOtherCalls();
    }

    [TestCase(false, new[] { 1726 })]
    [TestCase(true, new int[0])]
    public async Task ExecuteAsync_RetainsSupportedSubjectAlternativeWithoutWarning_WhenSiblingIsUnevaluable(
        bool excluded,
        int[] expectedIds)
    {
        // Arrange
        var binding = new SubjectResolutionBinding(
            "person-0",
            new SubjectFilter(
                excluded,
                [
                    new SubjectAlternative(
                        [SubjectRole.Person],
                        [[
                            new SubjectIdentity("Alice", "tmdb", "1"),
                            new SubjectIdentity("Alice", "musicbrainz", "abc")
                        ]]),
                    new SubjectAlternative(
                        [SubjectRole.Person],
                        [[new SubjectIdentity("Bob", "musicbrainz", "abc")]])
                ]));
        var plan = new QueryPlan
        {
            Root = new FetchPlanNode
            {
                Id = 2,
                Precondition = PlanPrecondition.None,
                Contributes = "fetch",
                Route = ContentRoute.Movie,
                AnchorKind = QueryAnchorKind.Search,
                SubjectBindings = [binding],
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
        var execution = await _subject.ExecuteAsync(plan, Context, default).ConfigureAwait(false);
        var hits = await execution.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(hits.Select(hit => hit.Id), Is.EqualTo(expectedIds));
            Assert.That(execution.Warnings, Is.Empty);
        }
    }

    [Test]
    public async Task ExecuteAsync_RetainsSupportedSubjectRoleWithoutWarning_WhenSiblingRoleIsUnevaluable()
    {
        // Arrange
        _cache.Setup(cache => cache.GetSubjectResolutionAsync(
                It.IsAny<string>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Func<It.IsAnyType, CancellationToken, Task<SubjectResolution>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubjectResolution { Kind = SubjectResolutionKind.Resolved, Id = 1, Name = "Alice" });
        var binding = new SubjectResolutionBinding(
            "person-0",
            new SubjectFilter(
                false,
                [
                    new SubjectAlternative(
                        [SubjectRole.Person, SubjectRole.Company],
                        [[new SubjectIdentity("Alice", "imdb", "nm0000001")]])
                ]));
        var plan = new QueryPlan
        {
            Root = new FetchPlanNode
            {
                Id = 2,
                Precondition = PlanPrecondition.None,
                Contributes = "fetch",
                Route = ContentRoute.Movie,
                AnchorKind = QueryAnchorKind.Search,
                SubjectBindings = [binding],
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
        var execution = await _subject.ExecuteAsync(plan, Context, default).ConfigureAwait(false);
        var hits = await execution.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(hits.Select(hit => hit.Id), Is.EqualTo([1726]));
            Assert.That(execution.Warnings, Is.Empty);
        }
    }

    [Test]
    public async Task ExecuteAsync_PreservesRuntimeWarning_WhenSubjectResolutionReturnsMatches()
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
                    Id = 1,
                    Name = "Alice",
                    Incomplete = true,
                    Warning = "TMDb person name search reached the 500-page API limit."
                }
            ]);
        var binding = new SubjectResolutionBinding(
            "person-0",
            new SubjectFilter(
                false,
                [new SubjectAlternative([SubjectRole.Person], [[new SubjectIdentity("Alice", null, null)]])]));
        var plan = new QueryPlan
        {
            Root = new FetchPlanNode
            {
                Id = 2,
                Precondition = PlanPrecondition.None,
                Contributes = "fetch",
                Route = ContentRoute.Movie,
                AnchorKind = QueryAnchorKind.Search,
                SubjectBindings = [binding],
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
        var execution = await _subject.ExecuteAsync(plan, Context, default).ConfigureAwait(false);
        var hits = await execution.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(hits.Select(hit => hit.Id), Is.EqualTo([1726]));
            Assert.That(
                execution.Warnings,
                Is.EqualTo(["TMDb person name search reached the 500-page API limit."]));
        }
    }

    [Test]
    public async Task ExecuteAsync_FetchesWithoutWarning_WhenExcludedSubjectIsAbsent()
    {
        // Arrange
        _cache.Setup(cache => cache.GetSubjectResolutionsAsync(
                It.IsAny<string>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Func<It.IsAnyType, CancellationToken, Task<IReadOnlyList<SubjectResolution>>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SubjectResolution { Kind = SubjectResolutionKind.NotFound }]);
        var binding = new SubjectResolutionBinding(
            "person-0",
            new SubjectFilter(
                true,
                [
                    new SubjectAlternative(
                        [SubjectRole.Person],
                        [[new SubjectIdentity("Nobody", null, null)]])
                ]));
        var plan = new QueryPlan
        {
            Root = new FetchPlanNode
            {
                Id = 2,
                Precondition = PlanPrecondition.None,
                Contributes = "fetch",
                Route = ContentRoute.Movie,
                AnchorKind = QueryAnchorKind.Search,
                SubjectBindings = [binding],
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
        var execution = await _subject.ExecuteAsync(plan, Context, default).ConfigureAwait(false);
        var hits = await execution.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(hits.Select(hit => hit.Id), Is.EqualTo([1726]));
            Assert.That(execution.Warnings, Is.Empty);
        }
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
    public async Task ExecuteAsync_SkipsTheRemainingCoalesceChildren_WhenTheFirstChildIsApplicable()
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
            new RequestContext { ApiKey = "key", Language = "en-US" },
            default).ConfigureAwait(false);
        var hits = await execution.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        Assert.That(hits.Single().Id, Is.EqualTo(10));

        _client.Verify(
            client => client.GetAsync<SearchEnvelopeDto<MovieSearchHitDto>>(
                It.IsAny<RequestContext>(),
                It.IsAny<Request>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_PreservesIndependentAnswers_WhenOtherLookupsFail(
        [Values] bool union,
        [Values] bool failureFirst,
        [Values] bool empty,
        [Values] bool timeout,
        [Values] bool allFail,
        [Values] bool callerCancellation)
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        _client.Setup(client => client.GetAsync<MovieDetailDto>(
                It.IsAny<RequestContext>(), It.IsAny<Request>(), It.IsAny<CancellationToken>()))
            .Returns(async (RequestContext _, Request request, CancellationToken token) =>
            {
                if (callerCancellation && request.Path == "3/movie/551")
                {
                    await cancellation.CancelAsync().ConfigureAwait(false);
                    return await Task.FromCanceled<MovieDetailDto?>(token).ConfigureAwait(false);
                }

                return request.Path == "3/movie/550" && !allFail
                    ? empty ? null : new MovieDetailDto { Id = 550, Title = "Kept" }
                    : await Task.FromException<MovieDetailDto?>(timeout
                        ? new TaskCanceledException("request timed out")
                        : new System.Net.Http.HttpRequestException("unavailable")).ConfigureAwait(false);
            });
        var first = new ContentId("tmdb", failureFirst ? "551" : "550");
        var second = new ContentId("tmdb", failureFirst ? "550" : "551");
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
            Fingerprint = "fetch-1",
            ContentIdFilters = [new ContentIdFilter(false, [first, second])]
        };
        var plan = new QueryPlan
        {
            Root = union ? new UnionPlanNode
            {
                Id = 3,
                Precondition = PlanPrecondition.None,
                Contributes = "union",
                Children =
                [
                    fetch with { ContentIdFilters = [new ContentIdFilter(false, [first])] },
                    fetch with { Id = 2, Fingerprint = "fetch-2",
                        ContentIdFilters = [new ContentIdFilter(false, [second])] }
                ]
            } : fetch,
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 25,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };

        // Act and assert
        if (callerCancellation)
        {
            Assert.CatchAsync<OperationCanceledException>(async () =>
            {
                var cancelled = await _subject.ExecuteAsync(
                    plan, new RequestContext { ApiKey = "key", Language = "en-US" }, cancellation.Token)
                    .ConfigureAwait(false);
                await cancelled.Hits.ToListAsync().ConfigureAwait(false);
            });
            return;
        }

        if (allFail)
        {
            Assert.ThrowsAsync<System.Net.Http.HttpRequestException>(async () =>
            {
                var failed = await _subject.ExecuteAsync(
                    plan, new RequestContext { ApiKey = "key", Language = "en-US" }, default).ConfigureAwait(false);
                await failed.Hits.ToListAsync().ConfigureAwait(false);
            });
            return;
        }

        var execution = await _subject.ExecuteAsync(
            plan, new RequestContext { ApiKey = "key", Language = "en-US" }, default).ConfigureAwait(false);
        var hits = await execution.Hits.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(hits.Select(hit => hit.Id), Is.EqualTo(empty ? Array.Empty<int>() : [550]));
            Assert.That(execution.Warnings, Is.Not.Empty);
            Assert.That(execution.Diagnostics.StreamFaulted, Is.True);
        }
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
        }

        _client.Verify(
            client => client.GetAsync<MovieDetailDto>(
                It.IsAny<RequestContext>(),
                It.Is<Request>(request => request.CacheKey == "3/movie/550?language=en-US"),
                It.IsAny<CancellationToken>()),
            Times.Once);

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
        var hits = await execution.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(hits.Select(hit => hit.Id), Is.EqualTo([550]));
            _client.Verify(
                client => client.GetAsync<MovieDetailDto>(
                    It.IsAny<RequestContext>(),
                    It.Is<Request>(request =>
                        request.CacheKey == "3/movie/550?language=en-US&append_to_response=credits"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
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
        }

        _client.Verify(
            client => client.GetAsync<FindEnvelopeDto>(
                It.IsAny<RequestContext>(),
                It.Is<Request>(request => request.CacheKey ==
                    "3/find/external-1?external_source=" + expectedExternalSource + "&language=en-US"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_ReturnsEmptyWithoutWarning_WhenTheContentIdSourceHasNoLookup()
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
            Assert.That(execution.Warnings, Is.Empty);
        }

        _client.VerifyNoOtherCalls();
    }

    [Test]
    public async Task ExecuteAsync_ReturnsSupportedHitWithoutWarning_WhenUnsupportedAlternativeDoesNotResolve()
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
                ContentIdFilters =
                [
                    new ContentIdFilter(
                        false,
                        [new ContentId("tmdb", "550"), new ContentId("musicbrainz", "abc")])
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
            Assert.That(hits.Select(hit => hit.Id), Is.EqualTo([550]));
            Assert.That(execution.Warnings, Is.Empty);
        }
    }

    [TestCase("tmdb", "550")]
    [TestCase("imdb", "tt0137523")]
    [TestCase("tvdb", "123")]
    public async Task ExecuteAsync_ReturnsEmptyWithoutWarnings_WhenSupportedIdentifierDoesNotResolve(
        string source,
        string identifier)
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
                ContentIdFilters = [new ContentIdFilter(false, [new ContentId(source, identifier)])]
            },
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 25,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };

        // Act
        var execution = await _subject.ExecuteAsync(plan, Context, default).ConfigureAwait(false);
        var hits = await execution.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(hits, Is.Empty);
            Assert.That(execution.Warnings, Is.Empty);
        }
    }

    [Test]
    public async Task ExecuteAsync_IntersectsCredits_WhenMultiplePersonGroupsAreRequired()
    {
        // Arrange
        _cache.Setup(cache => cache.GetCreditsAsync(
                "3/person/1/tv_credits?language=en-US",
                RequestRole.TvCredits,
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
                RequestRole.TvCredits,
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
                RequestRole.TvCredits,
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
                RequestRole.TvCredits,
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
    public async Task SearchAsync_ReturnsPartialEmpty_WhenAnAlternativeNameHasNoMatches(
        [Values] bool continuation,
        [Values] bool allFail)
    {
        // Arrange
        _cache.SetupSequence(cache => cache.GetSubjectResolutionsAsync(
                It.IsAny<string>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Func<It.IsAnyType, CancellationToken, Task<IReadOnlyList<SubjectResolution>>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new SubjectResolution
                {
                    Kind = allFail ? SubjectResolutionKind.Failed : SubjectResolutionKind.NotFound
                }
            ])
            .ReturnsAsync([new SubjectResolution { Kind = SubjectResolutionKind.Failed, Warning = "fixture failure" }]);
        var binding = new SubjectResolutionBinding(
            "person-0",
            new SubjectFilter(
                false,
                [
                    new SubjectAlternative(
                        [SubjectRole.Writer],
                        [[new SubjectIdentity("Alice", null, null), new SubjectIdentity("Bob", null, null)]])
                ]));
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

        var planner = new Mock<IQueryPlanner>();
        planner.Setup(value => value.Plan(It.IsAny<QueryRequest>())).Returns(plan);
        var settings = new Mock<ISettingsResolver>();
        settings.Setup(value => value.AcquireContextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Context);
        var engine = new Magpie.Extensions.Tmdb.QueryEngine.QueryEngine(
            settings.Object,
            _subject,
            _cache.Object,
            _client.Object,
            planner.Object,
            NullLogger<Magpie.Extensions.Tmdb.QueryEngine.QueryEngine>.Instance);
        var request = new QueryRequest
        {
            Route = ContentRoute.TvShow,
            Criteria = new(),
            Page = 1,
            Progress = continuation ? new QueryProgress() : null,
            DefaultLanguagePreference = "en-US"
        };

        // Act and assert
        if (allFail)
        {
            Assert.ThrowsAsync<System.Net.Http.HttpRequestException>(async () =>
            {
                var execution = await engine.SearchAsync(request, default).ConfigureAwait(false);
                await execution.Hits.ToListAsync().ConfigureAwait(false);
            });
        }
        else
        {
            var execution = await engine.SearchAsync(request, default).ConfigureAwait(false);
            var hits = await execution.Hits.ToListAsync().ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(hits, Is.Empty);
                Assert.That(execution.Warnings, Is.Not.Empty);
            }
        }
    }

    [Test]
    public async Task ExecuteAsync_RetainsCredits_WhenAnAlternativeFails(
        [Values] bool failureFirst,
        [Values] bool empty,
        [Values] bool timeout)
    {
        // Arrange
        var calls = 0;
        _cache.Setup(cache => cache.GetCreditsAsync(
                It.IsAny<string>(),
                RequestRole.TvCredits,
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Func<It.IsAnyType, CancellationToken, Task<CreditResponse>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                calls++;
                if (calls == (failureFirst ? 1 : 2))
                {
                    throw timeout ? new OperationCanceledException("fixture timeout") :
                        new System.Net.Http.HttpRequestException("fixture failure");
                }

                return new CreditResponse
                {
                    Tv = new PersonTvCreditsDto
                    {
                        Crew = empty ? [] : [new TvCreditDto { Id = 10, Name = "Credited", Job = "Story" }]
                    }
                };
            });
        _cache.Setup(cache => cache.GetSubjectResolutionsAsync(
                It.IsAny<string>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Func<It.IsAnyType, CancellationToken, Task<IReadOnlyList<SubjectResolution>>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new SubjectResolution { Kind = SubjectResolutionKind.Resolved, Id = 1, Name = "Alice" },
                new SubjectResolution { Kind = SubjectResolutionKind.Resolved, Id = 2, Name = "Alice" }
            ]);
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
        using (Assert.EnterMultipleScope())
        {
            Assert.That(hits, Has.Count.EqualTo(empty ? 0 : 1));
            Assert.That(execution.Warnings, Is.Not.Empty);
            Assert.That(calls, Is.EqualTo(2));
        }
    }

    [Test]
    public void ExecuteAsync_Fails_WhenEveryCreditsAlternativeFails()
    {
        // Arrange
        var calls = 0;
        _cache.Setup(cache => cache.GetCreditsAsync(
                It.IsAny<string>(),
                RequestRole.TvCredits,
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Func<It.IsAnyType, CancellationToken, Task<CreditResponse>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                calls++;
                throw new System.Net.Http.HttpRequestException("fixture failure");
            });
        _cache.Setup(cache => cache.GetSubjectResolutionsAsync(
                It.IsAny<string>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Func<It.IsAnyType, CancellationToken, Task<IReadOnlyList<SubjectResolution>>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new SubjectResolution { Kind = SubjectResolutionKind.Resolved, Id = 1, Name = "Alice" },
                new SubjectResolution { Kind = SubjectResolutionKind.Resolved, Id = 2, Name = "Alice" }
            ]);
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

        // Act and assert
        Assert.ThrowsAsync<System.Net.Http.HttpRequestException>(async () => await _subject.ExecuteAsync(
            plan,
            new RequestContext { ApiKey = "key", Language = "en-US" },
            default).ConfigureAwait(false));
        Assert.That(calls, Is.EqualTo(2));
    }

    [Test]
    public async Task ExecuteAsync_StopsCreditsAlternatives_WhenTheCallerCancels()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var calls = 0;
        _cache.Setup(cache => cache.GetCreditsAsync(
                It.IsAny<string>(),
                RequestRole.TvCredits,
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Func<It.IsAnyType, CancellationToken, Task<CreditResponse>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                calls++;
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            });
        _cache.Setup(cache => cache.GetSubjectResolutionsAsync(
                It.IsAny<string>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Func<It.IsAnyType, CancellationToken, Task<IReadOnlyList<SubjectResolution>>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new SubjectResolution { Kind = SubjectResolutionKind.Resolved, Id = 1, Name = "Alice" },
                new SubjectResolution { Kind = SubjectResolutionKind.Resolved, Id = 2, Name = "Alice" }
            ]);
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

        // Act and assert
        Assert.ThrowsAsync<OperationCanceledException>(async () => await _subject.ExecuteAsync(
            plan,
            new RequestContext { ApiKey = "key", Language = "en-US" },
            cancellation.Token).ConfigureAwait(false));
        Assert.That(calls, Is.EqualTo(1));
    }

    [Test]
    public async Task ContinueSearchAsync_ReplaysLastSourcePage_WhenApiPageCapEndsTheScan()
    {
        // Arrange
        _client.Setup(client => client.GetAsync<SearchEnvelopeDto<MovieSearchHitDto>>(
                It.IsAny<RequestContext>(), It.IsAny<Request>(), It.IsAny<CancellationToken>()))
            .Callback((RequestContext _, Request request, CancellationToken _) => _requests.Add(request))
            .ReturnsAsync((RequestContext _, Request request, CancellationToken _) =>
                new SearchEnvelopeDto<MovieSearchHitDto>
                {
                    Page = int.Parse(request.QueryParameters.Single(pair => pair.Key == "page").Value,
                        CultureInfo.InvariantCulture),
                    TotalPages = 501,
                    TotalResults = 10020,
                    Results = []
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
                TextFilters = [new TextFilter(TextField.Title, CriteriaValueOperator.Equals, false, ["Title"])]
            },
            Warnings = [],
            RequestedPage = 1,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };
        var planner = new Mock<IQueryPlanner>();
        planner.Setup(value => value.Plan(It.IsAny<QueryRequest>())).Returns(plan);
        var settings = new Mock<ISettingsResolver>();
        settings.Setup(value => value.AcquireContextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Context);
        var engine = new Magpie.Extensions.Tmdb.QueryEngine.QueryEngine(settings.Object, _subject, _cache.Object,
            _client.Object, planner.Object, NullLogger<Magpie.Extensions.Tmdb.QueryEngine.QueryEngine>.Instance);
        var searcher = new ContentMetadataSearcher(engine, settings.Object, _client.Object, _cacheFactory,
            _cache.Object, NullLogger<ContentMetadataSearcher>.Instance);

        // Act
        var first = await searcher.ContinueSearchAsync("movie", new(), 2, "en-US", null).ConfigureAwait(false);
        var firstRequests = _requests.Count;
        var second = await searcher.ContinueSearchAsync("movie", new(), 2, "en-US", first.Continuation)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Page.Matches, Is.Empty);
            Assert.That(first.Page.Warnings, Has.Some.Contains("500"));
            Assert.That(first.Page.PageInfo.HasMore, Is.True);
            Assert.That(second.Page.Warnings, Has.Some.Contains("500"));
            Assert.That(second.Page.PageInfo.HasMore, Is.True);
            Assert.That(firstRequests, Is.EqualTo(500));
            Assert.That(_requests[firstRequests - 1].QueryParameters.Single(pair => pair.Key == "page").Value,
                Is.EqualTo("500"));
            Assert.That(_requests.Skip(firstRequests)
                .Select(request => request.QueryParameters.Single(pair => pair.Key == "page").Value),
                Is.EqualTo(["500"]));
        }
    }

    [Test]
    public async Task ContinueSearchAsync_ReplaysOnlyTheLastPage_WhenRequestsContinueRetryAndStartIndependently()
    {
        // Arrange
        var pages = new Dictionary<string, IReadOnlyList<MovieSearchHitDto>>
        {
            ["1"] = [new() { Id = 10, Title = "Ten" }, new() { Id = 11, Title = "Eleven" }],
            ["2"] = [new() { Id = 20, Title = "Twenty" }, new() { Id = 21, Title = "Twenty one" }],
            ["3"] = [new() { Id = 30, Title = "Thirty" }, new() { Id = 31, Title = "Thirty one" }]
        };
        _client.Setup(client => client.GetAsync<SearchEnvelopeDto<MovieSearchHitDto>>(
                It.IsAny<RequestContext>(), It.IsAny<Request>(), It.IsAny<CancellationToken>()))
            .Callback((RequestContext _, Request request, CancellationToken _) => _requests.Add(request))
            .ReturnsAsync((RequestContext _, Request request, CancellationToken _) =>
                new SearchEnvelopeDto<MovieSearchHitDto>
                {
                    Page = int.Parse(request.QueryParameters.Single(pair => pair.Key == "page").Value,
                        CultureInfo.InvariantCulture),
                    TotalPages = 3,
                    TotalResults = 6,
                    Results = pages[request.QueryParameters.Single(pair => pair.Key == "page").Value]
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
                TextFilters = [new TextFilter(TextField.Title, CriteriaValueOperator.Equals, false, ["Title"])]
            },
            Warnings = [],
            RequestedPage = 1,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };
        var planner = new Mock<IQueryPlanner>();
        planner.Setup(value => value.Plan(It.IsAny<QueryRequest>())).Returns(plan);
        var settings = new Mock<ISettingsResolver>();
        settings.Setup(value => value.AcquireContextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Context);
        var engine = new Magpie.Extensions.Tmdb.QueryEngine.QueryEngine(settings.Object, _subject, _cache.Object,
            _client.Object, planner.Object, NullLogger<Magpie.Extensions.Tmdb.QueryEngine.QueryEngine>.Instance);
        var searcher = new ContentMetadataSearcher(engine, settings.Object, _client.Object, _cacheFactory,
            _cache.Object, NullLogger<ContentMetadataSearcher>.Instance);
        var criteria = new Magpie.Models.Data.ContentHierarchyItemCriteria();

        // Act
        var first = await searcher.ContinueSearchAsync("movie", criteria, 2, "en-US", null).ConfigureAwait(false);
        var second = await searcher.ContinueSearchAsync("movie", criteria, 2, "en-US", first.Continuation)
            .ConfigureAwait(false);
        var third = await searcher.ContinueSearchAsync("movie", criteria, 2, "en-US", second.Continuation)
            .ConfigureAwait(false);
        var retry = await searcher.ContinueSearchAsync("movie", criteria, 2, "en-US", first.Continuation)
            .ConfigureAwait(false);
        var independent = await searcher.ContinueSearchAsync("movie", criteria, 2, "en-US", null).ConfigureAwait(false);
        var end = await searcher.ContinueSearchAsync("movie", criteria, 2, "en-US", third.Continuation)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Page.Matches.Select(candidate => candidate.ExternalId), Is.EqualTo(["10", "11"]));
            Assert.That(second.Page.Matches.Select(candidate => candidate.ExternalId), Is.EqualTo(["20", "21"]));
            Assert.That(third.Page.Matches.Select(candidate => candidate.ExternalId), Is.EqualTo(["30", "31"]));
            Assert.That(retry.Page.Matches.Select(candidate => candidate.ExternalId), Is.EqualTo(["20", "21"]));
            Assert.That(independent.Page.Matches.Select(candidate => candidate.ExternalId), Is.EqualTo(["10", "11"]));
            Assert.That(end.Page.Matches, Is.Empty);
            Assert.That(end.Page.PageInfo.HasMore, Is.False);
            Assert.That(_requests.Select(request => request.QueryParameters.Single(pair => pair.Key == "page").Value),
                Is.EqualTo(["1", "1", "2", "2", "3", "1", "2", "1", "3"]));
        }
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
            Assert.That(parameters["primary_release_date.gte"], Is.EqualTo("2021-01-01"));
            Assert.That(parameters["primary_release_date.lte"], Is.EqualTo("2025-12-31"));
            Assert.That(parameters["with_runtime.gte"], Is.EqualTo("90"));
            Assert.That(parameters["with_runtime.lte"], Is.EqualTo("180"));
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
    public async Task ExecuteAsync_RetainsResolvedGenreAlternativeWithoutWarning_WhenSiblingIsUnresolved()
    {
        // Arrange
        _genres["Action"] = 28;
        var genre = new GenreFilter(
            false,
            [
                [new GenrePredicate(CriteriaValueOperator.Equals, false, ["Action"])],
                [new GenrePredicate(CriteriaValueOperator.Equals, false, ["Missing"])]
            ]);
        var plan = new QueryPlan
        {
            Root = new FetchPlanNode
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
            },
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 25,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };

        // Act
        var execution = await _subject.ExecuteAsync(plan, Context, default).ConfigureAwait(false);
        var hits = await execution.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(hits.Select(hit => hit.Id), Is.EqualTo([1726]));
            Assert.That(execution.Warnings, Is.Empty);
            Assert.That(
                _requests.Single().QueryParameters.Single(pair => pair.Key == "with_genres").Value,
                Is.EqualTo("28"));
        }
    }

    [Test]
    public async Task ExecuteAsync_ReturnsEmptyWithoutWarning_WhenNoGenreAlternativeResolves()
    {
        // Arrange
        var genre = new GenreFilter(
            false,
            [
                [new GenrePredicate(CriteriaValueOperator.Equals, false, ["Missing"])],
                [new GenrePredicate(CriteriaValueOperator.Equals, false, ["Also Missing"])]
            ]);
        var plan = new QueryPlan
        {
            Root = new FetchPlanNode
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
            },
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 25,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };

        // Act
        var execution = await _subject.ExecuteAsync(plan, Context, default).ConfigureAwait(false);
        var hits = await execution.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(hits, Is.Empty);
            Assert.That(execution.Warnings, Is.Empty);
        }

        _client.VerifyNoOtherCalls();
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

    [TestCaseSource(nameof(NameSearchCases))]
    public async Task ExecuteAsync_BoundsThePersonNameSearch_WhenManyPeopleShareTheName(
        CriteriaValueOperator identityOperator,
        int expectedNameSearches,
        string expectedPeople)
    {
        // Arrange
        _client.Setup(client => client.GetAsync<SearchEnvelopeDto<NamedSearchHitDto>>(
                It.IsAny<RequestContext>(),
                It.IsAny<Request>(),
                It.IsAny<CancellationToken>()))
            .Callback((RequestContext _, Request request, CancellationToken _) => _requests.Add(request))
            .ReturnsAsync((RequestContext _, Request request, CancellationToken _) =>
            {
                var page = int.Parse(
                    request.QueryParameters.First(pair => pair.Key == "page").Value,
                    CultureInfo.InvariantCulture);
                return new SearchEnvelopeDto<NamedSearchHitDto>
                {
                    Page = page,
                    TotalPages = 50,
                    TotalResults = 1000,
                    Results = [new NamedSearchHitDto { Id = page, Name = "Sam Smith" }]
                };
            });
        var executor = new QueryExecutor(
            _client.Object,
            new QueryCoordinator(_scopeFactory, Mock.Of<IHostApplicationLifetime>()),
            NullLogger<QueryExecutor>.Instance);
        var binding = new SubjectResolutionBinding(
            "person",
            new SubjectFilter(
                false,
                [
                    new SubjectAlternative(
                        [SubjectRole.Person],
                        [[new SubjectIdentity("Sam Smith", null, null, identityOperator)]])
                ]));
        var plan = new QueryPlan
        {
            Root = new FetchPlanNode
            {
                Id = 2,
                Precondition = PlanPrecondition.None,
                Contributes = "fetch",
                Route = ContentRoute.Movie,
                AnchorKind = QueryAnchorKind.Discover,
                SubjectBindings = [binding],
                PushedFilters = [],
                RequestedPage = 1,
                Fingerprint = "fetch-2",
                DiscoverParameters =
                [
                    new DiscoverParameter
                    {
                        Name = "with_people",
                        Combination = DiscoverCombination.Or,
                        SubjectBindingKeys = ["person"]
                    }
                ],
                Resolve = new ResolvePlanNode
                {
                    Id = 1,
                    Precondition = PlanPrecondition.None,
                    Contributes = "resolve",
                    Route = ContentRoute.Movie,
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
        var execution = await executor.ExecuteAsync(plan, Context, default).ConfigureAwait(false);
        var hits = await execution.Hits.ToListAsync().ConfigureAwait(false);
        var discover = _requests.Single(request => request.Path == "3/discover/movie");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                _requests.Count(request => request.Path == "3/search/person"),
                Is.EqualTo(expectedNameSearches));
            Assert.That(
                discover.QueryParameters.Single(pair => pair.Key == "with_people").Value,
                Is.EqualTo(expectedPeople));
            Assert.That(execution.Warnings, Is.Empty);
            Assert.That(hits.Select(hit => hit.Id), Is.EqualTo([1726]));
        }
    }

    [Test]
    public async Task ExecuteAsync_RetainsResolvedNames_WhenALaterNamePageFails()
    {
        // Arrange
        _client.Setup(client => client.GetAsync<SearchEnvelopeDto<NamedSearchHitDto>>(
                It.IsAny<RequestContext>(),
                It.IsAny<Request>(),
                It.IsAny<CancellationToken>()))
            .Callback((RequestContext _, Request request, CancellationToken _) => _requests.Add(request))
            .ReturnsAsync((RequestContext _, Request request, CancellationToken _) =>
            {
                var page = int.Parse(
                    request.QueryParameters.First(pair => pair.Key == "page").Value,
                    CultureInfo.InvariantCulture);
                if (page == 2)
                {
                    throw new System.Net.Http.HttpRequestException("fixture failure");
                }

                return new SearchEnvelopeDto<NamedSearchHitDto>
                {
                    Page = page,
                    TotalPages = 50,
                    TotalResults = 1000,
                    Results = [new NamedSearchHitDto { Id = page, Name = "Sam Smith" }]
                };
            });
        var executor = new QueryExecutor(
            _client.Object,
            new QueryCoordinator(_scopeFactory, Mock.Of<IHostApplicationLifetime>()),
            NullLogger<QueryExecutor>.Instance);
        var binding = new SubjectResolutionBinding(
            "person",
            new SubjectFilter(
                false,
                [
                    new SubjectAlternative(
                        [SubjectRole.Person],
                        [[new SubjectIdentity("Sam Smith", null, null, CriteriaValueOperator.Equals)]])
                ]));
        var plan = new QueryPlan
        {
            Root = new FetchPlanNode
            {
                Id = 2,
                Precondition = PlanPrecondition.None,
                Contributes = "fetch",
                Route = ContentRoute.Movie,
                AnchorKind = QueryAnchorKind.Discover,
                SubjectBindings = [binding],
                PushedFilters = [],
                RequestedPage = 1,
                Fingerprint = "fetch-2",
                DiscoverParameters =
                [
                    new DiscoverParameter
                    {
                        Name = "with_people",
                        Combination = DiscoverCombination.Or,
                        SubjectBindingKeys = ["person"]
                    }
                ],
                Resolve = new ResolvePlanNode
                {
                    Id = 1,
                    Precondition = PlanPrecondition.None,
                    Contributes = "resolve",
                    Route = ContentRoute.Movie,
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
        var execution = await executor.ExecuteAsync(plan, Context, default).ConfigureAwait(false);
        var hits = await execution.Hits.ToListAsync().ConfigureAwait(false);
        var discover = _requests.Single(request => request.Path == "3/discover/movie");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                _requests.Count(request => request.Path == "3/search/person"),
                Is.EqualTo(2));
            Assert.That(
                discover.QueryParameters.Single(pair => pair.Key == "with_people").Value,
                Is.EqualTo("1"));
            Assert.That(execution.Warnings, Is.Not.Empty);
            Assert.That(hits.Select(hit => hit.Id), Is.EqualTo([1726]));
        }
    }

    [Test]
    public async Task ExecuteAsync_ReturnsRowsBeforeTheDiscoverRoundCompletes_WhenSeveralSourcesAreScanned()
    {
        // Arrange
        _genres["action"] = 28;
        _genres["comedy"] = 35;
        _genres["drama"] = 18;
        var pages = new Dictionary<string, IReadOnlyList<MovieSearchHitDto>>(StringComparer.Ordinal)
        {
            ["28"] =
            [
                new MovieSearchHitDto { Id = 10, Title = "Alpha" },
                new MovieSearchHitDto { Id = 11, Title = "Bravo" }
            ],
            ["35"] =
            [
                new MovieSearchHitDto { Id = 20, Title = "Charlie" },
                new MovieSearchHitDto { Id = 21, Title = "Delta" }
            ],
            ["18"] =
            [
                new MovieSearchHitDto { Id = 30, Title = "Echo" },
                new MovieSearchHitDto { Id = 31, Title = "Foxtrot" }
            ]
        };
        _client.Setup(client => client.GetAsync<SearchEnvelopeDto<MovieSearchHitDto>>(
                It.IsAny<RequestContext>(),
                It.IsAny<Request>(),
                It.IsAny<CancellationToken>()))
            .Callback((RequestContext _, Request request, CancellationToken _) => _requests.Add(request))
            .ReturnsAsync((RequestContext _, Request request, CancellationToken _) =>
                new SearchEnvelopeDto<MovieSearchHitDto>
                {
                    Page = 1,
                    TotalPages = 1,
                    TotalResults = 2,
                    Results = pages[request.QueryParameters.Single(pair => pair.Key == "with_genres").Value]
                });
        var genreFilter = new GenreFilter(
            false,
            [[new GenrePredicate(CriteriaValueOperator.In, false, ["action", "comedy", "drama"])]]);
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
            Fingerprint = "fetch-2",
            DiscoverParameters =
            [
                new DiscoverParameter
                {
                    Name = "with_genres",
                    Combination = DiscoverCombination.And,
                    GenreFilters = [genreFilter]
                }
            ],
            Resolve = new ResolvePlanNode
            {
                Id = 1,
                Precondition = PlanPrecondition.None,
                Contributes = "resolve",
                Route = ContentRoute.Movie,
                SubjectBindings = [],
                GenreFilters = [genreFilter],
                ResolveImageBase = false
            }
        };
        var filter = new FilterPlanNode
        {
            Id = 3,
            Precondition = PlanPrecondition.None,
            Contributes = "filter",
            Route = ContentRoute.Movie,
            Child = fetch,
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
        };
        var limited = new QueryPlan
        {
            Root = filter,
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 1,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };
        var drained = new QueryPlan
        {
            Root = filter with { MaximumResults = 25 },
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 25,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };

        // Act
        var first = await _subject.ExecuteAsync(limited, Context, default).ConfigureAwait(false);
        var firstHits = await first.Hits.ToListAsync().ConfigureAwait(false);
        var firstRequests = _requests.Count;
        var second = await _subject.ExecuteAsync(drained, Context, default).ConfigureAwait(false);
        var secondHits = await second.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstHits.Select(hit => hit.Id).ToArray(), Is.EqualTo([10, 20]));
            Assert.That(firstRequests, Is.EqualTo(2));
            Assert.That(
                secondHits.Select(hit => hit.Id).ToArray(),
                Is.EqualTo([10, 20, 30, 11, 21, 31]));
            Assert.That(_requests, Has.Count.EqualTo(3));
        }
    }

    [TestCase("count")]
    [TestCase("order")]
    [TestCase("identity")]
    [TestCase("configuration")]
    public async Task ContinueSearchAsync_RejectsChangedSources_WhenAnExistingCheckpointIsValidated(string change)
    {
        // Arrange
        _genres["action"] = 28;
        _genres["comedy"] = 35;
        _genres["drama"] = 18;
        var pages = new Dictionary<string, IReadOnlyList<MovieSearchHitDto>>(StringComparer.Ordinal)
        {
            ["28"] =
            [
                new MovieSearchHitDto { Id = 10, Title = "Alpha" },
                new MovieSearchHitDto { Id = 11, Title = "Bravo" }
            ],
            ["35"] =
            [
                new MovieSearchHitDto { Id = 20, Title = "Charlie" },
                new MovieSearchHitDto { Id = 21, Title = "Delta" }
            ],
            ["18"] =
            [
                new MovieSearchHitDto { Id = 30, Title = "Echo" },
                new MovieSearchHitDto { Id = 31, Title = "Foxtrot" }
            ]
        };
        _client.Setup(client => client.GetAsync<SearchEnvelopeDto<MovieSearchHitDto>>(
                It.IsAny<RequestContext>(),
                It.IsAny<Request>(),
                It.IsAny<CancellationToken>()))
            .Callback((RequestContext _, Request request, CancellationToken _) => _requests.Add(request))
            .ReturnsAsync((RequestContext _, Request request, CancellationToken _) =>
                new SearchEnvelopeDto<MovieSearchHitDto>
                {
                    Page = 1,
                    TotalPages = 1,
                    TotalResults = 2,
                    Results = pages[request.QueryParameters.Single(pair => pair.Key == "with_genres").Value]
                });
        var names = new List<string> { "action", "comedy", "drama" };
        var genreFilter = new GenreFilter(
            false,
            [[new GenrePredicate(CriteriaValueOperator.In, false, names)]]);
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
            Fingerprint = "fetch-2",
            DiscoverParameters =
            [
                new DiscoverParameter
                {
                    Name = "with_genres",
                    Combination = DiscoverCombination.And,
                    GenreFilters = [genreFilter]
                }
            ],
            Resolve = new ResolvePlanNode
            {
                Id = 1,
                Precondition = PlanPrecondition.None,
                Contributes = "resolve",
                Route = ContentRoute.Movie,
                SubjectBindings = [],
                GenreFilters = [genreFilter],
                ResolveImageBase = false
            }
        };
        var filter = new FilterPlanNode
        {
            Id = 3,
            Precondition = PlanPrecondition.None,
            Contributes = "filter",
            Route = ContentRoute.Movie,
            Child = fetch,
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
        };
        var plan = new QueryPlan
        {
            Root = filter,
            Warnings = [],
            RequestedPage = 1,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "continued-plan"
        };
        var planner = new Mock<IQueryPlanner>();
        planner.Setup(value => value.Plan(It.IsAny<QueryRequest>())).Returns(plan);
        var settings = new Mock<ISettingsResolver>();
        settings.Setup(value => value.AcquireContextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Context);
        var engine = new Magpie.Extensions.Tmdb.QueryEngine.QueryEngine(settings.Object, _subject, _cache.Object,
            _client.Object, planner.Object, NullLogger<Magpie.Extensions.Tmdb.QueryEngine.QueryEngine>.Instance);
        var searcher = new ContentMetadataSearcher(engine, settings.Object, _client.Object, _cacheFactory,
            _cache.Object, NullLogger<ContentMetadataSearcher>.Instance);

        // Act
        var first = await searcher.ContinueSearchAsync("movie", new(), 1, "en-US", null).ConfigureAwait(false);
        switch (change)
        {
            case "count": names.Remove("drama"); break;
            case "order":
                _genres.Clear();
                _genres["drama"] = 18;
                _genres["comedy"] = 35;
                _genres["action"] = 28;
                break;
            case "identity": _genres["action"] = 99; break;
            case "configuration":
                settings.Setup(value => value.AcquireContextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Context with { ApiKey = "another-key" });
                break;
        }

        var exception = Assert.ThrowsAsync<Magpie.Sdk.Content.MetadataSearchContinuationException>(() =>
            searcher.ContinueSearchAsync("movie", new(), 0, "en-US", first.Continuation));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception, Is.Not.Null);
            Assert.That(_requests, Has.Count.EqualTo(1));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ContinueSearchAsync_PreservesInterleaving_WhenASourceFailsOrAPageStopsMidRound(bool fail)
    {
        // Arrange
        _genres["action"] = 28;
        _genres["comedy"] = 35;
        _genres["drama"] = 18;
        var failSecond = fail;
        var pages = new Dictionary<string, IReadOnlyList<MovieSearchHitDto>>(StringComparer.Ordinal)
        {
            ["28"] =
            [
                new MovieSearchHitDto { Id = 10, Title = "Alpha" },
                new MovieSearchHitDto { Id = 11, Title = "Bravo" }
            ],
            ["35"] =
            [
                new MovieSearchHitDto { Id = 20, Title = "Charlie" },
                new MovieSearchHitDto { Id = 21, Title = "Delta" }
            ],
            ["18"] =
            [
                new MovieSearchHitDto { Id = 30, Title = "Echo" },
                new MovieSearchHitDto { Id = 31, Title = "Foxtrot" }
            ]
        };
        _client.Setup(client => client.GetAsync<SearchEnvelopeDto<MovieSearchHitDto>>(
                It.IsAny<RequestContext>(),
                It.IsAny<Request>(),
                It.IsAny<CancellationToken>()))
            .Callback((RequestContext _, Request request, CancellationToken _) => _requests.Add(request))
            .ReturnsAsync((RequestContext _, Request request, CancellationToken _) =>
            {
                if (failSecond && request.QueryParameters.Single(pair => pair.Key == "with_genres").Value == "35")
                {
                    failSecond = false;
                    throw new System.Net.Http.HttpRequestException("fixture failure");
                }

                return new SearchEnvelopeDto<MovieSearchHitDto>
                {
                    Page = 1,
                    TotalPages = 1,
                    TotalResults = 2,
                    Results = pages[request.QueryParameters.Single(pair => pair.Key == "with_genres").Value]
                };
            });
        var names = new List<string> { "action", "comedy", "drama" };
        var genreFilter = new GenreFilter(
            false,
            [[new GenrePredicate(CriteriaValueOperator.In, false, names)]]);
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
            Fingerprint = "fetch-2",
            DiscoverParameters =
            [
                new DiscoverParameter
                {
                    Name = "with_genres",
                    Combination = DiscoverCombination.And,
                    GenreFilters = [genreFilter]
                }
            ],
            Resolve = new ResolvePlanNode
            {
                Id = 1,
                Precondition = PlanPrecondition.None,
                Contributes = "resolve",
                Route = ContentRoute.Movie,
                SubjectBindings = [],
                GenreFilters = [genreFilter],
                ResolveImageBase = false
            }
        };
        var filter = new FilterPlanNode
        {
            Id = 3,
            Precondition = PlanPrecondition.None,
            Contributes = "filter",
            Route = ContentRoute.Movie,
            Child = fetch,
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
        };
        var plan = new QueryPlan
        {
            Root = filter,
            Warnings = [],
            RequestedPage = 1,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "continued-plan"
        };
        var planner = new Mock<IQueryPlanner>();
        planner.Setup(value => value.Plan(It.IsAny<QueryRequest>())).Returns(plan);
        var settings = new Mock<ISettingsResolver>();
        settings.Setup(value => value.AcquireContextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Context);
        var engine = new Magpie.Extensions.Tmdb.QueryEngine.QueryEngine(settings.Object, _subject, _cache.Object,
            _client.Object, planner.Object, NullLogger<Magpie.Extensions.Tmdb.QueryEngine.QueryEngine>.Instance);
        var searcher = new ContentMetadataSearcher(engine, settings.Object, _client.Object, _cacheFactory,
            _cache.Object, NullLogger<ContentMetadataSearcher>.Instance);

        // Act
        var first = await searcher.ContinueSearchAsync("movie", new(), 5, "en-US", null).ConfigureAwait(false);
        var second = await searcher.ContinueSearchAsync("movie", new(), 10, "en-US", first.Continuation)
            .ConfigureAwait(false);
        var replay = await searcher.ContinueSearchAsync("movie", new(), 10, "en-US", first.Continuation)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Page.Matches.Select(value => value.ExternalId),
                fail ? Is.EqualTo(["10", "30", "11", "31"]) : Is.EqualTo(["10", "20", "30", "11", "21"]));
            Assert.That(first.Page.Warnings.Count > 0, Is.EqualTo(fail));
            Assert.That(second.Page.Matches.Select(value => value.ExternalId),
                fail ? Is.EqualTo(["20", "21"]) : Is.EqualTo(["31"]));
            Assert.That(second.Page.PageInfo.HasMore, Is.False);
            Assert.That(replay.Page.Matches.Select(value => value.ExternalId),
                Is.EqualTo(second.Page.Matches.Select(value => value.ExternalId)));
            Assert.That(_requests.Select(request =>
                request.QueryParameters.Single(pair => pair.Key == "with_genres").Value),
                fail ? Is.EqualTo(["28", "35", "18", "35", "35"]) :
                    Is.EqualTo(["28", "35", "18", "28", "35", "18", "28", "35", "18"]));
        }
    }

    [Test]
    public async Task ExecuteAsync_ContinuesScanSources_WhenOneSourceFails(
        [Values] bool continuation,
        [Values] bool failureFirst,
        [Values] bool empty,
        [Values] bool allFail)
    {
        // Arrange
        _genres["action"] = 28;
        _genres["comedy"] = 35;
        _genres["drama"] = 18;
        var failedGenre = failureFirst ? "28" : "18";
        var pages = new Dictionary<string, IReadOnlyList<MovieSearchHitDto>>(StringComparer.Ordinal)
        {
            ["28"] =
            [
                new MovieSearchHitDto { Id = 10, Title = "Alpha" },
                new MovieSearchHitDto { Id = 11, Title = "Bravo" }
            ],
            ["35"] =
            [
                new MovieSearchHitDto { Id = 20, Title = "Charlie" },
                new MovieSearchHitDto { Id = 21, Title = "Delta" }
            ],
            ["18"] =
            [
                new MovieSearchHitDto { Id = 30, Title = "Echo" },
                new MovieSearchHitDto { Id = 31, Title = "Foxtrot" }
            ]
        };
        _client.Setup(client => client.GetAsync<SearchEnvelopeDto<MovieSearchHitDto>>(
                It.IsAny<RequestContext>(),
                It.IsAny<Request>(),
                It.IsAny<CancellationToken>()))
            .Callback((RequestContext _, Request request, CancellationToken _) => _requests.Add(request))
            .ReturnsAsync((RequestContext _, Request request, CancellationToken _) =>
            {
                if (allFail || request.QueryParameters.Single(pair => pair.Key == "with_genres").Value == failedGenre)
                {
                    throw new System.Net.Http.HttpRequestException("fixture failure");
                }

                return new SearchEnvelopeDto<MovieSearchHitDto>
                {
                    Page = 1,
                    TotalPages = 1,
                    TotalResults = 2,
                    Results = empty ? [] :
                        pages[request.QueryParameters.Single(pair => pair.Key == "with_genres").Value]
                };
            });
        var names = new List<string> { "action", "comedy", "drama" };
        var genreFilter = new GenreFilter(
            false,
            [[new GenrePredicate(CriteriaValueOperator.In, false, names)]]);
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
            Fingerprint = "fetch-2",
            DiscoverParameters =
            [
                new DiscoverParameter
                {
                    Name = "with_genres",
                    Combination = DiscoverCombination.And,
                    GenreFilters = [genreFilter]
                }
            ],
            Resolve = new ResolvePlanNode
            {
                Id = 1,
                Precondition = PlanPrecondition.None,
                Contributes = "resolve",
                Route = ContentRoute.Movie,
                SubjectBindings = [],
                GenreFilters = [genreFilter],
                ResolveImageBase = false
            }
        };
        var filter = new FilterPlanNode
        {
            Id = 3,
            Precondition = PlanPrecondition.None,
            Contributes = "filter",
            Route = ContentRoute.Movie,
            Child = fetch,
            ResidualPredicates = [],
            TextFilters = [],
            PublishDateFilters = [],
            DurationFilters = [],
            GenreFilters = [],
            SubjectBindings = [],
            ContentIdFilters = [],
            ElementFilters = [],
            RequestedPage = 1,
            MaximumResults = 25
        };
        var plan = new QueryPlan
        {
            Root = filter,
            Warnings = [],
            RequestedPage = 1,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "continued-plan"
        };
        var context = Context with { Progress = continuation ? new QueryProgress() : null };

        // Act and assert
        var execution = await _subject.ExecuteAsync(plan, context, default).ConfigureAwait(false);
        if (allFail)
        {
            Assert.ThrowsAsync<System.Net.Http.HttpRequestException>(async () =>
                await execution.Hits.ToListAsync().ConfigureAwait(false));
        }
        else
        {
            var hits = await execution.Hits.ToListAsync().ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(hits, Has.Count.EqualTo(empty ? 0 : 4));
                Assert.That(execution.Warnings, Is.Not.Empty);
            }
        }

        Assert.That(_requests, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task ExecuteAsync_RetainsCachedScanAnswers_WhenTheFailedSourceStillFails([Values] bool empty)
    {
        // Arrange
        _genres["action"] = 28;
        _genres["comedy"] = 35;
        _genres["drama"] = 18;
        var failedGenre = "28";
        var pages = new Dictionary<string, IReadOnlyList<MovieSearchHitDto>>(StringComparer.Ordinal)
        {
            ["28"] =
            [
                new MovieSearchHitDto { Id = 10, Title = "Alpha" },
                new MovieSearchHitDto { Id = 11, Title = "Bravo" }
            ],
            ["35"] =
            [
                new MovieSearchHitDto { Id = 20, Title = "Charlie" },
                new MovieSearchHitDto { Id = 21, Title = "Delta" }
            ],
            ["18"] =
            [
                new MovieSearchHitDto { Id = 30, Title = "Echo" },
                new MovieSearchHitDto { Id = 31, Title = "Foxtrot" }
            ]
        };
        _client.Setup(client => client.GetAsync<SearchEnvelopeDto<MovieSearchHitDto>>(
                It.IsAny<RequestContext>(),
                It.IsAny<Request>(),
                It.IsAny<CancellationToken>()))
            .Callback((RequestContext _, Request request, CancellationToken _) => _requests.Add(request))
            .ReturnsAsync((RequestContext _, Request request, CancellationToken _) =>
            {
                if (request.QueryParameters.Single(pair => pair.Key == "with_genres").Value == failedGenre)
                {
                    throw new System.Net.Http.HttpRequestException("fixture failure");
                }

                return new SearchEnvelopeDto<MovieSearchHitDto>
                {
                    Page = 1,
                    TotalPages = 1,
                    TotalResults = 2,
                    Results = empty ? [] :
                        pages[request.QueryParameters.Single(pair => pair.Key == "with_genres").Value]
                };
            });
        var names = new List<string> { "action", "comedy", "drama" };
        var genreFilter = new GenreFilter(
            false,
            [[new GenrePredicate(CriteriaValueOperator.In, false, names)]]);
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
            Fingerprint = "fetch-2",
            DiscoverParameters =
            [
                new DiscoverParameter
                {
                    Name = "with_genres",
                    Combination = DiscoverCombination.And,
                    GenreFilters = [genreFilter]
                }
            ],
            Resolve = new ResolvePlanNode
            {
                Id = 1,
                Precondition = PlanPrecondition.None,
                Contributes = "resolve",
                Route = ContentRoute.Movie,
                SubjectBindings = [],
                GenreFilters = [genreFilter],
                ResolveImageBase = false
            }
        };
        var filter = new FilterPlanNode
        {
            Id = 3,
            Precondition = PlanPrecondition.None,
            Contributes = "filter",
            Route = ContentRoute.Movie,
            Child = fetch,
            ResidualPredicates = [],
            TextFilters = [],
            PublishDateFilters = [],
            DurationFilters = [],
            GenreFilters = [],
            SubjectBindings = [],
            ContentIdFilters = [],
            ElementFilters = [],
            RequestedPage = 1,
            MaximumResults = 25
        };
        var plan = new QueryPlan
        {
            Root = filter,
            Warnings = [],
            RequestedPage = 1,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "continued-plan"
        };
        var context = Context;

        // Act
        var first = await _subject.ExecuteAsync(plan, context, default).ConfigureAwait(false);
        var firstHits = await first.Hits.ToListAsync().ConfigureAwait(false);
        var second = await _subject.ExecuteAsync(plan, context, default).ConfigureAwait(false);
        var secondHits = await second.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstHits, Has.Count.EqualTo(empty ? 0 : 4));
            Assert.That(secondHits.Select(hit => hit.Id), Is.EqualTo(firstHits.Select(hit => hit.Id)));
            Assert.That(second.Diagnostics.ReceivedResponse, Is.True);
            Assert.That(second.Warnings, Is.Not.Empty);
            Assert.That(_requests, Has.Count.EqualTo(4));
        }
    }

    [Test]
    public async Task ExecuteAsync_ScansThePersonFilteredDiscoverFromPageOne_WhenTheFallbackScannedTheQueryFirst()
    {
        // Arrange
        _cache.SetupSequence(cache => cache.GetSubjectResolutionsAsync(
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
            ])
            .ReturnsAsync([
                new SubjectResolution { Kind = SubjectResolutionKind.Resolved, Id = 31, Name = "Tom Hanks" }
            ]);
        _client.SetupSequence(client => client.GetAsync<SearchEnvelopeDto<MovieSearchHitDto>>(
                It.IsAny<RequestContext>(),
                It.IsAny<Request>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchEnvelopeDto<MovieSearchHitDto>
            {
                Page = 1,
                TotalPages = 1,
                TotalResults = 1,
                Results = [new MovieSearchHitDto { Id = 10, Title = "Unfiltered" }]
            })
            .ReturnsAsync(new SearchEnvelopeDto<MovieSearchHitDto>
            {
                Page = 1,
                TotalPages = 1,
                TotalResults = 1,
                Results = [new MovieSearchHitDto { Id = 13, Title = "Philadelphia" }]
            });
        var bindings = new[]
        {
            new SubjectResolutionBinding(
                "person-0",
                new SubjectFilter(
                    false,
                    [new SubjectAlternative([SubjectRole.Cast], [[new SubjectIdentity("Tom Hanks", null, null)]])]))
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
        var plan = new QueryPlan
        {
            Root = new CoalescePlanNode
            {
                Id = 4,
                Precondition = PlanPrecondition.None,
                Contributes = "first applicable anchor",
                Children =
                [
                    new FetchPlanNode
                    {
                        Id = 2,
                        Precondition = new PlanPrecondition { Kind = PlanPreconditionKind.NodeApplicable, NodeId = 1 },
                        Contributes = "candidate rows",
                        Route = ContentRoute.Movie,
                        AnchorKind = QueryAnchorKind.Discover,
                        SubjectBindings = bindings,
                        PushedFilters = [],
                        RequestedPage = 1,
                        Fingerprint = "movie-cast",
                        Resolve = resolve,
                        RequiresLogicalStream = true,
                        DiscoverParameters =
                        [
                            new DiscoverParameter
                            {
                                Name = "with_cast",
                                Combination = DiscoverCombination.And,
                                SubjectBindingKeys = ["person-0"]
                            }
                        ]
                    },
                    new FetchPlanNode
                    {
                        Id = 3,
                        Precondition = new PlanPrecondition { Kind = PlanPreconditionKind.NodeFailed, NodeId = 1 },
                        Contributes = "fallback candidates when relationship resolution fails",
                        Route = ContentRoute.Movie,
                        AnchorKind = QueryAnchorKind.Discover,
                        SubjectBindings = [],
                        IgnoreSubjectFilters = true,
                        PushedFilters = [],
                        RequestedPage = 1,
                        Fingerprint = "movie-cast",
                        RequiresLogicalStream = true
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
        var fallback = await _subject.ExecuteAsync(plan, Context, default).ConfigureAwait(false);
        await fallback.Hits.ToListAsync().ConfigureAwait(false);
        var primary = await _subject.ExecuteAsync(plan, Context, default).ConfigureAwait(false);
        var hits = await primary.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(hits.Select(hit => hit.Id), Is.EqualTo([13]));
            _client.Verify(
                client => client.GetAsync<SearchEnvelopeDto<MovieSearchHitDto>>(
                    It.IsAny<RequestContext>(),
                    It.Is<Request>(request =>
                        request.QueryParameters.Any(pair => pair.Key == "with_cast" && pair.Value == "31") &&
                        request.QueryParameters.Any(pair => pair.Key == "page" && pair.Value == "1")),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }
}
