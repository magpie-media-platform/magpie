using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Wikidata;
using Magpie.Extensions.Wikidata.Mapping;
using Magpie.Extensions.Wikidata.QueryEngine;
using Magpie.Models.Enums;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Wikidata;

[TestFixture]
internal sealed class QueryTelemetryTests
{
    private readonly ConcurrentQueue<Activity> _activities = new();

    private ActivityListener _listener = null!;

    [SetUp]
    public void SetUp()
    {
        _activities.Clear();
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "magpie.extensions.wikidata",
            Sample = (ref ActivityCreationOptions<ActivityContext> options) =>
                ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = (ref ActivityCreationOptions<string> options) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _activities.Enqueue
        };
        ActivitySource.AddActivityListener(_listener);
    }

    [TearDown]
    public void TearDown() => _listener.Dispose();

    [Test]
    public async Task SearchAsync_ReportsRowsRequestsAndCompletedTerminalReason_WhenExecuteActivity()
    {
        // Arrange
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            .. Enumerable.Range(1, 2).Select(index => new Dictionary<string, SparqlBindingDto>
            {
                ["item"] = new SparqlBindingDto { Value = "Q" + index },
                ["itemLabel"] = new SparqlBindingDto { Value = "Movie " + index },
                ["type"] = new SparqlBindingDto { Value = "Q11424" }
            })
        ];
        var client = new Mock<IWikidataClient>();
        client.Setup(value => value.ExecuteSparqlAsync(
                It.IsAny<SparqlRequest>(),
                It.IsAny<RequestContext>(),
                It.IsAny<CancellationToken>()))
            .Callback((SparqlRequest _, RequestContext requestContext, CancellationToken _) =>
                requestContext.UpstreamRequestObserver?.Invoke())
            .ReturnsAsync(rows);
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns(async (
                string _,
                Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>> factory,
                CancellationToken token,
                Action<SearchCacheProvenance>? provenance) =>
            {
                provenance?.Invoke(SearchCacheProvenance.Remote);
                return new SearchCacheResult(
                    await factory(token).ConfigureAwait(false),
                    SearchCacheProvenance.Remote);
            });
        IQueryExecutor subject = new QueryExecutor(
            client.Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Text,
                    Source = "Title",
                    Hydration = ResidualHydration.None,
                    TextField = ResidualTextField.Title,
                    Criterion = new ResidualStringCriterion
                    {
                        Operator = CriteriaValueOperator.Regex,
                        Negate = false,
                        Values = ["^Movie 1$"]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };
        using var root = new Activity("test").Start();

        // Act
        await subject.SearchAsync(plan, context, default).ConfigureAwait(false);

        // Assert
        var execute = _activities.Single(activity =>
            activity.OperationName == "wikidata.query.execute" &&
            activity.RootId == root.RootId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(execute.GetTagItem("magpie.query.candidate_window_rows_fetched"), Is.EqualTo(2));
            Assert.That(execute.GetTagItem("magpie.query.rows_surviving"), Is.EqualTo(1));
            Assert.That(execute.GetTagItem("magpie.query.request_count"), Is.EqualTo(1));
            Assert.That(execute.GetTagItem("magpie.query.has_more"), Is.EqualTo(false));
            Assert.That(execute.GetTagItem("magpie.query.terminal_reason"), Is.EqualTo("completed"));
            Assert.That(execute.GetTagItem("magpie.query.fingerprint"), Has.Length.EqualTo(64));
            Assert.That(execute.Status, Is.EqualTo(ActivityStatusCode.Unset));
        }
    }

    [Test]
    public async Task SearchAsync_ReportsEveryUpstreamRequest_WhenEntitySearchPages()
    {
        // Arrange
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        var client = new Mock<IWikidataClient>();
        client.Setup(value => value.SearchEntitiesAsync(
                "The Mat",
                It.IsAny<RequestContext>(),
                It.IsAny<CancellationToken>()))
            .Callback((string _, RequestContext requestContext, CancellationToken _) =>
            {
                requestContext.UpstreamRequestObserver?.Invoke();
                requestContext.UpstreamRequestObserver?.Invoke();
            })
            .ReturnsAsync(["Q83495"]);
        client.Setup(value => value.ExecuteSparqlAsync(
                It.IsAny<SparqlRequest>(),
                It.IsAny<RequestContext>(),
                It.IsAny<CancellationToken>()))
            .Callback((SparqlRequest _, RequestContext requestContext, CancellationToken _) =>
                requestContext.UpstreamRequestObserver?.Invoke())
            .ReturnsAsync([]);
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitySearchAsync(
                It.IsAny<string>(),
                It.IsAny<Func<CancellationToken, Task<IReadOnlyList<string>>>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns(async (
                string _,
                Func<CancellationToken, Task<IReadOnlyList<string>>> factory,
                CancellationToken token,
                Action<SearchCacheProvenance>? provenance) =>
            {
                provenance?.Invoke(SearchCacheProvenance.Remote);
                return await factory(token).ConfigureAwait(false);
            });
        cache.Setup(value => value.GetSearchAsync(
                It.IsAny<string>(),
                It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns(async (
                string _,
                Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>> factory,
                CancellationToken token,
                Action<SearchCacheProvenance>? provenance) =>
            {
                provenance?.Invoke(SearchCacheProvenance.Remote);
                return new SearchCacheResult(
                    await factory(token).ConfigureAwait(false),
                    SearchCacheProvenance.Remote);
            });
        IQueryExecutor subject = new QueryExecutor(client.Object, cache.Object, NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.TitleSearch,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.Title,
                    Operator = CriteriaValueOperator.StartsWithString,
                    Values = ["The Mat"]
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };
        using var root = new Activity("test").Start();

        // Act
        await subject.SearchAsync(plan, context, default).ConfigureAwait(false);

        // Assert
        var execute = _activities.Single(activity =>
            activity.OperationName == "wikidata.query.execute" &&
            activity.RootId == root.RootId);
        Assert.That(execute.GetTagItem("magpie.query.request_count"), Is.EqualTo(3));
    }

    [Test]
    public async Task SearchAsync_EmitsResidualRejectionEvent_WhenResidualPrunesCandidate()
    {
        // Arrange
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            .. Enumerable.Range(1, 2).Select(index => new Dictionary<string, SparqlBindingDto>
            {
                ["item"] = new SparqlBindingDto { Value = "Q" + index },
                ["itemLabel"] = new SparqlBindingDto { Value = "Movie " + index },
                ["type"] = new SparqlBindingDto { Value = "Q11424" }
            })
        ];
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Text,
                    Source = "Title",
                    Hydration = ResidualHydration.None,
                    TextField = ResidualTextField.Title,
                    Criterion = new ResidualStringCriterion
                    {
                        Operator = CriteriaValueOperator.Regex,
                        Negate = false,
                        Values = ["^Movie 1$"]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };
        using var root = new Activity("test").Start();

        // Act
        await subject.SearchAsync(plan, context, default).ConfigureAwait(false);

        // Assert
        var execute = _activities.Single(activity =>
            activity.OperationName == "wikidata.query.execute" &&
            activity.RootId == root.RootId);
        var residuals = execute.Events.Single(value => value.Name == "residuals").Tags.ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(residuals, Has.Length.EqualTo(1));
            Assert.That(residuals[0].Key, Is.EqualTo("magpie.query.rejected.Title"));
            Assert.That(residuals[0].Value, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task SearchAsync_ReportsSkippedStages_WhenProjectionAndResidualsDoNotRun()
    {
        // Arrange
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            .. Enumerable.Range(1, 2).Select(index => new Dictionary<string, SparqlBindingDto>
            {
                ["item"] = new SparqlBindingDto { Value = "Q" + index },
                ["itemLabel"] = new SparqlBindingDto { Value = "Movie " + index },
                ["type"] = new SparqlBindingDto { Value = "Q11424" }
            })
        ];
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters = [],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true,
            ProjectCandidateDetails = false
        };
        using var root = new Activity("test").Start();

        // Act
        await subject.SearchAsync(plan, context, default).ConfigureAwait(false);

        // Assert
        var execute = _activities.Single(activity =>
            activity.OperationName == "wikidata.query.execute" &&
            activity.RootId == root.RootId);
        var skipped = execute.Events.Where(value => value.Name == "stage_skipped").ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(execute.GetTagItem("magpie.query.pruned_stages"), Is.EqualTo(2));
            Assert.That(skipped, Has.Length.EqualTo(2));
            Assert.That(skipped[0].Tags.ToArray(), Is.EqualTo(new KeyValuePair<string, object?>[]
            {
                new("magpie.request.role", "candidateProjection"),
                new("magpie.query.skip_reason", "not_requested")
            }));
            Assert.That(skipped[1].Tags.ToArray(), Is.EqualTo(new KeyValuePair<string, object?>[]
            {
                new("magpie.request.role", "residualEntityHydration"),
                new("magpie.query.skip_reason", "no_residual_filters")
            }));
        }
    }

    [Test]
    public async Task SearchAsync_ReportsRoleTargetRowsAndCacheMiss_WhenRequestActivity()
    {
        // Arrange
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            .. Enumerable.Range(1, 2).Select(index => new Dictionary<string, SparqlBindingDto>
            {
                ["item"] = new SparqlBindingDto { Value = "Q" + index },
                ["itemLabel"] = new SparqlBindingDto { Value = "Movie " + index },
                ["type"] = new SparqlBindingDto { Value = "Q11424" }
            })
        ];
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };
        using var root = new Activity("test").Start();

        // Act
        await subject.SearchAsync(plan, context, default).ConfigureAwait(false);

        // Assert
        var request = _activities.Single(activity =>
            activity.OperationName == "wikidata.query.request" &&
            activity.RootId == root.RootId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(request.Kind, Is.EqualTo(ActivityKind.Client));
            Assert.That(request.GetTagItem("magpie.request.role"), Is.EqualTo("wdqsWindows"));
            Assert.That(request.GetTagItem("magpie.cache.hit"), Is.EqualTo(false));
            Assert.That(request.GetTagItem("magpie.request.rows"), Is.EqualTo(2));
            Assert.That(
                request.GetTagItem("magpie.request.target"),
                Does.Contain("SELECT ?item ?type ?itemLabel ?itemDescription"));
        }
    }

    [Test]
    public async Task SearchAsync_ReportsKnownEmptyTerminalReasonWithRedactedPlanEvent_WhenExecuteActivity()
    {
        // Arrange
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            new Mock<IQueryCoordinator>(MockBehavior.Strict).Object,
            NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Empty,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.Title,
                    Values = ["unique-secret"]
                }
            ],
            CriterionDispositions =
            [
                new CriterionDisposition
                {
                    Source = "Title[0]",
                    Disposition = CriterionDispositionKind.ProvablyUnsatisfiable,
                    Reason = "secret-reason"
                }
            ],
            Warnings = [],
            Page = 1,
            OrdinaryTerm = "ordinary-secret"
        };
        using var root = new Activity("test").Start();

        // Act
        await subject.SearchAsync(plan, context, default).ConfigureAwait(false);

        // Assert
        var execute = _activities.Single(activity =>
            activity.OperationName == "wikidata.query.execute" &&
            activity.RootId == root.RootId);
        var planEvent = execute.Events.Single(value => value.Name == "plan");
        var planJson = planEvent.Tags.Single(value => value.Key == "magpie.query.plan_json").Value;
        var knownEmpty = planEvent.Tags.Single(value => value.Key == "magpie.query.known_empty").Value;
        var anchorKind = planEvent.Tags.Single(value => value.Key == "magpie.query.anchor_kind").Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(execute.GetTagItem("magpie.query.terminal_reason"), Is.EqualTo("known_empty"));
            Assert.That(execute.GetTagItem("magpie.query.request_count"), Is.EqualTo(0));
            Assert.That(knownEmpty, Is.EqualTo(true));
            Assert.That(anchorKind, Is.EqualTo("Empty"));
            Assert.That(planJson, Does.Contain("\"serverFilterCount\":1"));
            Assert.That(planJson, Does.Contain("\"criterionDispositionCount\":1"));
            Assert.That(planJson, Does.Not.Contain("unique-secret"));
            Assert.That(planJson, Does.Not.Contain("ordinary-secret"));
            Assert.That(planJson, Does.Not.Contain("secret-reason"));
        }
    }

    [Test]
    public async Task SearchAsync_ReportOneRequestOnEachIsolatedExecuteActivity_WhenRepeatedSearches()
    {
        // Arrange
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        var client = new Mock<IWikidataClient>();
        client.Setup(value => value.ExecuteSparqlAsync(
            It.IsAny<SparqlRequest>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .Callback((SparqlRequest _, RequestContext requestContext, CancellationToken _) =>
                requestContext.UpstreamRequestObserver?.Invoke())
            .ReturnsAsync([]);
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns(async (
                string _,
                Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>> factory,
                CancellationToken token,
                Action<SearchCacheProvenance>? provenance) =>
            {
                provenance?.Invoke(SearchCacheProvenance.Remote);
                return new SearchCacheResult(
                    await factory(token).ConfigureAwait(false),
                    SearchCacheProvenance.Remote);
            });
        IQueryExecutor subject = new QueryExecutor(
            client.Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            Warnings = [],
            Page = 1,
            ProjectCandidateDetails = false
        };
        using var root = new Activity("test").Start();

        // Act
        await subject.SearchAsync(plan, context, default).ConfigureAwait(false);
        await subject.SearchAsync(plan, context, default).ConfigureAwait(false);

        // Assert
        var executions = _activities.Where(activity =>
            activity.OperationName == "wikidata.query.execute" &&
            activity.RootId == root.RootId)
            .ToArray();
        var requests = _activities.Where(activity =>
            activity.OperationName == "wikidata.query.request" &&
            activity.RootId == root.RootId)
            .ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(executions, Has.Length.EqualTo(2));
            Assert.That(executions[0].SpanId, Is.Not.EqualTo(executions[1].SpanId));
            Assert.That(executions[0].GetTagItem("magpie.query.request_count"), Is.EqualTo(1));
            Assert.That(executions[1].GetTagItem("magpie.query.request_count"), Is.EqualTo(1));
            Assert.That(requests, Has.Length.EqualTo(2));
            Assert.That(requests[0].GetTagItem("magpie.request.role"), Is.EqualTo("wdqsWindows"));
            Assert.That(requests[0].GetTagItem("magpie.cache.hit"), Is.EqualTo(false));
            Assert.That(requests[1].GetTagItem("magpie.cache.hit"), Is.EqualTo(false));
        }
    }

    [Test]
    public async Task SearchAsync_AreSiblingsOfEachOther_WhenHydrationRequestActivities()
    {
        // Arrange
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["itemLabel"] = new SparqlBindingDto { Value = "Movie 1" },
                ["type"] = new SparqlBindingDto { Value = "Q11424" }
            }
        ];
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        using var languageValue = JsonDocument.Parse("{\"id\":\"Q100\"}");
        cache.Setup(value => value.GetEntitiesAsync(
            It.Is<IReadOnlyCollection<string>>(qids => qids.SequenceEqual(new[] { "Q1" })),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new Dictionary<string, EntityDto>
            {
                ["Q1"] = new EntityDto
                {
                    Id = "Q1",
                    Claims = new Dictionary<string, List<StatementDto>>
                    {
                        [Constants.OriginalLanguagePropertyId] =
                        [
                            new StatementDto
                            {
                                Rank = "normal",
                                Mainsnak = new SnakDto
                                {
                                    SnakType = "value",
                                    DataValue = new DataValueDto
                                    {
                                        Value = languageValue.RootElement.Clone()
                                    }
                                }
                            }
                        ]
                    }
                }
            });
        cache.Setup(value => value.GetEntitiesAsync(
            It.Is<IReadOnlyCollection<string>>(qids => qids.SequenceEqual(new[] { "Q100" })),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new Dictionary<string, EntityDto>
            {
                ["Q100"] = new EntityDto
                {
                    Id = "Q100",
                    Labels = new Dictionary<string, MonolingualTextDto>
                    {
                        ["en"] = new MonolingualTextDto { Language = "en", Value = "English" }
                    }
                }
            });
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Language,
                    Source = "Language",
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    Criterion = new ResidualStringCriterion
                    {
                        Operator = CriteriaValueOperator.Equals,
                        Negate = false,
                        Values = ["English"]
                    },
                    LanguageNames = ["English"]
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };
        using var root = new Activity("test").Start();

        // Act
        await subject.SearchAsync(plan, context, default).ConfigureAwait(false);

        // Assert
        var execute = _activities.Single(activity =>
            activity.OperationName == "wikidata.query.execute" &&
            activity.RootId == root.RootId);
        var entity = _activities.Single(activity =>
            activity.RootId == root.RootId &&
            (string?)activity.GetTagItem("magpie.request.role") == "residualEntityHydration");
        var support = _activities.Single(activity =>
            activity.RootId == root.RootId &&
            (string?)activity.GetTagItem("magpie.request.role") == "residualSupportHydration");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entity.ParentSpanId, Is.EqualTo(execute.SpanId));
            Assert.That(support.ParentSpanId, Is.EqualTo(execute.SpanId));
        }
    }
}
