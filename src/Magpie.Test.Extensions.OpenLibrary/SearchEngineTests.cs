using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.OpenLibrary;
using Magpie.Extensions.OpenLibrary.Mapping;
using Magpie.Extensions.OpenLibrary.QueryEngine;
using Magpie.Models.MetadataSearch;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Extensions.OpenLibrary;

[TestFixture]
public sealed class SearchEngineTests
{
    private static IEnumerable<TestCaseData> RemoteFailureCases
    {
        get
        {
            return
            [
                new TestCaseData(new HttpRequestException("remote refused")).SetName("http failure"),
                new TestCaseData(new TimeoutException("remote timed out")).SetName("timeout failure"),
                new TestCaseData(new InvalidOperationException("remote payload")).SetName("invalid operation failure")
            ];
        }
    }

    [Test]
    public async Task SearchAsync_ExecutorResult_WhenThePlanExecutes()
    {
        // Arrange
        var request = new QueryRequest
        {
            Intent = QueryIntent.OrdinarySearch,
            Hierarchy = new BookHierarchy { ContentTypeSlug = "books", BookLevelSlug = "book" },
            Value = "Dune"
        };
        var plan = new QueryPlan
        {
            Root = new PlanNode { Kind = PlanNodeKind.Fetch },
            Hierarchy = new BookHierarchy { ContentTypeSlug = "books", BookLevelSlug = "book" }
        };
        var planner = new Mock<IQueryPlanner>(MockBehavior.Strict);
        planner.Setup(value => value.Plan(request)).Returns(plan);
        var executor = new Mock<IQueryExecutor>(MockBehavior.Strict);
        executor
            .Setup(value => value.ExecuteSearchAsync(plan, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryResult
            {
                Hits = [new WorkHit { WorkOlid = "OL1W", Title = "Dune", Authors = [] }],
                PageInfo = new ProviderSearchPageInfo { RequestedPage = 2, HasMore = false },
                Warnings =
                [
                    "executor warning"
                ]
            });
        var subject = new SearchEngine(planner.Object, executor.Object, NullLogger<SearchEngine>.Instance);

        // Act
        var result = await subject.SearchAsync(request, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits[0].WorkOlid, Is.EqualTo("OL1W"));
            Assert.That(result.PageInfo.RequestedPage, Is.EqualTo(2));
            Assert.That(result.PageInfo.HasMore, Is.False);
            Assert.That(result.Warnings.Select(warning => warning), Is.EqualTo(["executor warning"]));
        }
    }

    [TestCaseSource(nameof(RemoteFailureCases))]
    public async Task SearchAsync_WarnedEmptyPage_WhenAdvancedExecutionFails(Exception failure)
    {
        // Arrange
        var request = new QueryRequest
        {
            Intent = QueryIntent.AdvancedSearch,
            Hierarchy = new BookHierarchy { ContentTypeSlug = "books", BookLevelSlug = "book" },
            Page = 3
        };
        var plan = new QueryPlan
        {
            Root = new PlanNode { Kind = PlanNodeKind.Fetch },
            Hierarchy = new BookHierarchy { ContentTypeSlug = "books", BookLevelSlug = "book" },
            CandidateLanguagePreferences = ["eng"],
            Page = 3
        };
        var planner = new Mock<IQueryPlanner>(MockBehavior.Strict);
        planner.Setup(value => value.Plan(request)).Returns(plan);
        var executor = new Mock<IQueryExecutor>(MockBehavior.Strict);
        executor
            .Setup(value => value.ExecuteSearchAsync(plan, It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);
        var subject = new SearchEngine(planner.Object, executor.Object, NullLogger<SearchEngine>.Instance);

        // Act
        var result = await subject.SearchAsync(request, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Is.Empty);
            Assert.That(result.PageInfo.RequestedPage, Is.EqualTo(3));
            Assert.That(result.PageInfo.HasMore, Is.True);
            Assert.That(
                result.Warnings.Select(warning => warning),
                Is.EqualTo(["Open Library could not complete the remote search."]));
            Assert.That(result.CandidateLanguagePreferences, Is.EqualTo(["eng"]));
        }
    }

