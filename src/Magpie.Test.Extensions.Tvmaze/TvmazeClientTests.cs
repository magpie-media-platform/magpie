using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Tvmaze;
using Magpie.Models;
using Magpie.Sdk.Services;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

using RichardSzalay.MockHttp;

namespace Magpie.Test.Extensions.Tvmaze;

[TestFixture]
public class TvmazeClientTests
{
    private const string SearchJson = """
        [
          { "score": 0.9, "show": { "id": 82, "name": "Game of Thrones",
            "externals": { "imdb": "tt0944947", "thetvdb": 121361 } } },
          { "score": 0.5, "show": { "id": 83, "name": "Game On" } }
        ]
        """;

    private const string ShowJson = """
        {
          "id": 82,
          "name": "Game of Thrones",
          "type": "Scripted",
          "language": "English",
          "genres": [ "Drama", "Adventure" ],
          "status": "Ended",
          "schedule": { "time": "21:00", "days": [ "Sunday" ] },
          "rating": { "average": 9.2 },
          "runtime": 60,
          "premiered": "2011-04-17",
          "summary": "<p>Seven noble families fight.</p>",
          "externals": { "imdb": "tt0944947", "thetvdb": 121361 },
          "image": { "medium": "https://static.tvmaze.com/uploads/medium.jpg",
            "original": "https://static.tvmaze.com/uploads/original.jpg" },
          "_embedded": {
            "episodes": [
              { "id": 4952, "name": "Winter Is Coming", "season": 1, "number": 1,
                "airdate": "2011-04-17", "runtime": 60, "summary": "<p>Ned.</p>" },
              { "id": 4953, "name": "The Kingsroad", "season": 1, "number": 2, "airdate": "2011-04-24" }
            ],
            "images": [
              { "id": 1, "type": "poster", "main": true, "resolutions": {
                "original": { "url": "https://static.tvmaze.com/uploads/poster.jpg" } } }
            ],
            "akas": [
              { "name": "Игра престолов", "country": { "name": "Russia", "code": "RU" } }
            ],
            "seasons": [
              { "id": 1, "number": 1, "premiereDate": "2011-04-17", "endDate": "2012-03-31" }
            ]
          }
        }
        """;

    private const string EpisodeJson = """
        {
          "id": 4952,
          "name": "Winter Is Coming",
          "season": 1,
          "number": 1,
          "airdate": "2011-04-17",
          "runtime": 60,
          "summary": "<p>Ned receives an unexpected visitor.</p>",
          "image": {
            "medium": "https://static.tvmaze.com/uploads/episode-medium.jpg",
            "original": "https://static.tvmaze.com/uploads/episode-original.jpg"
          }
        }
        """;

    private const string LookupJson = """
        { "id": 82, "name": "Game of Thrones", "externals": { "imdb": "tt0944947" } }
        """;

    private const string PersonSearchJson = """
        [
          { "score": 0.75, "person": { "id": 7, "name": "Mackenzie Lintz",
            "birthday": "1996-11-22", "deathday": null,
            "image": { "medium": "https://static.tvmaze.com/uploads/person-medium.jpg",
              "original": "https://static.tvmaze.com/uploads/person-original.jpg" } } }
        ]
        """;

    private const string CastCreditsJson = """
        [
          {
            "self": false,
            "voice": true,
            "_embedded": {
              "show": {
                "id": 1,
                "name": "Under the Dome",
                "language": "English",
                "externals": { "imdb": "tt1553656", "thetvdb": 264492 }
              }
            }
          }
        ]
        """;

    private const string CrewCreditsJson = """
        [
          {
            "type": "Co-Executive Producer",
            "_embedded": {
              "show": {
                "id": 2,
                "name": "Person of Interest",
                "language": "English",
                "externals": { "imdb": "tt1839578", "thetvdb": 248742 }
              }
            }
          }
        ]
        """;

