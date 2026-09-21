using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Wikidata;
using Magpie.Extensions.Wikidata.QueryEngine;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Models.MetadataSearch;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Wikidata;

[TestFixture]
internal sealed class SearchEngineTests
{
    [Test]
    public async Task SearchAsync_ReturnsExecutorResult_WhenPlannerProducesPlan()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.Equals,
                        Values = ["The Matrix"]
                    }
                ]
            }
        };
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.TitleSearch,
            Filters = [],
            Warnings = [],
            Page = 3
        };
        var expected = new QueryResult
        {
            Hits = [new SearchHit { Qid = "Q83495", TypeQid = "Q11424", Label = "The Matrix" }],
            PageInfo = new ProviderSearchPageInfo { RequestedPage = 3, HasMore = false }
        };
        var planner = new Mock<IQueryPlanner>();
        planner.Setup(value => value.Plan(EntityKind.Movie, criteria, 3, "matrix", 7)).Returns(plan);
        var executor = new Mock<IQueryExecutor>();
        executor.Setup(value => value.SearchAsync(plan, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        var subject = new SearchEngine(planner.Object, executor.Object);

        // Act
        var result = await subject.SearchAsync(
            new QueryRequest
            {
                EntityKind = EntityKind.Movie,
                Criteria = criteria,
                Context = context,
                Page = 3,
                MaximumResults = 7,
                OrdinaryTerm = "matrix"
            },
            default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.SameAs(expected));
    }

    [Test]
    public void SearchAsync_PropagatesExecutorFailure_WhenExecutorThrows()
    {
        // Arrange
        var criteria = new MetadataCriteria();
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        var planner = new Mock<IQueryPlanner>();
        planner.Setup(value => value.Plan(
            It.IsAny<EntityKind>(),
            It.IsAny<MetadataCriteria>(),
            It.IsAny<int>(),
            It.IsAny<string?>(),
            It.IsAny<int>()))
            .Returns(new QueryPlan
            {
                EntityKind = EntityKind.Movie,
                Strategy = QueryStrategy.Unsupported,
                Filters = [],
                Warnings = [],
                Page = 1
            });
        var executor = new Mock<IQueryExecutor>();
        executor.Setup(value => value.SearchAsync(
            It.IsAny<QueryPlan>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Wikidata is unavailable."));
        var subject = new SearchEngine(planner.Object, executor.Object);

        // Act
        var act = async () => await subject.SearchAsync(
            new QueryRequest
            {
                EntityKind = EntityKind.Movie,
                Criteria = criteria,
                Context = context,
                Page = 1
            },
            default).ConfigureAwait(false);

        // Assert
        Assert.That(act, Throws.TypeOf<HttpRequestException>());
    }

    [Test]
    public async Task SearchAsync_ReturnsNoResultsWithoutWarning_WhenARequestedCriterionIsRefused()
    {
        // Arrange
        var criteria = new MetadataCriteria();
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.TitleSearch,
            Filters = [],
            Warnings = ["unsupported criterion"],
            Page = 1,
            CriterionDispositions =
            [
                new CriterionDisposition
                {
                    Source = "Metadata.Relationships.And[0]",
                    Disposition = CriterionDispositionKind.Refused,
                    Reason = "Not supported."
                }
            ]
        };
        var planner = new Mock<IQueryPlanner>();
        planner.Setup(value => value.Plan(EntityKind.Movie, criteria, 1, null, 25)).Returns(plan);
        var executor = new Mock<IQueryExecutor>();
        var subject = new SearchEngine(planner.Object, executor.Object);

        // Act
        var result = await subject.SearchAsync(
            new QueryRequest
            {
                EntityKind = EntityKind.Movie,
                Criteria = criteria,
                Context = context,
                Page = 1,
                MaximumResults = 25
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Supported, Is.True);
            Assert.That(result.Hits, Is.Empty);
            Assert.That(result.Warnings, Is.Empty);
        }

        executor.VerifyNoOtherCalls();
    }
}