    [Test]
    public void SearchAsync_Throws_WhenOrdinarySearchExecutionFails()
    {
        // Arrange
        var request = new QueryRequest
        {
            Intent = QueryIntent.OrdinarySearch,
            Hierarchy = new BookHierarchy { ContentTypeSlug = "books", BookLevelSlug = "book" },
            Value = "Dune"
        };
        var plan = new QueryPlan
        {
            Root = new PlanNode { Kind = PlanNodeKind.Fetch },
            Hierarchy = new BookHierarchy { ContentTypeSlug = "books", BookLevelSlug = "book" }
        };
        var planner = new Mock<IQueryPlanner>(MockBehavior.Strict);
        planner.Setup(value => value.Plan(request)).Returns(plan);
        var executor = new Mock<IQueryExecutor>(MockBehavior.Strict);
        executor
            .Setup(value => value.ExecuteSearchAsync(plan, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("remote refused"));
        var subject = new SearchEngine(planner.Object, executor.Object, NullLogger<SearchEngine>.Instance);

        // Act
        var search = async () => await subject.SearchAsync(request, default).ConfigureAwait(false);

        // Assert
        Assert.That(search, Throws.TypeOf<HttpRequestException>());
    }

    [Test]
    public void SearchAsync_Throws_WhenCancellationIsRequested()
    {
        // Arrange
        var request = new QueryRequest
        {
            Intent = QueryIntent.AdvancedSearch,
            Hierarchy = new BookHierarchy { ContentTypeSlug = "books", BookLevelSlug = "book" }
        };
        var plan = new QueryPlan
        {
            Root = new PlanNode { Kind = PlanNodeKind.Fetch },
            Hierarchy = new BookHierarchy { ContentTypeSlug = "books", BookLevelSlug = "book" }
        };
        var planner = new Mock<IQueryPlanner>(MockBehavior.Strict);
        planner.Setup(value => value.Plan(request)).Returns(plan);
        var executor = new Mock<IQueryExecutor>(MockBehavior.Strict);
        executor
            .Setup(value => value.ExecuteSearchAsync(plan, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        var subject = new SearchEngine(planner.Object, executor.Object, NullLogger<SearchEngine>.Instance);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Act
        var search = async () => await subject.SearchAsync(request, cancellation.Token).ConfigureAwait(false);

        // Assert
        Assert.That(search, Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public async Task ResolveWorkAsync_ExecutorResolution_WhenTheRequestIsPlanned()
    {
        // Arrange
        var request = new QueryRequest
        {
            Intent = QueryIntent.WorkResolution,
            Hierarchy = new BookHierarchy { ContentTypeSlug = "books", BookLevelSlug = "book" },
            Source = Constants.ProviderType,
            Value = "OL1W"
        };
        var plan = new QueryPlan
        {
            Root = new PlanNode { Kind = PlanNodeKind.Fetch },
            Hierarchy = new BookHierarchy { ContentTypeSlug = "books", BookLevelSlug = "book" }
        };
        var planner = new Mock<IQueryPlanner>(MockBehavior.Strict);
        planner.Setup(value => value.Plan(request)).Returns(plan);
        var executor = new Mock<IQueryExecutor>(MockBehavior.Strict);
        executor
            .Setup(value => value.ResolveWorkAsync(plan, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkResolution
            {
                Kind = WorkResolutionKind.Resolved,
                Hit = new WorkHit { WorkOlid = "OL1W", Title = "Dune", Authors = [] }
            });
        var subject = new SearchEngine(planner.Object, executor.Object, NullLogger<SearchEngine>.Instance);

        // Act
        var resolution = await subject.ResolveWorkAsync(request, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(resolution.Kind, Is.EqualTo(WorkResolutionKind.Resolved));
            Assert.That(resolution.Hit?.WorkOlid, Is.EqualTo("OL1W"));
        }
    }

    [Test]
    public async Task GetAuthorAsync_ExecutorAuthor_WhenAnOlidIsRequested()
    {
        // Arrange
        var planner = new Mock<IQueryPlanner>(MockBehavior.Strict);
        var executor = new Mock<IQueryExecutor>(MockBehavior.Strict);
        executor
            .Setup(value => value.GetAuthorAsync("OL1A", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthorDto { Name = "Frank Herbert" });
        var subject = new SearchEngine(planner.Object, executor.Object, NullLogger<SearchEngine>.Instance);

        // Act
        var author = await subject.GetAuthorAsync("OL1A", default).ConfigureAwait(false);

        // Assert
        Assert.That(author?.Name, Is.EqualTo("Frank Herbert"));
    }

    [Test]
    public async Task GetRepresentativeEditionAsync_ExecutorEdition_WhenAHitIsRequested()
    {
        // Arrange
        var hit = new WorkHit { WorkOlid = "OL1W", Title = "Dune", Authors = [] };
        var planner = new Mock<IQueryPlanner>(MockBehavior.Strict);
        var executor = new Mock<IQueryExecutor>(MockBehavior.Strict);
        executor
            .Setup(value => value.GetRepresentativeEditionAsync(hit, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EditionDto { NumberOfPages = 412 });
        var subject = new SearchEngine(planner.Object, executor.Object, NullLogger<SearchEngine>.Instance);

        // Act
        var edition = await subject.GetRepresentativeEditionAsync(hit, default).ConfigureAwait(false);

        // Assert
        Assert.That(edition?.NumberOfPages, Is.EqualTo(412));
    }
}
