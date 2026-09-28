using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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
using Magpie.Sdk.Http;
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

    private ServiceProvider _cooldownServices = null!;
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
        _cooldownServices = new ServiceCollection().AddHttpClient("test-cooldown")
            .AddUpstreamCooldown().Services.BuildServiceProvider();
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
                NullLogger<FlareSolverrClient>.Instance, _cooldownServices.GetRequiredService<IUpstreamCooldown>(),
                new SolverQueue(), TimeProvider.System),
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
        _cooldownServices.Dispose();
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
                NullLogger<FlareSolverrClient>.Instance, _cooldownServices.GetRequiredService<IUpstreamCooldown>(),
                new SolverQueue(), TimeProvider.System),
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
            _cooldownServices.GetRequiredService<IUpstreamCooldown>(),
            new SolverQueue(), TimeProvider.System);

        // Act
        var result = await solver.SolveAsync(new Uri("https://tracker.test/"), reuse: true, default)
            .ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Null);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SolveAsync_RejectsBeforeSend_WhenUpstreamThrottlesWhileWaitingForSolver(bool interactive)
    {
        // Arrange
        var cooldown = _cooldownServices.GetRequiredService<IUpstreamCooldown>();
        var request = _http.When(HttpMethod.Post, "http://solver.test:8191/v1")
            .Respond("application/json", SolvedResponse);
        var queue = new SolverQueue();
        using var scope = new SolverRequestScope(interactive
            ? SolverRequestKind.Interactive : SolverRequestKind.Background);
        await queue.EnterAsync(default).ConfigureAwait(false);
        var solver = new FlareSolverrClient(_clientFactory, _settingsService, _memoFactory,
            NullLogger<FlareSolverrClient>.Instance, cooldown, queue, TimeProvider.System);
        var waiting = solver.SolveAsync(BaseUri, reuse: false, default, "account-a");
        using var upstream = new HttpRequestMessage(HttpMethod.Get, BaseUri);
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.TryAddWithoutValidation("Retry-After", "120");

        // Act
        cooldown.RecordThrottle(upstream, "account-a", response);
        queue.Leave();
        var rejected = Assert.ThrowsAsync<UpstreamThrottledException>(async () =>
            await waiting.ConfigureAwait(false));
        var healthy = await solver.SolveAsync(BaseUri, reuse: false, default, "account-b").ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(rejected!.RequestSent, Is.False);
            Assert.That(healthy, Is.Not.Null);
            Assert.That(_http.GetMatchCount(request), Is.EqualTo(1));
        }
    }

    [Test]
    public void SendAsync_SharesSolverThrottle_WhenAuthenticatedSessionRetries()
    {
        // Arrange
        _http.When(HttpMethod.Get, "https://tracker.test/*")
            .Respond(_ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.Forbidden)
                {
                    Content = new StringContent(ChallengePage)
                };
                response.Headers.TryAddWithoutValidation("CF-RAY", "test");
                return response;
            });
        var solverRequest = _http.When(HttpMethod.Post, "http://solver.test:8191/v1")
            .Respond("application/json",
                """{"status":"ok","solution":{"status":429,"headers":{"Retry-After":"120"}}}""");

        // Act
        var first = Assert.ThrowsAsync<UpstreamThrottledException>(() => _sessions.SendAsync(
            SessionKey + "#account-a", BaseUri, HttpMethod.Get, new Uri(BaseUri, "search"),
            null, "test", false, default));
        var second = Assert.ThrowsAsync<UpstreamThrottledException>(() => _sessions.SendAsync(
            SessionKey + "#account-a", BaseUri, HttpMethod.Get, new Uri(BaseUri, "other"),
            null, "test", false, default));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first!.RequestSent, Is.True);
            Assert.That(second!.RequestSent, Is.False);
            Assert.That(_http.GetMatchCount(solverRequest), Is.EqualTo(1));
        }
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
            NullLogger<FlareSolverrClient>.Instance, _cooldownServices.GetRequiredService<IUpstreamCooldown>(),
            new SolverQueue(), TimeProvider.System);

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
            _cooldownServices.GetRequiredService<IUpstreamCooldown>(),
            new SolverQueue(), TimeProvider.System);

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
            NullLogger<FlareSolverrClient>.Instance, _cooldownServices.GetRequiredService<IUpstreamCooldown>(),
            new SolverQueue { Wait = TimeSpan.FromSeconds(1) }, TimeProvider.System);
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
            NullLogger<FlareSolverrClient>.Instance, _cooldownServices.GetRequiredService<IUpstreamCooldown>(),
            new SolverQueue { Wait = TimeSpan.FromSeconds(1) }, TimeProvider.System);
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

    [Test]
    public async Task SolveAsync_HoldsCapacityUntilTheSolverAnswers_WhenAnAdmittedCallIsCancelled()
    {
        // Arrange
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var answer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>(async (_, token) =>
            {
                entered.TrySetResult();
                await answer.Task.WaitAsync(token).ConfigureAwait(false);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SolvedResponse) };
            });
        using var http = new HttpClient(handler.Object);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(value => value.CreateClient(It.IsAny<string>())).Returns(http);
        var solver = new FlareSolverrClient(factory.Object, _settingsService, _memoFactory,
            NullLogger<FlareSolverrClient>.Instance, _cooldownServices.GetRequiredService<IUpstreamCooldown>(),
            new SolverQueue { Wait = TimeSpan.Zero }, TimeProvider.System);
        using var cancellation = new CancellationTokenSource();

        // Act
        Task<CloudflareClearance?> pending;
        using (new SolverRequestScope(SolverRequestKind.Interactive))
        {
            pending = solver.SolveAsync(BaseUri, reuse: false, cancellation.Token);
        }

        await entered.Task.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);
        var cancelled = Assert.CatchAsync<OperationCanceledException>(async () =>
            await pending.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false));
        var busy = Assert.CatchAsync(async () =>
            await solver.SolveAsync(BaseUri, reuse: false, default).WaitAsync(TimeSpan.FromSeconds(1))
                .ConfigureAwait(false));
        answer.SetResult();
        CloudflareClearance? next;
        using (new SolverRequestScope(SolverRequestKind.Interactive))
        {
            next = await solver.SolveAsync(BaseUri, reuse: false, default).WaitAsync(TimeSpan.FromSeconds(1))
                .ConfigureAwait(false);
        }

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(cancelled, Is.Not.Null);
            Assert.That(busy, Is.InstanceOf<SolverBusyException>());
            Assert.That(next, Is.Not.Null);
            handler.Protected().Verify("SendAsync", Times.Exactly(2),
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
        }
    }

    [Test]
    public async Task SendAsync_SolvesTheSiteRootOnce_WhenTheSolutionCarriesNoCloudflareCookie()
    {
        // Arrange
        var rootSolve = _http.When(HttpMethod.Post, "http://solver.test:8191/v1")
            .WithContent("""{"cmd":"request.get","url":"https://tracker.test/","maxTimeout":60000}""")
            .Respond("application/json", SolvedPageWithoutCookiesResponse);
        _http.When(HttpMethod.Post, "http://solver.test:8191/v1")
            .Respond("application/json", SolvedPageWithoutCookiesResponse);
        _http.When(HttpMethod.Get, "https://tracker.test/*").Respond(_ =>
        {
            var challenge = new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent(ChallengePage),
            };
            challenge.Headers.TryAddWithoutValidation("CF-RAY", "7a1b2c3d");
            return challenge;
        });
        var sessions = new CardigannSessionManager(_clientFactory, _resultParser.Object,
            new FlareSolverrClient(_clientFactory, _settingsService, Mock.Of<ICacheFactory>(factory =>
                    factory.GetOrCreate(It.IsAny<string>(), It.IsAny<Func<CachePolicy>>()) == Mock.Of<ICache>()),
                NullLogger<FlareSolverrClient>.Instance, _cooldownServices.GetRequiredService<IUpstreamCooldown>(),
                new SolverQueue(), TimeProvider.System),
            new CardigannSessionState(), TimeProvider.System, NullLogger<CardigannSessionManager>.Instance);

        // Act
        using var first = await sessions.SendAsync(SessionKey, BaseUri, HttpMethod.Get,
            new Uri("https://tracker.test/search?q=one"), null, "magpie-test", false, default).ConfigureAwait(false);
        using var second = await sessions.SendAsync(SessionKey, BaseUri, HttpMethod.Get,
            new Uri("https://tracker.test/search?q=two"), null, "magpie-test", false, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(_http.GetMatchCount(rootSolve), Is.EqualTo(1));
        }
    }

    [TestCase(HttpStatusCode.InternalServerError,
        """{"status":"error","message":"Error: Error solving the challenge. Timeout after 60.0 seconds."}""")]
    [TestCase(HttpStatusCode.InternalServerError,
        """{"status":"error","message":"Error: Error solving the challenge. Cloudflare has blocked """ +
        """this request. Probably your IP is banned for this site, check in your web browser."}""")]
    public async Task SolveAsync_SkipsBackgroundSolvesOfTheSite_WhenTheSolverFailedTheChallengeThere(
        HttpStatusCode status, string body)
    {
        // Arrange
        var failed = _http.When(HttpMethod.Post, "http://solver.test:8191/v1")
            .WithContent("""{"cmd":"request.get","url":"https://tracker.test/search?q=one","maxTimeout":60000}""")
            .Respond(status, "application/json", body);
        var skipped = _http.When(HttpMethod.Post, "http://solver.test:8191/v1")
            .WithContent("""{"cmd":"request.get","url":"https://tracker.test/search?q=two","maxTimeout":60000}""")
            .Respond("application/json", SolvedPageResponse);
        var other = _http.When(HttpMethod.Post, "http://solver.test:8191/v1")
            .WithContent("""{"cmd":"request.get","url":"https://other.test/search","maxTimeout":60000}""")
            .Respond("application/json", SolvedPageResponse);
        var solver = new FlareSolverrClient(_clientFactory, _settingsService, _memoFactory,
            NullLogger<FlareSolverrClient>.Instance, _cooldownServices.GetRequiredService<IUpstreamCooldown>(),
            new SolverQueue(), TimeProvider.System);

        // Act
        var first = await solver.SolveAsync(new Uri("https://tracker.test/search?q=one"), reuse: false, default)
            .ConfigureAwait(false);
        var second = await solver.SolveAsync(new Uri("https://tracker.test/search?q=two"), reuse: false, default)
            .ConfigureAwait(false);
        var unrelated = await solver.SolveAsync(new Uri("https://other.test/search"), reuse: false, default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.Null);
            Assert.That(second, Is.Null);
            Assert.That(unrelated, Is.Not.Null);
            Assert.That(_http.GetMatchCount(failed), Is.EqualTo(1));
            Assert.That(_http.GetMatchCount(skipped), Is.Zero);
            Assert.That(_http.GetMatchCount(other), Is.EqualTo(1));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SolveAsync_ReachesTheSolverDuringABackoff_WhenTheCallerIsNotBackground(bool connectionTest)
    {
        // Arrange
        _http.When(HttpMethod.Post, "http://solver.test:8191/v1")
            .WithContent("""{"cmd":"request.get","url":"https://tracker.test/search?q=one","maxTimeout":60000}""")
            .Respond(HttpStatusCode.InternalServerError, "application/json",
                """{"status":"error","message":"Error: Error solving the challenge. Timeout after 60.0 seconds."}""");
        var bypassed = _http.When(HttpMethod.Post, "http://solver.test:8191/v1")
            .WithContent("""{"cmd":"request.get","url":"https://tracker.test/search?q=two","maxTimeout":60000}""")
            .Respond("application/json", SolvedPageResponse);
        var solver = new FlareSolverrClient(_clientFactory, _settingsService, _memoFactory,
            NullLogger<FlareSolverrClient>.Instance, _cooldownServices.GetRequiredService<IUpstreamCooldown>(),
            new SolverQueue(), TimeProvider.System);
        await solver.SolveAsync(new Uri("https://tracker.test/search?q=one"), reuse: false, default)
            .ConfigureAwait(false);

        // Act
        CloudflareClearance? solved;
        using (new SolverRequestScope(connectionTest
            ? SolverRequestKind.ConnectionTest : SolverRequestKind.Interactive))
        {
            solved = await solver.SolveAsync(new Uri("https://tracker.test/search?q=two"), reuse: false, default)
                .ConfigureAwait(false);
        }

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(solved, Is.Not.Null);
            Assert.That(_http.GetMatchCount(bypassed), Is.EqualTo(1));
        }
    }

    [TestCase("""{"status":"error","message":"Error: Error starting Chrome: Message: session not created: """ +
        """cannot connect to chrome at 127.0.0.1:50123"}""")]
    [TestCase("""{"status":"error","message":"Error: Error solving the challenge. Message: session not """ +
        """created: cannot connect to chrome at 127.0.0.1:50123"}""")]
    [TestCase("""{"status":"error","message":"Error: Error solving the challenge. HTTPConnectionPool(""" +
        """host='localhost', port=50123): Read timed out. (read timeout=120)"}""")]
    [TestCase("""{"status":"error","message":"Error: Error solving the challenge. "}""")]
    [TestCase("""{"status":"error","message":"Error: Message: invalid session id"}""")]
    [TestCase("<html><body>Internal Server Error</body></html>")]
    public async Task SolveAsync_KeepsSolvingTheSite_WhenTheSolverFailedWithALocalFault(string body)
    {
        // Arrange
        var solve = _http.When(HttpMethod.Post, "http://solver.test:8191/v1")
            .Respond(HttpStatusCode.InternalServerError, "application/json", body);
        var solver = new FlareSolverrClient(_clientFactory, _settingsService, _memoFactory,
            NullLogger<FlareSolverrClient>.Instance, _cooldownServices.GetRequiredService<IUpstreamCooldown>(),
            new SolverQueue(), TimeProvider.System);

        // Act
        var first = await solver.SolveAsync(new Uri("https://tracker.test/search?q=one"), reuse: false, default)
            .ConfigureAwait(false);
        var second = await solver.SolveAsync(new Uri("https://tracker.test/search?q=two"), reuse: false, default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.Null);
            Assert.That(second, Is.Null);
            Assert.That(_http.GetMatchCount(solve), Is.EqualTo(2));
        }
    }

    [Test]
    public async Task TestConnectionAsync_Succeeds_WhenABackgroundSearchFailedOnTheBackoffAndTheSolverRecovered()
    {
        // Arrange
        var definition = new CardigannDefinition { Id = "backoff", Name = "Backoff" };
        definition.Links.Add("https://tracker.test/");
        definition.Search.Paths.Add(new SearchPath { Path = "/search" });
        var integration = new CardigannIndexerIntegration(Guid.Empty, definition, _resultParser.Object, _sessions,
            NullLogger<CardigannIndexerIntegration>.Instance);
        _http.When(HttpMethod.Get, "https://tracker.test/*").Respond(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("<html>Just a moment...</html>")
            };
            response.Headers.TryAddWithoutValidation("CF-RAY", "challenge");
            return response;
        });
        var solves = 0;
        _http.When(HttpMethod.Post, "http://solver.test:8191/v1").Respond(_ => ++solves == 1
            ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent(
                    """{"status":"error","message":"Error: Error solving the challenge. Timeout after 60.0 s."}""")
            }
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SolvedPageWithoutCookiesResponse)
            });

        // Act
        var failure = Assert.CatchAsync(async () =>
            await integration.SearchAsync(new ReleaseSearchRequest { Title = "query" },
                new Dictionary<string, string>(), default).ConfigureAwait(false));
        var result = await integration.TestConnectionAsync(new Dictionary<string, string>()).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure, Is.InstanceOf<HttpRequestException>());
            Assert.That(result.Success, Is.True);
            Assert.That(solves, Is.EqualTo(3));
        }
    }

    [Test]
    public async Task SearchAsync_DropsOnlyTheChallengedRow_WhenADetailFetchMeetsTheBackoff()
    {
        // Arrange
        var definition = new CardigannDefinition { Id = "details", Name = "Details" };
        definition.Links.Add("https://tracker.test/");
        definition.Search.Paths.Add(new SearchPath { Path = "/search" });
        definition.Download = new DownloadBlock { Selector = "a.magnet", Attribute = "href" };
        var integration = new CardigannIndexerIntegration(Guid.Empty, definition, _resultParser.Object, _sessions,
            NullLogger<CardigannIndexerIntegration>.Instance);
        _http.When(HttpMethod.Get, "https://tracker.test/search").Respond("text/html", "<html>rows</html>");
        _http.When(HttpMethod.Get, "https://tracker.test/details/b").Respond("text/html", "<html>b</html>");
        _http.When(HttpMethod.Get, "https://tracker.test/details/a").Respond(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("<html>Just a moment...</html>")
            };
            response.Headers.TryAddWithoutValidation("CF-RAY", "challenge");
            return response;
        });
        var solve = _http.When(HttpMethod.Post, "http://solver.test:8191/v1")
            .Respond(HttpStatusCode.InternalServerError, "application/json",
                """{"status":"error","message":"Error: Error solving the challenge. Timeout after 60.0 seconds."}""");
        _resultParser.Setup(value => value.Parse(It.IsAny<string>(), It.IsAny<CardigannDefinition>(),
                It.IsAny<Uri>(), It.IsAny<IReadOnlyDictionary<string, object?>>()))
            .Returns(
            [
                new Release
                {
                    Title = "Row A", Protocol = "torrent", InfoUrl = new Uri("https://tracker.test/details/a")
                },
                new Release
                {
                    Title = "Row B", Protocol = "torrent", InfoUrl = new Uri("https://tracker.test/details/b")
                },
            ]);
        _resultParser.Setup(value => value.ExtractDownloadTarget(It.IsAny<string>(), It.IsAny<DownloadBlock>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>()))
            .Returns("magnet:?xt=urn:btih:b");

        // Act
        var releases = await integration.SearchAsync(new ReleaseSearchRequest { Title = "query" },
            new Dictionary<string, string>(), default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(releases.Select(release => release.Title), Is.EqualTo(["Row B"]));
            Assert.That(_http.GetMatchCount(solve), Is.EqualTo(1));
        }
    }

    [Test]
    public async Task TestConnectionAsync_ReturnsFatal_WhenTheTrackerCannotBeReached()
    {
        // Arrange
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("tracker offline"));
        using var http = new HttpClient(handler.Object);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(value => value.CreateClient(It.IsAny<string>())).Returns(http);
        var manager = new CardigannSessionManager(factory.Object, _resultParser.Object,
            Mock.Of<ICloudflareSolver>(), new CardigannSessionState(), TimeProvider.System,
            NullLogger<CardigannSessionManager>.Instance);
        var definition = new CardigannDefinition { Id = "offline", Name = "Offline" };
        definition.Links.Add("https://tracker.test/");
        var integration = new CardigannIndexerIntegration(Guid.Empty, definition, _resultParser.Object, manager,
            NullLogger<CardigannIndexerIntegration>.Instance);

        // Act
        var result = await integration.TestConnectionAsync(new Dictionary<string, string>())
            .WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.FailureKind, Is.EqualTo(ConfigurationFailureKind.Fatal));
            Assert.That(result.Message, Is.EqualTo("Network error: tracker offline"));
        }
    }

    [Test]
    public async Task TestConnectionAsync_ReturnsFatal_WhenTheSolverRejectsTheChallenge()
    {
        // Arrange
        var definition = new CardigannDefinition { Id = "rejected", Name = "Rejected" };
        definition.Links.Add("https://tracker.test/");
        var integration = new CardigannIndexerIntegration(Guid.Empty, definition, _resultParser.Object, _sessions,
            NullLogger<CardigannIndexerIntegration>.Instance);
        _http.When(HttpMethod.Get, "https://tracker.test/").Respond(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("<html>Just a moment...</html>")
            };
            response.Headers.TryAddWithoutValidation("CF-RAY", "challenge");
            return response;
        });
        _http.When(HttpMethod.Post, "http://solver.test:8191/v1").Respond(HttpStatusCode.ServiceUnavailable);

        // Act
        var result = await integration.TestConnectionAsync(new Dictionary<string, string>()).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.FailureKind, Is.EqualTo(ConfigurationFailureKind.Fatal));
            Assert.That(result.Message, Is.EqualTo(
                "Cloudflare protection detected at https://tracker.test/: HTTP 403 with CF headers"));
        }
    }

    [Test]
    public void RegisterServices_PreservesSolverLimits_WhenContentionAdmissionIsEnabled()
    {
        // Arrange
        var services = new ServiceCollection();
        new CardigannExtension().RegisterServices(services);
        using var provider = services.BuildServiceProvider();

        // Act
        using var client = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient("magpie.cardigann.flaresolverr");
        var queue = provider.GetRequiredService<SolverQueue>();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(client.Timeout.TotalSeconds, Is.EqualTo(120));
            Assert.That(FlareSolverrClient.ReuseWindow.TotalSeconds, Is.EqualTo(60));
            Assert.That(queue.Wait.TotalSeconds, Is.EqualTo(20));
        }
    }

    [Test]
    public async Task EnsureLoggedInAsync_AdmitsInteractiveFirst_WhenBackgroundWaitsOnTheSameTracker()
    {
        // Arrange
        var state = new CardigannSessionState();
        var gate = new SolverQueue { WaitForBackground = true };
        await gate.EnterAsync(default).ConfigureAwait(false);
        state.LoginGates[SessionKey] = gate;
        var manager = new CardigannSessionManager(_clientFactory, _resultParser.Object,
            Mock.Of<ICloudflareSolver>(), state, TimeProvider.System, NullLogger<CardigannSessionManager>.Instance);
        var sentAgents = new List<string>();
        _http.When(HttpMethod.Get, "https://tracker.test/login").Respond(request =>
        {
            sentAgents.Add(request.Headers.UserAgent.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(LogoutPage) };
        });
        var login = new LoginBlock { Method = "get", Path = "/login" };

        // Act
        var background = manager.EnsureLoggedInAsync(SessionKey, login, BaseUri, Variables, null,
            "background", false, default);
        Task<CardigannLoginOutcome> interactive;
        using (new SolverRequestScope(SolverRequestKind.Interactive))
        {
            interactive = manager.EnsureLoggedInAsync(SessionKey, login, BaseUri, Variables, null,
                "interactive", false, default);
        }

        gate.Leave();
        var results = await Task.WhenAll(background, interactive).WaitAsync(TimeSpan.FromSeconds(1))
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sentAgents, Is.EqualTo(["interactive"]));
            Assert.That(results.Select(result => result.Success), Is.EqualTo([true, true]));
        }
    }

    [Test]
    public async Task SendAsync_AdmitsInteractiveClearanceFirst_WhenBackgroundWaitsOnTheSameTracker()
    {
        // Arrange
        var state = new CardigannSessionState();
        var tracker = new TrackerSession();
        state.Sessions[SessionKey] = tracker;
        await tracker.ClearanceGate.EnterAsync(default).ConfigureAwait(false);
        var solver = new Mock<ICloudflareSolver>();
        var callers = new List<SolverRequestKind>();
        solver.Setup(value => value.SolveAsync(It.IsAny<Uri>(), true, It.IsAny<CancellationToken>(), null))
            .Callback(() => callers.Add(SolverRequestScope.Current))
            .ReturnsAsync(new CloudflareClearance
            {
                Cookies = new Dictionary<string, string> { ["cf_clearance"] = "TOKEN" }
            });
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((request, _) =>
            {
                if (request.Headers.Contains("Cookie"))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("healthy")
                    });
                }

                var response = new HttpResponseMessage(HttpStatusCode.Forbidden)
                {
                    Content = new StringContent("<html>Just a moment...</html>")
                };
                response.Headers.TryAddWithoutValidation("CF-RAY", "challenge");
                return Task.FromResult(response);
            });
        using var http = new HttpClient(handler.Object);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(value => value.CreateClient(It.IsAny<string>())).Returns(http);
        var manager = new CardigannSessionManager(factory.Object, _resultParser.Object, solver.Object,
            state, TimeProvider.System, NullLogger<CardigannSessionManager>.Instance);

        // Act
        var background = manager.SendAsync(SessionKey, BaseUri, HttpMethod.Get,
            new Uri("https://tracker.test/search"), null, "background", false, default);
        Task<HttpResponseMessage> interactive;
        using (new SolverRequestScope(SolverRequestKind.Interactive))
        {
            interactive = manager.SendAsync(SessionKey, BaseUri, HttpMethod.Get,
                new Uri("https://tracker.test/search"), null, "interactive", false, default);
        }

        tracker.ClearanceGate.Leave();
        using var first = await interactive.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        using var second = await background.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(callers, Is.EqualTo([SolverRequestKind.Interactive]));
            Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }
    }
}
