using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.App;
using Magpie.Models;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Host;

[TestFixture]
public sealed class MagpieExceptionHandlerTests
{
    private FakeTimeProvider _timeProvider = null!;
    private ProblemDetailsContext? _written;
    private MagpieExceptionHandler _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero));
        _written = null;
        var problemDetailsService = new Mock<IProblemDetailsService>();
        problemDetailsService
            .Setup(service => service.TryWriteAsync(It.IsAny<ProblemDetailsContext>()))
            .Callback<ProblemDetailsContext>(context => _written = context)
            .ReturnsAsync(true);
        _subject = new MagpieExceptionHandler(
            problemDetailsService.Object,
            _timeProvider,
            NullLogger<MagpieExceptionHandler>.Instance);
    }

    [TestCase(30.2, "31")]
    [TestCase(-5, "0")]
    public async Task TryHandleAsync_ReturnsServiceUnavailableWithRetryAfter_WhenTheUpstreamIsThrottled(
        double secondsUntilRetry,
        string expectedRetryAfter)
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var exception = new UpstreamThrottledException(
            _timeProvider.GetUtcNow().AddSeconds(secondsUntilRetry));

        // Act
        var handled = await _subject.TryHandleAsync(
            httpContext,
            exception,
            CancellationToken.None).ConfigureAwait(false);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(handled, Is.True);
            Assert.That(httpContext.Response.StatusCode, Is.EqualTo(StatusCodes.Status503ServiceUnavailable));
            Assert.That(httpContext.Response.Headers.RetryAfter.ToString(), Is.EqualTo(expectedRetryAfter));
            Assert.That(_written?.ProblemDetails.Status, Is.EqualTo(StatusCodes.Status503ServiceUnavailable));
            Assert.That(_written?.ProblemDetails.Detail, Is.EqualTo("The provider is busy. Try again later."));
        });
    }

    [Test]
    public async Task TryHandleAsync_LeavesRetryAfterUnset_WhenTheFailureIsNotAThrottle()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();

        // Act
        await _subject.TryHandleAsync(
            httpContext,
            new ArgumentException("bad"),
            CancellationToken.None).ConfigureAwait(false);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(httpContext.Response.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
            Assert.That(httpContext.Response.Headers.ContainsKey("Retry-After"), Is.False);
        });
    }
}
