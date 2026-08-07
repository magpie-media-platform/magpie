using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

using AngleSharp.Dom;

using Magpie.Extensions.Cardigann.Engine;
using Magpie.Extensions.Cardigann.Engine.Session;
using Magpie.Models;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

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

    private static readonly Uri BaseUri = new("https://tracker.test/");

    private static readonly Dictionary<string, object?> Variables = new()
    {
        [".Config.username"] = "myuser",
        [".Config.password"] = "secret",
    };

    private ICardigannSessionManager _sessions = null!;
    private MockHttpMessageHandler _http = null!;
    private Mock<IResultParser> _resultParser = null!;
    private LoginBlock _login = null!;
    private LoginBlock _cookieLogin = null!;

    [SetUp]
    public void SetUp()
    {
        _http = new MockHttpMessageHandler();
        var clientFactory = new Mock<IHttpClientFactory>();
        clientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => _http.ToHttpClient());

        _resultParser = new Mock<IResultParser>();
        _sessions = new CardigannSessionManager(
            clientFactory.Object,
            _resultParser.Object,
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
            "magpie-test",
            captureCookies: true,
            default)
            .ConfigureAwait(false);

        // Assert
        Assert.That(searchCookie, Is.EqualTo("sid=PASTED"));
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
            "magpie-test",
            captureCookies: true,
            default)
            .ConfigureAwait(false);

        // Assert
        Assert.That(searchCookie, Is.Empty);
    }

    [TestCase(HttpStatusCode.Forbidden, "<html><body>denied</body></html>", true)]
    [TestCase(HttpStatusCode.OK, """<html><body><input type="password"/></body></html>""", true)]
    [TestCase(HttpStatusCode.OK, """<html><body><a class="logout">Logout</a></body></html>""", false)]
    public void IsLoggedOut_ReportsWhetherTheSessionEnded_WhenAResponseIsInspected(
        HttpStatusCode statusCode,
        string body,
        bool expected)
    {
        // Act
        var result = _sessions.IsLoggedOut(_login, statusCode, body);

        // Assert
        Assert.That(result, Is.EqualTo(expected));
    }
}
