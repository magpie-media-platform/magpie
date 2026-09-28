using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Wikidata;
using Magpie.Extensions.Wikidata.Mapping;
using Magpie.Extensions.Wikidata.QueryEngine;
using Magpie.Models.Data;
using Magpie.Models.Enums;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Wikidata;

[TestFixture]
internal sealed class ContinuationTests
{
    [TestCase(false, 20)]
    [TestCase(true, 40)]
    public async Task SearchAsync_ReplaysLastWindow_WhenSourceWindowBudgetEnds(
        bool relationshipSeed, int expectedFirstRequests)
    {
        // Arrange
        var requests = new List<string>();
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows = [.. Enumerable.Range(1, 51)
            .Select(id => new Dictionary<string, SparqlBindingDto>
            {
                ["item"] = new() { Value = $"http://www.wikidata.org/entity/Q{id}" },
                ["itemLabel"] = new() { Value = "Excluded" },
                ["type"] = new() { Value = "http://www.wikidata.org/entity/Q11424" }
            })];
        var client = new Mock<IWikidataClient>();
        client.Setup(value => value.ExecuteSparqlAsync(It.IsAny<SparqlRequest>(),
                It.IsAny<RequestContext>(), It.IsAny<CancellationToken>()))
            .Callback((SparqlRequest request, RequestContext _, CancellationToken _) => requests.Add(request.QueryText))
            .ReturnsAsync((SparqlRequest request, RequestContext _, CancellationToken _) =>
                relationshipSeed && !request.QueryText.Contains("SELECT DISTINCT ?item", StringComparison.Ordinal)
                    ? [] : rows);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Page = 1,
            MaximumResults = 2,
            Warnings = [],
            Filters = relationshipSeed
                ? [new QueryFilter
                {
                    Kind = QueryFilterKind.Relationship,
                    Alternatives =
                    [
                        new Magpie.Extensions.Wikidata.QueryEngine.SubjectFilter
                        {
                            PropertyIds = ["P161"], Qids = ["Q42"]
                        }
                    ]
                }]
                : [],
            ResidualFilters = relationshipSeed
                ? [new ResidualFilter
                {
                    Kind = ResidualFilterKind.Relationship,
                    Source = "Relationships.Some[0]",
                    Hydration = ResidualHydration.None
                }]
                : [new ResidualFilter
                {
                    Kind = ResidualFilterKind.Text,
                    Source = "Title",
                    Hydration = ResidualHydration.None,
                    TextField = ResidualTextField.Title,
                    Criterion = new ResidualStringCriterion
                    {
                        Operator = CriteriaValueOperator.Equals,
                        Negate = false,
                        Values = ["Wanted"]
                    }
                }]
        };
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "test",
            Progress = new QueryProgress()
        };
        var executor = new QueryExecutor(
            client.Object, Mock.Of<IQueryCoordinator>(), NullLogger<QueryExecutor>.Instance);

        // Act
        var first = await executor.SearchAsync(plan, context, default).ConfigureAwait(false);
        var firstRequests = requests.Count;
        var storedOffset = relationshipSeed ? context.Progress.SeedOffset : context.Progress.CandidateOffset;
        var second = await executor.SearchAsync(plan, context with { Progress = context.Progress.Copy() }, default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Hits, Is.Empty);
            Assert.That(first.Warnings, Has.Some.Contains("source-window limit"));
            Assert.That(first.PageInfo.HasMore, Is.True);
            Assert.That(second.PageInfo.HasMore, Is.True);
            Assert.That(firstRequests, Is.EqualTo(expectedFirstRequests));
            Assert.That(storedOffset, Is.EqualTo(950));
            Assert.That(requests[firstRequests], Does.Contain("OFFSET 950"));
        }
    }

    [Test]
    public async Task ContinueSearchAsync_ReplaysOnlyTheLastWindow_WhenResultsContinueAndRetry()
    {
        // Arrange
        var offsets = new List<int>();
        var client = new Mock<IWikidataClient>();
        client.Setup(value => value.ExecuteSparqlAsync(It.IsAny<SparqlRequest>(),
            It.IsAny<RequestContext>(), It.IsAny<CancellationToken>()))
            .Callback((SparqlRequest request, RequestContext _, CancellationToken _) =>
                offsets.Add(request.QueryText.Contains("OFFSET 50", StringComparison.Ordinal) ? 50 : 0))
            .ReturnsAsync((SparqlRequest request, RequestContext _, CancellationToken _) =>
                (IReadOnlyList<Dictionary<string, SparqlBindingDto>>)[.. Enumerable.Range(
                    request.QueryText.Contains("OFFSET 50", StringComparison.Ordinal) ? 51 : 1,
                    request.QueryText.Contains("OFFSET 50", StringComparison.Ordinal) ? 2 : 51)
                    .Select(id => new Dictionary<string, SparqlBindingDto>
                    {
                        ["item"] = new() { Value = $"http://www.wikidata.org/entity/Q{id}" },
                        ["itemLabel"] = new() { Value = $"Movie {id}" },
                        ["type"] = new() { Value = "http://www.wikidata.org/entity/Q11424" }
                    })]);
        var cache = new Mock<IQueryCoordinator>();
        var settings = new Mock<ISettingsResolver>();
        settings.Setup(value => value.ResolveAsync("en", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RequestContext { Language = "en", FallbackLanguages = ["en"], UserAgent = "test" });
        var planner = new Mock<IQueryPlanner>();
        planner.Setup(value => value.Plan(EntityKind.Movie, It.IsAny<MetadataCriteria>(), 1, null, It.IsAny<int>()))
            .Returns((EntityKind _, MetadataCriteria _, int _, string? _, int maximum) => new QueryPlan
            {
                EntityKind = EntityKind.Movie,
                Strategy = QueryStrategy.Structured,
                Filters = [],
                Warnings = [],
                Page = 1,
                MaximumResults = maximum
            });
        var executor = new QueryExecutor(client.Object, cache.Object, NullLogger<QueryExecutor>.Instance);
        var searcher = new ContentMetadataSearcher(settings.Object, new SearchEngine(planner.Object, executor),
            cache.Object, TimeProvider.System, NullLogger<ContentMetadataSearcher>.Instance);

        // Act
        var first = await searcher.ContinueSearchAsync("movie", new(), 48, "en", null).ConfigureAwait(false);
        var second = await searcher.ContinueSearchAsync("movie", new(), 5, "en", first.Continuation)
            .ConfigureAwait(false);
        var retry = await searcher.ContinueSearchAsync("movie", new(), 5, "en", first.Continuation)
            .ConfigureAwait(false);
        var independent = await searcher.ContinueSearchAsync("movie", new(), 48, "en", null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Page.Matches, Has.Count.EqualTo(48));
            Assert.That(first.Page.Matches[^1].ExternalId, Is.EqualTo("Q48"));
            Assert.That(second.Page.Matches.Select(value => value.ExternalId),
                Is.EqualTo(["Q49", "Q50", "Q51", "Q52"]));
            Assert.That(second.Page.PageInfo.HasMore, Is.False);
            Assert.That(retry.Page.Matches.Select(value => value.ExternalId),
                Is.EqualTo(["Q49", "Q50", "Q51", "Q52"]));
            Assert.That(independent.Page.Matches[0].ExternalId, Is.EqualTo("Q1"));
            Assert.That(offsets, Is.EqualTo([0, 0, 50, 0, 50, 0]));
        }
    }
}
