using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Auth;
using Magpie.Auth.Providers;
using Magpie.Models.Result;

using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Auth;

[TestFixture]
public class BearerAuthProviderTests
{
    private Mock<ITokenService> _tokenService = null!;
    private BearerAuthProvider _subject = null!;
    private DefaultHttpContext _httpContext = null!;

    [SetUp]
    public void SetUp()
    {
        _tokenService = new Mock<ITokenService>();
        _subject = new BearerAuthProvider(_tokenService.Object);
        _httpContext = new DefaultHttpContext();
    }

    [Test]
    public void ProviderType_ReturnsBearer()
    {
        // Assert
        Assert.That(_subject.ProviderType, Is.EqualTo("bearer"));
    }

    [Test]
    public async Task TryAuthenticateAsync_NoAuthHeader_ReturnsNotApplicable()
    {
        // Act
        var result = await _subject.TryAuthenticateAsync(_httpContext);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<ClaimsPrincipal?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task TryAuthenticateAsync_NonBearerScheme_ReturnsNotApplicable()
    {
        // Arrange
        _httpContext.Request.Headers[HeaderNames.Authorization] = "Basic dXNlcjpwYXNz";

        // Act
        var result = await _subject.TryAuthenticateAsync(_httpContext);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<ClaimsPrincipal?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task TryAuthenticateAsync_EmptyToken_ReturnsFailure()
    {
        // Arrange
        _httpContext.Request.Headers[HeaderNames.Authorization] = "Bearer ";

        // Act
        var result = await _subject.TryAuthenticateAsync(_httpContext);

        // Assert
        Assert.That(result, Is.InstanceOf<FailureResult<ClaimsPrincipal?>>()
            .With.Property(nameof(FailureResult<>.Reason)).EqualTo("Bearer token is empty"));
    }

    [Test]
    public async Task TryAuthenticateAsync_InvalidToken_ReturnsFailure()
    {
        // Arrange
        _httpContext.Request.Headers[HeaderNames.Authorization] = "Bearer invalid.token.here";
        _tokenService.Setup(s => s.ValidateAccessTokenAsync("invalid.token.here", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Fail<ClaimsPrincipal>("Invalid access token"));

        // Act
        var result = await _subject.TryAuthenticateAsync(_httpContext);

        // Assert
        Assert.That(result, Is.InstanceOf<FailureResult<ClaimsPrincipal?>>()
            .With.Property(nameof(FailureResult<>.Reason)).EqualTo("Invalid access token"));
    }

    [Test]
    public async Task TryAuthenticateAsync_ExpiredToken_ReturnsFailure()
    {
        // Arrange
        _httpContext.Request.Headers[HeaderNames.Authorization] = "Bearer expired.token.here";
        _tokenService.Setup(s => s.ValidateAccessTokenAsync("expired.token.here", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Fail<ClaimsPrincipal>("Access token has expired"));

        // Act
        var result = await _subject.TryAuthenticateAsync(_httpContext);

        // Assert
        Assert.That(result, Is.InstanceOf<FailureResult<ClaimsPrincipal?>>()
            .With.Property(nameof(FailureResult<>.Reason)).EqualTo("Access token has expired"));
    }

    [Test]
    public async Task TryAuthenticateAsync_ValidToken_ReturnsSuccess()
    {
        // Arrange
        _httpContext.Request.Headers[HeaderNames.Authorization] = "Bearer valid.token.here";
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "11111111-1111-1111-1111-111111111111"),
            new Claim(ClaimTypes.Name, "testuser"),
            new Claim(ClaimTypes.Role, "Viewer"),
            new Claim(ClaimTypes.AuthenticationMethod, "jwt")
        ], "jwt"));
        _tokenService.Setup(s => s.ValidateAccessTokenAsync("valid.token.here", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(principal));

        // Act
        var result = await _subject.TryAuthenticateAsync(_httpContext);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<ClaimsPrincipal?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Not.Null);
    }

    [Test]
    public async Task TryAuthenticateAsync_ValidToken_ReturnsPrincipalFromTokenService()
    {
        // Arrange
        _httpContext.Request.Headers[HeaderNames.Authorization] = "Bearer valid.token.here";
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "22222222-2222-2222-2222-222222222222"),
            new Claim(ClaimTypes.Name, "anotheruser"),
            new Claim(ClaimTypes.Role, "Admin"),
            new Claim(ClaimTypes.AuthenticationMethod, "jwt")
        ], "jwt"));
        _tokenService.Setup(s => s.ValidateAccessTokenAsync("valid.token.here", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(principal));

        // Act
        var result = await _subject.TryAuthenticateAsync(_httpContext);
        var returnedPrincipal = ((SuccessResult<ClaimsPrincipal?>)result).Value!;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                returnedPrincipal.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                Is.EqualTo("22222222-2222-2222-2222-222222222222"));
            Assert.That(
                returnedPrincipal.FindFirst(ClaimTypes.Name)?.Value,
                Is.EqualTo("anotheruser"));
            Assert.That(
                returnedPrincipal.FindFirst(ClaimTypes.Role)?.Value,
                Is.EqualTo("Admin"));
            Assert.That(
                returnedPrincipal.FindFirst(ClaimTypes.AuthenticationMethod)?.Value,
                Is.EqualTo("jwt"));
        }
    }

    [Test]
    public async Task TryAuthenticateAsync_CaseInsensitiveScheme_AcceptsLowercaseBearer()
    {
        // Arrange
        _httpContext.Request.Headers[HeaderNames.Authorization] = "bearer valid.token.here";
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "11111111-1111-1111-1111-111111111111"),
            new Claim(ClaimTypes.Name, "testuser"),
            new Claim(ClaimTypes.Role, "Viewer"),
            new Claim(ClaimTypes.AuthenticationMethod, "jwt")
        ], "jwt"));
        _tokenService.Setup(s => s.ValidateAccessTokenAsync("valid.token.here", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(principal));

        // Act
        var result = await _subject.TryAuthenticateAsync(_httpContext);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<ClaimsPrincipal?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Not.Null);
    }

    [Test]
    public async Task TryAuthenticateAsync_TokenWithWhitespace_TrimsToken()
    {
        // Arrange
        _httpContext.Request.Headers[HeaderNames.Authorization] = "Bearer   valid.token.here   ";
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "11111111-1111-1111-1111-111111111111"),
            new Claim(ClaimTypes.Name, "testuser"),
            new Claim(ClaimTypes.Role, "Viewer"),
            new Claim(ClaimTypes.AuthenticationMethod, "jwt")
        ], "jwt"));
        _tokenService.Setup(s => s.ValidateAccessTokenAsync("valid.token.here", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(principal));

        // Act
        var result = await _subject.TryAuthenticateAsync(_httpContext);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<ClaimsPrincipal?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Not.Null);
    }

    [Test]
    public void Challenge_AddsWWWAuthenticateHeader()
    {
        // Act
        _subject.Challenge(_httpContext);

        // Assert
        Assert.That(_httpContext.Response.Headers[HeaderNames.WWWAuthenticate].ToString(), Is.EqualTo("Bearer"));
    }
}
