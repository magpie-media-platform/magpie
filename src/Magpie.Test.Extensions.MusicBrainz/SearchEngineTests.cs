using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.MusicBrainz;
using Magpie.Extensions.MusicBrainz.QueryEngine;
using Magpie.Models.MetadataSearch;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Extensions.MusicBrainz;

[TestFixture]
public class SearchEngineTests
{
    [Test]
    public async Task SearchAsync_ExecutesThePlannedQuery_WhenARequestIsSearched()
    {
        // Arrange
        var request = new QueryRequest
        {
            Target = QueryTarget.Artist,
            Criteria = new(),
            RequestedPage = 1,
            Settings = new Settings()
        };
        var plan = new QueryPlan
        {
            RequestedPage = 1,
            MaxResults = 25,
            Settings = request.Settings,
            Warnings = [],
            Fingerprint = "fp-engine"
        };
        var result = new QueryResult
        {
            Supported = true,
            Hits = [],
            PageInfo = new ProviderSearchPageInfo { RequestedPage = 1, HasMore = false }
        };
        var planner = new Mock<IQueryPlanner>();
        planner.Setup(value => value.Plan(request)).Returns(plan);
        var executor = new Mock<IQueryExecutor>();
        executor.Setup(value => value.ExecuteAsync(plan, It.IsAny<CancellationToken>())).ReturnsAsync(result);
        var subject = new SearchEngine(planner.Object, executor.Object);

        // Act
        var searched = await subject.SearchAsync(request, default).ConfigureAwait(false);

        // Assert
        Assert.That(searched, Is.SameAs(result));

        planner.Verify(value => value.Plan(request), Times.Once);
        executor.Verify(value => value.ExecuteAsync(plan, It.IsAny<CancellationToken>()), Times.Once);
    }
}
