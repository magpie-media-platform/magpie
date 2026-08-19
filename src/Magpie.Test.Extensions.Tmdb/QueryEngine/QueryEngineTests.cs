using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Tmdb;
using Magpie.Extensions.Tmdb.QueryEngine;
using Magpie.Models.Data;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

using TmdbQueryEngine = Magpie.Extensions.Tmdb.QueryEngine.QueryEngine;

namespace Magpie.Test.Extensions.Tmdb.QueryEngine;

[TestFixture]
public class QueryEngineTests
{
    private Mock<ISettingsResolver> _settingsResolver = null!;
    private Mock<IQueryExecutor> _executor = null!;
    private Mock<IQueryCoordinator> _cache = null!;
    private Mock<IQueryPlanner> _planner = null!;
    private IQueryEngine _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _settingsResolver = new Mock<ISettingsResolver>();
        _executor = new Mock<IQueryExecutor>();
        _cache = new Mock<IQueryCoordinator>();
        _planner = new Mock<IQueryPlanner>();
        _subject = new TmdbQueryEngine(
            _settingsResolver.Object,
            _executor.Object,
            _cache.Object,
            new Mock<ITmdbClient>().Object,
            _planner.Object,
            NullLogger<TmdbQueryEngine>.Instance);
    }

    [Test]
    public async Task SearchAsync_ReturnsWarningWithoutExecuting_WhenProviderIsUnconfigured()
    {
        // Arrange
        _planner.Setup(planner => planner.Plan(It.IsAny<QueryRequest>()))
            .Returns(new QueryPlan
            {
                Root = new UnionPlanNode
                {
                    Id = 1,
                    Precondition = PlanPrecondition.None,
                    Contributes = "movie search",
                    Children = []
                },
                Warnings = [],
                RequestedPage = 1,
                MaximumResults = 25,
                DefaultLanguagePreference = "en-US",
                Fingerprint = "plan"
            });
        _settingsResolver.Setup(resolver => resolver.AcquireContextAsync(
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((RequestContext?)null);

        // Act
        var execution = await _subject.SearchAsync(
            new QueryRequest
            {
                Route = ContentRoute.Movie,
                Criteria = new MetadataCriteria(),
                Page = 1,
                DefaultLanguagePreference = "en-US"
            },
            default).ConfigureAwait(false);
        var hits = await execution.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(execution.Supported, Is.True);
            Assert.That(
                execution.Warnings.Select(warning => warning),
                Has.Some.Contains("not configured"));
            Assert.That(hits, Is.Empty);
            Assert.That(execution.PagingMode, Is.EqualTo(QueryPagingMode.LogicalStream));
        }

        _executor.VerifyNoOtherCalls();
    }

    [Test]
    public async Task SearchAsync_ExecutesThePlannedTree_WhenTheContextResolves()
    {
        // Arrange
        var plan = new QueryPlan
        {
            Root = new UnionPlanNode
            {
                Id = 1,
                Precondition = PlanPrecondition.None,
                Contributes = "movie search",
                Children = []
            },
            Warnings = [],
            RequestedPage = 1,
            MaximumResults = 25,
            DefaultLanguagePreference = "en-US",
            Fingerprint = "plan"
        };
        var request = new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = new MetadataCriteria(),
            Page = 1,
            DefaultLanguagePreference = "en-US"
        };
        var context = new RequestContext { ApiKey = "key", Language = "en-US" };
        List<SearchHit> planned = [new() { Route = ContentRoute.Movie, Id = 1726 }];
        _planner.Setup(planner => planner.Plan(It.IsAny<QueryRequest>())).Returns(plan);
        _settingsResolver.Setup(resolver => resolver.AcquireContextAsync(
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(context);
        _executor.Setup(executor => executor.ExecuteAsync(
                It.IsAny<QueryPlan>(),
                It.IsAny<RequestContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryExecution
            {
                Supported = true,
                Hits = planned.ToAsyncEnumerable(),
                Warnings = [],
                RequestedPage = 1,
                PagingMode = QueryPagingMode.LogicalStream
            });
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "magpie.extensions.tmdb",
            Sample = (ref ActivityCreationOptions<ActivityContext> options) =>
                ActivitySamplingResult.PropagationData
        };
        ActivitySource.AddActivityListener(listener);

        // Act
        var execution = await _subject.SearchAsync(request, default).ConfigureAwait(false);
        var hits = await execution.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(hits.Single().Id, Is.EqualTo(1726));
            _planner.Verify(planner => planner.Plan(request), Times.Once);
            _executor.Verify(
                executor => executor.ExecuteAsync(
                    plan,
                    It.Is<RequestContext>(actual =>
                        actual.ApiKey == "key" &&
                        actual.Language == "en-US" &&
                        actual.Telemetry != null &&
                        actual.Telemetry.Activity != null &&
                        actual.Telemetry.Activity.OperationName == "tmdb.query.execute" &&
                        "plan".Equals(actual.Telemetry.Activity.GetTagItem("magpie.query.fingerprint"))),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task SearchAsync_ResolvesOneImageBase_WhenSeveralHitsCarryPosters()
    {
        // Arrange
        List<SearchHit> planned =
        [
            new() { Route = ContentRoute.Movie, Id = 1, PosterPath = "/one.jpg" },
            new() { Route = ContentRoute.Movie, Id = 2 },
            new() { Route = ContentRoute.Movie, Id = 3, PosterPath = "/three.jpg" }
        ];
        _planner.Setup(planner => planner.Plan(It.IsAny<QueryRequest>()))
            .Returns(new QueryPlan
            {
                Root = new UnionPlanNode
                {
                    Id = 1,
                    Precondition = PlanPrecondition.None,
                    Contributes = "movie search",
                    Children = []
                },
                Warnings = [],
                RequestedPage = 1,
                MaximumResults = 25,
                DefaultLanguagePreference = "en-US",
                Fingerprint = "plan"
            });
        _settingsResolver.Setup(resolver => resolver.AcquireContextAsync(
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RequestContext { ApiKey = "key", Language = "en-US" });
        _cache.Setup(cache => cache.GetImageBaseUrlAsync(
                It.IsAny<RequestContext>(),
                It.IsAny<ITmdbClient>(),
                It.IsAny<ILogger>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://image.tmdb.org/t/p/");
        _executor.Setup(executor => executor.ExecuteAsync(
                It.IsAny<QueryPlan>(),
                It.IsAny<RequestContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryExecution
            {
                Supported = true,
                Hits = planned.ToAsyncEnumerable(),
                Warnings = [],
                RequestedPage = 1,
                PagingMode = QueryPagingMode.LogicalStream
            });

        // Act
        var execution = await _subject.SearchAsync(
            new QueryRequest
            {
                Route = ContentRoute.Movie,
                Criteria = new MetadataCriteria(),
                Page = 1,
                DefaultLanguagePreference = "en-US"
            },
            default).ConfigureAwait(false);
        var hits = await execution.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(hits[0].ImageUrl!.ToString(), Is.EqualTo("https://image.tmdb.org/t/p/w92/one.jpg"));
            Assert.That(hits[1].ImageUrl, Is.Null);
            Assert.That(hits[2].ImageUrl!.ToString(), Is.EqualTo("https://image.tmdb.org/t/p/w92/three.jpg"));
        }

        _cache.Verify(
            cache => cache.GetImageBaseUrlAsync(
                It.IsAny<RequestContext>(),
                It.IsAny<ITmdbClient>(),
                It.IsAny<ILogger>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task SearchAsync_AttributesTheConfigurationCall_WhenTheHitNamesItsPlanNode()
    {
        // Arrange
        List<SearchHit> planned =
        [
            new()
            {
                Route = ContentRoute.Movie,
                Id = 1,
                PosterPath = "/one.jpg",
                SourcePlanNodeId = 7
            }
        ];
        _planner.Setup(planner => planner.Plan(It.IsAny<QueryRequest>()))
            .Returns(new QueryPlan
            {
                Root = new UnionPlanNode
                {
                    Id = 1,
                    Precondition = PlanPrecondition.None,
                    Contributes = "movie search",
                    Children = []
                },
                Warnings = [],
                RequestedPage = 1,
                MaximumResults = 25,
                DefaultLanguagePreference = "en-US",
                Fingerprint = "plan"
            });
        _settingsResolver.Setup(resolver => resolver.AcquireContextAsync(
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RequestContext { ApiKey = "key", Language = "en-US" });
        _cache.Setup(cache => cache.GetImageBaseUrlAsync(
                It.IsAny<RequestContext>(),
                It.IsAny<ITmdbClient>(),
                It.IsAny<ILogger>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://image.tmdb.org/t/p/");
        _executor.Setup(executor => executor.ExecuteAsync(
                It.IsAny<QueryPlan>(),
                It.IsAny<RequestContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryExecution
            {
                Supported = true,
                Hits = planned.ToAsyncEnumerable(),
                Warnings = [],
                RequestedPage = 1,
                PagingMode = QueryPagingMode.LogicalStream
            });

        // Act
        var execution = await _subject.SearchAsync(
            new QueryRequest
            {
                Route = ContentRoute.Movie,
                Criteria = new MetadataCriteria(),
                Page = 1,
                DefaultLanguagePreference = "en-US"
            },
            default).ConfigureAwait(false);
        await execution.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        _cache.Verify(
            cache => cache.GetImageBaseUrlAsync(
                It.Is<RequestContext>(context => context.PlanNodeId == 7),
                It.IsAny<ITmdbClient>(),
                It.IsAny<ILogger>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task SearchAsync_WarnsAndEndsTheStream_WhenExecutionFaultsMidEnumeration()
    {
        // Arrange
        static async IAsyncEnumerable<SearchHit> Faulting()
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield return new SearchHit { Route = ContentRoute.Movie, Id = 1 };
            throw new InvalidOperationException("remote boom");
        }

        _planner.Setup(planner => planner.Plan(It.IsAny<QueryRequest>()))
            .Returns(new QueryPlan
            {
                Root = new UnionPlanNode
                {
                    Id = 1,
                    Precondition = PlanPrecondition.None,
                    Contributes = "movie search",
                    Children = []
                },
                Warnings = [],
                RequestedPage = 1,
                MaximumResults = 25,
                DefaultLanguagePreference = "en-US",
                Fingerprint = "plan"
            });
        _settingsResolver.Setup(resolver => resolver.AcquireContextAsync(
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RequestContext { ApiKey = "key", Language = "en-US" });
        _executor.Setup(executor => executor.ExecuteAsync(
                It.IsAny<QueryPlan>(),
                It.IsAny<RequestContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryExecution
            {
                Supported = true,
                Hits = Faulting(),
                Warnings = [],
                RequestedPage = 1,
                PagingMode = QueryPagingMode.LogicalStream
            });

        // Act
        var execution = await _subject.SearchAsync(
            new QueryRequest
            {
                Route = ContentRoute.Movie,
                Criteria = new MetadataCriteria(),
                Page = 1,
                DefaultLanguagePreference = "en-US"
            },
            default).ConfigureAwait(false);
        var hits = await execution.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(hits, Has.Count.EqualTo(1));
            Assert.That(
                execution.Warnings.Select(warning => warning),
                Is.EqualTo(["TMDb could not complete the remote query."]));
            Assert.That(execution.Diagnostics.StreamFaulted, Is.True);
        }
    }

    [Test]
    public async Task SearchAsync_DoesNotRepeatTheFailureWarning_WhenTheExecutionAlreadyReportedIt()
    {
        // Arrange
        static async IAsyncEnumerable<SearchHit> Faulting()
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield return new SearchHit { Route = ContentRoute.Movie, Id = 1 };
            throw new InvalidOperationException("remote boom");
        }

        var warning = "TMDb could not complete the remote query.";

        _planner.Setup(planner => planner.Plan(It.IsAny<QueryRequest>()))
            .Returns(new QueryPlan
            {
                Root = new UnionPlanNode
                {
                    Id = 1,
                    Precondition = PlanPrecondition.None,
                    Contributes = "movie search",
                    Children = []
                },
                Warnings = [],
                RequestedPage = 1,
                MaximumResults = 25,
                DefaultLanguagePreference = "en-US",
                Fingerprint = "plan"
            });
        _settingsResolver.Setup(resolver => resolver.AcquireContextAsync(
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RequestContext { ApiKey = "key", Language = "en-US" });
        _executor.Setup(executor => executor.ExecuteAsync(
                It.IsAny<QueryPlan>(),
                It.IsAny<RequestContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryExecution
            {
                Supported = true,
                Hits = Faulting(),
                Warnings = [warning],
                RequestedPage = 1,
                PagingMode = QueryPagingMode.LogicalStream
            });

        // Act
        var execution = await _subject.SearchAsync(
            new QueryRequest
            {
                Route = ContentRoute.Movie,
                Criteria = new MetadataCriteria(),
                Page = 1,
                DefaultLanguagePreference = "en-US"
            },
            default).ConfigureAwait(false);
        await execution.Hits.ToListAsync().ConfigureAwait(false);

        // Assert
        Assert.That(execution.Warnings, Has.Count.EqualTo(1));
    }

    [Test]
    public void SearchAsync_PropagatesCancellation_WhenTheCallerCancelsMidEnumeration()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        async IAsyncEnumerable<SearchHit> Cancelling()
        {
            await Task.CompletedTask.ConfigureAwait(false);
            cancellation.Token.ThrowIfCancellationRequested();
            yield return new SearchHit { Route = ContentRoute.Movie, Id = 1 };
        }

        _planner.Setup(planner => planner.Plan(It.IsAny<QueryRequest>()))
            .Returns(new QueryPlan
            {
                Root = new UnionPlanNode
                {
                    Id = 1,
                    Precondition = PlanPrecondition.None,
                    Contributes = "movie search",
                    Children = []
                },
                Warnings = [],
                RequestedPage = 1,
                MaximumResults = 25,
                DefaultLanguagePreference = "en-US",
                Fingerprint = "plan"
            });
        _settingsResolver.Setup(resolver => resolver.AcquireContextAsync(
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RequestContext { ApiKey = "key", Language = "en-US" });
        _executor.Setup(executor => executor.ExecuteAsync(
                It.IsAny<QueryPlan>(),
                It.IsAny<RequestContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryExecution
            {
                Supported = true,
                Hits = Cancelling(),
                Warnings = [],
                RequestedPage = 1,
                PagingMode = QueryPagingMode.LogicalStream
            });

        // Act
        var execution = _subject.SearchAsync(
            new QueryRequest
            {
                Route = ContentRoute.Movie,
                Criteria = new MetadataCriteria(),
                Page = 1,
                DefaultLanguagePreference = "en-US"
            },
            default).GetAwaiter().GetResult();

        // Assert
        Assert.CatchAsync<OperationCanceledException>(async () =>
            await execution.Hits.ToListAsync().ConfigureAwait(false));
    }
}
