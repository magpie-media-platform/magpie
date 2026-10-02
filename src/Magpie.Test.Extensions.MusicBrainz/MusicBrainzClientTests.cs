using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

using Magpie.Extensions.MusicBrainz;
using Magpie.Extensions.MusicBrainz.QueryEngine;
using Magpie.Sdk.Http;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

using RichardSzalay.MockHttp;

namespace Magpie.Test.Extensions.MusicBrainz;

[TestFixture]
public class MusicBrainzClientTests
{
    [Test]
    public async Task SearchAsync_Page_WhenRequestHasBoundsAndServerReturnsJson()
    {
        // Arrange
        using var http = new MockHttpMessageHandler();
        var userAgent = string.Empty;
        var accept = string.Empty;
        http.Expect(
            HttpMethod.Get,
            "https://musicbrainz.org/ws/2/artist?fmt=json&query=Queen&limit=25&offset=0&dismax=true")
            .Respond(request =>
            {
                userAgent = request.Headers.UserAgent.ToString();
                accept = string.Join(",", request.Headers.Accept.Select(value => value.MediaType));
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {"count":1,"offset":0,"artists":[{"id":"0383dadf-2a4e-4d10-a46a-e9e041da8eb3","name":"Queen"}]}
                        """,
                        Encoding.UTF8,
                        "application/json")
                };
            });
        using var httpClient = http.ToHttpClient();
        httpClient.BaseAddress = new Uri(MusicBrainzClient.DefaultBaseUrl);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(value => value.CreateClient(MusicBrainzClient.HttpClientName)).Returns(() =>
        {
            var client = http.ToHttpClient();
            client.BaseAddress = new Uri(MusicBrainzClient.DefaultBaseUrl);
            return client;
        });
        var client = new MusicBrainzClient(factory.Object, TimeProvider.System, NullLogger<MusicBrainzClient>.Instance,
            Mock.Of<IUpstreamCooldown>());

        // Act
        var result = await client.SearchAsync(
            new Request
            {
                RelativeTarget = "artist?fmt=json&query=Queen&limit=25&offset=0&dismax=true",
                Target = QueryTarget.Artist,
                Fingerprint = "safe-fingerprint",
                Limit = 25,
                Offset = 0
            },
            new Settings { Contact = "tests@example.com" },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Outcome, Is.EqualTo(FetchOutcome.Success));
            Assert.That(result.Value!.Count, Is.EqualTo(1));
            Assert.That(result.Value.Artists.Single().Name, Is.EqualTo("Queen"));
            Assert.That(userAgent, Does.Contain("tests@example.com"));
            Assert.That(accept, Does.Contain("application/json"));
        }

        http.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task BrowseReleaseGroupsAsync_Success_WhenWebsiteDefaultStatusAndGenresAreRequested()
    {
        // Arrange
        using var http = new MockHttpMessageHandler();
        http.Expect(
            HttpMethod.Get,
            "https://musicbrainz.org/ws/2/release-group?fmt=json" +
            "&artist=0383dadf-2a4e-4d10-a46a-e9e041da8eb3" +
            "&release-group-status=website-default&inc=genres&limit=100&offset=200")
            .Respond(
                "application/json",
                "{\"release-group-count\":0,\"release-group-offset\":200,\"release-groups\":[]}");
        using var httpClient = http.ToHttpClient();
        httpClient.BaseAddress = new Uri(MusicBrainzClient.DefaultBaseUrl);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(value => value.CreateClient(MusicBrainzClient.HttpClientName)).Returns(httpClient);
        var client = new MusicBrainzClient(factory.Object, TimeProvider.System, NullLogger<MusicBrainzClient>.Instance,
            Mock.Of<IUpstreamCooldown>());

        // Act
        var result = await client.BrowseReleaseGroupsAsync(
            new BrowseCacheKey
            {
                ArtistMbid = "0383dadf-2a4e-4d10-a46a-e9e041da8eb3",
                StatusFilter = "website-default",
                Limit = 100,
                Offset = 200
            },
            new Settings(),
            default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Outcome, Is.EqualTo(FetchOutcome.Success));
        http.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task BrowseTrackReleasesAsync_Success_WhenTheTrackRecordingsAreRequested()
    {
        // Arrange
        using var http = new MockHttpMessageHandler();
        http.Expect(
            HttpMethod.Get,
            "https://musicbrainz.org/ws/2/release?fmt=json&track=63a9abaa-7b6c-4290-83e7-828de5802e7e" +
            "&inc=recordings%2Bartist-credits%2Bisrcs")
            .Respond(
                "application/json",
                "{\"release-count\":1,\"release-offset\":0," +
                "\"releases\":[{\"id\":\"75c2e824-78bb-42c2-a6ec-5648357efd27\"}]}");
        using var httpClient = http.ToHttpClient();
        httpClient.BaseAddress = new Uri(MusicBrainzClient.DefaultBaseUrl);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(value => value.CreateClient(MusicBrainzClient.HttpClientName)).Returns(httpClient);
        var client = new MusicBrainzClient(factory.Object, TimeProvider.System, NullLogger<MusicBrainzClient>.Instance,
            Mock.Of<IUpstreamCooldown>());

        // Act
        var result = await client.BrowseTrackReleasesAsync(
            "63a9abaa-7b6c-4290-83e7-828de5802e7e",
            new Settings(),
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Outcome, Is.EqualTo(FetchOutcome.Success));
            Assert.That(result.Value!.Releases!.Single().Id, Is.EqualTo("75c2e824-78bb-42c2-a6ec-5648357efd27"));
        }

        http.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task SearchAsync_ReturnsUnavailable_WhenResponseIsServiceUnavailable()
    {
        // Arrange
        using var http = new MockHttpMessageHandler();
        http.Expect(HttpMethod.Get, "https://musicbrainz.org/ws/2/recording*")
            .Respond(HttpStatusCode.ServiceUnavailable);
        using var httpClient = http.ToHttpClient();
        httpClient.BaseAddress = new Uri(MusicBrainzClient.DefaultBaseUrl);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(value => value.CreateClient(MusicBrainzClient.HttpClientName)).Returns(httpClient);
        var client = new MusicBrainzClient(factory.Object, TimeProvider.System, NullLogger<MusicBrainzClient>.Instance,
            Mock.Of<IUpstreamCooldown>());

        // Act
        var result = await client.SearchAsync(
            new Request
            {
                RelativeTarget = "recording?fmt=json&query=secret-user-text&limit=25&offset=0&dismax=true",
                Target = QueryTarget.Track,
                Fingerprint = "opaque-key",
                Limit = 25,
                Offset = 0
            },
            new Settings { Contact = "private@example.com" },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Outcome, Is.EqualTo(FetchOutcome.Unavailable));
            Assert.That(result.StatusCode, Is.EqualTo(503));
        }

        http.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task SearchAsync_ReturnsInvalidRequest_WhenResponseIsBadRequest()
    {
        // Arrange
        using var http = new MockHttpMessageHandler();
        http.Expect(HttpMethod.Get, "https://musicbrainz.org/ws/2/artist*")
            .Respond(HttpStatusCode.BadRequest);
        using var httpClient = http.ToHttpClient();
        httpClient.BaseAddress = new Uri(MusicBrainzClient.DefaultBaseUrl);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(value => value.CreateClient(MusicBrainzClient.HttpClientName)).Returns(httpClient);
        var client = new MusicBrainzClient(factory.Object, TimeProvider.System, NullLogger<MusicBrainzClient>.Instance,
            Mock.Of<IUpstreamCooldown>());

        // Act
        var result = await client.SearchAsync(
            new Request
            {
                RelativeTarget = "artist?fmt=json&query=private-name&limit=25&offset=0&dismax=true",
                Target = QueryTarget.Artist,
                Fingerprint = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                Limit = 25,
                Offset = 0
            },
            new Settings(),
            default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Outcome, Is.EqualTo(FetchOutcome.InvalidRequest));
        http.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task GetIsrcAsync_ResponseTooLarge_WhenAdvertisedLengthExceedsEightMebibytes()
    {
        // Arrange
        using var http = new MockHttpMessageHandler();
        http.Expect(HttpMethod.Get, "https://musicbrainz.org/ws/2/isrc/USRC17607839*")
            .Respond(_ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([])
                };
                response.Content.Headers.ContentLength = 8 * 1024 * 1024 + 1;
                return response;
            });
        using var httpClient = http.ToHttpClient();
        httpClient.BaseAddress = new Uri(MusicBrainzClient.DefaultBaseUrl);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(value => value.CreateClient(MusicBrainzClient.HttpClientName)).Returns(httpClient);
        var client = new MusicBrainzClient(factory.Object, TimeProvider.System, NullLogger<MusicBrainzClient>.Instance,
            Mock.Of<IUpstreamCooldown>());

        // Act
        var result = await client.GetIsrcAsync("USRC17607839", new Settings(), default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Outcome, Is.EqualTo(FetchOutcome.ResponseTooLarge));
        http.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task GetIsrcAsync_Recording_WhenSupportedIncludesAreRequested()
    {
        // Arrange
        using var http = new MockHttpMessageHandler();
        http.Expect(
            HttpMethod.Get,
            "https://musicbrainz.org/ws/2/isrc/GBUM71029604?fmt=json&inc=artist-credits")
            .Respond(
                "application/json",
                """
                {
                  "isrc": "GBUM71029604",
                  "recordings": [{
                    "id": "b1a9c0e9-d987-4042-ae91-78d6a3267d69",
                    "title": "Bohemian Rhapsody",
                    "artist-credit": [{"artist":{"name":"Queen"}}]
                  }]
                }
                """);
        using var httpClient = http.ToHttpClient();
        httpClient.BaseAddress = new Uri(MusicBrainzClient.DefaultBaseUrl);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(value => value.CreateClient(MusicBrainzClient.HttpClientName)).Returns(httpClient);
        var client = new MusicBrainzClient(factory.Object, TimeProvider.System, NullLogger<MusicBrainzClient>.Instance,
            Mock.Of<IUpstreamCooldown>());

        // Act
        var result = await client.GetIsrcAsync("GBUM71029604", new Settings(), default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Outcome, Is.EqualTo(FetchOutcome.Success));
            Assert.That(result.Value!.Recordings.Single().Isrcs, Is.EqualTo(["GBUM71029604"]));
            Assert.That(result.Value.Recordings.Single().ArtistCredit!.Single().Artist!.Name, Is.EqualTo("Queen"));
        }

        http.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task SearchAsync_Malformed_WhenResponseContainsInvalidJson()
    {
        // Arrange
        using var http = new MockHttpMessageHandler();
        http.Expect(HttpMethod.Get, "https://musicbrainz.org/ws/2/artist*")
            .Respond("application/json", "{not-json");
        using var httpClient = http.ToHttpClient();
        httpClient.BaseAddress = new Uri(MusicBrainzClient.DefaultBaseUrl);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(value => value.CreateClient(MusicBrainzClient.HttpClientName)).Returns(httpClient);
        var client = new MusicBrainzClient(factory.Object, TimeProvider.System, NullLogger<MusicBrainzClient>.Instance,
            Mock.Of<IUpstreamCooldown>());

        // Act
        var result = await client.SearchAsync(
            new Request
            {
                RelativeTarget = "artist?fmt=json&query=x&limit=25&offset=0&dismax=true",
                Target = QueryTarget.Artist,
                Fingerprint = "malformed",
                Limit = 25,
                Offset = 0
            },
            new Settings(),
            default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Outcome, Is.EqualTo(FetchOutcome.Malformed));
        http.VerifyNoOutstandingExpectation();
    }

    [TestCase((int)ArtistCompositionEntity.ReleaseGroup, "release-group")]
    [TestCase((int)ArtistCompositionEntity.Release, "release")]
    [TestCase((int)ArtistCompositionEntity.Recording, "recording")]
    [TestCase((int)ArtistCompositionEntity.Work, "work")]
    public async Task BrowseArtistsAsync_Result_WhenEachSupportedRelatedEntityIsRequested(
        int entityValue,
        string parameter)
    {
        // Arrange
        var entity = (ArtistCompositionEntity)entityValue;
        using var http = new MockHttpMessageHandler();
        const string mbid = "11111111-1111-4111-8111-111111111111";
        http.Expect(
            HttpMethod.Get,
            $"https://musicbrainz.org/ws/2/artist?fmt=json&{parameter}={mbid}&limit=100&offset=0")
            .Respond(
                "application/json",
                """
                {
                  "artist-count": 1,
                  "artist-offset": 0,
                  "artists": [{ "id": "22222222-2222-4222-8222-222222222222", "name": "Artist" }]
                }
                """);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(value => value.CreateClient(MusicBrainzClient.HttpClientName)).Returns(() =>
        {
            var client = http.ToHttpClient();
            client.BaseAddress = new Uri(MusicBrainzClient.DefaultBaseUrl);
            return client;
        });
        var client = new MusicBrainzClient(factory.Object, TimeProvider.System, NullLogger<MusicBrainzClient>.Instance,
            Mock.Of<IUpstreamCooldown>());

        // Act
        var result = await client.BrowseArtistsAsync(
            new ArtistBrowseCacheKey
            {
                RelatedEntity = entity,
                RelatedMbid = mbid,
                Limit = 100,
                Offset = 0
            },
            new Settings(),
            default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Value!.ArtistCount, Is.EqualTo(1));
        http.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task BrowseArtistsAsync_Result_WhenResidualCapabilitiesAreRequested()
    {
        // Arrange
        using var http = new MockHttpMessageHandler();
        const string mbid = "11111111-1111-4111-8111-111111111111";
        http.Expect(
            HttpMethod.Get,
            "https://musicbrainz.org/ws/2/artist?fmt=json&release-group=" +
            mbid +
            "&inc=aliases+genres+url-rels&limit=100&offset=0")
            .Respond("application/json", "{\"artist-count\":0,\"artist-offset\":0,\"artists\":[]}");
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(value => value.CreateClient(MusicBrainzClient.HttpClientName)).Returns(() =>
        {
            var httpClient = http.ToHttpClient();
            httpClient.BaseAddress = new Uri(MusicBrainzClient.DefaultBaseUrl);
            return httpClient;
        });
        var client = new MusicBrainzClient(factory.Object, TimeProvider.System, NullLogger<MusicBrainzClient>.Instance,
            Mock.Of<IUpstreamCooldown>());

        // Act
        var result = await client.BrowseArtistsAsync(
            new ArtistBrowseCacheKey
            {
                RelatedEntity = ArtistCompositionEntity.ReleaseGroup,
                RelatedMbid = mbid,
                Capabilities = LookupCapability.UrlRelations |
                    LookupCapability.Aliases |
                    LookupCapability.Genres,
                Limit = 100,
                Offset = 0
            },
            new Settings(),
            default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Outcome, Is.EqualTo(FetchOutcome.Success));
        http.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task ResolveArtistCompositionAsync_Result_WhenWorkSearchReturnsCandidate()
    {
        // Arrange
        using var http = new MockHttpMessageHandler();
        http.Expect(HttpMethod.Get, "https://musicbrainz.org/ws/2/work*")
            .Respond(
                "application/json",
                """
                {
                  "count": 1,
                  "offset": 0,
                  "works": [{ "id": "11111111-1111-4111-8111-111111111111", "title": "Work" }]
                }
                """);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(value => value.CreateClient(MusicBrainzClient.HttpClientName)).Returns(() =>
        {
            var httpClient = http.ToHttpClient();
            httpClient.BaseAddress = new Uri(MusicBrainzClient.DefaultBaseUrl);
            return httpClient;
        });
        var client = new MusicBrainzClient(factory.Object, TimeProvider.System, NullLogger<MusicBrainzClient>.Instance,
            Mock.Of<IUpstreamCooldown>());

        // Act
        var result = await client.ResolveArtistCompositionAsync(
            new ArtistResolutionRequest
            {
                Entity = ArtistCompositionEntity.Work,
                RelativeTarget = "work?fmt=json&query=workaccent%3A%22Work%22&limit=2&offset=0&dismax=false",
                Fingerprint = "work-resolution"
            },
            new Settings(),
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Outcome, Is.EqualTo(FetchOutcome.Success));
            Assert.That(result.Value!.Candidates.Single().Name, Is.EqualTo("Work"));
        }

        http.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task GetEntityAsync_Result_WhenArtistRelationsCapabilityIsRequested()
    {
        // Arrange
        using var http = new MockHttpMessageHandler();
        const string bandMbid = "11111111-1111-4111-8111-111111111111";
        http.Expect(
            HttpMethod.Get,
            $"https://musicbrainz.org/ws/2/artist/{bandMbid}?fmt=json&inc=artist-rels")
            .Respond(
                "application/json",
                $$"""
                {
                  "id": "{{bandMbid}}",
                  "name": "Band",
                  "relations": [
                    {
                      "type": "member of band",
                      "target-type": "artist",
                      "direction": "backward",
                      "artist": {
                        "id": "22222222-2222-4222-8222-222222222222",
                        "name": "Member"
                      }
                    }
                  ]
                }
                """);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(value => value.CreateClient(MusicBrainzClient.HttpClientName)).Returns(() =>
        {
            var httpClient = http.ToHttpClient();
            httpClient.BaseAddress = new Uri(MusicBrainzClient.DefaultBaseUrl);
            return httpClient;
        });
        var client = new MusicBrainzClient(factory.Object, TimeProvider.System, NullLogger<MusicBrainzClient>.Instance,
            Mock.Of<IUpstreamCooldown>());

        // Act
        var result = await client.GetEntityAsync(
            new EntityCacheKey
            {
                Kind = EntityKind.Artist,
                Mbid = bandMbid,
                Profile = LookupProfile.Search,
                Capabilities = LookupCapability.ArtistRelations
            },
            new Settings(),
            default).ConfigureAwait(false);

        // Assert
        var relation = result.Value!.Artist!.Relations!.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(relation.Type, Is.EqualTo("member of band"));
            Assert.That(relation.TargetType, Is.EqualTo("artist"));
            Assert.That(relation.Direction, Is.EqualTo("backward"));
            Assert.That(relation.Artist!.Name, Is.EqualTo("Member"));
        }

        http.VerifyNoOutstandingExpectation();
    }
}
