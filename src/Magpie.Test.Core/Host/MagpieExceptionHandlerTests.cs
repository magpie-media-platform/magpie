using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.App;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Host;

[TestFixture]
public sealed class MagpieExceptionHandlerTests
{
    private ProblemDetailsContext? _written;
    private MagpieExceptionHandler _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _written = null;
        var problemDetailsService = new Mock<IProblemDetailsService>();
        problemDetailsService
            .Setup(service => service.TryWriteAsync(It.IsAny<ProblemDetailsContext>()))
            .Callback<ProblemDetailsContext>(context => _written = context)
            .ReturnsAsync(true);
        _subject = new MagpieExceptionHandler(
            problemDetailsService.Object,
            NullLogger<MagpieExceptionHandler>.Instance);
    }

    [Test]
    public async Task TryHandleAsync_ReturnsBadRequest_WhenAnArgumentIsInvalid()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();

        // Act
        await _subject.TryHandleAsync(
            httpContext,
            new ArgumentException("bad"),
            CancellationToken.None).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(httpContext.Response.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
            Assert.That(_written?.ProblemDetails.Status, Is.EqualTo(StatusCodes.Status400BadRequest));
            Assert.That(_written?.ProblemDetails.Detail, Is.EqualTo("bad"));
        }
    }
}
