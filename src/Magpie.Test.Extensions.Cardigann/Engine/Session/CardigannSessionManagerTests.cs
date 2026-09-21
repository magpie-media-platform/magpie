using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using AngleSharp.Dom;

using Magpie.Extensions.Cardigann;
using Magpie.Extensions.Cardigann.Engine;
using Magpie.Extensions.Cardigann.Engine.Session;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Sdk.Caching;
using Magpie.Sdk.Services;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;
using Moq.Protected;

using NUnit.Framework;

using RichardSzalay.MockHttp;

namespace Magpie.Test.Extensions.Cardigann.Engine.Session;

[TestFixture]
public sealed class CardigannSessionManagerTests
{
    private const string LoginPage =
        """<html><body><form action="/takelogin.php"><input name="token" value="csrf1"/>""" +
        """<input name="username"/><input name="password"/></form></body></html>""";

    private const string LogoutPage = """<html><body><a class="logout" href="/logout">Logout</a></body></html>""";

    private const string SessionKey = "priv@https://tracker.test";

    private const string ChallengePage = "<html><body>Just a moment...</body></html>";

    private const string SolvedResponse =
        """
        {"status":"ok","solution":{"url":"https://tracker.test/search","status":200,
        "cookies":[{"name":"cf_clearance","value":"TOKEN"}],"userAgent":"SolverAgent/1.0"}}
        """;

    private const string SolvedPageResponse =
        """
        {"status":"ok","solution":{"url":"https://tracker.test/search","status":200,
        "cookies":[{"name":"cf_clearance","value":"TOKEN"}],"userAgent":"SolverAgent/1.0",
        "response":"<html><body><tr class=\"row\">Cool Release</tr></body></html>"}}
        """;

    private const string SolvedJsonResponse =
        """
        {"status":"ok","solution":{"url":"https://tracker.test/search","status":200,
        "cookies":[{"name":"cf_clearance","value":"TOKEN"}],"userAgent":"SolverAgent/1.0",
        "response":"<html><head></head><body><pre>{\"results\":[{\"title\":\"Cool\"}]}</pre></body></html>"}}
        """;

    private const string SolvedFirstClearanceResponse =
        """
        {"status":"ok","solution":{"url":"https://tracker.test/","status":200,
        "cookies":[{"name":"cf_clearance","value":"FIRST"}],"userAgent":"SolverAgent/1.0"}}
        """;

    private const string SolvedSecondClearanceResponse =
        """
        {"status":"ok","solution":{"url":"https://tracker.test/","status":200,
        "cookies":[{"name":"cf_clearance","value":"SECOND"}],"userAgent":"SolverAgent/1.0"}}
        """;

    private const string SolvedPageWithoutCookiesResponse =
        """
        {"status":"ok","solution":{"url":"https://tracker.test/search","status":200,"cookies":[],
        "userAgent":"SolverAgent/1.0",
        "response":"<html><body><tr class=\"row\">Cool Release</tr></body></html>"}}
        """;

    private static readonly Uri BaseUri = new("https://tracker.test/");

    private static readonly Dictionary<string, object?> Variables = new()
    {
        [".Config.username"] = "myuser",
        [".Config.password"] = "secret",
    };

    private ICardigannSessionManager _sessions = null!;
    private MockHttpMessageHandler _http = null!;
    private Mock<IResultParser> _resultParser = null!;
    private IHttpClientFactory _clientFactory = null!;
    private ISettingsService _settingsService = null!;
    private ICacheFactory _memoFactory = null!;
    private SettingsGroup _solverGroup = null!;
    private LoginBlock _login = null!;
    private LoginBlock _cookieLogin = null!;

    [SetUp]
    public void SetUp()
    {
        _http = new MockHttpMessageHandler();
        var clientFactory = new Mock<IHttpClientFactory>();
        clientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => _http.ToHttpClient());
        _clientFactory = clientFactory.Object;

        _solverGroup = new SettingsGroup { Name = "magpie.cardigann" };
        _solverGroup.Settings.Add(new Setting { Key = "flareSolverrUrl", Value = "http://solver.test:8191" });
        var settingsService = new Mock<ISettingsService>();
        settingsService
            .Setup(service => service.GetExtensionGroupAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new SettingsGroupDetail { Group = _solverGroup });
        _settingsService = settingsService.Object;
        var memoFactory = new Mock<ICacheFactory>();
        var caches = new Dictionary<string, ICache>(StringComparer.Ordinal);
        memoFactory.Setup(factory => factory.GetOrCreate(It.IsAny<string>(), It.IsAny<Func<CachePolicy>>()))
            .Returns<string, Func<CachePolicy>?>((name, _) =>
            {
                if (caches.TryGetValue(name, out var existing))
                {
                    return existing;
                }

                var values = new Dictionary<string, object?>(StringComparer.Ordinal);
                var cache = new Mock<ICache>();
                cache.Setup(value => value.Set(It.IsAny<string>(), It.IsAny<It.IsAnyType>()))
                    .Callback(new InvocationAction(invocation =>
                        values[(string)invocation.Arguments[0]] = invocation.Arguments[1]));
                cache.Setup(value => value.TryGet(It.IsAny<string>(), out It.Ref<It.IsAnyType?>.IsAny))
                    .Returns(new InvocationFunc(invocation =>
                    {
                        var found = values.TryGetValue((string)invocation.Arguments[0], out var value);
                        ((object?[])invocation.Arguments)[1] = value;
                        return found;
                    }));
                cache.Setup(value => value.Remove(It.IsAny<string>())).Returns<string>(values.Remove);
                return caches[name] = cache.Object;
            });
        _memoFactory = memoFactory.Object;

        _resultParser = new Mock<IResultParser>();
        _sessions = new CardigannSessionManager(
            _clientFactory,
            _resultParser.Object,
            new FlareSolverrClient(
                _clientFactory,
                _settingsService,
                _memoFactory,
                NullLogger<FlareSolverrClient>.Instance,
                new SolverQueue()),
            new CardigannSessionState(),
            TimeProvider.System,
            NullLogger<CardigannSessionManager>.Instance);

