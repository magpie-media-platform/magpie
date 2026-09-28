using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Auth;
using Magpie.Auth;
using Magpie.Sdk.Api;
using Magpie.Sdk.Auth;

using Microsoft.AspNetCore.Http;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Auth;

[TestFixture]
public sealed class JwtBearerAuthenticationProviderTests
{
    private const string StreamPath = "/api/events/stream";
    private const string ValidToken = "valid.jwt.token";

    private Mock<ITokenService> _tokenService = null!;
    private IAuthenticationProvider _provider = null!;
    private ClaimsPrincipal _principal = null!;

    [SetUp]
    public void SetUp()
    {
        _principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "user-1")],
            "Test"));

        _tokenService = new Mock<ITokenService>();
        _tokenService
            .Setup(m => m.TryValidateAccessTokenAsync(ValidToken, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_principal);

        _provider = new JwtBearerAuthenticationProvider(_tokenService.Object);
    }

    [Test]
    public async Task TryAuthenticate_ReturnsAuthenticated_WhenValidCookieOnStreamPath()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = StreamPath;
        context.Request.Headers.Cookie = $"{StreamAuthentication.CookieName}={ValidToken}";

        // Act
        var result = await _provider.TryAuthenticateAsync(context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(AuthenticationAttemptStatus.Authenticated));
            Assert.That(result.Principal, Is.SameAs(_principal));
        }
    }

    [Test]
    public async Task TryAuthenticate_ReturnsNoCredentials_WhenCookieMissingOnStreamPath()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = StreamPath;

        // Act
        var result = await _provider.TryAuthenticateAsync(context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(AuthenticationAttemptStatus.NoCredentials));
            Assert.That(result.Principal, Is.Null);
            Assert.That(result.RejectionReason, Is.Null);
        }
    }

    [Test]
    public async Task TryAuthenticate_ReturnsRejected_WhenCookieTokenInvalid()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = StreamPath;
        context.Request.Headers.Cookie = $"{StreamAuthentication.CookieName}=tampered.or.expired";

        // Act
        var result = await _provider.TryAuthenticateAsync(context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(AuthenticationAttemptStatus.Rejected));
            Assert.That(result.Principal, Is.Null);
            Assert.That(result.RejectionReason, Is.EqualTo("the access token is invalid or expired"));
        }
    }

    [Test]
    public async Task TryAuthenticate_ReturnsNoCredentials_WhenNotOnStreamPath()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/content";
        context.Request.Headers.Cookie = $"{StreamAuthentication.CookieName}={ValidToken}";

        // Act
        var result = await _provider.TryAuthenticateAsync(context).ConfigureAwait(false);

        // Assert
        Assert.That(result.Status, Is.EqualTo(AuthenticationAttemptStatus.NoCredentials));
    }

    [Test]
    public async Task TryAuthenticate_ReturnsNoCredentials_WhenTokenSuppliedAsAccessTokenQueryParam()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = StreamPath;
        context.Request.QueryString = new QueryString($"?access_token={ValidToken}");

        // Act
        var result = await _provider.TryAuthenticateAsync(context).ConfigureAwait(false);

        // Assert
        Assert.That(result.Status, Is.EqualTo(AuthenticationAttemptStatus.NoCredentials));
    }

    [Test]
    public async Task TryAuthenticate_ReturnsNoCredentials_WhenAuthorizationHeaderUsesAnotherScheme()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/content";
        context.Request.Headers.Authorization = "Basic dXNlcjpwYXNz";

        // Act
        var result = await _provider.TryAuthenticateAsync(context).ConfigureAwait(false);

        // Assert
        Assert.That(result.Status, Is.EqualTo(AuthenticationAttemptStatus.NoCredentials));
    }

    [Test]
    public async Task TryAuthenticate_ReturnsAuthenticated_WhenValidBearerHeader()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/content";
        context.Request.Headers.Authorization = $"Bearer {ValidToken}";

        // Act
        var result = await _provider.TryAuthenticateAsync(context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(AuthenticationAttemptStatus.Authenticated));
            Assert.That(result.Principal, Is.SameAs(_principal));
        }
    }

    [Test]
    public async Task TryAuthenticate_ReturnsRejected_WhenBearerHeaderTokenInvalid()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/content";
        context.Request.Headers.Authorization = "Bearer tampered.or.expired";

        // Act
        var result = await _provider.TryAuthenticateAsync(context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(AuthenticationAttemptStatus.Rejected));
            Assert.That(result.Principal, Is.Null);
            Assert.That(result.RejectionReason, Is.EqualTo("the access token is invalid or expired"));
        }
    }

    [Test]
    public async Task TryAuthenticate_UsesItsPortCookie_WhenAnotherInstanceCookieIsPresent()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("localhost", 5000);
        context.Request.Path = StreamPath;
        context.Request.Headers.Cookie = "magpie.stream.5000=valid.jwt.token; magpie.stream.5001=other-token";

        // Act
        var result = await _provider.TryAuthenticateAsync(context).ConfigureAwait(false);

        // Assert
        Assert.That(result.Status, Is.EqualTo(AuthenticationAttemptStatus.Authenticated));
        _tokenService.Verify(service => service.TryValidateAccessTokenAsync(
            "other-token", It.IsAny<CancellationToken>()), Times.Never);
    }
}