    private const string GuestCastJson = """
        [
          {
            "person": { "id": 7, "name": "Mackenzie Lintz" },
            "character": { "id": 7, "name": "Norrie Calvert-Hill" },
            "self": false,
            "voice": true
          }
        ]
        """;

    private const string GuestCrewJson = """
        [
          {
            "person": { "id": 15, "name": "Stephen King" },
            "guestCrewType": "Story"
          }
        ]
        """;

    private Mock<ISettingsService> _settingsService = null!;
    private MockHttpMessageHandler _http = null!;
    private HttpClient _httpClient = null!;
    private TvmazeClient _client = null!;

    [SetUp]
    public void SetUp()
    {
        _settingsService = new Mock<ISettingsService>();
        _settingsService.Setup(value => value.GetExtensionGroupAsync(
                Constants.ExtensionId,
                ProviderSettings.GroupName,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((SettingsGroupDetail?)null);
        _http = new MockHttpMessageHandler();
        _httpClient = _http.ToHttpClient();
        _client = new TvmazeClient(
            _settingsService.Object,
            Mock.Of<IHttpClientFactory>(factory => factory.CreateClient(TvmazeClient.HttpClientName) == _httpClient),
            NullLogger<TvmazeClient>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _httpClient.Dispose();
        _http.Dispose();
    }

    [Test]
    public async Task SearchShowsAsync_ReturnsHits_WhenResponseIsValid()
    {
        // Arrange
        _http.Expect(HttpMethod.Get, "https://api.tvmaze.com/search/shows*")
            .WithQueryString("q", "game of thrones")
            .Respond("application/json", SearchJson);

        // Act
        var hits = await _client.SearchShowsAsync("game of thrones", default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(hits, Has.Count.EqualTo(2));
            Assert.That(hits[0].Show!.Id, Is.EqualTo(82));
            Assert.That(hits[0].Show!.Externals!.Imdb, Is.EqualTo("tt0944947"));
        }

        _http.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task SearchPeopleAsync_ReturnsPeople_WhenResponseIsValid()
    {
        // Arrange
        _http.Expect(HttpMethod.Get, "https://api.tvmaze.com/search/people*")
            .WithQueryString("q", "mackenzie lintz")
            .Respond("application/json", PersonSearchJson);

        // Act
        var hits = await _client.SearchPeopleAsync("mackenzie lintz", default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(hits, Has.Count.EqualTo(1));
            Assert.That(hits[0].Score, Is.EqualTo(0.75));
            Assert.That(hits[0].Person!.Id, Is.EqualTo(7));
            Assert.That(hits[0].Person!.Name, Is.EqualTo("Mackenzie Lintz"));
            Assert.That(hits[0].Person!.Birthday, Is.EqualTo("1996-11-22"));
        }

        _http.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task GetPersonByIdAsync_ReturnsPerson_WhenResponseIsValid()
    {
        // Arrange
        _http.Expect(HttpMethod.Get, "https://api.tvmaze.com/people/14072")
            .Respond(
                "application/json",
                """
                {
                  "id": 14072,
                  "name": "Peter Dinklage",
                  "birthday": "1969-06-11"
                }
                """);

        // Act
        var person = await _client.GetPersonByIdAsync(14072, default).ConfigureAwait(false);

        // Assert
        Assert.That(person, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(person!.Id, Is.EqualTo(14072));
            Assert.That(person.Name, Is.EqualTo("Peter Dinklage"));
            Assert.That(person.Birthday, Is.EqualTo("1969-06-11"));
        }

        _http.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task GetShowWithEmbedsAsync_ReturnsLeanShow_WhenResponseIsValid()
    {
        // Arrange
        _http.Expect(HttpMethod.Get, "https://api.tvmaze.com/shows/82")
            .Respond("application/json", LookupJson);

        // Act
        var show = await _client.GetShowWithEmbedsAsync(82, [], default).ConfigureAwait(false);

        // Assert
        Assert.That(show, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(show!.Id, Is.EqualTo(82));
            Assert.That(show.Name, Is.EqualTo("Game of Thrones"));
            Assert.That(show.Embedded, Is.Null);
        }

        _http.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task GetShowWithEmbedsAsync_EmbeddedResources_WhenResponseIsValid()
    {
        // Arrange
        _http.Expect(HttpMethod.Get, "https://api.tvmaze.com/shows/82*")
            .WithExactQueryString("embed[]=episodes&embed[]=images&embed[]=akas&embed[]=seasons")
            .Respond("application/json", ShowJson);

        // Act
        var show = await _client.GetShowWithEmbedsAsync(
            82, ["episodes", "images", "akas", "seasons"], default).ConfigureAwait(false);

        // Assert
        Assert.That(show, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(show!.Name, Is.EqualTo("Game of Thrones"));
            Assert.That(show.Type, Is.EqualTo("Scripted"));
            Assert.That(show.Rating!.Average, Is.EqualTo(9.2));
            Assert.That(show.Schedule!.Time, Is.EqualTo("21:00"));
            Assert.That(show.Schedule.Days, Is.EqualTo(["Sunday"]));
            Assert.That(show.Externals!.TheTvdb, Is.EqualTo(121361));
            Assert.That(show.Embedded!.Episodes, Has.Count.EqualTo(2));
            Assert.That(show.Embedded.Episodes![0].Name, Is.EqualTo("Winter Is Coming"));
            Assert.That(show.Embedded.Images, Has.Count.EqualTo(1));
            Assert.That(show.Embedded.Akas, Has.Count.EqualTo(1));
            Assert.That(show.Embedded.Akas![0].Name, Is.EqualTo("Игра престолов"));
            Assert.That(show.Embedded.Seasons, Has.Count.EqualTo(1));
            Assert.That(show.Embedded.Seasons![0].EndDate, Is.EqualTo("2012-03-31"));
        }

        _http.VerifyNoOutstandingExpectation();
    }

    [Test]
    public void GetShowWithEmbedsAsync_Throws_WhenPayloadIsBlank()
    {
        // Arrange
        _http.Expect(HttpMethod.Get, "https://api.tvmaze.com/shows/82")
            .Respond("application/json", string.Empty);

        // Act
        var exception = Assert.ThrowsAsync<HttpRequestException>(async () =>
            await _client.GetShowWithEmbedsAsync(82, [], default).ConfigureAwait(false));

        // Assert
        Assert.That(exception!.Message, Does.Contain("unusable JSON payload"));
        _http.VerifyNoOutstandingExpectation();
    }

    [Test]
    public void SearchPeopleAsync_Throws_WhenPayloadIsMalformed()
    {
        // Arrange
        _http.Expect(HttpMethod.Get, "https://api.tvmaze.com/search/people*")
            .WithQueryString("q", "person")
            .Respond("application/json", "{");

        // Act
        var exception = Assert.ThrowsAsync<HttpRequestException>(async () =>
            await _client.SearchPeopleAsync("person", default).ConfigureAwait(false));

        // Assert
        Assert.That(exception!.Message, Does.Contain("unusable JSON payload"));
        _http.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task GetShowsPageAsync_ReturnsParsedShows_WhenPageIsRequested()
    {
        // Arrange
        _http.Expect(HttpMethod.Get, "https://api.tvmaze.com/shows")
            .WithQueryString("page", "2")
            .Respond(
                "application/json",
                """
                [
                  { "id": 82, "name": "Game of Thrones" },
                  { "id": 83, "name": "Game On" }
                ]
                """);

        // Act
        var shows = await _client.GetShowsPageAsync(2, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(shows, Has.Count.EqualTo(2));
            Assert.That(shows![0].Id, Is.EqualTo(82));
            Assert.That(shows[1].Name, Is.EqualTo("Game On"));
        }

        _http.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task GetEpisodeAsync_ReturnsEpisode_WhenResponseIsValid()
    {
        // Arrange
        _http.Expect(HttpMethod.Get, "https://api.tvmaze.com/episodes/4952")
            .Respond("application/json", EpisodeJson);

        // Act
        var episode = await _client.GetEpisodeAsync(4952, default).ConfigureAwait(false);

        // Assert
        Assert.That(episode, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(episode!.Id, Is.EqualTo(4952));
            Assert.That(episode.Name, Is.EqualTo("Winter Is Coming"));
            Assert.That(episode.Season, Is.EqualTo(1));
            Assert.That(episode.Number, Is.EqualTo(1));
            Assert.That(episode.AirDate, Is.EqualTo("2011-04-17"));
            Assert.That(episode.Runtime, Is.EqualTo(60));
            Assert.That(episode.Summary, Is.EqualTo("<p>Ned receives an unexpected visitor.</p>"));
            Assert.That(
                episode.Image!.Original,
                Is.EqualTo("https://static.tvmaze.com/uploads/episode-original.jpg"));
        }

        _http.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task GetShowWithEmbedsAsync_RequestsCastAndCrew_WhenShowIsFetched()
    {
        // Arrange
        _http.Expect(HttpMethod.Get, "https://api.tvmaze.com/shows/82*")
            .WithExactQueryString("embed[]=cast&embed[]=crew")
            .Respond(
                "application/json",
                """
                {
                  "id": 82,
                  "name": "Game of Thrones",
                  "network": { "id": 8, "name": "HBO" },
                  "_embedded": {
                    "cast": [
                      { "person": { "id": 14072, "name": "Peter Dinklage" },
                        "character": { "id": 22587, "name": "Tyrion Lannister" } }
                    ],
                    "crew": [
                      { "type": "Creator", "person": { "id": 15309, "name": "David Benioff" } }
                    ]
                  }
                }
                """);

        // Act
        var show = await _client.GetShowWithEmbedsAsync(82, ["cast", "crew"], default).ConfigureAwait(false);

        // Assert
        Assert.That(show, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(show!.Network!.Name, Is.EqualTo("HBO"));
            Assert.That(show.Embedded!.Cast, Has.Count.EqualTo(1));
            Assert.That(show.Embedded.Cast![0].Person!.Name, Is.EqualTo("Peter Dinklage"));
            Assert.That(show.Embedded.Cast[0].Character!.Name, Is.EqualTo("Tyrion Lannister"));
            Assert.That(show.Embedded.Crew, Has.Count.EqualTo(1));
            Assert.That(show.Embedded.Crew![0].Type, Is.EqualTo("Creator"));
        }

        _http.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task GetShowWithEmbedsAsync_RequestsCombinedEmbeds_WhenShowIsFetched()
    {
        // Arrange
        _http.Expect(HttpMethod.Get, "https://api.tvmaze.com/shows/82*")
            .WithExactQueryString("embed[]=akas&embed[]=cast&embed[]=crew&embed[]=images")
            .Respond(
                "application/json",
                """
                {
                  "id": 82,
                  "name": "Game of Thrones",
                  "_embedded": {
                    "akas": [],
                    "cast": [],
                    "crew": [],
                    "images": []
                  }
                }
                """);

        // Act
        var show = await _client.GetShowWithEmbedsAsync(
            82,
            ["akas", "cast", "crew", "images"],
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(show, Is.Not.Null);
            Assert.That(show!.Embedded!.Akas, Is.Empty);
            Assert.That(show.Embedded.Cast, Is.Empty);
            Assert.That(show.Embedded.Crew, Is.Empty);
            Assert.That(show.Embedded.Images, Is.Empty);
        }

        _http.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task GetPersonCastCreditsAsync_ReturnsCredits_WhenShowIsEmbedded()
    {
        // Arrange
        _http.Expect(HttpMethod.Get, "https://api.tvmaze.com/people/1/castcredits*")
            .WithQueryString("embed", "show")
            .Respond("application/json", CastCreditsJson);

        // Act
        var credits = await _client.GetPersonCastCreditsAsync(1, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(credits, Has.Count.EqualTo(1));
            Assert.That(credits[0].Self, Is.False);
            Assert.That(credits[0].Voice, Is.True);
            Assert.That(credits[0].Embedded!.Show!.Id, Is.EqualTo(1));
            Assert.That(credits[0].Embedded!.Show!.Externals!.Imdb, Is.EqualTo("tt1553656"));
        }

        _http.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task GetPersonCrewCreditsAsync_ReturnsCredits_WhenShowIsEmbedded()
    {
        // Arrange
        _http.Expect(HttpMethod.Get, "https://api.tvmaze.com/people/100/crewcredits*")
            .WithQueryString("embed", "show")
            .Respond("application/json", CrewCreditsJson);

        // Act
        var credits = await _client.GetPersonCrewCreditsAsync(100, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(credits, Has.Count.EqualTo(1));
            Assert.That(credits[0].Type, Is.EqualTo("Co-Executive Producer"));
            Assert.That(credits[0].Embedded!.Show!.Id, Is.EqualTo(2));
            Assert.That(credits[0].Embedded!.Show!.Externals!.TheTvdb, Is.EqualTo(248742));
        }

        _http.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task GetEpisodeGuestCastAsync_ReturnsGuestCast_WhenResponseIsValid()
    {
        // Arrange
        _http.Expect(HttpMethod.Get, "https://api.tvmaze.com/episodes/1/guestcast")
            .Respond("application/json", GuestCastJson);

        // Act
        var cast = await _client.GetEpisodeGuestCastAsync(1, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(cast, Has.Count.EqualTo(1));
            Assert.That(cast[0].Person!.Name, Is.EqualTo("Mackenzie Lintz"));
            Assert.That(cast[0].Character!.Name, Is.EqualTo("Norrie Calvert-Hill"));
            Assert.That(cast[0].Self, Is.False);
            Assert.That(cast[0].Voice, Is.True);
        }

        _http.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task GetEpisodeGuestCrewAsync_ReturnsGuestCrew_WhenResponseIsValid()
    {
        // Arrange
        _http.Expect(HttpMethod.Get, "https://api.tvmaze.com/episodes/1/guestcrew")
            .Respond("application/json", GuestCrewJson);

        // Act
        var crew = await _client.GetEpisodeGuestCrewAsync(1, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(crew, Has.Count.EqualTo(1));
            Assert.That(crew[0].Person!.Name, Is.EqualTo("Stephen King"));
            Assert.That(crew[0].Type, Is.EqualTo("Story"));
        }

        _http.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task LookupShowAsync_ReturnsShow_WhenExternalIdIsKnown()
    {
        // Arrange
        _http.Expect(HttpMethod.Get, "https://api.tvmaze.com/lookup/shows*")
            .WithQueryString("imdb", "tt0944947")
            .Respond("application/json", LookupJson);

        // Act
        var show = await _client.LookupShowAsync("imdb", "tt0944947", default).ConfigureAwait(false);

        // Assert
        Assert.That(show, Is.Not.Null);
        Assert.That(show!.Id, Is.EqualTo(82));
        _http.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task LookupShowAsync_ReturnsShow_WhenLookupIsMovedPermanently()
    {
        // Arrange
        _http.Expect(HttpMethod.Get, "https://api.tvmaze.com/lookup/shows*")
            .WithQueryString("imdb", "tt0944947")
            .Respond(_ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.MovedPermanently);
                response.Headers.Location = new Uri("https://api.tvmaze.com/shows/82");
                return response;
            });
        _http.Expect(HttpMethod.Get, "https://api.tvmaze.com/shows/82")
            .Respond("application/json", LookupJson);
        var services = new ServiceCollection();
        services.AddHttpClient(TvmazeClient.HttpClientName)
            .AddUpstreamCooldown(followRedirects: true)
            .ConfigurePrimaryHttpMessageHandler(() => _http);
        using var provider = services.BuildServiceProvider();
        var client = new TvmazeClient(
            _settingsService.Object,
            provider.GetRequiredService<IHttpClientFactory>(),
            NullLogger<TvmazeClient>.Instance);

        // Act
        var show = await client.LookupShowAsync("imdb", "tt0944947", default).ConfigureAwait(false);

        // Assert
        Assert.That(show, Is.Not.Null);
        Assert.That(show!.Id, Is.EqualTo(82));
        _http.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task GetShowWithEmbedsAsync_ReturnsNull_WhenNotFound()
    {
        // Arrange
        _http.Expect(HttpMethod.Get, "https://api.tvmaze.com/shows/999*")
            .Respond(HttpStatusCode.NotFound);

        // Act
        var show = await _client.GetShowWithEmbedsAsync(
            999, ["episodes", "images", "akas", "seasons"], default).ConfigureAwait(false);

        // Assert
        Assert.That(show, Is.Null);
        _http.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task GetEpisodeAsync_ReturnsNull_WhenNotFound()
    {
        // Arrange
        _http.Expect(HttpMethod.Get, "https://api.tvmaze.com/episodes/999")
            .Respond(HttpStatusCode.NotFound);

        // Act
        var episode = await _client.GetEpisodeAsync(999, default).ConfigureAwait(false);

        // Assert
        Assert.That(episode, Is.Null);
        _http.VerifyNoOutstandingExpectation();
    }

    [TestCase("", "", "https://api.tvmaze.com/shows/82")]
    [TestCase("", "cast", "https://api.tvmaze.com/shows/82?embed%5B%5D=cast")]
    [TestCase("cast", "", "https://api.tvmaze.com/shows/82?embed%5B%5D=cast")]
    public async Task GetShowWithEmbedsAsync_OmitsEmptyQueryValues_WhenEmbedsContainEmptyStrings(
        string first, string second, string expectedUri)
    {
        // Arrange
        var request = _http.When(HttpMethod.Get, "https://api.tvmaze.com/shows/82*")
            .With(message => message.RequestUri!.AbsoluteUri == expectedUri)
            .Respond("application/json", ShowJson);

        // Act
        var show = await _client.GetShowWithEmbedsAsync(82, [first, second], default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(show!.Id, Is.EqualTo(82));
            Assert.That(_http.GetMatchCount(request), Is.EqualTo(1));
        }
    }

    [Test]
    public async Task SearchShowsAsync_ReadsSettingsOncePerClient_WhenRequestsRunConcurrently()
    {
        // Arrange
        _http.When(HttpMethod.Get, "https://api.tvmaze.com/search/shows*").Respond("application/json", SearchJson);

        // Act
        await Task.WhenAll(_client.SearchShowsAsync("first", default), _client.SearchShowsAsync("second", default))
            .ConfigureAwait(false);

        // Assert
        _settingsService.Verify(value => value.GetExtensionGroupAsync(
            Constants.ExtensionId, ProviderSettings.GroupName, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestCase("null")]
    [TestCase("{")]
    [TestCase("")]
    public void GetShowsPageAsync_Throws_WhenPayloadIsUnusable(string payload)
    {
        // Arrange
        _http.When(HttpMethod.Get, "https://api.tvmaze.com/shows*").Respond("application/json", payload);

        // Act
        var exception = Assert.ThrowsAsync<HttpRequestException>(async () =>
            await _client.GetShowsPageAsync(0, default).ConfigureAwait(false));

        // Assert
        Assert.That(exception!.Message, Is.EqualTo("TVMaze returned an unusable JSON payload."));
    }
}