        _login = new LoginBlock
        {
            Method = "form",
            Path = "/login.php",
            SubmitPath = "/takelogin.php",
            Form = "form",
            Inputs = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["username"] = "{{ .Config.username }}",
                ["password"] = "{{ .Config.password }}",
            },
            Error =
            [
                new LoginError { Selector = "div.error", Message = new FieldDefinition { Selector = "div.error" } },
            ],
            Test = new LoginTest { Path = "/", Selector = "a.logout" },
        };

        _cookieLogin = new LoginBlock
        {
            Method = "cookie",
            Test = new LoginTest { Path = "/", Selector = "a.logout" },
        };
    }

    [TearDown]
    public void TearDown()
    {
        _http.Dispose();
    }

    [Test]
    public async Task EnsureLoggedInAsync_PostsRenderedCredentialsAndCapturesCookie_WhenFormLogin()
    {
        // Arrange
        var postedBody = string.Empty;
        var testCookie = string.Empty;
        _http.When(HttpMethod.Get, "https://tracker.test/login.php").Respond("text/html", LoginPage);
        _http.When(HttpMethod.Post, "https://tracker.test/takelogin.php").Respond(async request =>
        {
            postedBody = await request.Content!.ReadAsStringAsync(default).ConfigureAwait(false);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") };
            response.Headers.TryAddWithoutValidation("Set-Cookie", "sid=SESSION; Path=/");
            return response;
        });
        _http.When(HttpMethod.Get, "https://tracker.test/").Respond(request =>
        {
            testCookie = request.Headers.TryGetValues("Cookie", out var values)
                ? string.Join("; ", values)
                : string.Empty;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(LogoutPage) };
        });

        // Act
        var outcome = await _sessions.EnsureLoggedInAsync(
            SessionKey,
            _login,
            BaseUri,
            Variables,
            cookieValue: null,
            "magpie-test",
            forceRefresh: false,
            default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome.Success, Is.True);
            Assert.That(postedBody, Does.Contain("username=myuser"));
            Assert.That(postedBody, Does.Contain("password=secret"));
            Assert.That(postedBody, Does.Contain("token=csrf1"));
            Assert.That(testCookie, Is.EqualTo("sid=SESSION"));
        }
    }

    [Test]
    public async Task EnsureLoggedInAsync_SkipsSecondLogin_WhenSessionIsAlreadyAuthenticated()
    {
        // Arrange
        var loginPosts = 0;
        _http.When(HttpMethod.Get, "https://tracker.test/login.php").Respond("text/html", LoginPage);
        _http.When(HttpMethod.Post, "https://tracker.test/takelogin.php").Respond(_ =>
        {
            loginPosts++;
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") };
            response.Headers.TryAddWithoutValidation("Set-Cookie", "sid=SESSION; Path=/");
            return response;
        });
        _http.When(HttpMethod.Get, "https://tracker.test/").Respond("text/html", LogoutPage);

        // Act
        await _sessions.EnsureLoggedInAsync(
            SessionKey,
            _login,
            BaseUri,
            Variables,
            cookieValue: null,
            "magpie-test",
            forceRefresh: false,
            default)
            .ConfigureAwait(false);
        await _sessions.EnsureLoggedInAsync(
            SessionKey,
            _login,
            BaseUri,
            Variables,
            cookieValue: null,
            "magpie-test",
            forceRefresh: false,
            default)
            .ConfigureAwait(false);

        // Assert
        Assert.That(loginPosts, Is.EqualTo(1));
    }

    [Test]
    public async Task EnsureLoggedInAsync_ReturnsAuthFailure_WhenErrorSelectorMatches()
    {
        // Arrange
        var errorMessage = _login.Error![0].Message!;
        _resultParser
            .Setup(parser => parser.ExtractField(
                It.IsAny<IElement?>(),
                errorMessage,
                "login.error.message"))
            .Returns("Invalid password");
        _http.When(HttpMethod.Get, "https://tracker.test/login.php").Respond("text/html", LoginPage);
        _http.When(HttpMethod.Post, "https://tracker.test/takelogin.php").Respond(
            "text/html",
            """<html><body><div class="error">Invalid password</div></body></html>""");
        _http.When(HttpMethod.Get, "https://tracker.test/").Respond("text/html", LoginPage);

        // Act
        var outcome = await _sessions.EnsureLoggedInAsync(
            SessionKey,
            _login,
            BaseUri,
            Variables,
            cookieValue: null,
            "magpie-test",
            forceRefresh: false,
            default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome.Success, Is.False);
            Assert.That(outcome.FailureKind, Is.EqualTo(ConfigurationFailureKind.Auth));
            Assert.That(outcome.Message, Is.EqualTo("Invalid password"));
        }

        _resultParser.Verify(
            parser => parser.ExtractField(It.IsAny<IElement?>(), errorMessage, "login.error.message"),
            Times.Once);
    }

    [Test]
    public async Task EnsureLoggedInAsync_ReturnsAuthFailure_WhenTestSelectorMisses()
    {
        // Arrange
        _http.When(HttpMethod.Get, "https://tracker.test/login.php").Respond("text/html", LoginPage);
        _http.When(HttpMethod.Post, "https://tracker.test/takelogin.php").Respond("text/html", "ok");
        _http.When(HttpMethod.Get, "https://tracker.test/").Respond("text/html", LoginPage);

        // Act
        var outcome = await _sessions.EnsureLoggedInAsync(
            SessionKey,
            _login,
            BaseUri,
            Variables,
            cookieValue: null,
            "magpie-test",
            forceRefresh: false,
            default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome.Success, Is.False);
            Assert.That(outcome.FailureKind, Is.EqualTo(ConfigurationFailureKind.Auth));
            Assert.That(outcome.Message, Does.Contain("selector did not match"));
        }
    }

    [Test]
    public async Task EnsureLoggedInAsync_CapturesCookieFromRedirectHop_WhenLoginPostReturns302()
    {
        // Arrange
        var welcomeRequested = false;
        var testCookie = string.Empty;
        _http.When(HttpMethod.Get, "https://tracker.test/login.php").Respond("text/html", LoginPage);
        _http.When(HttpMethod.Post, "https://tracker.test/takelogin.php").Respond(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Content = new StringContent(string.Empty),
            };
            response.Headers.TryAddWithoutValidation("Set-Cookie", "sid=SESSION; Path=/");
            response.Headers.TryAddWithoutValidation("Location", "/welcome");
            return response;
        });
        _http.When(HttpMethod.Get, "https://tracker.test/welcome").Respond(_ =>
        {
            welcomeRequested = true;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("welcome") };
        });
        _http.When(HttpMethod.Get, "https://tracker.test/").Respond(request =>
        {
            testCookie = request.Headers.TryGetValues("Cookie", out var values)
                ? string.Join("; ", values)
                : string.Empty;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(LogoutPage) };
        });

        // Act
        var outcome = await _sessions.EnsureLoggedInAsync(
            SessionKey,
            _login,
            BaseUri,
            Variables,
            cookieValue: null,
            "magpie-test",
            forceRefresh: false,
            default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome.Success, Is.True);
            Assert.That(welcomeRequested, Is.True);
            Assert.That(testCookie, Is.EqualTo("sid=SESSION"));
        }
    }

    [Test]
    public async Task EnsureLoggedInAsync_SendsPastedCookieOnTestRequest_WhenCookieLogin()
    {
        // Arrange
        var testCookie = string.Empty;
        _http.When(HttpMethod.Get, "https://tracker.test/").Respond(request =>
        {
            testCookie = request.Headers.TryGetValues("Cookie", out var values)
                ? string.Join("; ", values)
                : string.Empty;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(LogoutPage) };
        });

        // Act
        var outcome = await _sessions.EnsureLoggedInAsync(
            SessionKey,
            _cookieLogin,
            BaseUri,
            Variables,
            "sid=PASTED",
            "magpie-test",
            forceRefresh: false,
            default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome.Success, Is.True);
            Assert.That(testCookie, Is.EqualTo("sid=PASTED"));
        }
    }

    [Test]
    public async Task EnsureLoggedInAsync_ReportsAConfigurationFailure_WhenTheConfiguredCookieIsMalformed()
    {
        // Arrange
        _http.When(HttpMethod.Get, "https://tracker.test/")
            .Respond(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(LogoutPage) });

        // Act
        var outcome = await _sessions.EnsureLoggedInAsync(
            SessionKey,
            _cookieLogin,
            BaseUri,
            Variables,
            "sid=a,b",
            "magpie-test",
            forceRefresh: false,
            default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome.Success, Is.False);
            Assert.That(outcome.FailureKind, Is.EqualTo(ConfigurationFailureKind.Auth));
        }
    }

    [Test]
    public async Task EnsureLoggedInAsync_ReturnsAuthFailure_WhenCaptchaConfigured()
    {
        // Arrange
        _login.Captcha = new LoginCaptcha();

        // Act
        var outcome = await _sessions.EnsureLoggedInAsync(
            SessionKey,
            _login,
            BaseUri,
            Variables,
            cookieValue: null,
            "magpie-test",
            forceRefresh: false,
            default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome.Success, Is.False);
            Assert.That(outcome.FailureKind, Is.EqualTo(ConfigurationFailureKind.Auth));
            Assert.That(outcome.Message, Does.Contain("captcha").IgnoreCase);
        }
    }

    [Test]
    public async Task SendAsync_AttachesSessionCookie_WhenTheTargetIsOnTheBaseAuthority()
    {
        // Arrange
        var searchCookie = string.Empty;
        _http.When(HttpMethod.Get, "https://tracker.test/").Respond("text/html", LogoutPage);
        _http.When(HttpMethod.Get, "https://tracker.test/search").Respond(request =>
        {
            searchCookie = request.Headers.TryGetValues("Cookie", out var values)
                ? string.Join("; ", values)
                : string.Empty;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html></html>") };
        });
        await _sessions.EnsureLoggedInAsync(
            SessionKey,
            _cookieLogin,
            BaseUri,
            Variables,
            "sid=PASTED",
            "magpie-test",
            forceRefresh: false,
            default)
            .ConfigureAwait(false);

        // Act
        using var response = await _sessions.SendAsync(
            SessionKey,
            BaseUri,
            HttpMethod.Get,
            new Uri("https://tracker.test/search"),
            null,
            "magpie-test",
            captureCookies: true,
            default)
            .ConfigureAwait(false);

        // Assert
        Assert.That(searchCookie, Is.EqualTo("sid=PASTED"));
    }

    [Test]
    public async Task SendAsync_PostsFormBody_WhenFormBodyIsProvided()
    {
        // Arrange
        var postedBody = string.Empty;
        _http.When(HttpMethod.Post, "https://tracker.test/search").Respond(async request =>
        {
            postedBody = await request.Content!.ReadAsStringAsync(default).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html></html>") };
        });

        // Act
        using var response = await _sessions.SendAsync(
            SessionKey,
            BaseUri,
            HttpMethod.Post,
            new Uri("https://tracker.test/search"),
            "q=cool%20movie",
            "magpie-test",
            captureCookies: false,
            default)
            .ConfigureAwait(false);

        // Assert
        Assert.That(postedBody, Is.EqualTo("q=cool%20movie"));
    }

    [Test]
    public async Task SendAsync_OmitsSessionCookie_WhenTheTargetIsOffOrigin()
    {
        // Arrange
        var offOriginCookie = "unset";
        _http.When(HttpMethod.Get, "https://tracker.test/").Respond("text/html", LogoutPage);
        _http.When(HttpMethod.Get, "https://evil.test/steal").Respond(request =>
        {
            offOriginCookie = request.Headers.TryGetValues("Cookie", out var values)
                ? string.Join("; ", values)
                : string.Empty;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html></html>") };
        });
        await _sessions.EnsureLoggedInAsync(
            SessionKey,
            _cookieLogin,
            BaseUri,
            Variables,
            "sid=PASTED",
            "magpie-test",
            forceRefresh: false,
            default)
            .ConfigureAwait(false);

        // Act
        using var response = await _sessions.SendAsync(
            SessionKey,
            BaseUri,
            HttpMethod.Get,
            new Uri("https://evil.test/steal"),
            null,
            "magpie-test",
            captureCookies: true,
            default)
            .ConfigureAwait(false);

        // Assert
        Assert.That(offOriginCookie, Is.Empty);
    }

    [Test]
    public async Task InvalidateSession_DropsTheSessionCookie_WhenTheSessionIsAuthenticated()
    {
        // Arrange
        var searchCookie = "unset";
        _http.When(HttpMethod.Get, "https://tracker.test/").Respond("text/html", LogoutPage);
        _http.When(HttpMethod.Get, "https://tracker.test/search").Respond(request =>
        {
            searchCookie = request.Headers.TryGetValues("Cookie", out var values)
                ? string.Join("; ", values)
                : string.Empty;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html></html>") };
        });
        await _sessions.EnsureLoggedInAsync(
            SessionKey,
            _cookieLogin,
            BaseUri,
            Variables,
            "sid=PASTED",
            "magpie-test",
            forceRefresh: false,
            default)
            .ConfigureAwait(false);

        // Act
        _sessions.InvalidateSession(SessionKey);
        using var response = await _sessions.SendAsync(
            SessionKey,
            BaseUri,
            HttpMethod.Get,
            new Uri("https://tracker.test/search"),
            null,
            "magpie-test",
            captureCookies: true,
            default)
            .ConfigureAwait(false);

        // Assert
        Assert.That(searchCookie, Is.Empty);
    }

    [Test]
    public async Task SendAsync_RetriesWithTheSolvedCookieAndUserAgent_WhenTheTrackerChallengesTheRequest()
    {
        // Arrange
        var retryCookie = string.Empty;
        var retryUserAgent = string.Empty;
        var solverCommand = string.Empty;
        _http.When(HttpMethod.Post, "http://solver.test:8191/v1").Respond(async request =>
        {
            solverCommand = await request.Content!.ReadAsStringAsync(default).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SolvedResponse, Encoding.UTF8, "application/json"),
            };
        });
        _http.When(HttpMethod.Get, "https://tracker.test/search").Respond(request =>
        {
            var cookie = request.Headers.TryGetValues("Cookie", out var values)
                ? string.Join("; ", values)
                : string.Empty;
            if (!cookie.Contains("cf_clearance=TOKEN", StringComparison.Ordinal))
            {
                var challenge = new HttpResponseMessage(HttpStatusCode.Forbidden)
                {
                    Content = new StringContent(ChallengePage),
                };
                challenge.Headers.TryAddWithoutValidation("CF-RAY", "7a1b2c3d");
                return challenge;
            }

            retryCookie = cookie;
            retryUserAgent = request.Headers.UserAgent.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html></html>") };
        });

        // Act
        using var response = await _sessions.SendAsync(
            SessionKey,
            BaseUri,
            HttpMethod.Get,
            new Uri("https://tracker.test/search"),
            null,
            "magpie-test",
            captureCookies: false,
            default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(retryCookie, Is.EqualTo("cf_clearance=TOKEN"));
            Assert.That(retryUserAgent, Is.EqualTo("SolverAgent/1.0"));
            Assert.That(
                solverCommand,
                Is.EqualTo("""{"cmd":"request.get","url":"https://tracker.test/","maxTimeout":60000}"""));
        }
    }

    [Test]
    public async Task SendAsync_RepeatsThePostBody_WhenTheChallengedRequestCarriesAForm()
    {
        // Arrange
        var retryMethod = string.Empty;
        var retryBody = string.Empty;
        var retryContentType = string.Empty;
        _http.When(HttpMethod.Post, "http://solver.test:8191/v1").Respond("application/json", SolvedResponse);
        _http.When(HttpMethod.Post, "https://tracker.test/search").Respond(async request =>
        {
            var cookie = request.Headers.TryGetValues("Cookie", out var values)
                ? string.Join("; ", values)
                : string.Empty;
            if (!cookie.Contains("cf_clearance=TOKEN", StringComparison.Ordinal))
            {
                var challenge = new HttpResponseMessage(HttpStatusCode.Forbidden)
                {
                    Content = new StringContent(ChallengePage),
                };
                challenge.Headers.TryAddWithoutValidation("CF-RAY", "7a1b2c3d");
                return challenge;
            }

            retryMethod = request.Method.Method;
            retryBody = await request.Content!.ReadAsStringAsync(default).ConfigureAwait(false);
            retryContentType = request.Content.Headers.ContentType?.ToString() ?? string.Empty;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html></html>") };
        });

        // Act
        using var response = await _sessions.SendAsync(
            SessionKey,
            BaseUri,
            HttpMethod.Post,
            new Uri("https://tracker.test/search"),
            "q=cool&cat=1",
            "magpie-test",
            captureCookies: false,
            default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(retryMethod, Is.EqualTo("POST"));
            Assert.That(retryBody, Is.EqualTo("q=cool&cat=1"));
            Assert.That(retryContentType, Is.EqualTo("application/x-www-form-urlencoded; charset=utf-8"));
        }
    }

    [Test]
    public async Task SendAsync_KeepsTheLoginCookie_WhenTheSolverReturnsItsOwnSessionCookie()
    {
        // Arrange
        var retryCookie = string.Empty;
        _http.When(HttpMethod.Post, "http://solver.test:8191/v1").Respond(
            "application/json",
            """
            {"status":"ok","solution":{"url":"https://tracker.test/","status":200,"cookies":[
            {"name":"cf_clearance","value":"TOKEN"},{"name":"PHPSESSID","value":"SOLVER"}],
            "userAgent":"SolverAgent/1.0"}}
            """);
        _http.When(HttpMethod.Get, "https://tracker.test/").Respond("text/html", LogoutPage);
        _http.When(HttpMethod.Get, "https://tracker.test/search").Respond(request =>
        {
            var cookie = request.Headers.TryGetValues("Cookie", out var values)
                ? string.Join("; ", values)
                : string.Empty;
            if (!cookie.Contains("cf_clearance=TOKEN", StringComparison.Ordinal))
            {
                var challenge = new HttpResponseMessage(HttpStatusCode.Forbidden)
                {
                    Content = new StringContent(ChallengePage),
                };
                challenge.Headers.TryAddWithoutValidation("CF-RAY", "7a1b2c3d");
                return challenge;
            }

            retryCookie = cookie;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html></html>") };
        });
        await _sessions.EnsureLoggedInAsync(
            SessionKey,
            _cookieLogin,
            BaseUri,
            Variables,
            "PHPSESSID=LOGGEDIN",
            "magpie-test",
            forceRefresh: false,
            default)
            .ConfigureAwait(false);

        // Act
        using var response = await _sessions.SendAsync(
            SessionKey,
            BaseUri,
            HttpMethod.Get,
            new Uri("https://tracker.test/search"),
            null,
            "magpie-test",
            captureCookies: false,
            default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(retryCookie, Does.Contain("PHPSESSID=LOGGEDIN"));
            Assert.That(retryCookie, Does.Contain("cf_clearance=TOKEN"));
            Assert.That(retryCookie, Does.Not.Contain("PHPSESSID=SOLVER"));
        }
    }

    [Test]
    public async Task SendAsync_ReturnsTheChallenge_WhenTheSolverUrlHasNoScheme()
    {
        // Arrange
        _solverGroup.Settings.Clear();
        _solverGroup.Settings.Add(new Setting { Key = "flareSolverrUrl", Value = "localhost:8191" });
        _http.When(HttpMethod.Get, "https://tracker.test/search").Respond(_ =>
        {
            var challenge = new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent(ChallengePage),
            };
            challenge.Headers.TryAddWithoutValidation("CF-RAY", "7a1b2c3d");
            return challenge;
        });

        // Act
        using var response = await _sessions.SendAsync(
            SessionKey,
            BaseUri,
            HttpMethod.Get,
            new Uri("https://tracker.test/search"),
            null,
            "magpie-test",
            captureCookies: false,
            default)
            .ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task SendAsync_ReturnsTheChallenge_WhenNoSolverUrlIsConfigured()
    {
        // Arrange
        _solverGroup.Settings.Clear();
        var solverCalls = 0;
        _http.When(HttpMethod.Post, "http://solver.test:8191/v1").Respond(_ =>
        {
            solverCalls++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SolvedResponse, Encoding.UTF8, "application/json"),
            };
        });
        _http.When(HttpMethod.Get, "https://tracker.test/search").Respond(_ =>
        {
            var challenge = new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent(ChallengePage),
            };
            challenge.Headers.TryAddWithoutValidation("CF-RAY", "7a1b2c3d");
            return challenge;
        });

        // Act
        using var response = await _sessions.SendAsync(
            SessionKey,
            BaseUri,
            HttpMethod.Get,
            new Uri("https://tracker.test/search"),
            null,
            "magpie-test",
            captureCookies: false,
            default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(solverCalls, Is.Zero);
        }
    }

    [Test]
    public async Task SendAsync_ReturnsTheChallenge_WhenTheSolverReportsAnError()
    {
        // Arrange
        _http.When(HttpMethod.Post, "http://solver.test:8191/v1").Respond(
            "application/json",
            """
            {"status":"error","message":"Challenge not solved!","solution":{"url":"https://tracker.test/search",
            "status":403,"cookies":[{"name":"cf_clearance","value":"TOKEN"}],"userAgent":"SolverAgent/1.0"}}
            """);
        _http.When(HttpMethod.Get, "https://tracker.test/search").Respond(request =>
        {
            var cookie = request.Headers.TryGetValues("Cookie", out var values)
                ? string.Join("; ", values)
                : string.Empty;
            if (cookie.Contains("cf_clearance=TOKEN", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html></html>") };
            }

            var challenge = new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent(ChallengePage),
            };
            challenge.Headers.TryAddWithoutValidation("CF-RAY", "7a1b2c3d");
            return challenge;
        });

        // Act
        using var response = await _sessions.SendAsync(
            SessionKey,
            BaseUri,
            HttpMethod.Get,
            new Uri("https://tracker.test/search"),
            null,
            "magpie-test",
            captureCookies: false,
            default)
            .ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task SendAsync_ReturnsTheChallenge_WhenTheSolvedRetryIsStillChallenged()
    {
        // Arrange
        var solverCalls = 0;
        _http.When(HttpMethod.Post, "http://solver.test:8191/v1").Respond(_ =>
        {
            solverCalls++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SolvedResponse, Encoding.UTF8, "application/json"),
            };
        });
        _http.When(HttpMethod.Get, "https://tracker.test/search").Respond(_ =>
        {
            var challenge = new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent(ChallengePage),
            };
            challenge.Headers.TryAddWithoutValidation("CF-RAY", "7a1b2c3d");
            return challenge;
        });

        // Act
        using var response = await _sessions.SendAsync(
            SessionKey,
            BaseUri,
            HttpMethod.Get,
            new Uri("https://tracker.test/search"),
            null,
            "magpie-test",
            captureCookies: false,
            default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(solverCalls, Is.EqualTo(2));
        }
    }

    [Test]
    public async Task SendAsync_SolvesOnce_WhenTheSecondRequestReusesTheClearance()
    {
        // Arrange
        var solverCalls = 0;
        _http.When(HttpMethod.Post, "http://solver.test:8191/v1").Respond(_ =>
        {
            solverCalls++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SolvedResponse, Encoding.UTF8, "application/json"),
            };
        });
        _http.When(HttpMethod.Get, "https://tracker.test/search").Respond(request =>
        {
            var cookie = request.Headers.TryGetValues("Cookie", out var values)
                ? string.Join("; ", values)
                : string.Empty;
            if (cookie.Contains("cf_clearance=TOKEN", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html></html>") };
            }

            var challenge = new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent(ChallengePage),
            };
            challenge.Headers.TryAddWithoutValidation("CF-RAY", "7a1b2c3d");
            return challenge;
        });

        // Act
        using var first = await _sessions.SendAsync(
            SessionKey,
            BaseUri,
            HttpMethod.Get,
            new Uri("https://tracker.test/search"),
            null,
            "magpie-test",
            captureCookies: false,
            default)
            .ConfigureAwait(false);
        using var second = await _sessions.SendAsync(
            SessionKey,
            BaseUri,
            HttpMethod.Get,
            new Uri("https://tracker.test/search"),
            null,
            "magpie-test",
            captureCookies: false,
            default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(solverCalls, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task SendAsync_SolvesAgain_WhenTheTrackerChallengesTheSecondRequest()
    {
        // Arrange
        var solverCalls = 0;
        _http.When(HttpMethod.Post, "http://solver.test:8191/v1").Respond(_ =>
        {
            solverCalls++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SolvedResponse, Encoding.UTF8, "application/json"),
            };
        });
        _http.When(HttpMethod.Post, "https://tracker.test/search").Respond(_ =>
        {
            var challenge = new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent(ChallengePage),
            };
            challenge.Headers.TryAddWithoutValidation("CF-RAY", "7a1b2c3d");
            return challenge;
        });

        // Act
        using var first = await _sessions.SendAsync(
            SessionKey,
            BaseUri,
            HttpMethod.Post,
            new Uri("https://tracker.test/search"),
            "q=cool",
            "magpie-test",
            captureCookies: false,
            default)
            .ConfigureAwait(false);
        using var second = await _sessions.SendAsync(
            SessionKey,
            BaseUri,
            HttpMethod.Post,
            new Uri("https://tracker.test/search"),
            "q=cool",
            "magpie-test",
            captureCookies: false,
            default)
            .ConfigureAwait(false);

        // Assert
        Assert.That(solverCalls, Is.EqualTo(2));
    }

    [Test]
    public async Task SendAsync_SolvesAgainAndKeepsTheCookiePath_WhenTheTrackerRotatesTheClearance()
    {
        // Arrange
        var solverCalls = 0;
        var accepted = "cf_clearance=FIRST";
        _http.When(HttpMethod.Post, "http://solver.test:8191/v1").Respond(_ =>
        {
            solverCalls++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    solverCalls == 1 ? SolvedFirstClearanceResponse : SolvedSecondClearanceResponse,
                    Encoding.UTF8,
                    "application/json"),
            };
        });
        _http.When(HttpMethod.Get, "https://tracker.test/search").Respond(request =>
        {
            var cookie = request.Headers.TryGetValues("Cookie", out var values)
                ? string.Join("; ", values)
                : string.Empty;
            if (cookie.Contains(accepted, StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html></html>") };
            }

            var challenge = new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent(ChallengePage),
            };
            challenge.Headers.TryAddWithoutValidation("CF-RAY", "7a1b2c3d");
            return challenge;
        });

        // Act
        using var first = await _sessions.SendAsync(
            SessionKey,
            BaseUri,
            HttpMethod.Get,
            new Uri("https://tracker.test/search"),
            null,
            "magpie-test",
            captureCookies: false,
            default)
            .ConfigureAwait(false);
        accepted = "cf_clearance=SECOND";
        using var rotated = await _sessions.SendAsync(
            SessionKey,
            BaseUri,
            HttpMethod.Get,
            new Uri("https://tracker.test/search"),
            null,
            "magpie-test",
            captureCookies: false,
            default)
            .ConfigureAwait(false);
        using var later = await _sessions.SendAsync(
            SessionKey,
            BaseUri,
            HttpMethod.Get,
            new Uri("https://tracker.test/search"),
            null,
            "magpie-test",
            captureCookies: false,
            default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(rotated.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(later.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(solverCalls, Is.EqualTo(2));
        }
    }

    [Test]
    public async Task SendAsync_SolvesOneAtATime_WhenTwoTrackersAreChallengedAtOnce()
    {
        // Arrange
        var held = new TaskCompletionSource();
        var arrivals = new SemaphoreSlim(0);
        _http.When(HttpMethod.Post, "http://solver.test:8191/v1").Respond(async _ =>
        {
            arrivals.Release();
            await held.Task.ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SolvedResponse, Encoding.UTF8, "application/json"),
            };
        });
        _http.When(HttpMethod.Get, "https://tracker.test/search").Respond(request =>
        {
            var cookie = request.Headers.TryGetValues("Cookie", out var values)
                ? string.Join("; ", values)
                : string.Empty;
            if (cookie.Contains("cf_clearance=TOKEN", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html></html>") };
            }

            var challenge = new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent(ChallengePage),
            };
            challenge.Headers.TryAddWithoutValidation("CF-RAY", "7a1b2c3d");
            return challenge;
        });
        _http.When(HttpMethod.Get, "https://other.test/search").Respond(request =>
        {
            var cookie = request.Headers.TryGetValues("Cookie", out var values)
                ? string.Join("; ", values)
                : string.Empty;
            if (cookie.Contains("cf_clearance=TOKEN", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html></html>") };
            }

            var challenge = new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent(ChallengePage),
            };
            challenge.Headers.TryAddWithoutValidation("CF-RAY", "7a1b2c3d");
            return challenge;
        });

        // Act
        var first = _sessions.SendAsync(
            SessionKey,
            BaseUri,
            HttpMethod.Get,
            new Uri("https://tracker.test/search"),
            null,
            "magpie-test",
            captureCookies: false,
            default);
        var second = _sessions.SendAsync(
            "priv@https://other.test",
            new Uri("https://other.test/"),
            HttpMethod.Get,
            new Uri("https://other.test/search"),
            null,
            "magpie-test",
            captureCookies: false,
            default);
        var oneSolveStarted = await arrivals.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        var secondSolveStarted = await arrivals.WaitAsync(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);
        held.SetResult();
        using var firstResponse = await first.ConfigureAwait(false);
        using var secondResponse = await second.ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(oneSolveStarted, Is.True);
            Assert.That(secondSolveStarted, Is.False);
            Assert.That(firstResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(secondResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }
    }

    [Test]
    public async Task SendAsync_ReturnsTheChallenge_WhenTheSolverQueueStaysBusy()
    {
        // Arrange
        var held = new TaskCompletionSource();
        var arrivals = new SemaphoreSlim(0);
        var solverCalls = 0;
        var impatient = new CardigannSessionManager(
            _clientFactory,
            _resultParser.Object,
            new FlareSolverrClient(
                _clientFactory,
                _settingsService,
                _memoFactory,
                NullLogger<FlareSolverrClient>.Instance,
                new SolverQueue { Wait = TimeSpan.FromMilliseconds(100) }),
            new CardigannSessionState(),
            TimeProvider.System,
            NullLogger<CardigannSessionManager>.Instance);
        _http.When(HttpMethod.Post, "http://solver.test:8191/v1").Respond(async _ =>
        {
            Interlocked.Increment(ref solverCalls);
            arrivals.Release();
            await held.Task.ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SolvedResponse, Encoding.UTF8, "application/json"),
            };
        });
        _http.When(HttpMethod.Get, "https://tracker.test/search").Respond(request =>
        {
            var cookie = request.Headers.TryGetValues("Cookie", out var values)
                ? string.Join("; ", values)
                : string.Empty;
            if (cookie.Contains("cf_clearance=TOKEN", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html></html>") };
            }

            var challenge = new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent(ChallengePage),
            };
            challenge.Headers.TryAddWithoutValidation("CF-RAY", "7a1b2c3d");
            return challenge;
        });
        _http.When(HttpMethod.Get, "https://other.test/search").Respond(request =>
        {
            var cookie = request.Headers.TryGetValues("Cookie", out var values)
                ? string.Join("; ", values)
                : string.Empty;
            if (cookie.Contains("cf_clearance=TOKEN", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html></html>") };
            }

            var challenge = new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent(ChallengePage),
            };
            challenge.Headers.TryAddWithoutValidation("CF-RAY", "7a1b2c3d");
            return challenge;
        });

        // Act
        var blocking = impatient.SendAsync(
            SessionKey,
            BaseUri,
            HttpMethod.Get,
            new Uri("https://tracker.test/search"),
            null,
            "magpie-test",
            captureCookies: false,
            default);
        await arrivals.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        using var queued = await impatient.SendAsync(
            "priv@https://other.test",
            new Uri("https://other.test/"),
            HttpMethod.Get,
            new Uri("https://other.test/search"),
            null,
            "magpie-test",
            captureCookies: false,
            default)
            .ConfigureAwait(false);
        held.SetResult();
        using var blocked = await blocking.ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(queued.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(queued.DetectCloudflareChallenge(), Is.EqualTo("HTTP 403 with CF headers"));
            Assert.That(solverCalls, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task SendAsync_FetchesAFreshPage_WhenTheSecondSendRepeatsASolvedUrl()
    {
        // Arrange
        var solverCalls = 0;
        _http.When(HttpMethod.Post, "http://solver.test:8191/v1").Respond(_ =>
        {
            solverCalls++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SolvedPageWithoutCookiesResponse, Encoding.UTF8, "application/json"),
            };
        });
        _http.When(HttpMethod.Get, "https://tracker.test/search").Respond(_ =>
        {
            var challenge = new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent(ChallengePage),
            };
            challenge.Headers.TryAddWithoutValidation("CF-RAY", "7a1b2c3d");
            return challenge;
        });

        // Act
        using var first = await _sessions.SendAsync(
            SessionKey,
            BaseUri,
            HttpMethod.Get,
            new Uri("https://tracker.test/search"),
            null,
            "magpie-test",
            captureCookies: false,
            default)
            .ConfigureAwait(false);
        using var second = await _sessions.SendAsync(
            SessionKey,
            BaseUri,
            HttpMethod.Get,
            new Uri("https://tracker.test/search"),
            null,
            "magpie-test",
            captureCookies: false,
            default)
            .ConfigureAwait(false);
        var body = await second.Content.ReadAsStringAsync(default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(solverCalls, Is.EqualTo(3));
            Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Is.EqualTo("<html><body><tr class=\"row\">Cool Release</tr></body></html>"));
        }
    }

    [Test]
    public async Task SendAsync_ReturnsTheSolvedPage_WhenTheRetryIsStillChallenged()
    {
        // Arrange
        _http.When(HttpMethod.Post, "http://solver.test:8191/v1").Respond("application/json", SolvedPageResponse);
        _http.When(HttpMethod.Get, "https://tracker.test/search").Respond(_ =>
        {
            var challenge = new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent(ChallengePage),
            };
            challenge.Headers.TryAddWithoutValidation("CF-RAY", "7a1b2c3d");
            return challenge;
        });

        // Act
        using var response = await _sessions.SendAsync(
            SessionKey,
            BaseUri,
            HttpMethod.Get,
            new Uri("https://tracker.test/search"),
            null,
            "magpie-test",
            captureCookies: false,
            default)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Is.EqualTo("<html><body><tr class=\"row\">Cool Release</tr></body></html>"));
            Assert.That(response.Content.Headers.ContentType?.ToString(), Is.EqualTo("text/html; charset=utf-8"));
        }
    }

    [Test]
    public async Task SendAsync_UnwrapsTheSolvedJson_WhenTheSolverWrapsItInAnHtmlEnvelope()
    {
        // Arrange
        _http.When(HttpMethod.Post, "http://solver.test:8191/v1").Respond("application/json", SolvedJsonResponse);
        _http.When(HttpMethod.Get, "https://tracker.test/search").Respond(_ =>
        {
            var challenge = new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent(ChallengePage),
            };
            challenge.Headers.TryAddWithoutValidation("CF-RAY", "7a1b2c3d");
            return challenge;
        });

        // Act
        using var response = await _sessions.SendAsync(
            SessionKey,
            BaseUri,
            HttpMethod.Get,
            new Uri("https://tracker.test/search"),
            null,
            "magpie-test",
            captureCookies: false,
            default)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(body, Is.EqualTo("""{"results":[{"title":"Cool"}]}"""));
            Assert.That(
                response.Content.Headers.ContentType?.ToString(),
                Is.EqualTo("application/json; charset=utf-8"));
        }
    }

    [Test]
    public async Task SendAsync_ReturnsTheSolvedPage_WhenTheSolutionCarriesNoCloudflareCookie()
    {
        // Arrange
        var trackerCalls = 0;
        _http.When(HttpMethod.Post, "http://solver.test:8191/v1").Respond(
            "application/json",
            SolvedPageWithoutCookiesResponse);
        _http.When(HttpMethod.Get, "https://tracker.test/search").Respond(_ =>
        {
            trackerCalls++;
            var challenge = new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent(ChallengePage),
            };
            challenge.Headers.TryAddWithoutValidation("CF-RAY", "7a1b2c3d");
            return challenge;
        });

        // Act
        using var response = await _sessions.SendAsync(
            SessionKey,
            BaseUri,
            HttpMethod.Get,
            new Uri("https://tracker.test/search"),
            null,
            "magpie-test",
            captureCookies: false,
            default)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Is.EqualTo("<html><body><tr class=\"row\">Cool Release</tr></body></html>"));
            Assert.That(trackerCalls, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task SendAsync_ReturnsTheChallenge_WhenAStillChallengedRequestIsAPost()
    {
        // Arrange
        _http.When(HttpMethod.Post, "http://solver.test:8191/v1").Respond("application/json", SolvedPageResponse);
        _http.When(HttpMethod.Post, "https://tracker.test/search").Respond(_ =>
        {
            var challenge = new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent(ChallengePage),
            };
            challenge.Headers.TryAddWithoutValidation("CF-RAY", "7a1b2c3d");
            return challenge;
        });

        // Act
        using var response = await _sessions.SendAsync(
            SessionKey,
            BaseUri,
            HttpMethod.Post,
            new Uri("https://tracker.test/search"),
            "q=cool",
            "magpie-test",
            captureCookies: false,
            default)
            .ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task SendAsync_SkipsTheCookieRetry_WhenTheHostAlreadyRefusedTheClearance()
    {
        // Arrange
        var trackerCalls = 0;
        _http.When(HttpMethod.Post, "http://solver.test:8191/v1").Respond("application/json", SolvedPageResponse);
        _http.When(HttpMethod.Get, "https://tracker.test/search").Respond(_ =>
        {
            trackerCalls++;
            var challenge = new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent(ChallengePage),
            };
            challenge.Headers.TryAddWithoutValidation("CF-RAY", "7a1b2c3d");
            return challenge;
        });

        // Act
        using var first = await _sessions.SendAsync(
            SessionKey,
            BaseUri,
            HttpMethod.Get,
            new Uri("https://tracker.test/search"),
            null,
            "magpie-test",
            captureCookies: false,
            default)
            .ConfigureAwait(false);
        using var second = await _sessions.SendAsync(
            SessionKey,
            BaseUri,
            HttpMethod.Get,
            new Uri("https://tracker.test/search"),
            null,
            "magpie-test",
            captureCookies: false,
            default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(trackerCalls, Is.EqualTo(3));
        }
    }

    [Test]
    public async Task SendAsync_ReadsTheTrackerDirectly_WhenTheSessionIsInvalidatedAfterARefusedClearance()
    {
        // Arrange
        var accepted = "cf_clearance=NONE";
        _http.When(HttpMethod.Post, "http://solver.test:8191/v1").Respond("application/json", SolvedPageResponse);
        _http.When(HttpMethod.Get, "https://tracker.test/search").Respond(request =>
        {
            var cookie = request.Headers.TryGetValues("Cookie", out var values)
                ? string.Join("; ", values)
                : string.Empty;
            if (cookie.Contains(accepted, StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("<html><body>direct</body></html>"),
                };
            }

            var challenge = new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent(ChallengePage),
            };
            challenge.Headers.TryAddWithoutValidation("CF-RAY", "7a1b2c3d");
            return challenge;
        });

        // Act
        using var refused = await _sessions.SendAsync(
            SessionKey,
            BaseUri,
            HttpMethod.Get,
            new Uri("https://tracker.test/search"),
            null,
            "magpie-test",
            captureCookies: false,
            default)
            .ConfigureAwait(false);
        var solvedBody = await refused.Content.ReadAsStringAsync(default).ConfigureAwait(false);
        _sessions.InvalidateSession(SessionKey);
        accepted = "cf_clearance=TOKEN";
        using var recovered = await _sessions.SendAsync(
            SessionKey,
            BaseUri,
            HttpMethod.Get,
            new Uri("https://tracker.test/search"),
            null,
            "magpie-test",
            captureCookies: false,
            default)
            .ConfigureAwait(false);
        var recoveredBody = await recovered.Content.ReadAsStringAsync(default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(solvedBody, Is.EqualTo("<html><body><tr class=\"row\">Cool Release</tr></body></html>"));
            Assert.That(recoveredBody, Is.EqualTo("<html><body>direct</body></html>"));
        }
    }

    [Test]
    public async Task SendAsync_ReadsTheTrackerDirectly_WhenTheRefusedClearanceExpires()
    {
        // Arrange
        var solverCalls = 0;
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var sessions = new CardigannSessionManager(
            _clientFactory,
            _resultParser.Object,
            new FlareSolverrClient(
                _clientFactory,
                _settingsService,
                _memoFactory,
                NullLogger<FlareSolverrClient>.Instance,
                new SolverQueue()),
            new CardigannSessionState(),
            clock,
            NullLogger<CardigannSessionManager>.Instance);
        _http.When(HttpMethod.Post, "http://solver.test:8191/v1").Respond(_ =>
        {
            solverCalls++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    solverCalls == 1 ? SolvedFirstClearanceResponse : SolvedSecondClearanceResponse,
                    Encoding.UTF8,
                    "application/json"),
            };
        });
        _http.When(HttpMethod.Get, "https://tracker.test/search").Respond(request =>
        {
            var cookie = request.Headers.TryGetValues("Cookie", out var values)
                ? string.Join("; ", values)
                : string.Empty;
            if (cookie.Contains("cf_clearance=SECOND", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html></html>") };
            }

            var challenge = new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent(ChallengePage),
            };
            challenge.Headers.TryAddWithoutValidation("CF-RAY", "7a1b2c3d");
            return challenge;
        });

        // Act
        using var refused = await sessions.SendAsync(
            SessionKey,
            BaseUri,
            HttpMethod.Get,
            new Uri("https://tracker.test/search"),
            null,
            "magpie-test",
            captureCookies: false,
            default)
            .ConfigureAwait(false);
        clock.Advance(TimeSpan.FromMinutes(11));
        using var recovered = await sessions.SendAsync(
            SessionKey,
            BaseUri,
            HttpMethod.Get,
            new Uri("https://tracker.test/search"),
            null,
            "magpie-test",
            captureCookies: false,
            default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(recovered.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }
    }

    [Test]
    public async Task SolveAsync_ReturnsNull_WhenNoSolverEndpointIsConfigured()
    {
        // Arrange
        var solver = new FlareSolverrClient(
            _clientFactory, Mock.Of<ISettingsService>(), _memoFactory, NullLogger<FlareSolverrClient>.Instance,
            new SolverQueue());

        // Act
        var result = await solver.SolveAsync(new Uri("https://tracker.test/"), reuse: true, default)
            .ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task SolveAsync_SolvesTheUrlAgain_WhenAnotherUrlEvictedItFromTheMemo()
    {
        // Arrange
        var solverCalls = 0;
        _http.When(HttpMethod.Post, "http://solver.test:8191/v1").Respond(_ =>
        {
            solverCalls++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SolvedResponse, Encoding.UTF8, "application/json"),
            };
        });
        var entry = new Dictionary<string, object?>(StringComparer.Ordinal);
        var memo = new Mock<ICache>();
        memo.Setup(value => value.Set(It.IsAny<string>(), It.IsAny<It.IsAnyType>()))
            .Callback(new InvocationAction(invocation =>
            {
                entry.Clear();
                entry[(string)invocation.Arguments[0]] = invocation.Arguments[1];
            }));
        memo.Setup(value => value.TryGet(It.IsAny<string>(), out It.Ref<It.IsAnyType?>.IsAny))
            .Returns(new InvocationFunc(invocation =>
            {
                var found = entry.TryGetValue((string)invocation.Arguments[0], out var value);
                ((object?[])invocation.Arguments)[1] = value;
                return found;
            }));
        memo.Setup(value => value.Remove(It.IsAny<string>())).Returns<string>(entry.Remove);
        var solver = new FlareSolverrClient(
            _clientFactory,
            _settingsService,
            Mock.Of<ICacheFactory>(factory =>
                factory.GetOrCreate(It.IsAny<string>(), It.IsAny<Func<CachePolicy>>()) == memo.Object),
            NullLogger<FlareSolverrClient>.Instance,
            new SolverQueue());

        // Act
        await solver.SolveAsync(new Uri("https://tracker.test/one"), reuse: true, default).ConfigureAwait(false);
        await solver.SolveAsync(new Uri("https://tracker.test/two"), reuse: true, default).ConfigureAwait(false);
        await solver.SolveAsync(new Uri("https://tracker.test/one"), reuse: true, default).ConfigureAwait(false);

        // Assert
        Assert.That(solverCalls, Is.EqualTo(3));
    }

    [Test]
    public void SolveAsync_Throws_WhenSettingsReadFails()
    {
        // Arrange
        var settings = new Mock<ISettingsService>();
        settings.Setup(service => service.GetExtensionGroupAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Settings unavailable."));
        var solver = new FlareSolverrClient(
            _clientFactory, settings.Object, _memoFactory, NullLogger<FlareSolverrClient>.Instance,
            new SolverQueue());

        // Act
        var action = async () => await solver.SolveAsync(new Uri("https://tracker.test/"), reuse: true, default)
            .ConfigureAwait(false);

        // Assert
        Assert.That(action, Throws.InvalidOperationException);
    }

    [TestCase(HttpStatusCode.Forbidden, "<html><body>denied</body></html>", true)]
    [TestCase(HttpStatusCode.OK, """<html><body><input type="password"/></body></html>""", true)]
    [TestCase(HttpStatusCode.OK, """<html><body><a class="logout">Logout</a></body></html>""", false)]
    public void IsLoggedOut_ReportsWhetherTheSessionEnded_WhenAResponseIsInspected(
        HttpStatusCode statusCode,
        string body,
        bool expected)
    {
        // Arrange

        // Act
        var result = _sessions.IsLoggedOut(_login, statusCode, body);

        // Assert
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase("sid=deleted; Max-Age=0; Path=/", "/search", "")]
    [TestCase("sid=deleted; Expires=Thu, 01 Jan 1970 00:00:00 GMT; Path=/", "/search", "")]
    [TestCase("scoped=value; Path=/private", "/search", "sid=live")]
    [TestCase("scoped=value; Path=/private", "/private/search", "scoped=value; sid=live")]
    [TestCase("scoped=value; Domain=other.test; Path=/", "/search", "sid=live")]
    public async Task SendAsync_RespectsCookieAttributes_WhenTrackerSetsCookies(
        string cookie, string path, string expected)
    {
        // Arrange
        var handler = new Mock<HttpMessageHandler>();
        using var first = new HttpResponseMessage(HttpStatusCode.OK);
        first.Headers.TryAddWithoutValidation("Set-Cookie", "sid=live; Path=/");
        using var second = new HttpResponseMessage(HttpStatusCode.OK);
        second.Headers.TryAddWithoutValidation("Set-Cookie", cookie);
        string? sentCookie = null;
        handler.Protected().SetupSequence<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(first).ReturnsAsync(second);
        using var http = new HttpClient(handler.Object);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(client => client.CreateClient(It.IsAny<string>())).Returns(http);
        var sessions = new CardigannSessionManager(factory.Object, _resultParser.Object,
            Mock.Of<ICloudflareSolver>(), new CardigannSessionState(), TimeProvider.System,
            NullLogger<CardigannSessionManager>.Instance);
        using var initial = await sessions.SendAsync(SessionKey, BaseUri, HttpMethod.Get, BaseUri,
            null, "test", true, default).ConfigureAwait(false);
        using var update = await sessions.SendAsync(SessionKey, BaseUri, HttpMethod.Get, BaseUri,
            null, "test", true, default).ConfigureAwait(false);
        handler.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) =>
                sentCookie = request.Headers.TryGetValues("Cookie", out var cookies) ? string.Join("; ", cookies) : "")
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK));

        // Act
        using var result = await sessions.SendAsync(SessionKey, BaseUri, HttpMethod.Get, new Uri(BaseUri, path),
            null, "test", false, default).ConfigureAwait(false);

        // Assert
        Assert.That(sentCookie, Is.EqualTo(expected));
    }

    [TestCase("https://other.test/", false)]
    [TestCase("https://tracker.test/redirected", true)]
    public async Task EnsureLoggedInAsync_ScopesDefinitionHeaders_WhenLoginRedirects(string target, bool expected)
    {
        // Arrange
        var handler = new Mock<HttpMessageHandler>();
        var requests = new List<bool>();
        handler.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) =>
                requests.Add(request.Headers.Contains("Authorization")))
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
                request.RequestUri!.AbsolutePath == "/login"
                    ? new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri(target) } }
                    : new HttpResponseMessage(HttpStatusCode.OK));
        using var http = new HttpClient(handler.Object);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(client => client.CreateClient(It.IsAny<string>())).Returns(http);
        var sessions = new CardigannSessionManager(factory.Object, _resultParser.Object,
            Mock.Of<ICloudflareSolver>(), new CardigannSessionState(), TimeProvider.System,
            NullLogger<CardigannSessionManager>.Instance);
        var login = new LoginBlock
        {
            Method = "get",
            Path = "/login",
            Headers = new Dictionary<string, List<string>> { ["Authorization"] = ["secret"] },
        };

        // Act
        await sessions.EnsureLoggedInAsync(SessionKey, login, BaseUri, Variables, null, "test", false, default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(requests, Has.Count.EqualTo(2));
            Assert.That(requests[0], Is.True);
            Assert.That(requests[1], Is.EqualTo(expected));
        }
    }

    [Test]
    public async Task EnsureLoggedInAsync_SharesOnlyCardigannState_WhenScopesLoginConcurrently()
    {
        // Arrange
        var handler = new Mock<HttpMessageHandler>();
        var requests = 0;
        var releaseLogin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((_, _) => requests++)
            .Returns(async () =>
            {
                await releaseLogin.Task.ConfigureAwait(false);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(LogoutPage),
                    Headers = { { "Set-Cookie", "sid=live; Path=/" } },
                };
            });
        using var http = new HttpClient(handler.Object);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(client => client.CreateClient(It.IsAny<string>())).Returns(http);
        using var foreignGate = new SemaphoreSlim(0, 1);
        var foreignGates = new ConcurrentDictionary<string, SemaphoreSlim>();
        foreignGates[SessionKey] = foreignGate;
        var foreignSessions = new ConcurrentDictionary<string, TrackerSession>();
        foreignSessions[SessionKey] = new TrackerSession { Authenticated = true };
        var services = new ServiceCollection();
        new CardigannExtension().RegisterServices(services);
        services.AddSingleton(factory.Object);
        services.AddSingleton(_resultParser.Object);
        services.AddSingleton(Mock.Of<ICloudflareSolver>());
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ILogger<CardigannSessionManager>>(NullLogger<CardigannSessionManager>.Instance);
        services.AddSingleton(foreignGates);
        services.AddSingleton(foreignSessions);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();
        var first = firstScope.ServiceProvider.GetRequiredService<ICardigannSessionManager>();
        var second = secondScope.ServiceProvider.GetRequiredService<ICardigannSessionManager>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var login = new LoginBlock { Method = "get", Path = "/login" };

        // Act
        var initialLogin = first
            .EnsureLoggedInAsync(SessionKey, login, BaseUri, Variables, null, "test", false, timeout.Token);
        var concurrentLogin = second
            .EnsureLoggedInAsync(SessionKey, login, BaseUri, Variables, null, "test", false, timeout.Token);
        releaseLogin.SetResult();
        var initial = await initialLogin.ConfigureAwait(false);
        var concurrent = await concurrentLogin.ConfigureAwait(false);
        firstScope.Dispose();
        var repeat = await second
            .EnsureLoggedInAsync(SessionKey, login, BaseUri, Variables, null, "test", false, timeout.Token)
            .ConfigureAwait(false);
        string? sentCookie = null;
        handler.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) =>
                sentCookie = request.Headers.TryGetValues("Cookie", out var cookies)
                    ? string.Join("; ", cookies)
                    : string.Empty)
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK));
        using var response = await second
            .SendAsync(SessionKey, BaseUri, HttpMethod.Get, BaseUri, null, "test", false, default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(initial.Success, Is.True);
            Assert.That(concurrent.Success, Is.True);
            Assert.That(repeat.Success, Is.True);
            Assert.That(requests, Is.EqualTo(1));
            Assert.That(sentCookie, Is.EqualTo("sid=live"));
            Assert.That(foreignGates, Has.Count.EqualTo(1));
            Assert.That(foreignGate.CurrentCount, Is.Zero);
            Assert.That(foreignSessions[SessionKey].Cookies.Count, Is.Zero);
        }
    }

    [Test]
    public async Task SolveAsync_PreservesClearanceMemo_WhenFetchingPagesWithoutReuse()
    {
        // Arrange
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SolvedPageResponse),
            });
        using var http = new HttpClient(handler.Object);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(client => client.CreateClient(It.IsAny<string>())).Returns(http);
        var solver = new FlareSolverrClient(factory.Object, _settingsService, _memoFactory,
            NullLogger<FlareSolverrClient>.Instance, new SolverQueue { Wait = TimeSpan.FromSeconds(1) });
        await solver.SolveAsync(BaseUri, reuse: true, default).ConfigureAwait(false);

        // Act
        await solver.SolveAsync(new Uri("https://tracker.test/page?passkey=secret"), reuse: false, default)
            .ConfigureAwait(false);
        var clearance = await solver.SolveAsync(BaseUri, reuse: true, default).ConfigureAwait(false);

        // Assert
        Assert.That(clearance!.Reused, Is.True);
        handler.Protected().Verify("SendAsync", Times.Exactly(2),
            ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }

    [Test]
    public async Task SolveAsync_HashesMemoKey_WhenScopeContainsCredentials()
    {
        // Arrange
        var memo = new Mock<ICache>();
        string? key = null;
        memo.Setup(cache => cache.Set(It.IsAny<string>(), It.IsAny<CloudflareClearance>()))
            .Callback<string, CloudflareClearance>((savedKey, _) => key = savedKey);
        var solver = new FlareSolverrClient(
            _clientFactory,
            _settingsService,
            Mock.Of<ICacheFactory>(factory =>
                factory.GetOrCreate(It.IsAny<string>(), It.IsAny<Func<CachePolicy>>()) == memo.Object),
            NullLogger<FlareSolverrClient>.Instance,
            new SolverQueue { Wait = TimeSpan.FromSeconds(1) });
        _http.When(HttpMethod.Post, "http://solver.test:8191/v1").Respond("application/json", SolvedResponse);

        // Act
        await solver.SolveAsync(new Uri("https://tracker.test/?passkey=secret-value"), reuse: true, default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(key, Has.Length.EqualTo(64));
            Assert.That(key, Does.Not.Contain("secret-value"));
            Assert.That(key, Does.Not.Contain("tracker.test"));
        }
    }
}
