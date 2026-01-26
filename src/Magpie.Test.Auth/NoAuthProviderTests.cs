using System.Security.Claims;

using Magpie.Abstractions.Auth;
using Magpie.Auth.Providers;
using Magpie.Models.Result;

using Microsoft.AspNetCore.Http;

using NUnit.Framework;

namespace Magpie.Test.Auth;

[TestFixture]
public class NoAuthProviderTests
{
    private NoAuthProvider _subject = null!;
    private DefaultHttpContext _httpContext = null!;

    [SetUp]
    public void SetUp()
    {
        _subject = new NoAuthProvider();
        _httpContext = new DefaultHttpContext();
    }

    [Test]
    public void ProviderType_ReturnsNone()
    {
        Assert.That(_subject.ProviderType, Is.EqualTo("none"));
    }

    [Test]
    public async System.Threading.Tasks.Task TryAuthenticateAsync_AlwaysReturnsSuccess()
    {
        var result = await _subject.TryAuthenticateAsync(_httpContext);

        Assert.That(result, Is.InstanceOf<SuccessResult<ClaimsPrincipal?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Not.Null);
    }

    [Test]
    public async System.Threading.Tasks.Task TryAuthenticateAsync_ReturnsCorrectClaims()
    {
        var result = await _subject.TryAuthenticateAsync(_httpContext);
        var principal = ((SuccessResult<ClaimsPrincipal?>)result).Value!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                principal.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                Is.EqualTo(NoAuthProvider.AdminUserId.ToString()));
            Assert.That(
                principal.FindFirst(ClaimTypes.Name)?.Value,
                Is.EqualTo("admin"));
            Assert.That(
                principal.FindFirst(ClaimTypes.Role)?.Value,
                Is.EqualTo("Admin"));
            Assert.That(
                principal.FindFirst(MagpieClaims.AuthProviderId)?.Value,
                Is.EqualTo(NoAuthProvider.ProviderId.ToString()));
            Assert.That(
                principal.FindFirst(ClaimTypes.AuthenticationMethod)?.Value,
                Is.EqualTo("none"));
        }
    }

    [Test]
    public void Challenge_DoesNothing()
    {
        _subject.Challenge(_httpContext);

        Assert.That(_httpContext.Response.Headers, Is.Empty);
    }
}
