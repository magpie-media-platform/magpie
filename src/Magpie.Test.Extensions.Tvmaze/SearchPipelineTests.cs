using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Tvmaze;
using Magpie.Extensions.Tvmaze.Mapping;
using Magpie.Extensions.Tvmaze.Pipeline;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

using RichardSzalay.MockHttp;

namespace Magpie.Test.Extensions.Tvmaze;

[TestFixture]
public class SearchPipelineTests
{
    private const string BlueySearchJson = """
        [
          { "score": 0.91, "show": { "id": 35256, "url": "https://www.tvmaze.com/shows/35256/bluey",
            "name": "Bluey", "type": "Animation", "language": "English",
            "genres": [ "Children", "Comedy" ], "status": "Running", "runtime": 7,
            "premiered": "2018-10-01", "summary": "<p>Bluey is a six-year-old dog.</p>",
            "externals": { "imdb": "tt7678620", "thetvdb": 352379 },
            "image": { "medium": "https://static.tvmaze.com/uploads/medium.jpg",
              "original": "https://static.tvmaze.com/uploads/original.jpg" } } },
          { "score": 0.55, "show": { "id": 2114, "name": "Bluey Unrelated", "language": "English",
            "premiered": "1976-08-02", "status": "Ended" } }
        ]
        """;

    [Test]
    public async Task AdvancedSearchAsync_ReturnsUpstreamHitsWithoutWarnings_WhenTitleContainsCriterionMatches()
    {
        // Arrange
        using var http = new MockHttpMessageHandler();
        http.Expect(HttpMethod.Get, "https://api.tvmaze.com/search/shows*")
            .WithQueryString("q", "bluey")
            .Respond("application/json", BlueySearchJson);
        var httpClient = http.ToHttpClient();
        var settingsService = new Mock<ISettingsService>();
        settingsService.Setup(value => value.GetExtensionGroupAsync(
                Constants.ExtensionId,
                ProviderSettings.GroupName,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((SettingsGroupDetail?)null);
        using var client = new TvmazeClient(
            settingsService.Object,
            Mock.Of<IHttpClientFactory>(factory => factory.CreateClient(TvmazeClient.HttpClientName) == httpClient),
            NullLogger<TvmazeClient>.Instance);
        var searcher = new ContentMetadataSearcher(
            client,
            new SearchPlanner(NullLogger<SearchPlanner>.Instance),
            new SearchExecutor(client, NullLogger<SearchExecutor>.Instance),
            NullLogger<ContentMetadataSearcher>.Instance);
        var query = new ContentHierarchyItemCriteria
        {
            ContentType = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Value = "tv" },
                    new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Value = "tv" }
                ]
            },
            Metadata = new CriteriaSet<MetadataCriteria>
            {
                And =
                [
                    new MetadataCriteria
                    {
                        Title = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And =
                            [
                                new PropertyCriterion<string>
                                {
                                    Operator = CriteriaValueOperator.ContainsString,
                                    Value = "bluey"
                                }
                            ]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await searcher.AdvancedSearchAsync("tv", query, 25, string.Empty, default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Supported, Is.True);
            Assert.That(result.Matches, Has.Count.EqualTo(2));
            Assert.That(result.Matches[0].Title, Is.EqualTo("Bluey"));
            Assert.That(result.Matches[0].ProviderType, Is.EqualTo("tvmaze"));
            Assert.That(result.Matches[0].ExternalIdSource, Is.EqualTo("tvmaze"));
            Assert.That(result.Matches[0].ExternalId, Is.EqualTo("35256"));
            Assert.That(result.Matches[0].ContentType, Is.EqualTo("tv"));
            Assert.That(result.Matches[0].Level, Is.EqualTo("show"));
            Assert.That(result.Matches[0].Year, Is.EqualTo(2018));
            Assert.That(result.Warnings, Is.Empty);
        }
    }

    [Test]
    public async Task AdvancedSearchAsync_ReturnsEmptyWithoutRequests_WhenARequiredRelationshipCannotComeFromTvmaze()
    {
        // Arrange
        var client = new Mock<IClient>(MockBehavior.Strict);
        var searcher = new ContentMetadataSearcher(
            client.Object,
            new SearchPlanner(NullLogger<SearchPlanner>.Instance),
            new SearchExecutor(client.Object, NullLogger<SearchExecutor>.Instance),
            NullLogger<ContentMetadataSearcher>.Instance);
        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria>
            {
                And =
                [
                    new MetadataCriteria
                    {
                        Title = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Value = "Lost" }]
                        },
                        Relationships = new CollectionCriteria<RelationshipCriteria>
                        {
                            Some =
                            [
                                [
                                    new RelationshipCriteria
                                    {
                                        Kind = new CriteriaSet<PropertyCriterion<string>>
                                        {
                                            And = [new PropertyCriterion<string> { Value = "publisher" }]
                                        }
                                    }
                                ]
                            ]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await searcher.AdvancedSearchAsync("tv", query, 25, string.Empty, default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Matches, Is.Empty);
            Assert.That(result.Warnings, Is.Empty);
        }

        client.VerifyNoOtherCalls();
    }
}
