using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Cardigann;
using Magpie.Extensions.Cardigann.Engine;
using Magpie.Extensions.Cardigann.Engine.Session;
using Magpie.Models;
using Magpie.Models.Data;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Cardigann;

[TestFixture]
public sealed class CardigannIndexerIntegrationTests
{
    private const string Magnet = "magnet:?xt=urn:btih:abcdef0123456789abcdef0123456789abcdef01";

    private CardigannDefinition _definition = null!;
    private Mock<IResultParser> _parser = null!;
    private Mock<ICardigannSessionManager> _sessions = null!;

    [SetUp]
    public void SetUp()
    {
        _definition = new CardigannDefinition { Id = "priv", Name = "Priv Tracker" };
        _definition.Links.Add("https://tracker.test/");
        _definition.Search.Paths.Add(new SearchPath { Path = "/search" });
        _definition.Search.Rows.Selector = "tr.row";

        _parser = new Mock<IResultParser>();
        _parser
            .Setup(p => p.Parse(
                It.IsAny<string>(),
                It.IsAny<CardigannDefinition>(),
                It.IsAny<Uri>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>()))
            .Returns([]);

        _sessions = new Mock<ICardigannSessionManager>();
        _sessions
            .Setup(s => s.EnsureLoggedInAsync(
                It.IsAny<string>(),
                It.IsAny<LoginBlock>(),
                It.IsAny<Uri>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CardigannLoginOutcome { Success = true });
        _sessions
            .Setup(s => s.SendAsync(
                It.IsAny<string>(),
                It.IsAny<Uri>(),
                It.IsAny<HttpMethod>(),
                It.IsAny<Uri>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html></html>"),
            });
    }

    [Test]
    public void SearchAsync_ThrowsAuthenticationFailure_WhenLoginFails()
    {
        // Arrange
        _definition.Login = new LoginBlock { Test = new LoginTest { Path = "/", Selector = "a.logout" } };
        _sessions
            .Setup(s => s.EnsureLoggedInAsync(
                It.IsAny<string>(),
                It.IsAny<LoginBlock>(),
                It.IsAny<Uri>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CardigannLoginOutcome
            {
                Success = false,
                Message = "Invalid password",
                FailureKind = ConfigurationFailureKind.Auth,
            });
        var integration = new CardigannIndexerIntegration(
            Guid.Empty,
            _definition,
            _parser.Object,
            _sessions.Object,
            NullLogger<CardigannIndexerIntegration>.Instance);

        // Act
        var action = async () => await integration
            .SearchAsync(new ReleaseSearchRequest { Title = "cool" }, new Dictionary<string, string>(), default)
            .ConfigureAwait(false);

        // Assert
        Assert.That(action, Throws.TypeOf<CardigannAuthenticationException>());
    }

    [Test]
    public async Task SearchAsync_ReturnsNoReleases_WhenLoginRequiresCaptcha()
    {
        // Arrange
        _definition.Login = new LoginBlock
        {
            Captcha = new LoginCaptcha(),
            Test = new LoginTest { Path = "/", Selector = "a.logout" },
        };
        var integration = new CardigannIndexerIntegration(
            Guid.Empty,
            _definition,
            _parser.Object,
            _sessions.Object,
            NullLogger<CardigannIndexerIntegration>.Instance);

        // Act
        var releases = await integration
            .SearchAsync(new ReleaseSearchRequest { Title = "cool" }, new Dictionary<string, string>(), default)
            .ConfigureAwait(false);

        // Assert
        Assert.That(releases, Is.Empty);

        _sessions.Verify(
            s => s.EnsureLoggedInAsync(
                It.IsAny<string>(),
                It.IsAny<LoginBlock>(),
                It.IsAny<Uri>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task SearchAsync_InvalidatesSessionAndRetries_WhenSearchResponseIsForbidden()
    {
        // Arrange
        _definition.Login = new LoginBlock { Test = new LoginTest { Path = "/", Selector = "a.logout" } };
        _parser
            .Setup(p => p.Parse(
                It.IsAny<string>(),
                It.IsAny<CardigannDefinition>(),
                It.IsAny<Uri>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>()))
            .Returns([new Release { Protocol = "torrent", Title = "Cool Release 1080p" }]);
        _sessions
            .Setup(s => s.IsLoggedOut(It.IsAny<LoginBlock>(), HttpStatusCode.Forbidden, It.IsAny<string>()))
            .Returns(true);
        _sessions
            .SetupSequence(s => s.SendAsync(
                It.IsAny<string>(),
                It.IsAny<Uri>(),
                It.IsAny<HttpMethod>(),
                It.IsAny<Uri>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("denied"),
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html></html>"),
            });
        var integration = new CardigannIndexerIntegration(
            Guid.Empty,
            _definition,
            _parser.Object,
            _sessions.Object,
            NullLogger<CardigannIndexerIntegration>.Instance);

        // Act
        var releases = await integration
            .SearchAsync(new ReleaseSearchRequest { Title = "cool" }, new Dictionary<string, string>(), default)
            .ConfigureAwait(false);

        // Assert
        Assert.That(releases.ToList(), Has.Count.EqualTo(1));

        _sessions.Verify(s => s.InvalidateSession("priv@https://tracker.test"), Times.Once);
        _sessions.Verify(
            s => s.SendAsync(
                It.IsAny<string>(),
                It.IsAny<Uri>(),
                It.IsAny<HttpMethod>(),
                It.IsAny<Uri>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Test]
    public async Task SearchAsync_ReturnsMaximumResults_WhenParserReturnsMore()
    {
        // Arrange
        _parser
            .Setup(p => p.Parse(
                It.IsAny<string>(),
                It.IsAny<CardigannDefinition>(),
                It.IsAny<Uri>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>()))
            .Returns(
            [
                new Release { Protocol = "torrent", Title = "One" },
                new Release { Protocol = "torrent", Title = "Two" },
                new Release { Protocol = "torrent", Title = "Three" },
            ]);
        var integration = new CardigannIndexerIntegration(
            Guid.Empty,
            _definition,
            _parser.Object,
            _sessions.Object,
            NullLogger<CardigannIndexerIntegration>.Instance);

        // Act
        var releases = await integration.SearchAsync(
            new ReleaseSearchRequest { Title = "cool", MaximumResults = 2 },
            new Dictionary<string, string>(),
            default).ConfigureAwait(false);

        // Assert
        Assert.That(releases.Select(release => release.Title), Is.EqualTo(["One", "Two"]));
    }

    [Test]
    public async Task SearchAsync_ResolvesEveryRequestedRow_WhenMoreThanFiftyDetailsAreNeeded()
    {
        // Arrange
        _definition.Download = new DownloadBlock { Selector = "a.magnet", Attribute = "href" };
        _parser
            .Setup(p => p.Parse(
                It.IsAny<string>(),
                It.IsAny<CardigannDefinition>(),
                It.IsAny<Uri>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>()))
            .Returns(
            [
                .. Enumerable
                    .Range(0, 60)
                    .Select(index => new Release
                    {
                        Protocol = "torrent",
                        Title = "Cool Release " + index,
                        InfoUrl = new Uri("https://tracker.test/details/" + index),
                    }),
                new Release { Protocol = "torrent", Title = "Resolved magnet", InfoUrl = new Uri(Magnet) }
            ]);
        _parser
            .Setup(p => p.ExtractDownloadTarget(
                It.IsAny<string>(),
                It.IsAny<DownloadBlock>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>()))
            .Returns(Magnet);
        var integration = new CardigannIndexerIntegration(
            Guid.Empty,
            _definition,
            _parser.Object,
            _sessions.Object,
            NullLogger<CardigannIndexerIntegration>.Instance);

        // Act
        var releases = (await integration
            .SearchAsync(
                new ReleaseSearchRequest { Title = "cool", MaximumResults = 200 },
                new Dictionary<string, string>(),
                default)
            .ConfigureAwait(false)).ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(releases, Has.Count.EqualTo(61));
            Assert.That(releases[^1].Title, Is.EqualTo("Resolved magnet"));
        }

        _parser.Verify(
            p => p.ExtractDownloadTarget(
                It.IsAny<string>(),
                It.IsAny<DownloadBlock>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>()),
            Times.Exactly(60));
    }

    [Test]
    public async Task SearchAsync_ResolvesDetailPageIntoInfoUrl_WhenDownloadBlockIsConfigured()
    {
        // Arrange
        _definition.Download = new DownloadBlock { Selector = "a.magnet", Attribute = "href" };
        _parser
            .Setup(p => p.Parse(
                It.IsAny<string>(),
                It.IsAny<CardigannDefinition>(),
                It.IsAny<Uri>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>()))
            .Returns(
            [
                new Release
                {
                    Protocol = "torrent",
                    Title = "Cool Release 1080p",
                    InfoUrl = new Uri("https://tracker.test/details/42"),
                },
            ]);
        _parser
            .Setup(p => p.ExtractDownloadTarget(
                It.IsAny<string>(),
                It.IsAny<DownloadBlock>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>()))
            .Returns(Magnet);
        var integration = new CardigannIndexerIntegration(
            Guid.Empty,
            _definition,
            _parser.Object,
            _sessions.Object,
            NullLogger<CardigannIndexerIntegration>.Instance);

        // Act
        var releases = (await integration
            .SearchAsync(new ReleaseSearchRequest { Title = "cool" }, new Dictionary<string, string>(), default)
            .ConfigureAwait(false)).ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(releases, Has.Count.EqualTo(1));
            Assert.That(releases[0].InfoUrl, Is.EqualTo(new Uri(Magnet)));
        }

        _parser.Verify(p => p.ApplyResolvedDownload(releases[0], new Uri(Magnet)), Times.Once);
    }

    [Test]
    public async Task SearchAsync_DropsRelease_WhenDownloadSelectorMissesOnTheDetailPage()
    {
        // Arrange
        _definition.Download = new DownloadBlock { Selector = "a.magnet", Attribute = "href" };
        _parser
            .Setup(p => p.Parse(
                It.IsAny<string>(),
                It.IsAny<CardigannDefinition>(),
                It.IsAny<Uri>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>()))
            .Returns(
            [
                new Release
                {
                    Protocol = "torrent",
                    Title = "Cool Release 1080p",
                    InfoUrl = new Uri("https://tracker.test/details/42"),
                },
            ]);
        _parser
            .Setup(p => p.ExtractDownloadTarget(
                It.IsAny<string>(),
                It.IsAny<DownloadBlock>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>()))
            .Returns((string?)null);
        var integration = new CardigannIndexerIntegration(
            Guid.Empty,
            _definition,
            _parser.Object,
            _sessions.Object,
            NullLogger<CardigannIndexerIntegration>.Instance);

        // Act
        var releases = await integration
            .SearchAsync(new ReleaseSearchRequest { Title = "cool" }, new Dictionary<string, string>(), default)
            .ConfigureAwait(false);

        // Assert
        Assert.That(releases, Is.Empty);
    }

    [Test]
    public async Task SearchAsync_RendersKeywordsIntoTheRequestUri_WhenSearchInputsAreTemplated()
    {
        // Arrange
        _definition.Search.Inputs["q"] = "{{ .Keywords }}";
        var integration = new CardigannIndexerIntegration(
            Guid.Empty,
            _definition,
            _parser.Object,
            _sessions.Object,
            NullLogger<CardigannIndexerIntegration>.Instance);

        // Act
        await integration.SearchAsync(
            new ReleaseSearchRequest { Title = "cool movie" },
            new Dictionary<string, string>(),
            default)
            .ConfigureAwait(false);

        // Assert
        _sessions.Verify(
            s => s.SendAsync(
                "priv@https://tracker.test",
                new Uri("https://tracker.test/"),
                HttpMethod.Get,
                new Uri("https://tracker.test/search?q=cool%20movie"),
                null,
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestCase("password")]
    [TestCase("cookie")]
    [TestCase("text")]
    public async Task SearchAsync_PostsInputsAndRecordsRequest_WhenSearchMethodIsPost(string settingType)
    {
        // Arrange
        _definition.Search.Paths[0].Method = "post";
        _definition.Settings.Add(new CardigannSettingDefinition { Name = "passkey", Type = settingType });
        _definition.Search.Inputs["q"] = "{{ .Keywords }}";
        _definition.Search.Inputs["passkey"] = "{{ .Config.passkey }}";
        var integration = new CardigannIndexerIntegration(
            Guid.Empty,
            _definition,
            _parser.Object,
            _sessions.Object,
            NullLogger<CardigannIndexerIntegration>.Instance);
        using var activity = new Activity("magpie.release-query.request").Start();

        // Act
        await integration.SearchAsync(
            new ReleaseSearchRequest { Title = "cool movie" },
            new Dictionary<string, string> { ["passkey"] = "secret-value" },
            default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(activity.GetTagItem("magpie.request.method"), Is.EqualTo("POST"));
            Assert.That(
                activity.GetTagItem("magpie.request.target"),
                Is.EqualTo("/search"));
            Assert.That(
                activity.GetTagItem("magpie.request.body"),
                Is.EqualTo("q=cool%20movie&passkey=secret-value"));
        }

        _sessions.Verify(
            sessions => sessions.SendAsync(
                It.Is<string>(key => key.StartsWith("priv@https://tracker.test#", StringComparison.Ordinal) &&
                    !key.Contains("secret-value", StringComparison.Ordinal)),
                new Uri("https://tracker.test/"),
                HttpMethod.Post,
                new Uri("https://tracker.test/search"),
                "q=cool%20movie&passkey=secret-value",
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestCase("cool movie", "POST")]
    [TestCase(null, "GET")]
    public async Task SearchAsync_ResolvesTheVerbFromTheRenderedTemplate_WhenTheSearchMethodIsTemplated(
        string? title,
        string expected)
    {
        // Arrange
        _definition.Search.Paths[0].Method = "{{ if .Keywords }}post{{ else }}get{{ end }}";
        var integration = new CardigannIndexerIntegration(
            Guid.Empty,
            _definition,
            _parser.Object,
            _sessions.Object,
            NullLogger<CardigannIndexerIntegration>.Instance);
        using var activity = new Activity("magpie.release-query.request").Start();

        // Act
        await integration.SearchAsync(
            new ReleaseSearchRequest { Title = title },
            new Dictionary<string, string>(),
            default)
            .ConfigureAwait(false);

        // Assert
        Assert.That(activity.GetTagItem("magpie.request.method"), Is.EqualTo(expected));
    }

    [Test]
    public void SearchAsync_Throws_WhenTheSearchMethodIsAnUnsupportedVerb()
    {
        // Arrange
        _definition.Search.Paths[0].Method = "put";
        var integration = new CardigannIndexerIntegration(
            Guid.Empty,
            _definition,
            _parser.Object,
            _sessions.Object,
            NullLogger<CardigannIndexerIntegration>.Instance);

        // Act
        var exception = Assert.ThrowsAsync<InvalidOperationException>(() => integration.SearchAsync(
            new ReleaseSearchRequest { Title = "cool" },
            new Dictionary<string, string>(),
            default));

        // Assert
        Assert.That(exception!.Message, Is.EqualTo("Cardigann search method 'put' is not supported."));
    }

    [Test]
    public async Task SearchAsync_PassesTheConfiguredCookieToTheSessionManager_WhenACookieSettingExists()
    {
        // Arrange
        _definition.Login = new LoginBlock { Method = "cookie", Test = new LoginTest { Path = "/" } };
        _definition.Settings.Add(new CardigannSettingDefinition { Name = "cookie", Type = "cookie" });
        var integration = new CardigannIndexerIntegration(
            Guid.Empty,
            _definition,
            _parser.Object,
            _sessions.Object,
            NullLogger<CardigannIndexerIntegration>.Instance);
        var settings = new Dictionary<string, string>(StringComparer.Ordinal) { ["cookie"] = "sid=PASTED" };

        // Act
        await integration.SearchAsync(new ReleaseSearchRequest { Title = "cool" }, settings, default)
            .ConfigureAwait(false);

        // Assert
        _sessions.Verify(
            s => s.EnsureLoggedInAsync(
                It.Is<string>(key => key.StartsWith("priv@https://tracker.test#", StringComparison.Ordinal) &&
                    !key.Contains("PASTED", StringComparison.Ordinal)),
                _definition.Login,
                new Uri("https://tracker.test/"),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                "sid=PASTED",
                It.IsAny<string>(),
                false,
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Test]
    public void SearchAsync_ThrowsTheUnsolvedChallengeError_WhenTheResponseIsACloudflareChallenge()
    {
        // Arrange
        _sessions
            .Setup(s => s.SendAsync(
                It.IsAny<string>(),
                It.IsAny<Uri>(),
                It.IsAny<HttpMethod>(),
                It.IsAny<Uri>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var challenge = new HttpResponseMessage(HttpStatusCode.Forbidden)
                {
                    Content = new StringContent("<html><body>Just a moment...</body></html>"),
                };
                challenge.Headers.TryAddWithoutValidation("CF-RAY", "7a1b2c3d");
                return challenge;
            });
        var integration = new CardigannIndexerIntegration(
            Guid.Empty,
            _definition,
            _parser.Object,
            _sessions.Object,
            NullLogger<CardigannIndexerIntegration>.Instance);

        // Act
        var exception = Assert.ThrowsAsync<HttpRequestException>(() => integration.SearchAsync(
            new ReleaseSearchRequest { Title = "cool" },
            new Dictionary<string, string>(),
            default));

        // Assert
        Assert.That(
            exception!.Message,
            Is.EqualTo(
                "Cardigann tracker priv is behind Cloudflare (HTTP 403 with CF headers) and the challenge " +
                "could not be solved."));
    }

    [Test]
    public async Task TestConnectionAsync_ReturnsSuccess_WhenTheLoginOutcomeSucceeds()
    {
        // Arrange
        _definition.Login = new LoginBlock { Test = new LoginTest { Path = "/", Selector = "a.logout" } };
        var integration = new CardigannIndexerIntegration(
            Guid.Empty,
            _definition,
            _parser.Object,
            _sessions.Object,
            NullLogger<CardigannIndexerIntegration>.Instance);

        // Act
        var result = await integration.TestConnectionAsync(new Dictionary<string, string>(), default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.FailureKind, Is.EqualTo(ConfigurationFailureKind.None));
        }
    }

    [Test]
    public async Task TestConnectionAsync_ReportsTheLoginFailure_WhenTheLoginOutcomeFails()
    {
        // Arrange
        _definition.Login = new LoginBlock { Test = new LoginTest { Path = "/", Selector = "a.logout" } };
        _sessions
            .Setup(s => s.EnsureLoggedInAsync(
                It.IsAny<string>(),
                It.IsAny<LoginBlock>(),
                It.IsAny<Uri>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CardigannLoginOutcome
            {
                Success = false,
                Message = "Invalid password",
                FailureKind = ConfigurationFailureKind.Auth,
            });
        var integration = new CardigannIndexerIntegration(
            Guid.Empty,
            _definition,
            _parser.Object,
            _sessions.Object,
            NullLogger<CardigannIndexerIntegration>.Instance);

        // Act
        var result = await integration.TestConnectionAsync(new Dictionary<string, string>(), default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.FailureKind, Is.EqualTo(ConfigurationFailureKind.Auth));
            Assert.That(result.Message, Is.EqualTo("Invalid password"));
        }
    }

    [Test]
    public async Task TestConnectionAsync_ReportsCaptcha_WhenTheLoginRequiresCaptcha()
    {
        // Arrange
        _definition.Login = new LoginBlock
        {
            Captcha = new LoginCaptcha(),
            Test = new LoginTest { Path = "/", Selector = "a.logout" },
        };
        var integration = new CardigannIndexerIntegration(
            Guid.Empty,
            _definition,
            _parser.Object,
            _sessions.Object,
            NullLogger<CardigannIndexerIntegration>.Instance);

        // Act
        var result = await integration.TestConnectionAsync(new Dictionary<string, string>(), default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.FailureKind, Is.EqualTo(ConfigurationFailureKind.Auth));
            Assert.That(result.Message, Does.Contain("captcha").IgnoreCase);
        }
    }

    [Test]
    public async Task TestConnectionAsync_ReportsSuccess_WhenTheTrackerAnswersWithoutLogin()
    {
        // Arrange
        var integration = new CardigannIndexerIntegration(
            Guid.Empty,
            _definition,
            _parser.Object,
            _sessions.Object,
            NullLogger<CardigannIndexerIntegration>.Instance);

        // Act
        var result = await integration.TestConnectionAsync(new Dictionary<string, string>(), default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.Message, Does.Contain("https://tracker.test/"));
        }
    }

    [Test]
    public void SearchFacets_MapsCapsModesAndAliasesEpisode_WhenModesAreDeclared()
    {
        // Arrange
        _definition.Caps.Modes["search"] = ["q"];
        _definition.Caps.Modes["tv-search"] = ["q", "season", "ep"];
        var integration = new CardigannIndexerIntegration(
            Guid.Empty,
            _definition,
            _parser.Object,
            _sessions.Object,
            NullLogger<CardigannIndexerIntegration>.Instance);

        // Act
        var facets = integration.SearchFacets;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(facets, Has.Count.EqualTo(2));
            Assert.That(facets[0], Is.EquivalentTo(["q"]));
            Assert.That(facets[1], Is.EquivalentTo(["q", "season", "episode"]));
            Assert.That(integration.SupportsKeywordSearch, Is.True);
        }
    }

    [Test]
    public void SupportsKeywordSearch_ReturnsFalse_WhenNoModeAcceptsAKeywordQuery()
    {
        // Arrange
        _definition.Caps.Modes["movie-search"] = ["imdbid"];
        var integration = new CardigannIndexerIntegration(
            Guid.Empty,
            _definition,
            _parser.Object,
            _sessions.Object,
            NullLogger<CardigannIndexerIntegration>.Instance);

        // Act
        var result = integration.SupportsKeywordSearch;

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task SearchAsync_ReturnsRowsFromEveryPath_WhenDefinitionHasMultiplePaths()
    {
        // Arrange
        _definition.Search.Paths.Add(new SearchPath { Path = "/second" });
        _parser.SetupSequence(parser => parser.Parse(It.IsAny<string>(), _definition, It.IsAny<Uri>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>()))
            .Returns([new Release { Protocol = "torrent", Title = "First" }])
            .Returns([new Release { Protocol = "torrent", Title = "Second" }]);
        var integration = new CardigannIndexerIntegration(Guid.Empty, _definition, _parser.Object,
            _sessions.Object, NullLogger<CardigannIndexerIntegration>.Instance);

        // Act
        var rows = await integration.SearchAsync(new ReleaseSearchRequest { MaximumResults = 2 },
            new Dictionary<string, string>(), default).ConfigureAwait(false);

        // Assert
        Assert.That(rows.Select(row => row.Title), Is.EqualTo(["First", "Second"]));
    }

    [Test]
    public async Task SearchAsync_StopsRequestingPaths_WhenTheRequestedResultCountIsReached()
    {
        // Arrange
        _definition.Search.Paths.Add(new SearchPath { Path = "/second" });
        _parser.Setup(parser => parser.Parse(It.IsAny<string>(), _definition, It.IsAny<Uri>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>()))
            .Returns([new Release { Protocol = "torrent", Title = "First" }]);
        var integration = new CardigannIndexerIntegration(Guid.Empty, _definition, _parser.Object,
            _sessions.Object, NullLogger<CardigannIndexerIntegration>.Instance);
        using var activity = new Activity("magpie.release-query.request").Start();

        // Act
        var rows = await integration.SearchAsync(new ReleaseSearchRequest { MaximumResults = 1 },
            new Dictionary<string, string>(), default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(rows.Select(row => row.Title), Is.EqualTo(["First"]));
            Assert.That(activity.GetTagItem("magpie.request.target"), Is.EqualTo("/search"));
        }

        _sessions.Verify(
            sessions => sessions.SendAsync(It.IsAny<string>(), It.IsAny<Uri>(), It.IsAny<HttpMethod>(),
                It.IsAny<Uri>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task SearchAsync_RecordsEveryRequestedPath_WhenDefinitionHasMultiplePaths()
    {
        // Arrange
        _definition.Search.Paths.Add(new SearchPath { Path = "/second" });
        var integration = new CardigannIndexerIntegration(Guid.Empty, _definition, _parser.Object,
            _sessions.Object, NullLogger<CardigannIndexerIntegration>.Instance);
        using var activity = new Activity("magpie.release-query.request").Start();

        // Act
        await integration.SearchAsync(new ReleaseSearchRequest { MaximumResults = 5 },
            new Dictionary<string, string>(), default).ConfigureAwait(false);

        // Assert
        Assert.That(activity.GetTagItem("magpie.request.target"), Is.EqualTo("/search /second"));
    }

    [TestCase("get")]
    [TestCase("post")]
    public async Task SearchAsync_EncodesSubstitutions_WhenPathAndRawInputContainQueryValues(string method)
    {
        // Arrange
        _definition.Search.Paths[0] = new SearchPath { Path = "{{ .Keywords }}", Method = method };
        _definition.Search.Inputs["$raw"] = "q={{ .Keywords }}&fixed=1";
        Uri? sentUri = null;
        string? sentBody = null;
        _sessions.Setup(session => session.SendAsync(It.IsAny<string>(), It.IsAny<Uri>(), It.IsAny<HttpMethod>(),
                It.IsAny<Uri>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, Uri, HttpMethod, Uri, string?, string, bool, CancellationToken>(
                (_, _, _, uri, body, _, _, _) => { sentUri = uri; sentBody = body; })
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("rows") });
        var integration = new CardigannIndexerIntegration(Guid.Empty, _definition, _parser.Object,
            _sessions.Object, NullLogger<CardigannIndexerIntegration>.Instance);

        // Act
        await integration.SearchAsync(new ReleaseSearchRequest { Title = "//attacker.test/a&admin=1" },
            new Dictionary<string, string>(), default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sentUri!.Host, Is.EqualTo("tracker.test"));
            Assert.That(sentUri.AbsolutePath, Is.EqualTo("/%2F%2Fattacker.test%2Fa%26admin%3D1"));
            Assert.That(sentBody ?? sentUri.Query.TrimStart('?'),
                Is.EqualTo("q=%2F%2Fattacker.test%2Fa%26admin%3D1&fixed=1"));
        }
    }

    [TestCase("get")]
    [TestCase("post")]
    public async Task SearchAsync_SkipsTarget_WhenDefinitionLeavesConfiguredAuthority(string method)
    {
        // Arrange
        _definition.Search.Paths[0] = new SearchPath { Path = "//attacker.test/search", Method = method };
        var integration = new CardigannIndexerIntegration(Guid.Empty, _definition, _parser.Object,
            _sessions.Object, NullLogger<CardigannIndexerIntegration>.Instance);

        // Act
        var rows = await integration.SearchAsync(new ReleaseSearchRequest(),
            new Dictionary<string, string>(), default).ConfigureAwait(false);

        // Assert
        Assert.That(rows, Is.Empty);
        _sessions.Verify(session => session.SendAsync(It.IsAny<string>(), It.IsAny<Uri>(), It.IsAny<HttpMethod>(),
            It.IsAny<Uri>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestCase("get", "//api.tracker.test/search")]
    [TestCase("post", "//api.tracker.test/search")]
    [TestCase("get", "http://tracker.test/search")]
    [TestCase("post", "https://tracker.test:8443/search")]
    [TestCase("get", "https://user:password@tracker.test/search")]
    public async Task SearchAsync_RetainsValidResults_WhenOnePathLeavesConfiguredAuthority(string method, string path)
    {
        // Arrange
        _definition.Search.Paths.Add(new SearchPath { Path = path, Method = method });
        _definition.Search.Paths.Add(new SearchPath { Path = "/third" });
        _parser.SetupSequence(parser => parser.Parse(It.IsAny<string>(), _definition, It.IsAny<Uri>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>()))
            .Returns([new Release { Protocol = "torrent", Title = "First" }])
            .Returns([new Release { Protocol = "torrent", Title = "Third" }]);
        var integration = new CardigannIndexerIntegration(Guid.Empty, _definition, _parser.Object,
            _sessions.Object, NullLogger<CardigannIndexerIntegration>.Instance);
        using var activity = new Activity("magpie.release-query.request").Start();

        // Act
        var rows = await integration.SearchAsync(new ReleaseSearchRequest { MaximumResults = 5 },
            new Dictionary<string, string>(), default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(rows.Select(row => row.Title), Is.EqualTo(["First", "Third"]));
            Assert.That(activity.GetTagItem("magpie.request.target"), Is.EqualTo("/search /third"));
        }

        _sessions.Verify(session => session.SendAsync(It.IsAny<string>(), It.IsAny<Uri>(), It.IsAny<HttpMethod>(),
            It.Is<Uri>(uri => uri.AbsoluteUri != "https://tracker.test/search" &&
                uri.AbsoluteUri != "https://tracker.test/third"),
            It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _sessions.Verify(session => session.SendAsync(It.IsAny<string>(), It.IsAny<Uri>(), It.IsAny<HttpMethod>(),
            It.IsAny<Uri>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Test]
    public void SearchAsync_ReportsLoginFailure_WhenSessionExpiresBeforeDetailFetch()
    {
        // Arrange
        _definition.Login = new LoginBlock { Method = "cookie" };
        _definition.Download = new DownloadBlock { Selector = "a.download" };
        _parser.Setup(parser => parser.Parse(It.IsAny<string>(), _definition, It.IsAny<Uri>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>()))
            .Returns(
                [new Release { Protocol = "torrent", Title = "One", InfoUrl = new Uri("https://tracker.test/item") }]);
        _sessions.SetupSequence(session => session.EnsureLoggedInAsync(It.IsAny<string>(), _definition.Login,
                It.IsAny<Uri>(), It.IsAny<IReadOnlyDictionary<string, object?>>(), It.IsAny<string?>(),
                It.IsAny<string>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CardigannLoginOutcome { Success = true })
            .ReturnsAsync(new CardigannLoginOutcome { Success = true })
            .ReturnsAsync(new CardigannLoginOutcome { Success = false, FailureKind = ConfigurationFailureKind.Auth });
        var integration = new CardigannIndexerIntegration(Guid.Empty, _definition, _parser.Object,
            _sessions.Object, NullLogger<CardigannIndexerIntegration>.Instance);

        // Act
        var action = async () => await integration.SearchAsync(new ReleaseSearchRequest { MaximumResults = 1 },
            new Dictionary<string, string>(), default).ConfigureAwait(false);

        // Assert
        Assert.That(action, Throws.TypeOf<CardigannAuthenticationException>());
    }

    [Test]
    public async Task SearchAsync_RecordsTextSettings_WhenCredentialsAppearInTarget()
    {
        // Arrange
        _definition.Search.Paths[0].Path = "/{{ .Config.passkey }}/search";
        _definition.Search.Inputs["key"] = "{{ .Config.passkey }}";
        _definition.Settings.Add(new CardigannSettingDefinition { Name = "passkey", Type = "text" });
        var integration = new CardigannIndexerIntegration(Guid.Empty, _definition, _parser.Object,
            _sessions.Object, NullLogger<CardigannIndexerIntegration>.Instance);
        using var activity = new Activity("magpie.release-query.request").Start();

        // Act
        await integration.SearchAsync(new ReleaseSearchRequest(),
            new Dictionary<string, string> { ["passkey"] = "secret-value" }, default).ConfigureAwait(false);

        // Assert
        Assert.That(
            activity.GetTagItem("magpie.request.target"),
            Is.EqualTo("/secret-value/search?key=secret-value"));
    }
}
