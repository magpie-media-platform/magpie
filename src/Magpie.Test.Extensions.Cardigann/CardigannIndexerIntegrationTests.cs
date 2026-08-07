using System;
using System.Collections.Generic;
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
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html></html>"),
            });
    }

    [Test]
    public async Task SearchAsync_ReturnsNoReleases_WhenLoginFails()
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
    public async Task SearchAsync_ReturnsNoReleases_WhenLoginRequiresCaptcha()
    {
        // Arrange
        _definition.Login = new LoginBlock
        {
            Captcha = new LoginCaptcha(),
            Test = new LoginTest { Path = "/", Selector = "a.logout" },
        };
        var integration = new CardigannIndexerIntegration(
            _definition,
            _parser.Object,
            _sessions.Object,
            NullLogger<CardigannIndexerIntegration>.Instance);

        // Act
        var releases = await integration
            .SearchAsync(new ReleaseSearchRequest { Title = "cool" }, new Dictionary<string, string>(), default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
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
            _definition,
            _parser.Object,
            _sessions.Object,
            NullLogger<CardigannIndexerIntegration>.Instance);

        // Act
        var releases = await integration
            .SearchAsync(new ReleaseSearchRequest { Title = "cool" }, new Dictionary<string, string>(), default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(releases.ToList(), Has.Count.EqualTo(1));
            _sessions.Verify(s => s.InvalidateSession("priv@https://tracker.test"), Times.Once);
            _sessions.Verify(
                s => s.SendAsync(
                    It.IsAny<string>(),
                    It.IsAny<Uri>(),
                    It.IsAny<HttpMethod>(),
                    It.IsAny<Uri>(),
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()),
                Times.Exactly(2));
        }
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
            .Setup(p => p.ExtractDownloadTarget(It.IsAny<string>(), It.IsAny<DownloadBlock>()))
            .Returns(Magnet);
        var integration = new CardigannIndexerIntegration(
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
            _parser.Verify(p => p.ApplyResolvedDownload(releases[0], new Uri(Magnet)), Times.Once);
        }
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
            .Setup(p => p.ExtractDownloadTarget(It.IsAny<string>(), It.IsAny<DownloadBlock>()))
            .Returns((string?)null);
        var integration = new CardigannIndexerIntegration(
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
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task SearchAsync_PassesTheConfiguredCookieToTheSessionManager_WhenACookieSettingExists()
    {
        // Arrange
        _definition.Login = new LoginBlock { Method = "cookie", Test = new LoginTest { Path = "/" } };
        _definition.Settings.Add(new CardigannSettingDefinition { Name = "cookie", Type = "cookie" });
        var integration = new CardigannIndexerIntegration(
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
                "priv@https://tracker.test",
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
    public async Task TestConnectionAsync_ReturnsSuccess_WhenTheLoginOutcomeSucceeds()
    {
        // Arrange
        _definition.Login = new LoginBlock { Test = new LoginTest { Path = "/", Selector = "a.logout" } };
        var integration = new CardigannIndexerIntegration(
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
            _definition,
            _parser.Object,
            _sessions.Object,
            NullLogger<CardigannIndexerIntegration>.Instance);

        // Act
        var result = integration.SupportsKeywordSearch;

        // Assert
        Assert.That(result, Is.False);
    }
}
